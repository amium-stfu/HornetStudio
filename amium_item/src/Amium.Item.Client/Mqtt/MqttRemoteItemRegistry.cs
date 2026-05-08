using System.Collections.Concurrent;
using System.Globalization;
using Amium.Item.Server;
using Amium.Item.Server.Mqtt;
using Amium.Items;
using ItemModel = Amium.Items.Item;

namespace Amium.Item.Client.Mqtt;

/// <summary>
/// Describes the type of change applied to a remote MQTT item registry.
/// </summary>
public enum MqttRemoteItemChangeKind
{
    /// <summary>
    /// A remote item snapshot or missing ancestor was created.
    /// </summary>
    Snapshot,

    /// <summary>
    /// A remote item value changed.
    /// </summary>
    Value,

    /// <summary>
    /// A remote item parameter changed.
    /// </summary>
    Parameter,

    /// <summary>
    /// A remote client status changed.
    /// </summary>
    ClientStatus,

    /// <summary>
    /// Diagnostic information was produced.
    /// </summary>
    Diagnostic
}

/// <summary>
/// Provides data for remote MQTT item registry changes.
/// </summary>
/// <param name="Kind">The change kind.</param>
/// <param name="RemoteClientId">The remote client id.</param>
/// <param name="Path">The remote item path.</param>
/// <param name="ItemModel">The changed item.</param>
/// <param name="ParameterName">The changed parameter name.</param>
/// <param name="Message">The diagnostic message.</param>
public sealed record MqttRemoteItemChangedEventArgs(
    MqttRemoteItemChangeKind Kind,
    string RemoteClientId,
    string Path,
    ItemModel? ItemModel,
    string? ParameterName = null,
    string? Message = null);

/// <summary>
/// Reconstructs remote item trees from MQTT item topics.
/// </summary>
public sealed class MqttRemoteItemRegistry
{
    private const string ValueParameterName = "read";
    private readonly ConcurrentDictionary<string, ItemModel> _clientRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>
    /// Occurs when the registry changes.
    /// </summary>
    public event EventHandler<MqttRemoteItemChangedEventArgs>? Changed;

    /// <summary>
    /// Gets the current remote client root snapshots.
    /// </summary>
    /// <returns>The remote client roots keyed by client id.</returns>
    public IReadOnlyDictionary<string, ItemModel> GetClientRoots()
    {
        lock (_sync)
        {
            return _clientRoots.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Applies an MQTT topic payload to the remote item registry.
    /// </summary>
    /// <param name="mapping">The mapped MQTT topic address.</param>
    /// <param name="payload">The textual payload.</param>
    public void Apply(MqttTopicMapping mapping, string payload)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (string.IsNullOrWhiteSpace(mapping.ClientId))
        {
            return;
        }

        var value = ParsePayload(payload);
        ItemModel item;
        List<MqttRemoteItemChangedEventArgs> changes = [];
        lock (_sync)
        {
            item = GetOrCreateItem(mapping.ClientId, mapping.Path, changes);
            if (string.Equals(mapping.PropertyName, ValueParameterName, StringComparison.Ordinal))
            {
                object? existingValue = item.Value;
                if (!ItemServerValueCoercion.TryConvertForExistingValue(value, existingValue, out object? convertedValue))
                {
                    return;
                }

                item.Value = convertedValue!;
                TouchClientRoot(mapping.ClientId, "online", changes);
                changes.Add(CreateChanged(MqttRemoteItemChangeKind.Value, mapping.ClientId, mapping.Path, item));
            }
            else
            {
                var parameter = item.Properties[mapping.PropertyName];
                object? existingValue = parameter.Value;
                if (!ItemServerValueCoercion.TryConvertForExistingValue(value, existingValue, out object? convertedValue))
                {
                    return;
                }

                parameter.Value = convertedValue!;
                TouchClientRoot(mapping.ClientId, "online", changes);
                changes.Add(CreateChanged(MqttRemoteItemChangeKind.Parameter, mapping.ClientId, mapping.Path, item, mapping.PropertyName));
            }
        }

        RaiseChanges(changes);
    }

    /// <summary>
    /// Marks a remote client as offline while preserving its retained items.
    /// </summary>
    /// <param name="remoteClientId">The remote client id.</param>
    public void MarkOffline(string remoteClientId)
    {
        if (string.IsNullOrWhiteSpace(remoteClientId))
        {
            return;
        }

        ItemModel root;
        List<MqttRemoteItemChangedEventArgs> changes = [];
        lock (_sync)
        {
            root = GetOrCreateClientRoot(remoteClientId.Trim());
            root.Properties["connection_status"].Value = "offline";
            root.Properties["stale"].Value = true;
            changes.Add(CreateChanged(MqttRemoteItemChangeKind.ClientStatus, remoteClientId.Trim(), string.Empty, root));
        }

        RaiseChanges(changes);
    }

    /// <summary>
    /// Publishes a diagnostic registry event.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    public void ReportDiagnostic(string message)
        => Changed?.Invoke(this, new MqttRemoteItemChangedEventArgs(MqttRemoteItemChangeKind.Diagnostic, string.Empty, string.Empty, null, Message: message));

    private ItemModel GetOrCreateClientRoot(string remoteClientId)
    {
        return _clientRoots.GetOrAdd(remoteClientId, id =>
        {
            var root = new ItemModel(id, path: id);
            root.Properties["remote_client_id"].Value = id;
            root.Properties["connection_status"].Value = "online";
            root.Properties["last_seen_utc"].Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            root.Properties["stale"].Value = false;
            return root;
        });
    }

    private ItemModel GetOrCreateItem(string remoteClientId, string path, List<MqttRemoteItemChangedEventArgs> changes)
    {
        var root = GetOrCreateClientRoot(remoteClientId);
        var current = root;
        foreach (var segment in SplitPath(path))
        {
            if (!current.Has(segment))
            {
                current[segment] = new ItemModel(segment, path: current.Path);
                changes.Add(CreateChanged(MqttRemoteItemChangeKind.Snapshot, remoteClientId, path, current[segment]));
            }

            current = current[segment];
        }

        return current;
    }

    private void TouchClientRoot(string remoteClientId, string status, List<MqttRemoteItemChangedEventArgs> changes)
    {
        var root = GetOrCreateClientRoot(remoteClientId);
        var statusChanged = !string.Equals(root.Properties["connection_status"].Value?.ToString(), status, StringComparison.OrdinalIgnoreCase)
                            || root.Properties["stale"].Value is true;
        root.Properties["connection_status"].Value = status;
        root.Properties["last_seen_utc"].Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        root.Properties["stale"].Value = false;
        if (statusChanged)
        {
            changes.Add(CreateChanged(MqttRemoteItemChangeKind.ClientStatus, remoteClientId, string.Empty, root));
        }
    }

    private static MqttRemoteItemChangedEventArgs CreateChanged(MqttRemoteItemChangeKind kind, string remoteClientId, string path, ItemModel item, string? parameterName = null)
        => new(kind, remoteClientId, path, item.Clone(), parameterName);

    private void RaiseChanges(IEnumerable<MqttRemoteItemChangedEventArgs> changes)
    {
        foreach (var change in changes)
        {
            Changed?.Invoke(this, change);
        }
    }

    private static IReadOnlyList<string> SplitPath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? []
            : ItemPath.Normalize(path).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static object? ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        if (bool.TryParse(payload, out var boolean))
        {
            return boolean;
        }

        if (long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatingPoint))
        {
            return floatingPoint;
        }

        return payload;
    }
}