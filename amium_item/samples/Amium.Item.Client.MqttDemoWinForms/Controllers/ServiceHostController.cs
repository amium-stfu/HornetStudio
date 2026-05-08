using Amium.Item.Server;
using Amium.Item.Server.Mqtt;

namespace Amium.Item.Server.MqttDemoWinForms.Controllers;

internal sealed class ServiceHostController : IAsyncDisposable
{
    private readonly MqttItemServerOptions _options;
    private MqttItemServerHost? _host;

    internal ServiceHostController(MqttItemServerOptions options)
    {
        _options = options;
    }

    internal event EventHandler<string>? LogMessage;

    internal bool IsRunning => _host?.IsRunning == true;

    internal IItemServer? Broker => _host?.Broker;

    internal string EndpointSummary => $"{_options.Host}:{_options.Port} / {_options.BaseTopic}";

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            OnLogMessage("Service host is already running.");
            return;
        }

        _host = new MqttItemServerHost(_options);
        await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        OnLogMessage($"Service host started on {_options.Host}:{_options.Port} with base topic '{_options.BaseTopic}'.");
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            await _host.StopAsync(cancellationToken).ConfigureAwait(false);
            OnLogMessage("Service host stopped.");
        }
        finally
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void OnLogMessage(string message)
        => LogMessage?.Invoke(this, message);
}
