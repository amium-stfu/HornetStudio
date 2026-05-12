using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HornetStudio.Contracts;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Host;

internal sealed class DataRegistrySignal : ISignal
{
    private readonly IDataRegistry _dataRegistry;
    private readonly string _sourcePath;
    private object? _cachedValue;

    public DataRegistrySignal(IDataRegistry dataRegistry, string sourcePath, SignalDescriptor descriptor)
    {
        _dataRegistry = dataRegistry ?? throw new ArgumentNullException(nameof(dataRegistry));
        _sourcePath = string.IsNullOrWhiteSpace(sourcePath)
            ? throw new ArgumentException("Source path must not be empty.", nameof(sourcePath))
            : sourcePath;
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

        if (_dataRegistry.TryResolve(_sourcePath, out var item) && item is not null)
        {
            _cachedValue = item.Value;
        }
    }

    public SignalDescriptor Descriptor { get; }

    public object? Value
    {
        get => _cachedValue;
        set
        {
            _dataRegistry.UpdateValue(_sourcePath, value, null);
        }
    }

    public event EventHandler<SignalValueChangedEventArgs>? ValueChanged;

    internal void OnSourceValueUpdated(object? newValue)
    {
        var oldValue = _cachedValue;
        _cachedValue = newValue;
        var args = new SignalValueChangedEventArgs(Descriptor, oldValue, newValue, DateTimeOffset.UtcNow);
        ValueChanged?.Invoke(this, args);
    }
}

/// <summary>
/// Exposes data registry items as host signals.
/// </summary>
public sealed class SignalRegistry : ISignalRegistry
{
    private const string StudioRootSegment = "studio";
    private static readonly string[] LegacyProjectRootSegments = ["Project", "UdlProject", "UdlBook"];
    private readonly IDataRegistry _dataRegistry;
    private readonly ConcurrentDictionary<string, DataRegistrySignal> _signalsBySourcePath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DataRegistrySignal> _signalsById = new(StringComparer.OrdinalIgnoreCase);

    public SignalRegistry(IDataRegistry dataRegistry)
    {
        _dataRegistry = dataRegistry ?? throw new ArgumentNullException(nameof(dataRegistry));
        _dataRegistry.ItemChanged += OnDataRegistryItemChanged;
    }

    public event EventHandler<SignalValueChangedEventArgs>? SignalChanged;

    public IReadOnlyCollection<SignalDescriptor> GetAllDescriptors()
        => _signalsById.Values
            .Select(signal => signal.Descriptor)
            .Distinct()
            .ToArray();

    public bool TryGetById(string id, out ISignal? signal)
    {
        signal = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (_signalsById.TryGetValue(id, out var existing))
        {
            signal = existing;
            return true;
        }

        // Fallback: interpret id as source path if no explicit descriptor exists yet.
        if (!TryGetBySourcePath(id, out signal))
        {
            return false;
        }

        if (signal is DataRegistrySignal concrete)
        {
            _signalsById.TryAdd(concrete.Descriptor.Id, concrete);
        }

        return true;
    }

    public bool TryGetBySourcePath(string sourcePath, out ISignal? signal)
    {
        signal = null;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        var signalKey = NormalizeSourcePath(sourcePath);
        if (_signalsBySourcePath.TryGetValue(signalKey, out var existing))
        {
            signal = existing;
            return true;
        }

        if (!_dataRegistry.TryResolve(sourcePath, out var item) || item is null)
        {
            return false;
        }

        var canonicalSourcePath = string.IsNullOrWhiteSpace(item.Path) ? sourcePath : item.Path!;
        var canonicalSignalKey = NormalizeSourcePath(canonicalSourcePath);
        if (_signalsBySourcePath.TryGetValue(canonicalSignalKey, out existing))
        {
            _signalsBySourcePath.TryAdd(signalKey, existing);
            signal = existing;
            return true;
        }

        var descriptor = CreateDescriptorFromItem(canonicalSourcePath, item);
        var created = new DataRegistrySignal(_dataRegistry, canonicalSourcePath, descriptor);

        existing = _signalsBySourcePath.GetOrAdd(canonicalSignalKey, created);
        _signalsBySourcePath.TryAdd(signalKey, existing);
        _signalsById.TryAdd(existing.Descriptor.Id, existing);

        signal = existing;
        return true;
    }

    private static SignalDescriptor CreateDescriptorFromItem(string sourcePath, ItemModel item)
    {
        var name = item.Name ?? sourcePath;
        var unit = item.Properties.Has("unit") ? item.Properties["unit"].Value?.ToString() : null;
        var format = item.Properties.Has("format") ? item.Properties["format"].Value?.ToString() : null;

        var value = item.Value;
        var dataType = InferDataType(value);

        var isWritable = InferIsWritable(item);
        var category = item.Properties.Has("kind") ? item.Properties["kind"].Value?.ToString() : null;

        return new SignalDescriptor(
            id: sourcePath,
            name: name,
            dataType: dataType,
            unit: unit,
            format: format,
            sourcePath: sourcePath,
            isWritable: isWritable,
            category: category);
    }

    private static SignalDataType InferDataType(object? value)
    {
        if (value is null)
        {
            return SignalDataType.Unknown;
        }

        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();

        if (type == typeof(bool))
        {
            return SignalDataType.Boolean;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                return SignalDataType.Integer;

            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return SignalDataType.Float;

            case TypeCode.String:
                return SignalDataType.String;

            default:
                return SignalDataType.Object;
        }
    }

    private static bool InferIsWritable(ItemModel item)
    {
        bool writable;
        if (item.Properties.Has("write"))
        {
            return true;
        }

        if (item.Properties.Has("writable") && TryConvertBoolean((object?)item.Properties["writable"].Value, out writable))
        {
            return writable;
        }

        if (item.Properties.Has("is_writable") && TryConvertBoolean((object?)item.Properties["is_writable"].Value, out writable))
        {
            return writable;
        }

        if (item.Properties.Has("write_path") || item.Properties.Has("Set") || item.Properties.Has("Write"))
        {
            return true;
        }

        return true;
    }

    private static bool TryConvertBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool booleanValue:
                result = booleanValue;
                return true;
            case string stringValue when bool.TryParse(stringValue, out var parsed):
                result = parsed;
                return true;
            case string stringValue when string.Equals(stringValue, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stringValue, "yes", StringComparison.OrdinalIgnoreCase):
                result = true;
                return true;
            case string stringValue when string.Equals(stringValue, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stringValue, "no", StringComparison.OrdinalIgnoreCase):
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private void OnDataRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind != DataChangeKind.ValueUpdated)
        {
            return;
        }

        if (!TryGetSignalForChangedItem(e, out var signal) || signal is null)
        {
            return;
        }

        var currentValue = e.ItemModel.Value;
        signal.OnSourceValueUpdated(currentValue);

        var args = new SignalValueChangedEventArgs(signal.Descriptor, null, currentValue, DateTimeOffset.FromUnixTimeMilliseconds((long)e.Timestamp));
        SignalChanged?.Invoke(this, args);
    }

    private bool TryGetSignalForChangedItem(DataChangedEventArgs e, out DataRegistrySignal? signal)
    {
        if (_signalsBySourcePath.TryGetValue(NormalizeSourcePath(e.Key), out signal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(e.ItemModel.Path)
            && _signalsBySourcePath.TryGetValue(NormalizeSourcePath(e.ItemModel.Path!), out signal))
        {
            return true;
        }

        signal = null;
        return false;
    }

    private static string NormalizeSourcePath(string path)
        => string.Join('.', NormalizeStudioRoot(path
            .Trim()
            .Replace('\\', '.')
            .Replace('/', '.')
            .Trim('.')
            .Split(['.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(HostPathSegmentNormalizer.Normalize)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray()));

    private static IEnumerable<string> NormalizeStudioRoot(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            yield break;
        }

        if (LegacyProjectRootSegments.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            yield return StudioRootSegment;
            foreach (var segment in segments.Skip(1))
            {
                yield return HostPathSegmentNormalizer.Normalize(segment);
            }

            yield break;
        }

        if (string.Equals(segments[0], StudioRootSegment, StringComparison.OrdinalIgnoreCase)
            && segments.Count > 1
            && LegacyProjectRootSegments.Contains(segments[1], StringComparer.OrdinalIgnoreCase))
        {
            yield return StudioRootSegment;
            foreach (var segment in segments.Skip(2))
            {
                yield return HostPathSegmentNormalizer.Normalize(segment);
            }

            yield break;
        }

        foreach (var segment in segments)
        {
            yield return HostPathSegmentNormalizer.Normalize(segment);
        }
    }
}
