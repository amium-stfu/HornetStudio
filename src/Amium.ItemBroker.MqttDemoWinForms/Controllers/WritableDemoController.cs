using Amium.ItemBroker;
using Amium.Items;

namespace Amium.ItemBroker.MqttDemoWinForms.Controllers;

internal sealed class WritableDemoController : IItemBrokerClient, IAsyncDisposable
{
    private readonly Item _item;
    private IItemBroker? _broker;
    private IItemSubscription? _subscription;

    internal WritableDemoController(string clientId)
    {
        ClientId = clientId;
        _item = new Item("Setpoint", 50.0, "Demo.Controls");
        _item.Params["Unit"].Value = "percent";
        _item.Params["Format"].Value = "0.0";
        _item.Params["Writable"].Value = true;
        _item.Params["Description"].Value = "Publish a new value to the item topic to trigger an external write.";
    }

    public string ClientId { get; }

    internal event EventHandler<string>? LogMessage;

    internal event EventHandler<WritableDemoState>? StateChanged;

    internal bool IsRunning => _subscription is not null;

    internal string ItemPath => _item.Path ?? "Demo.Controls.Setpoint";

    internal async Task StartAsync(IItemBroker broker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);

        if (IsRunning)
        {
            OnLogMessage("Writable demo item is already registered.");
            return;
        }

        _broker = broker;
        _subscription = await broker.SubscribeAsync(
            client: this,
            path: ItemPath,
            options: new ItemSubscriptionOptions
            {
                Recursive = false,
                IncludeRetained = false,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await broker.PublishSnapshotAsync(
            item: _item,
            retained: true,
            sourceClientId: ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        PublishState(lastWriteSource: "Initial value", lastWriteUtc: DateTimeOffset.UtcNow);
        OnLogMessage($"Writable demo item published at '{ItemPath}'.");
    }

    internal async Task StopAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
            _subscription = null;
        }

        _broker = null;
        OnLogMessage("Writable demo item stopped.");
    }

    public async Task ReceiveAsync(ItemBrokerMessage message, CancellationToken cancellationToken = default)
    {
        if (message is not ItemWriteRequestMessage writeRequest)
        {
            return;
        }

        if (!string.Equals(writeRequest.ParameterName, "Value", StringComparison.OrdinalIgnoreCase))
        {
            OnLogMessage($"Ignored write for unsupported parameter '{writeRequest.ParameterName}'.");
            return;
        }

        if (!ItemBrokerValueCoercion.TryConvertForExistingValue(writeRequest.Value, _item.Value, out object? convertedValue))
        {
            OnLogMessage($"Rejected write '{writeRequest.Value}' because it could not be converted to the current value type.");
            return;
        }

        _item.Value = convertedValue!;

        if (_broker is not null)
        {
            await _broker.UpdateValueAsync(
                item: _item,
                sourceClientId: ClientId,
                correlationId: writeRequest.CorrelationId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var source = writeRequest.SourceClientId ?? "unknown";
        PublishState(lastWriteSource: source, lastWriteUtc: DateTimeOffset.UtcNow);
        OnLogMessage($"Writable demo item updated to {_item.Value:0.0} from '{source}'.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void PublishState(string lastWriteSource, DateTimeOffset lastWriteUtc)
    {
        var numericValue = Convert.ToDouble(_item.Value, System.Globalization.CultureInfo.InvariantCulture);
        StateChanged?.Invoke(
            this,
            new WritableDemoState(
                Value: numericValue,
                LastWriteSource: lastWriteSource,
                LastWriteUtc: lastWriteUtc));
    }

    private void OnLogMessage(string message)
        => LogMessage?.Invoke(this, message);
}

internal sealed record WritableDemoState(
    double Value,
    string LastWriteSource,
    DateTimeOffset LastWriteUtc);
