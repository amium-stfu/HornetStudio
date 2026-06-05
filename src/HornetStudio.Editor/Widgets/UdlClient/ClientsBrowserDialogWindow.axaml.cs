using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amium.Items;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.UdlClients;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Host;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Provides a folder-local browser shell for file-backed client definitions and runtime state.
/// </summary>
public sealed partial class ClientsBrowserDialogWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly UdlClientDefinitionFileCodec _codec = new();
    private MainWindowViewModel? _subscribedViewModel;
    private FolderModel _folder;
    private ClientBrowserEntry? _selectedClient;
    private string _dialogBackground = "#FFFFFF";
    private string _panelBackground = "#F8FAFC";
    private string _borderColor = "#CBD5E1";
    private string _primaryTextBrush = "#111827";
    private string _secondaryTextBrush = "#5E6777";
    private string _sectionBackground = "#EEF3F8";
    private string _editorBackground = "#FFFFFF";
    private string _editorForeground = "#111827";
    private string _buttonBackground = "#F8FAFC";
    private string _buttonBorderBrush = "#CBD5E1";
    private string _clientsSummaryText = "0 clients";
    private string _selectedClientDisplayName = string.Empty;
    private string _selectedClientId = string.Empty;
    private string _selectedClientTypeText = string.Empty;
    private string _selectedClientSocketText = string.Empty;
    private string _selectedClientModeAndSocketText = string.Empty;
    private string _selectedClientConnectionText = "Disconnected";
    private string _selectedClientStatusBadgeText = "[Disconnected]";
    private string _editableClientId = string.Empty;
    private string _editableText = string.Empty;
    private string _editableHost = string.Empty;
    private decimal _editablePort = UdlClientDefinitionDefaults.Port;
    private bool _editableEnabled;
    private bool _editableAutoConnect;
    private bool _editableDebugLogging;
    private bool _editableDemoEnabled;
    private string _diagnosticsLine1 = "Connection state: Disconnected";
    private string _diagnosticsLine2 = "Counters: messages 0 | rx 0 | tx 0";
    private string _diagnosticsLine3 = "Last diagnostic: -";
    private string _pathsLine1 = "Definition file: -";
    private string _pathsLine2 = "Status path: -";
    private string _pathsLine3 = "Runtime root: -";

    public ClientsBrowserDialogWindow()
        : this(null, new FolderModel())
    {
    }

    public ClientsBrowserDialogWindow(MainWindowViewModel? viewModel, FolderModel folder)
    {
        _viewModel = viewModel;
        _folder = folder;
        Clients = [];
        ReceivedItems = [];
        AttachedItems = [];
        InitializeComponent();
        DataContext = this;
        AttachToViewModel(viewModel);
        ApplyFolderContext(folder, refreshClients: true);
        ReceivedItems.CollectionChanged += (_, _) => RaiseSelectionStateChanged();
        AttachedItems.CollectionChanged += (_, _) => RaiseSelectionStateChanged();
        Closed += OnClosed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ClientBrowserEntry> Clients { get; }

    public ObservableCollection<ClientDetailRow> ReceivedItems { get; }

    public ObservableCollection<ClientDetailRow> AttachedItems { get; }

    public string DialogBackground
    {
        get => _dialogBackground;
        private set => SetAndRaise(ref _dialogBackground, value);
    }

    public string PanelBackground
    {
        get => _panelBackground;
        private set => SetAndRaise(ref _panelBackground, value);
    }

    public string BorderColor
    {
        get => _borderColor;
        private set => SetAndRaise(ref _borderColor, value);
    }

    public string PrimaryTextBrush
    {
        get => _primaryTextBrush;
        private set => SetAndRaise(ref _primaryTextBrush, value);
    }

    public string SecondaryTextBrush
    {
        get => _secondaryTextBrush;
        private set => SetAndRaise(ref _secondaryTextBrush, value);
    }

    public string SectionBackground
    {
        get => _sectionBackground;
        private set => SetAndRaise(ref _sectionBackground, value);
    }

    public string EditorBackground
    {
        get => _editorBackground;
        private set => SetAndRaise(ref _editorBackground, value);
    }

    public string EditorForeground
    {
        get => _editorForeground;
        private set => SetAndRaise(ref _editorForeground, value);
    }

    public string ButtonBackground
    {
        get => _buttonBackground;
        private set => SetAndRaise(ref _buttonBackground, value);
    }

    public string ButtonBorderBrush
    {
        get => _buttonBorderBrush;
        private set => SetAndRaise(ref _buttonBorderBrush, value);
    }

    public string ClientsSummaryText
    {
        get => _clientsSummaryText;
        private set => SetAndRaise(ref _clientsSummaryText, value);
    }

    public ClientBrowserEntry? SelectedClient
    {
        get => _selectedClient;
        set
        {
            if (!SetAndRaise(ref _selectedClient, value))
            {
                return;
            }

            ApplySelectedClient(value);
        }
    }

    public bool HasSelectedClient => SelectedClient is not null;

    public bool IsSelectionEmpty => SelectedClient is null;

    public string SelectedClientDisplayName
    {
        get => _selectedClientDisplayName;
        private set => SetAndRaise(ref _selectedClientDisplayName, value);
    }

    public string SelectedClientId
    {
        get => _selectedClientId;
        private set => SetAndRaise(ref _selectedClientId, value);
    }

    public string SelectedClientTypeText
    {
        get => _selectedClientTypeText;
        private set => SetAndRaise(ref _selectedClientTypeText, value);
    }

    public string SelectedClientSocketText
    {
        get => _selectedClientSocketText;
        private set => SetAndRaise(ref _selectedClientSocketText, value);
    }

    public string SelectedClientModeAndSocketText
    {
        get => _selectedClientModeAndSocketText;
        private set => SetAndRaise(ref _selectedClientModeAndSocketText, value);
    }

    public string SelectedClientConnectionText
    {
        get => _selectedClientConnectionText;
        private set => SetAndRaise(ref _selectedClientConnectionText, value);
    }

    public string SelectedClientStatusBadgeText
    {
        get => _selectedClientStatusBadgeText;
        private set => SetAndRaise(ref _selectedClientStatusBadgeText, value);
    }

    public string EditableClientId
    {
        get => _editableClientId;
        private set => SetAndRaise(ref _editableClientId, value);
    }

    public string EditableText
    {
        get => _editableText;
        set => SetAndRaise(ref _editableText, value);
    }

    public string EditableHost
    {
        get => _editableHost;
        set => SetAndRaise(ref _editableHost, value);
    }

    public decimal EditablePort
    {
        get => _editablePort;
        set => SetAndRaise(ref _editablePort, value);
    }

    public bool EditableEnabled
    {
        get => _editableEnabled;
        set => SetAndRaise(ref _editableEnabled, value);
    }

    public bool EditableAutoConnect
    {
        get => _editableAutoConnect;
        set => SetAndRaise(ref _editableAutoConnect, value);
    }

    public bool EditableDebugLogging
    {
        get => _editableDebugLogging;
        set => SetAndRaise(ref _editableDebugLogging, value);
    }

    public bool EditableDemoEnabled
    {
        get => _editableDemoEnabled;
        set => SetAndRaise(ref _editableDemoEnabled, value);
    }

    public bool CanConnectSelectedClient => SelectedClient is not null && !SelectedClient.IsConnected;

    public bool CanDisconnectSelectedClient => SelectedClient?.IsConnected == true;

    public bool CanToggleAllReceivedItems => ReceivedItems.Count > 0;

    public bool AreAllReceivedItemsAttached => ReceivedItems.Count > 0 && ReceivedItems.All(static row => row.IsAttached);

    public bool CanAddDemo => SelectedClient?.Definition.DemoEnabled == true;

    public string DiagnosticsLine1
    {
        get => _diagnosticsLine1;
        private set => SetAndRaise(ref _diagnosticsLine1, value);
    }

    public string DiagnosticsLine2
    {
        get => _diagnosticsLine2;
        private set => SetAndRaise(ref _diagnosticsLine2, value);
    }

    public string DiagnosticsLine3
    {
        get => _diagnosticsLine3;
        private set => SetAndRaise(ref _diagnosticsLine3, value);
    }

    public string PathsLine1
    {
        get => _pathsLine1;
        private set => SetAndRaise(ref _pathsLine1, value);
    }

    public string PathsLine2
    {
        get => _pathsLine2;
        private set => SetAndRaise(ref _pathsLine2, value);
    }

    public string PathsLine3
    {
        get => _pathsLine3;
        private set => SetAndRaise(ref _pathsLine3, value);
    }

    public static ClientsBrowserDialogWindow ShowOrActivate(Window? owner, MainWindowViewModel? viewModel, FolderModel folder)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserDialogOpen(owner, nameof(ClientsBrowserDialogWindow), folder.Name);
        var dialog = new ClientsBrowserDialogWindow(viewModel, folder)
        {
            Owner = owner
        };

        if (owner is null)
        {
            dialog.Show();
        }
        else
        {
            dialog.Show(owner);
        }

        return dialog;
    }

    public void UpdateFolderContext(FolderModel folder)
    {
        if (folder is null)
        {
            return;
        }

        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackBrowserOperation(this, nameof(ClientsBrowserDialogWindow), nameof(UpdateFolderContext));
        _folder = folder;
        ApplyFolderContext(folder, refreshClients: true);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AttachToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            RefreshThemeBindings();
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RefreshThemeBindings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.SelectedFolder)
            || e.PropertyName == nameof(MainWindowViewModel.DialogBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBackground)
            || e.PropertyName == nameof(MainWindowViewModel.CardBorderBrush)
            || e.PropertyName == nameof(MainWindowViewModel.PrimaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.SecondaryTextBrush)
            || e.PropertyName == nameof(MainWindowViewModel.EditorDialogSectionContentBackground)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterEditBackgrundColor)
            || e.PropertyName == nameof(MainWindowViewModel.ParameterEditForeColor)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBackground)
            || e.PropertyName == nameof(MainWindowViewModel.EditPanelButtonBorderBrush))
        {
            RefreshThemeBindings();

            if (string.IsNullOrWhiteSpace(e.PropertyName)
                || e.PropertyName == nameof(MainWindowViewModel.SelectedFolder))
            {
                UpdateFolderContext(_viewModel?.SelectedFolder ?? _folder);
            }
        }
    }

    private void RefreshThemeBindings()
    {
        DialogBackground = _viewModel?.DialogBackground ?? "#FFFFFF";
        PanelBackground = _viewModel?.CardBackground ?? "#F8FAFC";
        BorderColor = _viewModel?.CardBorderBrush ?? "#CBD5E1";
        PrimaryTextBrush = _viewModel?.PrimaryTextBrush ?? "#111827";
        SecondaryTextBrush = _viewModel?.SecondaryTextBrush ?? "#5E6777";
        SectionBackground = _viewModel?.EditorDialogSectionContentBackground ?? PanelBackground;
        EditorBackground = _viewModel?.ParameterEditBackgrundColor ?? "#FFFFFF";
        EditorForeground = _viewModel?.ParameterEditForeColor ?? PrimaryTextBrush;
        ButtonBackground = _viewModel?.EditPanelButtonBackground ?? "#F8FAFC";
        ButtonBorderBrush = _viewModel?.EditPanelButtonBorderBrush ?? BorderColor;
    }

    private void ApplyFolderContext(FolderModel folder, bool refreshClients)
    {
        Title = $"Clients Browser - {folder.Name}";
        if (refreshClients)
        {
            ReloadClients();
        }
    }

    private void ReloadClients()
    {
        Clients.Clear();

        var folderDirectory = TryGetFolderDirectory(_folder);
        if (string.IsNullOrWhiteSpace(folderDirectory) || !Directory.Exists(folderDirectory))
        {
            ClientsSummaryText = "0 clients";
            SelectedClient = null;
            return;
        }

        foreach (var entry in _codec.LoadFolder(folderDirectory))
        {
            var isConnected = UdlClientRuntimeManager.TryGetRuntime(_folder.Name, entry.Definition.ClientId, out _);
            Clients.Add(ClientBrowserEntry.FromUdl(entry, isConnected));
        }

        ClientsSummaryText = Clients.Count == 1 ? "1 client" : $"{Clients.Count} clients";

        if (Clients.Count == 0)
        {
            SelectedClient = null;
            return;
        }

        var previouslySelectedId = SelectedClient?.ClientId;
        SelectedClient = Clients.FirstOrDefault(candidate => string.Equals(candidate.ClientId, previouslySelectedId, StringComparison.OrdinalIgnoreCase))
            ?? Clients[0];
    }

    private void ApplySelectedClient(ClientBrowserEntry? row)
    {
        RaiseSelectionStateChanged();

        if (row is null)
        {
            SelectedClientDisplayName = string.Empty;
            SelectedClientId = string.Empty;
            SelectedClientTypeText = string.Empty;
            SelectedClientSocketText = string.Empty;
            SelectedClientModeAndSocketText = string.Empty;
            SelectedClientConnectionText = "Disconnected";
            SelectedClientStatusBadgeText = "[Disconnected]";
            EditableClientId = string.Empty;
            EditableText = string.Empty;
            EditableHost = string.Empty;
            EditablePort = UdlClientDefinitionDefaults.Port;
            EditableEnabled = false;
            EditableAutoConnect = false;
            EditableDebugLogging = false;
            EditableDemoEnabled = false;
            ReceivedItems.Clear();
            AttachedItems.Clear();
            DiagnosticsLine1 = "Connection state: Disconnected";
            DiagnosticsLine2 = "Counters: messages 0 | rx 0 | tx 0";
            DiagnosticsLine3 = "Last diagnostic: -";
            PathsLine1 = "Definition file: -";
            PathsLine2 = "Status path: -";
            PathsLine3 = "Runtime root: -";
            return;
        }

        SelectedClientDisplayName = row.Text;
        SelectedClientId = row.ClientId;
        SelectedClientTypeText = row.ClientType;
        SelectedClientSocketText = row.SocketText;
        SelectedClientModeAndSocketText = $"{row.ClientType} | {row.SocketText}";
        EditableClientId = row.ClientId;
        EditableText = row.Definition.Text;
        EditableHost = row.Definition.Host;
        EditablePort = row.Definition.Port;
        EditableEnabled = row.Definition.Enabled;
        EditableAutoConnect = row.Definition.AutoConnect;
        EditableDebugLogging = row.Definition.DebugLogging;
        EditableDemoEnabled = row.Definition.DemoEnabled;

        var liveRuntimePaths = row.GetRuntimeRelativePaths(_folder.Name);
        var receivedRootPaths = row.GetReceivedRootPaths(_folder.Name);
        var attachedPaths = ParseAttachedPaths(row.Definition.AttachedItemPaths);
        ReceivedItems.ResetFrom(receivedRootPaths.Select(path => ClientDetailRow.CreateReceived(path, attachedPaths.Contains(path))));
        AttachedItems.ResetFrom(attachedPaths.Select(path => ClientDetailRow.CreateAttached(path, liveRuntimePaths.Contains(path))));

        var runtimeSnapshot = row.GetDiagnosticsSnapshot(_folder.Name);
        var connectionText = runtimeSnapshot?.IsConnected == true ? "Connected" : "Disconnected";
        SelectedClientConnectionText = connectionText;
        SelectedClientStatusBadgeText = $"[{connectionText}]";
        DiagnosticsLine1 = $"Connection state: {connectionText} | Endpoint: {row.Definition.Host}:{row.Definition.Port}";
        DiagnosticsLine2 = runtimeSnapshot is null
            ? $"Counts: roots {ReceivedItems.Count} | attached {AttachedItems.Count} | type {row.ClientType}"
            : $"Counters: messages {runtimeSnapshot.MessageCount} | rx {runtimeSnapshot.RxCount} | tx {runtimeSnapshot.TxCount} | local port {runtimeSnapshot.LocalPort}";
        DiagnosticsLine3 = $"Last diagnostic: {(string.IsNullOrWhiteSpace(runtimeSnapshot?.LastDiagnostic) ? "-" : runtimeSnapshot.LastDiagnostic)}";
        PathsLine1 = $"Definition file: {row.DefinitionFilePath}";
        PathsLine2 = $"Status path: {UdlPathHelper.GetCanonicalStatusBasePath(_folder.Name, row.ClientId)}";
        PathsLine3 = $"Runtime root: {UdlPathHelper.GetCanonicalRuntimeBasePath(row.ClientId)}";
        RaiseSelectionStateChanged();
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        ReloadClients();
        e.Handled = true;
    }

    private void OnReloadSelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is not null)
        {
            ApplySelectedClient(SelectedClient);
        }

        e.Handled = true;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null)
        {
            return;
        }

        try
        {
            var updatedDefinition = BuildEditedDefinition(SelectedClient.Definition);
            var changed = SaveSelectedDefinition(updatedDefinition, SelectedClient.DefinitionFilePath);
            DiagnosticsLine3 = changed
                ? "Last diagnostic: Definition saved and runtime synchronization triggered."
                : "Last diagnostic: Definition saved with no effective changes.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Save failed - {ex.Message}";
        }

        e.Handled = true;
    }

    private async void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null)
        {
            return;
        }

        await ExecuteConnectAsync(SelectedClient).ConfigureAwait(true);
        e.Handled = true;
    }

    private async void OnDisconnectClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null)
        {
            return;
        }

        await ExecuteDisconnectAsync(SelectedClient).ConfigureAwait(true);
        e.Handled = true;
    }

    private async Task ExecuteConnectAsync(ClientBrowserEntry row)
    {
        try
        {
            await UdlClientRuntimeManager.ConnectAsync(_folder.Name, row.Definition, CancellationToken.None).ConfigureAwait(true);
            ReloadClients();
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Connect failed - {ex.Message}";
        }
    }

    private async Task ExecuteDisconnectAsync(ClientBrowserEntry row)
    {
        try
        {
            await UdlClientRuntimeManager.DisconnectAsync(_folder.Name, row.ClientId, CancellationToken.None).ConfigureAwait(true);
            ReloadClients();
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Disconnect failed - {ex.Message}";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        Closed -= OnClosed;
    }

    private static string TryGetFolderDirectory(FolderModel folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.UiFilePath))
        {
            return Path.GetDirectoryName(folder.UiFilePath) ?? string.Empty;
        }

        return string.Empty;
    }

    private UdlClientDefinition BuildEditedDefinition(UdlClientDefinition source)
    {
        return source with
        {
            Text = EditableText?.Trim() ?? string.Empty,
            Host = string.IsNullOrWhiteSpace(EditableHost) ? UdlClientDefinitionDefaults.Host : EditableHost.Trim(),
            Port = decimal.ToInt32(decimal.Clamp(EditablePort, 1, 65535)),
            Enabled = EditableEnabled,
            AutoConnect = EditableAutoConnect,
            DebugLogging = EditableDebugLogging,
            DemoEnabled = EditableDemoEnabled
        };
    }

    private bool SaveSelectedDefinition(UdlClientDefinition updatedDefinition, string existingFilePath)
    {
        var folderDirectory = TryGetFolderDirectory(_folder);
        if (string.IsNullOrWhiteSpace(folderDirectory))
        {
            throw new InvalidOperationException("Could not resolve folder directory.");
        }

        var changed = SelectedClient is null || updatedDefinition != SelectedClient.Definition;
        _codec.SaveDefinition(folderDirectory, updatedDefinition, existingFilePath);

        var definitions = _codec.LoadFolder(folderDirectory)
            .Select(static entry => entry.Definition)
            .ToArray();
        UdlClientRuntimeManager.SyncDefinitions(_folder.Name, definitions, forceRecreate: changed);

        ReloadClients();
        SelectedClient = Clients.FirstOrDefault(candidate => string.Equals(candidate.ClientId, updatedDefinition.ClientId, StringComparison.OrdinalIgnoreCase));
        return changed;
    }

    private void RaiseSelectionStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedClient));
        RaisePropertyChanged(nameof(IsSelectionEmpty));
        RaisePropertyChanged(nameof(CanConnectSelectedClient));
        RaisePropertyChanged(nameof(CanDisconnectSelectedClient));
        RaisePropertyChanged(nameof(CanToggleAllReceivedItems));
        RaisePropertyChanged(nameof(AreAllReceivedItemsAttached));
        RaisePropertyChanged(nameof(CanAddDemo));
    }

    private void OnAttachReceivedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null
            || sender is not Button { CommandParameter: ClientDetailRow row }
            || !row.CanExecuteAction)
        {
            return;
        }

        var updatedDefinition = SelectedClient.Definition with
        {
            AttachedItemPaths = AddAttachedPaths(SelectedClient.Definition.AttachedItemPaths, [row.RelativePath])
        };

        try
        {
            SaveSelectedDefinition(updatedDefinition, SelectedClient.DefinitionFilePath);
            DiagnosticsLine3 = $"Last diagnostic: Attached '{row.RelativePath}'.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Attach failed - {ex.Message}";
        }

        e.Handled = true;
    }

    private void OnDetachAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null
            || sender is not Button { CommandParameter: ClientDetailRow row }
            || !row.CanDetach)
        {
            return;
        }

        var updatedDefinition = SelectedClient.Definition with
        {
            AttachedItemPaths = RemoveAttachedPaths(SelectedClient.Definition.AttachedItemPaths, [row.RelativePath])
        };

        try
        {
            SaveSelectedDefinition(updatedDefinition, SelectedClient.DefinitionFilePath);
            DiagnosticsLine3 = $"Last diagnostic: Detached '{row.RelativePath}'.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Detach failed - {ex.Message}";
        }

        e.Handled = true;
    }

    private void OnToggleAllReceivedItemsClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null || sender is not ToggleButton toggleButton)
        {
            return;
        }

        var receivedPaths = ReceivedItems
            .Select(static row => row.RelativePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (receivedPaths.Length == 0)
        {
            return;
        }

        var updatedDefinition = SelectedClient.Definition with
        {
            AttachedItemPaths = toggleButton.IsChecked == true
                ? AddAttachedPaths(SelectedClient.Definition.AttachedItemPaths, receivedPaths)
                : RemoveAttachedPaths(SelectedClient.Definition.AttachedItemPaths, receivedPaths)
        };

        try
        {
            SaveSelectedDefinition(updatedDefinition, SelectedClient.DefinitionFilePath);
            DiagnosticsLine3 = toggleButton.IsChecked == true
                ? "Last diagnostic: Attached all received roots."
                : "Last diagnostic: Detached all received roots.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Bulk attach update failed - {ex.Message}";
        }

        e.Handled = true;
    }

    private async void OnAddDemoClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null
            || SelectedClient.Definition.DemoEnabled != true)
        {
            return;
        }

        var owner = Owner as Window ?? this;
        var viewModel = _viewModel;
        var ownerItem = FindMatchingUdlClientItem(SelectedClient.ClientId) ?? CreateDialogOwnerItem(SelectedClient);

        var result = await UdlDemoModulesDialogWindow.ShowAsync(
            owner: owner,
            viewModel: viewModel,
            ownerItem: ownerItem,
            rawDefinitions: SelectedClient.Definition.DemoModuleDefinitions);
        if (result is null)
        {
            return;
        }

        var updatedDefinition = SelectedClient.Definition with
        {
            DemoModuleDefinitions = result
        };

        try
        {
            SaveSelectedDefinition(updatedDefinition, SelectedClient.DefinitionFilePath);
            DiagnosticsLine3 = "Last diagnostic: Demo modules updated.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine3 = $"Last diagnostic: Demo update failed - {ex.Message}";
        }

        e.Handled = true;
    }

    private async void OnEditAttachedItemClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedClient is null
            || sender is not Button { CommandParameter: ClientDetailRow row }
            || string.IsNullOrWhiteSpace(row.ModuleName))
        {
            return;
        }

        var result = await UdlModuleExposureDialogWindow.ShowAsync(
            owner: Owner as Window ?? this,
            viewModel: _viewModel ?? throw new InvalidOperationException("Main window view model is not available."),
            rawDefinitions: SelectedClient.Definition.UdlModuleExposureDefinitions,
            runtimeChannels: ResolveRuntimeChannels(SelectedClient.ClientId),
            moduleName: row.ModuleName);
        if (result is null)
        {
            e.Handled = true;
            return;
        }

        var updatedDefinition = SelectedClient.Definition with
        {
            UdlModuleExposureDefinitions = result
        };

        SaveSelectedDefinition(updatedDefinition, SelectedClient.DefinitionFilePath);
        DiagnosticsLine3 = $"Last diagnostic: Exposure settings updated for module '{row.ModuleName}'.";
        e.Handled = true;
    }

    private FolderItemModel CreateDialogOwnerItem(ClientBrowserEntry client)
    {
        return new FolderItemModel
        {
            Kind = ControlKind.UdlClientControl,
            Name = client.ClientId,
            UdlClientHost = client.Definition.Host,
            UdlClientPort = client.Definition.Port,
            UdlClientAutoConnect = client.Definition.AutoConnect,
            UdlClientDebugLogging = client.Definition.DebugLogging,
            UdlClientDemoEnabled = client.Definition.DemoEnabled,
            UdlAttachedItemPaths = string.Join(Environment.NewLine, client.Definition.AttachedItemPaths ?? []),
            UdlModuleExposureDefinitions = client.Definition.UdlModuleExposureDefinitions,
            UdlDemoModuleDefinitions = client.Definition.DemoModuleDefinitions
        };
    }

    private FolderItemModel? FindMatchingUdlClientItem(string clientId)
    {
        return EnumerateFolderItems(_folder.Items)
            .FirstOrDefault(item => item.Kind == ControlKind.UdlClientControl
                && string.Equals(UdlPathHelper.NormalizeClientName(item.Name), clientId, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<FolderItemModel> EnumerateFolderItems(IEnumerable<FolderItemModel> items)
    {
        foreach (var item in items ?? [])
        {
            yield return item;

            foreach (var child in EnumerateFolderItems(item.Items))
            {
                yield return child;
            }
        }
    }

    private static IReadOnlyList<UdlRuntimeModuleChannelDescriptor> ResolveRuntimeChannels(string clientId)
    {
        var prefixes = UdlPathHelper.GetRuntimeBasePaths(UdlPathHelper.NormalizeClientName(clientId));

        return HostRegistries.Data.GetAllKeys()
            .Select(key => ResolveRuntimeChannelDescriptor(prefixes, key))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .GroupBy(static descriptor => $"{descriptor.ModuleName}|{descriptor.ChannelName}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static descriptor => descriptor.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static descriptor => descriptor.ChannelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UdlRuntimeModuleChannelDescriptor? ResolveRuntimeChannelDescriptor(IReadOnlyList<string> prefixes, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string? suffix = null;
        string? resolvedFullPath = null;
        foreach (var prefix in prefixes)
        {
            suffix = TryGetPathSuffix(key, prefix);
            if (string.IsNullOrWhiteSpace(suffix))
            {
                suffix = TryGetPathSuffix(TargetPathHelper.NormalizeComparablePath(key), TargetPathHelper.NormalizeComparablePath(prefix));
            }

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                resolvedFullPath = TargetPathHelper.NormalizeConfiguredTargetPath(key);
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(suffix) || string.IsNullOrWhiteSpace(resolvedFullPath))
        {
            return null;
        }

        var segments = TargetPathHelper.SplitPathSegments(suffix);
        if (segments.Count != 2)
        {
            return null;
        }

        var format = HostRegistries.Data.TryResolve(resolvedFullPath, out var runtimeItem) && runtimeItem is not null && runtimeItem.Properties.Has("format")
            ? runtimeItem.Properties["format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        var unit = HostRegistries.Data.TryResolve(resolvedFullPath, out runtimeItem) && runtimeItem is not null && runtimeItem.Properties.Has("unit")
            ? runtimeItem.Properties["unit"].Value?.ToString() ?? string.Empty
            : string.Empty;

        return new UdlRuntimeModuleChannelDescriptor
        {
            ModuleName = segments[0],
            ChannelName = segments[1],
            Format = format,
            Unit = unit,
            BitCount = GetBitCount(format)
        };
    }

    private static string? TryGetPathSuffix(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = path[prefix.Length..].TrimStart('/', '.', '\\');
        return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
    }

    private static int GetBitCount(string? format)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(format)
            ? string.Empty
            : format.Trim().Split(':', 2, StringSplitOptions.TrimEntries)[0].ToLowerInvariant();

        return normalizedKind switch
        {
            "b4" => 4,
            "b8" => 8,
            "b16" => 16,
            _ => 0
        };
    }

    private bool SetAndRaise<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static IReadOnlyList<string> ParseAttachedPaths(IEnumerable<string> paths)
    {
        return (paths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path.Count(static ch => ch == '.'))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> AddAttachedPaths(IEnumerable<string> existingPaths, IEnumerable<string> pathsToAdd)
    {
        return ParseAttachedPaths((existingPaths ?? []).Concat(pathsToAdd ?? []));
    }

    private static IReadOnlyList<string> RemoveAttachedPaths(IEnumerable<string> existingPaths, IEnumerable<string> pathsToRemove)
    {
        var removals = new HashSet<string>((pathsToRemove ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath), StringComparer.OrdinalIgnoreCase);

        return ParseAttachedPaths((existingPaths ?? []).Where(path => !removals.Contains(TargetPathHelper.NormalizeConfiguredTargetPath(path))));
    }

}

public sealed class ClientBrowserEntry
{
    private ClientBrowserEntry(UdlClientFileEntry entry, bool isConnected)
    {
        DefinitionFilePath = entry.FilePath;
        Definition = entry.Definition;
        IsConnected = isConnected;
    }

    public string DefinitionFilePath { get; }

    public string ClientId => Definition.ClientId;

    public string Text => string.IsNullOrWhiteSpace(Definition.Text) ? Definition.ClientId : Definition.Text.Trim();

    public string ClientType => Definition.DemoEnabled ? "UDL Demo" : "UDL Hardware";

    public string SocketText => $"{Definition.Host}:{Definition.Port}";

    public UdlClientDefinition Definition { get; }

    public bool IsConnected { get; }

    public string StatusText => IsConnected ? "Connected" : "Disconnected";

    public string StatusBadgeText => $"[{StatusText}]";

    public IBrush StatusBrush => IsConnected ? Brushes.ForestGreen : Brushes.Gray;

    public static ClientBrowserEntry FromUdl(UdlClientFileEntry entry, bool isConnected)
        => new(entry, isConnected);

    public string[] GetReceivedRootPaths(string folderName)
    {
        if (!UdlClientRuntimeManager.TryGetRuntime(folderName, Definition.ClientId, out var runtime) || runtime is null)
        {
            return [];
        }

        return runtime.GetReceivedRootPaths().ToArray();
    }

    public HashSet<string> GetRuntimeRelativePaths(string folderName)
    {
        if (!UdlClientRuntimeManager.TryGetRuntime(folderName, Definition.ClientId, out var runtime) || runtime is null)
        {
            return [];
        }

        return runtime.GetRuntimeItemsSnapshot()
            .Select(static item => UdlPathHelper.GetRelativeRuntimePath(item.Path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal UdlClientRuntimeDiagnosticsSnapshot? GetDiagnosticsSnapshot(string folderName)
    {
        return UdlClientRuntimeManager.TryGetRuntime(folderName, Definition.ClientId, out var runtime) && runtime is not null
            ? runtime.GetDiagnosticsSnapshot()
            : null;
    }
}

public sealed class ClientDetailRow
{
    private ClientDetailRow(string relativePath, string summaryText, string actionText, bool canExecuteAction, bool canDetach, bool isAttached, IBrush statusBrush)
    {
        RelativePath = relativePath;
        SummaryText = summaryText;
        ActionText = actionText;
        CanExecuteAction = canExecuteAction;
        CanDetach = canDetach;
        IsAttached = isAttached;
        StatusIndicatorBrush = statusBrush;
        ModuleName = TryGetModuleName(relativePath);
    }

    public string RelativePath { get; }

    public string PathText => RelativePath;

    public string ModuleName { get; }

    public string SummaryText { get; }

    public string ToolTipText => SummaryText;

    public string ActionText { get; }

    public bool CanExecuteAction { get; }

    public bool CanDetach { get; }

    public bool IsAttached { get; }

    public bool CanEdit => true;

    public IBrush StatusIndicatorBrush { get; }

    public static ClientDetailRow CreateReceived(string relativePath, bool isAttached)
    {
        return new ClientDetailRow(
            relativePath: relativePath,
            summaryText: isAttached
                ? "Runtime item is available and already attached to the browser-backed client definition."
                : "Runtime item is available and can be attached to the browser-backed client definition.",
            actionText: isAttached ? "Attached" : "Attach",
            canExecuteAction: !isAttached,
            canDetach: false,
            isAttached: isAttached,
            statusBrush: isAttached ? Brushes.ForestGreen : Brushes.Gray);
    }

    public static ClientDetailRow CreateAttached(string relativePath, bool isLive)
    {
        return new ClientDetailRow(
            relativePath: relativePath,
            summaryText: isLive
                ? "Attached item resolves to a live runtime item."
                : "Saved attachment does not currently resolve to a live runtime item.",
            actionText: "Detach",
            canExecuteAction: false,
            canDetach: true,
            isAttached: true,
            statusBrush: isLive ? Brushes.ForestGreen : Brushes.Firebrick);
    }

    private static string TryGetModuleName(string relativePath)
    {
        var segments = TargetPathHelper.SplitPathSegments(relativePath);
        return segments.Count > 0 ? segments[0] : string.Empty;
    }
}

internal static class ObservableCollectionExtensions
{
    public static void ResetFrom<T>(this ObservableCollection<T> collection, System.Collections.Generic.IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
