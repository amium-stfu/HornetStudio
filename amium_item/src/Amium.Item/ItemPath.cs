using System.Text;

namespace Amium.Items;

/// <summary>
/// Provides canonical item path and segment normalization helpers.
/// </summary>
public static class ItemPath
{
    /// <summary>
    /// Normalizes an item path to dot-separated snake_case segments.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path
            .Replace('\\', '.')
            .Replace('/', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToSnakeCaseSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            throw new ArgumentException("Item path must contain at least one segment.", nameof(path));
        }

        return string.Join('.', segments);
    }

    /// <summary>
    /// Combines a parent path and child name into a canonical item path.
    /// </summary>
    /// <param name="path">The optional parent path.</param>
    /// <param name="name">The child item name.</param>
    /// <returns>The combined normalized path.</returns>
    public static string Combine(string? path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Normalize(name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Normalize(path);
        }

        return Normalize(path + "." + name);
    }

    /// <summary>
    /// Converts a single path or property segment to snake_case.
    /// </summary>
    /// <param name="segment">The segment to convert.</param>
    /// <returns>The converted segment.</returns>
    public static string ToSnakeCaseSegment(string segment)
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
}
