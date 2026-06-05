using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Widgets;
using HornetStudio.Host;
using HornetStudio.Logging;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Editor.UdlClients;

internal sealed class UdlClientRegistryProjection : IDisposable
{
    private static readonly double StopwatchTickToMilliseconds = 1000d / Stopwatch.Frequency;
    private readonly UdlClientRuntime _runtime;
    private readonly UdlHostRegistryProjection _attachmentProjection;
    private readonly Dictionary<string, string> _publishedStatusValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UdlAttachmentProjectionInput> _resolvedAttachmentCache = new(StringComparer.OrdinalIgnoreCase);
    private UdlClientDefinition _definition = null!;
    private IReadOnlyList<UdlModuleExposureDefinition> _exposureDefinitions = [];
    private bool _hasActiveBitExposures;
    private string _lastMissingAttachmentSignature = string.Empty;
    private bool _disposed;

    public UdlClientRegistryProjection(string folderName, UdlClientDefinition definition, UdlClientRuntime runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runtime);

        FolderName = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        ClientId = UdlPathHelper.NormalizeClientName(definition.ClientId);
        UpdateDefinitionState(definition);
        _runtime = runtime;
        _attachmentProjection = new UdlHostRegistryProjection(FolderName, ClientId);
        _runtime.FrameReceived += OnRuntimeFrameReceived;
        _runtime.RuntimeStructureChanged += OnRuntimeStructureChanged;
        HostRegistries.Data.ItemChanged += OnExposureTargetChanged;

        Refresh();
    }

    public string FolderName { get; }

    public string ClientId { get; }

    public void UpdateDefinition(UdlClientDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        UpdateDefinitionState(definition);
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.FrameReceived -= OnRuntimeFrameReceived;
        _runtime.RuntimeStructureChanged -= OnRuntimeStructureChanged;
        HostRegistries.Data.ItemChanged -= OnExposureTargetChanged;
        UdlClientExposureProjection.Clear(_runtime);
        _attachmentProjection.Dispose();
        ClearStatusItems();
        RemoveLegacyExposureRoot();
    }

    private void OnRuntimeStructureChanged()
    {
        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: "UdlProjectionRuntimeStructureChanged",
            path: GetOwnedClientRootPath(),
            module: ClientId);
        Refresh();
    }

    private void OnRuntimeFrameReceived(uint id, byte dlc, byte[] data)
    {
        if (_disposed)
        {
            return;
        }

        if (!_hasActiveBitExposures)
        {
            return;
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        UdlClientExposureProjection.SynchronizeBitValues(_runtime, _definition, _exposureDefinitions);

        if (!UiResponsivenessDiagnostics.IsEnabled)
        {
            return;
        }

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * StopwatchTickToMilliseconds;
        UiResponsivenessDiagnostics.RecordSignalPipelineDelay(
            stage: "UdlProjectionFrameReceived",
            delay: TimeSpan.FromMilliseconds(elapsedMilliseconds));
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        RemoveLegacyExposureRoot();
        PublishStatusItems();
        UdlClientExposureProjection.Synchronize(_runtime, _definition, _exposureDefinitions);
        SynchronizeAttachments();
    }

    private void OnExposureTargetChanged(object? sender, DataChangedEventArgs e)
    {
        if (_disposed
            || e.ItemModel is null
            || !IsRelevantExposureEvent(e))
        {
            return;
        }

        if (UdlClientExposureProjection.TryGetExposureBitMetadata(e.ItemModel, out var moduleName, out var channelName, out var bitIndex))
        {
            if (!UdlClientExposureProjection.ApplyBitWriteback(_runtime, _definition, _exposureDefinitions, moduleName, channelName, bitIndex, TryReadBool(e.ItemModel.Value, false)))
            {
                return;
            }
            return;
        }

        if (!TryResolveOwnedChannelEvent(e, out moduleName, out channelName))
        {
            return;
        }

        UdlClientExposureProjection.ApplyChannelValueUpdate(_runtime, _definition, _exposureDefinitions, moduleName, channelName, e.ParameterName, e.ItemModel);
    }

    private void PublishStatusItems()
    {
        var statusBasePath = UdlPathHelper.GetCanonicalStatusBasePath(FolderName, ClientId);
        PublishStatusValue(statusBasePath, "endpoint", $"{_definition.Host}:{_definition.Port}", "UdlClient endpoint");
        PublishStatusValue(statusBasePath, "connection", _runtime.IsConnected ? "Connected" : "Disconnected", "Connection state");
        PublishStatusValue(statusBasePath, "item_count", _runtime.GetRootItemCount(), "Discovered items");
        PublishStatusValue(statusBasePath, "auto_connect", _definition.AutoConnect, "AutoConnect");
    }

    private void SynchronizeAttachments()
    {
        var requestedAttachments = _definition.AttachedItemPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var cachedAttachmentPath in _resolvedAttachmentCache.Keys
            .Except(requestedAttachments, StringComparer.OrdinalIgnoreCase)
            .ToArray())
        {
            _resolvedAttachmentCache.Remove(cachedAttachmentPath);
        }

        var resolvedAttachments = _runtime.GetAttachmentProjectionInput(string.Join('\n', requestedAttachments));
        foreach (var resolvedAttachment in resolvedAttachments)
        {
            var normalizedRelativePath = TargetPathHelper.NormalizeConfiguredTargetPath(resolvedAttachment.RelativePath);
            if (string.IsNullOrWhiteSpace(normalizedRelativePath))
            {
                continue;
            }

            _resolvedAttachmentCache[normalizedRelativePath] = resolvedAttachment with
            {
                RelativePath = normalizedRelativePath
            };
        }

        var currentlyResolvedAttachments = resolvedAttachments
            .Select(static attachment => TargetPathHelper.NormalizeConfiguredTargetPath(attachment.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var attachments = requestedAttachments
            .Where(path => _resolvedAttachmentCache.ContainsKey(path))
            .Select(path => _resolvedAttachmentCache[path])
            .ToArray();

        var missingAttachments = requestedAttachments
            .Where(path => !currentlyResolvedAttachments.Contains(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingSignature = string.Join("|", missingAttachments);
        if (!string.Equals(_lastMissingAttachmentSignature, missingSignature, StringComparison.Ordinal))
        {
            foreach (var missingAttachment in missingAttachments)
            {
                HostLogger.Log.Warning(
                    "[UdlClientProjection] Could not attach public item Folder={FolderName} ClientId={ClientId} AttachedPath={AttachedPath}.",
                    FolderName,
                    ClientId,
                    missingAttachment);
            }

            _lastMissingAttachmentSignature = missingSignature;
        }

        var changed = _attachmentProjection.SynchronizeAttachments(attachments);
        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(
            stage: changed ? "UdlProjectionAttachmentChanged" : "UdlProjectionAttachmentStable",
            path: GetOwnedClientRootPath(),
            module: ClientId);
    }

    private void ClearStatusItems()
    {
        foreach (var path in _publishedStatusValues.Keys.ToArray())
        {
            HostRegistries.Data.Remove(path);
        }

        _publishedStatusValues.Clear();
    }

    private void PublishStatusValue(string statusBasePath, string name, object? value, string title)
    {
        var cacheKey = $"{statusBasePath}.{name}";
        var serializedValue = value?.ToString() ?? "<null>";
        if (_publishedStatusValues.TryGetValue(cacheKey, out var previousValue)
            && string.Equals(previousValue, serializedValue, StringComparison.Ordinal))
        {
            return;
        }

        _publishedStatusValues[cacheKey] = serializedValue;

        var snapshot = new ItemModel(name, value, statusBasePath);
        snapshot.Properties["kind"].Value = "Status";
        snapshot.Properties["text"].Value = title;
        snapshot.Properties["title"].Value = title;
        HostRegistries.Data.UpsertSnapshot(snapshot.Path!, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
    }

    private bool IsRelevantExposureEvent(DataChangedEventArgs e)
    {
        var normalizedPath = TargetPathHelper.NormalizeComparablePath(e.Key);
        var ownedClientRoot = TargetPathHelper.NormalizeComparablePath(GetOwnedClientRootPath());
        if (string.IsNullOrWhiteSpace(normalizedPath)
            || !normalizedPath.StartsWith(ownedClientRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(TargetPathHelper.NormalizeComparablePath(UdlPathHelper.GetCanonicalStatusBasePath(FolderName, ClientId)), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(e.ParameterName, "read", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.ParameterName, "write", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.ParameterName, "value", StringComparison.OrdinalIgnoreCase)
            || e.ChangeKind == DataChangeKind.SnapshotUpserted
            || e.ChangeKind == DataChangeKind.ValueUpdated;
    }

    private bool TryResolveOwnedChannelEvent(DataChangedEventArgs e, out string moduleName, out string channelName)
    {
        moduleName = string.Empty;
        channelName = string.Empty;

        var relativePath = TargetPathHelper.NormalizeConfiguredTargetPath(e.Key)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(3)
            .ToArray();
        if (relativePath.Length != 2)
        {
            return false;
        }

        moduleName = relativePath[0];
        channelName = relativePath[1];
        return !string.IsNullOrWhiteSpace(moduleName) && !string.IsNullOrWhiteSpace(channelName);
    }

    private static bool TryReadBool(object? value, bool fallback)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            uint uintValue => uintValue != 0,
            _ => fallback
        };
    }

    private void UpdateDefinitionState(UdlClientDefinition definition)
    {
        _definition = NormalizeDefinition(definition);
        _exposureDefinitions = UdlModuleExposureDefinitionCodec.ParseDefinitions(_definition.UdlModuleExposureDefinitions);
        _hasActiveBitExposures = UdlClientExposureProjection.HasActiveBitExposures(_exposureDefinitions);
    }

    private string GetOwnedClientRootPath()
        => $"studio.{FolderName}.{ClientId}";

    private void RemoveLegacyExposureRoot()
    {
        HostRegistries.Data.Remove($"studio.{FolderName}.udl_clientruntime.{ClientId}.modules");
    }

    private static UdlClientDefinition NormalizeDefinition(UdlClientDefinition definition)
    {
        return definition with
        {
            ClientId = UdlPathHelper.NormalizeClientName(definition.ClientId),
            AttachedItemPaths = definition.AttachedItemPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(TargetPathHelper.NormalizeConfiguredTargetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
