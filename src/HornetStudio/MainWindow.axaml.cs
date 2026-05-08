using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HornetStudio.Host;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Widgets;
using HornetStudio.Editor.Models;
using HornetStudio.ViewModels;

namespace HornetStudio;

public partial class MainWindow : Window
{
    private const string LegendFolderDragDataFormat = "application/x-hornetstudio-folder";
    private LogWindow? _logWindow;
    private ItemTreeWindow? _itemTreeWindow;
    private Window? _keyboardWindow;
    private TextBox? _keyboardTarget;
    private Action? _keyboardApplyAction;
    private Action? _keyboardCancelAction;
    private MainWindowViewModel? _boundViewModel;
    private Point? _legendDragStartPoint;
    private FolderModel? _legendDragSourceFolder;
    private bool _legendDragInProgress;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWindowIcon();
        Core.UiStateChanged += HandleHostUiStateChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeWindowIcon()
    {
        var uri = new Uri("avares://HornetStudio.Editor/EditorIcons/Hornet.png");

        try
        {
            using var stream = AssetLoader.Open(uri);
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch
        {
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.ResolveDuplicatePageNameAsync = null;
        }

        _boundViewModel = DataContext as MainWindowViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.ResolveDuplicatePageNameAsync = PromptForDuplicatePageNameAsync;
        }
    }

    private async System.Threading.Tasks.Task<string?> PromptForDuplicatePageNameAsync(string currentName, string message)
    {
        return await EditorInputDialogs.EditTextAsync(
            this,
            header: "Duplicate folder name",
            subHeader: message,
            initialValue: currentName);
    }

    private async void OnViewsButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not FolderModel page)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var menu = new ContextMenu();
        var views = page.Views ?? new System.Collections.Generic.Dictionary<int, string>();

        var orderedViews = views.Count > 0
            ? views.OrderBy(v => v.Key)
            : System.Linq.Enumerable.Empty<System.Collections.Generic.KeyValuePair<int, string>>();

        foreach (var kvp in orderedViews)
        {
            var viewId = kvp.Key;
            var header = string.IsNullOrWhiteSpace(kvp.Value) ? $"Screen {viewId}" : kvp.Value;

            var item = new MenuItem
            {
                Header = header,
                IsChecked = page.ActualViewId == viewId,
                StaysOpenOnClick = false
            };

            item.Click += (_, _) =>
            {
                page.ActualViewId = viewId;
            };

            menu.Items.Add(item);
        }

        if (viewModel.IsEditMode)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            async System.Threading.Tasks.Task AddViewInRangeAsync(string header, int startIdInclusive, int endIdInclusive)
            {
                var name = await EditorInputDialogs.EditTextAsync(
                    this,
                    header: header,
                    subHeader: "Enter screen name",
                    initialValue: string.Empty);

                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (page.Views is null)
                {
                    return;
                }

                int? freeId = null;
                for (var id = startIdInclusive; id <= endIdInclusive; id++)
                {
                    if (!page.Views.ContainsKey(id))
                    {
                        freeId = id;
                        break;
                    }
                }

                if (freeId is null)
                {
                    return;
                }

                page.UpdateViewCaption(freeId.Value, name);
                page.ActualViewId = freeId.Value;
            }

            void AddRangeMenuItem(string header, int startIdInclusive, int endIdInclusive)
            {
                var item = new MenuItem
                {
                    Header = header
                };

                item.Click += async (_, _) =>
                {
                    await AddViewInRangeAsync(header, startIdInclusive, endIdInclusive);
                };

                menu.Items.Add(item);
            }

            AddRangeMenuItem("Add new user screen", 1, 10);
            AddRangeMenuItem("Add new service screen", 11, 20);
            AddRangeMenuItem("Add new admin screen", 20, 30);
        }

        // Kontextmenü korrekt an den Button anhängen, damit Avalonia eine
        // gültige Visual-Root hat und kein ArgumentNullException wirft.
        menu.PlacementTarget = button;
        button.ContextMenu = menu;

        void Cleanup(object? _, RoutedEventArgs _e)
        {
            menu.Closed -= Cleanup;
            if (ReferenceEquals(button.ContextMenu, menu))
            {
                button.ContextMenu = null;
            }
        }

        menu.Closed += Cleanup;
        menu.Open();
    }

    private async void OnPageTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsEditMode)
        {
            return;
        }

        if (sender is not TextBlock textBlock || textBlock.DataContext is not FolderModel page)
        {
            return;
        }

        if (!page.IsSelected)
        {
            return;
        }

        var currentTitle = page.TabTitle;

        var newTitle = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Edit folder title",
            subHeader: "Title",
            initialValue: currentTitle);

        if (string.IsNullOrWhiteSpace(newTitle))
        {
            return;
        }

        page.DisplayText = newTitle;
    }

    private async void OnViewCaptionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsEditMode)
        {
            return;
        }

        if (sender is not TextBlock textBlock || textBlock.DataContext is not FolderModel page)
        {
            return;
        }

        if (!page.IsSelected)
        {
            return;
        }

        var viewId = page.ActualViewId;
        if (viewId <= 0)
        {
            return;
        }

        page.Views.TryGetValue(viewId, out var currentCaption);

        var newCaption = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Edit screen text",
            subHeader: "Screen text",
            initialValue: currentCaption ?? string.Empty);

        if (string.IsNullOrWhiteSpace(newCaption))
        {
            return;
        }

        page.UpdateViewCaption(viewId, newCaption);
    }

    private async void OnAddNewPageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var pageName = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Add new folder",
            subHeader: "Folder name",
            initialValue: string.Empty);

        if (string.IsNullOrWhiteSpace(pageName))
        {
            return;
        }

        if (!viewModel.TryCreateNewPage(pageName, out _, out var errorMessage) && !string.IsNullOrWhiteSpace(errorMessage))
        {
            await EditorInputDialogs.EditTextAsync(
                this,
                header: "Folder creation blocked",
                subHeader: errorMessage,
                initialValue: string.Empty);
        }
    }

    private void OnLegendItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsEditMode)
        {
            _legendDragStartPoint = null;
            _legendDragSourceFolder = null;
            return;
        }

        if (sender is not Control control || control.DataContext is not FolderModel folder)
        {
            _legendDragStartPoint = null;
            _legendDragSourceFolder = null;
            return;
        }

        _legendDragStartPoint = e.GetPosition(control);
        _legendDragSourceFolder = folder;
    }

    private async void OnLegendItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_legendDragInProgress
            || _legendDragStartPoint is null
            || _legendDragSourceFolder is null
            || DataContext is not MainWindowViewModel viewModel
            || !viewModel.IsEditMode)
        {
            return;
        }

        if (sender is not Control control || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(control);
        var delta = currentPoint - _legendDragStartPoint.Value;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
        {
            return;
        }

        var data = new DataObject();
        data.Set(LegendFolderDragDataFormat, _legendDragSourceFolder.Name);

        _legendDragInProgress = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally
        {
            if (DataContext is MainWindowViewModel dragViewModel)
            {
                dragViewModel.IsDeleteFolderDropTargetActive = false;
            }

            Cursor = null;
            _legendDragInProgress = false;
            _legendDragStartPoint = null;
            _legendDragSourceFolder = null;
        }
    }

    private void OnLegendItemDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!viewModel.IsEditMode)
        {
            ClearLegendDropMarkers(viewModel);
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourceFolder = ResolveDraggedFolder(e.Data, viewModel);
        var targetFolder = (sender as Control)?.DataContext as FolderModel;
        if (sourceFolder is null || targetFolder is null || ReferenceEquals(sourceFolder, targetFolder))
        {
            ClearLegendDropMarkers(viewModel);
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        UpdateLegendDropMarkers(viewModel, targetFolder, ShouldInsertAfter((Control)sender!, e, viewModel));
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnLegendItemDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        ClearLegendDropMarkers(viewModel);
    }

    private void OnLegendItemDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not Control control || control.DataContext is not FolderModel targetFolder)
        {
            return;
        }

        var sourceFolder = ResolveDraggedFolder(e.Data, viewModel);
        if (sourceFolder is null || ReferenceEquals(sourceFolder, targetFolder))
        {
            ClearLegendDropMarkers(viewModel);
            return;
        }

        var insertAfter = ShouldInsertAfter(control, e, viewModel);
        if (viewModel.TryMoveFolder(sourceFolder, targetFolder, insertAfter))
        {
            e.Handled = true;
        }

        ClearLegendDropMarkers(viewModel);
    }

    private void OnLegendDeleteDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!viewModel.IsEditMode)
        {
            viewModel.IsDeleteFolderDropTargetActive = false;
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourceFolder = ResolveDraggedFolder(e.Data, viewModel);
        viewModel.IsDeleteFolderDropTargetActive = sourceFolder is not null;
        e.DragEffects = sourceFolder is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnLegendDeleteDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsDeleteFolderDropTargetActive = false;
        }
    }

    private async void OnLegendDeleteDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.IsDeleteFolderDropTargetActive = false;
        var sourceFolder = ResolveDraggedFolder(e.Data, viewModel);
        if (sourceFolder is null)
        {
            return;
        }

        var confirmed = await EditorInputDialogs.ConfirmAsync(
            this,
            $"Delete folder '{sourceFolder.TabTitle}'?",
            "The folder will be fully removed from the project, active UDL and Python clients will be stopped, references in project.aaep will be updated, and the folder directory will be moved to the Recycle Bin. This action cannot be undone from inside the application.",
            confirmText: "Delete",
            cancelText: "Cancel");

        if (!confirmed)
        {
            e.Handled = true;
            return;
        }

        if (!viewModel.TryDeleteFolder(sourceFolder, out var errorMessage) && !string.IsNullOrWhiteSpace(errorMessage))
        {
            await EditorInputDialogs.EditTextAsync(
                this,
                "Folder deletion failed",
                errorMessage,
                string.Empty);
        }

        e.Handled = true;
    }

    private static FolderModel? ResolveDraggedFolder(IDataObject dataObject, MainWindowViewModel viewModel)
    {
        if (!dataObject.Contains(LegendFolderDragDataFormat))
        {
            return null;
        }

        var folderName = dataObject.Get(LegendFolderDragDataFormat) as string;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        return viewModel.Folders.FirstOrDefault(folder => string.Equals(folder.Name, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldInsertAfter(Control control, DragEventArgs e, MainWindowViewModel viewModel)
    {
        var position = e.GetPosition(control);
        var bounds = control.Bounds;

        return viewModel.IsTopTabStripPlacement
            ? position.X >= bounds.Width / 2d
            : position.Y >= bounds.Height / 2d;
    }

    private static void UpdateLegendDropMarkers(MainWindowViewModel viewModel, FolderModel targetFolder, bool insertAfter)
    {
        ClearLegendDropMarkers(viewModel);

        if (viewModel.IsTopTabStripPlacement)
        {
            targetFolder.ShowDropMarkerLeft = !insertAfter;
            targetFolder.ShowDropMarkerRight = insertAfter;
            return;
        }

        targetFolder.ShowDropMarkerTop = !insertAfter;
        targetFolder.ShowDropMarkerBottom = insertAfter;
    }

    private static void ClearLegendDropMarkers(MainWindowViewModel viewModel)
    {
        foreach (var folder in viewModel.Folders)
        {
            folder.ShowDropMarkerLeft = false;
            folder.ShowDropMarkerRight = false;
            folder.ShowDropMarkerTop = false;
            folder.ShowDropMarkerBottom = false;
        }
    }

    private void OnMainMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        if (control.ContextMenu is { } menu)
        {
            menu.PlacementTarget = control;
            menu.Open();
        }
    }

    private async void UserLogin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var password = await ShowUserPasswordDialogAsync();
        if (password is null)
        {
            return;
        }

        viewModel.TrySetUserByPassword(password);
    }

    private Window EnsureKeyboardWindow()
    {
        if (_keyboardWindow is { } existing)
        {
            return existing;
        }

        var keyboardHeight = Height * 0.25;
        var window = new Window
        {
            Width = Width,
            Height = keyboardHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            Topmost = true
        };

        var pad = new EditorTextInputPad
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        pad.KeyInvoked += OnKeyboardKeyInvoked;
        pad.ActionInvoked += OnKeyboardActionInvoked;

        window.Content = pad;
        window.Closed += (_, _) => _keyboardWindow = null;

        _keyboardWindow = window;
        return window;
    }

    private void ShowKeyboard(TextBox target, Action applyAction, Action cancelAction)
    {
        _keyboardTarget = target;
        _keyboardApplyAction = applyAction;
        _keyboardCancelAction = cancelAction;

        var window = EnsureKeyboardWindow();

        var mainWidth = Width;
        var mainHeight = Height;
        var keyboardHeight = window.Height;

        window.Width = mainWidth;

        var topOffset = mainHeight - keyboardHeight;
        if (topOffset < 0)
        {
            topOffset = 0;
        }

        window.Position = new PixelPoint(Position.X, Position.Y + (int)topOffset);

        if (!window.IsVisible)
        {
            window.Show();
        }
        else
        {
            window.Activate();
        }
    }

    private void HideKeyboard()
    {
        _keyboardTarget = null;
        _keyboardApplyAction = null;
        _keyboardCancelAction = null;
        _keyboardWindow?.Hide();
    }

    private void OnKeyboardKeyInvoked(object? sender, string key)
    {
        if (_keyboardTarget is null)
        {
            return;
        }

        InsertTextAtCaret(_keyboardTarget, key);
        _keyboardTarget.Focus();
    }

    private void OnKeyboardActionInvoked(object? sender, string action)
    {
        if (_keyboardTarget is null)
        {
            return;
        }

        switch (action)
        {
            case "Backspace":
                BackspaceAtCaret(_keyboardTarget);
                _keyboardTarget.Focus();
                break;
            case "MoveLeft":
                MoveCaret(_keyboardTarget, -1);
                _keyboardTarget.Focus();
                break;
            case "Apply":
                _keyboardApplyAction?.Invoke();
                _keyboardTarget.Focus();
                break;
            case "Cancel":
                _keyboardCancelAction?.Invoke();
                break;
        }
    }

    private static void InsertTextAtCaret(TextBox input, string value)
    {
        var text = input.Text ?? string.Empty;
        var start = input.SelectionStart;
        var end = input.SelectionEnd;

        if (end > start)
        {
            text = text.Remove(start, end - start);
        }

        text = text.Insert(start, value);
        input.Text = text;

        var caret = start + value.Length;
        input.CaretIndex = caret;
        input.SelectionStart = caret;
        input.SelectionEnd = caret;
    }

    private static void BackspaceAtCaret(TextBox input)
    {
        var text = input.Text ?? string.Empty;
        var start = input.SelectionStart;
        var end = input.SelectionEnd;

        if (end > start)
        {
            text = text.Remove(start, end - start);
            input.Text = text;
            input.CaretIndex = start;
        }
        else if (start > 0)
        {
            text = text.Remove(start - 1, 1);
            input.Text = text;
            var caret = start - 1;
            input.CaretIndex = caret;
        }

        input.SelectionStart = input.CaretIndex;
        input.SelectionEnd = input.CaretIndex;
    }

    private static void MoveCaret(TextBox input, int delta)
    {
        var textLength = (input.Text ?? string.Empty).Length;
        var target = input.CaretIndex + delta;
        if (target < 0)
        {
            target = 0;
        }
        else if (target > textLength)
        {
            target = textLength;
        }

        input.CaretIndex = target;
        input.SelectionStart = target;
        input.SelectionEnd = target;
    }

    private async System.Threading.Tasks.Task<string?> ShowUserPasswordDialogAsync()
    {
        // Verwende den neuen, gemeinsamen Text-Dialog im Passwortmodus.
        return await EditorInputDialogs.EditTextAsync(
            this,
            header: "User login",
            subHeader: "Enter password",
            initialValue: string.Empty,
            isPassword: true);
    }

    private async void ChangePassword_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await ShowChangePasswordDialogAsync(viewModel);
    }

    private async System.Threading.Tasks.Task ShowChangePasswordDialogAsync(MainWindowViewModel viewModel)
    {
        // Verwende hintereinander die neuen Passwort-Dialoge
        // für Alt-Kennwort, neues Kennwort und Wiederholung.

        var oldPassword = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Change password",
            subHeader: "Enter old password",
            initialValue: string.Empty,
            isPassword: true);

        if (oldPassword is null)
        {
            return;
        }

        var newPassword = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Change password",
            subHeader: "Enter new password",
            initialValue: string.Empty,
            isPassword: true);

        if (newPassword is null)
        {
            return;
        }

        var repeatPassword = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Change password",
            subHeader: "Repeat new password",
            initialValue: string.Empty,
            isPassword: true);

        if (repeatPassword is null)
        {
            return;
        }

        if (!viewModel.TryChangeCurrentUserPassword(oldPassword, newPassword, repeatPassword, out var error))
        {
            // Fehlertext kurz anzeigen, ohne eigenen Spezialdialog zu bauen.
            await EditorInputDialogs.EditTextAsync(
                this,
                header: "Change password failed",
                subHeader: error,
                initialValue: string.Empty);
        }
    }

    private void ResetPasswords_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ResetAllUserPasswords();
    }

    private void Logout_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.TrySetUserByPassword(null);
    }

    private async void NewBook_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select folder for new HornetStudio project",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        var folderPath = TryGetStoragePath(folders[0]);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var firstFolderName = await EditorInputDialogs.EditTextAsync(
            this,
            header: "Create project",
            subHeader: "Enter the first folder name",
            initialValue: "main");

        if (string.IsNullOrWhiteSpace(firstFolderName))
        {
            return;
        }

        var path = Path.Combine(folderPath, "project.aaep");
        if (!viewModel.CreateNewBook(path, firstFolderName, out var errorMessage) && !string.IsNullOrWhiteSpace(errorMessage))
        {
            await EditorInputDialogs.EditTextAsync(
                this,
            header: "Project creation blocked",
                subHeader: errorMessage,
                initialValue: string.Empty);
        }
    }

    private async void LoadBook_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // Stop all runtime activities before loading a new project.
        HornetStudio.Host.TasksManager.StopAll();
        HornetStudio.Host.ThreadsManager.StopAll();
        HornetStudio.Host.TimerManager.StopAll();

        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open HornetStudio project",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Project entry")
                {
                    Patterns = new[] { "*.aaep", "*.udlb" }
                }
            }
        });

        if (files.Count == 0)
        {
            return;
        }

        var projectPath = TryGetStoragePath(files[0]);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        viewModel.ProjectPath = projectPath;
        if (viewModel.LoadProjectCommand.CanExecute(null))
        {
            viewModel.LoadProjectCommand.Execute(null);
        }
    }

    private void SaveLayout_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SaveCurrentLayout();
    }

    private void SetStartLayout_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetCurrentLayoutAsStartup();
    }

    private async void SaveLayoutAs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsDirectoryBook)
        {
            var bookFile = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save HornetStudio project as",
                SuggestedFileName = "project.aaep",
                DefaultExtension = "aaep",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Project entry")
                    {
                        Patterns = new[] { "*.aaep", "*.udlb" }
                    }
                }
            });

            if (bookFile is null)
            {
                return;
            }

        var targetPath = TryGetStoragePath(bookFile);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

            viewModel.SaveCurrentLayoutAs(targetPath);
            return;
        }

        var layoutFile = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save layout as",
            SuggestedFileName = "Folder.yaml",
            DefaultExtension = "yaml",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("YAML layout")
                {
                    Patterns = new[] { "*.yaml" }
                }
            }
        });

        if (layoutFile is null)
        {
            return;
        }

        var path = TryGetStoragePath(layoutFile);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        viewModel.SaveCurrentLayoutAs(path);
    }

    private static string? TryGetStoragePath(IStorageItem? item)
    {
        if (item?.TryGetLocalPath() is { } localPath && !string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var path = item?.Path;
        if (path is null)
        {
            return null;
        }

        if (path.IsAbsoluteUri)
        {
            return path.LocalPath;
        }

        return Uri.UnescapeDataString(path.OriginalString);
    }

    private void OnTabPlacementTopClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.SetTabStripPlacementCommand.CanExecute("Top"))
        {
            viewModel.SetTabStripPlacementCommand.Execute("Top");
        }
    }

    private void OnTabPlacementRightClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.SetTabStripPlacementCommand.CanExecute("Right"))
        {
            viewModel.SetTabStripPlacementCommand.Execute("Right");
        }
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        var page = menuItem.CommandParameter as FolderModel ?? menuItem.DataContext as FolderModel;
        if (page is null)
        {
            return;
        }

        var uiFilePath = page.UiFilePath;
        if (string.IsNullOrWhiteSpace(uiFilePath))
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(uiFilePath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directoryPath}\"",
            UseShellExecute = true
        });
    }

    private void OnOpenItemTreeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || sender is not MenuItem menuItem)
        {
            return;
        }

        var page = menuItem.CommandParameter as FolderModel ?? menuItem.DataContext as FolderModel;
        if (page is null)
        {
            return;
        }

        if (_itemTreeWindow is { } existingWindow)
        {
            existingWindow.Attach(viewModel, page);
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }

            existingWindow.Activate();
            existingWindow.Topmost = true;
            existingWindow.Topmost = false;
            return;
        }

        _itemTreeWindow = new ItemTreeWindow(viewModel, page);
        _itemTreeWindow.Closed += OnItemTreeWindowClosed;
        _itemTreeWindow.Show();
        _itemTreeWindow.Activate();
    }

    private void OpenItemTreeWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_itemTreeWindow is { } existingWindow)
        {
            existingWindow.AttachToProject(viewModel);
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }

            existingWindow.Activate();
            existingWindow.Topmost = true;
            existingWindow.Topmost = false;
            return;
        }

        _itemTreeWindow = new ItemTreeWindow();
        _itemTreeWindow.AttachToProject(viewModel);
        _itemTreeWindow.Closed += OnItemTreeWindowClosed;
        _itemTreeWindow.Show();
        _itemTreeWindow.Activate();
    }

    private void OpenLogWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_logWindow is { } existingWindow)
        {
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }

            existingWindow.Activate();
            existingWindow.Topmost = true;
            existingWindow.Topmost = false;
            return;
        }

        viewModel.RefreshLog();
        _logWindow = new LogWindow
        {
            DataContext = viewModel
        };
        _logWindow.Closed += OnLogWindowClosed;
        _logWindow.Show();
        _logWindow.Activate();
    }

    private void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.IsDarkTheme = !viewModel.IsDarkTheme;
    }

    private void OnLogWindowClosed(object? sender, EventArgs e)
    {
        if (sender is LogWindow window)
        {
            window.Closed -= OnLogWindowClosed;
        }

        _logWindow = null;
    }

    private void OnItemTreeWindowClosed(object? sender, EventArgs e)
    {
        if (sender is ItemTreeWindow window)
        {
            window.Closed -= OnItemTreeWindowClosed;
        }

        _itemTreeWindow = null;
    }

    private void HandleHostUiStateChanged(string action, ProjectModel? project)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            switch (action)
            {
                case "Destroy":
                    viewModel.ApplyDestroyedUi(project);
                    break;
                case "Run" when project is not null:
                    viewModel.ApplyRunningUi(project);
                    break;
            }
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        try
        {
            HornetStudio.Editor.Widgets.ApplicationExplorerRuntime.StopAllEnvironments();
            HornetStudio.Host.HostShutdownManager.ShutdownApplication("MainWindow closing");
            HornetStudio.Host.HostShutdownManager.StopAllRuntimeScopes("MainWindow closing");
        }
        catch
        {
            // Best-effort Shutdown – Fehler hier sollen das Beenden nicht verhindern.
        }

        base.OnClosing(e);
    }
}
