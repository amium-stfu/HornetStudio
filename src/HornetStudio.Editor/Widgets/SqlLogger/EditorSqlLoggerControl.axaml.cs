using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HornetStudio.Host;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EditorSqlLoggerControl : EditorTemplateWidget
{
    private const string RecordItemName = "Record";
    private const string OutputPathItemName = "OutputPath";
    private const string IsRecordingItemName = "IsRecording";
    private const string LastFileItemName = "LastFile";
    private const string StatusItemName = "Status";

    private FolderItemModel? _item;
    private FolderItemModel? ItemModel => _item;
    private bool _isRecording;
    private string _registryPath = string.Empty;
    private string _recordRuntimePath = string.Empty;
    private string _outputPathRuntimePath = string.Empty;
    private bool _isUpdatingRuntimeSignals;
    private string _runtimeOutputPath = string.Empty;
    private string _pendingExternalOutputPath = string.Empty;
    private string _lastFilePath = string.Empty;
    private string _statusText = "Ready";
    private INotifyPropertyChanged? _itemPropertySource;
    private readonly Dictionary<string, string> _publishedRuntimeValues = new(StringComparer.OrdinalIgnoreCase);

    private bool IsSupportedItem => ItemModel?.IsSqlLoggerControl == true;

    public EditorSqlLoggerControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _item = DataContext as FolderItemModel;
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
        HookItemProperties();
        UpdateRuntimeSnapshot();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        UnhookItemProperties();
        RemovePublishedRuntimeItems();
        _item = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _item = DataContext as FolderItemModel;
        HookItemProperties();
        UpdateRuntimeSnapshot();
    }

    private void HookItemProperties()
    {
        UnhookItemProperties();
        if (!IsSupportedItem)
        {
            return;
        }

        _itemPropertySource = ItemModel;
        if (_itemPropertySource is not null)
        {
            _itemPropertySource.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void UnhookItemProperties()
    {
        if (_itemPropertySource is null)
        {
            return;
        }

        _itemPropertySource.PropertyChanged -= OnItemPropertyChanged;
        _itemPropertySource = null;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.Path), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.Name), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.FolderName), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.CsvDirectory), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(FolderItemModel.CsvFilename), StringComparison.Ordinal))
        {
            if (ItemModel is not null && string.IsNullOrWhiteSpace(_pendingExternalOutputPath))
            {
                _runtimeOutputPath = ItemModel.GetLoggerConfiguredOutputPath();
            }

            UpdateRuntimeSnapshot();
        }
    }

    private void UpdateRuntimeSnapshot()
    {
        var previousRegistryPath = _registryPath;

        if (!IsSupportedItem)
        {
            _registryPath = string.Empty;
            _recordRuntimePath = string.Empty;
            _outputPathRuntimePath = string.Empty;
            RemovePublishedRuntimeItems();
            return;
        }

        _registryPath = ItemModel?.GetLoggerRuntimeBasePath() ?? string.Empty;
        _recordRuntimePath = ItemModel?.GetLoggerRuntimePath(RecordItemName) ?? string.Empty;
        _outputPathRuntimePath = ItemModel?.GetLoggerRuntimePath(OutputPathItemName) ?? string.Empty;

        if (!string.Equals(previousRegistryPath, _registryPath, StringComparison.OrdinalIgnoreCase))
        {
            RemovePublishedRuntimeItems();
        }

        if (ItemModel is not null && string.IsNullOrWhiteSpace(_runtimeOutputPath))
        {
            _runtimeOutputPath = ItemModel.GetLoggerConfiguredOutputPath();
        }

        EnsureRuntimeSignals();
    }

    private void EnsureRuntimeSignals()
    {
        if (!IsSupportedItem || _isUpdatingRuntimeSignals || ItemModel is null || string.IsNullOrWhiteSpace(_registryPath))
        {
            return;
        }

        _isUpdatingRuntimeSignals = true;
        try
        {
            PublishRuntimeValue(RecordItemName, _isRecording, "Sql logger record trigger");
            PublishRuntimeValue(OutputPathItemName, string.IsNullOrWhiteSpace(_runtimeOutputPath) ? ItemModel.GetLoggerConfiguredOutputPath() : _runtimeOutputPath, "Sql logger output path");
            PublishRuntimeValue(IsRecordingItemName, _isRecording, "Sql logger recording state");
            PublishRuntimeValue(LastFileItemName, _lastFilePath, "Sql logger last file");
            PublishRuntimeValue(StatusItemName, _statusText, "Sql logger status");
        }
        finally
        {
            _isUpdatingRuntimeSignals = false;
        }
    }

    private void PublishRuntimeValue(string itemName, object? value, string title)
    {
        var path = ItemModel?.GetLoggerRuntimePath(itemName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var serializedValue = value?.ToString() ?? string.Empty;
        if (_publishedRuntimeValues.TryGetValue(path, out var previousValue)
            && string.Equals(previousValue, serializedValue, StringComparison.Ordinal))
        {
            return;
        }

        _publishedRuntimeValues[path] = serializedValue;

        var snapshot = new ItemModel(itemName, value, _registryPath);
        snapshot.Properties["kind"].Value = "LoggerRuntime";
        snapshot.Properties["text"].Value = title;
        snapshot.Properties["title"].Value = title;
        HostRegistries.Data.UpsertSnapshot(snapshot.Path!, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
    }

    private void RemovePublishedRuntimeItems()
    {
        foreach (var path in _publishedRuntimeValues.Keys)
        {
            HostRegistries.Data.Remove(path);
        }

        _publishedRuntimeValues.Clear();
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (!IsSupportedItem || _isUpdatingRuntimeSignals || ItemModel is null)
        {
            return;
        }

        if (!(ItemModel.LoggerRuntimeBindingMatchesRegistryChange(_recordRuntimePath, e)
              || ItemModel.LoggerRuntimeBindingMatchesRegistryChange(_outputPathRuntimePath, e)))
        {
            return;
        }

        Dispatcher.UIThread.Post(ApplyExternalRuntimeState);
    }

    private void ApplyExternalRuntimeState()
    {
        if (!IsSupportedItem || ItemModel is null)
        {
            return;
        }

        var runtimeOutputPath = ReadRuntimeString(_outputPathRuntimePath);
        if (!string.IsNullOrWhiteSpace(runtimeOutputPath)
            && !string.Equals(runtimeOutputPath, _runtimeOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            if (_isRecording)
            {
                _pendingExternalOutputPath = runtimeOutputPath;
                _runtimeOutputPath = runtimeOutputPath;
                _statusText = "Pending output path";
            }
            else if (ItemModel.TryApplyLoggerOutputPath(runtimeOutputPath))
            {
                _pendingExternalOutputPath = string.Empty;
                _runtimeOutputPath = ItemModel.GetLoggerConfiguredOutputPath();
                _statusText = "Ready";
            }
            else
            {
                _runtimeOutputPath = runtimeOutputPath;
                _statusText = "Invalid output path";
            }
        }

        var desiredRecord = ReadRuntimeBoolean(_recordRuntimePath, _isRecording);
        if (desiredRecord != _isRecording)
        {
            if (desiredRecord)
            {
                StartRecordingFromRuntime();
            }
            else
            {
                StopRecordingFromRuntime();
            }
        }

        EnsureRuntimeSignals();
        UpdateRecordingVisualState(_isRecording);
    }

    private void StartRecordingFromRuntime()
    {
        if (ItemModel is null || TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel })
        {
            return;
        }

        if (!TryApplyPendingOutputPath())
        {
            EnsureRuntimeSignals();
            return;
        }

        viewModel.StartSqlLogging(ItemModel);
        _isRecording = true;
        _lastFilePath = ItemModel.GetLoggerConfiguredOutputPath();
        _statusText = "Recording";
    }

    private void StopRecordingFromRuntime()
    {
        if (ItemModel is null || TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel })
        {
            return;
        }

        viewModel.StopSqlLogging(ItemModel);
        _isRecording = false;
        _statusText = "Stopped";

        if (TryApplyPendingOutputPath())
        {
            _statusText = "Ready";
        }
    }

    private bool TryApplyPendingOutputPath()
    {
        if (ItemModel is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_pendingExternalOutputPath))
        {
            _runtimeOutputPath = ItemModel.GetLoggerConfiguredOutputPath();
            return true;
        }

        if (!ItemModel.TryApplyLoggerOutputPath(_pendingExternalOutputPath))
        {
            _statusText = "Invalid output path";
            return false;
        }

        _pendingExternalOutputPath = string.Empty;
        _runtimeOutputPath = ItemModel.GetLoggerConfiguredOutputPath();
        return true;
    }

    private static bool ReadRuntimeBoolean(string path, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(path) || !HostRegistries.Data.TryResolve(path, out var item) || item is null)
        {
            return fallback;
        }

        return item.Value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when int.TryParse(text, out var parsedInt) => parsedInt != 0,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            _ => fallback
        };
    }

    private static string ReadRuntimeString(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !HostRegistries.Data.TryResolve(path, out var item) || item is null)
        {
            return string.Empty;
        }

        return item.Value?.ToString() ?? string.Empty;
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HandleInteractivePointerPressed(e);
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        HandleSettingsClicked(e);
    }

    private void OnToggleRecordingClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel viewModel })
        {
            return;
        }

        if (_isRecording)
        {
            viewModel.StopSqlLogging(ItemModel);
            _isRecording = false;
            _statusText = "Stopped";
            if (TryApplyPendingOutputPath())
            {
                _statusText = "Ready";
            }
        }
        else
        {
            if (!TryApplyPendingOutputPath())
            {
                EnsureRuntimeSignals();
                return;
            }

            viewModel.StartSqlLogging(ItemModel);
            _isRecording = true;
            _lastFilePath = ItemModel.GetLoggerConfiguredOutputPath();
            _statusText = "Recording";
        }

        UpdateRecordingVisualState(_isRecording);
        EnsureRuntimeSignals();
    }

    private void UpdateRecordingVisualState(bool isRecording)
    {
        if (this.FindControl<TextBlock>("HeaderStateText") is { } headerStateText
            && this.FindControl<Border>("HeaderStateBorder") is { } headerBorder)
        {
            headerStateText.Text = isRecording ? "Stop" : "Start";
            headerBorder.Background = isRecording
                ? new SolidColorBrush(Color.Parse("#DC2626")) // red
                : new SolidColorBrush(Color.Parse("#111827")); // default dark
        }
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (ItemModel is null)
        {
            return;
        }

        var directory = string.IsNullOrWhiteSpace(ItemModel.CsvDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HornetStudioLogs")
            : ItemModel.CsvDirectory.Trim();

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // ignored
        }

        e.Handled = true;
    }
}

