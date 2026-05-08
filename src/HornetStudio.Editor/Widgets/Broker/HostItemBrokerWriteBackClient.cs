using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amium.Item.Server;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Host;
using HornetStudio.Logging;

namespace HornetStudio.Editor.Widgets;

/// <summary>
/// Applies writable BrokerWidget updates from the broker back to local host registry items.
/// </summary>
public sealed class HostItemBrokerWriteBackClient : IDisposable, IAsyncDisposable
{
    private static readonly ItemSubscriptionOptions ExactSubscriptionOptions = new()
    {
        Recursive = false,
        IncludeRetained = true,
    };

    private readonly IHostItemBrokerClient _client;
    private readonly IReadOnlyDictionary<string, BrokerPublishedItemDefinition> _definitionsByBrokerPath;
    private readonly Dictionary<string, object?> _lastAppliedValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IItemSubscription> _subscriptions = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostItemBrokerWriteBackClient"/> class.
    /// </summary>
    /// <param name="client">The connected broker client.</param>
    /// <param name="definitions">The publish definitions used for write-back.</param>
    public HostItemBrokerWriteBackClient(IHostItemBrokerClient client, IEnumerable<BrokerPublishedItemDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(definitions);

        _client = client;
        _definitionsByBrokerPath = BuildWritableDefinitions(definitions);
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
                HostLogger.Log.Warning(ex, "[BrokerWidgetWriteBack] Failed to dispose subscription Path={Path}.", subscription.Path);
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
            return Task.CompletedTask;
        }

        if (!HostRegistries.Data.TryResolve(definition.LocalPath, out var localItem) || localItem is null)
        {
            HostLogger.Log.Warning(
                "[BrokerWidgetWriteBack] Ignored write-back because the local item was not found. LocalPath={LocalPath} BrokerPath={BrokerPath}",
                definition.LocalPath,
                definition.BrokerPath);
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

        if (IsEcho(echoItem, definition.BrokerPath, parameterName, value, message.SourceClientId))
        {
            return Task.CompletedTask;
        }

        var updated = string.Equals(parameterName, "read", StringComparison.OrdinalIgnoreCase)
            ? HostRegistries.Data.UpdateValue(writeTargetPath, value)
            : TryUpdateProperty(definition.LocalPath, parameterName, value);

        if (updated)
        {
            _lastAppliedValues[GetStateKey(definition.BrokerPath, parameterName)] = value;
        }

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
            ItemValueChangedMessage => "read",
            ItemPropertyChangedMessage parameterChanged => parameterChanged.PropertyName,
            _ => null,
        };

    private static object? GetValue(ItemServerMessage message)
        => message switch
        {
            ItemValueChangedMessage valueChanged => valueChanged.Value,
            ItemPropertyChangedMessage parameterChanged => parameterChanged.Value,
            _ => null,
        };

    private static bool TryUpdateProperty(string localPath, string parameterName, object? value)
    {
        if (!HostRegistryPropertyPolicy.CanUserWriteProperty(parameterName))
        {
            HostLogger.Log.Warning(
                "[BrokerWidgetWriteBack] Blocked protected parameter write. LocalPath={LocalPath} Parameter={Parameter}",
                localPath,
                parameterName);
            return false;
        }

        return HostRegistries.Data.TryUpdateUserProperty(localPath, parameterName, value);
    }

    private static string ResolveValueWriteTargetPath(Amium.Items.Item sourceItem, string fallbackPath)
    {
        if (sourceItem.Properties.Has("write"))
        {
            return sourceItem.Path ?? fallbackPath;
        }

        if (TryResolveDeclaredWriteTarget(sourceItem, out var declaredTarget))
        {
            return declaredTarget.Path ?? fallbackPath;
        }

        if (sourceItem.Has("Request"))
        {
            return sourceItem["Request"].Path ?? fallbackPath;
        }

        return sourceItem.Path ?? fallbackPath;
    }

    private static bool TryResolveDeclaredWriteTarget(Amium.Items.Item sourceItem, out Amium.Items.Item writeTargetItem)
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

        if (!HostRegistries.Data.TryResolve(writePath, out Amium.Items.Item? resolvedItem) || resolvedItem is null)
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
            writeMode = parsedMode;
        }

        var nonNullResolvedItem = resolvedItem!;
        writeTargetItem = writeMode == SignalWriteMode.Request && nonNullResolvedItem.Has("Request")
            ? nonNullResolvedItem["Request"]!
            : nonNullResolvedItem;
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
