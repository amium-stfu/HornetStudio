using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.ViewModels;

public sealed class PropertyDialogSessionRequestedEventArgs(MainWindowViewModel.PropertyDialogSessionViewModel session) : EventArgs
{
    public MainWindowViewModel.PropertyDialogSessionViewModel Session { get; } = session;
}

public partial class MainWindowViewModel
{
    public event EventHandler<PropertyDialogSessionRequestedEventArgs>? PropertyDialogSessionRequested;

    MainWindowViewModel? IPropertyDialogHost.OwnerViewModel => this;

    private void RequestPropertyDialogSession(FolderItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        PropertyDialogSessionRequested?.Invoke(
            this,
            new PropertyDialogSessionRequestedEventArgs(new PropertyDialogSessionViewModel(this, item)));
    }

    public sealed class PropertyDialogSessionViewModel : ObservableObject, IPropertyDialogHost, IDisposable
    {
        private readonly MainWindowViewModel _owner;
        private readonly FolderItemModel _item;
        private string _editorDialogTitle;
        private string _editorDialogError = string.Empty;
        private bool _disposed;
        private bool _isRefreshingFields;

        public PropertyDialogSessionViewModel(MainWindowViewModel owner, FolderItemModel item)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _editorDialogTitle = $"Edit {item.Name}";

            EditorDialogSections = [];
            EditorDialogActionFields = [];

            _owner.PropertyChanged += OnOwnerPropertyChanged;
            BuildSections();
            BuildActionFields();
            RefreshChoiceOptions();
        }

        public event EventHandler? CloseRequested;

        public MainWindowViewModel? OwnerViewModel => _owner;

        public ObservableCollection<EditorDialogSection> EditorDialogSections { get; }

        public ObservableCollection<EditorDialogField> EditorDialogActionFields { get; }

        public bool HasEditorDialogActionFields => EditorDialogActionFields.Count > 0;

        public bool ShowEditorDialogActionPlaceholder => !HasEditorDialogActionFields;

        public string DialogBackground => _owner.DialogBackground;

        public string CardBorderBrush => _owner.CardBorderBrush;

        public string ParameterEditBackgrundColor => _owner.ParameterEditBackgrundColor;

        public string ParameterEditForeColor => _owner.ParameterEditForeColor;

        public string ParameterHoverColor => _owner.ParameterHoverColor;

        public string EditPanelButtonBackground => _owner.EditPanelButtonBackground;

        public string EditPanelButtonBorderBrush => _owner.EditPanelButtonBorderBrush;

        public string PrimaryTextBrush => _owner.PrimaryTextBrush;

        public string SecondaryTextBrush => _owner.SecondaryTextBrush;

        public string TabSelectBackColor => _owner.TabSelectBackColor;

        public string TabSelectForeColor => _owner.TabSelectForeColor;

        public string TabBackColor => _owner.TabBackColor;

        public string TabForeColor => _owner.TabForeColor;

        public string EditorDialogSectionHeaderBackground => _owner.EditorDialogSectionHeaderBackground;

        public string EditorDialogSectionHeaderForeground => _owner.EditorDialogSectionHeaderForeground;

        public string EditorDialogSectionHeaderBorderBrush => _owner.EditorDialogSectionHeaderBorderBrush;

        public string EditorDialogSectionContentBackground => _owner.EditorDialogSectionContentBackground;

        public string EditorDialogTitle
        {
            get => _editorDialogTitle;
            private set => SetProperty(ref _editorDialogTitle, value);
        }

        public string EditorDialogError
        {
            get => _editorDialogError;
            private set
            {
                if (SetProperty(ref _editorDialogError, value))
                {
                    RaisePropertyChanged(nameof(HasEditorDialogError));
                }
            }
        }

        public bool HasEditorDialogError => !string.IsNullOrWhiteSpace(EditorDialogError);

        public void CommitEditorDialog()
            => CommitCore(closeAfterSave: true);

        public void ApplyEditorDialog()
            => CommitCore(closeAfterSave: false);

        public void CancelEditorDialog()
        {
            EditorDialogError = string.Empty;
            RequestClose();
        }

        public void EnsureEditorDialogSectionExpanded(EditorDialogSection section)
        {
            ArgumentNullException.ThrowIfNull(section);
            section.BeginLoadFields();
        }

        public string? GetPythonScriptTargetPath(EditorDialogField field, string extension)
            => _owner.GetPythonScriptTargetPath(field, extension);

        public void CreatePythonScriptForField(EditorDialogField field, bool overwriteExisting)
            => _owner.CreatePythonScriptForField(field, overwriteExisting);

        public string? GetPythonTemplatesDirectory()
            => _owner.GetPythonTemplatesDirectory();

        public void CopyPythonTemplateForField(EditorDialogField field, string templatePath, bool overwriteExisting)
            => _owner.CopyPythonTemplateForField(field, templatePath, overwriteExisting);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.PropertyChanged -= OnOwnerPropertyChanged;

            foreach (var field in EnumerateFields())
            {
                field.PropertyChanged -= OnFieldPropertyChanged;
            }
        }

        private void BuildSections()
        {
            EditorDialogSections.Clear();

            foreach (var sectionBinding in _owner.BuildBindingSectionsForItem(_item))
            {
                var section = new EditorDialogSection(
                    sectionBinding.Title,
                    isExpanded: string.Equals(sectionBinding.Title, "Identity", StringComparison.Ordinal));
                section.FieldsLoaded += RefreshChoiceOptions;

                foreach (var binding in sectionBinding.Bindings)
                {
                    section.AddFieldFactory(() => CreateConfiguredField(binding));
                }

                EditorDialogSections.Add(section);
                if (section.IsExpanded)
                {
                    section.BeginLoadFields();
                }
            }
        }

        private void BuildActionFields()
        {
            EditorDialogActionFields.Clear();

            foreach (var field in _owner.BuildActionFieldsForItem(_item))
            {
                field.OwnerWorkspaceDirectory = _owner.ResolveWorkspaceDirectory(_item);
                field.PropertyChanged += OnFieldPropertyChanged;
                EditorDialogActionFields.Add(field);
            }

            RaisePropertyChanged(nameof(HasEditorDialogActionFields));
            RaisePropertyChanged(nameof(ShowEditorDialogActionPlaceholder));
        }

        private EditorDialogField CreateConfiguredField(EditorDialogBindingDefinition binding)
        {
            var field = binding.CreateField(_item);
            field.IsVisible = ShouldShowEditorDialogField(_item, field.Key);
            if (string.Equals(field.Key, "Name", StringComparison.Ordinal))
            {
                field.IsReadOnly = true;
            }

            if (string.Equals(field.Key, "ControlCaption", StringComparison.Ordinal) && _item.SyncText)
            {
                field.IsReadOnly = true;
            }

            field.OwnerWorkspaceDirectory = _owner.ResolveWorkspaceDirectory(_item);
            field.PropertyChanged += OnFieldPropertyChanged;
            return field;
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.PropertyName))
            {
                return;
            }

            RaisePropertyChanged(e.PropertyName);
        }

        private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isRefreshingFields
                || e.PropertyName != nameof(EditorDialogField.Value)
                || sender is not EditorDialogField field)
            {
                return;
            }

            if (string.Equals(field.Key, "Name", StringComparison.Ordinal))
            {
                var pathField = FindField("Path");
                if (pathField is not null)
                {
                    pathField.Value = _owner.BuildPreviewPath(_item, field.Value);
                }

                EditorDialogTitle = $"Edit {field.Value}";
            }

            if (string.Equals(field.Key, "Text", StringComparison.Ordinal))
            {
                var syncField = FindField("SyncText");
                var captionField = FindField("ControlCaption");
                if (captionField is not null
                    && syncField is not null
                    && string.Equals(syncField.Value, "True", StringComparison.OrdinalIgnoreCase))
                {
                    captionField.Value = field.Value;
                }
            }

            if (string.Equals(field.Key, "SyncText", StringComparison.Ordinal))
            {
                var captionField = FindField("ControlCaption");
                if (captionField is not null)
                {
                    var syncText = string.Equals(field.Value, "True", StringComparison.OrdinalIgnoreCase);
                    captionField.IsReadOnly = syncText;
                    if (syncText)
                    {
                        var textField = FindField("Text");
                        if (textField is not null)
                        {
                            captionField.Value = textField.Value;
                        }
                    }
                }
            }

            if (string.Equals(field.Key, "TargetPath", StringComparison.Ordinal))
            {
                RefreshChoiceOptions();
            }

            if (field.Key is "TargetPropertyFormatKind" or "TargetParameterFormatKind")
            {
                UpdateFormatPropertyField(field.Value);
            }

            EditorDialogError = string.Empty;
        }

        private void UpdateFormatPropertyField(string selectedFormatKind)
        {
            var formatField = FindField("TargetPropertyFormatProperty") ?? FindField("TargetParameterFormatParameter");
            if (formatField is null)
            {
                return;
            }

            if (!MainWindowViewModel.FormatUsesParameter(selectedFormatKind))
            {
                formatField.Value = string.Empty;
            }

            formatField.ToolTipText = MainWindowViewModel.GetFormatPropertyToolTip(selectedFormatKind);
        }

        private void RefreshChoiceOptions()
        {
            var wasRefreshing = _isRefreshingFields;
            _isRefreshingFields = true;
            try
            {
                foreach (var field in EnumerateFields())
                {
                    if (field.IsAttachItemList)
                    {
                        var attachOptions = field.Definition.OptionsFactory is null
                            ? []
                            : field.Definition.OptionsFactory(_item);
                        field.RefreshAttachItemOptions(attachOptions);
                        continue;
                    }

                    if (field.IsTargetTree)
                    {
                        var targetOptions = field.Definition.OptionsFactory is null
                            ? []
                            : field.Definition.OptionsFactory(_item);
                        field.RefreshTargetTreeOptions(targetOptions);
                        continue;
                    }

                    if (field.IsChartSeriesList)
                    {
                        var chartOptions = field.Definition.OptionsFactory is null
                            ? []
                            : field.Definition.OptionsFactory(_item);
                        field.RefreshChartSeriesOptions(chartOptions);
                        continue;
                    }

                    if (field.IsInteractionRuleList)
                    {
                        field.RefreshInteractionRuleOptions(
                            _owner.GetSelectableTargetOptions(_item),
                            _owner.GetSelectableApplicationOptions(_item),
                            _owner.GetSelectableDialogWidgetOptions(_item));
                        continue;
                    }

                    if (!field.IsChoice)
                    {
                        continue;
                    }

                    var selectedTargetPath = GetSelectedTargetPath();
                    var choiceOptions = field.Key switch
                    {
                        "TargetPropertyPath" or "TargetParameterPath" => GetTargetPropertyOptions(selectedTargetPath, _item.FolderName),
                        _ when field.Definition.OptionsFactory is not null => field.Definition.OptionsFactory(_item),
                        _ => []
                    };

                    var selectFirstWhenInvalid = (field.Key == "TargetPropertyPath" || field.Key == "TargetParameterPath")
                        && !string.IsNullOrWhiteSpace(selectedTargetPath);
                    RefreshDialogFieldOptions(field, choiceOptions, selectFirstWhenInvalid);
                }
            }
            finally
            {
                _isRefreshingFields = wasRefreshing;
            }

            foreach (var field in EnumerateFields())
            {
                field.IsVisible = ShouldShowEditorDialogField(_item, field.Key);
            }
        }

        private string GetSelectedTargetPath()
            => FindField("TargetPath")?.Value ?? _item.TargetPath ?? string.Empty;

        private void CommitCore(bool closeAfterSave)
        {
            var page = _owner.FindOwningPage(_item) ?? _owner.SelectedFolder;
            if (page is null)
            {
                EditorDialogError = "Owning page was not found.";
                return;
            }

            var nameField = FindField("Name");
            if (nameField is null)
            {
                EditorDialogError = "Name field is missing.";
                return;
            }

            if (!_owner.TryValidateControlName(nameField.Value, page, _item, out var normalizedName, out var error))
            {
                EditorDialogError = error;
                _owner.StatusText = error;
                return;
            }

            foreach (var field in EnumerateFields())
            {
                if (field.IsReadOnly)
                {
                    continue;
                }

                var valueToApply = string.Equals(field.Key, "Name", StringComparison.Ordinal) ? normalizedName : field.Value;
                var applyError = field.Definition.Apply(_item, valueToApply);
                if (!string.IsNullOrWhiteSpace(applyError))
                {
                    EditorDialogError = applyError!;
                    _owner.StatusText = applyError!;
                    return;
                }
            }

            _item.ApplyTheme(_owner.IsDarkTheme);
            if (_item.Kind is ControlKind.Signal or ControlKind.ItemModel)
            {
                _owner.SyncSignalHistory(page);
            }

            EditorDialogError = string.Empty;
            _owner.StatusText = $"Control saved: {_item.Path}";

            if (closeAfterSave)
            {
                RequestClose();
                return;
            }

            RefreshLoadedFieldValues();
            RefreshChoiceOptions();
        }

        private void RefreshLoadedFieldValues()
        {
            var wasRefreshing = _isRefreshingFields;
            _isRefreshingFields = true;
            try
            {
                foreach (var field in EnumerateFields())
                {
                    field.Value = field.Definition.ReadValue(_item);
                    field.ToolTipText = field.Definition.ToolTipFactory?.Invoke(_item) ?? string.Empty;
                    field.IsVisible = ShouldShowEditorDialogField(_item, field.Key);
                }

                var captionField = FindField("ControlCaption");
                if (captionField is not null)
                {
                    captionField.IsReadOnly = _item.SyncText;
                }

                EditorDialogTitle = $"Edit {_item.Name}";
            }
            finally
            {
                _isRefreshingFields = wasRefreshing;
            }
        }

        private EditorDialogField? FindField(string key)
            => EnumerateFields().FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.Ordinal));

        private IEnumerable<EditorDialogField> EnumerateFields()
            => EditorDialogSections.SelectMany(static section => section.Fields).Concat(EditorDialogActionFields);

        private void RequestClose()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}