using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amium.Items;
using HornetStudio.Host.Registries;
using HornetStudio.Logging;

namespace HornetStudio.Host.Logging.Values;

/// <summary>
/// Defines the supported value log backends.
/// </summary>
public enum ValueLogKind
{
    Csv,
    Sql
}

/// <summary>
/// Defines one folder-local value log source binding.
/// </summary>
public sealed record ValueLogSourceDefinition
{
    public string Name { get; init; } = string.Empty;

    public string TargetPath { get; init; } = string.Empty;

    public string Caption { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public int? IntervalMs { get; init; }
}

/// <summary>
/// Defines one folder-local CSV or SQL value log.
/// </summary>
public sealed record ValueLogDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public ValueLogKind Kind { get; init; }

    public bool Enabled { get; init; } = true;

    public bool AutoStart { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public int IntervalMs { get; init; } = 1000;

    public bool SplitDaily { get; init; }

    public string SplitDailyTime { get; init; } = "00:00:00";

    public int SplitMaxFileSizeMb { get; init; }

    public string PersistenceMode { get; init; } = "Balanced";

    public int FlushIntervalMs { get; init; }

    public int FlushBatchSize { get; init; }

    public IReadOnlyList<ValueLogSourceDefinition> Sources { get; init; } = Array.Empty<ValueLogSourceDefinition>();
}

/// <summary>
/// Owns folder-scoped CSV and SQL value loggers independently from Avalonia controls.
/// </summary>
public static class LogManager
{
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, Task> PendingFolderOperations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, ValueLogEntry>> EntriesByFolder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Synchronizes folder-local value log definitions with host-owned loggers.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    /// <param name="definitions">The folder-local definitions.</param>
    /// <returns>The active value log statuses for the folder.</returns>
    public static IReadOnlyList<ValueLogStatus> SyncDefinitions(string folderName, IEnumerable<ValueLogDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definitions);

        var normalizedFolderName = folderName.Trim();
        var definitionList = definitions
            .Where(static definition => definition is not null && !string.IsNullOrWhiteSpace(definition.Id))
            .Select(static definition => definition with
            {
                Sources = definition.Sources.Select(source => source with { }).ToArray()
            })
            .GroupBy(static definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<ValueLogEntry> entriesToStop = [];
        List<ValueLogEntry> entriesToStart = [];
        List<ValueLogStatus> activeStatuses;

        lock (Sync)
        {
            if (!EntriesByFolder.TryGetValue(normalizedFolderName, out var entriesById))
            {
                entriesById = new Dictionary<string, ValueLogEntry>(StringComparer.OrdinalIgnoreCase);
                EntriesByFolder[normalizedFolderName] = entriesById;
            }

            foreach (var staleId in entriesById.Keys.Where(id => definitionList.All(definition => !string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase))).ToArray())
            {
                entriesToStop.Add(entriesById[staleId]);
                entriesById.Remove(staleId);
            }

            foreach (var definition in definitionList)
            {
                if (!entriesById.TryGetValue(definition.Id, out var entry))
                {
                    entry = new ValueLogEntry(normalizedFolderName, definition, QueueRunRequest);
                    entriesById[definition.Id] = entry;
                }
                else
                {
                    if (entry.Definition.Kind != definition.Kind)
                    {
                        entriesToStop.Add(entry);
                        entry.RemoveProjection();
                        entry = new ValueLogEntry(normalizedFolderName, definition, QueueRunRequest);
                        entriesById[definition.Id] = entry;
                    }
                    else
                    {
                        entry.UpdateDefinition(definition);
                    }
                }

                if (!definition.Enabled && entry.IsRunning)
                {
                    entriesToStop.Add(entry);
                }
                else if (definition.Enabled && definition.AutoStart && !entry.IsRunning)
                {
                    entriesToStart.Add(entry);
                }
            }

            activeStatuses = entriesById.Values
                .OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.GetStatus())
                .ToList();
        }

        QueueFolderOperations(
            normalizedFolderName,
            entriesToStop.Distinct().Select(entry => new ValueLogOperation(entry, OperationKind.Stop, RemoveProjectionAfterStop: !definitionList.Any(definition => string.Equals(definition.Id, entry.Id, StringComparison.OrdinalIgnoreCase))))
                .Concat(entriesToStart.Distinct().Select(entry => new ValueLogOperation(entry, OperationKind.Start, RemoveProjectionAfterStop: false)))
                .ToArray());

        if (definitionList.Length == 0)
        {
            ReleaseFolder(normalizedFolderName);
            return Array.Empty<ValueLogStatus>();
        }

        return activeStatuses;
    }

    /// <summary>
    /// Attempts to resolve the status of one active value log by folder and id.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    /// <param name="valueLogId">The value log id.</param>
    /// <param name="status">The resolved status when available.</param>
    /// <returns><see langword="true"/> when the value log exists; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetStatus(string folderName, string valueLogId, out ValueLogStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(valueLogId))
        {
            return false;
        }

        lock (Sync)
        {
            if (EntriesByFolder.TryGetValue(folderName.Trim(), out var entriesById)
                && entriesById.TryGetValue(valueLogId.Trim(), out var resolved))
            {
                status = resolved.GetStatus();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Releases all value loggers and projections for one folder.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    public static void ReleaseFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        Dictionary<string, ValueLogEntry>? entriesById = null;
        var normalizedFolderName = folderName.Trim();

        lock (Sync)
        {
            if (EntriesByFolder.TryGetValue(normalizedFolderName, out var existing))
            {
                entriesById = existing;
                EntriesByFolder.Remove(normalizedFolderName);
            }
        }

        if (entriesById is null)
        {
            return;
        }

        QueueFolderOperations(
            normalizedFolderName,
            entriesById.Values.Select(entry => new ValueLogOperation(entry, OperationKind.Stop, RemoveProjectionAfterStop: true)).ToArray());
    }

    private static void QueueRunRequest(ValueLogEntry entry, bool run)
    {
        ArgumentNullException.ThrowIfNull(entry);

        QueueFolderOperations(
            entry.FolderName,
            [new ValueLogOperation(entry, run ? OperationKind.Start : OperationKind.Stop, RemoveProjectionAfterStop: false)]);
    }

    private static void QueueFolderOperations(string folderName, IReadOnlyList<ValueLogOperation> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        PendingFolderOperations.AddOrUpdate(
            folderName,
            _ => Task.Run(() => RunFolderOperationsAsync(folderName, operations)),
            (_, previous) => ContinueFolderOperations(previous, folderName, operations));
    }

    private static Task ContinueFolderOperations(Task previous, string folderName, IReadOnlyList<ValueLogOperation> operations)
    {
        return previous.ContinueWith(
            static async (antecedent, state) =>
            {
                var (name, queuedOperations) = ((string, IReadOnlyList<ValueLogOperation>))state!;
                try
                {
                    await antecedent.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    HostLogger.Log.Error(ex, "[LogManager] Previous ValueLog folder operation failed Folder={FolderName}.", name);
                }

                await RunFolderOperationsAsync(name, queuedOperations).ConfigureAwait(false);
            },
            (folderName, operations),
            TaskScheduler.Default).Unwrap();
    }

    private static async Task RunFolderOperationsAsync(string folderName, IReadOnlyList<ValueLogOperation> operations)
    {
        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case OperationKind.Start:
                    try
                    {
                        await operation.Entry.StartAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        HostLogger.Log.Error(ex, "[LogManager] Failed to start ValueLog logger Folder={FolderName} ValueLogId={ValueLogId}.", folderName, operation.Entry.Id);
                    }

                    break;
                case OperationKind.Stop:
                    try
                    {
                        await operation.Entry.StopAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        HostLogger.Log.Error(ex, "[LogManager] Failed to stop ValueLog logger Folder={FolderName} ValueLogId={ValueLogId}.", folderName, operation.Entry.Id);
                    }

                    if (operation.RemoveProjectionAfterStop)
                    {
                        operation.Entry.RemoveProjection();
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unsupported logger operation kind '{operation.Kind}'.");
            }
        }
    }

    private enum OperationKind
    {
        Start,
        Stop
    }

    private readonly record struct ValueLogOperation(ValueLogEntry Entry, OperationKind Kind, bool RemoveProjectionAfterStop);

    private sealed class ValueLogEntry
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly Action<ValueLogEntry, bool> _queueRunRequest;
        private readonly string _runPath;
        private readonly string _outputDirectoryPath;
        private readonly string _filenamePath;
        private ValueLogDefinition _definition;
        private string _status = "Stopped";
        private string _lastError = string.Empty;
        private string _lastFile = string.Empty;
        private CsvLogger? _csvLogger;
        private SqlLogger? _sqlLogger;

        public ValueLogEntry(string folderName, ValueLogDefinition definition, Action<ValueLogEntry, bool> queueRunRequest)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(queueRunRequest);

            _queueRunRequest = queueRunRequest;
            FolderName = folderName.Trim();
            _definition = CloneDefinition(definition);
            RuntimePath = $"studio.{HostPathSegmentNormalizer.Normalize(FolderName)}.value_log.{HostPathSegmentNormalizer.Normalize(_definition.Id)}";
            _runPath = $"{RuntimePath}.run";
            _outputDirectoryPath = $"{RuntimePath}.output_directory";
            _filenamePath = $"{RuntimePath}.filename";
            HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
            PublishSnapshot();
        }

        public string Id => Definition.Id;

        public string FolderName { get; }

        public string RuntimePath { get; }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _definition.Kind switch
                    {
                        ValueLogKind.Csv => _csvLogger?.Running == true,
                        ValueLogKind.Sql => _sqlLogger?.Running == true,
                        _ => false
                    };
                }
            }
        }

        public ValueLogDefinition Definition
        {
            get
            {
                lock (_sync)
                {
                    return CloneDefinition(_definition);
                }
            }
        }

        public ValueLogStatus GetStatus()
        {
            lock (_sync)
            {
                return new ValueLogStatus(
                    Id,
                    RuntimePath,
                    CloneDefinition(_definition),
                    IsRunning,
                    _status,
                    _lastError,
                    _lastFile);
            }
        }

        public void UpdateDefinition(ValueLogDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            lock (_sync)
            {
                _definition = CloneDefinition(definition);
            }

            PublishSnapshot();
        }

        public async Task StartAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var definition = Definition;
                if (!definition.Enabled)
                {
                    UpdateStatus(status: "Disabled", lastError: string.Empty, lastFile: definition.OutputPath);
                    return;
                }

                if (definition.Sources.Count == 0)
                {
                    UpdateStatus(status: "Error", lastError: "ValueLog does not contain any configured sources.", lastFile: definition.OutputPath);
                    return;
                }

                await StopBackendAsync().ConfigureAwait(false);

                try
                {
                    await Task.Run(() => StartBackend(definition)).ConfigureAwait(false);
                    UpdateStatus(status: "Running", lastError: string.Empty, lastFile: GetCurrentLastFilePath(definition.OutputPath));
                }
                catch (Exception ex)
                {
                    await StopBackendAsync().ConfigureAwait(false);
                    UpdateStatus(status: "Error", lastError: ex.Message, lastFile: definition.OutputPath);
                    throw;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopBackendAsync().ConfigureAwait(false);

                lock (_sync)
                {
                    if (_status != "Error")
                    {
                        _status = "Stopped";
                    }
                }

                PublishSnapshot();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public void RemoveProjection()
        {
            HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
            HostRegistries.Data.Remove(RuntimePath);
        }

        private void StartBackend(ValueLogDefinition definition)
        {
            switch (definition.Kind)
            {
                case ValueLogKind.Csv:
                {
                    var logger = new CsvLogger(definition.Id)
                    {
                        Interval = Math.Max(1, definition.IntervalMs),
                        SplitDaily = definition.SplitDaily,
                        SplitDailyTime = string.IsNullOrWhiteSpace(definition.SplitDailyTime) ? "00:00:00" : definition.SplitDailyTime.Trim(),
                        SplitMaxFileSizeMb = Math.Max(0, definition.SplitMaxFileSizeMb),
                        PersistenceMode = string.IsNullOrWhiteSpace(definition.PersistenceMode) ? "Balanced" : definition.PersistenceMode.Trim(),
                        FlushIntervalMs = Math.Max(0, definition.FlushIntervalMs),
                        FlushBatchSize = Math.Max(0, definition.FlushBatchSize)
                    };

                    foreach (var source in definition.Sources)
                    {
                        var sourceItems = ResolveRequiredSourceItems(folderName: FolderName, definition: definition, source: source);
                        logger.Add(
                            name: ResolveSourceName(source, sourceItems.MetadataItem),
                            unit: ReadItemStringProperty(sourceItems.MetadataItem, "unit"),
                            format: ReadItemStringProperty(sourceItems.MetadataItem, "format"),
                            value: () => ReadItemValue(sourceItems.ValueItem),
                            caption: ReadItemStringProperty(sourceItems.MetadataItem, "text"));
                    }

                    logger.Start(definition.OutputPath, interval: Math.Max(1, definition.IntervalMs));
                    lock (_sync)
                    {
                        _csvLogger = logger;
                        _sqlLogger = null;
                    }

                    break;
                }
                case ValueLogKind.Sql:
                {
                    var logger = new SqlLogger(definition.Id)
                    {
                        File = definition.OutputPath,
                        Directory = Path.GetDirectoryName(definition.OutputPath) ?? string.Empty,
                        SplitDaily = definition.SplitDaily,
                        SplitDailyTime = string.IsNullOrWhiteSpace(definition.SplitDailyTime) ? "00:00:00" : definition.SplitDailyTime.Trim(),
                        SplitMaxFileSizeMb = Math.Max(0, definition.SplitMaxFileSizeMb),
                        PersistenceMode = string.IsNullOrWhiteSpace(definition.PersistenceMode) ? "Balanced" : definition.PersistenceMode.Trim(),
                        FlushIntervalMs = Math.Max(0, definition.FlushIntervalMs),
                        FlushBatchSize = Math.Max(0, definition.FlushBatchSize)
                    };

                    foreach (var source in definition.Sources)
                    {
                        var sourceItems = ResolveRequiredSourceItems(folderName: FolderName, definition: definition, source: source);
                        var sourceInterval = Math.Max(1, source.IntervalMs ?? definition.IntervalMs);
                        logger.Add(
                            name: ResolveSourceName(source, sourceItems.MetadataItem),
                            text: ReadItemStringProperty(sourceItems.MetadataItem, "text"),
                            unit: ReadItemStringProperty(sourceItems.MetadataItem, "unit"),
                            format: ReadItemStringProperty(sourceItems.MetadataItem, "format"),
                            period: sourceInterval,
                            value: () => ReadItemValue(sourceItems.ValueItem));
                    }

                    logger.Start();
                    lock (_sync)
                    {
                        _csvLogger = null;
                        _sqlLogger = logger;
                    }

                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported value log kind '{definition.Kind}'.");
            }
        }

        private async Task StopBackendAsync()
        {
            CsvLogger? csvLogger;
            SqlLogger? sqlLogger;
            lock (_sync)
            {
                csvLogger = _csvLogger;
                sqlLogger = _sqlLogger;
                _csvLogger = null;
                _sqlLogger = null;
            }

            if (csvLogger is not null)
            {
                await csvLogger.Stop().ConfigureAwait(false);
            }

            if (sqlLogger is not null)
            {
                await sqlLogger.Stop().ConfigureAwait(false);
            }
        }

        private string GetCurrentLastFilePath(string fallback)
        {
            lock (_sync)
            {
                return _definition.Kind switch
                {
                    ValueLogKind.Csv => string.IsNullOrWhiteSpace(_csvLogger?.LastFilePath) ? fallback : _csvLogger.LastFilePath,
                    ValueLogKind.Sql => string.IsNullOrWhiteSpace(_sqlLogger?.LastFilePath) ? fallback : _sqlLogger.LastFilePath,
                    _ => fallback
                };
            }
        }

        private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
        {
            if (e.ChangeKind is not (DataChangeKind.ValueUpdated or DataChangeKind.PropertyUpdated))
            {
                return;
            }

            var normalizedRuntimePath = NormalizeConfiguredPath(RuntimePath);
            if (!NormalizeConfiguredPath(e.Key).StartsWith(normalizedRuntimePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsRegistryItemChange(e, _runPath))
            {
                if (TryReadBooleanRequest(e.ItemModel, out var run))
                {
                    MirrorWriteProperty(e.ItemModel, run);
                    _queueRunRequest(this, run);
                }

                return;
            }

            if (IsRegistryItemChange(e, _outputDirectoryPath))
            {
                UpdateOutputDirectory(ReadStringRequest(e.ItemModel));
                return;
            }

            if (IsRegistryItemChange(e, _filenamePath))
            {
                UpdateFilename(ReadStringRequest(e.ItemModel));
            }
        }

        private void UpdateStatus(string status, string lastError, string lastFile)
        {
            lock (_sync)
            {
                _status = status;
                _lastError = lastError ?? string.Empty;
                _lastFile = lastFile ?? string.Empty;
            }

            PublishSnapshot();
        }

        private void PublishSnapshot()
        {
            ValueLogDefinition definition;
            string status;
            string lastError;
            string lastFile;

            lock (_sync)
            {
                definition = CloneDefinition(_definition);
                status = _status;
                lastError = _lastError;
                lastFile = _lastFile;
            }

            HostRegistries.Data.UpsertSnapshot(
                RuntimePath,
                BuildSnapshotItem(definition, IsRunning, status, lastError, lastFile),
                DataRegistryItemMetadata.WidgetStatus(),
                pruneMissingMembers: true);
        }

        private Item BuildSnapshotItem(ValueLogDefinition definition, bool isRunning, string status, string lastError, string lastFile)
        {
            var item = new Item(
                HostPathSegmentNormalizer.Normalize(definition.Id),
                status,
                $"studio.{HostPathSegmentNormalizer.Normalize(FolderName)}.value_log");
            item.Properties["kind"].Value = "ValueLog";
            item.Properties["backend"].Value = definition.Kind.ToString();
            item.Properties["text"].Value = string.IsNullOrWhiteSpace(definition.Text) ? definition.Id : definition.Text.Trim();
            item.Properties["enabled"].Value = definition.Enabled;
            item.Properties["auto_start"].Value = definition.AutoStart;
            item.Properties["is_running"].Value = isRunning;
            item.Properties["status"].Value = status;
            item.Properties["output_path"].Value = definition.OutputPath;
            item.Properties["last_file"].Value = string.IsNullOrWhiteSpace(lastFile) ? definition.OutputPath : lastFile;
            item.Properties["error"].Value = lastError;
            item.Properties["interval_ms"].Value = Math.Max(1, definition.IntervalMs);
            item.Properties["source_count"].Value = definition.Sources.Count;

            var outputDirectory = Path.GetDirectoryName(definition.OutputPath) ?? string.Empty;
            var filename = Path.GetFileName(definition.OutputPath);

            item["run"].Value = isRunning;
            item["run"].Properties["kind"].Value = "ValueLogRun";
            item["run"].Properties["text"].Value = "Run";
            item["run"].Properties["write"].Value = isRunning;

            item["status"].Value = status;
            item["status"].Properties["kind"].Value = "ValueLogStatus";
            item["status"].Properties["text"].Value = "Status";

            item["output_directory"].Value = outputDirectory;
            item["output_directory"].Properties["kind"].Value = "ValueLogOutputDirectory";
            item["output_directory"].Properties["text"].Value = "OutputDirectory";
            item["output_directory"].Properties["write"].Value = outputDirectory;

            item["filename"].Value = filename;
            item["filename"].Properties["kind"].Value = "ValueLogFilename";
            item["filename"].Properties["text"].Value = "Filename";
            item["filename"].Properties["write"].Value = filename;

            item["last_file"].Value = string.IsNullOrWhiteSpace(lastFile) ? definition.OutputPath : lastFile;
            item["last_file"].Properties["kind"].Value = "ValueLogLastFile";
            item["last_file"].Properties["text"].Value = "LastFile";

            item["error"].Value = lastError;
            item["error"].Properties["kind"].Value = "ValueLogError";
            item["error"].Properties["text"].Value = "Error";

            return item;
        }

        private void UpdateOutputDirectory(string outputDirectory)
        {
            lock (_sync)
            {
                var definition = CloneDefinition(_definition);
                var filename = Path.GetFileName(definition.OutputPath);
                _definition = definition with
                {
                    OutputPath = BuildOutputPath(outputDirectory, filename)
                };
            }

            PublishSnapshot();
        }

        private void UpdateFilename(string filename)
        {
            lock (_sync)
            {
                var definition = CloneDefinition(_definition);
                var outputDirectory = Path.GetDirectoryName(definition.OutputPath) ?? string.Empty;
                _definition = definition with
                {
                    OutputPath = BuildOutputPath(outputDirectory, filename)
                };
            }

            PublishSnapshot();
        }

        private bool IsRegistryItemChange(DataChangedEventArgs e, string itemPath)
        {
            if (PathsEqual(e.Key, itemPath))
            {
                return true;
            }

            return (HostRegistries.Data.TryResolve(itemPath, out var item)
                    && item is not null
                    && ReferenceEquals(e.ItemModel, item))
                   || IsNamedRuntimeChildChange(e, itemPath);
        }

        private bool IsNamedRuntimeChildChange(DataChangedEventArgs e, string itemPath)
        {
            var normalizedEventPath = NormalizeConfiguredPath(e.Key);
            if (!normalizedEventPath.StartsWith(NormalizeConfiguredPath(RuntimePath), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var expectedName = NormalizeConfiguredPath(itemPath)
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault() ?? string.Empty;
            return string.Equals(HostPathSegmentNormalizer.Normalize(e.ItemModel.Name), expectedName, StringComparison.OrdinalIgnoreCase);
        }

        private static SourceItems ResolveRequiredSourceItems(string folderName, ValueLogDefinition definition, ValueLogSourceDefinition source)
        {
            foreach (var candidatePath in EnumerateResolutionCandidates(folderName, source.TargetPath))
            {
                if (HostRegistries.Data.TryResolve(candidatePath, out var item) && item is not null)
                {
                    return ResolveSourceItems(candidatePath, item);
                }
            }

            throw new InvalidOperationException(
                $"ValueLog source '{ResolveSourceName(source)}' could not resolve target '{source.TargetPath}' in folder '{definition.Id}'.");
        }

        private static IEnumerable<string> EnumerateResolutionCandidates(string folderName, string targetPath)
        {
            var normalized = NormalizeConfiguredPath(targetPath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                yield break;
            }

            yield return normalized;

            if (!normalized.StartsWith("studio.", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"studio.{HostPathSegmentNormalizer.Normalize(folderName)}.{normalized}";
            }
        }

        private static string ResolveSourceName(ValueLogSourceDefinition source)
        {
            if (!string.IsNullOrWhiteSpace(source.Name))
            {
                return source.Name.Trim();
            }

            var fileName = Path.GetFileName(source.TargetPath?.Trim().Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return HostPathSegmentNormalizer.Normalize(fileName);
            }

            return "value";
        }

        private static string ResolveSourceName(ValueLogSourceDefinition source, Item item)
            => !string.IsNullOrWhiteSpace(item.Name) ? item.Name.Trim() : ResolveSourceName(source);

        private static string ReadItemStringProperty(Item item, string propertyName)
        {
            if (TryReadItemProperty(item, propertyName, out var value))
            {
                return value?.ToString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private static object ReadItemValue(Item item)
        {
            if (item.Properties.Has("read"))
            {
                return item.Properties["read"].Value;
            }

            if (item.Properties.Has("value"))
            {
                return item.Properties["value"].Value;
            }

            return item.Value ?? string.Empty;
        }

        private static SourceItems ResolveSourceItems(string resolvedPath, Item resolvedItem)
        {
            if (string.Equals(HostPathSegmentNormalizer.Normalize(resolvedItem.Name), "read", StringComparison.OrdinalIgnoreCase)
                && TryResolveParentItem(resolvedPath, out var parentItem))
            {
                return new SourceItems(parentItem, resolvedItem);
            }

            if (TryResolveReadChild(resolvedPath, resolvedItem, out var readItem))
            {
                return new SourceItems(resolvedItem, readItem);
            }

            return new SourceItems(resolvedItem, resolvedItem);
        }

        private static bool TryResolveReadChild(string resolvedPath, Item resolvedItem, out Item readItem)
        {
            if (resolvedItem.Has("read"))
            {
                readItem = resolvedItem["read"];
                return true;
            }

            var readPath = $"{NormalizeConfiguredPath(resolvedPath)}.read";
            if (HostRegistries.Data.TryResolve(readPath, out var registryReadItem) && registryReadItem is not null)
            {
                readItem = registryReadItem;
                return true;
            }

            readItem = resolvedItem;
            return false;
        }

        private static bool TryResolveParentItem(string resolvedPath, out Item parentItem)
        {
            var normalizedPath = NormalizeConfiguredPath(resolvedPath);
            var lastSeparator = normalizedPath.LastIndexOf('.');
            if (lastSeparator <= 0)
            {
                parentItem = null!;
                return false;
            }

            var parentPath = normalizedPath[..lastSeparator];
            if (HostRegistries.Data.TryResolve(parentPath, out var resolvedParent) && resolvedParent is not null)
            {
                parentItem = resolvedParent;
                return true;
            }

            parentItem = null!;
            return false;
        }

        private static ValueLogDefinition CloneDefinition(ValueLogDefinition definition)
        {
            return definition with
            {
                Sources = definition.Sources
                    .Select(source => source with { })
                    .ToArray()
            };
        }

        private static string NormalizeConfiguredPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var segments = value
                .Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(HostPathSegmentNormalizer.Normalize)
                .Where(static segment => !string.IsNullOrWhiteSpace(segment));
            return string.Join('.', segments);
        }

        private static string BuildOutputPath(string outputDirectory, string filename)
        {
            var directory = outputDirectory?.Trim() ?? string.Empty;
            var file = filename?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(file))
            {
                return directory;
            }

            return string.IsNullOrWhiteSpace(directory) ? file : Path.Combine(directory, file);
        }

        private static bool PathsEqual(string? left, string? right)
            => string.Equals(NormalizeConfiguredPath(left), NormalizeConfiguredPath(right), StringComparison.OrdinalIgnoreCase);

        private static bool TryReadBooleanRequest(Item item, out bool value)
        {
            if (item.Properties.Has("write"))
            {
                return TryConvertBoolean(item.Properties["write"].Value, out value);
            }

            if (TryConvertBoolean(item.Value, out value))
            {
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryConvertBoolean(object? rawValue, out bool value)
        {
            switch (rawValue)
            {
                case bool boolValue:
                    value = boolValue;
                    return true;
                case string text when bool.TryParse(text, out var parsedBool):
                    value = parsedBool;
                    return true;
                case string text when int.TryParse(text, out var parsedInt):
                    value = parsedInt != 0;
                    return true;
                case int intValue:
                    value = intValue != 0;
                    return true;
                case long longValue:
                    value = longValue != 0;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        private static string ReadStringRequest(Item item)
        {
            if (item.Properties.Has("write"))
            {
                return item.Properties["write"].Value?.ToString() ?? string.Empty;
            }

            return item.Value?.ToString() ?? string.Empty;
        }

        private static void MirrorWriteProperty(Item item, bool value)
        {
            if (item.Properties.Has("write"))
            {
                item.Properties["write"].Value = value;
            }
        }

        private static bool TryReadItemProperty(Item item, string propertyName, out object? value)
        {
            if (item.Properties.Has(propertyName))
            {
                value = item.Properties[propertyName].Value;
                return true;
            }

            var normalizedName = propertyName.ToLowerInvariant();
            if (item.Properties.Has(normalizedName))
            {
                value = item.Properties[normalizedName].Value;
                return true;
            }

            var titleName = char.ToUpperInvariant(normalizedName[0]) + normalizedName[1..];
            if (item.Properties.Has(titleName))
            {
                value = item.Properties[titleName].Value;
                return true;
            }

            value = null;
            return false;
        }

        private readonly record struct SourceItems(Item MetadataItem, Item ValueItem);
    }
}

/// <summary>
/// Describes the current manager-owned value log state.
/// </summary>
/// <param name="Id">The value log id.</param>
/// <param name="RuntimePath">The registry projection root path.</param>
/// <param name="Definition">The current persisted definition snapshot.</param>
/// <param name="IsRunning">A value indicating whether the concrete logger backend is running.</param>
/// <param name="Status">The current runtime status text.</param>
/// <param name="LastError">The last runtime error text.</param>
/// <param name="LastFile">The last output file path reported by the backend.</param>
public readonly record struct ValueLogStatus(
    string Id,
    string RuntimePath,
    ValueLogDefinition Definition,
    bool IsRunning,
    string Status,
    string LastError,
    string LastFile);
