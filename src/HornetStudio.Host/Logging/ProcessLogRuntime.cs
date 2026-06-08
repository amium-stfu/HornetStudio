using System.Collections.Concurrent;
using Amium.Items;
using HornetStudio.Host;
using HornetStudio.Host.Registries;
using Serilog.Events;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Logging;

/// <summary>
/// Publishes process logs together with writable level input items.
/// </summary>
public static class ProcessLogRuntime
{
    private static readonly string[] LevelItemNames = ["debug", "info", "warning", "error", "fatal"];
    private static readonly ConcurrentDictionary<string, ProcessLog> Logs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, LogInputBinding> InputBindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures that a process log and its writable level input items are published at the requested path.
    /// </summary>
    /// <param name="path">The process log root path.</param>
    /// <param name="title">The optional display title.</param>
    /// <param name="logDirectory">The optional log file directory.</param>
    /// <returns>The normalized process log root path.</returns>
    public static string EnsurePublished(string path, string? title = null, string? logDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("Process log path must contain at least one valid segment.", nameof(path));
        }

        var targetLogDirectory = GetLogDirectory(normalizedPath, logDirectory);
        var processLog = Logs.GetOrAdd(normalizedPath, _ =>
        {
            var log = new ProcessLog();
            log.InitializeLog(targetLogDirectory);
            return log;
        });

        if (!string.Equals(processLog.LogDirectory, targetLogDirectory, StringComparison.OrdinalIgnoreCase))
        {
            processLog.InitializeLog(targetLogDirectory);
        }

        UiPublisher.Publish(normalizedPath, processLog, title ?? GetDefaultTitle(normalizedPath));
        PublishInputItems(normalizedPath, processLog);
        return normalizedPath;
    }

    private static void PublishInputItems(string logPath, ProcessLog processLog)
    {
        foreach (var levelItemName in LevelItemNames)
        {
            var inputPath = $"{logPath}.{levelItemName}";
            var inputItem = ItemExtension.CreateWithPath(inputPath, string.Empty);
            inputItem.Properties["kind"].Value = "ProcessLogInput";
            inputItem.Properties["log_level"].Value = levelItemName;
            inputItem.Properties["title"].Value = levelItemName;
            inputItem.Properties["text"].Value = levelItemName;
            inputItem.Properties["writable"].Value = true;
            inputItem.Properties["write"].Value = string.Empty;
            inputItem.Properties["write_path"].Value = inputPath;
            inputItem.Properties["write_mode"].Value = "Direct";

            HostRegistries.Data.UpsertSnapshot(inputPath, inputItem, DataRegistryItemMetadata.PublicData());
            InputBindings[NormalizePath(inputPath)] = new LogInputBinding(processLog, ToLogEventLevel(levelItemName));
        }
    }

    /// <summary>
    /// Writes a process log input item change to its owning process log.
    /// </summary>
    /// <param name="key">The changed registry key.</param>
    /// <param name="item">The changed item.</param>
    /// <param name="changeKind">The registry change kind.</param>
    /// <param name="parameterName">The changed parameter name.</param>
    /// <param name="changedValue">The already converted changed value.</param>
    /// <returns><see langword="true"/> when a log entry was written; otherwise, <see langword="false"/>.</returns>
    public static bool TryWriteInputEntry(string key, ItemModel item, DataChangeKind changeKind, string? parameterName, object? changedValue)
    {
        if (changeKind == DataChangeKind.PropertyUpdated
            && !string.Equals(parameterName, "write", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (changeKind is not (DataChangeKind.ValueUpdated or DataChangeKind.PropertyUpdated))
        {
            return false;
        }

        var normalizedKey = NormalizePath(key);
        if (!TryResolveInputBinding(normalizedKey, out var binding))
        {
            return false;
        }

        var message = changedValue?.ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        binding.ProcessLog.WriteEntry(binding.Level, message!.Trim());
        if (changeKind == DataChangeKind.PropertyUpdated)
        {
            HostRegistries.Data.UpdateProperty(normalizedKey, "write", string.Empty);
        }
        else
        {
            HostRegistries.Data.UpdateValue(normalizedKey, string.Empty);
        }

        return true;
    }

    private static string GetLogDirectory(string normalizedPath, string? logDirectory)
    {
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            return logDirectory;
        }

        return Path.Combine(HostLogger.LogDirectory, normalizedPath.Replace('.', '_'));
    }

    private static string GetDefaultTitle(string normalizedPath)
    {
        var lastSeparator = normalizedPath.LastIndexOf('.');
        return lastSeparator >= 0 ? normalizedPath[(lastSeparator + 1)..] : normalizedPath;
    }

    private static LogEventLevel ToLogEventLevel(string levelItemName)
        => levelItemName switch
        {
            "debug" => LogEventLevel.Debug,
            "info" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

    private static bool TryResolveInputBinding(string normalizedKey, out LogInputBinding binding)
    {
        if (InputBindings.TryGetValue(normalizedKey, out binding!))
        {
            return true;
        }

        var lastSeparator = normalizedKey.LastIndexOf('.');
        if (lastSeparator <= 0)
        {
            binding = null!;
            return false;
        }

        var logPath = normalizedKey[..lastSeparator];
        var levelItemName = normalizedKey[(lastSeparator + 1)..];
        if (!LevelItemNames.Contains(levelItemName, StringComparer.OrdinalIgnoreCase)
            || !Logs.TryGetValue(logPath, out var processLog))
        {
            binding = null!;
            return false;
        }

        binding = new LogInputBinding(processLog, ToLogEventLevel(levelItemName));
        InputBindings[normalizedKey] = binding;
        return true;
    }

    private static string NormalizePath(string path)
    {
        var segments = path
            .Replace('\\', '.')
            .Replace('/', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(HostPathSegmentNormalizer.Normalize)
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        return string.Join('.', segments);
    }

    private sealed record LogInputBinding(ProcessLog ProcessLog, LogEventLevel Level);
}
