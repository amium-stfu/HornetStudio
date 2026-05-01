namespace HornetStudio.Editor.ViewModels;

/// <summary>
/// Represents one row in the attach-items editor.
/// </summary>
public sealed class AttachItemEditorRow : ObservableObject
{
    private bool _isAttached;
    private bool _isRemoved;
    private int _intervalMs;

    private string[] ParsedParts => RelativePath.Split('|', System.StringSplitOptions.TrimEntries);

    /// <summary>
    /// The raw option/relative path string as provided by the caller.
    /// For Csv/Sql signal selection this is typically "Name|Path" or "Name|Path|Unit".
    /// For other attach lists (e.g. UDL client) this is usually just the relative path.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display text shown for tree-oriented attach rows.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the compact source text shown for tree-oriented attach rows.
    /// </summary>
    public string DisplaySource { get; init; } = string.Empty;

    /// <summary>
    /// Gets the indentation level for tree-oriented attach rows.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets a value indicating whether this row is a non-attachable tree group.
    /// </summary>
    public bool IsGroup { get; init; }

    /// <summary>
    /// Gets a value indicating whether the row represents a saved path that is not currently live.
    /// </summary>
    public bool IsMissing { get; init; }

    /// <summary>
    /// Gets the row left margin derived from <see cref="Level"/>.
    /// </summary>
    public double Indent => Level * 16d;

    /// <summary>
    /// Gets the left margin used to indent tree rows.
    /// </summary>
    public Avalonia.Thickness IndentMargin => new(Indent, 0, 0, 0);

    /// <summary>
    /// Gets a value indicating whether the row can be selected for attachment.
    /// </summary>
    public bool CanAttach => !IsGroup && !IsRemoved;

    /// <summary>
    /// Gets a value indicating whether the missing-row remove action is visible.
    /// </summary>
    public bool CanRemoveMissing => IsMissing && !IsGroup && !IsRemoved;

    /// <summary>
    /// Gets a compact label for the row.
    /// </summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Source)
        ? Name
        : $"{Name}|{Source}";

    /// <summary>
    /// Gets the display name parsed from <see cref="RelativePath"/>.
    /// </summary>
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            var parts = ParsedParts;
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            if (parts.Length == 1)
            {
                return parts[0];
            }

            return parts[0];
        }
    }

    /// <summary>
    /// Gets the source/path text for the row.
    /// </summary>
    public string Source
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                if (IsGroup)
                {
                    return string.Empty;
                }

                return IsMissing ? "Missing saved attachment" : DisplaySource;
            }

            var parts = ParsedParts;
            return parts.Length > 1 ? parts[1] : RelativePath;
        }
    }

    /// <summary>
    /// Gets the optional unit parsed from <see cref="RelativePath"/>.
    /// </summary>
    public string Unit
    {
        get
        {
            var parts = ParsedParts;
            return parts.Length > 2 ? parts[2] : string.Empty;
        }
    }

    /// <summary>
    /// Gets or sets whether the item is attached.
    /// </summary>
    public bool IsAttached
    {
        get => _isAttached;
        set
        {
            if (IsGroup || IsRemoved)
            {
                value = false;
            }

            SetProperty(ref _isAttached, value);
        }
    }

    /// <summary>
    /// Gets or sets whether the row was removed from the saved attach list.
    /// </summary>
    public bool IsRemoved
    {
        get => _isRemoved;
        set
        {
            if (!SetProperty(ref _isRemoved, value))
            {
                return;
            }

            if (value)
            {
                IsAttached = false;
            }

            RaisePropertyChanged(nameof(CanAttach));
            RaisePropertyChanged(nameof(CanRemoveMissing));
        }
    }

    /// <summary>
    /// Optional per-item interval in milliseconds. Used by SqlLogger when configured
    /// via the CsvSignalPaths attach-list field. Ignored for other attach-list usages.
    /// </summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set => SetProperty(ref _intervalMs, value);
    }
}
