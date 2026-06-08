using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Host.Logging.Values;
using YamlDotNet.Serialization;

namespace HornetStudio.Editor.Persistence.ValueLog;

/// <summary>
/// Provides file-based persistence for folder-local CSV and SQL value logs.
/// </summary>
public sealed class ValueLogDefinitionFileCodec
{
    private const string ValueLogDirectoryName = "ValueLog";
    private const string CsvDirectoryName = "csv";
    private const string SqlDirectoryName = "sql";
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    /// <summary>
    /// Loads all folder-local CSV and SQL value log definitions.
    /// </summary>
    /// <param name="folderDirectory">The owning folder directory.</param>
    /// <returns>The normalized file entries.</returns>
    public IReadOnlyList<ValueLogFileEntry> LoadFolder(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);

        var entries = new List<ValueLogFileEntry>();
        entries.AddRange(LoadFolder(folderDirectory, ValueLogKind.Csv));
        entries.AddRange(LoadFolder(folderDirectory, ValueLogKind.Sql));
        return entries
            .OrderBy(static entry => entry.Definition.Kind)
            .ThenBy(static entry => entry.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Loads all file entries for one backend kind.
    /// </summary>
    /// <param name="folderDirectory">The owning folder directory.</param>
    /// <param name="kind">The backend kind.</param>
    /// <returns>The normalized file entries.</returns>
    public IReadOnlyList<ValueLogFileEntry> LoadFolder(string folderDirectory, ValueLogKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);

        var directory = GetKindDirectory(folderDirectory, kind);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ValueLogFileEntry>();
        }

        return Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadFile(path, kind))
            .OrderBy(static entry => entry.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Loads one value log definition file.
    /// </summary>
    /// <param name="filePath">The full YAML file path.</param>
    /// <param name="kind">The expected backend kind.</param>
    /// <returns>The normalized file entry.</returns>
    public ValueLogFileEntry LoadFile(string filePath, ValueLogKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var id = Path.GetFileNameWithoutExtension(fullPath);
        ValidateId(id, path: fullPath);

        var raw = File.ReadAllText(fullPath);
        var document = Deserializer.Deserialize<ValueLogDefinitionDocument>(raw) ?? new ValueLogDefinitionDocument();
        var definition = FromDocument(document, id, kind);
        return new ValueLogFileEntry(fullPath, definition, document);
    }

    /// <summary>
    /// Saves one folder-local value log definition.
    /// </summary>
    /// <param name="folderDirectory">The owning folder directory.</param>
    /// <param name="definition">The normalized definition.</param>
    /// <param name="existingFilePath">The current file path when renaming an existing definition.</param>
    /// <returns>The resulting saved file path.</returns>
    public string SaveDefinition(string folderDirectory, ValueLogDefinition definition, string? existingFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentNullException.ThrowIfNull(definition);

        ValidateId(definition.Id, path: null);

        var targetDirectory = GetKindDirectory(folderDirectory, definition.Kind);
        Directory.CreateDirectory(targetDirectory);

        var targetFilePath = BuildFilePath(folderDirectory, definition.Kind, definition.Id);
        var normalizedExistingPath = string.IsNullOrWhiteSpace(existingFilePath)
            ? string.Empty
            : Path.GetFullPath(existingFilePath);
        if (!string.IsNullOrWhiteSpace(normalizedExistingPath)
            && !string.Equals(normalizedExistingPath, targetFilePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(normalizedExistingPath))
        {
            File.Delete(normalizedExistingPath);
        }

        var yaml = Serializer.Serialize(ToDocument(definition));
        File.WriteAllText(targetFilePath, yaml);
        return targetFilePath;
    }

    /// <summary>
    /// Deletes one persisted definition file when it exists.
    /// </summary>
    /// <param name="filePath">The full YAML file path.</param>
    public void DeleteDefinition(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    /// <summary>
    /// Builds the full YAML file path for one value log id.
    /// </summary>
    /// <param name="folderDirectory">The owning folder directory.</param>
    /// <param name="kind">The backend kind.</param>
    /// <param name="id">The technical value log id.</param>
    /// <returns>The full YAML file path.</returns>
    public string BuildFilePath(string folderDirectory, ValueLogKind kind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Path.Combine(GetKindDirectory(folderDirectory, kind), $"{id}.yaml");
    }

    /// <summary>
    /// Gets the folder-local directory for one value log backend kind.
    /// </summary>
    /// <param name="folderDirectory">The owning folder directory.</param>
    /// <param name="kind">The backend kind.</param>
    /// <returns>The backend directory path.</returns>
    public static string GetKindDirectory(string folderDirectory, ValueLogKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        var kindDirectory = kind == ValueLogKind.Sql ? SqlDirectoryName : CsvDirectoryName;
        return Path.Combine(Path.GetFullPath(folderDirectory), ValueLogDirectoryName, kindDirectory);
    }

    private static ValueLogDefinition FromDocument(ValueLogDefinitionDocument document, string id, ValueLogKind kind)
    {
        return new ValueLogDefinition
        {
            Id = id,
            Text = document.Text?.Trim() ?? string.Empty,
            Kind = kind,
            Enabled = document.Enabled,
            AutoStart = document.AutoStart,
            OutputPath = document.OutputPath?.Trim() ?? string.Empty,
            IntervalMs = Math.Max(1, document.IntervalMs <= 0 ? 1000 : document.IntervalMs),
            SplitDaily = document.SplitDaily,
            SplitDailyTime = string.IsNullOrWhiteSpace(document.SplitDailyTime) ? "00:00:00" : document.SplitDailyTime.Trim(),
            SplitMaxFileSizeMb = Math.Max(0, document.SplitMaxFileSizeMb),
            PersistenceMode = string.IsNullOrWhiteSpace(document.PersistenceMode) ? "Balanced" : document.PersistenceMode.Trim(),
            FlushIntervalMs = Math.Max(0, document.FlushIntervalMs),
            FlushBatchSize = Math.Max(0, document.FlushBatchSize),
            Sources = (document.Sources ?? [])
                .Select(static source => new
                {
                    Source = source,
                    TargetPath = ResolveSourcePath(source)
                })
                .Where(static source => !string.IsNullOrWhiteSpace(source.TargetPath))
                .Select(source => new ValueLogSourceDefinition
                {
                    TargetPath = source.TargetPath,
                    IntervalMs = source.Source.IntervalMs > 0 ? source.Source.IntervalMs : null
                })
                .ToArray()
        };
    }

    private static ValueLogDefinitionDocument ToDocument(ValueLogDefinition definition)
    {
        return new ValueLogDefinitionDocument
        {
            Text = definition.Text?.Trim() ?? string.Empty,
            Enabled = definition.Enabled,
            AutoStart = definition.AutoStart,
            OutputPath = definition.OutputPath?.Trim() ?? string.Empty,
            IntervalMs = Math.Max(1, definition.IntervalMs),
            SplitDaily = definition.SplitDaily,
            SplitDailyTime = string.IsNullOrWhiteSpace(definition.SplitDailyTime) ? "00:00:00" : definition.SplitDailyTime.Trim(),
            SplitMaxFileSizeMb = Math.Max(0, definition.SplitMaxFileSizeMb),
            PersistenceMode = string.IsNullOrWhiteSpace(definition.PersistenceMode) ? "Balanced" : definition.PersistenceMode.Trim(),
            FlushIntervalMs = Math.Max(0, definition.FlushIntervalMs),
            FlushBatchSize = Math.Max(0, definition.FlushBatchSize),
            Sources = definition.Sources
                .Select(source => new ValueLogSourceDocument
                {
                    Path = source.TargetPath?.Trim() ?? string.Empty,
                    IntervalMs = Math.Max(0, source.IntervalMs ?? 0)
                })
                .ToList()
        };
    }

    private static string ResolveSourcePath(ValueLogSourceDocument source)
        => !string.IsNullOrWhiteSpace(source.Path)
            ? source.Path.Trim()
            : source.TargetPath?.Trim() ?? string.Empty;

    private static void ValidateId(string id, string? path)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException(path is null
                ? "ValueLog id is missing."
                : $"ValueLog file '{path}' is missing a value log id.");
        }

        if (!TargetPathHelper.IsValidPathIdentityName(id))
        {
            throw new InvalidOperationException(path is null
                ? $"ValueLog id '{id}' must use snake_case."
                : $"ValueLog file '{path}' must use a snake_case file name instead of '{id}'.");
        }
    }
}

/// <summary>
/// Represents one file-backed value log definition.
/// </summary>
/// <param name="FilePath">The YAML file path.</param>
/// <param name="Definition">The normalized runtime definition.</param>
/// <param name="Document">The persisted document model.</param>
public sealed record ValueLogFileEntry(
    string FilePath,
    ValueLogDefinition Definition,
    ValueLogDefinitionDocument Document);

/// <summary>
/// Represents the persisted YAML document for one folder-local value log file.
/// </summary>
public sealed class ValueLogDefinitionDocument
{
    [YamlMember(Alias = "Text")]
    public string Text { get; init; } = string.Empty;

    [YamlMember(Alias = "Enabled")]
    public bool Enabled { get; init; } = true;

    [YamlMember(Alias = "AutoStart")]
    public bool AutoStart { get; init; }

    [YamlMember(Alias = "OutputPath")]
    public string OutputPath { get; init; } = string.Empty;

    [YamlMember(Alias = "IntervalMs")]
    public int IntervalMs { get; init; } = 1000;

    [YamlMember(Alias = "SplitDaily")]
    public bool SplitDaily { get; init; }

    [YamlMember(Alias = "SplitDailyTime")]
    public string SplitDailyTime { get; init; } = "00:00:00";

    [YamlMember(Alias = "SplitMaxFileSizeMb")]
    public int SplitMaxFileSizeMb { get; init; }

    [YamlMember(Alias = "PersistenceMode")]
    public string PersistenceMode { get; init; } = "Balanced";

    [YamlMember(Alias = "FlushIntervalMs")]
    public int FlushIntervalMs { get; init; }

    [YamlMember(Alias = "FlushBatchSize")]
    public int FlushBatchSize { get; init; }

    [YamlMember(Alias = "Sources")]
    public List<ValueLogSourceDocument> Sources { get; init; } = [];
}

/// <summary>
/// Represents one persisted value log source entry.
/// </summary>
public sealed class ValueLogSourceDocument
{
    [YamlMember(Alias = "Path")]
    public string Path { get; init; } = string.Empty;

    [YamlMember(Alias = "Name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "TargetPath")]
    public string TargetPath { get; init; } = string.Empty;

    [YamlMember(Alias = "Caption")]
    public string Caption { get; init; } = string.Empty;

    [YamlMember(Alias = "Unit")]
    public string Unit { get; init; } = string.Empty;

    [YamlMember(Alias = "Format")]
    public string Format { get; init; } = string.Empty;

    [YamlMember(Alias = "IntervalMs")]
    public int IntervalMs { get; init; }
}
