using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class MonitorSelectionDialogWindow : Window, INotifyPropertyChanged
{
    private readonly EditorDialogField? _field;
    private MainWindowViewModel? _viewModel;
    private string _dialogBackground = "#E3E5EE";
    private string _borderColor = "#D5D9E0";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";
    private string _buttonBackground = "#F8FAFC";
    private string _buttonBorderBrush = "#CBD5E1";
    private string _buttonForeground = "#111827";
    private string _editorDialogSectionContentBackground = "#EEF3F8";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public MonitorSelectionDialogWindow()
    {
        Rows = [];
        InitializeComponent();
        DataContext = this;
    }

    public MonitorSelectionDialogWindow(MainWindowViewModel? viewModel, EditorDialogField field)
    {
        _field = field;
        Rows = [];
        InitializeComponent();
        DataContext = this;
        AttachToViewModel(viewModel);
        BuildRows();
    }

    public ObservableCollection<MonitorSelectionRow> Rows { get; }

    public bool HasRows => Rows.Count > 0;

    public bool ShowEmptyRowsMessage => Rows.Count == 0;

    public string ToggleSelectionButtonText
        => Rows.Count > 0 && Rows.All(static row => row.IsSelected)
            ? "Unselect All"
            : "Select All";

    public string DialogBackground
    {
        get => _dialogBackground;
        private set => SetAndRaise(ref _dialogBackground, value, nameof(DialogBackground));
    }

    public string BorderColor
    {
        get => _borderColor;
        private set => SetAndRaise(ref _borderColor, value, nameof(BorderColor));
    }

    public string PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetAndRaise(ref _primaryTextBrush, value, nameof(PrimaryTextBrush));
    }

    public string SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetAndRaise(ref _secondaryTextBrush, value, nameof(SecondaryTextBrush));
    }

    public string ButtonBackground
    {
        get => _buttonBackground;
        private set => SetAndRaise(ref _buttonBackground, value, nameof(ButtonBackground));
    }

    public string ButtonBorderBrush
    {
        get => _buttonBorderBrush;
        private set => SetAndRaise(ref _buttonBorderBrush, value, nameof(ButtonBorderBrush));
    }

    public string ButtonForeground
    {
        get => _buttonForeground;
        private set => SetAndRaise(ref _buttonForeground, value, nameof(ButtonForeground));
    }

    public string EditorDialogSectionContentBackground
    {
        get => _editorDialogSectionContentBackground;
        private set => SetAndRaise(ref _editorDialogSectionContentBackground, value, nameof(EditorDialogSectionContentBackground));
    }

    protected override void OnClosed(EventArgs e)
    {
        AttachToViewModel(null);
        base.OnClosed(e);
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_field is not null)
        {
            _field.Value = MonitorRegistry.SerializeSelectedIds(Rows.Where(static row => row.IsSelected).Select(static row => row.Name));
        }

        Close();
        e.Handled = true;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void OnToggleSelectionClicked(object? sender, RoutedEventArgs e)
    {
        var selectAll = Rows.Any(static row => !row.IsSelected);
        foreach (var row in Rows)
        {
            row.IsSelected = selectAll;
        }

        RaiseRowsChanged();
        e.Handled = true;
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateThemeBindings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.DialogBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionContentBackground))
        {
            UpdateThemeBindings();
        }
    }

    private void UpdateThemeBindings()
    {
        DialogBackground = _viewModel?.DialogBackground ?? "#E3E5EE";
        BorderColor = _viewModel?.CardBorderBrush ?? "#D5D9E0";
        PrimaryTextBrush = _viewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = _viewModel?.SecondaryTextBrush ?? "#5E6777";
        ButtonBackground = _viewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = _viewModel?.EditPanelButtonBorderBrush ?? "#CBD5E1";
        ButtonForeground = _viewModel?.PrimaryTextBrush ?? "#111827";
        EditorDialogSectionContentBackground = _viewModel?.EditorDialogSectionContentBackground ?? "#EEF3F8";
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (_field is null)
        {
            RaiseRowsChanged();
            return;
        }

        var selectedIds = MonitorRegistry.ParseSelectedIds(_field.Value);
        var selectedLookup = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = _field.GetMonitorSelectionOptions();
        var knownNames = options.Select(static option => option.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var option in options)
        {
            var row = new MonitorSelectionRow(option, selectedLookup.Contains(option.Name));
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        foreach (var missingId in selectedIds.Where(selectedId => !knownNames.Contains(selectedId)))
        {
            var row = new MonitorSelectionRow(
                new MonitorSelectionOption(
                    Name: missingId,
                    Label: $"Missing rule ({missingId})",
                    Description: "This persisted rule is not currently available in the folder registry.",
                    IsMissing: true),
                isSelected: true);
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        RaiseRowsChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorSelectionRow.IsSelected))
        {
            RaiseRowsChanged();
        }
    }

    private void RaiseRowsChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRows)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEmptyRowsMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleSelectionButtonText)));
    }

    private void SetAndRaise(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class MonitorSelectionRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public MonitorSelectionRow(MonitorSelectionOption option, bool isSelected)
    {
        Option = option;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MonitorSelectionOption Option { get; }

    public string Name => Option.Name;

    public string Label => Option.Label;

    public string Description => Option.Description;

    public bool IsMissing => Option.IsMissing;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}