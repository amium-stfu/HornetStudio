namespace Item.Server.Monitor.ViewModels;

public sealed class MonitorTreeRowViewModel : ObservableObject
{
    private string _displayName;
    private string _valueText;
    private int _depth;
    private bool _hasChildren;
    private bool _isExpanded;
    private bool _hasSnapshot;
    private bool _isHovered;
    private bool _isSelected;

    public MonitorTreeRowViewModel(string path, string displayName, string valueText, int depth, bool hasChildren, bool isExpanded, bool hasSnapshot)
    {
        Path = path;
        _displayName = displayName;
        _valueText = valueText;
        _depth = depth;
        _hasChildren = hasChildren;
        _isExpanded = isExpanded;
        _hasSnapshot = hasSnapshot;
    }

    public string Path { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    public int Depth
    {
        get => _depth;
        private set
        {
            if (SetProperty(ref _depth, value))
            {
                OnPropertyChanged(nameof(IndentWidth));
            }
        }
    }

    public double IndentWidth => Depth * 18d;

    public bool HasChildren
    {
        get => _hasChildren;
        private set => SetProperty(ref _hasChildren, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandGlyph));
            }
        }
    }

    public bool HasSnapshot
    {
        get => _hasSnapshot;
        private set => SetProperty(ref _hasSnapshot, value);
    }

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    public bool IsHovered
    {
        get => _isHovered;
        set => SetProperty(ref _isHovered, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void UpdateFrom(MonitorTreeRowViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        DisplayName = source.DisplayName;
        ValueText = source.ValueText;
        Depth = source.Depth;
        HasChildren = source.HasChildren;
        IsExpanded = source.IsExpanded;
        HasSnapshot = source.HasSnapshot;
    }
}
