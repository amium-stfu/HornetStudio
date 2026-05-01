using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HornetStudio.Editor.Controls;
using HornetStudio.Host;
using HornetStudio.Host.Python.Client;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class ApplicationExplorerControl : EditorTemplateControl
{
    public static readonly DirectProperty<ApplicationExplorerControl, bool> CanStartAllProperty =
        AvaloniaProperty.RegisterDirect<ApplicationExplorerControl, bool>(nameof(CanStartAll), control => control.CanStartAll);

    public static readonly DirectProperty<ApplicationExplorerControl, bool> CanStopAllProperty =
        AvaloniaProperty.RegisterDirect<ApplicationExplorerControl, bool>(nameof(CanStopAll), control => control.CanStopAll);

    public static readonly DirectProperty<ApplicationExplorerControl, bool> HasNoEnvironmentsProperty =
        AvaloniaProperty.RegisterDirect<ApplicationExplorerControl, bool>(nameof(HasNoEnvironments), control => control.HasNoEnvironments);

    public static readonly DirectProperty<ApplicationExplorerControl, string> StartAllButtonTextProperty =
        AvaloniaProperty.RegisterDirect<ApplicationExplorerControl, string>(nameof(StartAllButtonText), control => control.StartAllButtonText);

    public static readonly DirectProperty<ApplicationExplorerControl, string> StartAllButtonBackgroundProperty =
        AvaloniaProperty.RegisterDirect<ApplicationExplorerControl, string>(nameof(StartAllButtonBackground), control => control.StartAllButtonBackground);

    public static readonly DirectProperty<ApplicationExplorerControl, string> StartAllButtonForegroundProperty =
        AvaloniaProperty.RegisterDirect<ApplicationExplorerControl, string>(nameof(StartAllButtonForeground), control => control.StartAllButtonForeground);

    private FolderItemModel? _observedItem;
    private bool _canStartAll;
    private bool _canStopAll;
    private bool _hasNoEnvironments;

    private string _startAllButtonText = "Start all";
    private string _startAllButtonBackground = string.Empty;
    private string _startAllButtonForeground = string.Empty;

    public ObservableCollection<ApplicationEntryRow> Environments { get; } = [];

    public bool CanStartAll
    {
        get => _canStartAll;
        private set => SetAndRaise(CanStartAllProperty, ref _canStartAll, value);
    }

    public bool CanStopAll
    {
        get => _canStopAll;
        private set => SetAndRaise(CanStopAllProperty, ref _canStopAll, value);
    }

    public bool HasNoEnvironments
    {
        get => _hasNoEnvironments;
        private set => SetAndRaise(HasNoEnvironmentsProperty, ref _hasNoEnvironments, value);
    }

    public string StartAllButtonText
    {
        get => _startAllButtonText;
        private set => SetAndRaise(StartAllButtonTextProperty, ref _startAllButtonText, value);
    }

    public string StartAllButtonBackground
    {
        get => _startAllButtonBackground;
        private set => SetAndRaise(StartAllButtonBackgroundProperty, ref _startAllButtonBackground, value);
    }

    public string StartAllButtonForeground
    {
        get => _startAllButtonForeground;
        private set => SetAndRaise(StartAllButtonForegroundProperty, ref _startAllButtonForeground, value);
    }

    private FolderItemModel? Item => DataContext as FolderItemModel;

    public ApplicationExplorerControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookObservedItem();
        RebuildEnvironmentList();
        ApplicationExplorerRuntime.StartAutoStartEnvironments(_observedItem);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookObservedItem();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookObservedItem();
        RebuildEnvironmentList();
    }

    private void HookObservedItem()
    {
        if (ReferenceEquals(_observedItem, Item))
        {
            return;
        }

        UnhookObservedItem();
        _observedItem = Item;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnObservedItemPropertyChanged;
        }
    }

    private void UnhookObservedItem()
    {
        if (_observedItem is null)
        {
            return;
        }

        _observedItem.PropertyChanged -= OnObservedItemPropertyChanged;
        _observedItem = null;
    }

    private void OnObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var propertyName = e.PropertyName;
            Dispatcher.UIThread.Post(() => OnObservedItemPropertyChanged(sender, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(FolderItemModel.ApplicationDefinitions))
        {
            RebuildEnvironmentList();
            return;
        }

        // React to theme / effective color changes so buttons and rows follow the palette
        if (e.PropertyName is nameof(FolderItemModel.EffectiveBodyBackground)
            or nameof(FolderItemModel.EffectiveBodyForeground)
            or nameof(FolderItemModel.EffectiveBodyBorder)
            or nameof(FolderItemModel.EffectiveMutedForeground)
            or nameof(FolderItemModel.EffectiveButtonBodyBackground)
            or nameof(FolderItemModel.EffectiveButtonBodyForeground)
            or nameof(FolderItemModel.EffectiveAccentBackground)
            or nameof(FolderItemModel.EffectiveAccentForeground))
        {
            OnItemThemeChanged();
        }
    }

    private void OnItemThemeChanged()
    {
        foreach (var row in Environments)
        {
            row.OnThemeChanged();
        }

        // update header button colors as well
        UpdateStateFlags();
    }

    private void RebuildEnvironmentList()
    {
        foreach (var existing in Environments.ToList())
        {
            existing.PropertyChanged -= OnRowPropertyChanged;
        }

        Environments.Clear();

        var item = _observedItem;
        var raw = item?.ApplicationDefinitions ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            UpdateStateFlags();
            return;
        }

        foreach (var row in ApplicationExplorerRuntime.BuildRows(item))
        {
            row.PropertyChanged += OnRowPropertyChanged;
            Environments.Add(row);
        }

        UpdateStateFlags();
    }

    private void UpdateStateFlags()
    {
        var anyEnv = Environments.Count > 0;
        var anyRunning = Environments.Any(row => row.IsRunning);
        var anyError = Environments.Any(row => row.HasError);

        CanStartAll = anyEnv;
        CanStopAll = anyRunning;
        HasNoEnvironments = !anyEnv;

        UpdateHeaderVisualState(anyRunning, anyError);
    }

    private void UpdateHeaderVisualState(bool anyRunning, bool anyError)
    {
        var item = _observedItem;
        var defaultBackground = item?.EffectiveAccentBackground ?? string.Empty;
        var defaultForeground = item?.EffectiveAccentForeground ?? string.Empty;

        if (anyRunning)
        {
            StartAllButtonText = "Stop all";
            StartAllButtonBackground = "#EA580C";
            StartAllButtonForeground = string.IsNullOrWhiteSpace(defaultForeground) ? "#F9FAFB" : defaultForeground;
        }
        else if (anyError)
        {
            StartAllButtonText = "Error";
            StartAllButtonBackground = "Tomato";
            StartAllButtonForeground = string.IsNullOrWhiteSpace(defaultForeground) ? "#F9FAFB" : defaultForeground;
        }
        else
        {
            StartAllButtonText = "Start all";
            StartAllButtonBackground = defaultBackground;
            StartAllButtonForeground = defaultForeground;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationEntryRow.IsRunning) || e.PropertyName == nameof(ApplicationEntryRow.HasError))
        {
            UpdateStateFlags();
        }
    }

    private void OnToggleAllClicked(object? sender, RoutedEventArgs e)
    {
        if (Environments.Any(env => env.IsRunning))
        {
            foreach (var env in Environments)
            {
                env.Stop();
            }
        }
        else
        {
            foreach (var env in Environments)
            {
                env.Start();
            }
        }

        UpdateStateFlags();
        e.Handled = true;
    }

    private void HandleInteractivePointerPressed(PointerPressedEventArgs e)
    {
        // keep for potential future interaction handling
    }

    private async void OnEnvironmentRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not Border { DataContext: ApplicationEntryRow row } || !row.HasError || !row.HasErrorDetails)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new ApplicationErrorDialogWindow();
        dialog.Initialize(DataContext as MainWindowViewModel, row.ErrorDetails!);
        await dialog.ShowDialog(owner);
        e.Handled = true;
    }
}

public sealed class ApplicationErrorDetails
{
    public string EnvironmentName { get; init; } = string.Empty;
    public string Summary { get; init; } = "Error";
    public string? FullMessage { get; init; }
    public string? File { get; init; }
    public int? LineNumber { get; init; }
    public string? FunctionName { get; init; }
    public string? Traceback { get; init; }

    public static ApplicationErrorDetails FromResultPayload(string environmentName, string fallbackMessage, JsonNode? payload)
    {
        var payloadObject = payload as JsonObject;
        var summary = payloadObject?["exception_type"]?.GetValue<string?>();
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = "Error";
        }

        return new ApplicationErrorDetails
        {
            EnvironmentName = environmentName,
            Summary = summary!,
            FullMessage = payloadObject?["message"]?.GetValue<string?>() ?? fallbackMessage,
            File = payloadObject?["file"]?.GetValue<string?>(),
            LineNumber = payloadObject?["line"]?.GetValue<int?>(),
            FunctionName = payloadObject?["function"]?.GetValue<string?>(),
            Traceback = payloadObject?["traceback"]?.GetValue<string?>()
        };
    }
}

public static class ApplicationExplorerRuntime
{
    public const string InteractionTargetPrefix = "python-env:";

    public static string BuildInteractionTargetPath(FolderItemModel? ownerItem, string? envName)
    {
        var ownerKey = ownerItem?.Path;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            ownerKey = ownerItem?.Id;
        }

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            ownerKey = "Unknown";
        }

        return BuildInteractionTargetPathFromOwnerKey(ownerKey, envName);
    }

    public static bool IsInteractionTargetPath(string? targetPath)
        => !string.IsNullOrWhiteSpace(targetPath)
           && targetPath.Trim().StartsWith(InteractionTargetPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ToPersistedInteractionTargetPath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        var trimmedTargetPath = targetPath.Trim();
        if (!TryParseInteractionTargetPath(trimmedTargetPath, out var ownerKey, out var envName))
        {
            return trimmedTargetPath;
        }

        return $"{GetOwnerDisplayName(ownerKey)}:{envName}";
    }

    public static string ResolveInteractionTargetPath(FolderItemModel? sourceItem, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        var trimmedTargetPath = targetPath.Trim();
        if (IsInteractionTargetPath(trimmedTargetPath))
        {
            return trimmedTargetPath;
        }

        if (!TryParsePersistedInteractionTargetPath(trimmedTargetPath, out var ownerName, out var envName))
        {
            return trimmedTargetPath;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceItem?.FolderName))
        {
            candidates.Add(BuildInteractionTargetPathFromOwnerKey($"{sourceItem.FolderName}.{ownerName}", envName));

            if (sourceItem?.ParentItem is not null && !string.IsNullOrWhiteSpace(sourceItem.ParentItem.Name))
            {
                candidates.Add(BuildInteractionTargetPathFromOwnerKey($"{sourceItem.FolderName}.{sourceItem.ParentItem.Name}.{ownerName}", envName));
            }
        }

        candidates.Add(BuildInteractionTargetPathFromOwnerKey(ownerName, envName));

        var registeredTargets = PythonClientRuntimeRegistry.GetRegisteredTargetPaths();
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var registeredMatch = registeredTargets.FirstOrDefault(path => string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(registeredMatch))
            {
                return registeredMatch;
            }
        }

        var fallbackMatch = registeredTargets.FirstOrDefault(path =>
            TryParseInteractionTargetPath(path, out var registeredOwnerKey, out var registeredEnvName)
            && string.Equals(registeredEnvName, envName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetOwnerDisplayName(registeredOwnerKey), ownerName, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(sourceItem?.FolderName)
                || registeredOwnerKey.StartsWith($"{sourceItem.FolderName}.", StringComparison.OrdinalIgnoreCase)));

        return !string.IsNullOrWhiteSpace(fallbackMatch)
            ? fallbackMatch
            : candidates.FirstOrDefault() ?? trimmedTargetPath;
    }

    public static string GetInteractionTargetDisplayText(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        var trimmedTargetPath = targetPath.Trim();
        if (TryParseInteractionTargetPath(trimmedTargetPath, out var ownerKey, out var envName))
        {
            return $"{GetOwnerDisplayName(ownerKey)} -> {envName}";
        }

        if (TryParsePersistedInteractionTargetPath(trimmedTargetPath, out var ownerName, out envName))
        {
            return $"{ownerName} -> {envName}";
        }

        return trimmedTargetPath;
    }

    public static IReadOnlyList<ApplicationEntryRow> BuildRows(FolderItemModel? ownerItem, string? baseDirectory = null)
        => BuildRows(ownerItem, ownerItem?.ApplicationDefinitions, baseDirectory);

    public static IReadOnlyList<ApplicationEntryRow> BuildRows(FolderItemModel? ownerItem, string? rawDefinitions, string? baseDirectory = null)
    {
        if (ownerItem is null || string.IsNullOrWhiteSpace(rawDefinitions))
        {
            return Array.Empty<ApplicationEntryRow>();
        }

        var effectiveBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? ApplicationDefinitionHelper.TryResolveLayoutDirectory(ownerItem.FolderLayoutPath)
            : baseDirectory;

        return ApplicationDefinitionHelper.ParseDefinitions(rawDefinitions)
            .Select(env =>
            {
                var effectivePath = ApplicationDefinitionHelper.ResolveScriptPath(env.ScriptPath, effectiveBaseDirectory);
                return ApplicationEntryRegistry.GetOrCreate(env.Name, env.ScriptPath, effectivePath, env.StartupDelayMs, ownerItem);
            })
            .ToArray();
    }

    public static void StartAutoStartEnvironments(FolderItemModel? ownerItem, string? baseDirectory = null)
    {
        if (ownerItem is null || !ownerItem.ApplicationAutoStart)
        {
            return;
        }

        foreach (var row in BuildRows(ownerItem, baseDirectory))
        {
            row.Start();
        }
    }

    public static void StopAllEnvironments()
    {
        foreach (var row in ApplicationEntryRegistry.GetAll())
        {
            row.Stop();
        }
    }

    private static string BuildInteractionTargetPathFromOwnerKey(string? ownerKey, string? envName)
    {
        var normalizedOwnerKey = string.IsNullOrWhiteSpace(ownerKey) ? "Unknown" : ownerKey.Trim();
        var normalizedEnvName = string.IsNullOrWhiteSpace(envName) ? "PythonEnv" : envName.Trim();
        return $"{InteractionTargetPrefix}{normalizedOwnerKey}:{normalizedEnvName}";
    }

    private static bool TryParseInteractionTargetPath(string targetPath, out string ownerKey, out string envName)
    {
        ownerKey = string.Empty;
        envName = string.Empty;

        if (!IsInteractionTargetPath(targetPath))
        {
            return false;
        }

        var remainder = targetPath.Trim()[InteractionTargetPrefix.Length..];
        var separatorIndex = remainder.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= remainder.Length - 1)
        {
            return false;
        }

        ownerKey = remainder[..separatorIndex].Trim();
        envName = remainder[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(ownerKey) && !string.IsNullOrWhiteSpace(envName);
    }

    private static bool TryParsePersistedInteractionTargetPath(string targetPath, out string ownerName, out string envName)
    {
        ownerName = string.Empty;
        envName = string.Empty;

        if (IsInteractionTargetPath(targetPath))
        {
            return false;
        }

        var separatorIndex = targetPath.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= targetPath.Length - 1)
        {
            return false;
        }

        ownerName = targetPath[..separatorIndex].Trim();
        envName = targetPath[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(ownerName) && !string.IsNullOrWhiteSpace(envName);
    }

    private static string GetOwnerDisplayName(string ownerKey)
    {
        var normalizedOwnerKey = ownerKey.Replace('/', '.').Trim('.');
        var segments = normalizedOwnerKey.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.LastOrDefault() ?? normalizedOwnerKey;
    }
}

public sealed class ApplicationEntryRow : INotifyPropertyChanged
{
    private bool _isRunning;
    private ATask? _task;
    private bool _hasError;
    private string _lastError = string.Empty;
    private FolderItemModel? _ownerItem;
    private string _name = string.Empty;
    private string _scriptPath = string.Empty;
    private string _effectiveScriptPath = string.Empty;
    private int _startupDelayMs;
    private string _startupDelayEditText = "0";
    private ApplicationErrorDetails? _errorDetails;

    public ApplicationEntryRow(string name, string scriptPath, string? effectiveScriptPath, int startupDelayMs, FolderItemModel? ownerItem)
    {
        StartCommand = new RelayCommand(Start);
        StopCommand = new RelayCommand(Stop);
        ToggleCommand = new RelayCommand(Toggle);
        EditCommand = new RelayCommand(Edit);

        UpdateDefinition(name, scriptPath, effectiveScriptPath, startupDelayMs, ownerItem);
    }

    public string Name => _name;

    public string InteractionTargetPath => ApplicationExplorerRuntime.BuildInteractionTargetPath(OwnerItem, Name);

    public string ScriptPath => _scriptPath;

    public string EffectiveScriptPath => _effectiveScriptPath;

    private FolderItemModel? OwnerItem => _ownerItem;

    public string ScriptSummary => ScriptPath;

    public int StartupDelayMs
    {
        get => _startupDelayMs;
        set
        {
            var normalized = Math.Max(0, value);
            if (_startupDelayMs == normalized)
            {
                return;
            }

            _startupDelayMs = normalized;
            OnPropertyChanged(nameof(StartupDelayMs));
            OnPropertyChanged(nameof(StartupDelayText));
            _startupDelayEditText = normalized.ToString();
            OnPropertyChanged(nameof(StartupDelayEditText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StartupDelayText
    {
        get => StartupDelayMs.ToString();
        set
        {
            if (!int.TryParse(value?.Trim(), out var parsed))
            {
                parsed = 0;
            }

            StartupDelayMs = parsed;
        }
    }

    public string StartupDelayEditText
    {
        get => _startupDelayEditText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_startupDelayEditText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _startupDelayEditText = normalized;
            OnPropertyChanged(nameof(StartupDelayEditText));
        }
    }

    public void CommitStartupDelayEdit()
    {
        StartupDelayText = _startupDelayEditText;
        StartupDelayEditText = StartupDelayMs.ToString();
    }

    public void SetInvocationError(string? message)
    {
        HasError = true;
        LastError = string.IsNullOrWhiteSpace(message)
            ? "Python-Funktion ist fehlgeschlagen."
            : message!;
        ErrorDetails = new ApplicationErrorDetails
        {
            EnvironmentName = Name,
            Summary = "Error",
            FullMessage = LastError,
            Traceback = LastError
        };
    }

    public void SetInvocationError(ApplicationErrorDetails details)
    {
        ErrorDetails = details;
        HasError = true;
        LastError = "Error";
    }

    public void ClearInvocationError()
    {
        ErrorDetails = null;
        HasError = false;
        LastError = string.Empty;
    }

    public ApplicationErrorDetails? ErrorDetails
    {
        get => _errorDetails;
        private set
        {
            if (ReferenceEquals(_errorDetails, value))
            {
                return;
            }

            _errorDetails = value;
            OnPropertyChanged(nameof(ErrorDetails));
            OnPropertyChanged(nameof(HasErrorDetails));
        }
    }

    public bool HasErrorDetails => ErrorDetails is not null;

    public string LastError
    {
        get => _lastError;
        private set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_lastError, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _lastError = normalized;
            OnPropertyChanged(nameof(LastError));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText
        => HasError
            ? $"{Name} Error"
            : IsRunning
                ? (StartupDelayMs > 0 ? $"{Name} Running ({StartupDelayMs} ms delay)" : $"{Name} Running")
                : (StartupDelayMs > 0 ? $"{Name} Stopped ({StartupDelayMs} ms delay)" : $"{Name} Stopped");

    public void UpdateDefinition(string name, string scriptPath, string? effectiveScriptPath, int startupDelayMs, FolderItemModel? ownerItem)
    {
        _ownerItem = ownerItem;

        var normalizedName = string.IsNullOrWhiteSpace(name) ? "Env" : name.Trim();
        var normalizedScriptPath = scriptPath?.Trim() ?? string.Empty;
        var normalizedEffectivePath = !string.IsNullOrWhiteSpace(effectiveScriptPath)
            ? effectiveScriptPath.Trim()
            : normalizedScriptPath;

        normalizedEffectivePath = Path.IsPathRooted(normalizedEffectivePath)
            ? normalizedEffectivePath
            : ResolveEffectivePath(ownerItem, normalizedEffectivePath);

        var changed = false;

        if (!string.Equals(_name, normalizedName, StringComparison.Ordinal))
        {
            _name = normalizedName;
            changed = true;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(StatusText));
        }

        if (!string.Equals(_scriptPath, normalizedScriptPath, StringComparison.Ordinal))
        {
            _scriptPath = normalizedScriptPath;
            changed = true;
            OnPropertyChanged(nameof(ScriptPath));
            OnPropertyChanged(nameof(ScriptSummary));
        }

        if (!string.Equals(_effectiveScriptPath, normalizedEffectivePath, StringComparison.Ordinal))
        {
            _effectiveScriptPath = normalizedEffectivePath;
            changed = true;
            OnPropertyChanged(nameof(EffectiveScriptPath));
        }

        StartupDelayMs = startupDelayMs;

        if (changed)
        {
            OnThemeChanged();
        }
    }

    private static string ResolveEffectivePath(FolderItemModel? ownerItem, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var layoutPath = ownerItem?.FolderLayoutPath;
        if (!string.IsNullOrWhiteSpace(layoutPath))
        {
            try
            {
                var fullLayoutPath = Path.GetFullPath(layoutPath);
                var layoutDirectory = Path.GetDirectoryName(fullLayoutPath);
                if (!string.IsNullOrWhiteSpace(layoutDirectory))
                {
                    return Path.Combine(layoutDirectory, path);
                }
            }
            catch
            {
                // Ignore and fall back to the original relative path.
            }
        }

        return path;
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnStateChanged();
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError == value)
            {
                return;
            }

            _hasError = value;
            OnStateChanged();
        }
    }

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand ToggleCommand { get; }

    public ICommand EditCommand { get; }

    public string ToggleButtonText => IsRunning ? "Stop" : "Start";

    public void Start()
    {
        if (_task is { IsRunning: true })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EffectiveScriptPath))
        {
            Core.LogWarn($"[ApplicationExplorer] Cannot start '{Name}': script path is empty.");
            return;
        }

        try
        {
            var scriptPath = EffectiveScriptPath;
            var clientName = BuildClientName(Name);
            var targetPath = ApplicationExplorerRuntime.BuildInteractionTargetPath(OwnerItem, Name);
            Core.LogInfo($"[ApplicationExplorer] Starting '{Name}' -> {scriptPath} (DelayMs={StartupDelayMs})");

            _task?.Stop();
            _task = null;

            var instanceName = $"PythonEnv:{targetPath}";
            _task = new ATask(instanceName, token => RunEnvAsync(scriptPath, clientName, targetPath, StartupDelayMs, token));

            _task.OnCompleted += () => Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                HasError = false;
                LastError = string.Empty;
                ErrorDetails = null;
            });

            _task.OnCancelled += () => Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
            });

            _task.OnException += ex => Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                HasError = true;
                LastError = "Error";
                ErrorDetails = new ApplicationErrorDetails
                {
                    EnvironmentName = Name,
                    Summary = ex.GetType().Name,
                    FullMessage = ex.Message,
                    Traceback = ex.ToString()
                };
            });

            IsRunning = true;
            HasError = false;
            LastError = string.Empty;
            ErrorDetails = null;
        }
        catch (Exception ex)
        {
            Core.LogError($"[ApplicationExplorer] Failed to start application '{Name}'", ex);
            IsRunning = false;
            HasError = true;
            LastError = "Error";
            ErrorDetails = new ApplicationErrorDetails
            {
                EnvironmentName = Name,
                Summary = ex.GetType().Name,
                FullMessage = ex.Message,
                Traceback = ex.ToString()
            };
        }
    }

    public void Toggle()
    {
        if (IsRunning)
        {
            Stop();
            return;
        }

        Start();
    }

    public void Stop()
    {
        Core.LogInfo($"[ApplicationExplorer] Stop requested for '{Name}' -> {EffectiveScriptPath}");

        try
        {
            _task?.Stop();
        }
        catch (Exception ex)
        {
            Core.LogWarn($"[ApplicationExplorer] Error while stopping application '{Name}'", ex);
        }
        finally
        {
            _task = null;
            IsRunning = false;
        }
    }

    public void Edit()
    {
        try
        {
            var path = EffectiveScriptPath;
            var directory = Directory.Exists(path)
                ? path
                : (Path.GetDirectoryName(path) ?? path);

            if (!Directory.Exists(directory))
            {
                Core.LogWarn($"[ApplicationExplorer] Edit requested for '{Name}', but directory not found: {directory}");
                return;
            }

            VsCodeLauncher.OpenPythonEnvironmentFolder(directory, newWindow: true);
        }
        catch (Exception ex)
        {
            Core.LogWarn($"[ApplicationExplorer] Edit failed for '{Name}' -> {ScriptPath}", ex);
        }
    }

    private static string BuildClientName(string name)
        => string.IsNullOrWhiteSpace(name) ? "PythonEnv" : name.Trim();

    private static string BuildValueRegistryRootPath(FolderItemModel? ownerItem, string? envName)
    {
        var folderName = string.IsNullOrWhiteSpace(ownerItem?.FolderName) ? "Page" : ownerItem!.FolderName.Trim();
        var normalizedEnvName = string.IsNullOrWhiteSpace(envName) ? "PythonEnv" : envName.Trim();
        return $"Studio.{folderName}.Applications.Python.{normalizedEnvName}";
    }

    private async Task RunEnvAsync(string scriptPath, string clientName, string targetPath, int startupDelayMs, CancellationToken cancellationToken)
    {
        var options = new PythonClientOptions
        {
            Name = clientName,
            ClientType = "PythonEnv",
            ScriptPath = scriptPath,
            RegistryRootPath = BuildValueRegistryRootPath(OwnerItem, Name),
            AllowedCapabilities = new[] { "functions", "host_log", "values" }
        };

        try
        {
            if (startupDelayMs > 0)
            {
                Core.LogInfo($"[ApplicationExplorer] Delaying start of '{clientName}' by {startupDelayMs} ms.");
                await Task.Delay(startupDelayMs, cancellationToken).ConfigureAwait(false);
            }

            await using var client = new PythonClient(options);
            await client.StartAsync(cancellationToken);
            PythonClientRuntimeRegistry.Register(targetPath, clientName, client);

            var exitTask = client.WaitForProcessExitAsync(cancellationToken);
            var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var completedTask = await Task.WhenAny(exitTask, cancellationTask).ConfigureAwait(false);

            if (completedTask == exitTask)
            {
                var exitCode = await exitTask.ConfigureAwait(false);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"Python application '{clientName}' exited with code {exitCode}.");
                }

                return;
            }

            try
            {
                await client.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Core.LogError($"[ApplicationExplorer] Soft stop failed for '{clientName}'", ex);
                await client.HardStopAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected when the application is stopped
        }
        catch (Exception ex)
        {
            Core.LogError($"[ApplicationExplorer] Application '{clientName}' failed.", ex);
            throw;
        }
        finally
        {
            PythonClientRuntimeRegistry.Unregister(targetPath);
        }
    }

    public bool CanStart => !IsRunning;

    public string DelaySummary => StartupDelayMs <= 0 ? "0" : StartupDelayMs.ToString();

    public string StartButtonText => HasError ? "Error" : (IsRunning ? "Running" : "Start");

    public string StartButtonBackground
    {
        get
        {
            if (HasError)
            {
                return "Tomato";
            }

            if (IsRunning)
            {
                return "#16A34A";
            }

            var item = OwnerItem;
            return item?.EffectiveButtonBodyBackground ?? string.Empty;
        }
    }

    public string StatusIndicatorBackground
    {
        get
        {
            if (HasError)
            {
                return "#EAB308";
            }

            if (IsRunning)
            {
                return "#16A34A";
            }

            var item = OwnerItem;
            return item?.EffectiveButtonBodyBackground ?? string.Empty;
        }
    }

    public string StartButtonForeground
    {
        get
        {
            var item = OwnerItem;
            return item?.EffectiveButtonBodyForeground ?? "#F9FAFB";
        }
    }

    // Settings dialog specific visuals

    public string SettingsStartButtonBackground
    {
        get
        {
            var defaultBackground = OwnerItem?.EffectiveButtonBodyBackground ?? "#4B5563";

            if (HasError)
            {
                return defaultBackground;
            }

            if (IsRunning)
            {
                // Highlight Start when the environment is running
                return "#EA580C";
            }

            return defaultBackground;
        }
    }

    public string SettingsStartButtonForeground
    {
        get
        {
            var item = OwnerItem;
            return item?.EffectiveButtonBodyForeground ?? "#F9FAFB";
        }
    }

    public string SettingsStopButtonBackground
    {
        get
        {
            var defaultBackground = OwnerItem?.EffectiveButtonBodyBackground ?? "#4B5563";

            if (HasError)
            {
                return defaultBackground;
            }

            if (!IsRunning)
            {
                // Highlight Stop when the environment is not running
                return "#EA580C";
            }

            return defaultBackground;
        }
    }

    public string SettingsStopButtonForeground
    {
        get
        {
            var item = OwnerItem;
            return item?.EffectiveButtonBodyForeground ?? "#F9FAFB";
        }
    }

    public string RowBackground
    {
        get
        {
            var item = OwnerItem;
            if (HasError)
            {
                return "Tomato";
            }

            return item?.EffectiveBodyBackground ?? "#111827";
        }
    }

    public string RowBorderBrush
    {
        get
        {
            if (HasError)
            {
                return "Tomato";
            }

            var item = OwnerItem;
            return item?.EffectiveBodyBorder ?? "#1F2937";
        }
    }

    public double RowBorderThickness => HasError ? 1 : 0;

    public string RowPrimaryForeground
    {
        get
        {
            var item = OwnerItem;
            return item?.EffectiveBodyForeground ?? "#F9FAFB";
        }
    }

    public string RowSecondaryForeground
    {
        get
        {
            var item = OwnerItem;
            return item?.EffectiveMutedForeground ?? "#9CA3AF";
        }
    }

    public string SettingsRowBackground => HasError ? "Tomato" : "Transparent";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(DelaySummary));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonBackground));
        OnPropertyChanged(nameof(StatusIndicatorBackground));
        OnPropertyChanged(nameof(StartButtonForeground));
        OnPropertyChanged(nameof(SettingsStartButtonBackground));
        OnPropertyChanged(nameof(SettingsStartButtonForeground));
        OnPropertyChanged(nameof(SettingsStopButtonBackground));
        OnPropertyChanged(nameof(SettingsStopButtonForeground));
        OnPropertyChanged(nameof(ToggleButtonText));
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(SettingsRowBackground));
        OnPropertyChanged(nameof(RowBorderBrush));
        OnPropertyChanged(nameof(RowBorderThickness));
    }

    public void OnThemeChanged()
    {
        // Theme / palette changed on the owning item; refresh all visuals
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(RowBorderBrush));
        OnPropertyChanged(nameof(RowPrimaryForeground));
        OnPropertyChanged(nameof(RowSecondaryForeground));
        OnPropertyChanged(nameof(StartButtonBackground));
        OnPropertyChanged(nameof(StatusIndicatorBackground));
        OnPropertyChanged(nameof(StartButtonForeground));
        OnPropertyChanged(nameof(SettingsStartButtonBackground));
        OnPropertyChanged(nameof(SettingsStartButtonForeground));
        OnPropertyChanged(nameof(SettingsStopButtonBackground));
        OnPropertyChanged(nameof(SettingsStopButtonForeground));
        OnPropertyChanged(nameof(ToggleButtonText));
        OnPropertyChanged(nameof(SettingsRowBackground));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class ApplicationEntryRegistry
{
    private static readonly Dictionary<string, ApplicationEntryRow> Rows = new(StringComparer.OrdinalIgnoreCase);

    public static ApplicationEntryRow GetOrCreate(string name, string scriptPath, string? effectiveScriptPath, int startupDelayMs, FolderItemModel? ownerItem)
    {
        var key = BuildKey(ownerItem, scriptPath, effectiveScriptPath);
        if (!Rows.TryGetValue(key, out var row))
        {
            row = new ApplicationEntryRow(name, scriptPath, effectiveScriptPath, startupDelayMs, ownerItem);
            Rows[key] = row;
            return row;
        }

        row.UpdateDefinition(name, scriptPath, effectiveScriptPath, startupDelayMs, ownerItem);
        return row;
    }

    public static void Remove(FolderItemModel? ownerItem, string scriptPath, string? effectiveScriptPath)
    {
        var key = BuildKey(ownerItem, scriptPath, effectiveScriptPath);
        Rows.Remove(key);
    }

    public static IReadOnlyList<ApplicationEntryRow> GetAll()
        => Rows.Values.ToArray();

    public static bool TryGetByInteractionTargetPath(string? interactionTargetPath, out ApplicationEntryRow? row)
    {
        row = Rows.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.InteractionTargetPath, interactionTargetPath, StringComparison.OrdinalIgnoreCase));

        return row is not null;
    }

    private static string BuildKey(FolderItemModel? ownerItem, string scriptPath, string? effectiveScriptPath)
    {
        var ownerKey = ownerItem?.Id;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            ownerKey = ownerItem?.Path;
        }

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            ownerKey = effectiveScriptPath;
        }

        return $"{ownerKey ?? string.Empty}|{scriptPath?.Trim() ?? string.Empty}";
    }
}

