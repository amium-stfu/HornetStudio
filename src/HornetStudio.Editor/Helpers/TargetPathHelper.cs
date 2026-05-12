using System;
using System.Collections.Generic;
using System.Text;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Editor.Helpers;

public static class TargetPathHelper
{
    private const string StudioRootSegment = "studio";
    private const string RuntimeItemBrokerPrefix = "runtime.item_broker";
    private static readonly string[] BrokerAttachOptionRoots = [StudioRootSegment, "project", "udl_project"];
    private static readonly string[] ProjectRootSegments = [StudioRootSegment, "project", "udl_project", "udl_book"];
    private static readonly string[] LegacyProjectRootSegments = ["project", "udl_project", "udl_book"];
    private static readonly string[] NonProjectRootSegments = ["runtime", "logs", "commands"];
    private static readonly char[] HierarchySeparators = ['.', '/'];

    /// <summary>
    /// Normalizes path separators without applying canonical casing rules.
    /// </summary>
    /// <param name="path">The input path.</param>
    /// <returns>A dot-separated path preserving the original segment casing.</returns>
    public static string NormalizePathDelimiters(string? path)
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
            .Split(HierarchySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Length == 0
            ? string.Empty
            : string.Join('.', segments);
    }

    /// <summary>
    /// Normalizes a single path-relevant segment to canonical snake_case.
    /// </summary>
    /// <param name="segment">The segment to normalize.</param>
    /// <param name="fallbackSegment">The fallback segment to use when <paramref name="segment"/> is empty.</param>
    /// <returns>The normalized segment.</returns>
    public static string NormalizePathSegment(string? segment, string fallbackSegment)
    {
        var value = string.IsNullOrWhiteSpace(segment)
            ? fallbackSegment
            : segment.Trim();

        return ConvertToSnakeCaseSegment(value);
    }

    /// <summary>
    /// Normalizes a technical identity name without applying casing transformations.
    /// </summary>
    /// <param name="name">The identity name.</param>
    /// <returns>The trimmed identity name or an empty string.</returns>
    public static string NormalizeIdentityName(string? name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

    /// <summary>
    /// Determines whether a technical identity already uses strict snake_case.
    /// </summary>
    /// <param name="name">The identity name to validate.</param>
    /// <returns><c>true</c> when the identity name contains only lowercase letters, digits, and underscores.</returns>
    public static bool IsValidPathIdentityName(string? name)
    {
        var normalized = NormalizeIdentityName(name);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        foreach (var character in normalized)
        {
            if (character == '_')
            {
                continue;
            }

            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Generates the next available indexed snake_case identity name.
    /// </summary>
    /// <param name="baseName">The desired base name.</param>
    /// <param name="existingNames">Existing identity names that must remain unique.</param>
    /// <param name="fallbackBaseName">The fallback base name.</param>
    /// <returns>The next available indexed identity name.</returns>
    public static string GenerateIndexedPathIdentityName(string? baseName, IEnumerable<string>? existingNames, string fallbackBaseName)
    {
        var normalizedBaseName = NormalizePathSegment(baseName, fallbackBaseName);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (existingNames is not null)
        {
            foreach (var existingName in existingNames)
            {
                var normalizedExistingName = NormalizeIdentityName(existingName);
                if (!string.IsNullOrWhiteSpace(normalizedExistingName))
                {
                    usedNames.Add(normalizedExistingName);
                }
            }
        }

        var index = 1;
        while (true)
        {
            var candidate = $"{normalizedBaseName}_{index}";
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    public static string NormalizeConfiguredTargetPath(string? path)
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
            .Split(HierarchySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Length == 0
            ? string.Empty
            : string.Join('.', NormalizeStudioRoot(segments));
    }

    public static string NormalizeComparablePath(string? path)
    {
        var segments = SplitPathSegments(path);
        return segments.Count == 0
            ? string.Empty
            : string.Join('.', segments);
    }

    public static IReadOnlyList<string> SplitPathSegments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var normalized = NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        return normalized
            .Split(HierarchySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool PathsEqual(string? left, string? right)
        => string.Equals(NormalizeComparablePath(left), NormalizeComparablePath(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the path uses the internal ItemBroker runtime prefix.
    /// </summary>
    public static bool IsRuntimeItemServerPath(string? path)
    {
        var segments = SplitPathSegments(path);
        return segments.Count >= 2
            && string.Equals(segments[0], "runtime", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "item_broker", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes the internal ItemBroker runtime prefix from a path when present.
    /// </summary>
    public static string ToFlatItemServerPath(string? path)
    {
        var segments = SplitPathSegments(path);
        if (segments.Count <= 2 || !IsRuntimeItemServerPath(path))
        {
            return NormalizeConfiguredTargetPath(path);
        }

        return string.Join('.', segments.Skip(2));
    }

    /// <summary>
    /// Converts a flat broker path to its internal ItemBroker runtime path.
    /// </summary>
    public static string ToRuntimeItemServerPath(string? path)
    {
        var normalized = NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized) || IsRuntimeItemServerPath(normalized))
        {
            return normalized;
        }

        return $"{RuntimeItemBrokerPrefix}.{normalized}";
    }

    /// <summary>
    /// Removes the broker widget and MQTT client segments from a flat ItemBroker path when present.
    /// </summary>
    public static string ToRelativeItemServerPath(string? path)
    {
        var flatPath = ToFlatItemServerPath(path);
        var segments = SplitPathSegments(flatPath);
        return segments.Count <= 2 ? flatPath : string.Join('.', segments.Skip(2));
    }

    /// <summary>
    /// Converts legacy and visible broker received paths to the persisted attach identity.
    /// </summary>
    /// <param name="path">The broker received path.</param>
    /// <returns>The normalized attach identity.</returns>
    public static string ToBrokerReceivedAttachIdentity(string? path)
    {
        var flatPath = ToFlatItemServerPath(path);
        var segments = SplitPathSegments(flatPath)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        if (IsProjectRootSegment(segments[0])
            && segments.Length >= 5
            && string.Equals(segments[3], "mqtt", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('.', segments.Skip(2));
        }

        if (segments.Length >= 3 && string.Equals(segments[1], "shared", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('.', new[] { segments[0], "mqtt" }.Concat(segments.Skip(2)));
        }

        for (var index = 1; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "mqtt", StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('.', segments.Skip(index - 1));
            }
        }

        return flatPath;
    }

    /// <summary>
    /// Gets the canonical broker widget segment used in runtime and studio paths.
    /// </summary>
    /// <param name="widgetName">The configured widget name.</param>
    /// <returns>The canonical broker widget segment.</returns>
    public static string GetCanonicalBrokerWidgetName(string? widgetName)
        => NormalizePathSegment(widgetName, "broker_widget");

    /// <summary>
    /// Gets the canonical broker attach-options base path.
    /// </summary>
    /// <param name="folderName">The containing folder or page name.</param>
    /// <param name="widgetName">The configured widget name.</param>
    /// <returns>The canonical attach-options base path.</returns>
    public static string GetCanonicalBrokerAttachOptionsBasePath(string? folderName, string? widgetName)
    {
        var normalizedFolderName = NormalizeConfiguredTargetPath(folderName);
        return JoinPath(StudioRootSegment, JoinPath(normalizedFolderName, $"{GetCanonicalBrokerWidgetName(widgetName)}.status.attach_options"));
    }

    /// <summary>
    /// Enumerates canonical and legacy broker attach-options prefixes for discovery.
    /// </summary>
    /// <param name="folderName">The containing folder or page name.</param>
    /// <param name="widgetName">The configured widget name.</param>
    /// <returns>The attach-options prefixes to probe.</returns>
    public static IReadOnlyList<string> GetBrokerAttachOptionPrefixes(string? folderName, string? widgetName)
    {
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canonicalFolder = NormalizeConfiguredTargetPath(folderName);
        var canonicalWidget = GetCanonicalBrokerWidgetName(widgetName);
        var legacyFolder = NormalizePathDelimiters(folderName);
        var legacyWidget = string.IsNullOrWhiteSpace(widgetName) ? "BrokerWidget" : widgetName.Trim();

        foreach (var root in BrokerAttachOptionRoots)
        {
            prefixes.Add(JoinPath(root, JoinPath(canonicalFolder, $"{canonicalWidget}.status.attach_options")));

            var legacyPrefix = JoinRawPath(root, legacyFolder, legacyWidget, "Status", "AttachOptions");
            if (!string.IsNullOrWhiteSpace(legacyPrefix))
            {
                prefixes.Add(legacyPrefix);
            }
        }

        return prefixes.ToArray();
    }

    /// <summary>
    /// Enumerates internal ItemBroker runtime candidates for a configured broker target path.
    /// </summary>
    public static IEnumerable<string> EnumerateItemBrokerRuntimeCandidates(string? path)
    {
        var normalized = NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return IsRuntimeItemServerPath(normalized)
            ? normalized
            : ToRuntimeItemServerPath(normalized);
    }

    public static bool IsDescendantPath(string? path, string? prefix)
        => TryGetRelativePath(path, prefix, out _);

    public static bool TryGetRelativePath(string? path, string? prefix, out string relativePath)
    {
        relativePath = string.Empty;

        var pathSegments = SplitPathSegments(path);
        var prefixSegments = SplitPathSegments(prefix);
        if (pathSegments.Count == 0 || prefixSegments.Count == 0 || pathSegments.Count <= prefixSegments.Count)
        {
            return false;
        }

        for (var index = 0; index < prefixSegments.Count; index++)
        {
            if (!string.Equals(pathSegments[index], prefixSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        relativePath = string.Join('.', pathSegments.Skip(prefixSegments.Count));
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    public static string ToPersistedLayoutTargetPath(string? path, string? pageName = null)
    {
        var normalized = NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "this", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var folderRelativePath = RemoveFolderContextPrefix(normalized, pageName);
        if (!string.IsNullOrWhiteSpace(folderRelativePath))
        {
            return folderRelativePath;
        }

        return normalized;
    }

    public static IEnumerable<string> EnumerateResolutionCandidates(string? path, string? pageName = null)
    {
        var normalized = NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in ExpandCandidateForms(normalized))
        {
            if (yielded.Add(candidate))
            {
                yield return candidate;
            }
        }

        if (ShouldResolveAgainstFolderRoot(normalized))
        {
            foreach (var folderRootPrefix in GetFolderRootPrefixes(pageName))
            {
                foreach (var candidate in ExpandCandidateForms(JoinPath(folderRootPrefix, normalized)))
                {
                    if (yielded.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        if (ShouldPrependProjectRoot(normalized))
        {
            foreach (var projectRootPrefix in ProjectRootSegments)
            {
                foreach (var candidate in ExpandCandidateForms(JoinPath(projectRootPrefix, normalized)))
                {
                    if (yielded.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }
    }

    public static string NormalizeChartSeriesDefinitions(string? definitions)
        => TransformChartSeriesDefinitions(definitions, NormalizeConfiguredTargetPath);

    public static string ToPersistedChartSeriesDefinitions(string? definitions, string? pageName = null)
        => TransformChartSeriesDefinitions(definitions, path => ToPersistedLayoutTargetPath(path, pageName));

    private static string TransformChartSeriesDefinitions(string? definitions, Func<string?, string> transformPath)
    {
        if (string.IsNullOrWhiteSpace(definitions))
        {
            return string.Empty;
        }

        var lines = definitions
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var normalizedLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var targetPath = transformPath(parts[0]);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                continue;
            }

            parts[0] = targetPath;
            normalizedLines.Add(string.Join('|', parts));
        }

        return string.Join(Environment.NewLine, normalizedLines);
    }

    private static bool ShouldPrependProjectRoot(string path)
    {
        var segments = SplitPathSegments(path);
        if (segments.Count == 0
            || string.Equals(path, "this", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rootSegment = segments[0];
        if (ProjectRootSegments.Contains(rootSegment, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var prefix in NonProjectRootSegments)
        {
            if (string.Equals(rootSegment, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldResolveAgainstFolderRoot(string path)
    {
        var segments = SplitPathSegments(path);
        if (segments.Count == 0
            || string.Equals(path, "this", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rootSegment = segments[0];
        if (ProjectRootSegments.Contains(rootSegment, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var prefix in NonProjectRootSegments)
        {
            if (string.Equals(rootSegment, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> GetFolderRootPrefixes(string? pageName)
    {
        var normalizedPageName = NormalizeConfiguredTargetPath(pageName);

        if (string.IsNullOrWhiteSpace(normalizedPageName))
        {
            yield break;
        }

        yield return normalizedPageName;

        foreach (var projectRootPrefix in ProjectRootSegments)
        {
            yield return JoinPath(projectRootPrefix, normalizedPageName);
        }
    }

    private static string RemoveFolderContextPrefix(string path, string? pageName)
    {
        var normalizedPageName = NormalizeConfiguredTargetPath(pageName);
        if (string.IsNullOrWhiteSpace(normalizedPageName))
        {
            return string.Empty;
        }

        var segments = SplitPathSegments(path);
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (string.Equals(segments[index], normalizedPageName, StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('.', segments.Skip(index + 1));
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> ExpandCandidateForms(string path)
    {
        var configured = NormalizeConfiguredTargetPath(path);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var legacyProjectPath = ToLegacyProjectPath(configured);
        if (!string.IsNullOrWhiteSpace(legacyProjectPath))
        {
            yield return legacyProjectPath;
        }
    }

    private static string JoinPath(string prefix, string path)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return NormalizeConfiguredTargetPath(path);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return NormalizeConfiguredTargetPath(prefix);
        }

        return $"{NormalizeConfiguredTargetPath(prefix)}.{NormalizeConfiguredTargetPath(path)}";
    }

    private static string JoinRawPath(params string?[] segments)
    {
        var normalizedSegments = segments
            .Select(NormalizePathDelimiters)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return normalizedSegments.Length == 0
            ? string.Empty
            : string.Join('.', normalizedSegments);
    }

    private static IEnumerable<string> NormalizeStudioRoot(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            yield break;
        }

        if (LegacyProjectRootSegments.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            yield return StudioRootSegment;
            foreach (var segment in segments.Skip(1))
            {
                    yield return ConvertToSnakeCaseSegment(segment);
            }

            yield break;
        }

        if (string.Equals(segments[0], StudioRootSegment, StringComparison.OrdinalIgnoreCase)
            && segments.Count > 1
            && LegacyProjectRootSegments.Contains(segments[1], StringComparer.OrdinalIgnoreCase))
        {
            yield return StudioRootSegment;
            foreach (var segment in segments.Skip(2))
            {
                yield return ConvertToSnakeCaseSegment(segment);
            }

            yield break;
        }

        foreach (var segment in segments)
        {
            yield return ConvertToSnakeCaseSegment(segment);
        }
    }

    private static string ConvertToSnakeCaseSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(segment.Length + 8);
        var previousWasSeparator = true;

        for (var index = 0; index < segment.Length; index++)
        {
            var character = segment[index];
            if (!char.IsLetterOrDigit(character))
            {
                AppendSeparator(builder, ref previousWasSeparator);
                continue;
            }

            if (char.IsUpper(character) && ShouldInsertSeparator(segment, index))
            {
                AppendSeparator(builder, ref previousWasSeparator);
            }

            builder.Append(char.ToLowerInvariant(character));
            previousWasSeparator = false;
        }

        return builder.ToString().Trim('_');
    }

    private static bool ShouldInsertSeparator(string value, int index)
    {
        if (index == 0)
        {
            return false;
        }

        var previous = value[index - 1];
        if (!char.IsLetterOrDigit(previous))
        {
            return false;
        }

        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        return index + 1 < value.Length && char.IsLower(value[index + 1]);
    }

    private static void AppendSeparator(StringBuilder builder, ref bool previousWasSeparator)
    {
        if (!previousWasSeparator && builder.Length > 0)
        {
            builder.Append('_');
        }

        previousWasSeparator = true;
    }

    private static bool IsProjectRootSegment(string segment)
        => ProjectRootSegments.Contains(segment, StringComparer.OrdinalIgnoreCase);

    private static string ToLegacyProjectPath(string? path)
    {
        var segments = SplitPathSegments(path);
        if (segments.Count <= 1 || !string.Equals(segments[0], StudioRootSegment, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Join('.', new[] { "project" }.Concat(segments.Skip(1)));
    }
}
