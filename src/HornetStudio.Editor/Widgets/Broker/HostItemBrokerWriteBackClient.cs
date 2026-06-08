using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amium.Item.Client;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using HornetStudio.Host.Registries;
using HornetStudio.Logging;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Applies writable ItemClient updates from the broker back to local host registry items.
/// </summary>
public sealed class HostItemBrokerWriteBackClient : IDisposable, IAsyncDisposable
{
    private const string WriteBackDiagnosticsSwitchName = "HornetStudio.ItemClient.WriteBackDiagnostics";
    private static readonly ItemSubscriptionOptions ExactSubscriptionOptions = new()
    {
        Recursive = false,
        IncludeRetained = true,
    };

    private readonly IHostItemBrokerClient _client;
    private readonly IReadOnlyDictionary<string, BrokerPublishedItemDefinition> _definitionsByBrokerPath;
    private readonly Func<string, string, object?, bool>? _tryConsumeOwnWriteEcho;
    private readonly Func<string, string, object?, bool>? _hasRecentLocalHostWriteConflict;
    private readonly Dictionary<string, object?> _lastAppliedValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IItemSubscription> _subscriptions = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostItemBrokerWriteBackClient"/> class.
    /// </summary>
    /// <param name="client">The connected broker client.</param>
    /// <param name="definitions">The publish definitions used for write-back.</param>
    /// <param name="tryConsumeOwnWriteEcho">An optional callback that consumes a pending self-published write echo.</param>
    public HostItemBrokerWriteBackClient(
        IHostItemBrokerClient client,
        IEnumerable<BrokerPublishedItemDefinition> definitions,
        Func<string, string, object?, bool>? tryConsumeOwnWriteEcho = null)
        : this(client, definitions, tryConsumeOwnWriteEcho, hasRecentLocalHostWriteConflict: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostItemBrokerWriteBackClient"/> class.
    /// </summary>
    /// <param name="client">The connected broker client.</param>
    /// <param name="definitions">The publish definitions used for write-back.</param>
    /// <param name="tryConsumeOwnWriteEcho">An optional callback that consumes a pending self-published write echo.</param>
    /// <param name="hasRecentLocalHostWriteConflict">An optional callback that reports whether a recent local host write should keep priority over broker state.</param>
    public HostItemBrokerWriteBackClient(
        IHostItemBrokerClient client,
        IEnumerable<BrokerPublishedItemDefinition> definitions,
        Func<string, string, object?, bool>? tryConsumeOwnWriteEcho,
        Func<string, string, object?, bool>? hasRecentLocalHostWriteConflict)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(definitions);

        _client = client;
        _definitionsByBrokerPath = BuildWritableDefinitions(definitions);
        _tryConsumeOwnWriteEcho = tryConsumeOwnWriteEcho;
        _hasRecentLocalHostWriteConflict = hasRecentLocalHostWriteConflict;
    }

    /// <summary>
    /// Gets the number of writable broker paths managed by this runtime.
    /// </summary>
    public int WritablePathCount => _definitionsByBrokerPath.Count;

    /// <summary>
    /// Starts exact broker subscriptions for writable published entries.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        foreach (var definition in _definitionsByBrokerPath.Values)
        {
            var subscription = await _client.SubscribeAsync(
                path: definition.BrokerPath,
                handler: HandleMessageAsync,
                options: ExactSubscriptionOptions,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _subscriptions.Add(subscription);
        }
    }

    /// <summary>
    /// Stops all active write-back subscriptions.
    /// </summary>
    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Stops all active write-back subscriptions.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions.ToArray())
        {
            try
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HostLogger.Log.Warning(ex, "[ItemClientWriteBack] Failed to dispose subscription Path={Path}.", subscription.Path);
            }
        }

        _subscriptions.Clear();
    }

    private Task HandleMessageAsync(ItemServerMessage message, CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var parameterName = GetParameterName(message);
        var value = GetValue(message);
        if (string.IsNullOrWhiteSpace(parameterName)
            || !_definitionsByBrokerPath.TryGetValue(TargetPathHelper.NormalizeConfiguredTargetPath(message.Path), out var definition))
        {
            LogDecision(
                message: message,
                brokerPath: message.Path,
                parameterName: parameterName ?? "<none>",
                value: value,
                localPath: "<none>",
                resolvedWriteTargetPath: "<none>",
                action: "ignored",
                reason: "definitionMissing");
            return Task.CompletedTask;
        }

        if (!HostRegistries.Data.TryResolve(definition.LocalPath, out var localItem) || localItem is null)
        {
            HostLogger.Log.Warning(
                "[ItemClientWriteBack] Ignored write-back because the local item was not found. LocalPath={LocalPath} BrokerPath={BrokerPath}",
                definition.LocalPath,
                definition.BrokerPath);
            LogDecision(
                message: message,
                brokerPath: definition.BrokerPath,
                parameterName: parameterName,
                value: value,
                localPath: definition.LocalPath,
                resolvedWriteTargetPath: "<missing>",
                action: "ignored",
                reason: "localItemMissing");
            return Task.CompletedTask;
        }

        var writeTargetPath = definition.LocalPath;
        var echoItem = localItem;
        if (string.Equals(parameterName, "read", StringComparison.OrdinalIgnoreCase))
        {
            writeTargetPath = ResolveValueWriteTargetPath(localItem, definition.LocalPath);
            if (HostRegistries.Data.TryResolve(writeTargetPath, out var writeTargetItem) && writeTargetItem is not null)
            {
                echoItem = writeTargetItem;
            }
        }

        var resolvedWriteTargetPath = message is ItemWriteRequestMessage
            ? ResolveWriteRequestTargetPath(localItem, definition.LocalPath)
            : writeTargetPath;
        if (message is ItemWriteRequestMessage
            && IsWriteParameter(parameterName)
            && _tryConsumeOwnWriteEcho?.Invoke(definition.BrokerPath, parameterName, value) == true)
        {
            LogDecision(
                message: message,
                brokerPath: definition.BrokerPath,
                parameterName: parameterName,
                value: value,
                localPath: definition.LocalPath,
                resolvedWriteTargetPath: resolvedWriteTargetPath,
                action: "ignored",
                reason: "ownWriteEcho");
            return Task.CompletedTask;
        }

        if (message is not ItemWriteRequestMessage
            && IsWriteParameter(parameterName))
        {
            LogDecision(
                message: message,
                brokerPath: definition.BrokerPath,
                parameterName: parameterName,
                value: value,
                localPath: definition.LocalPath,
                resolvedWriteTargetPath: resolvedWriteTargetPath,
                action: "ignored",
                reason: "writeStateIgnored");
            return Task.CompletedTask;
        }

        if (message is not ItemWriteRequestMessage
            && _hasRecentLocalHostWriteConflict?.Invoke(resolvedWriteTargetPath, parameterName, value) == true)
        {
            LogDecision(
                message: message,
                brokerPath: definition.BrokerPath,
                parameterName: parameterName,
                value: value,
                localPath: definition.LocalPath,
                resolvedWriteTargetPath: resolvedWriteTargetPath,
                action: "ignored",
                reason: "hostWritePriority");
            return Task.CompletedTask;
        }

        if (message is not ItemWriteRequestMessage
            && IsEcho(echoItem, definition.BrokerPath, parameterName, value, message.SourceClientId))
        {
            LogDecision(
                message: message,
                brokerPath: definition.BrokerPath,
                parameterName: parameterName,
                value: value,
                localPath: definition.LocalPath,
                resolvedWriteTargetPath: resolvedWriteTargetPath,
                action: "ignored",
                reason: "selfEcho");
            return Task.CompletedTask;
        }

        var updated = message is ItemWriteRequestMessage
            ? TryApplyWriteRequest(definition.LocalPath, localItem, value)
            : string.Equals(parameterName, "read", StringComparison.OrdinalIgnoreCase)
                ? HostRegistries.Data.UpdateValue(writeTargetPath, value)
                : TryUpdateProperty(definition.LocalPath, parameterName, value);

        if (updated)
        {
            _lastAppliedValues[GetStateKey(definition.BrokerPath, parameterName)] = value;
        }

        LogDecision(
            message: message,
            brokerPath: definition.BrokerPath,
            parameterName: parameterName,
            value: value,
            localPath: definition.LocalPath,
            resolvedWriteTargetPath: resolvedWriteTargetPath,
            action: updated ? "applied" : "ignored",
            reason: updated ? GetAppliedReason(message, parameterName) : "registryUpdateSkipped");
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, BrokerPublishedItemDefinition> BuildWritableDefinitions(IEnumerable<BrokerPublishedItemDefinition> definitions)
        => BrokerPublishedItemDefinitionCodec.ParseDefinitions(BrokerPublishedItemDefinitionCodec.SerializeDefinitions(definitions))
            .Where(static definition => definition.Active
                && definition.Writable
                && !string.IsNullOrWhiteSpace(definition.LocalPath)
                && !string.IsNullOrWhiteSpace(definition.BrokerPath))
            .GroupBy(static definition => definition.BrokerPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static string? GetParameterName(ItemServerMessage message)
        => message switch
        {
            ItemWriteRequestMessage writeRequest => writeRequest.ParameterName,
            ItemValueChangedMessage => "read",
            ItemPropertyChangedMessage parameterChanged => parameterChanged.PropertyName,
            _ => null,
        };

    private static object? GetValue(ItemServerMessage message)
        => message switch
        {
            ItemWriteRequestMessage writeRequest => writeRequest.Value,
            ItemValueChangedMessage valueChanged => valueChanged.Value,
            ItemPropertyChangedMessage parameterChanged => parameterChanged.Value,
            _ => null,
        };

    private static bool TryApplyWriteRequest(string localPath, Amium.Items.Item localItem, object? value)
    {
        if (localItem.Properties.Has("write"))
        {
            return HostRegistries.Data.UpdateProperty(
                key: localPath,
                parameterName: "write",
                value: value,
                forceChangeNotification: true);
        }

        var writeTargetPath = ResolveValueWriteTargetPath(localItem, localPath);
        if (HostRegistries.Data.TryResolve(writeTargetPath, out var writeTargetItem) && writeTargetItem?.Properties.Has("write") == true)
        {
            return HostRegistries.Data.UpdateProperty(
                key: writeTargetPath,
                parameterName: "write",
                value: value,
                forceChangeNotification: true);
        }

        return HostRegistries.Data.UpdateValue(writeTargetPath, value);
    }

    internal static string ResolveWriteRequestTargetPath(Amium.Items.Item localItem, string localPath)
        => ResolveWriteRequestTargetPath(
            localItem: localItem,
            localPath: localPath,
            tryResolveItem: static path => HostRegistries.Data.TryResolve(path, out Amium.Items.Item? resolvedItem) ? resolvedItem : null);

    internal static string ResolveWriteRequestTargetPath(
        Amium.Items.Item localItem,
        string localPath,
        Func<string, Amium.Items.Item?>? tryResolveItem)
    {
        if (localItem.Properties.Has("write"))
        {
            return localItem.Path ?? localPath;
        }

        var writeTargetPath = ResolveValueWriteTargetPath(localItem, localPath, tryResolveItem);
        var writeTargetItem = tryResolveItem?.Invoke(writeTargetPath);
        return writeTargetItem?.Properties.Has("write") == true
            ? writeTargetItem.Path ?? writeTargetPath
            : writeTargetPath;
    }

    private static bool TryUpdateProperty(string localPath, string parameterName, object? value)
    {
        if (!HostRegistryPropertyPolicy.CanUserWriteProperty(parameterName))
        {
            HostLogger.Log.Warning(
                "[ItemClientWriteBack] Blocked protected parameter write. LocalPath={LocalPath} Parameter={Parameter}",
                localPath,
                parameterName);
            return false;
        }

        return HostRegistries.Data.TryUpdateUserProperty(localPath, parameterName, value);
    }

    private static bool IsWriteParameter(string parameterName)
        => string.Equals(parameterName, "write", StringComparison.OrdinalIgnoreCase);

    internal static string ResolveValueWriteTargetPath(Amium.Items.Item sourceItem, string fallbackPath)
        => ResolveValueWriteTargetPath(
            sourceItem: sourceItem,
            fallbackPath: fallbackPath,
            tryResolveItem: static path => HostRegistries.Data.TryResolve(path, out Amium.Items.Item? resolvedItem) ? resolvedItem : null);

    internal static string ResolveValueWriteTargetPath(
        Amium.Items.Item sourceItem,
        string fallbackPath,
        Func<string, Amium.Items.Item?>? tryResolveItem)
    {
        if (sourceItem.Properties.Has("write"))
        {
            return sourceItem.Path ?? fallbackPath;
        }

        if (TryResolveDeclaredWriteTarget(sourceItem, out var declaredTarget, tryResolveItem))
        {
            return declaredTarget.Path ?? fallbackPath;
        }

        return sourceItem.Path ?? fallbackPath;
    }

    private static bool TryResolveDeclaredWriteTarget(
        Amium.Items.Item sourceItem,
        out Amium.Items.Item writeTargetItem,
        Func<string, Amium.Items.Item?>? tryResolveItem)
    {
        writeTargetItem = null!;
        if (sourceItem.Properties.Has("write"))
        {
            writeTargetItem = sourceItem;
            return true;
        }

        if (!sourceItem.Properties.Has("write_path"))
        {
            return false;
        }

        var writePath = sourceItem.Properties["write_path"].Value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(writePath))
        {
            return false;
        }

        var resolvedItem = tryResolveItem?.Invoke(writePath);
        if (resolvedItem is null)
        {
            return false;
        }

        var writeMode = SignalWriteMode.Direct;
        object? rawWriteMode = sourceItem.Properties.Has("write_mode")
            ? sourceItem.Properties["write_mode"].Value
            : null;
        var writeModeText = rawWriteMode?.ToString();
        if (sourceItem.Properties.Has("write_mode")
            && Enum.TryParse<SignalWriteMode>(writeModeText, true, out SignalWriteMode parsedMode))
        {
            writeMode = parsedMode == SignalWriteMode.Request
                ? SignalWriteMode.Direct
                : parsedMode;
        }

        var nonNullResolvedItem = resolvedItem!;
        writeTargetItem = nonNullResolvedItem;
        return true;
    }

    private bool IsEcho(Amium.Items.Item localItem, string brokerPath, string parameterName, object? value, string? sourceClientId)
    {
        var stateKey = GetStateKey(brokerPath, parameterName);
        if (_lastAppliedValues.TryGetValue(stateKey, out var lastValue) && ValuesEqual(lastValue, value))
        {
            return true;
        }

        if (!string.Equals(sourceClientId, _client.ClientId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(parameterName, "read", StringComparison.OrdinalIgnoreCase))
        {
            return ValuesEqual(localItem.Value, value);
        }

        return localItem.Properties.Has(parameterName) && ValuesEqual(localItem.Properties[parameterName].Value, value);
    }

    private static string GetStateKey(string brokerPath, string parameterName)
        => $"{TargetPathHelper.NormalizeConfiguredTargetPath(brokerPath)}\n{parameterName.Trim()}";

    private static string GetAppliedReason(ItemServerMessage message, string parameterName)
    {
        if (message is ItemWriteRequestMessage)
        {
            return "writeRequestApplied";
        }

        return string.Equals(parameterName, "read", StringComparison.OrdinalIgnoreCase)
            ? "stateReadApplied"
            : "propertyApplied";
    }

    private static void LogDecision(
        ItemServerMessage message,
        string brokerPath,
        string parameterName,
        object? value,
        string localPath,
        string resolvedWriteTargetPath,
        string action,
        string reason)
    {
        if (!ShouldWriteBackDiagnostics())
        {
            return;
        }

        HostLogger.Log.Debug(
            "[ItemClientWriteBack] messageType={MessageType} brokerPath={BrokerPath} parameter={Parameter} value={Value} sourceClientId={SourceClientId} localPath={LocalPath} resolvedWriteTarget={ResolvedWriteTarget} action={Action} reason={Reason}",
            message.GetType().Name,
            brokerPath,
            parameterName,
            FormatDiagnosticValue(value),
            message.SourceClientId ?? "<none>",
            localPath,
            resolvedWriteTargetPath,
            action,
            reason);
    }

    private static bool ShouldWriteBackDiagnostics()
        => AppContext.TryGetSwitch(WriteBackDiagnosticsSwitchName, out var enabled) && enabled;

    private static string FormatDiagnosticValue(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value is IFormattable formattable
            ? $"{formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)} ({value.GetType().Name})"
            : $"{value} ({value.GetType().Name})";
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (Equals(left, right))
        {
            return true;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            try
            {
                return Convert.ToDecimal(left, System.Globalization.CultureInfo.InvariantCulture)
                    == Convert.ToDecimal(right, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsNumeric(object? value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HostItemBrokerWriteBackClient));
        }
    }
}
