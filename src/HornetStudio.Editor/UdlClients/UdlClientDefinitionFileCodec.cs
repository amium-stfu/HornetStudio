using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using YamlDotNet.Serialization;

namespace HornetStudio.Editor.UdlClients;

/// <summary>
/// Provides file-based persistence for folder-local UDL client definitions under <c>Clients/Udl/*.yaml</c>.
/// </summary>
public sealed class UdlClientDefinitionFileCodec
{
    private const string ClientsDirectoryName = "Clients";
    private const string UdlDirectoryName = "Udl";
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    /// <summary>
    /// Enumerates all UDL client files for the provided folder directory.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the client files.</param>
    /// <returns>The loaded file entries sorted by client id.</returns>
    public IReadOnlyList<UdlClientFileEntry> LoadFolder(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);

        var clientsDirectory = GetClientsDirectory(folderDirectory);
        if (!Directory.Exists(clientsDirectory))
        {
            return Array.Empty<UdlClientFileEntry>();
        }

        return Directory.EnumerateFiles(clientsDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(LoadFile)
            .OrderBy(entry => entry.Definition.ClientId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Loads one UDL client definition file.
    /// </summary>
    /// <param name="filePath">The full YAML file path.</param>
    /// <returns>The loaded file entry.</returns>
    public UdlClientFileEntry LoadFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var clientId = Path.GetFileNameWithoutExtension(fullPath);
        ValidateClientId(clientId, fullPath);

        var raw = File.ReadAllText(fullPath);
        var document = Deserializer.Deserialize<UdlClientDefinitionDocument>(raw) ?? new UdlClientDefinitionDocument();
        var definition = FromDocument(document, clientId);
        return new UdlClientFileEntry(fullPath, definition, document);
    }

    /// <summary>
    /// Saves one UDL client definition into its folder-local YAML file.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the client file.</param>
    /// <param name="definition">The definition to save.</param>
    /// <param name="existingFilePath">The current file path when renaming an existing definition.</param>
    /// <returns>The resulting saved file path.</returns>
    public string SaveDefinition(string folderDirectory, UdlClientDefinition definition, string? existingFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentNullException.ThrowIfNull(definition);

        ValidateClientId(definition.ClientId, path: null);

        var clientsDirectory = GetClientsDirectory(folderDirectory);
        Directory.CreateDirectory(clientsDirectory);

        var targetFilePath = BuildFilePath(folderDirectory, definition.ClientId);
        var normalizedExistingPath = string.IsNullOrWhiteSpace(existingFilePath)
            ? string.Empty
            : Path.GetFullPath(existingFilePath);

        if (!string.IsNullOrWhiteSpace(normalizedExistingPath)
            && !string.Equals(normalizedExistingPath, targetFilePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(normalizedExistingPath))
        {
            File.Delete(normalizedExistingPath);
        }

        var document = ToDocument(definition);
        var yaml = Serializer.Serialize(document);
        File.WriteAllText(targetFilePath, yaml);
        return targetFilePath;
    }

    /// <summary>
    /// Deletes one UDL client file when it exists.
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
    /// Returns the full YAML file path for a UDL client id inside the provided folder directory.
    /// </summary>
    /// <param name="folderDirectory">The folder directory that owns the client file.</param>
    /// <param name="clientId">The technical client id.</param>
    /// <returns>The full YAML file path.</returns>
    public string BuildFilePath(string folderDirectory, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        return Path.Combine(GetClientsDirectory(folderDirectory), $"{clientId}.yaml");
    }

    /// <summary>
    /// Gets the folder-local UDL clients directory path.
    /// </summary>
    /// <param name="folderDirectory">The folder directory path.</param>
    /// <returns>The UDL clients directory path.</returns>
    public static string GetClientsDirectory(string folderDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderDirectory);
        return Path.Combine(Path.GetFullPath(folderDirectory), ClientsDirectoryName, UdlDirectoryName);
    }

    private static UdlClientDefinition FromDocument(UdlClientDefinitionDocument document, string clientId)
    {
        var attachedItemPaths = document.AttachedItemPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UdlClientDefinition
        {
            ClientId = clientId,
            Text = document.Text?.Trim() ?? string.Empty,
            Host = string.IsNullOrWhiteSpace(document.Host) ? UdlClientDefinitionDefaults.Host : document.Host.Trim(),
            Port = document.Port <= 0 ? UdlClientDefinitionDefaults.Port : document.Port,
            AutoConnect = document.AutoConnect,
            DebugLogging = document.DebugLogging,
            Enabled = document.Enabled,
            DemoEnabled = document.DemoEnabled,
            AttachedItemPaths = attachedItemPaths,
            UdlModuleExposureDefinitions = UdlModuleExposureDefinitionCodec.SerializeDefinitions(document.UdlModuleExposures),
            DemoModuleDefinitions = UdlDemoModuleDefinitionCodec.FromDocuments(document.DemoModules)
        };
    }

    private static UdlClientDefinitionDocument ToDocument(UdlClientDefinition definition)
    {
        return new UdlClientDefinitionDocument
        {
            Text = definition.Text?.Trim() ?? string.Empty,
            Host = string.IsNullOrWhiteSpace(definition.Host) ? UdlClientDefinitionDefaults.Host : definition.Host.Trim(),
            Port = definition.Port <= 0 ? UdlClientDefinitionDefaults.Port : definition.Port,
            AutoConnect = definition.AutoConnect,
            DebugLogging = definition.DebugLogging,
            Enabled = definition.Enabled,
            DemoEnabled = definition.DemoEnabled,
            AttachedItemPaths = definition.AttachedItemPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UdlModuleExposures = UdlModuleExposureDefinitionCodec.ParseDefinitions(definition.UdlModuleExposureDefinitions).ToList(),
            DemoModules = UdlDemoModuleDefinitionCodec.ToDocuments(definition.DemoModuleDefinitions)
        };
    }

    private static void ValidateClientId(string clientId, string? path)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(path is null
                ? "UDL client id is missing."
                : $"UDL client file '{path}' is missing a client id.");
        }

        if (!TargetPathHelper.IsValidPathIdentityName(clientId))
        {
            throw new InvalidOperationException(path is null
                ? $"UDL client id '{clientId}' must use snake_case."
                : $"UDL client file '{path}' must use a snake_case file name instead of '{clientId}'.");
        }
    }
}

/// <summary>
/// Represents the persisted YAML document for one UDL client file.
/// </summary>
public sealed class UdlClientDefinitionDocument
{
    /// <summary>
    /// Gets the optional display label shown to the user.
    /// </summary>
    [YamlMember(Alias = "Text")]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the configured host name or address.
    /// </summary>
    [YamlMember(Alias = "Host")]
    public string Host { get; init; } = UdlClientDefinitionDefaults.Host;

    /// <summary>
    /// Gets the configured TCP port.
    /// </summary>
    [YamlMember(Alias = "Port")]
    public int Port { get; init; } = UdlClientDefinitionDefaults.Port;

    /// <summary>
    /// Gets a value indicating whether the client should connect automatically.
    /// </summary>
    [YamlMember(Alias = "AutoConnect")]
    public bool AutoConnect { get; init; }

    /// <summary>
    /// Gets a value indicating whether verbose UDL diagnostics are enabled.
    /// </summary>
    [YamlMember(Alias = "DebugLogging")]
    public bool DebugLogging { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client is enabled for runtime synchronization.
    /// </summary>
    [YamlMember(Alias = "Enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether demo modules are enabled.
    /// </summary>
    [YamlMember(Alias = "DemoEnabled")]
    public bool DemoEnabled { get; init; }

    /// <summary>
    /// Gets the explicitly attached relative item paths.
    /// </summary>
    [YamlMember(Alias = "AttachedItemPaths")]
    public List<string> AttachedItemPaths { get; init; } = [];

    /// <summary>
    /// Gets the persisted module exposure definitions.
    /// </summary>
    [YamlMember(Alias = "UdlModuleExposures")]
    public List<UdlModuleExposureDefinition> UdlModuleExposures { get; init; } = [];

    /// <summary>
    /// Gets the persisted demo modules.
    /// </summary>
    [YamlMember(Alias = "DemoModules")]
    public List<UdlDemoModuleDefinitionDocument> DemoModules { get; init; } = [];
}

/// <summary>
/// Represents one file-backed UDL client definition.
/// </summary>
/// <param name="FilePath">The YAML file path.</param>
/// <param name="Definition">The normalized runtime definition.</param>
/// <param name="Document">The persisted document model.</param>
public sealed record UdlClientFileEntry(
    string FilePath,
    UdlClientDefinition Definition,
    UdlClientDefinitionDocument Document);