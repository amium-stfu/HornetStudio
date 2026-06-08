using System;
using System.Collections.Generic;
using System.Linq;
using Amium.Items;
using HornetStudio.Host;
using HornetStudio.Host.Registries;
using HornetStudio.Host.Runtimes.EnhancedSignal;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Host.Runtimes.Udl;

public sealed record UdlAttachmentProjectionInput(
    string RelativePath,
    string Alias,
    ItemModel RuntimeItem);

internal sealed class UdlHostRegistryProjection : IDisposable
{
    private const string ValueRefPathPropertyName = "valueRefPath";
    private const string ValueRefParameterPropertyName = "valueRefParameter";
    private readonly object _syncLock = new();
    private readonly Dictionary<string, AttachmentProjectionEntry> _attachmentEntries = new(StringComparer.OrdinalIgnoreCase);

    public UdlHostRegistryProjection(string folderName, string clientName)
    {
        FolderName = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName);
        ClientName = UdlPathHelper.NormalizeClientName(clientName);
    }

    public string FolderName { get; }

    public string ClientName { get; }

    public bool SynchronizeAttachments(IReadOnlyList<UdlAttachmentProjectionInput> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        lock (_syncLock)
        {
            var requestedEntries = new Dictionary<string, UdlAttachmentProjectionInput>(StringComparer.OrdinalIgnoreCase);
            foreach (var attachment in attachments)
            {
                var relativePath = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(attachment.RelativePath);
                if (string.IsNullOrWhiteSpace(relativePath)
                    || string.IsNullOrWhiteSpace(attachment.RuntimeItem.Path))
                {
                    continue;
                }

                requestedEntries[relativePath] = attachment with
                {
                    RelativePath = relativePath,
                    Alias = NormalizeAlias(relativePath, attachment.Alias)
                };
            }

            var changed = false;

            foreach (var removedRelativePath in _attachmentEntries.Keys
                .Except(requestedEntries.Keys, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            {
                _attachmentEntries[removedRelativePath].Dispose();
                _attachmentEntries.Remove(removedRelativePath);
                changed = true;
            }

            foreach (var requestedEntry in requestedEntries.Values)
            {
                if (_attachmentEntries.TryGetValue(requestedEntry.RelativePath, out var existingEntry)
                    && existingEntry.Matches(requestedEntry.Alias, requestedEntry.RuntimeItem.Path!))
                {
                    continue;
                }

                existingEntry?.Dispose();
                _attachmentEntries[requestedEntry.RelativePath] = CreateAttachmentEntry(requestedEntry);
                changed = true;
            }

            return changed;
        }
    }

    public void ClearAttachments()
    {
        lock (_syncLock)
        {
            ClearAttachmentsCore();
        }
    }

    public void Dispose()
    {
        ClearAttachments();
    }

    private AttachmentProjectionEntry CreateAttachmentEntry(UdlAttachmentProjectionInput attachment)
    {
        var folderContext = new UiFolderContext($"{FolderName}.{ClientName}", "Project");
        var attached = folderContext.Attach(attachment.RuntimeItem, attachment.Alias);
        ApplyValueReferenceMetadata(attached, attachment.RuntimeItem);
        HostRegistries.Data.UpsertSnapshot(attached.Path!, attached.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
        return new AttachmentProjectionEntry(folderContext, attachment.Alias, attachment.RuntimeItem.Path!);
    }

    private void ClearAttachmentsCore()
    {
        if (_attachmentEntries.Count == 0)
        {
            return;
        }

        foreach (var entry in _attachmentEntries.Values)
        {
            entry.Dispose();
        }

        _attachmentEntries.Clear();
    }

    private static string NormalizeAlias(string relativePath, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return relativePath;
        }

        return EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(alias);
    }

    private static void ApplyValueReferenceMetadata(ItemModel attachedItem, ItemModel runtimeItem)
    {
        if (TryResolveReadChannel(attachedItem, out var attachedRead)
            && TryResolveReadChannel(runtimeItem, out var runtimeRead)
            && attachedRead is not null
            && runtimeRead is not null
            && !string.IsNullOrWhiteSpace(runtimeRead.Path))
        {
            attachedRead.Properties[ValueRefPathPropertyName].Value = runtimeRead.Path;
            attachedRead.Properties[ValueRefParameterPropertyName].Value = "read";
        }
    }

    private static bool TryResolveReadChannel(ItemModel item, out ItemModel? readItem)
    {
        if (string.Equals(item.Name, "read", StringComparison.OrdinalIgnoreCase))
        {
            readItem = item;
            return true;
        }

        if (item.Has("read"))
        {
            readItem = item["read"];
            return true;
        }

        readItem = null;
        return false;
    }

    private sealed class AttachmentProjectionEntry : IDisposable
    {
        private readonly UiFolderContext _folderContext;

        public AttachmentProjectionEntry(UiFolderContext folderContext, string alias, string runtimePath)
        {
            _folderContext = folderContext;
            Alias = alias;
            RuntimePath = runtimePath;
        }

        public string Alias { get; }

        public string RuntimePath { get; }

        public bool Matches(string alias, string runtimePath)
        {
            return string.Equals(Alias, alias, StringComparison.OrdinalIgnoreCase)
                && string.Equals(RuntimePath, runtimePath, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            _folderContext.Dispose();
        }
    }
}
