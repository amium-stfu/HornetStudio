using System;
using System.Collections.Generic;
using System.Linq;
using HornetStudio.Host.Registries;
using HornetStudio.Host.Runtimes.EnhancedSignal;

namespace HornetStudio.Host.Runtimes.Udl;

public static class UdlPathHelper
{
    private static readonly string[] AttachOptionRoots = ["studio", "project", "udl_project"];
    private const string DefaultCanonicalClientName = "udl_client";
    private const string DefaultLegacyClientName = "UdlClient";

    public static string NormalizeClientName(string? name)
        => HostPathSegmentNormalizer.Normalize(string.IsNullOrWhiteSpace(name) ? DefaultCanonicalClientName : name.Trim());

    public static string GetCanonicalRuntimeBasePath(string? clientName)
        => $"runtime.udl_client.{NormalizeClientName(clientName)}";

    public static IReadOnlyList<string> GetRuntimeBasePaths(string? clientName)
    {
        var normalizedClientName = NormalizeClientName(clientName);
        var legacyClientName = string.IsNullOrWhiteSpace(clientName) ? DefaultLegacyClientName : clientName.Trim();
        return
        [
            $"runtime.udl_client.{normalizedClientName}",
            $"runtime.UdlClient.{legacyClientName}"
        ];
    }

    public static string GetCanonicalStatusBasePath(string? folderName, string? clientName)
        => $"studio.{EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName)}.{NormalizeClientName(clientName)}.status";

    public static string GetCanonicalAttachOptionsBasePath(string? folderName, string? clientName)
        => $"{GetCanonicalStatusBasePath(folderName, clientName)}.attach_options";

    public static IReadOnlyList<string> GetAttachOptionPrefixes(string? folderName, string? clientName)
    {
        var normalizedClientName = NormalizeClientName(clientName);
        var legacyClientName = string.IsNullOrWhiteSpace(clientName) ? DefaultLegacyClientName : clientName.Trim();
        var canonicalFolderName = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName);
        var legacyFolderName = NormalizePathDelimiters(folderName);
        var prefixes = new List<string>(AttachOptionRoots.Length * 2 + 2);

        foreach (var root in AttachOptionRoots)
        {
            prefixes.Add($"{root}.{canonicalFolderName}.{normalizedClientName}.status.attach_options");
            prefixes.Add($"{root}.{legacyFolderName}.{legacyClientName}.Status.AttachOptions");
        }

        prefixes.AddRange(GetRuntimeBasePaths(clientName));
        return prefixes;
    }

    public static bool IsUdlRuntimePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = EnhancedSignalPathHelper.NormalizeComparablePath(path);
        return normalizedPath.StartsWith("runtime.udl_client.", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("runtime.UdlClient.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetRuntimeClientName(string? fullPath, out string clientName)
    {
        clientName = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var segments = EnhancedSignalPathHelper.SplitPathSegments(fullPath);
        for (var index = 0; index < segments.Count - 2; index++)
        {
            if (!string.Equals(segments[index], "runtime", StringComparison.OrdinalIgnoreCase)
                || !IsUdlClientRuntimeSegment(segments[index + 1]))
            {
                continue;
            }

            clientName = NormalizeClientName(segments[index + 2]);
            return !string.IsNullOrWhiteSpace(clientName);
        }

        return false;
    }

    public static string GetRelativeRuntimePath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        var segments = EnhancedSignalPathHelper.SplitPathSegments(fullPath);
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var runtimeRootIndex = -1;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (string.Equals(segments[index], "runtime", StringComparison.OrdinalIgnoreCase)
                && IsUdlClientRuntimeSegment(segments[index + 1]))
            {
                runtimeRootIndex = index;
                break;
            }
        }

        if (runtimeRootIndex < 0)
        {
            return EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(fullPath);
        }

        var relativeSegments = segments.Skip(runtimeRootIndex + 3).ToArray();
        return relativeSegments.Length == 0 ? string.Empty : string.Join('.', relativeSegments);
    }

    private static bool IsUdlClientRuntimeSegment(string segment)
        => !string.IsNullOrWhiteSpace(segment)
           && string.Equals(segment.Replace("_", string.Empty), "udlclient", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePathDelimiters(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim().Replace('\\', '/').Trim('/', '.');
        if (string.Equals(normalized, "this", StringComparison.OrdinalIgnoreCase))
        {
            return "this";
        }

        var segments = normalized
            .Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Length == 0
            ? string.Empty
            : string.Join('.', segments);
    }
}
