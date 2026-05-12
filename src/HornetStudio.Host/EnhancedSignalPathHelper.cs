using System;
using System.Collections.Generic;
using System.Linq;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Host;

internal static class EnhancedSignalPathHelper
{
    private const string StudioRootSegment = "studio";
    private static readonly string[] ProjectRootSegments = [StudioRootSegment, "project", "udl_project", "udl_book"];
    private static readonly string[] LegacyProjectRootSegments = ["project", "udl_project", "udl_book"];
    private static readonly string[] NonProjectRootSegments = ["runtime", "logs", "commands"];
    private static readonly char[] HierarchySeparators = ['.', '/'];

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

        var segments = normalized.Split(HierarchySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? string.Empty : string.Join('.', NormalizeStudioRoot(segments));
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

        return normalized.Split(HierarchySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string NormalizeComparablePath(string? path)
    {
        var segments = SplitPathSegments(path);
        return segments.Count == 0 ? string.Empty : string.Join('.', segments);
    }

    public static bool PathsEqual(string? left, string? right)
        => string.Equals(NormalizeComparablePath(left), NormalizeComparablePath(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsDescendantPath(string? path, string? prefix)
    {
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

        return true;
    }

    public static IEnumerable<string> EnumerateResolutionCandidates(string? path, string? folderName = null)
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
            foreach (var prefix in GetFolderRootPrefixes(folderName))
            {
                foreach (var candidate in ExpandCandidateForms(JoinPath(prefix, normalized)))
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

    private static IEnumerable<string> GetFolderRootPrefixes(string? folderName)
    {
        var normalizedFolder = NormalizeConfiguredTargetPath(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolder))
        {
            yield break;
        }

        yield return normalizedFolder;
        yield return JoinPath(StudioRootSegment, normalizedFolder);
        yield return JoinPath("project", normalizedFolder);
    }

    private static IEnumerable<string> ExpandCandidateForms(string path)
    {
        var normalized = NormalizeConfiguredTargetPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return normalized;
        yield return normalized.Replace('/', '.');
        yield return normalized.Replace('.', '/');

        var legacyProjectPath = ToLegacyProjectPath(normalized);
        if (!string.IsNullOrWhiteSpace(legacyProjectPath))
        {
            yield return legacyProjectPath;
            yield return legacyProjectPath.Replace('.', '/');
        }
    }

    private static bool ShouldPrependProjectRoot(string path)
    {
        var segments = SplitPathSegments(path);
        if (segments.Count == 0 || string.Equals(path, "this", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rootSegment = segments[0];
        if (ProjectRootSegments.Contains(rootSegment, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !NonProjectRootSegments.Contains(rootSegment, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ShouldResolveAgainstFolderRoot(string path)
        => ShouldPrependProjectRoot(path);

    private static string JoinPath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return NormalizeConfiguredTargetPath(right);
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return NormalizeConfiguredTargetPath(left);
        }

        return $"{NormalizeConfiguredTargetPath(left)}.{NormalizeConfiguredTargetPath(right)}";
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
                yield return HostPathSegmentNormalizer.Normalize(segment);
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
                yield return HostPathSegmentNormalizer.Normalize(segment);
            }

            yield break;
        }

        foreach (var segment in segments)
        {
            yield return HostPathSegmentNormalizer.Normalize(segment);
        }
    }

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
