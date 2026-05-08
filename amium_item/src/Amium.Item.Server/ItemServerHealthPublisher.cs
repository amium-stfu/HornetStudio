using System.Diagnostics;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace Amium.Item.Server;

/// <summary>
/// Publishes retained core health values for an item server runtime.
/// </summary>
public sealed class ItemServerHealthPublisher : IAsyncDisposable
{
    private const double BytesPerMegabyte = 1024.0 * 1024.0;
    private readonly IItemServer _server;
    private readonly ItemServerHealthOptions _options;
    private readonly Func<int> _retainedItemCountProvider;
    private PeriodicTimer? _timer;
    private Task? _healthLoopTask;
    private CancellationTokenSource? _healthCancellation;
    private TimeSpan _lastProcessorTime;
    private DateTimeOffset _lastCpuSampleUtc;
    private double _lastCpuUsagePercent;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemServerHealthPublisher"/> class.
    /// </summary>
    /// <param name="server">The target item server.</param>
    /// <param name="options">The core health publisher options.</param>
    /// <param name="retainedItemCountProvider">An optional retained item count provider.</param>
    public ItemServerHealthPublisher(
        IItemServer server,
        ItemServerHealthOptions? options = null,
        Func<int>? retainedItemCountProvider = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _options = options ?? new ItemServerHealthOptions();
        _retainedItemCountProvider = retainedItemCountProvider ?? CreateRetainedItemCountProvider(server);
    }

    /// <summary>
    /// Gets a value indicating whether the publisher is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Starts core health publishing.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning || !_options.Enabled)
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        _lastCpuSampleUtc = startedAt;
        _lastProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
        _lastCpuUsagePercent = 0.0;

        await PublishHealthSnapshotsAsync(startedAt, cancellationToken).ConfigureAwait(false);
        await PublishHealthAsync(startedAt, cancellationToken).ConfigureAwait(false);

        _timer = new PeriodicTimer(_options.PublishInterval);
        _healthCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _healthLoopTask = RunHealthLoopAsync(startedAt, _healthCancellation.Token);
        IsRunning = true;
    }

    /// <summary>
    /// Stops core health publishing.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_healthCancellation is not null)
        {
            await _healthCancellation.CancelAsync().ConfigureAwait(false);
            _healthCancellation.Dispose();
            _healthCancellation = null;
        }

        if (_healthLoopTask is not null)
        {
            try
            {
                await _healthLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _healthLoopTask = null;
        }

        _timer?.Dispose();
        _timer = null;
        IsRunning = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
        => await StopAsync().ConfigureAwait(false);

    private async Task RunHealthLoopAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        while (_timer is not null && await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await PublishHealthAsync(startedAt, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishHealthSnapshotsAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _server.PublishSnapshotAsync(
            item: new ItemModel("State", "Starting").Repath(ItemServerHealthPaths.StatusState),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("UptimeSeconds", 0.0).Repath(ItemServerHealthPaths.StatusUptimeSeconds),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("StartedAtUtc", startedAt.ToString("O")).Repath(ItemServerHealthPaths.StatusStartedAtUtc),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("LastUpdatedUtc", now.ToString("O")).Repath(ItemServerHealthPaths.StatusLastUpdatedUtc),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("ItemCount", _retainedItemCountProvider()).Repath(ItemServerHealthPaths.MetricsItemCount),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("MemoryWorkingSetMb", 0.0).Repath(ItemServerHealthPaths.MetricsMemoryWorkingSetMb),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("MemoryManagedHeapMb", 0.0).Repath(ItemServerHealthPaths.MetricsMemoryManagedHeapMb),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.PublishSnapshotAsync(
            item: new ItemModel("CpuUsagePercent", 0.0).Repath(ItemServerHealthPaths.MetricsCpuUsagePercent),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishHealthAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        using var process = Process.GetCurrentProcess();

        await _server.UpdateValueAsync(
            item: new ItemModel("State", "Running").Repath(ItemServerHealthPaths.StatusState),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("UptimeSeconds", (now - startedAt).TotalSeconds).Repath(ItemServerHealthPaths.StatusUptimeSeconds),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("StartedAtUtc", startedAt.ToString("O")).Repath(ItemServerHealthPaths.StatusStartedAtUtc),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("LastUpdatedUtc", now.ToString("O")).Repath(ItemServerHealthPaths.StatusLastUpdatedUtc),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("ItemCount", _retainedItemCountProvider()).Repath(ItemServerHealthPaths.MetricsItemCount),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("MemoryWorkingSetMb", ConvertBytesToMegabytes(process.WorkingSet64)).Repath(ItemServerHealthPaths.MetricsMemoryWorkingSetMb),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("MemoryManagedHeapMb", ConvertBytesToMegabytes(GC.GetTotalMemory(forceFullCollection: false))).Repath(ItemServerHealthPaths.MetricsMemoryManagedHeapMb),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _server.UpdateValueAsync(
            item: new ItemModel("CpuUsagePercent", GetCpuUsagePercent(process, now)).Repath(ItemServerHealthPaths.MetricsCpuUsagePercent),
            retained: true,
            sourceClientId: _options.ClientId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private double GetCpuUsagePercent(Process process, DateTimeOffset sampleUtc)
    {
        var processorTime = process.TotalProcessorTime;
        var elapsedSeconds = (sampleUtc - _lastCpuSampleUtc).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return _lastCpuUsagePercent;
        }

        var processorSeconds = (processorTime - _lastProcessorTime).TotalSeconds;
        var usagePercent = processorSeconds / (elapsedSeconds * Environment.ProcessorCount) * 100.0;
        _lastProcessorTime = processorTime;
        _lastCpuSampleUtc = sampleUtc;
        _lastCpuUsagePercent = Math.Clamp(usagePercent, min: 0.0, max: 100.0);
        return _lastCpuUsagePercent;
    }

    private static double ConvertBytesToMegabytes(long bytes)
        => bytes / BytesPerMegabyte;

    private static Func<int> CreateRetainedItemCountProvider(IItemServer server)
        => server is InMemoryItemServer inMemoryServer
            ? () => inMemoryServer.RetainedItemCount
            : static () => 0;
}