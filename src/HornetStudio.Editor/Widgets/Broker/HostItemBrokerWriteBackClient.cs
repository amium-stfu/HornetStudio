using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amium.ItemBroker;
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

    private Task HandleMessageAsync(ItemBrokerMessage message, CancellationToken cancellationToken)
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

        if (IsEcho(localItem, definition.BrokerPath, parameterName, value))
        {
            return Task.CompletedTask;
        }

        var updated = string.Equals(parameterName, "Value", StringComparison.OrdinalIgnoreCase)
            ? HostRegistries.Data.UpdateValue(definition.LocalPath, value)
            : TryUpdateParameter(definition.LocalPath, parameterName, value);

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

    private static string? GetParameterName(ItemBrokerMessage message)
        => message switch
        {
            ItemValueChangedMessage => "Value",
            ItemParameterChangedMessage parameterChanged => parameterChanged.ParameterName,
            _ => null,
        };

    private static object? GetValue(ItemBrokerMessage message)
        => message switch
        {
            ItemValueChangedMessage valueChanged => valueChanged.Value,
            ItemParameterChangedMessage parameterChanged => parameterChanged.Value,
            _ => null,
        };

    private static bool TryUpdateParameter(string localPath, string parameterName, object? value)
    {
        if (!HostRegistryParameterPolicy.CanUserWriteParameter(parameterName))
        {
            HostLogger.Log.Warning(
                "[BrokerWidgetWriteBack] Blocked protected parameter write. LocalPath={LocalPath} Parameter={Parameter}",
                localPath,
                parameterName);
            return false;
        }

        return HostRegistries.Data.TryUpdateUserParameter(localPath, parameterName, value);
    }

    private bool IsEcho(Amium.Item.Item localItem, string brokerPath, string parameterName, object? value)
    {
        if (string.Equals(parameterName, "Value", StringComparison.OrdinalIgnoreCase))
        {
            return ValuesEqual(localItem.Value, value)
                || (_lastAppliedValues.TryGetValue(GetStateKey(brokerPath, parameterName), out var lastValue) && ValuesEqual(lastValue, value));
        }

        if (localItem.Params.Has(parameterName) && ValuesEqual(localItem.Params[parameterName].Value, value))
        {
            return true;
        }

        return _lastAppliedValues.TryGetValue(GetStateKey(brokerPath, parameterName), out var lastParameterValue)
            && ValuesEqual(lastParameterValue, value);
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
