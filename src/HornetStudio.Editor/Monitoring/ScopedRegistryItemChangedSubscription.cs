using System;
using System.Collections.Generic;
using System.Linq;
using HornetStudio.Editor.Helpers;
using HornetStudio.Host.Registries;

namespace HornetStudio.Editor.Monitoring;

/// <summary>
/// Provides prefix-based registry event matching for browser-scoped subscriptions.
/// </summary>
public static class ScopedRegistryEventFilter
{
    /// <summary>
    /// Normalizes and de-duplicates registry scope prefixes.
    /// </summary>
    /// <param name="prefixes">The raw prefixes to normalize.</param>
    /// <returns>The normalized prefixes ordered from shortest to longest scope.</returns>
    public static string[] NormalizePrefixes(IEnumerable<string?> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        var normalized = prefixes
            .Select(static prefix => TargetPathHelper.NormalizeConfiguredTargetPath(prefix))
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static prefix => prefix.Count(static character => character is '.' or '/'))
            .ThenBy(static prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<string>(normalized.Length);
        foreach (var prefix in normalized)
        {
            if (result.Any(existing => MatchesPrefix(path: prefix, prefix: existing)))
            {
                continue;
            }

            result.RemoveAll(existing => MatchesPrefix(path: existing, prefix: prefix));
            result.Add(prefix);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Determines whether a registry key matches one of the configured prefixes.
    /// </summary>
    /// <param name="key">The changed registry key.</param>
    /// <param name="prefixes">The normalized prefixes.</param>
    /// <param name="matchedPrefix">The matched prefix when available.</param>
    /// <param name="includeAncestorMatches">Whether ancestor keys should match more specific prefixes.</param>
    /// <returns><see langword="true"/> when the key matches one of the prefixes; otherwise <see langword="false"/>.</returns>
    public static bool TryMatchPrefix(string? key, IReadOnlyList<string>? prefixes, out string matchedPrefix, bool includeAncestorMatches = true)
    {
        matchedPrefix = string.Empty;
        if (string.IsNullOrWhiteSpace(key) || prefixes is null || prefixes.Count == 0)
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!MatchesPrefix(path: key, prefix: prefix, includeAncestorMatches: includeAncestorMatches))
            {
                continue;
            }

            matchedPrefix = prefix;
            return true;
        }

        return false;
    }

    private static bool MatchesPrefix(string? path, string? prefix, bool includeAncestorMatches = true)
        => TargetPathHelper.PathsEqual(path, prefix)
           || TargetPathHelper.IsDescendantPath(path, prefix)
           || (includeAncestorMatches && TargetPathHelper.IsDescendantPath(prefix, path));
}

internal sealed class ScopedRegistryItemChangedSubscription : IDisposable
{
    private readonly EventHandler<DataChangedEventArgs> _callback;
    private readonly string? _diagnosticsSource;
    private readonly bool _includeAncestorMatches;
    private int _disposed;
    private string[] _prefixes = [];

    public ScopedRegistryItemChangedSubscription(EventHandler<DataChangedEventArgs> callback, string? diagnosticsSource = null, bool includeAncestorMatches = true)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _diagnosticsSource = string.IsNullOrWhiteSpace(diagnosticsSource) ? null : diagnosticsSource;
        _includeAncestorMatches = includeAncestorMatches;
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
    }

    public void UpdatePrefixes(IEnumerable<string?> prefixes)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _prefixes = ScopedRegistryEventFilter.NormalizePrefixes(prefixes);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        _prefixes = [];
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var prefixes = _prefixes;
        if (!ScopedRegistryEventFilter.TryMatchPrefix(e.Key, prefixes, out _, includeAncestorMatches: _includeAncestorMatches))
        {
            return;
        }

        if (_diagnosticsSource is null)
        {
            _callback(sender, e);
            return;
        }

        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackSteadyStateOperation(
            owner: null,
            category: "SteadyStateRegistry",
            name: $"{_diagnosticsSource}.ItemChangedDispatch",
            threshold: TimeSpan.FromMilliseconds(10),
            stateFactory: () => new Dictionary<string, object?>
            {
                ["Key"] = e.Key,
                ["PrefixCount"] = prefixes.Length
            });
        _callback(sender, e);
    }
}
