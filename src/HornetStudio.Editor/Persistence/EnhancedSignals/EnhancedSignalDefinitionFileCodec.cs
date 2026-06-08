using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HornetStudio.Editor.Persistence.EnhancedSignals;

/// <summary>
/// Provides file-based persistence for folder-local enhanced signal definitions.
/// </summary>
public sealed class EnhancedSignalDefinitionFileCodec
{
    private const string SignalsDirectoryName = "Signals";
    private const string EnhancedDirectoryName = "Enhanced";
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Enumerates all enhanced signal files for the provided folder directory.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the signal files.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <returns>The loaded file entries sorted by definition name.</returns>
    public IReadOnlyList<EnhancedSignalFileEntry> LoadFolder(string folderDirectory, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        var signalsDirectory = GetEnhancedSignalsDirectory(folderDirectory);
        if (!Directory.Exists(signalsDirectory))
        {
            return Array.Empty<EnhancedSignalFileEntry>();
        }

        return Directory.EnumerateFiles(signalsDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadFile(path, folderName))
            .OrderBy(entry => entry.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Loads one enhanced signal file.
    /// </summary>
    /// <param name="filePath">The full YAML file path.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <returns>The loaded file entry.</returns>
    public EnhancedSignalFileEntry LoadFile(string filePath, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        var fullPath = Path.GetFullPath(filePath);
        var raw = File.ReadAllText(fullPath);
        var document = Deserializer.Deserialize<ExtendedSignalDefinitionDocument>(raw) ?? new ExtendedSignalDefinitionDocument();
        var definition = ExtendedSignalDefinitionCodec.FromDocument(document, folderName).NormalizeLegacyFields();
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
        ValidateDefinitionName(definition.Name, fullPath, fileNameWithoutExtension);

        return new EnhancedSignalFileEntry(fullPath, definition, document);
    }

    /// <summary>
    /// Saves one enhanced signal definition into its folder-local YAML file.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the signal file.</param>
    /// <param name="folderName">The technical folder name used for path normalization.</param>
    /// <param name="definition">The definition to save.</param>
    /// <param name="existingFilePath">The current file path when renaming an existing definition.</param>
    /// <returns>The resulting saved file path.</returns>
    public string SaveDefinition(string folderDirectory, string folderName, ExtendedSignalDefinition definition, string? existingFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);

        if (!TargetPathHelper.IsValidPathIdentityName(definition.Name))
        {
            throw new InvalidOperationException($"Enhanced signal name '{definition.Name}' must use snake_case.");
        }

        var signalsDirectory = GetEnhancedSignalsDirectory(folderDirectory);
        Directory.CreateDirectory(signalsDirectory);

        var targetFilePath = BuildFilePath(folderDirectory, definition.Name);
        var normalizedExistingPath = string.IsNullOrWhiteSpace(existingFilePath)
            ? string.Empty
            : Path.GetFullPath(existingFilePath);

        if (!string.IsNullOrWhiteSpace(normalizedExistingPath)
            && !string.Equals(normalizedExistingPath, targetFilePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(normalizedExistingPath))
        {
            File.Delete(normalizedExistingPath);
        }

        var document = ExtendedSignalDefinitionCodec.ToDocument(definition, folderName);
        var yaml = Serializer.Serialize(document);
        File.WriteAllText(targetFilePath, yaml);
        return targetFilePath;
    }

    /// <summary>
    /// Deletes one enhanced signal file when it exists.
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
    /// Returns the full YAML file path for a signal name inside the provided folder directory.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the signal file.</param>
    /// <param name="signalName">The technical signal name.</param>
    /// <returns>The full YAML file path.</returns>
    public string BuildFilePath(string folderDirectory, string signalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        return Path.Combine(GetEnhancedSignalsDirectory(folderDirectory), $"{signalName}.yaml");
    }

    /// <summary>
    /// Gets the folder-local enhanced signals directory path.
    /// </summary>
    /// <param name="folderDirectory">The folder directory path.</param>
    /// <returns>The enhanced signals directory path.</returns>
    public static string GetEnhancedSignalsDirectory(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        return Path.Combine(Path.GetFullPath(folderDirectory), SignalsDirectoryName, EnhancedDirectoryName);
    }

    private static void ValidateDefinitionName(string definitionName, string filePath, string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(definitionName))
        {
            throw new InvalidOperationException($"Enhanced signal file '{filePath}' is missing a name.");
        }

        if (!TargetPathHelper.IsValidPathIdentityName(definitionName))
        {
            throw new InvalidOperationException($"Enhanced signal '{definitionName}' in '{filePath}' must use snake_case.");
        }

        if (!string.Equals(definitionName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Enhanced signal file '{filePath}' must match definition name '{definitionName}'.");
        }
    }
}

/// <summary>
/// Represents one file-backed enhanced signal definition.
/// </summary>
/// <param name="FilePath">The YAML file path.</param>
/// <param name="Definition">The normalized runtime definition.</param>
/// <param name="Document">The persisted document model.</param>
public sealed record EnhancedSignalFileEntry(
    string FilePath,
    ExtendedSignalDefinition Definition,
    ExtendedSignalDefinitionDocument Document);
