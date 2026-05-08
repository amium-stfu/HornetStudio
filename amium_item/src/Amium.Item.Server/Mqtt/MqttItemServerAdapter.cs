using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Amium.Item.Server;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using MQTTnet;
using MQTTnet.Server;

namespace Amium.Item.Server.Mqtt;

/// <summary>
/// Bridges item broker messages to an embedded MQTT server.
/// </summary>
public sealed class MqttItemServerAdapter : IItemServerTransport, IItemServerClient, IMqttMessagePublisher, IAsyncDisposable
{
    private static readonly TimeSpan InjectedTopicIgnoreWindow = TimeSpan.FromSeconds(2);
    private const string SystemTopicPrefix = "$SYS/";
    private const string MetaParameterName = "meta";
    private const string ValueParameterName = "read";
    private const int MaxRejectedWriteDiagnostics = 80;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly MqttItemServerOptions _options;
    private readonly MqttItemTopicMapper _topicMapper;
    private readonly IMqttMessagePublisher _publisher;
    private readonly ConcurrentDictionary<string, WritableItemState> _writableItems = new(PathComparer);
    private readonly ConcurrentDictionary<string, int> _mqttOriginatedPublishes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _injectedServerMirrorTopics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _connectedExternalClientIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _visibleTopicKeysByPath = new(PathComparer);
    private MqttServer? _server;
    private IItemServer? _broker;
    private IItemSubscription? _subscription;
    private IItemSubscription? _systemSubscription;
    private int _rejectedWriteDiagnosticCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemServerAdapter"/> class.
    /// </summary>
    /// <param name="options">The MQTT adapter options.</param>
    /// <param name="publisher">An optional publisher used for tests or alternate MQTT output.</param>
    public MqttItemServerAdapter(MqttItemServerOptions options, IMqttMessagePublisher? publisher = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _topicMapper = new MqttItemTopicMapper(options.BaseTopic);
        _publisher = publisher ?? this;
    }

    /// <inheritdoc />
    public string ClientId => _options.ClientId;

    /// <summary>
    /// Gets the MQTT topic mapper used by the adapter.
    /// </summary>
    public MqttItemTopicMapper TopicMapper => _topicMapper;

    /// <summary>
    /// Gets the number of currently connected external MQTT clients.
    /// </summary>
    public int ConnectedExternalClientCount => _connectedExternalClientIds.Count;

    /// <summary>
    /// Gets the number of currently visible non-system MQTT item paths.
    /// </summary>
    public int VisibleItemCount => _visibleTopicKeysByPath.Count;

    /// <inheritdoc />
    public async Task StartAsync(IItemServer broker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;

        if (!_options.Enabled)
        {
            return;
        }

        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Parse(_options.Host))
            .WithDefaultEndpointPort(_options.Port)
            .Build();

        _server = new MqttServerFactory().CreateMqttServer(serverOptions);
        _server.InterceptingPublishAsync += HandleMqttPublishAsync;
        _server.ClientConnectedAsync += HandleClientConnectedAsync;
        _server.ClientDisconnectedAsync += HandleClientDisconnectedAsync;
        await _server.StartAsync().ConfigureAwait(false);

        _subscription = await broker.SubscribeAsync(
            client: this,
            path: _options.SubscriptionRootPath,
            options: new ItemSubscriptionOptions
            {
                Recursive = true,
                IncludeRetained = true,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!IsSystemDataCoveredByMainSubscription())
        {
            _systemSubscription = await broker.SubscribeAsync(
                client: this,
                path: ItemServerHealthPaths.Root,
                options: new ItemSubscriptionOptions
                {
                    Recursive = true,
                    IncludeRetained = true,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
            _subscription = null;
        }

        if (_systemSubscription is not null)
        {
            await _systemSubscription.DisposeAsync().ConfigureAwait(false);
            _systemSubscription = null;
        }

        if (_server is not null)
        {
            _server.InterceptingPublishAsync -= HandleMqttPublishAsync;
            _server.ClientConnectedAsync -= HandleClientConnectedAsync;
            _server.ClientDisconnectedAsync -= HandleClientDisconnectedAsync;
            await _server.StopAsync().ConfigureAwait(false);
            _server.Dispose();
            _server = null;
        }

        _connectedExternalClientIds.Clear();
        _visibleTopicKeysByPath.Clear();
    }

    /// <inheritdoc />
    public async Task ReceiveAsync(ItemServerMessage message, CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case ItemSnapshotMessage snapshot:
                TrackWritableState(snapshot.Path, snapshot.ItemModel);
                await PublishSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case ItemValueChangedMessage valueChanged:
                await PublishParameterTopicAsync(valueChanged.Path, ValueParameterName, valueChanged.Value, valueChanged.SourceClientId, cancellationToken).ConfigureAwait(false);

                break;
            case ItemPropertyChangedMessage parameterChanged:
                TrackWritableState(parameterChanged.Path, parameterChanged.PropertyName, parameterChanged.Value);
                if (!IsRedundantPublishedProperty(parameterChanged.PropertyName))
                {
                    var value = string.Equals(ItemPath.ToSnakeCaseSegment(parameterChanged.PropertyName), MetaParameterName, StringComparison.Ordinal)
                        ? ResolveMetaPayload(parameterChanged.Value)
                        : parameterChanged.Value;
                    await PublishParameterTopicAsync(parameterChanged.Path, parameterChanged.PropertyName, value, parameterChanged.SourceClientId, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ItemRemoveMessage remove:
                await _publisher.PublishAsync(_topicMapper.ToTopic(remove.Path, MetaParameterName, remove.SourceClientId), string.Empty, retain: true, cancellationToken).ConfigureAwait(false);
                await _publisher.PublishAsync(_topicMapper.ToTopic(remove.Path, ValueParameterName, remove.SourceClientId), string.Empty, retain: true, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken = default)
    {
        if (_server is null)
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .Build();

        TrackInjectedTopic(topic);
        await _server.InjectApplicationMessage(new InjectedMqttApplicationMessage(message)
        {
            SenderClientId = ClientId,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles an incoming MQTT publish and maps it back to the broker.
    /// </summary>
    /// <param name="topic">The incoming MQTT topic.</param>
    /// <param name="payload">The incoming MQTT textual payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleIncomingPublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
        => await HandleIncomingPublishAsync(topic, payload, publishingClientId: null, retain: false, cancellationToken).ConfigureAwait(false);

    private async Task HandleIncomingPublishAsync(
        string topic,
        string payload,
        string? publishingClientId,
        bool retain,
        CancellationToken cancellationToken)
    {
        if (_broker is null || !_topicMapper.TryMapTopic(topic, payload, out var mapping))
        {
            return;
        }

        var value = ParsePayload(payload);
        var writeSourceClientId = string.IsNullOrWhiteSpace(publishingClientId)
            ? (mapping.ClientId ?? ClientId)
            : publishingClientId.Trim();
        var observedSourceClientId = mapping.ClientId ?? ClientId;
        UpdateVisibleItemTopic(mapping.Path, mapping.PropertyName, value);
        TrackWritableState(mapping.Path, mapping.PropertyName, value);
        if (IsWritable(mapping.Path, out var state))
        {
            var originKey = CreateOriginKey(mapping.Path, mapping.PropertyName, writeSourceClientId);
            _mqttOriginatedPublishes.AddOrUpdate(originKey, addValue: 1, (_, count) => count + 1);
            try
            {
                var targetPath = string.IsNullOrWhiteSpace(state.WritePath) ? mapping.Path : state.WritePath;
                await PublishIncomingMappedMessageAsync(mapping, targetPath, value, writeSourceClientId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _mqttOriginatedPublishes.AddOrUpdate(originKey, addValue: 0, (_, count) => Math.Max(0, count - 1));
                if (_mqttOriginatedPublishes.TryGetValue(originKey, out var remaining) && remaining == 0)
                {
                    _mqttOriginatedPublishes.TryRemove(originKey, out _);
                }
            }

            return;
        }

        if (!_options.AllowObservedInboundPublishes)
        {
            ReportRejectedWriteDiagnostic(mapping);
            return;
        }

        await ImportIncomingObservedMessageAsync(mapping, value, observedSourceClientId, retain, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private Task HandleClientConnectedAsync(ClientConnectedEventArgs args)
    {
        if (IsExternalClientId(args.ClientId))
        {
            _connectedExternalClientIds[args.ClientId] = 0;
        }

        return Task.CompletedTask;
    }

    private Task HandleClientDisconnectedAsync(ClientDisconnectedEventArgs args)
    {
        if (IsExternalClientId(args.ClientId))
        {
            _connectedExternalClientIds.TryRemove(args.ClientId, out _);
        }

        return Task.CompletedTask;
    }

    private async Task PublishIncomingMappedMessageAsync(
        MqttTopicMapping mapping,
        string path,
        object? value,
        string sourceClientId,
        CancellationToken cancellationToken)
    {
        if (_broker is null)
        {
            return;
        }

        if (string.Equals(mapping.PropertyName, ValueParameterName, StringComparison.Ordinal))
        {
            await _broker.UpdateValueAsync(
                item: new ItemModel(GetLeafName(path), value).Repath(path),
                sourceClientId: sourceClientId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await _broker.UpdatePropertyAsync(
            item: CreateParameterItem(path, mapping.PropertyName, value),
            parameterName: mapping.PropertyName,
            sourceClientId: sourceClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleMqttPublishAsync(InterceptingPublishEventArgs args)
    {
        if (IsServerOwnedOutputTopic(args.ApplicationMessage.Topic))
        {
            return;
        }

        if (IsInjectedServerMirrorTopic(args.ApplicationMessage.Topic))
        {
            return;
        }

        if (string.Equals(args.ClientId, ClientId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = args.ApplicationMessage.Payload.IsEmpty
            ? string.Empty
            : Encoding.UTF8.GetString(args.ApplicationMessage.Payload);
        try
        {
            await HandleIncomingPublishAsync(
                args.ApplicationMessage.Topic,
                payload,
                args.ClientId,
                args.ApplicationMessage.Retain,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MQTT publish mapping failed for topic '{args.ApplicationMessage.Topic}': {ex.Message}");
        }
    }

    private async Task ImportIncomingObservedMessageAsync(
        MqttTopicMapping mapping,
        object? value,
        string sourceClientId,
        bool retain,
        CancellationToken cancellationToken)
    {
        if (_broker is null)
        {
            return;
        }

        var item = string.Equals(mapping.PropertyName, ValueParameterName, StringComparison.Ordinal)
            ? new ItemModel(GetLeafName(mapping.Path), value).Repath(mapping.Path)
            : CreateParameterItem(mapping.Path, mapping.PropertyName, value);

        if (string.Equals(mapping.PropertyName, ValueParameterName, StringComparison.Ordinal))
        {
            var acknowledgement = await _broker.UpdateValueAsync(
                item: item,
                retained: retain,
                sourceClientId: sourceClientId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (acknowledgement.Accepted)
            {
                return;
            }
        }
        else
        {
            var acknowledgement = await _broker.UpdatePropertyAsync(
                item: item,
                parameterName: mapping.PropertyName,
                retained: retain || string.Equals(mapping.PropertyName, MetaParameterName, StringComparison.Ordinal),
                sourceClientId: sourceClientId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (acknowledgement.Accepted)
            {
                return;
            }
        }

        await _broker.PublishSnapshotAsync(
            item: item,
            retained: retain || string.Equals(mapping.PropertyName, MetaParameterName, StringComparison.Ordinal),
            sourceClientId: sourceClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishSnapshotAsync(ItemSnapshotMessage snapshot, CancellationToken cancellationToken)
    {
        if (IsSystemDataPath(snapshot.Path))
        {
            await PublishParameterTopicAsync(snapshot.Path, ValueParameterName, snapshot.ItemModel.Value, snapshot.SourceClientId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await PublishParameterTopicAsync(snapshot.Path, MetaParameterName, ResolveMetaPayload(snapshot.ItemModel), snapshot.SourceClientId, cancellationToken).ConfigureAwait(false);
        await PublishParameterTopicAsync(snapshot.Path, ValueParameterName, snapshot.ItemModel.Value, snapshot.SourceClientId, cancellationToken).ConfigureAwait(false);

        foreach (var parameter in snapshot.ItemModel.Properties.GetDictionary())
        {
            if (ShouldSkipPublishedProperty(parameter.Key))
            {
                continue;
            }

            await PublishParameterTopicAsync(snapshot.Path, parameter.Key, parameter.Value.Value, snapshot.SourceClientId, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task PublishParameterTopicAsync(string path, string parameterName, object? value, string? sourceClientId, CancellationToken cancellationToken)
    {
        UpdateVisibleItemTopic(path, parameterName, value);
        var topic = _topicMapper.ToTopic(path, parameterName, sourceClientId);
        return _publisher.PublishAsync(topic, FormatPayload(path, value), retain: true, cancellationToken);
    }

    private void UpdateVisibleItemTopic(string path, string parameterName, object? value)
    {
        if (IsSystemDataPath(path))
        {
            return;
        }

        var normalizedPath = ItemServerPath.Normalize(path);
        var topicKey = string.IsNullOrWhiteSpace(parameterName)
            ? ValueParameterName
            : ItemPath.ToSnakeCaseSegment(parameterName);

        if (value is null || (value is string text && string.IsNullOrEmpty(text)))
        {
            if (_visibleTopicKeysByPath.TryGetValue(normalizedPath, out var topicKeys))
            {
                topicKeys.TryRemove(topicKey, out _);
                if (topicKeys.IsEmpty)
                {
                    _visibleTopicKeysByPath.TryRemove(normalizedPath, out _);
                }
            }

            return;
        }

        var keysForPath = _visibleTopicKeysByPath.GetOrAdd(normalizedPath, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        keysForPath[topicKey] = 0;
    }

    private bool IsExternalClientId(string? clientId)
        => !string.IsNullOrWhiteSpace(clientId)
           && !string.Equals(clientId, ClientId, StringComparison.OrdinalIgnoreCase);

    private void TrackWritableState(string path, ItemModel item)
    {
        foreach (var parameter in item.Properties.GetDictionary())
        {
            TrackWritableState(path, parameter.Key, parameter.Value.Value);
        }
    }

    private void TrackWritableState(string path, string parameterName, object? value)
    {
        if (string.Equals(parameterName, "writable", StringComparison.OrdinalIgnoreCase))
        {
            GetWritableState(path).Writable = ToBoolean(value);
            return;
        }

        if (string.Equals(parameterName, "write_path", StringComparison.OrdinalIgnoreCase))
        {
            GetWritableState(path).WritePath = value?.ToString();
        }
    }

    private bool IsWritable(string path, out WritableItemState state)
    {
        if (_writableItems.TryGetValue(ItemServerPath.Normalize(path), out state!))
        {
            return state.Writable;
        }

        state = new WritableItemState();
        return false;
    }

    private WritableItemState GetWritableState(string path)
        => _writableItems.GetOrAdd(ItemServerPath.Normalize(path), _ => new WritableItemState());

    private bool ShouldSkipMqttMirror(string path, string parameterName, string? sourceClientId)
        => !string.IsNullOrWhiteSpace(sourceClientId)
           && _mqttOriginatedPublishes.TryGetValue(CreateOriginKey(path, parameterName, sourceClientId), out var count)
           && count > 0;

    private static string CreateOriginKey(string path, string parameterName, string sourceClientId)
        => string.Join('|', ItemServerPath.Normalize(path), string.IsNullOrWhiteSpace(parameterName) ? ValueParameterName : ItemPath.ToSnakeCaseSegment(parameterName), sourceClientId.Trim());

    private void TrackInjectedTopic(string topic)
        => _injectedServerMirrorTopics[topic] = DateTimeOffset.UtcNow.Add(InjectedTopicIgnoreWindow);

    private bool IsInjectedServerMirrorTopic(string topic)
    {
        if (!_injectedServerMirrorTopics.TryGetValue(topic, out var expiresAt))
        {
            return false;
        }

        if (expiresAt >= DateTimeOffset.UtcNow)
        {
            return true;
        }

        _injectedServerMirrorTopics.TryRemove(topic, out _);
        return false;
    }

    private bool IsServerOwnedOutputTopic(string topic)
    {
        var normalizedTopic = topic.Replace('\\', '/').Trim('/');
        if (normalizedTopic.StartsWith(SystemTopicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var brokerPrefix = string.IsNullOrWhiteSpace(_topicMapper.BaseTopic)
            ? "broker/"
            : _topicMapper.BaseTopic + "/broker/";
        return normalizedTopic.StartsWith(brokerPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSystemDataCoveredByMainSubscription()
    {
        try
        {
            return ItemServerPath.Matches(_options.SubscriptionRootPath, ItemServerHealthPaths.Root, recursive: true);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void ReportRejectedWriteDiagnostic(MqttTopicMapping mapping)
    {
        if (!AppContext.TryGetSwitch("Amium.Item.Server.Mqtt.WriteDiagnostics", out var enabled) || !enabled)
        {
            return;
        }

        var count = Interlocked.Increment(ref _rejectedWriteDiagnosticCount);
        if (count <= MaxRejectedWriteDiagnostics)
        {
            Console.Error.WriteLine($"MQTT write rejected for non-writable item path '{mapping.Path}' parameter '{mapping.PropertyName}'.");
        }
        else if (count == MaxRejectedWriteDiagnostics + 1)
        {
            Console.Error.WriteLine("MQTT write rejection diagnostics suppressed.");
        }
    }

    private static string FormatPayload(string path, object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (IsSystemDataPath(path))
        {
            return FormatSystemPayload(value);
        }

        return value switch
        {
            double number => FormatFloatingPointPayload(number),
            float number => FormatFloatingPointPayload(number),
            decimal number => FormatDecimalPayload(number),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string FormatSystemPayload(object value)
        => value switch
        {
            byte number => FormatFixedPointPayload(number),
            sbyte number => FormatFixedPointPayload(number),
            short number => FormatFixedPointPayload(number),
            ushort number => FormatFixedPointPayload(number),
            int number => FormatFixedPointPayload(number),
            uint number => FormatFixedPointPayload(number),
            long number => FormatFixedPointPayload(number),
            ulong number => FormatFixedPointPayload(number),
            float number => FormatFixedPointPayload(number),
            double number => FormatFixedPointPayload(number),
            decimal number => FormatFixedPointPayload(number),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static string FormatFixedPointPayload<T>(T value)
        where T : IFormattable
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string ResolveMetaPayload(ItemModel item)
    {
        if (!item.Properties.GetDictionary().TryGetValue(MetaParameterName, out var meta)
            || meta.Value is null)
        {
            return "{}";
        }

        return ResolveMetaPayload(meta.Value);
    }

    private static string ResolveMetaPayload(object? value)
    {
        if (value is null)
        {
            return "{}";
        }

        if (value is string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "{}" : text;
        }

        return JsonSerializer.Serialize(value);
    }

    private static bool ShouldSkipPublishedProperty(string parameterName)
    {
        var normalizedParameterName = ItemPath.ToSnakeCaseSegment(parameterName);
        return string.Equals(normalizedParameterName, MetaParameterName, StringComparison.OrdinalIgnoreCase)
               || IsRedundantPublishedProperty(normalizedParameterName);
    }

    private static bool IsSystemDataPath(string path)
    {
        var normalizedPath = ItemServerPath.Normalize(path);
        return string.Equals(normalizedPath, ItemServerHealthPaths.Root, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(ItemServerHealthPaths.Root + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedundantPublishedProperty(string parameterName)
    {
        var normalizedParameterName = ItemPath.ToSnakeCaseSegment(parameterName);
        return string.Equals(normalizedParameterName, "name", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedParameterName, "path", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFloatingPointPayload(double value)
    {
        var payload = value.ToString("R", CultureInfo.InvariantCulture);
        return LooksIntegral(payload) ? payload + ".0" : payload;
    }

    private static string FormatDecimalPayload(decimal value)
    {
        var payload = value.ToString(CultureInfo.InvariantCulture);
        return LooksIntegral(payload) ? payload + ".0" : payload;
    }

    private static bool LooksIntegral(string payload)
        => !payload.Contains('.', StringComparison.Ordinal) && !payload.Contains('E', StringComparison.OrdinalIgnoreCase);

    private static object? ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        if (bool.TryParse(payload, out var boolean))
        {
            return boolean;
        }

        if (long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatingPoint))
        {
            return floatingPoint;
        }

        return payload;
    }

    private static bool ToBoolean(object? value)
        => value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false,
        };

    private static string GetLeafName(string path)
    {
        var normalizedPath = ItemServerPath.Normalize(path);
        var separatorIndex = normalizedPath.LastIndexOf('.');
        return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
    }

    private static ItemModel CreateParameterItem(string path, string parameterName, object? value)
    {
        var item = new ItemModel(GetLeafName(path)).Repath(path);
        item.Properties[parameterName].Value = value is null ? null! : value;
        return item;
    }

    private sealed class WritableItemState
    {
        public bool Writable { get; set; }

        public string? WritePath { get; set; }
    }
}
