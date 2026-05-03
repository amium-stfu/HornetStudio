using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Amium.ItemBroker;
using Amium.ItemBroker.Client;
using MQTTnet;
using MQTTnet.Exceptions;
using ItemModel = Amium.Items.Item;

namespace Amium.ItemBroker.Mqtt.Client;

/// <summary>
/// Provides an MQTT-backed item broker client session for publishing item data.
/// </summary>
public sealed class MqttItemBrokerClientSession : IItemBrokerClientSession, IAsyncDisposable
{
    private const int MaxReceiveDiagnostics = 80;

    private readonly MqttItemBrokerClientOptions _options;
    private readonly MqttItemTopicMapper _topicMapper;
    private readonly IItemBrokerClock _clock;
    private readonly MqttClientFactory _mqttFactory = new();
    private readonly ConcurrentDictionary<string, MqttItemSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private IMqttClient? _client;
    private bool _isSubscribedToItems;
    private int _receiveDiagnosticCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttItemBrokerClientSession"/> class.
    /// </summary>
    /// <param name="options">The MQTT client options.</param>
    /// <param name="clock">The optional timestamp source.</param>
    public MqttItemBrokerClientSession(
        MqttItemBrokerClientOptions options,
        IItemBrokerClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BaseTopic);

        _options = options;
        _topicMapper = new MqttItemTopicMapper(options.BaseTopic);
        _clock = clock ?? new SystemItemBrokerClock();
    }

    /// <inheritdoc />
    public string ClientId => _options.ClientId;

    /// <summary>
    /// Gets the registry that reconstructs retained and live remote item trees.
    /// </summary>
    public MqttRemoteItemRegistry RemoteItems { get; } = new();

    /// <summary>
    /// Opens the MQTT connection when it is not already connected.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => EnsureConnectedAsync(cancellationToken);

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task PublishSnapshotAsync(
        ItemModel item,
        bool retained = true,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var publishItem in EnumerateItems(item))
        {
            await PublishValuePathAsync(
                path: publishItem.Path,
                value: publishItem.Item.Value,
                correlationId: correlationId,
                retained: retained,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var parameter in publishItem.Item.Params.GetDictionary())
            {
                await PublishParameterPathAsync(
                    path: publishItem.Path,
                    parameterName: parameter.Key,
                    value: parameter.Value.Value,
                    correlationId: correlationId,
                    retained: retained,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task<ItemBrokerAckMessage> UpdateValueAsync(
        ItemModel item,
        bool retained = false,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        => UpdateParameterAsync(
            item: item,
            parameterName: "Value",
            retained: retained,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ItemBrokerAckMessage> UpdateParameterAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        var normalizedPath = ItemBrokerItemPath.Resolve(item);
        var normalizedParameterName = string.IsNullOrWhiteSpace(parameterName) ? "Value" : parameterName.Trim();
        var value = string.Equals(normalizedParameterName, "Value", StringComparison.OrdinalIgnoreCase)
            ? item.Value
            : item.Params[normalizedParameterName].Value;

        await PublishParameterPathAsync(
            path: normalizedPath,
            parameterName: normalizedParameterName,
            value: value,
            correlationId: correlationId,
            retained: retained,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ItemBrokerAckMessage(
            Path: normalizedPath,
            Accepted: true,
            Reason: null,
            SourceClientId: ClientId,
            CorrelationId: correlationId,
            Timestamp: _clock.GetUtcNow());
    }

    /// <inheritdoc />
    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemBrokerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new MqttItemSubscription(
            client: this,
            path: ItemBrokerPath.Normalize(path),
            recursive: options?.Recursive ?? true,
            handler: handler,
            remove: id => _subscriptions.TryRemove(id, out _));
        _subscriptions[subscription.SubscriptionId] = subscription;
        return Task.FromResult<IItemSubscription>(subscription);
    }

    /// <inheritdoc />
    public Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }

            _client.Dispose();
        }
    }

    private Task PublishValuePathAsync(
        string path,
        object? value,
        string? correlationId,
        bool retained,
        CancellationToken cancellationToken)
        => PublishParameterPathAsync(path, "Value", value, correlationId, retained, cancellationToken);

    private async Task PublishParameterPathAsync(
        string path,
        string parameterName,
        object? value,
        string? correlationId,
        bool retained,
        CancellationToken cancellationToken)
    {
        var topic = _topicMapper.ToTopic(path, parameterName, ClientId);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(FormatPayload(value))
            .WithRetainFlag(retained)
            .Build();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                await _client!.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is MqttClientNotConnectedException or MqttCommunicationException or SocketException)
            {
                _client?.Dispose();
                _client = null;
                await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client?.IsConnected == true)
        {
            return;
        }

        _client?.Dispose();
        _client = _mqttFactory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _isSubscribedToItems = false;

        var builder = new MqttClientOptionsBuilder()
            .WithClientId(ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithProtocolVersion(_options.ProtocolVersion)
            .WithKeepAlivePeriod(_options.KeepAlivePeriod);

        if (_options.CleanSession)
        {
            builder.WithCleanSession();
        }

        RemoteItems.ReportDiagnostic($"connecting host={_options.Host}:{_options.Port} clientId={ClientId} baseTopic={_topicMapper.BaseTopic}");
        var result = await _client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"MQTT connection was rejected by the broker: {result.ResultCode}.");
        }

        RemoteItems.ReportDiagnostic($"connected result={result.ResultCode} clientId={ClientId}");
        await SubscribeToItemTopicsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SubscribeToItemTopicsAsync(CancellationToken cancellationToken)
    {
        if (_client?.IsConnected != true || _isSubscribedToItems)
        {
            return;
        }

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(_topicMapper.ItemSubscriptionTopic)
            .Build();
        RemoteItems.ReportDiagnostic($"subscribing topicFilter={_topicMapper.ItemSubscriptionTopic}");
        await _client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
        _isSubscribedToItems = true;
        RemoteItems.ReportDiagnostic($"subscribed topicFilter={_topicMapper.ItemSubscriptionTopic}");
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        if (!_topicMapper.TryMapTopic(topic, out var mapping))
        {
            ReportReceiveDiagnostic($"received unmapped topic={topic}");
            return;
        }

        var payload = args.ApplicationMessage.Payload.IsEmpty
            ? string.Empty
            : Encoding.UTF8.GetString(args.ApplicationMessage.Payload);
        ReportReceiveDiagnostic($"received mapped topic={topic} clientId={mapping.ClientId} path={mapping.Path} parameter={mapping.ParameterName}");
        RemoteItems.Apply(mapping, payload);

        var timestamp = _clock.GetUtcNow();
        ItemBrokerMessage message = string.Equals(mapping.ParameterName, "Value", StringComparison.OrdinalIgnoreCase)
            ? new ItemValueChangedMessage(mapping.Path, ParsePayload(payload), mapping.ClientId, null, timestamp)
            : new ItemParameterChangedMessage(mapping.Path, mapping.ParameterName, ParsePayload(payload), mapping.ClientId, null, timestamp);

        foreach (var subscription in _subscriptions.Values)
        {
            if (ItemBrokerPath.Matches(subscription.Path, mapping.Path, subscription.Recursive))
            {
                await subscription.HandleAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void ReportReceiveDiagnostic(string message)
    {
        if (!AppContext.TryGetSwitch("Amium.ItemBroker.Mqtt.ReceiveDiagnostics", out var enabled) || !enabled)
        {
            return;
        }

        var count = Interlocked.Increment(ref _receiveDiagnosticCount);
        if (count <= MaxReceiveDiagnostics)
        {
            RemoteItems.ReportDiagnostic(message);
        }
        else if (count == MaxReceiveDiagnostics + 1)
        {
            RemoteItems.ReportDiagnostic("receive diagnostics suppressed");
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

    private static IEnumerable<PublishItem> EnumerateItems(ItemModel root)
    {
        yield return new PublishItem(root, ItemBrokerItemPath.Resolve(root));

        foreach (var child in root.GetDictionary().Values)
        {
            foreach (var descendant in EnumerateItems(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record PublishItem(ItemModel Item, string Path);

    private sealed class MqttItemSubscription : IItemSubscription
    {
        private readonly Func<ItemBrokerMessage, CancellationToken, Task> _handler;
        private readonly Action<string> _remove;

        public MqttItemSubscription(
            IItemBrokerClient client,
            string path,
            bool recursive,
            Func<ItemBrokerMessage, CancellationToken, Task> handler,
            Action<string> remove)
        {
            Client = client;
            Path = path;
            Recursive = recursive;
            _handler = handler;
            _remove = remove;
            SubscriptionId = Guid.NewGuid().ToString("N");
        }

        public string SubscriptionId { get; }

        public IItemBrokerClient Client { get; }

        public string Path { get; }

        public bool Recursive { get; }

        public Task HandleAsync(ItemBrokerMessage message, CancellationToken cancellationToken)
            => _handler(message, cancellationToken);

        public ValueTask DisposeAsync()
        {
            _remove(SubscriptionId);
            return ValueTask.CompletedTask;
        }
    }
}
