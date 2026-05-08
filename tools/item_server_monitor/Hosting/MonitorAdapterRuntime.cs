using System.Threading;
using Amium.Item.Server;

namespace Item.Server.Monitor.Hosting;

internal sealed class MonitorAdapterRuntime
{
    private readonly IItemServer _broker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IItemServerTransport? _transport;

    public MonitorAdapterRuntime(MonitorAdapterDefinition definition, MonitorAdapterOptions options, IItemServer broker)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public event EventHandler? StateChanged;

    public MonitorAdapterDefinition Definition { get; }

    public MonitorAdapterOptions Options { get; private set; }

    public MonitorAdapterStatus Status { get; private set; } = MonitorAdapterStatus.Stopped;

    public string LastError { get; private set; } = string.Empty;

    public string Endpoint => Definition.Factory.FormatEndpoint(Options);

    public bool IsBusy => Status is MonitorAdapterStatus.Starting or MonitorAdapterStatus.Stopping;

    public void ApplyOptions(MonitorAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options.Clone();
        RaiseStateChanged();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status is MonitorAdapterStatus.Running or MonitorAdapterStatus.Starting)
            {
                return;
            }

            SetState(status: MonitorAdapterStatus.Starting, lastError: string.Empty);
            var transport = Definition.Factory.CreateTransport(Options.Clone());
            try
            {
                await transport.StartAsync(_broker, cancellationToken).ConfigureAwait(false);
                _transport = transport;
                SetState(status: MonitorAdapterStatus.Running, lastError: string.Empty);
            }
            catch (Exception ex)
            {
                await DisposeTransportAsync(transport).ConfigureAwait(false);
                _transport = null;
                SetState(status: MonitorAdapterStatus.Failed, lastError: ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_transport is null || Status is MonitorAdapterStatus.Stopped or MonitorAdapterStatus.Stopping)
            {
                if (Status != MonitorAdapterStatus.Stopped)
                {
                    SetState(status: MonitorAdapterStatus.Stopped, lastError: string.Empty);
                }

                return;
            }

            var transport = _transport;
            SetState(status: MonitorAdapterStatus.Stopping, lastError: string.Empty);
            try
            {
                await transport.StopAsync(cancellationToken).ConfigureAwait(false);
                await DisposeTransportAsync(transport).ConfigureAwait(false);
                _transport = null;
                SetState(status: MonitorAdapterStatus.Stopped, lastError: string.Empty);
            }
            catch (Exception ex)
            {
                _transport = null;
                await DisposeTransportAsync(transport).ConfigureAwait(false);
                SetState(status: MonitorAdapterStatus.Failed, lastError: ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetState(MonitorAdapterStatus status, string lastError)
    {
        Status = status;
        LastError = lastError;
        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static async Task DisposeTransportAsync(IItemServerTransport transport)
    {
        if (transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (transport is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}