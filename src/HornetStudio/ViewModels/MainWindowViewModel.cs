using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using HornetStudio.Host;
using HornetStudio.Logging;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;
using VBFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace HornetStudio.ViewModels;

public sealed class MainWindowViewModel : HornetStudio.Editor.ViewModels.MainWindowViewModel
{
    private const string ProjectEntryFileName = "project.aaep";
    private const string LegacyProjectEntryFileName = "project.udlb";
    private const string OlderLegacyProjectEntryFileName = "Book.udlb";
    private const string FoldersDirectoryName = "Folders";
    private const string LegacyFoldersDirectoryName = "Pages";
    private const string FolderLayoutFileName = "Folder.yaml";
    private const string LegacyFolderLayoutFileName = "Page.yaml";
    private const string FolderMetadataFileName = "Folder.meta.yaml";
    private const string LegacyFolderMetadataFileName = "Page.meta.yaml";
    private const string StructuredLayoutWatcherFilter = "*.yaml";
    private const string ScriptsDirectoryName = "Scripts";
    private const string AssetsDirectoryName = "Assets";
    private const string FolderTemplateRelativePath = "Templates\\Folder.yaml";
    private const string FallbackFolderTemplate = "Folder: 'main'\n"
        + "Caption: 'main'\n"
        + "Screens:\n"
        + "  1: 'HomeScreen'\n"
        + "  2: 'Screen2'\n"
        + "Controls:\n"
        + "  -\n"
        + "    Type: 'ApplicationExplorer'\n"
        + "    Screen: '1'\n"
        + "    Enabled: true\n"
        + "    Identity:\n"
        + "      Name: 'ApplicationExplorer'\n"
        + "      Text: ''\n"
        + "      Path: 'ApplicationExplorer'\n"
        + "      Id: 'template-applicationexplorer'\n"
        + "    Bounds:\n"
        + "      X: 28\n"
        + "      Y: 90\n"
        + "      Width: 420\n"
        + "      Height: 160\n"
        + "    Design:\n"
        + "      CornerRadius: 12\n"
        + "      BorderWidth: 1\n"
        + "      BorderColor: null\n"
        + "      BackColor: null\n"
        + "      ToolTip: ''\n"
        + "    Header:\n"
        + "      ControlCaption: 'ApplicationExplorer'\n"
        + "      SyncText: true\n"
        + "      HeaderForeColor: null\n"
        + "      CaptionVisible: true\n"
        + "      HeaderCornerRadius: 6\n"
        + "      HeaderBorderWidth: 0\n"
        + "      HeaderBorderColor: null\n"
        + "      HeaderBackColor: null\n"
        + "    Body:\n"
        + "      BodyCaption: ''\n"
        + "      BodyCaptionPosition: 'Top'\n"
        + "      BodyForeColor: null\n"
        + "      BodyCaptionVisible: false\n"
        + "      BodyCornerRadius: 0\n"
        + "      BodyBorderWidth: 0\n"
        + "      BodyBorderColor: null\n"
        + "      BodyBackColor: null\n"
        + "    Footer:\n"
        + "      ShowFooter: true\n"
        + "      FooterCornerRadius: 6\n"
        + "      FooterBorderWidth: 0\n"
        + "      FooterBorderColor: null\n"
        + "      FooterBackColor: null\n"
        + "    Properties:\n"
        + "      Applications: ''\n"
        + "      ApplicationAutoStart: false\n";
    private readonly string _configPath;
    private readonly string _default_layoutPath;
    private static readonly TimeSpan WatcherSaveSuppressionWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WatcherUpsertDebounceWindow = TimeSpan.FromMilliseconds(1200);
    private readonly Dictionary<string, WatchedPage> _watchedPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _watcherSuppressedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _watcherDebouncedUpserts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _watcherUpsertsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _folderOrder = [];
    private FileSystemWatcher? _bookWatcher;
    private readonly HornetStudioAppConfig _config;
    private string _startupPagePath;
    private string _currentLayoutFilePath = string.Empty;
    private string _currentBookEntryPath = string.Empty;
    private string _bookProjectPath;
    private string _loadedBookSummary;
    private string _messagesSummary;
    private string _currentLogText;
    private string _headerTitle = "HornetStudio";
    private bool _hasLayout;
    private bool _isdefault_layout;
    private bool _isBookOperationRunning;
    private bool _isDeleteFolderDropTargetActive;
    private bool _isStructuredBook;

    public MainWindowViewModel()
        : base(true)
    {
        AutoSaveOnEditModeExit = false;
        _configPath = Path.Combine(AppContext.BaseDirectory, "HornetStudio.config.yaml");
        _config = HornetStudioAppConfig.Load(_configPath);

        var default_layoutDirectory = Path.Combine(AppContext.BaseDirectory, "default_layout");
        _default_layoutPath = Path.Combine(default_layoutDirectory, FolderLayoutFileName);
        Ensuredefault_layout(default_layoutDirectory, _default_layoutPath);
        // Always start in Dark theme on app startup.
        IsDarkTheme = true;
        _config.DefaultTheme = "Dark";
        _config.Save(_configPath);

        if (string.IsNullOrWhiteSpace(_config.StartLayout))
        {
            _startupPagePath = _default_layoutPath;
        }
        else
        {
            var configuredPath = _config.StartLayout;
            string resolvedPath;
            try
            {
                resolvedPath = Path.GetFullPath(configuredPath);
            }
            catch
            {
                resolvedPath = configuredPath;
            }

            if (Directory.Exists(resolvedPath) || File.Exists(resolvedPath))
            {
                _startupPagePath = resolvedPath;
            }
            else
            {
                // Fallback: konfigurierter StartLayout-Pfad existiert nicht, default_layout laden.
                _startupPagePath = _default_layoutPath;
            }
        }
        LoadProjectCommand = new HornetStudio.Editor.ViewModels.RelayCommand(LoadBook, CanRunBookAction);
        RebuildProjectCommand = new HornetStudio.Editor.ViewModels.RelayCommand(RebuildBook, CanRunBookAction);
        RefreshLogCommand = new HornetStudio.Editor.ViewModels.RelayCommand(RefreshLog);

        _bookProjectPath = ResolveInitialBookProjectPath(_startupPagePath);
        _loadedBookSummary = "No project loaded";
        _messagesSummary = "Keine Meldungen";
        _currentLogText = string.Empty;
        HostLogger.ProcessLog.EntryAdded += OnHostLogEntryAdded;
        RefreshLog();

        // Ensure the canvas grid starts disabled and keep legend icon color in sync.
        ShowGrid = false;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEditMode))
            {
                if (!IsEditMode)
                {
                    ShowGrid = false;
                }

                OnPropertyChanged(nameof(LegendEditIconColor));
            }
            else if (e.PropertyName == nameof(TabSelectForeColor))
            {
                OnPropertyChanged(nameof(LegendEditIconColor));
            }
            else if (e.PropertyName == nameof(IsDeleteFolderDropTargetActive))
            {
                OnPropertyChanged(nameof(DeleteFolderBackColor));
                OnPropertyChanged(nameof(DeleteFolderNumerBackColor));
                OnPropertyChanged(nameof(DeleteFolderForeColor));
            }
            else if (e.PropertyName == nameof(IsDarkTheme))
            {
                _config.DefaultTheme = IsDarkTheme ? "Dark" : "Light";
                _config.Save(_configPath);
                OnPropertyChanged(nameof(DeleteFolderBackColor));
                OnPropertyChanged(nameof(DeleteFolderNumerBackColor));
                OnPropertyChanged(nameof(DeleteFolderForeColor));
            }
        };

        Dispatcher.UIThread.Post(LoadStartupPage, DispatcherPriority.Background);
    }

    private sealed class WatchedPage
    {
        public string FilePath { get; set; } = string.Empty;
        public string PageName { get; set; } = string.Empty;
        public ProjectFolderLayout Layout { get; set; } = default!;
    }

    protected override string? CurrentProjectRootDirectory => ProjectPath;

    public string ProjectPath
    {
        get => _bookProjectPath;
        set
        {
            if (SetProperty(ref _bookProjectPath, value))
            {
                RefreshCurrentFolderWorkspacePath();
                LoadProjectCommand.RaiseCanExecuteChanged();
                RebuildProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LoadedProjectSummary
    {
        get => _loadedBookSummary;
        private set => SetProperty(ref _loadedBookSummary, value);
    }

    public string MessagesSummary
    {
        get => _messagesSummary;
        private set => SetProperty(ref _messagesSummary, value);
    }

    public string CurrentLogFilePath => HostLogger.CurrentLogFilePath;

    public string CurrentLogText
    {
        get => _currentLogText;
        private set => SetProperty(ref _currentLogText, value);
    }

    public string HeaderTitle
    {
        get => _headerTitle;
        private set => SetProperty(ref _headerTitle, value);
    }

    public bool HasLayout
    {
        get => _hasLayout;
        private set
        {
            if (SetProperty(ref _hasLayout, value))
            {
                OnPropertyChanged(nameof(CanSaveLayout));
            }
        }
    }

    public bool Isdefault_layout
    {
        get => _isdefault_layout;
        private set
        {
            if (SetProperty(ref _isdefault_layout, value))
            {
                OnPropertyChanged(nameof(CanSaveLayout));
            }
        }
    }

    public bool CanSaveLayout
    {
        get => HasLayout && !Isdefault_layout;
    }

    public bool IsCurrentLayoutStartLayout
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_config.StartLayout))
            {
                return false;
            }

            try
            {
                var start = Path.GetFullPath(_config.StartLayout);

                if (IsDirectoryBook)
                {
                    if (string.IsNullOrWhiteSpace(ProjectPath))
                    {
                        return false;
                    }

                    var currentDirectory = Path.GetFullPath(ProjectPath);
                    if (string.Equals(currentDirectory, start, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(_currentBookEntryPath))
                    {
                        var currentEntry = Path.GetFullPath(_currentBookEntryPath);
                        return string.Equals(currentEntry, start, StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                }

                if (string.IsNullOrWhiteSpace(_currentLayoutFilePath))
                {
                    return false;
                }

                var currentFile = Path.GetFullPath(_currentLayoutFilePath);
                return string.Equals(currentFile, start, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsDeleteFolderDropTargetActive
    {
        get => _isDeleteFolderDropTargetActive;
        set => SetProperty(ref _isDeleteFolderDropTargetActive, value);
    }

    public string DeleteFolderBackColor
        => IsDeleteFolderDropTargetActive
            ? "#B91C1C"
            : (IsDarkTheme ? "#3B1F22" : "#FEE2E2");

    public string DeleteFolderNumerBackColor
        => IsDeleteFolderDropTargetActive
            ? "#991B1B"
            : (IsDarkTheme ? "#7F1D1D" : "#FCA5A5");

    public string DeleteFolderForeColor
        => IsDarkTheme || IsDeleteFolderDropTargetActive
            ? "#FEF2F2"
            : "#7F1D1D";

    public string StartLayoutIconColor => IsCurrentLayoutStartLayout ? "#F97316" : PrimaryTextBrush;

    public bool IsBookOperationRunning
    {
        get => _isBookOperationRunning;
        private set
        {
            if (SetProperty(ref _isBookOperationRunning, value))
            {
                LoadProjectCommand.RaiseCanExecuteChanged();
                RebuildProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LegendEditIconColor => IsEditMode ? "#DC2626" : TabSelectForeColor;

    public HornetStudio.Editor.ViewModels.RelayCommand LoadProjectCommand { get; }

    public HornetStudio.Editor.ViewModels.RelayCommand RebuildProjectCommand { get; }

    public HornetStudio.Editor.ViewModels.RelayCommand RefreshLogCommand { get; }

    public Func<string, string, Task<string?>>? ResolveDuplicatePageNameAsync { get; set; }

    public bool IsDirectoryBook => _isStructuredBook || _watchedPages.Count > 0;

    public bool CreateNewBook(string bookEntryPath, string firstFolderName, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(bookEntryPath))
        {
            errorMessage = "No target .aaep file selected.";
            StatusText = errorMessage;
            return false;
        }

        if (!TryValidateFolderIdentityName(firstFolderName, out var normalizedFirstFolderName, out errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        var normalizedEntryPath = NormalizeBookEntryPath(bookEntryPath);
        var rootDirectory = Path.GetDirectoryName(Path.GetFullPath(normalizedEntryPath));
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            errorMessage = "The selected .aaep file must be inside a valid directory.";
            StatusText = errorMessage;
            return false;
        }

        try
        {
            Directory.CreateDirectory(rootDirectory);
            Directory.CreateDirectory(GetPagesDirectoryPath(rootDirectory));
            EnsureBookEntryFile(normalizedEntryPath, rootDirectory);

            var firstPageDirectory = Path.Combine(GetPagesDirectoryPath(rootDirectory), normalizedFirstFolderName);
            var firstPageLayoutPath = GetPageLayoutPath(firstPageDirectory);
            if (!File.Exists(firstPageLayoutPath))
            {
                CreatePageDirectoryStructure(firstPageDirectory);
                File.WriteAllText(firstPageLayoutPath, BuildNewPageYaml(normalizedFirstFolderName, normalizedFirstFolderName));
            }

            if (!File.Exists(normalizedEntryPath))
            {
                File.WriteAllText(normalizedEntryPath, BuildBookEntryContent(rootDirectory));
            }

            if (!File.Exists(firstPageLayoutPath))
            {
                File.WriteAllText(firstPageLayoutPath, BuildNewPageYaml(normalizedFirstFolderName, normalizedFirstFolderName));
            }

            try
            {
                LoadStructuredBookFromEntryFile(normalizedEntryPath);
            }
            catch (Exception ex)
            {
                AddMessage("CreateBook", "Warning", $"Project created, but initial load failed: {ex.Message}", normalizedEntryPath);
            }

            StatusText = $"Project created: {normalizedEntryPath}";
            return true;
        }
        catch (Exception ex)
        {
            AddMessage("CreateBook", "Error", ex.Message, normalizedEntryPath);
            errorMessage = $"Could not create project: {ex.Message}";
            StatusText = errorMessage;
            return false;
        }
    }

    public bool TryCreateNewPage(string rawPageName, out string createdFilePath, out string errorMessage)
    {
        createdFilePath = string.Empty;
        errorMessage = string.Empty;

        var trimmedName = rawPageName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            errorMessage = "Folder name must not be empty.";
            StatusText = errorMessage;
            return false;
        }

        if (!TryValidateFolderIdentityName(trimmedName, out var normalizedFolderName, out errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        if (Folders.Any(page => string.Equals(page.Name, normalizedFolderName, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = $"A folder named '{normalizedFolderName}' already exists.";
            StatusText = errorMessage;
            return false;
        }

        var targetDirectory = string.IsNullOrWhiteSpace(ProjectPath)
            ? string.Empty
            : Path.GetFullPath(ProjectPath);

        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
        {
            errorMessage = "No valid project directory loaded.";
            StatusText = errorMessage;
            return false;
        }

        if (!IsDirectoryBook)
        {
            errorMessage = "New folders are only supported for loaded project directories.";
            StatusText = errorMessage;
            return false;
        }

        var safeDirectoryName = BuildSafePageDirectoryName(normalizedFolderName);
        if (string.IsNullOrWhiteSpace(safeDirectoryName))
        {
            errorMessage = "Folder name contains no valid directory name characters.";
            StatusText = errorMessage;
            return false;
        }

        var pagesDirectory = GetPagesDirectoryPath(targetDirectory);
        Directory.CreateDirectory(pagesDirectory);

        var pageDirectory = Path.Combine(pagesDirectory, safeDirectoryName);
        var fullPath = GetPageLayoutPath(pageDirectory);
        if (Directory.Exists(pageDirectory) || File.Exists(fullPath))
        {
            errorMessage = $"A folder directory named '{safeDirectoryName}' already exists.";
            StatusText = errorMessage;
            return false;
        }

        var yamlContent = BuildNewPageYaml(normalizedFolderName, safeDirectoryName);

        try
        {
            CreatePageDirectoryStructure(pageDirectory);
            File.WriteAllText(fullPath, yamlContent);
            EnsureBookEntryFile(GetBookEntryPath(targetDirectory), targetDirectory);
            LoadStructuredBookFromDirectory(targetDirectory, GetBookEntryPath(targetDirectory));

            var createdPage = Folders.FirstOrDefault(page => string.Equals(page.UiFilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (createdPage is not null)
            {
                SelectedFolder = createdPage;
            }

            createdFilePath = fullPath;
            StatusText = $"New folder created: {Path.GetFileName(fullPath)}";
            return true;
        }
        catch (Exception ex)
        {
            AddMessage("CreatePage", "Error", ex.Message, fullPath);
            errorMessage = $"Could not create folder: {ex.Message}";
            StatusText = errorMessage;
            return false;
        }
    }

    public bool TryDeleteFolder(FolderModel? folder, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (folder is null)
        {
            errorMessage = "No folder selected for deletion.";
            StatusText = errorMessage;
            return false;
        }

        if (!IsDirectoryBook)
        {
            errorMessage = "Folder deletion is only supported for loaded project directories.";
            StatusText = errorMessage;
            return false;
        }

        if (Folders.Count <= 1)
        {
            errorMessage = "The last remaining folder cannot be deleted.";
            StatusText = errorMessage;
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProjectPath) || !Directory.Exists(ProjectPath))
        {
            errorMessage = "No valid project directory loaded.";
            StatusText = errorMessage;
            return false;
        }

        var folderDirectory = ResolveFolderDirectory(folder);
        if (string.IsNullOrWhiteSpace(folderDirectory) || !Directory.Exists(folderDirectory))
        {
            errorMessage = $"Folder directory not found: {folderDirectory}";
            StatusText = errorMessage;
            return false;
        }

        var folderIndex = Folders.IndexOf(folder);
        if (folderIndex < 0)
        {
            errorMessage = "Folder is no longer part of the current project.";
            StatusText = errorMessage;
            return false;
        }

        var remainingFolders = Folders.Where(candidate => !ReferenceEquals(candidate, folder)).ToList();
        if (remainingFolders.Count == 0)
        {
            errorMessage = "The last remaining folder cannot be deleted.";
            StatusText = errorMessage;
            return false;
        }

        var preferredSelection = remainingFolders[Math.Min(folderIndex, remainingFolders.Count - 1)];
        var previousFolderOrder = _folderOrder.ToList();
        var entryPath = string.IsNullOrWhiteSpace(_currentBookEntryPath)
            ? GetBookEntryPath(ProjectPath)
            : _currentBookEntryPath;

        try
        {
            if (ReferenceEquals(SelectedFolder, folder))
            {
                SelectedFolder = preferredSelection;
            }

            Folders.Remove(folder);
            ReindexFolders();
            IsDeleteFolderDropTargetActive = false;
            _folderOrder = GetBookManifestFolderOrder().ToList();

            if (!TrySaveBookManifest(out _))
            {
                throw new InvalidOperationException("project.aaep could not be updated before deleting the folder.");
            }

            VBFileSystem.DeleteDirectory(
                folderDirectory,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);

            LoadStructuredBookFromDirectory(ProjectPath, entryPath);
            var restoredFolder = Folders.FirstOrDefault(candidate => string.Equals(candidate.Name, preferredSelection.Name, StringComparison.OrdinalIgnoreCase));
            if (restoredFolder is not null)
            {
                SelectedFolder = restoredFolder;
            }

            StatusText = $"Folder deleted: {folder.TabTitle}";
            return true;
        }
        catch (OperationCanceledException)
        {
            RestoreFolderDeleteState(previousFolderOrder, entryPath, folder.Name);
            errorMessage = "Folder deletion was canceled.";
            StatusText = errorMessage;
            return false;
        }
        catch (Exception ex)
        {
            RestoreFolderDeleteState(previousFolderOrder, entryPath, folder.Name);
            AddMessage("DeleteFolder", "Error", ex.Message, folderDirectory);
            errorMessage = $"Folder could not be deleted: {ex.Message}";
            StatusText = errorMessage;
            return false;
        }
    }

    public void SetCurrentLayoutAsStartup()
    {
        if (IsDirectoryBook)
        {
            if (string.IsNullOrWhiteSpace(ProjectPath) || !Directory.Exists(ProjectPath))
            {
                AddMessage("StartLayout", "Warning", "No project directory is currently loaded.");
                StatusText = "No project directory is currently loaded.";
                return;
            }

            var startPath = ProjectPath;
            if (_isStructuredBook)
            {
                startPath = string.IsNullOrWhiteSpace(_currentBookEntryPath)
                    ? GetBookEntryPath(ProjectPath)
                    : _currentBookEntryPath;

                EnsureBookEntryFile(startPath, ProjectPath);
                _currentBookEntryPath = startPath;
            }

            _config.StartLayout = startPath;
            _config.Save(_configPath);
            _startupPagePath = startPath;
            StatusText = _isStructuredBook
                ? $"Start project set to: {startPath}"
                : $"Start directory set to: {ProjectPath}";
            OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
            OnPropertyChanged(nameof(StartLayoutIconColor));
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentLayoutFilePath) || !File.Exists(_currentLayoutFilePath))
        {
            AddMessage("StartLayout", "Warning", "No layout file is currently loaded.");
            StatusText = "No layout file is currently loaded.";
            return;
        }

        _config.StartLayout = _currentLayoutFilePath;
        _config.Save(_configPath);
        _startupPagePath = _currentLayoutFilePath;
        StatusText = $"Start layout set to: {_currentLayoutFilePath}";
        OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
        OnPropertyChanged(nameof(StartLayoutIconColor));
    }

    public void SaveCurrentLayout()
    {
        if (!IsDirectoryBook)
        {
            SaveLayout();
            return;
        }

        StopBookWatcher();
        try
        {
            var pagesToSave = Folders
                .Where(page => !string.IsNullOrWhiteSpace(page.UiFilePath))
                .ToList();

            if (pagesToSave.Count == 0)
            {
                SaveLayout();
                return;
            }

            var originalSelectedPage = SelectedFolder;

            foreach (var page in pagesToSave)
            {
                SelectedFolder = page;
                SaveLayout();
            }

            if (originalSelectedPage is not null)
            {
                SelectedFolder = originalSelectedPage;
            }

            StatusText = $"Saved {pagesToSave.Count} folders in directory: {ProjectPath}";
        }
        finally
        {
            RestartBookWatcher();
        }
    }

    public void SaveCurrentLayoutAs(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        if (!IsDirectoryBook)
        {
            SaveLayout();

            var sourceLayoutPath = _currentLayoutFilePath;
            if (string.IsNullOrWhiteSpace(sourceLayoutPath))
            {
                sourceLayoutPath = SelectedFolder.UiFilePath ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourceLayoutPath) || !File.Exists(sourceLayoutPath))
            {
                AddMessage("Save", "Warning", "No existing layout file to save from.");
                StatusText = "No existing layout file to save.";
                return;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourceLayoutPath, targetPath, true);

            LoadYamlLayoutFromFile(targetPath);
            StatusText = $"Layout saved as: {targetPath}";
            return;
        }

        var sourceRoot = ProjectPath;
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            AddMessage("SaveAs", "Warning", "No project directory to copy.");
            StatusText = "No project directory to copy.";
            return;
        }

        try
        {
            StopBookWatcher();

            var targetRoot = IsBookEntryPath(targetPath)
                ? Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? targetPath
                : Path.GetFullPath(targetPath);
            var targetEntryPath = IsBookEntryPath(targetPath)
                ? Path.GetFullPath(targetPath)
                : GetBookEntryPath(targetRoot);

            CopyDirectory(sourceRoot, targetRoot);
            EnsureBookEntryFile(targetEntryPath, targetRoot);

            TasksManager.StopAll();
            ThreadsManager.StopAll();
            TimerManager.StopAll();

            ProjectPath = targetRoot;
            ResetMessages();
            if (_isStructuredBook)
            {
                LoadStructuredBookFromEntryFile(targetEntryPath);
            }
            else
            {
                LoadYamlBookFromDirectory(targetRoot);
            }

            StatusText = $"Project saved as: {targetEntryPath}";
        }
        catch (Exception ex)
        {
            AddMessage("SaveAs", "Error", ex.Message, targetPath);
            StatusText = $"Save As failed: {ex.Message}";
        }
    }

    public void RefreshLog()
    {
        var entries = HostLogger.ProcessLog.GetEntries();
        if (entries.Count == 0)
        {
            CurrentLogText = "No log entries yet.";
            OnPropertyChanged(nameof(CurrentLogFilePath));
            return;
        }

        CurrentLogText = string.Join(Environment.NewLine, entries.Select(FormatLogEntry));
        OnPropertyChanged(nameof(CurrentLogFilePath));
    }

    public void ApplyDestroyedUi(ProjectModel? project)
    {
        var pages = project is not null
            ? project.Folders.Select((page, index) => new FolderModel
            {
                Index = index + 1,
                Name = string.IsNullOrWhiteSpace(page.Name) ? GetDefaultFolderIdentityName(index + 1) : page.Name
            }).ToList()
            : Folders.Select(page => new FolderModel
            {
                Index = page.Index,
                Name = page.Name
            }).ToList();

        if (pages.Count > 0)
        {
            SetFolders(pages);
        }

        StatusText = "Runtime stopped. Canvas cleared.";
    }

    public void ApplyRunningUi(ProjectModel project)
    {
        ApplyBookManifestSettings(project.RootDirectory);
        SetFolders(CreateFoldersFromProject(project));
        ProjectPath = project.RootDirectory;
        LoadedProjectSummary = $"{project.ProjectName} | Folders: {project.Folders.Count} | C#: {project.SourceFiles.Count} | UI: {project.UiFiles.Count}";
        StatusText = $"Runtime started: {project.ProjectName}";
    }

    private bool CanRunBookAction()
        => !IsBookOperationRunning && !string.IsNullOrWhiteSpace(ProjectPath);

    private void OnHostLogEntryAdded(ProcessLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var formatted = FormatLogEntry(entry);
            CurrentLogText = string.IsNullOrWhiteSpace(CurrentLogText) || string.Equals(CurrentLogText, "Noch kein Log vorhanden.", StringComparison.Ordinal)
                ? formatted
                : $"{CurrentLogText}{Environment.NewLine}{formatted}";
        });
    }

    private static string FormatLogEntry(ProcessLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Exception))
        {
            return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}";
        }

        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}{Environment.NewLine}{entry.Exception}";
    }

    private void ResetMessages()
    {
        Messages.Clear();
        MessagesSummary = "Keine Meldungen";
    }

    private static void Ensuredefault_layout(string directory, string pagePath)
    {
        try
        {
            CreatePageDirectoryStructure(directory);

            if (File.Exists(pagePath))
            {
                return;
            }

            File.WriteAllText(pagePath, BuildNewPageYaml("Default Layout", "default_layout"));
        }
        catch
        {
            // Best-effort only; if default layout creation fails, HornetStudio still starts
            // and reports missing layouts later.
        }
    }

    private static string ResolveInitialBookProjectPath(string startupPath)
    {
        if (string.IsNullOrWhiteSpace(startupPath))
        {
            return AppContext.BaseDirectory;
        }

        try
        {
            if (Directory.Exists(startupPath))
            {
                return Path.GetFullPath(startupPath);
            }

            if (File.Exists(startupPath))
            {
                return Path.GetDirectoryName(Path.GetFullPath(startupPath)) ?? AppContext.BaseDirectory;
            }
        }
        catch
        {
        }

        return Path.GetDirectoryName(startupPath) ?? AppContext.BaseDirectory;
    }

    private static string BuildSafePageDirectoryName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        return sanitized.Trim('.');
    }

    private static string GetDefaultFolderIdentityName(int index)
        => $"folder_{Math.Max(1, index)}";

    private static string BuildNewPageYaml(string caption, string folderName)
    {
        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(LoadFolderTemplateContent());
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                return BuildFallbackPageYaml(caption, folderName);
            }

            SetYamlScalar(root, "Folder", folderName);
            SetYamlScalar(root, "Caption", caption);

            if (TryGetYamlSequence(root, "Controls", out var controls))
            {
                foreach (var control in controls.Children.OfType<YamlMappingNode>())
                {
                    RefreshTemplateControlIdentity(control, folderName);
                }
            }

            using var writer = new StringWriter();
            yaml.Save(writer, assignAnchors: false);
            return writer.ToString();
        }
        catch
        {
            return BuildFallbackPageYaml(caption, folderName);
        }
    }

    private static string LoadFolderTemplateContent()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, FolderTemplateRelativePath);
        return File.Exists(templatePath)
            ? File.ReadAllText(templatePath)
            : FallbackFolderTemplate;
    }

    private static string BuildFallbackPageYaml(string caption, string folderName)
    {
        var escapedCaption = EscapeYamlSingleQuoted(caption);
        var escapedFolderName = EscapeYamlSingleQuoted(folderName);
        var widgetId = Guid.NewGuid().ToString("N");

        return $"Folder: '{escapedFolderName}'{Environment.NewLine}"
            + $"Caption: '{escapedCaption}'{Environment.NewLine}"
            + "Screens:" + Environment.NewLine
            + "  1: 'HomeScreen'" + Environment.NewLine
            + "  2: 'Screen2'" + Environment.NewLine
            + "Controls:" + Environment.NewLine
            + "  -" + Environment.NewLine
            + "    Type: 'ApplicationExplorer'" + Environment.NewLine
            + "    Screen: '1'" + Environment.NewLine
            + "    Enabled: true" + Environment.NewLine
            + "    Identity:" + Environment.NewLine
            + "      Name: 'ApplicationExplorer'" + Environment.NewLine
            + "      Text: ''" + Environment.NewLine
            + $"      Path: '{escapedFolderName}.ApplicationExplorer'" + Environment.NewLine
            + $"      Id: '{widgetId}'" + Environment.NewLine
            + "    Bounds:" + Environment.NewLine
            + "      X: 28" + Environment.NewLine
            + "      Y: 90" + Environment.NewLine
            + "      Width: 420" + Environment.NewLine
            + "      Height: 160" + Environment.NewLine
            + "    Design:" + Environment.NewLine
            + "      CornerRadius: 12" + Environment.NewLine
            + "      BorderWidth: 1" + Environment.NewLine
            + "      BorderColor: null" + Environment.NewLine
            + "      BackColor: null" + Environment.NewLine
            + "      ToolTip: ''" + Environment.NewLine
            + "    Header:" + Environment.NewLine
            + "      ControlCaption: 'ApplicationExplorer'" + Environment.NewLine
            + "      SyncText: true" + Environment.NewLine
            + "      HeaderForeColor: null" + Environment.NewLine
            + "      CaptionVisible: true" + Environment.NewLine
            + "      HeaderCornerRadius: 6" + Environment.NewLine
            + "      HeaderBorderWidth: 0" + Environment.NewLine
            + "      HeaderBorderColor: null" + Environment.NewLine
            + "      HeaderBackColor: null" + Environment.NewLine
            + "    Body:" + Environment.NewLine
            + "      BodyCaption: ''" + Environment.NewLine
            + "      BodyCaptionPosition: 'Top'" + Environment.NewLine
            + "      BodyForeColor: null" + Environment.NewLine
            + "      BodyCaptionVisible: false" + Environment.NewLine
            + "      BodyCornerRadius: 0" + Environment.NewLine
            + "      BodyBorderWidth: 0" + Environment.NewLine
            + "      BodyBorderColor: null" + Environment.NewLine
            + "      BodyBackColor: null" + Environment.NewLine
            + "    Footer:" + Environment.NewLine
            + "      ShowFooter: true" + Environment.NewLine
            + "      FooterCornerRadius: 6" + Environment.NewLine
            + "      FooterBorderWidth: 0" + Environment.NewLine
            + "      FooterBorderColor: null" + Environment.NewLine
            + "      FooterBackColor: null" + Environment.NewLine
            + "    Properties:" + Environment.NewLine
            + "      Applications: ''" + Environment.NewLine
            + "      ApplicationAutoStart: false" + Environment.NewLine;
    }

    private static void RefreshTemplateControlIdentity(YamlMappingNode control, string folderName)
    {
        if (TryGetYamlMapping(control, "Identity", out var identity))
        {
            var itemName = GetYamlScalar(identity, "Name");
            SetYamlScalar(identity, "Id", Guid.NewGuid().ToString("N"));

            if (!string.IsNullOrWhiteSpace(itemName))
            {
                SetYamlScalar(identity, "Path", itemName);
            }
        }

        if (TryGetYamlSequence(control, "Children", out var children))
        {
            foreach (var child in children.Children.OfType<YamlMappingNode>())
            {
                RefreshTemplateControlIdentity(child, folderName);
            }
        }

        if (!TryGetYamlSequence(control, "Cells", out var cells))
        {
            return;
        }

        foreach (var cell in cells.Children.OfType<YamlMappingNode>())
        {
            if (TryGetYamlMapping(cell, "Child", out var child))
            {
                RefreshTemplateControlIdentity(child, folderName);
            }
        }
    }

    private static bool TryGetYamlMapping(YamlMappingNode node, string key, out YamlMappingNode mapping)
    {
        if (node.Children.TryGetValue(new YamlScalarNode(key), out var child) && child is YamlMappingNode result)
        {
            mapping = result;
            return true;
        }

        mapping = null!;
        return false;
    }

    private static bool TryGetYamlSequence(YamlMappingNode node, string key, out YamlSequenceNode sequence)
    {
        if (node.Children.TryGetValue(new YamlScalarNode(key), out var child) && child is YamlSequenceNode result)
        {
            sequence = result;
            return true;
        }

        sequence = null!;
        return false;
    }

    private static string? GetYamlScalar(YamlMappingNode node, string key)
    {
        if (node.Children.TryGetValue(new YamlScalarNode(key), out var child) && child is YamlScalarNode scalar)
        {
            return scalar.Value;
        }

        return null;
    }

    private static void SetYamlScalar(YamlMappingNode node, string key, string value)
        => node.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);

    private static string EscapeYamlSingleQuoted(string value)
        => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

    private static bool IsBookEntryPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".aaep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".udlb", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBookEntryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            return GetBookEntryPath(fullPath);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return fullPath;
        }

        return GetBookEntryPath(directory);
    }

    private static string GetBookEntryPath(string bookRootPath)
    {
        var projectEntryPath = Path.Combine(bookRootPath, ProjectEntryFileName);
        var legacyEntryPath = Path.Combine(bookRootPath, LegacyProjectEntryFileName);
        var olderLegacyEntryPath = Path.Combine(bookRootPath, OlderLegacyProjectEntryFileName);
        return File.Exists(projectEntryPath)
            ? projectEntryPath
            : (File.Exists(legacyEntryPath)
                ? legacyEntryPath
                : (File.Exists(olderLegacyEntryPath) ? olderLegacyEntryPath : projectEntryPath));
    }

    private static string GetPagesDirectoryPath(string bookRootPath)
    {
        var foldersPath = Path.Combine(bookRootPath, FoldersDirectoryName);
        var legacyFoldersPath = Path.Combine(bookRootPath, LegacyFoldersDirectoryName);
        return Directory.Exists(foldersPath) ? foldersPath : (Directory.Exists(legacyFoldersPath) ? legacyFoldersPath : foldersPath);
    }

    private static string GetPageLayoutPath(string pageDirectoryPath)
    {
        var folderLayoutPath = Path.Combine(pageDirectoryPath, FolderLayoutFileName);
        var legacyLayoutPath = Path.Combine(pageDirectoryPath, LegacyFolderLayoutFileName);
        return File.Exists(folderLayoutPath) ? folderLayoutPath : (File.Exists(legacyLayoutPath) ? legacyLayoutPath : folderLayoutPath);
    }

    private static string GetPageMetadataPath(string pageDirectoryPath)
    {
        var folderMetadataPath = Path.Combine(pageDirectoryPath, FolderMetadataFileName);
        var legacyMetadataPath = Path.Combine(pageDirectoryPath, LegacyFolderMetadataFileName);
        return File.Exists(folderMetadataPath) ? folderMetadataPath : (File.Exists(legacyMetadataPath) ? legacyMetadataPath : folderMetadataPath);
    }

    private static string GetPageNameFallback(string layoutPath)
    {
        var fileName = Path.GetFileName(layoutPath);
        if (string.Equals(fileName, FolderLayoutFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, LegacyFolderLayoutFileName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(Path.GetDirectoryName(layoutPath)) ?? "Folder";
        }

        return Path.GetFileNameWithoutExtension(layoutPath);
    }

    private static string GetTechnicalPageName(string layoutPath)
        => GetPageNameFallback(layoutPath);

    private static string BuildBookEntryContent(string bookRootPath)
    {
        var bookName = Path.GetFileName(bookRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return $"# HornetStudio project entry{Environment.NewLine}"
            + $"FormatVersion: 1{Environment.NewLine}"
            + $"Project: '{EscapeYamlSingleQuoted(bookName)}'{Environment.NewLine}";
    }

    private static void EnsureBookEntryFile(string entryPath, string bookRootPath)
    {
        var entryDirectory = Path.GetDirectoryName(entryPath);
        if (!string.IsNullOrWhiteSpace(entryDirectory))
        {
            Directory.CreateDirectory(entryDirectory);
        }

        if (!File.Exists(entryPath))
        {
            File.WriteAllText(entryPath, BuildBookEntryContent(bookRootPath));
        }
    }

    private static void CreatePageDirectoryStructure(string pageDirectory)
    {
        Directory.CreateDirectory(pageDirectory);
        Directory.CreateDirectory(Path.Combine(pageDirectory, ScriptsDirectoryName));
        Directory.CreateDirectory(Path.Combine(pageDirectory, AssetsDirectoryName));
    }

    private void ValidatePageMetadataScripts(string pageDirectory)
    {
        var metadataPath = GetPageMetadataPath(pageDirectory);
        if (!File.Exists(metadataPath))
        {
            return;
        }

        try
        {
            foreach (var relativeScriptPath in ReadMetadataScriptReferences(metadataPath))
            {
                var fullScriptPath = Path.GetFullPath(Path.Combine(pageDirectory, relativeScriptPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(fullScriptPath))
                {
                    AddMessage("Load", "Warning", $"Referenced script not found: {relativeScriptPath}", metadataPath);
                }
            }
        }
        catch (Exception ex)
        {
            AddMessage("Load", "Warning", $"Page metadata could not be parsed: {ex.Message}", metadataPath);
        }
    }

    private static bool TryValidateFolderIdentityName(string? rawFolderName, out string normalizedFolderName, out string errorMessage)
    {
        normalizedFolderName = TargetPathHelper.NormalizeIdentityName(rawFolderName);
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            errorMessage = "Folder name must not be empty.";
            return false;
        }

        if (!TargetPathHelper.IsValidPathIdentityName(normalizedFolderName))
        {
            errorMessage = "Folder name must use snake_case: lowercase letters, numbers and '_' only.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> ReadMetadataScriptReferences(string metadataPath)
    {
        var scriptPaths = new List<string>();
        var inScriptsSection = false;

        foreach (var rawLine in File.ReadLines(metadataPath))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var trimmedLine = rawLine.Trim();
            if (trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (!char.IsWhiteSpace(rawLine[0]))
            {
                inScriptsSection = string.Equals(trimmedLine, "Scripts:", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inScriptsSection || !trimmedLine.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmedLine[1..].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(value))
            {
                scriptPaths.Add(value);
            }
        }

        return scriptPaths;
    }

                private static void CopyDirectory(string sourceDirectory, string targetDirectory)
                {
                    var source = Path.GetFullPath(sourceDirectory);
                    var target = Path.GetFullPath(targetDirectory);

                    if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    Directory.CreateDirectory(target);

                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(source, file);
                        var destinationPath = Path.Combine(target, relativePath);
                        var destinationDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrWhiteSpace(destinationDir))
                        {
                            Directory.CreateDirectory(destinationDir);
                        }

                        File.Copy(file, destinationPath, true);
                    }
                }

    private void LoadStartupPage()
    {
        ResetMessages();

        if (Directory.Exists(_startupPagePath))
        {
            var entryPath = GetBookEntryPath(_startupPagePath);
            if (File.Exists(entryPath))
            {
                LoadStructuredBookFromEntryFile(entryPath);
            }
            else
            {
                LoadYamlBookFromDirectory(_startupPagePath);
            }
            return;
        }

        if (!File.Exists(_startupPagePath))
        {
            LoadedProjectSummary = "Default layout missing";
            AddMessage("UI", "Warning", "Startup-Folder.yaml nicht gefunden.", _startupPagePath);
            StatusText = $"No startup folder found: {_startupPagePath}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
            return;
        }

        if (IsBookEntryPath(_startupPagePath))
        {
            LoadStructuredBookFromEntryFile(_startupPagePath);
            return;
        }

        try
        {
            var layout = ProjectUiLayoutLoader.LoadYaml(_startupPagePath, "UdlClient");
            var pageName = GetTechnicalPageName(_startupPagePath);
            var model = CreateFolderModelFromLayout(_startupPagePath, layout, 1, pageName);

            ApplyBookManifestSettings(Path.GetDirectoryName(_startupPagePath) ?? AppContext.BaseDirectory);
            SetFolders([model]);
            ProjectPath = Path.GetDirectoryName(_startupPagePath) ?? AppContext.BaseDirectory;
            _currentLayoutFilePath = _startupPagePath;
            _currentBookEntryPath = string.Empty;
            _isStructuredBook = false;
            HasLayout = true;
            Isdefault_layout = string.Equals(_startupPagePath, _default_layoutPath, StringComparison.OrdinalIgnoreCase);
            HeaderTitle = Path.GetFileNameWithoutExtension(_startupPagePath) ?? "HornetStudio";
            LoadedProjectSummary = $"Default layout | {Path.GetFileName(_startupPagePath)}";
            StatusText = $"Default layout loaded: {_startupPagePath}";
            OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
            OnPropertyChanged(nameof(StartLayoutIconColor));
        }
        catch (Exception ex)
        {
            LoadedProjectSummary = "Default layout invalid";
            AddMessage("UI", "Error", ex.Message, _startupPagePath);
            StatusText = $"Default layout could not be loaded: {ex.Message}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
        }
    }

    public void LoadLayoutFromFile(string uiFilePath)
    {
        LoadYamlLayoutFromFile(NormalizeYamlPath(uiFilePath));
    }

    public void LoadYamlLayoutFromFile(string yamlFilePath)
    {
        ResetMessages();

        yamlFilePath = NormalizeYamlPath(yamlFilePath);

        if (!File.Exists(yamlFilePath))
        {
            LoadedProjectSummary = "YAML file missing";
            AddMessage("UI", "Warning", "YAML layout file not found.", yamlFilePath);
            StatusText = $"No YAML layout file found: {yamlFilePath}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
            return;
        }

        try
        {
            var fallbackName = Path.GetFileNameWithoutExtension(yamlFilePath);
            var layout = ProjectUiLayoutLoader.LoadYaml(yamlFilePath, fallbackName);
            var pageName = GetTechnicalPageName(yamlFilePath);
            var model = CreateFolderModelFromLayout(yamlFilePath, layout, 1, pageName);

            var rootDirectory = Path.GetDirectoryName(yamlFilePath) ?? AppContext.BaseDirectory;
            ApplyBookManifestSettings(rootDirectory);
            SetFolders([model]);
            ProjectPath = rootDirectory;
            _currentLayoutFilePath = yamlFilePath;
            _currentBookEntryPath = string.Empty;
            _isStructuredBook = false;
            HasLayout = true;
            Isdefault_layout = false;
            HeaderTitle = Path.GetFileNameWithoutExtension(yamlFilePath) ?? "HornetStudio";
            LoadedProjectSummary = $"YAML layout | {Path.GetFileName(yamlFilePath)}";
            StatusText = $"YAML layout loaded: {yamlFilePath}";
            OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
            OnPropertyChanged(nameof(StartLayoutIconColor));
        }
        catch (Exception ex)
        {
            LoadedProjectSummary = "YAML invalid";
            AddMessage("UI", "Error", ex.Message, yamlFilePath);
            StatusText = $"YAML layout could not be loaded: {ex.Message}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
        }
    }

    public void LoadYamlBookFromDirectory(string directoryPath)
    {
        var fullDirectory = Path.GetFullPath(directoryPath);
        var entryPath = GetBookEntryPath(fullDirectory);
        if (File.Exists(entryPath) || Directory.Exists(GetPagesDirectoryPath(fullDirectory)))
        {
            LoadStructuredBookFromDirectory(fullDirectory, File.Exists(entryPath) ? entryPath : string.Empty);
            return;
        }

        LoadLegacyYamlBookFromDirectory(fullDirectory);
    }

    private void LoadLegacyYamlBookFromDirectory(string directoryPath)
    {
        try
        {
            var fullDirectory = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(fullDirectory))
            {
                LoadedProjectSummary = "YAML directory not found";
                AddMessage("Load", "Warning", "YAML project directory not found.", fullDirectory);
                StatusText = $"No YAML project directory found: {fullDirectory}";
                HasLayout = false;
                Isdefault_layout = false;
                HeaderTitle = "HornetStudio";
                return;
            }

            StopBookWatcher();
            _watchedPages.Clear();
            _currentBookEntryPath = string.Empty;
            _isStructuredBook = false;

            var yamlFiles = Directory.GetFiles(fullDirectory, "*.yaml", SearchOption.TopDirectoryOnly);
            foreach (var filePath in yamlFiles)
            {
                try
                {
                    var fallbackName = GetPageNameFallback(filePath);
                    var layout = ProjectUiLayoutLoader.LoadYaml(filePath, fallbackName);
                    var pageName = GetTechnicalPageName(filePath);
                    var watchedPage = new WatchedPage
                    {
                        FilePath = filePath,
                        PageName = pageName,
                        Layout = layout
                    };

                    _watchedPages[filePath] = watchedPage;
                }
                catch (Exception ex)
                {
                    AddMessage("UI", "Error", ex.Message, filePath);
                }
            }

            UpdatePagesFromWatchedPages(fullDirectory);
            StartBookWatcher(fullDirectory, "*.yaml", false);
            ProjectPath = fullDirectory;
            LoadedProjectSummary = $"YAML directory project | {fullDirectory}";
            StatusText = $"YAML project directory loaded: {fullDirectory}";
        }
        catch (Exception ex)
        {
            LoadedProjectSummary = "YAML project could not be loaded";
            AddMessage("Load", "Error", ex.Message, directoryPath);
            StatusText = $"YAML load failed: {ex.Message}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
        }
    }

    private void LoadStructuredBookFromEntryFile(string entryPath)
    {
        var fullEntryPath = Path.GetFullPath(entryPath);
        if (!File.Exists(fullEntryPath))
        {
            LoadedProjectSummary = "Project entry not found";
            AddMessage("Load", "Warning", "Project entry file not found.", fullEntryPath);
            StatusText = $"No .aaep file found: {fullEntryPath}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
            return;
        }

        LoadStructuredBookFromDirectory(Path.GetDirectoryName(fullEntryPath) ?? AppContext.BaseDirectory, fullEntryPath);
    }

    private void LoadStructuredBookFromDirectory(string bookRootPath, string? entryPath)
    {
        try
        {
            var fullDirectory = Path.GetFullPath(bookRootPath);
            if (!Directory.Exists(fullDirectory))
            {
                LoadedProjectSummary = "Project directory not found";
                AddMessage("Load", "Warning", "HornetStudio project root directory not found.", fullDirectory);
                StatusText = $"No HornetStudio project directory found: {fullDirectory}";
                HasLayout = false;
                Isdefault_layout = false;
                HeaderTitle = "HornetStudio";
                return;
            }

            var fullEntryPath = string.IsNullOrWhiteSpace(entryPath)
                ? GetBookEntryPath(fullDirectory)
                : Path.GetFullPath(entryPath);
            var pagesDirectory = GetPagesDirectoryPath(fullDirectory);

            StopBookWatcher();
            _watchedPages.Clear();
            _isStructuredBook = true;
            _currentBookEntryPath = File.Exists(fullEntryPath) ? fullEntryPath : string.Empty;
            ProjectPath = fullDirectory;

            if (!File.Exists(fullEntryPath))
            {
                AddMessage("Load", "Warning", "project.aaep is missing.", fullDirectory);
            }

            if (!Directory.Exists(pagesDirectory))
            {
                HasLayout = false;
                Isdefault_layout = false;
                HeaderTitle = Path.GetFileName(fullDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "HornetStudio";
                SetFolders(Array.Empty<FolderModel>());
                LoadedProjectSummary = "Folders directory missing";
                StatusText = $"Folders directory missing: {pagesDirectory}";
                AddMessage("Load", "Warning", "Folders directory not found.", pagesDirectory);
                OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
                OnPropertyChanged(nameof(StartLayoutIconColor));
                return;
            }

            var pageDirectories = Directory.GetDirectories(pagesDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var usedPageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pageDirectory in pageDirectories)
            {
                var filePath = GetPageLayoutPath(pageDirectory);
                if (!File.Exists(filePath))
                {
                    AddMessage("Load", "Warning", "Folder.yaml missing in folder directory.", pageDirectory);
                    continue;
                }

                try
                {
                    ValidatePageMetadataScripts(pageDirectory);
                    var fallbackName = GetPageNameFallback(filePath);
                    var layout = ProjectUiLayoutLoader.LoadYaml(filePath, fallbackName);
                    var pageName = GetTechnicalPageName(filePath);
                    if (!usedPageNames.Add(pageName))
                    {
                        AddMessage("Load", "Warning", $"Duplicate folder name detected: {pageName}", filePath);
                        continue;
                    }

                    _watchedPages[filePath] = new WatchedPage
                    {
                        FilePath = filePath,
                        PageName = pageName,
                        Layout = layout
                    };
                }
                catch (Exception ex)
                {
                    AddMessage("UI", "Error", ex.Message, filePath);
                }
            }

            UpdatePagesFromWatchedPages(fullDirectory);
            StartBookWatcher(pagesDirectory, StructuredLayoutWatcherFilter, true);
            LoadedProjectSummary = $"HornetStudio | Folders: {_watchedPages.Count}";
            StatusText = $"HornetStudio loaded: {fullDirectory}";
        }
        catch (Exception ex)
        {
            LoadedProjectSummary = "HornetStudio could not be loaded";
            AddMessage("Load", "Error", ex.Message, bookRootPath);
            StatusText = $"HornetStudio load failed: {ex.Message}";
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = "HornetStudio";
        }
    }

    public void LoadYamlVersionFromCurrentLayout()
    {
        var basePath = !string.IsNullOrWhiteSpace(_currentLayoutFilePath)
            ? _currentLayoutFilePath
            : _startupPagePath;

        if (string.IsNullOrWhiteSpace(basePath))
        {
            StatusText = "No current layout available for YAML start";
            return;
        }

        var yamlPath = string.Equals(Path.GetExtension(basePath), ".yaml", StringComparison.OrdinalIgnoreCase)
            ? basePath
            : Path.ChangeExtension(basePath, ".yaml");

        LoadYamlLayoutFromFile(yamlPath);
    }

    private void AddMessage(string source, string severity, string message, string? location = null)
    {
        Messages.Add(new HostMessageEntry
        {
            Source = source,
            Severity = severity,
            Message = message,
            Location = location ?? string.Empty
        });

        MessagesSummary = $"{Messages.Count} Meldungen";
    }

    private void LoadBookFromDirectory(string directoryPath)
    {
        LoadYamlBookFromDirectory(directoryPath);
    }

    private async void LoadBook()
    {
        if (!CanRunBookAction())
        {
            return;
        }

        IsBookOperationRunning = true;
        ResetMessages();
        try
        {
            if (Directory.Exists(ProjectPath))
            {
                LoadYamlBookFromDirectory(ProjectPath);
                ProjectPath = Path.GetFullPath(ProjectPath);
                LoadedProjectSummary = _isStructuredBook
                    ? $"HornetStudio | {ProjectPath}"
                    : $"Directory project | {ProjectPath}";
                StatusText = _isStructuredBook
                    ? $"HornetStudio loaded: {ProjectPath}"
                    : $"Project directory loaded: {ProjectPath}";
                return;
            }

            if (!File.Exists(ProjectPath))
            {
                LoadedProjectSummary = "Project definition not found";
                AddMessage("Load", "Warning", "Project definition file not found.", ProjectPath);
                StatusText = $"No project definition found: {ProjectPath}";
                return;
            }

            if (IsBookEntryPath(ProjectPath))
            {
                var entryPath = Path.GetFullPath(ProjectPath);
                LoadStructuredBookFromEntryFile(entryPath);
                ProjectPath = Path.GetDirectoryName(entryPath) ?? AppContext.BaseDirectory;
                LoadedProjectSummary = $"HornetStudio | {Path.GetFileName(entryPath)}";
                StatusText = $"Project loaded: {entryPath}";
                return;
            }

            var yamlPath = NormalizeYamlPath(ProjectPath);
            LoadYamlLayoutFromFile(yamlPath);

            var definitionDirectory = Path.GetDirectoryName(yamlPath) ?? AppContext.BaseDirectory;
            ProjectPath = definitionDirectory;
            LoadedProjectSummary = $"Project | {Path.GetFileName(yamlPath)}";
            StatusText = $"Project loaded: {yamlPath}";
        }
        catch (Exception ex)
        {
            LoadedProjectSummary = "Project could not be loaded";
            AddMessage("Load", "Error", ex.Message);
            StatusText = $"Load failed: {ex.Message}";
        }
        finally
        {
            RefreshLog();
            IsBookOperationRunning = false;
        }
    }

    private async void RebuildBook()
    {
        if (!CanRunBookAction())
        {
            return;
        }

        IsBookOperationRunning = true;
        ResetMessages();
        try
        {
            if (IsDirectoryBook)
            {
                if (string.IsNullOrWhiteSpace(ProjectPath) || !Directory.Exists(ProjectPath))
                {
                    StatusText = "No project directory to reload.";
                    return;
                }

                if (_isStructuredBook)
                {
                    var entryPath = string.IsNullOrWhiteSpace(_currentBookEntryPath)
                        ? GetBookEntryPath(ProjectPath)
                        : _currentBookEntryPath;
                    LoadStructuredBookFromDirectory(ProjectPath, entryPath);
                    StatusText = $"HornetStudio reloaded: {ProjectPath}";
                }
                else
                {
                    LoadYamlBookFromDirectory(ProjectPath);
                    StatusText = $"Project reloaded: {ProjectPath}";
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(_currentLayoutFilePath))
            {
                StatusText = "No layout to reload.";
                return;
            }

            LoadYamlLayoutFromFile(_currentLayoutFilePath);
            StatusText = $"Layout reloaded: {_currentLayoutFilePath}";
        }
        catch (Exception ex)
        {
            AddMessage("Build", "Error", ex.Message);
            StatusText = $"Rebuild failed: {ex.Message}";
        }
        finally
        {
            RefreshLog();
            IsBookOperationRunning = false;
        }
    }

    private IReadOnlyList<FolderModel> CreateFoldersFromProject(ProjectModel project)
    {
        var pages = project.Folders
            .Select((page, index) => CreateFolderFromProject(page, index + 1))
            .ToList();

        if (pages.Count == 0)
        {
            pages.Add(new FolderModel
            {
                Index = 1,
                Name = TargetPathHelper.NormalizePathSegment(project.ProjectName, GetDefaultFolderIdentityName(1))
            });
        }

        return ApplyFolderOrder(pages);
    }

    protected override IReadOnlyList<string> GetBookManifestFolderOrder()
        => Folders
            .Select(GetFolderOrderKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToArray();

    protected override void ApplyBookManifestFolderOrder(IReadOnlyList<string> folderOrder)
    {
        _folderOrder = folderOrder
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryMoveFolder(FolderModel? sourceFolder, FolderModel? targetFolder, bool insertAfter)
    {
        if (!IsEditMode || sourceFolder is null || targetFolder is null || ReferenceEquals(sourceFolder, targetFolder))
        {
            return false;
        }

        var sourceIndex = Folders.IndexOf(sourceFolder);
        var targetIndex = Folders.IndexOf(targetFolder);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return false;
        }

        var destinationIndex = targetIndex + (insertAfter ? 1 : 0);
        if (sourceIndex < destinationIndex)
        {
            destinationIndex--;
        }

        if (destinationIndex < 0)
        {
            destinationIndex = 0;
        }
        else if (destinationIndex >= Folders.Count)
        {
            destinationIndex = Folders.Count - 1;
        }

        if (sourceIndex == destinationIndex)
        {
            return false;
        }

        Folders.Move(sourceIndex, destinationIndex);
        ReindexFolders();
        _folderOrder = GetBookManifestFolderOrder().ToList();

        if (TrySaveBookManifest(out _))
        {
            StatusText = $"Folder order updated: {sourceFolder.TabTitle}";
        }
        else
        {
            StatusText = $"Folder moved: {sourceFolder.TabTitle}";
        }

        return true;
    }

    private FolderModel CreateFolderFromProject(ProjectFolderDefinition page, int index)
    {
        var pageName = string.IsNullOrWhiteSpace(page.Name) ? GetDefaultFolderIdentityName(index) : page.Name;
        var pageDisplayText = GetFolderDisplayText(page) ?? pageName;
        var yamlPath = NormalizeYamlPath(page.UiFile);

        if (string.IsNullOrWhiteSpace(yamlPath) || !File.Exists(yamlPath))
        {
            var fallbackModel = new FolderModel
            {
                Index = index,
                Name = pageName,
                DisplayText = pageDisplayText,
                UiFilePath = yamlPath
            };

            fallbackModel.Items.Add(CreateFallbackItem(page.Name, "No Folder.yaml found"));
            return fallbackModel;
        }

        try
        {
            var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, page.Name);
            var model = new FolderModel
            {
                Index = index,
                Name = pageName,
                DisplayText = pageDisplayText,
                UiFilePath = yamlPath,
                UiLayoutDefinition = layout
            };

            var items = CreateItemsFromNode(pageName, layout.Layout, 48, 48).ToList();

            foreach (var item in items)
            {
                model.Items.Add(item);
            }

            return model;
        }
        catch (Exception ex)
        {
            AddMessage("UI", "Error", ex.Message, yamlPath);
            var fallbackModel = new FolderModel
            {
                Index = index,
                Name = pageName,
                DisplayText = pageDisplayText,
                UiFilePath = yamlPath
            };
            fallbackModel.Items.Add(CreateFallbackItem(page.Name, $"UI error: {ex.Message}"));
            return fallbackModel;
        }
    }

    private string GetFolderOrderKey(FolderModel folder)
        => folder.Name;

    private void RestoreFolderDeleteState(IReadOnlyList<string> previousFolderOrder, string entryPath, string preferredFolderName)
    {
        LoadStructuredBookFromDirectory(ProjectPath, entryPath);

        _folderOrder = previousFolderOrder
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TrySaveBookManifest(out _);

        var restoredFolder = Folders.FirstOrDefault(folder => string.Equals(folder.Name, preferredFolderName, StringComparison.OrdinalIgnoreCase));
        if (restoredFolder is not null)
        {
            SelectedFolder = restoredFolder;
        }
    }

    private string ResolveFolderDirectory(FolderModel folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.UiFilePath))
        {
            var uiDirectory = Path.GetDirectoryName(folder.UiFilePath);
            if (!string.IsNullOrWhiteSpace(uiDirectory))
            {
                return Path.GetFullPath(uiDirectory);
            }
        }

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return string.Empty;
        }

        return Path.Combine(GetPagesDirectoryPath(ProjectPath), folder.Name);
    }

    private IReadOnlyList<FolderModel> ApplyFolderOrder(IEnumerable<FolderModel> folders)
    {
        var orderedFolders = folders.ToList();
        if (orderedFolders.Count <= 1 || _folderOrder.Count == 0)
        {
            ReindexFolders(orderedFolders);
            return orderedFolders;
        }

        var resolvedFolders = new List<FolderModel>(orderedFolders.Count);
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _folderOrder)
        {
            var folder = orderedFolders.FirstOrDefault(candidate =>
                !usedKeys.Contains(GetFolderOrderKey(candidate))
                && string.Equals(GetFolderOrderKey(candidate), key, StringComparison.OrdinalIgnoreCase));

            if (folder is null)
            {
                continue;
            }

            resolvedFolders.Add(folder);
            usedKeys.Add(GetFolderOrderKey(folder));
        }

        foreach (var folder in orderedFolders)
        {
            if (usedKeys.Add(GetFolderOrderKey(folder)))
            {
                resolvedFolders.Add(folder);
            }
        }

        ReindexFolders(resolvedFolders);
        return resolvedFolders;
    }

    private void ReindexFolders()
        => ReindexFolders(Folders);

    private static void ReindexFolders(IEnumerable<FolderModel> folders)
    {
        var index = 1;
        foreach (var folder in folders)
        {
            folder.Index = index++;
        }
    }

    private IEnumerable<FolderItemModel> CreateItemsFromNode(string pageName, ProjectUiNode node, double defaultX, double defaultY)
    {
        if (IsContainerNode(node))
        {
            var x = node.X ?? defaultX;
            var y = node.Y ?? defaultY;
            var spacing = node.Spacing ?? 12d;
            var nextY = y;

            foreach (var child in node.Children)
            {
                foreach (var item in CreateItemsFromNode(pageName, child, x, nextY))
                {
                    yield return item;
                    nextY = Math.Max(nextY, item.Y + item.Height + spacing);
                }
            }

            yield break;
        }

        yield return CreateItemFromUiNode(pageName, node, defaultX, defaultY);
    }

    private FolderItemModel CreateItemFromUiNode(string pageName, ProjectUiNode node, double defaultX, double defaultY)
    {
        var type = node.Type;
        var text = string.IsNullOrWhiteSpace(node.Text) ? type : node.Text;
        var kind = GetControlKindFromUiType(type);
        var isButton = kind == ControlKind.Button;
        var isWidgetList = kind == ControlKind.WidgetList;
        var isChartControl = kind == ControlKind.ChartControl;
        var isCircleDisplay = kind == ControlKind.CircleDisplay;
        var isBrokerWidget = kind == ControlKind.BrokerWidget;
        var item = new FolderItemModel
        {
            Kind = kind,
            Name = text,
            BodyCaption = text,
            ControlCaption = pageName,
            Footer = isButton ? "Action" : type,
            X = node.X ?? defaultX,
            Y = node.Y ?? defaultY,
            Width = node.Width ?? (isButton ? 320 : (kind == ControlKind.LogControl ? 420 : (isChartControl ? 520 : (isBrokerWidget ? 420 : (isCircleDisplay ? 280 : 260))))),
            Height = node.Height ?? (isButton ? 96 : (kind == ControlKind.LogControl ? 260 : (isChartControl ? 260 : (isWidgetList ? 220 : (isBrokerWidget ? 190 : (isCircleDisplay ? 280 : 84)))))),
            IsAutoHeight = isWidgetList,
            UiNodeType = string.IsNullOrWhiteSpace(type) ? GetDefaultUiType(kind) : type,
            UiProperties = CloneJsonObject(node.Properties)
        };

        ApplyKnownUiProperties(item, node.Properties, pageName, type);
        item.SetHierarchy(pageName, null);

        foreach (var childNode in node.Children)
        {
            var childItems = CreateItemsFromNode(pageName, childNode, 0, 0).ToList();
            foreach (var childItem in childItems)
            {
                if (item.IsWidgetList)
                {
                    item.AttachChildToList(childItem);
                }
                else
                {
                    childItem.SetHierarchy(pageName, item);
                    item.Items.Add(childItem);
                }
            }
        }

        item.ApplyTheme(IsDarkTheme);
        item.SyncChildWidths();
        item.ApplyListHeightRules();
        return item;
    }

    private static bool IsContainerNode(ProjectUiNode node)
    {
        return string.Equals(node.Type, "StackPanel", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Type, "Canvas", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(node.Type);
    }

    private static ControlKind GetControlKindFromUiType(string? type)
    {
        if (string.Equals(type, "Button", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.Button;
        }

        if (string.Equals(type, "WidgetList", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "ListControl", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.WidgetList;
        }

        if (string.Equals(type, "TableControl", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.TableControl;
        }

        if (string.Equals(type, "CircleDisplay", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.CircleDisplay;
        }

        if (string.Equals(type, "LogControl", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "ProcessLog", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.LogControl;
        }

        if (string.Equals(type, "ChartControl", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Chart", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.ChartControl;
        }

        if (string.Equals(type, "UdlClientControl", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "UdlClient", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.UdlClientControl;
        }

        if (string.Equals(type, "BrokerWidget", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "BrokerClientControl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "ItemBroker", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.BrokerWidget;
        }

        if (string.Equals(type, "CsvLoggerControl", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "CsvLogger", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.CsvLoggerControl;
        }

        if (string.Equals(type, "SqlLoggerControl", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "SqlLogger", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.SqlLoggerControl;
        }

        if (string.Equals(type, "CameraControl", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Camera", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.CameraControl;
        }

        if (string.Equals(type, "PythonEnvManager", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "ApplicationExplorer", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.ApplicationExplorer;
        }

        if (string.Equals(type, "CustomSignals", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.CustomSignals;
        }

        if (string.Equals(type, "EnhancedSignals", StringComparison.OrdinalIgnoreCase))
        {
            return ControlKind.EnhancedSignals;
        }

        return ControlKind.Signal;
    }

    private FolderItemModel CreateFallbackItem(string pageName, string message)
    {
        var item = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = message,
            BodyCaption = message,
            ControlCaption = pageName,
            Footer = "Project",
            X = 48,
            Y = 48,
            Width = 320,
            Height = 84
        };

        item.SetHierarchy(pageName, null);
        item.ApplyTheme(IsDarkTheme);
        return item;
    }

    private FolderModel CreateFolderModelFromLayout(string uiFilePath, ProjectFolderLayout layout, int index, string pageName)
    {
        var model = new FolderModel
        {
            Index = index,
            Views = layout.Views.ToDictionary(static entry => entry.Key, static entry => entry.Value),
            Name = pageName,
            DisplayText = string.IsNullOrWhiteSpace(layout.Caption) ? (string.IsNullOrWhiteSpace(layout.Title) ? pageName : layout.Title) : layout.Caption,
            UiFilePath = uiFilePath,
            UiLayoutDefinition = layout
        };

        foreach (var item in CreateItemsFromNode(pageName, layout.Layout, 24, 24))
        {
            model.Items.Add(item);
        }

        model.ActualViewId = model.Views.ContainsKey(1)
            ? 1
            : model.Views.Keys.OrderBy(static key => key).FirstOrDefault();

        return model;
    }

    private void StartBookWatcher(string directory, string filter, bool includeSubdirectories)
    {
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = includeSubdirectories,
                Filter = filter,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            watcher.Created += OnBookFileChanged;
            watcher.Changed += OnBookFileChanged;
            watcher.Deleted += OnBookFileDeleted;
            watcher.Renamed += OnBookFileRenamed;
            watcher.EnableRaisingEvents = true;

            _bookWatcher = watcher;
        }
        catch (Exception ex)
        {
            AddMessage("Watch", "Error", ex.Message, directory);
        }
    }

    private void RestartBookWatcher()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath) || !Directory.Exists(ProjectPath))
        {
            return;
        }

        if (_isStructuredBook)
        {
            var pagesDirectory = GetPagesDirectoryPath(ProjectPath);
            if (Directory.Exists(pagesDirectory))
            {
                StartBookWatcher(pagesDirectory, StructuredLayoutWatcherFilter, true);
            }

            return;
        }

        StartBookWatcher(ProjectPath, "*.yaml", false);
    }

    private void StopBookWatcher()
    {
        if (_bookWatcher is null)
        {
            return;
        }

        try
        {
            _bookWatcher.EnableRaisingEvents = false;
            _bookWatcher.Created -= OnBookFileChanged;
            _bookWatcher.Changed -= OnBookFileChanged;
            _bookWatcher.Deleted -= OnBookFileDeleted;
            _bookWatcher.Renamed -= OnBookFileRenamed;
            _bookWatcher.Dispose();
        }
        catch
        {
            // Ignore dispose errors; watcher is best-effort.
        }
        finally
        {
            _bookWatcher = null;
        }
    }

    private void OnBookFileChanged(object sender, FileSystemEventArgs e)
    {
        // Normalize to full path for mapping consistency.
        var fullPath = Path.GetFullPath(e.FullPath);

        Dispatcher.UIThread.Post(() =>
        {
            if (e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Changed)
            {
                if (ShouldIgnoreWatcherUpsert(fullPath, DateTime.UtcNow))
                {
                    return;
                }

                _ = HandleBookFileUpsertAsync(fullPath);
            }
        });
    }

    protected override void OnPageYamlFileSaving(string yamlPath)
    {
        var fullPath = Path.GetFullPath(yamlPath);
        _watcherSuppressedPaths[fullPath] = DateTime.UtcNow.Add(WatcherSaveSuppressionWindow);
        _watcherDebouncedUpserts[fullPath] = DateTime.UtcNow;
    }

    private void OnBookFileDeleted(object sender, FileSystemEventArgs e)
    {
        var fullPath = Path.GetFullPath(e.FullPath);

        Dispatcher.UIThread.Post(() =>
        {
            if (ShouldIgnoreWatcherUpsert(fullPath, DateTime.UtcNow))
            {
                return;
            }

            if (_watchedPages.Remove(fullPath))
            {
                UpdatePagesFromWatchedPages(ProjectPath);
            }
        });
    }

    private void OnBookFileRenamed(object sender, RenamedEventArgs e)
    {
        var oldFullPath = Path.GetFullPath(e.OldFullPath);
        var newFullPath = Path.GetFullPath(e.FullPath);

        Dispatcher.UIThread.Post(() =>
        {
            var now = DateTime.UtcNow;
            if (ShouldIgnoreWatcherUpsert(newFullPath, now) || ShouldIgnoreWatcherUpsert(oldFullPath, now))
            {
                return;
            }

            _watchedPages.Remove(oldFullPath);
            _ = HandleBookFileUpsertAsync(newFullPath);
        });
    }

    private bool ShouldIgnoreWatcherUpsert(string fullPath, DateTime now)
    {
        if (_watcherSuppressedPaths.TryGetValue(fullPath, out var suppressedUntil))
        {
            if (suppressedUntil >= now)
            {
                return true;
            }

            _watcherSuppressedPaths.TryRemove(fullPath, out _);
        }

        if (_watcherDebouncedUpserts.TryGetValue(fullPath, out var lastAcceptedChange)
            && now - lastAcceptedChange < WatcherUpsertDebounceWindow)
        {
            return true;
        }

        _watcherDebouncedUpserts[fullPath] = now;
        return false;
    }

    private async Task HandleBookFileUpsertAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        if (!_watcherUpsertsInProgress.Add(filePath))
        {
            return;
        }

        try
        {
            var fallbackName = GetPageNameFallback(filePath);
            var layout = ProjectUiLayoutLoader.LoadYaml(filePath, fallbackName);
            var pageName = GetTechnicalPageName(filePath);
            var resolvedPageName = await ResolveWatchedPageNameConflictAsync(filePath, pageName);
            if (string.IsNullOrWhiteSpace(resolvedPageName))
            {
                StatusText = $"Folder import skipped for duplicate name: {pageName}";
                return;
            }

            var effectivePath = filePath;
            if (!string.Equals(resolvedPageName, pageName, StringComparison.Ordinal))
            {
                effectivePath = RenamePageStorage(filePath, resolvedPageName);
                fallbackName = GetPageNameFallback(effectivePath);
                layout = ProjectUiLayoutLoader.LoadYaml(effectivePath, fallbackName);
            }

            pageName = GetTechnicalPageName(effectivePath);
            var watchedPage = new WatchedPage
            {
                FilePath = effectivePath,
                PageName = pageName,
                Layout = layout
            };

            _watchedPages[effectivePath] = watchedPage;
            UpdatePagesFromWatchedPages(ProjectPath);
        }
        catch (Exception ex)
        {
            AddMessage("UI", "Error", ex.Message, filePath);
        }
        finally
        {
            _watcherUpsertsInProgress.Remove(filePath);
            if (_watcherDebouncedUpserts.TryGetValue(filePath, out var lastAcceptedChange)
                && DateTime.UtcNow - lastAcceptedChange >= WatcherUpsertDebounceWindow)
            {
                _watcherDebouncedUpserts.TryRemove(filePath, out _);
            }
        }
    }

    private async Task<string?> ResolveWatchedPageNameConflictAsync(string filePath, string requestedPageName)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedPageName)
            ? Path.GetFileNameWithoutExtension(filePath)
            : requestedPageName.Trim();

        while (HasDuplicatePageName(filePath, candidate))
        {
            if (ResolveDuplicatePageNameAsync is null)
            {
                AddMessage("Watch", "Warning", $"Duplicate folder name detected: {candidate}", filePath);
                return null;
            }

            var prompt = $"A folder named '{candidate}' already exists. Enter a new file-based folder name.";
            var replacement = await ResolveDuplicatePageNameAsync(candidate, prompt);
            if (string.IsNullOrWhiteSpace(replacement))
            {
                return null;
            }

            candidate = replacement.Trim();
        }

        return candidate;
    }

    private bool HasDuplicatePageName(string filePath, string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
        {
            return false;
        }

        return _watchedPages.Values.Any(page => !string.Equals(page.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(page.PageName, pageName, StringComparison.OrdinalIgnoreCase))
            || Folders.Any(page => !string.Equals(page.UiFilePath, filePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(page.Name, pageName, StringComparison.OrdinalIgnoreCase));
    }

    private string RenamePageStorage(string filePath, string pageName)
    {
        var fullPath = Path.GetFullPath(filePath);
        var pageFileName = Path.GetFileName(fullPath);
        var parentDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;

        if (string.Equals(pageFileName, FolderLayoutFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(pageFileName, LegacyFolderLayoutFileName, StringComparison.OrdinalIgnoreCase))
        {
            var currentFolderDirectory = parentDirectory;
            var projectFoldersDirectory = Path.GetDirectoryName(currentFolderDirectory) ?? string.Empty;
            var safeDirectoryName = BuildSafePageDirectoryName(pageName);
            if (string.IsNullOrWhiteSpace(safeDirectoryName))
            {
                throw new InvalidOperationException("Folder name contains no valid directory name characters.");
            }

            var targetFolderDirectory = Path.Combine(projectFoldersDirectory, safeDirectoryName);
            if (string.Equals(currentFolderDirectory, targetFolderDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            if (Directory.Exists(targetFolderDirectory))
            {
                throw new IOException($"A folder directory named '{safeDirectoryName}' already exists.");
            }

            Directory.Move(currentFolderDirectory, targetFolderDirectory);
            return GetPageLayoutPath(targetFolderDirectory);
        }

        var safeFileName = BuildSafePageDirectoryName(pageName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException("Folder name contains no valid file name characters.");
        }

        var targetFilePath = Path.Combine(parentDirectory, $"{safeFileName}.yaml");
        if (string.Equals(fullPath, targetFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        if (File.Exists(targetFilePath))
        {
            throw new IOException($"A folder file named '{Path.GetFileName(targetFilePath)}' already exists.");
        }

        File.Move(fullPath, targetFilePath);
        return targetFilePath;
    }

    private static string NormalizeYamlPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return string.Equals(Path.GetExtension(path), ".yaml", StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.ChangeExtension(path, ".yaml");
    }

    private void UpdatePagesFromWatchedPages(string directory)
    {
        ApplyBookManifestSettings(directory);

        var watchedPages = _watchedPages
            .Values
            .OrderBy(page => page.PageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(page => page.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (watchedPages.Count == 0)
        {
            ProjectPath = directory;
            _currentLayoutFilePath = string.Empty;
            HasLayout = false;
            Isdefault_layout = false;
            HeaderTitle = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "HornetStudio";
            SetFolders(Array.Empty<FolderModel>());
            LoadedProjectSummary = "No folders found";
            StatusText = $"No folders found in directory: {directory}";
            OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
            OnPropertyChanged(nameof(StartLayoutIconColor));
            return;
        }

        var pages = new List<FolderModel>(watchedPages.Count);
        for (var i = 0; i < watchedPages.Count; i++)
        {
            var watched = watchedPages[i];
            var model = CreateFolderModelFromLayout(watched.FilePath, watched.Layout, i + 1, watched.PageName);
            pages.Add(model);
        }

        var orderedPages = ApplyFolderOrder(pages);
        SetFolders(orderedPages);
        ProjectPath = directory;
        _currentLayoutFilePath = orderedPages[0].UiFilePath ?? watchedPages[0].FilePath;
        HasLayout = true;
        Isdefault_layout = false;
        HeaderTitle = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "HornetStudio";
        LoadedProjectSummary = _isStructuredBook
            ? $"HornetStudio | Folders: {orderedPages.Count}"
            : $"Directory project | Folders: {orderedPages.Count}";
        StatusText = _isStructuredBook
            ? $"HornetStudio loaded: {directory}"
            : $"Directory loaded: {directory}";
        OnPropertyChanged(nameof(IsCurrentLayoutStartLayout));
        OnPropertyChanged(nameof(StartLayoutIconColor));
    }

}
