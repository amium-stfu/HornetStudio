using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Provides file-based persistence for folder-local controller definitions under Controllers/*.yaml.
/// </summary>
public sealed class ControllerDefinitionFileCodec
{
    private const string ControllersDirectoryName = "Controllers";
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Enumerates all controller files for the provided folder directory.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the controller files.</param>
    /// <returns>The loaded file entries sorted by definition name.</returns>
    public IReadOnlyList<ControllerFileEntry> LoadFolder(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);

        var controllersDirectory = GetControllersDirectory(folderDirectory);
        if (!Directory.Exists(controllersDirectory))
        {
            return Array.Empty<ControllerFileEntry>();
        }

        return Directory.EnumerateFiles(controllersDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadFile(path))
            .OrderBy(entry => entry.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Loads one controller definition file.
    /// </summary>
    /// <param name="filePath">The full YAML file path.</param>
    /// <returns>The loaded file entry.</returns>
    public ControllerFileEntry LoadFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var raw = File.ReadAllText(fullPath);
        var document = Deserializer.Deserialize<ControllerDefinitionDocument>(raw) ?? new ControllerDefinitionDocument();
        var definition = ControllerDefinitionCodec.FromDocument(document);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
        ValidateDefinitionName(definition.Name, fullPath, fileNameWithoutExtension);

        return new ControllerFileEntry(fullPath, definition, document);
    }

    /// <summary>
    /// Saves one controller definition into its folder-local YAML file.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the controller file.</param>
    /// <param name="definition">The definition to save.</param>
    /// <param name="existingFilePath">The current file path when renaming an existing definition.</param>
    /// <returns>The resulting saved file path.</returns>
    public string SaveDefinition(string folderDirectory, ControllerDefinition definition, string? existingFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentNullException.ThrowIfNull(definition);

        if (!TargetPathHelper.IsValidPathIdentityName(definition.Name))
        {
            throw new InvalidOperationException($"Controller name '{definition.Name}' must use snake_case.");
        }

        var controllersDirectory = GetControllersDirectory(folderDirectory);
        Directory.CreateDirectory(controllersDirectory);

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

        var document = ControllerDefinitionCodec.ToDocument(definition);
        var yaml = Serializer.Serialize(document);
        File.WriteAllText(targetFilePath, yaml);
        return targetFilePath;
    }

    /// <summary>
    /// Deletes one controller file when it exists.
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
    /// Returns the full YAML file path for a controller name inside the provided folder directory.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the controller file.</param>
    /// <param name="controllerName">The technical controller name.</param>
    /// <returns>The full YAML file path.</returns>
    public string BuildFilePath(string folderDirectory, string controllerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerName);
        return Path.Combine(GetControllersDirectory(folderDirectory), $"{controllerName}.yaml");
    }

    /// <summary>
    /// Gets the folder-local controllers directory path.
    /// </summary>
    /// <param name="folderDirectory">The folder directory path.</param>
    /// <returns>The controllers directory path.</returns>
    public static string GetControllersDirectory(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        return Path.Combine(Path.GetFullPath(folderDirectory), ControllersDirectoryName);
    }

    private static void ValidateDefinitionName(string definitionName, string filePath, string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(definitionName))
        {
            throw new InvalidOperationException($"Controller file '{filePath}' is missing a name.");
        }

        if (!TargetPathHelper.IsValidPathIdentityName(definitionName))
        {
            throw new InvalidOperationException($"Controller '{definitionName}' in '{filePath}' must use snake_case.");
        }

        if (!string.Equals(definitionName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Controller file '{filePath}' must match definition name '{definitionName}'.");
        }
    }
}

/// <summary>
/// Represents one file-backed controller definition.
/// </summary>
/// <param name="FilePath">The YAML file path.</param>
/// <param name="Definition">The normalized runtime definition.</param>
/// <param name="Document">The persisted document model.</param>
public sealed record ControllerFileEntry(
    string FilePath,
    ControllerDefinition Definition,
    ControllerDefinitionDocument Document);
