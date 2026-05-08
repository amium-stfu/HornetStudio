using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Item.Server.Monitor.Hosting;
using Item.Server.Monitor.Monitoring;
using HornetStudio.Host;

namespace Item.Server.Monitor.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly MonitorHost _monitorHost;
    private readonly MonitorItemSource _source;
    private readonly MonitorSnapshotStore _snapshotStore;
    private readonly MonitorTreeStore _treeStore;
    private readonly MonitorUpdateScheduler _scheduler;
    private readonly RelayCommand _refreshNowCommand;
    private readonly RelayCommand _expandAllCommand;
    private readonly RelayCommand _collapseAllCommand;
    private readonly RelayCommand<MonitorTreeRowViewModel> _toggleExpandCommand;
    private string _filterText = string.Empty;
    private bool _isPaused;
    private int _selectedUpdateIntervalMs;
    private string _statusText = "0 visible / 0 items";
    private string _lastRefreshText = "No snapshot applied yet.";
    private MonitorTreeRowViewModel? _selectedRow;
    private string? _selectedRowPath;
    private string _selectedPath = "<none>";
    private string _selectedName = "<none>";
    private string _selectedValueText = "<none>";
    private string _selectedValueType = "<none>";
    private string _selectedUpdatedText = "<none>";
    private bool _isRefreshingVisibleRows;
    private readonly Dictionary<string, MonitorTreeRowViewModel> _visibleRowsByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public MainWindowViewModel()
    {
        _monitorHost = new MonitorHost();
        _source = new MonitorItemSource(HostRegistries.Data);
        _snapshotStore = new MonitorSnapshotStore();
        _treeStore = new MonitorTreeStore();
        _selectedUpdateIntervalMs = 250;
        _scheduler = new MonitorUpdateScheduler(TimeSpan.FromMilliseconds(_selectedUpdateIntervalMs));
        _refreshNowCommand = new RelayCommand(RefreshNow);
        _expandAllCommand = new RelayCommand(ExpandAll);
        _collapseAllCommand = new RelayCommand(CollapseAll);
        _toggleExpandCommand = new RelayCommand<MonitorTreeRowViewModel>(ToggleExpand, static row => row is not null && row.HasChildren);

        UpdateIntervalOptions = [100, 250, 500, 1000, 2000];
        Adapters = new ObservableCollection<MonitorAdapterViewModel>(_monitorHost.Adapters.Select(adapter => new MonitorAdapterViewModel(_monitorHost, adapter)));
        _source.SourceInvalidated += OnSourceInvalidated;
        _ = _monitorHost.InitializeAsync();
        RefreshNow();
        _treeStore.ExpandAll();
        RefreshVisibleRows();
    }

    public ObservableCollection<MonitorTreeRowViewModel> VisibleRows { get; } = [];

    public ObservableCollection<MonitorAdapterViewModel> Adapters { get; }

    public ObservableCollection<MonitorPropertyRowViewModel> SelectedProperties { get; } = [];

    public IReadOnlyList<int> UpdateIntervalOptions { get; }

    public RelayCommand RefreshNowCommand => _refreshNowCommand;

    public RelayCommand ExpandAllCommand => _expandAllCommand;

    public RelayCommand CollapseAllCommand => _collapseAllCommand;

    public RelayCommand<MonitorTreeRowViewModel> ToggleExpandCommand => _toggleExpandCommand;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                RefreshVisibleRows();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (!SetProperty(ref _isPaused, value))
            {
                return;
            }

            if (!_isPaused)
            {
                RefreshNow();
            }
            else
            {
                StatusText = $"{VisibleRows.Count} visible / {_snapshotStore.Count} items (frozen)";
            }
        }
    }

    public int SelectedUpdateIntervalMs
    {
        get => _selectedUpdateIntervalMs;
        set
        {
            if (!SetProperty(ref _selectedUpdateIntervalMs, value))
            {
                return;
            }

            _scheduler.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetProperty(ref _lastRefreshText, value);
    }

    public MonitorTreeRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (_isRefreshingVisibleRows && value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedRow, value))
            {
                foreach (var row in VisibleRows)
                {
                    row.IsSelected = ReferenceEquals(row, value);
                }

                _selectedRowPath = value?.Path ?? _selectedRowPath;
                UpdateSelectionDetails(value?.Path);
            }
        }
    }

    public string SelectedPath
    {
        get => _selectedPath;
        private set => SetProperty(ref _selectedPath, value);
    }

    public string SelectedName
    {
        get => _selectedName;
        private set => SetProperty(ref _selectedName, value);
    }

    public string SelectedValueText
    {
        get => _selectedValueText;
        private set => SetProperty(ref _selectedValueText, value);
    }

    public string SelectedValueType
    {
        get => _selectedValueType;
        private set => SetProperty(ref _selectedValueType, value);
    }

    public string SelectedUpdatedText
    {
        get => _selectedUpdatedText;
        private set => SetProperty(ref _selectedUpdatedText, value);
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.SourceInvalidated -= OnSourceInvalidated;
        _source.Dispose();
        _scheduler.Dispose();
        foreach (var adapter in Adapters)
        {
            adapter.Dispose();
        }

        await _monitorHost.DisposeAsync().ConfigureAwait(false);
    }

    private void OnSourceInvalidated(object? sender, EventArgs e)
    {
        if (_disposed || IsPaused)
        {
            return;
        }

        _scheduler.Request(RefreshNow);
    }

    private void RefreshNow()
    {
        if (_disposed)
        {
            return;
        }

        _snapshotStore.ReplaceAll(_source.CaptureSnapshot());
        RefreshVisibleRows();
        LastRefreshText = $"Last refresh: {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ({SelectedUpdateIntervalMs} ms throttle)";
    }

    private void RefreshVisibleRows()
    {
        var rows = _treeStore.BuildVisibleRows(_snapshotStore.Items, FilterText);
        var selectionPath = _selectedRowPath;

        _isRefreshingVisibleRows = true;
        try
        {
            SyncVisibleRows(rows);
            var restoredSelection = !string.IsNullOrWhiteSpace(selectionPath)
                ? VisibleRows.FirstOrDefault(row => string.Equals(row.Path, selectionPath, StringComparison.OrdinalIgnoreCase))
                : null;

            if (restoredSelection is not null)
            {
                SelectedRow = restoredSelection;
            }
            else if (string.IsNullOrWhiteSpace(selectionPath))
            {
                SelectedRow = VisibleRows.FirstOrDefault();
            }
            else
            {
                _selectedRowPath = null;
                SelectedRow = null;
            }
        }
        finally
        {
            _isRefreshingVisibleRows = false;
        }

        StatusText = $"{VisibleRows.Count} visible / {_snapshotStore.Count} items{(IsPaused ? " (frozen)" : string.Empty)}";
    }

    private void SyncVisibleRows(IReadOnlyList<MonitorTreeRowViewModel> sourceRows)
    {
        var nextRows = new List<MonitorTreeRowViewModel>(sourceRows.Count);
        var nextPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRow in sourceRows)
        {
            nextPaths.Add(sourceRow.Path);
            if (!_visibleRowsByPath.TryGetValue(sourceRow.Path, out var existingRow))
            {
                existingRow = new MonitorTreeRowViewModel(
                    path: sourceRow.Path,
                    displayName: sourceRow.DisplayName,
                    valueText: sourceRow.ValueText,
                    depth: sourceRow.Depth,
                    hasChildren: sourceRow.HasChildren,
                    isExpanded: sourceRow.IsExpanded,
                    hasSnapshot: sourceRow.HasSnapshot);
                _visibleRowsByPath[sourceRow.Path] = existingRow;
            }
            else
            {
                existingRow.UpdateFrom(sourceRow);
            }

            nextRows.Add(existingRow);
        }

        foreach (var stalePath in _visibleRowsByPath.Keys.Where(path => !nextPaths.Contains(path)).ToArray())
        {
            _visibleRowsByPath.Remove(stalePath);
        }

        SyncCollection(VisibleRows, nextRows);
    }

    private void ToggleExpand(MonitorTreeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _treeStore.ToggleExpanded(row.Path);
        RefreshVisibleRows();
    }

    private void ExpandAll()
    {
        _treeStore.ExpandAll();
        RefreshVisibleRows();
    }

    private void CollapseAll()
    {
        _treeStore.CollapseAll();
        RefreshVisibleRows();
    }

    private void UpdateSelectionDetails(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SelectedPath = "<none>";
            SelectedName = "<none>";
            SelectedValueText = "<none>";
            SelectedValueType = "<none>";
            SelectedUpdatedText = "<none>";
            SelectedProperties.Clear();
            return;
        }

        SelectedPath = path;
        if (!_snapshotStore.TryGet(path, out var snapshot) || snapshot is null)
        {
            SelectedName = path.Split('.').LastOrDefault() ?? path;
            SelectedValueText = "<branch>";
            SelectedValueType = "Path segment";
            SelectedUpdatedText = "<none>";
            SelectedProperties.Clear();
            return;
        }

        SelectedName = snapshot.ItemModel.Name ?? path.Split('.').LastOrDefault() ?? path;
        SelectedValueText = MonitorValueFormatter.Format(snapshot.ItemModel.Value);
        SelectedValueType = snapshot.ItemModel.Value?.GetType().Name ?? "null";
        SelectedUpdatedText = snapshot.Timestamp == 0
            ? "<unknown>"
            : DateTimeOffset.FromUnixTimeMilliseconds((long)snapshot.Timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        var properties = snapshot.ItemModel.Properties.GetDictionary()
            .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => new MonitorPropertyRowViewModel(entry.Key, MonitorValueFormatter.Format(entry.Value.Value)))
            .ToArray();

        ReplaceCollection(SelectedProperties, properties);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyCollection<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        var index = 0;
        while (index < source.Count)
        {
            if (index >= target.Count)
            {
                target.Add(source[index]);
                index++;
                continue;
            }

            if (ReferenceEquals(target[index], source[index]))
            {
                index++;
                continue;
            }

            var existingIndex = IndexOfReference(target, source[index], startIndex: index + 1);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
            }
            else
            {
                target.Insert(index, source[index]);
            }

            index++;
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static int IndexOfReference<T>(IList<T> items, T value, int startIndex)
    {
        for (var index = startIndex; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
