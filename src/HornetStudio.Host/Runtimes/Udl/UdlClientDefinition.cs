using System;
using System.Collections.Generic;

namespace HornetStudio.Host.Runtimes.Udl;

/// <summary>
/// Represents one folder-local UDL client definition that is stored in <c>Clients/Udl/&lt;client-id&gt;.yaml</c>.
/// </summary>
public sealed record UdlClientDefinition
{
    /// <summary>
    /// Gets the technical client identifier derived from the YAML file name.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional display label shown to the user.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the configured host name or address.
    /// </summary>
    public string Host { get; init; } = UdlClientDefinitionDefaults.Host;

    /// <summary>
    /// Gets the configured TCP port.
    /// </summary>
    public int Port { get; init; } = UdlClientDefinitionDefaults.Port;

    /// <summary>
    /// Gets a value indicating whether the client should connect automatically.
    /// </summary>
    public bool AutoConnect { get; init; }

    /// <summary>
    /// Gets a value indicating whether verbose UDL diagnostics are enabled.
    /// </summary>
    public bool DebugLogging { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client is enabled for runtime synchronization.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether demo modules are enabled.
    /// </summary>
    public bool DemoEnabled { get; init; }

    /// <summary>
    /// Gets the explicitly attached relative item paths.
    /// </summary>
    public IReadOnlyList<string> AttachedItemPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the serialized module exposure definitions used for attachment projection.
    /// </summary>
    public string UdlModuleExposureDefinitions { get; init; } = UdlModuleExposureDefinitionCodec.SerializeDefinitions(definitions: null);

    /// <summary>
    /// Gets the serialized demo module definitions.
    /// </summary>
    public string DemoModuleDefinitions { get; init; } = string.Empty;
}

/// <summary>
/// Provides default values for folder-local UDL client definitions.
/// </summary>
public static class UdlClientDefinitionDefaults
{
    /// <summary>
    /// The default host used when a file omits the endpoint.
    /// </summary>
    public const string Host = "192.168.178.151";

    /// <summary>
    /// The default port used when a file omits the endpoint.
    /// </summary>
    public const int Port = 9001;
}
