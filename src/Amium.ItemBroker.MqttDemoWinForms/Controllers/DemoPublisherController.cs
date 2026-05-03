using Amium.ItemBroker.Mqtt.Client;
using Amium.Items;

namespace Amium.ItemBroker.MqttDemoWinForms.Controllers;

internal sealed class DemoPublisherController : IAsyncDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(100);

    private readonly MqttItemBrokerClientOptions _options;
    private readonly Item _temperature;
    private readonly Item _pressure;
    private MqttItemBrokerClientSession? _session;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    internal DemoPublisherController(MqttItemBrokerClientOptions options)
    {
        _options = options;

        _temperature = new Item("Temperature", 22.0, "Demo.Sensors");
        _temperature.Params["Unit"].Value = "degC";
        _temperature.Params["Format"].Value = "0.0";
        _temperature["Raw"].Value = "0.0";

        _pressure = new Item("Pressure", 1012.0, "Demo.Sensors");
        _pressure.Params["Unit"].Value = "hPa";
        _pressure.Params["Format"].Value = "0.0";
        _pressure["Raw"].Value = "0.0";
    }

    internal event EventHandler<string>? LogMessage;

    internal event EventHandler<DemoPublisherState>? StateChanged;

    internal bool IsRunning => _runTask is not null;

    internal string TemperaturePath => _temperature.Path ?? "Demo.Sensors.Temperature";

    internal string PressurePath => _pressure.Path ?? "Demo.Sensors.Pressure";

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            OnLogMessage("Demo publisher is already running.");
            return;
        }

        _session = new MqttItemBrokerClientSession(_options);
        await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await PublishSnapshotsAsync(_session, cancellationToken).ConfigureAwait(false);

        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = RunLoopAsync(_session, _runCancellation.Token);
        OnLogMessage("Demo publisher connected and publishing sensor values.");
    }

    internal async Task StopAsync()
    {
        if (_runTask is null)
        {
            return;
        }

        try
        {
            await _runCancellation!.CancelAsync().ConfigureAwait(false);
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;

            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
        }

        OnLogMessage("Demo publisher stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunLoopAsync(MqttItemBrokerClientSession session, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(UpdateInterval);
        var startedAt = DateTimeOffset.UtcNow;

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            var temperature = Math.Round(22.5 + Math.Sin(elapsed.TotalSeconds) * 2.5, digits: 1);
            var pressure = Math.Round(1010.0 + Math.Cos(elapsed.TotalSeconds * 0.7) * 10.0, digits: 1);

            _temperature.Value = temperature;
            _pressure.Value = pressure;
            _temperature["Raw"].Value = temperature.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            _pressure["Raw"].Value = pressure.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            await session.UpdateValueAsync(_temperature, cancellationToken: cancellationToken).ConfigureAwait(false);
            await session.UpdateValueAsync(_pressure, cancellationToken: cancellationToken).ConfigureAwait(false);

            StateChanged?.Invoke(
                this,
                new DemoPublisherState(
                    Temperature: temperature,
                    Pressure: pressure,
                    TimestampUtc: DateTimeOffset.UtcNow));
        }
    }

    private async Task PublishSnapshotsAsync(MqttItemBrokerClientSession session, CancellationToken cancellationToken)
    {
        await session.PublishSnapshotAsync(_temperature, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await session.PublishSnapshotAsync(_pressure, retained: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        StateChanged?.Invoke(
            this,
            new DemoPublisherState(
                Temperature: Convert.ToDouble(_temperature.Value, System.Globalization.CultureInfo.InvariantCulture),
                Pressure: Convert.ToDouble(_pressure.Value, System.Globalization.CultureInfo.InvariantCulture),
                TimestampUtc: DateTimeOffset.UtcNow));
    }

    private void OnLogMessage(string message)
        => LogMessage?.Invoke(this, message);
}

internal sealed record DemoPublisherState(
    double Temperature,
    double Pressure,
    DateTimeOffset TimestampUtc);
