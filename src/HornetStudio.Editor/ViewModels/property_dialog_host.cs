using System.ComponentModel;
using System.Collections.ObjectModel;

namespace HornetStudio.Editor.ViewModels;

public interface IPropertyDialogHost : INotifyPropertyChanged
{
    MainWindowViewModel? OwnerViewModel { get; }

    string DialogBackground { get; }

    string CardBorderBrush { get; }

    string ParameterEditBackgrundColor { get; }

    string ParameterEditForeColor { get; }

    string ParameterHoverColor { get; }

    string EditPanelButtonBackground { get; }

    string EditPanelButtonBorderBrush { get; }

    string PrimaryTextBrush { get; }

    string SecondaryTextBrush { get; }

    string TabSelectBackColor { get; }

    string TabSelectForeColor { get; }

    string TabBackColor { get; }

    string TabForeColor { get; }

    string EditorDialogSectionHeaderBackground { get; }

    string EditorDialogSectionHeaderForeground { get; }

    string EditorDialogSectionHeaderBorderBrush { get; }

    string EditorDialogSectionContentBackground { get; }

    string EditorDialogTitle { get; }

    string EditorDialogError { get; }

    bool HasEditorDialogError { get; }

    ObservableCollection<EditorDialogSection> EditorDialogSections { get; }

    ObservableCollection<EditorDialogField> EditorDialogActionFields { get; }

    bool HasEditorDialogActionFields { get; }

    bool ShowEditorDialogActionPlaceholder { get; }

    void CommitEditorDialog();

    void ApplyEditorDialog();

    void CancelEditorDialog();

    void EnsureEditorDialogSectionExpanded(EditorDialogSection section);

    string? GetPythonScriptTargetPath(EditorDialogField field, string extension);

    void CreatePythonScriptForField(EditorDialogField field, bool overwriteExisting);

    string? GetPythonTemplatesDirectory();

    void CopyPythonTemplateForField(EditorDialogField field, string templatePath, bool overwriteExisting);
}