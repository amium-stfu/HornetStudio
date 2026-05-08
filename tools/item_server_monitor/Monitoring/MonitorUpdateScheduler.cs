using Avalonia.Threading;

namespace Item.Server.Monitor.Monitoring;

public sealed class MonitorUpdateScheduler : IDisposable
{
    private readonly object _sync = new();
    private TimeSpan _interval;
    private CancellationTokenSource? _pendingDelay;
    private bool _requestQueued;
    private bool _disposed;

    public MonitorUpdateScheduler(TimeSpan interval)
    {
        _interval = interval;
    }

    public TimeSpan Interval
    {
        get
        {
            lock (_sync)
            {
                return _interval;
            }
        }
        set
        {
            lock (_sync)
            {
                _interval = value < TimeSpan.Zero ? TimeSpan.Zero : value;
                _pendingDelay?.Cancel();
                _pendingDelay?.Dispose();
                _pendingDelay = null;
                _requestQueued = false;
            }
        }
    }

    public void Request(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CancellationToken token;
        TimeSpan delay;

        lock (_sync)
        {
            if (_disposed || _requestQueued)
            {
                return;
            }

            _requestQueued = true;
            _pendingDelay = new CancellationTokenSource();
            token = _pendingDelay.Token;
            delay = _interval;
        }

        _ = ExecuteAsync(action, delay, token);
    }

    public void RunNow(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sync)
        {
            _pendingDelay?.Cancel();
            _pendingDelay?.Dispose();
            _pendingDelay = null;
            _requestQueued = false;
        }

        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingDelay?.Cancel();
            _pendingDelay?.Dispose();
            _pendingDelay = null;
            _requestQueued = false;
        }
    }

    private async Task ExecuteAsync(Action action, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _requestQueued = false;
            _pendingDelay?.Dispose();
            _pendingDelay = null;
        }

        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }
}