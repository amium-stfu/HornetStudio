using System;
using System.Collections.Generic;
using System.IO;
using HornetStudio.Editor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HornetStudio.Editor.Monitoring;

/// <summary>
/// Provides file-based persistence for folder-local monitor definitions.
/// </summary>
public sealed class MonitorDefinitionFileCodec
{
    private const string MonitoringDirectoryName = "Monitoring";
    private const string MonitorFileName = "Monitor.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Gets the folder-local monitoring directory path.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the monitor file.</param>
    /// <returns>The monitoring directory path.</returns>
    public static string GetMonitoringDirectory(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        return Path.Combine(Path.GetFullPath(folderDirectory), MonitoringDirectoryName);
    }

    /// <summary>
    /// Gets the central monitor definition file path for one folder.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the monitor file.</param>
    /// <returns>The full monitor file path.</returns>
    public static string GetMonitorFilePath(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        return Path.Combine(GetMonitoringDirectory(folderDirectory), MonitorFileName);
    }

    /// <summary>
    /// Loads the central monitor definitions for one folder.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the monitor file.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <returns>The normalized monitor definitions.</returns>
    public IReadOnlyList<MonitorDefinition> LoadDefinitions(string folderDirectory, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        var filePath = GetMonitorFilePath(folderDirectory);
        if (!File.Exists(filePath))
        {
            return Array.Empty<MonitorDefinition>();
        }

        return ToDefinitions(LoadFileDocument(filePath, folderName), folderName);
    }

    /// <summary>
    /// Loads the complete central monitor document for one folder.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the monitor file.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <returns>The normalized monitor document.</returns>
    public MonitorDefinitionFileDocument LoadDocument(string folderDirectory, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        var filePath = GetMonitorFilePath(folderDirectory);
        if (!File.Exists(filePath))
        {
            return new MonitorDefinitionFileDocument();
        }

        return LoadFileDocument(filePath, folderName);
    }

    /// <summary>
    /// Loads monitor definitions from the specified YAML file.
    /// </summary>
    /// <param name="filePath">The full monitor YAML file path.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <returns>The normalized monitor definitions.</returns>
    public IReadOnlyList<MonitorDefinition> LoadFile(string filePath, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        return ToDefinitions(LoadFileDocument(filePath, folderName), folderName);
    }

    /// <summary>
    /// Loads the complete monitor document from the specified YAML file.
    /// </summary>
    /// <param name="filePath">The full monitor YAML file path.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <returns>The normalized monitor document.</returns>
    public MonitorDefinitionFileDocument LoadFileDocument(string filePath, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        var fullPath = Path.GetFullPath(filePath);
        var raw = File.ReadAllText(fullPath);
        var document = Deserializer.Deserialize<MonitorDefinitionFileDocument>(raw) ?? new MonitorDefinitionFileDocument();
        return NormalizeDocument(document);
    }

    /// <summary>
    /// Saves monitor definitions into the central folder-local YAML file.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the monitor file.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <param name="definitions">The monitor definitions to persist.</param>
    /// <returns>The saved monitor file path.</returns>
    public string SaveDefinitions(string folderDirectory, string folderName, IEnumerable<MonitorDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definitions);

        var monitoringDirectory = GetMonitoringDirectory(folderDirectory);
        Directory.CreateDirectory(monitoringDirectory);

        var filePath = GetMonitorFilePath(folderDirectory);
        var existingDocument = File.Exists(filePath)
            ? LoadFileDocument(filePath, folderName)
            : new MonitorDefinitionFileDocument();
        var document = new MonitorDefinitionFileDocument
        {
            Runtime = existingDocument.Runtime,
            Rules = MonitorDefinitionCodec.ToDocuments(MonitorDefinitionCodec.SerializeDefinitions(definitions), folderName)
        };

        var yaml = Serializer.Serialize(document);
        File.WriteAllText(filePath, yaml);
        return filePath;
    }

    /// <summary>
    /// Saves the complete monitor document into the central folder-local YAML file.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the monitor file.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <param name="document">The monitor document to persist.</param>
    /// <returns>The saved monitor file path.</returns>
    public string SaveDocument(string folderDirectory, string folderName, MonitorDefinitionFileDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(document);

        var monitoringDirectory = GetMonitoringDirectory(folderDirectory);
        Directory.CreateDirectory(monitoringDirectory);

        var filePath = GetMonitorFilePath(folderDirectory);
        var yaml = Serializer.Serialize(NormalizeDocument(document));
        File.WriteAllText(filePath, yaml);
        return filePath;
    }

    private static MonitorDefinitionFileDocument NormalizeDocument(MonitorDefinitionFileDocument document)
    {
        return new MonitorDefinitionFileDocument
        {
            Runtime = document.Runtime is null
                ? new MonitorRuntimeSettingsDocument()
                : new MonitorRuntimeSettingsDocument
                {
                    StartupDelayMs = Math.Max(0, document.Runtime.StartupDelayMs)
                },
            Rules = document.Rules ?? []
        };
    }

    private static IReadOnlyList<MonitorDefinition> ToDefinitions(MonitorDefinitionFileDocument document, string folderName)
    {
        return MonitorDefinitionCodec.FromDocuments(document.Rules, folderName).Length == 0
            ? Array.Empty<MonitorDefinition>()
            : MonitorDefinitionCodec.ParseDefinitions(MonitorDefinitionCodec.FromDocuments(document.Rules, folderName));
    }
}

/// <summary>
/// Represents the central folder-local monitor YAML document.
/// </summary>
public sealed class MonitorDefinitionFileDocument
{
    /// <summary>
    /// Gets the persisted folder-level monitor runtime settings.
    /// </summary>
    public MonitorRuntimeSettingsDocument Runtime { get; init; } = new();

    /// <summary>
    /// Gets the persisted monitor rule entries.
    /// </summary>
    public List<MonitorDefinitionDocument> Rules { get; init; } = [];
}