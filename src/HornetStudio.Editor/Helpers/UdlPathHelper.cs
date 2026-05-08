using System;
using System.Collections.Generic;
using System.Linq;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Editor.Helpers;

internal static class UdlPathHelper
{
    private static readonly string[] AttachOptionRoots = ["studio", "project", "udl_project"];
    private const string DefaultCanonicalClientName = "udl_client_control";
    private const string DefaultLegacyClientName = "UdlClientControl";

    public static string NormalizeClientName(string? name)
        => TargetPathHelper.NormalizePathSegment(name, DefaultCanonicalClientName);

    private static string GetLegacyClientName(string? name)
        => string.IsNullOrWhiteSpace(name) ? DefaultLegacyClientName : name.Trim();

    public static string GetCanonicalRuntimeBasePath(string? clientName)
        => $"runtime.udl_client.{NormalizeClientName(clientName)}";

    public static IReadOnlyList<string> GetRuntimeBasePaths(string? clientName)
    {
        var normalizedClientName = NormalizeClientName(clientName);
        var legacyClientName = GetLegacyClientName(clientName);
        return
        [
            $"runtime.udl_client.{normalizedClientName}",
            $"runtime.UdlClient.{legacyClientName}"
        ];
    }

    public static string GetCanonicalStatusBasePath(string? folderName, string? clientName)
        => $"studio.{TargetPathHelper.NormalizeConfiguredTargetPath(folderName)}.{NormalizeClientName(clientName)}.status";

    public static string GetCanonicalAttachOptionsBasePath(string? folderName, string? clientName)
        => $"{GetCanonicalStatusBasePath(folderName, clientName)}.attach_options";

    public static IReadOnlyList<string> GetAttachOptionPrefixes(string? folderName, string? clientName)
    {
        var normalizedClientName = NormalizeClientName(clientName);
        var legacyClientName = GetLegacyClientName(clientName);
        var canonicalFolderName = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        var legacyFolderName = TargetPathHelper.NormalizePathDelimiters(folderName);
        var prefixes = new List<string>(AttachOptionRoots.Length * 2 + 2);

        foreach (var root in AttachOptionRoots)
        {
            prefixes.Add($"{root}.{canonicalFolderName}.{normalizedClientName}.status.attach_options");
            prefixes.Add($"{root}.{legacyFolderName}.{legacyClientName}.Status.AttachOptions");
        }

        prefixes.AddRange(GetRuntimeBasePaths(clientName));
        return prefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsUdlRuntimePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = TargetPathHelper.NormalizeComparablePath(path);
        return normalizedPath.StartsWith("runtime.udl_client.", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("runtime.UdlClient.", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetRelativeRuntimePath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        var segments = TargetPathHelper.SplitPathSegments(fullPath);
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
            return TargetPathHelper.NormalizeConfiguredTargetPath(fullPath);
        }

        var relativeSegments = segments.Skip(runtimeRootIndex + 3).ToArray();
        return relativeSegments.Length == 0 ? string.Empty : string.Join('.', relativeSegments);
    }

    private static bool IsUdlClientRuntimeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        return string.Equals(segment.Replace("_", string.Empty), "udlclient", StringComparison.OrdinalIgnoreCase);
    }
}