using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Amium.Items;
using MQTTnet;
using MQTTnet.Server;
using ItemModel = Amium.Items.Item;

namespace Amium.ItemBroker.Mqtt;

/// <summary>
/// Bridges item broker messages to an embedded MQTT server.
/// </summary>
public sealed class MqttItemBrokerAdapter : IItemBrokerTransport, IItemBrokerClient, IMqttMessagePublisher, IAsyncDisposable
{
    private static readonly TimeSpan InjectedTopicIgnoreWindow = TimeSpan.FromSeconds(2);
    private const int MaxRejectedWriteDiagnostics = 80;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly MqttItemBrokerOptions _options;
    private readonly MqttItemTopicMapper _topicMapper;
    private readonly IMqttMessagePublisher _publisher;
    private readonly ConcurrentDictionary<string, WritableItemState> _writableItems = new(PathComparer);
    private readonly ConcurrentDictionary<string, int> _mqttOriginatedPublishes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _injectedBrokerMirrorTopics = new(StringComparer.OrdinalIgnoreCase);
    private MqttServer? _server;
    private IItemBroker? _broker;
    private IItemSubscription? _subscription;
    private int _rejectedWriteDiagnosticCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemBrokerAdapter"/> class.
    /// </summary>
    /// <param name="options">The MQTT adapter options.</param>
    /// <param name="publisher">An optional publisher used for tests or alternate MQTT output.</param>
    public MqttItemBrokerAdapter(MqttItemBrokerOptions options, IMqttMessagePublisher? publisher = null)
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

    /// <inheritdoc />
    public async Task StartAsync(IItemBroker broker, CancellationToken cancellationToken = default)
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
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
            _subscription = null;
        }

        if (_server is not null)
        {
            _server.InterceptingPublishAsync -= HandleMqttPublishAsync;
            await _server.StopAsync().ConfigureAwait(false);
            _server.Dispose();
            _server = null;
        }
    }

    /// <inheritdoc />
    public async Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case ItemSnapshotMessage snapshot:
                TrackWritableState(snapshot.Path, snapshot.Item);
                await PublishSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case ItemValueChangedMessage valueChanged:
                if (!ShouldSkipMqttMirror(valueChanged.Path, "Value", valueChanged.SourceClientId))
                {
                    await PublishParameterTopicAsync(valueChanged.Path, "Value", valueChanged.Value, valueChanged.SourceClientId, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ItemParameterChangedMessage parameterChanged:
                TrackWritableState(parameterChanged.Path, parameterChanged.ParameterName, parameterChanged.Value);
                if (!ShouldSkipMqttMirror(parameterChanged.Path, parameterChanged.ParameterName, parameterChanged.SourceClientId))
                {
                    await PublishParameterTopicAsync(parameterChanged.Path, parameterChanged.ParameterName, parameterChanged.Value, parameterChanged.SourceClientId, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ItemRemoveMessage remove:
                await _publisher.PublishAsync(_topicMapper.ToTopic(remove.Path, "Value", remove.SourceClientId), string.Empty, retain: true, cancellationToken).ConfigureAwait(false);
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
    {
        if (_broker is null || !_topicMapper.TryMapTopic(topic, out var mapping))
        {
            return;
        }

        var value = ParsePayload(payload);
        var sourceClientId = mapping.ClientId ?? ClientId;
        TrackWritableState(mapping.Path, mapping.ParameterName, value);
        if (!IsWritable(mapping.Path, out var state))
        {
            ReportRejectedWriteDiagnostic(mapping);
            return;
        }

        var originKey = CreateOriginKey(mapping.Path, mapping.ParameterName, sourceClientId);
        _mqttOriginatedPublishes.AddOrUpdate(originKey, addValue: 1, (_, count) => count + 1);
        try
        {
            var targetPath = string.IsNullOrWhiteSpace(state.WritePath) ? mapping.Path : state.WritePath;
            await PublishIncomingMappedMessageAsync(mapping, targetPath, value, sourceClientId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mqttOriginatedPublishes.AddOrUpdate(originKey, addValue: 0, (_, count) => Math.Max(0, count - 1));
            if (_mqttOriginatedPublishes.TryGetValue(originKey, out var remaining) && remaining == 0)
            {
                _mqttOriginatedPublishes.TryRemove(originKey, out _);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
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

        if (string.Equals(mapping.ParameterName, "Value", StringComparison.OrdinalIgnoreCase))
        {
            await _broker.UpdateValueAsync(
                item: new ItemModel(GetLeafName(path), value).Repath(path),
                sourceClientId: sourceClientId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await _broker.UpdateParameterAsync(
            item: CreateParameterItem(path, mapping.ParameterName, value),
            parameterName: mapping.ParameterName,
            sourceClientId: sourceClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleMqttPublishAsync(InterceptingPublishEventArgs args)
    {
        if (IsBrokerOwnedOutputTopic(args.ApplicationMessage.Topic))
        {
            return;
        }

        if (IsInjectedBrokerMirrorTopic(args.ApplicationMessage.Topic))
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
            await HandleIncomingPublishAsync(args.ApplicationMessage.Topic, payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MQTT publish mapping failed for topic '{args.ApplicationMessage.Topic}': {ex.Message}");
        }
    }

    private async Task PublishSnapshotAsync(ItemSnapshotMessage snapshot, CancellationToken cancellationToken)
    {
        await PublishParameterTopicAsync(snapshot.Path, "Value", snapshot.Item.Value, snapshot.SourceClientId, cancellationToken).ConfigureAwait(false);

        foreach (var parameter in snapshot.Item.Params.GetDictionary())
        {
            await PublishParameterTopicAsync(snapshot.Path, parameter.Key, parameter.Value.Value, snapshot.SourceClientId, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task PublishParameterTopicAsync(string path, string parameterName, object? value, string? sourceClientId, CancellationToken cancellationToken)
    {
        var topic = _topicMapper.ToTopic(path, parameterName, sourceClientId);
        return _publisher.PublishAsync(topic, FormatPayload(value), retain: true, cancellationToken);
    }

    private void TrackWritableState(string path, ItemModel item)
    {
        foreach (var parameter in item.Params.GetDictionary())
        {
            TrackWritableState(path, parameter.Key, parameter.Value.Value);
        }
    }

    private void TrackWritableState(string path, string parameterName, object? value)
    {
        if (string.Equals(parameterName, "Writable", StringComparison.OrdinalIgnoreCase))
        {
            GetWritableState(path).Writable = ToBoolean(value);
            return;
        }

        if (string.Equals(parameterName, "WritePath", StringComparison.OrdinalIgnoreCase))
        {
            GetWritableState(path).WritePath = value?.ToString();
        }
    }

    private bool IsWritable(string path, out WritableItemState state)
    {
        if (_writableItems.TryGetValue(ItemBrokerPath.Normalize(path), out state!))
        {
            return state.Writable;
        }

        state = new WritableItemState();
        return false;
    }

    private WritableItemState GetWritableState(string path)
        => _writableItems.GetOrAdd(ItemBrokerPath.Normalize(path), _ => new WritableItemState());

    private bool ShouldSkipMqttMirror(string path, string parameterName, string? sourceClientId)
        => !string.IsNullOrWhiteSpace(sourceClientId)
           && _mqttOriginatedPublishes.TryGetValue(CreateOriginKey(path, parameterName, sourceClientId), out var count)
           && count > 0;

    private static string CreateOriginKey(string path, string parameterName, string sourceClientId)
        => string.Join('|', ItemBrokerPath.Normalize(path), string.IsNullOrWhiteSpace(parameterName) ? "Value" : parameterName.Trim(), sourceClientId.Trim());

    private void TrackInjectedTopic(string topic)
        => _injectedBrokerMirrorTopics[topic] = DateTimeOffset.UtcNow.Add(InjectedTopicIgnoreWindow);

    private bool IsInjectedBrokerMirrorTopic(string topic)
    {
        if (!_injectedBrokerMirrorTopics.TryGetValue(topic, out var expiresAt))
        {
            return false;
        }

        if (expiresAt >= DateTimeOffset.UtcNow)
        {
            return true;
        }

        _injectedBrokerMirrorTopics.TryRemove(topic, out _);
        return false;
    }

    private bool IsBrokerOwnedOutputTopic(string topic)
    {
        var normalizedTopic = topic.Replace('\\', '/').Trim('/');
        var brokerPrefix = _topicMapper.BaseTopic + "/broker/";
        return normalizedTopic.StartsWith(brokerPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportRejectedWriteDiagnostic(MqttTopicMapping mapping)
    {
        if (!AppContext.TryGetSwitch("Amium.ItemBroker.Mqtt.WriteDiagnostics", out var enabled) || !enabled)
        {
            return;
        }

        var count = Interlocked.Increment(ref _rejectedWriteDiagnosticCount);
        if (count <= MaxRejectedWriteDiagnostics)
        {
            Console.Error.WriteLine($"MQTT write rejected for non-writable item path '{mapping.Path}' parameter '{mapping.ParameterName}'.");
        }
        else if (count == MaxRejectedWriteDiagnostics + 1)
        {
            Console.Error.WriteLine("MQTT write rejection diagnostics suppressed.");
        }
    }

    private static string FormatPayload(object? value)
        => value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            double number => FormatFloatingPointPayload(number),
            float number => FormatFloatingPointPayload(number),
            decimal number => FormatDecimalPayload(number),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

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
        var normalizedPath = ItemBrokerPath.Normalize(path);
        var separatorIndex = normalizedPath.LastIndexOf('.');
        return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
    }

    private static ItemModel CreateParameterItem(string path, string parameterName, object? value)
    {
        var item = new ItemModel(GetLeafName(path)).Repath(path);
        item.Params[parameterName].Value = value is null ? null! : value;
        return item;
    }

    private sealed class WritableItemState
    {
        public bool Writable { get; set; }

        public string? WritePath { get; set; }
    }
}
