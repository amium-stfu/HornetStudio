using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HornetStudio.Host.Runtimes.EnhancedSignal;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Host.Runtimes.Udl;

internal static class UdlClientExposureProjection
{
    private static readonly double StopwatchTickToMilliseconds = 1000d / Stopwatch.Frequency;

    internal static void Synchronize(UdlClientRuntime runtime, UdlClientDefinition definition)
        => Synchronize(runtime, definition, UdlModuleExposureDefinitionCodec.ParseDefinitions(definition.UdlModuleExposureDefinitions));

    internal static void Synchronize(UdlClientRuntime runtime, UdlClientDefinition definition, IReadOnlyList<UdlModuleExposureDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definitions);

        var runtimeItems = runtime.GetRuntimeItemsSnapshot();
        var desiredChannels = new Dictionary<string, (UdlModuleExposureDefinition Definition, ItemModel RuntimeChannel, int BitCount)>(StringComparer.OrdinalIgnoreCase);

        foreach (var exposureDefinition in definitions)
        {
            if (!exposureDefinition.ExposeBits
                || !TryResolveRuntimeChannel(runtime, exposureDefinition, out var runtimeChannel)
                || runtimeChannel?.Path is null)
            {
                continue;
            }

            var bitCount = ResolveBitCount(exposureDefinition, runtimeChannel);
            if (bitCount <= 0)
            {
                continue;
            }

            desiredChannels[runtimeChannel.Path] = (exposureDefinition, runtimeChannel, bitCount);
        }

        foreach (var runtimeChannel in runtimeItems.Where(IsRuntimeChannelItem))
        {
            if (string.IsNullOrWhiteSpace(runtimeChannel.Path))
            {
                continue;
            }

            if (desiredChannels.TryGetValue(runtimeChannel.Path, out var exposure))
            {
                UpsertRuntimeExposureBits(runtime, runtimeChannel, exposure.Definition, exposure.BitCount);
            }
            else
            {
                RemoveRuntimeExposureBits(runtimeChannel);
            }
        }
    }

    internal static bool HasActiveBitExposures(IReadOnlyList<UdlModuleExposureDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions.Any(static definition => definition.ExposeBits);
    }

    internal static void Clear(UdlClientRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        foreach (var runtimeChannel in runtime.GetRuntimeItemsSnapshot().Where(IsRuntimeChannelItem))
        {
            RemoveRuntimeExposureBits(runtimeChannel);
        }
    }

    internal static bool TryGetExposureBitMetadata(ItemModel item, out string moduleName, out string channelName, out int bitIndex)
    {
        moduleName = string.Empty;
        channelName = string.Empty;
        bitIndex = -1;

        if (!item.Properties.Has("module_name")
            || !item.Properties.Has("channel_name")
            || !item.Properties.Has("bit_index"))
        {
            return false;
        }

        moduleName = item.Properties["module_name"].Value?.ToString() ?? string.Empty;
        channelName = item.Properties["channel_name"].Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moduleName)
            || string.IsNullOrWhiteSpace(channelName)
            || !int.TryParse(item.Properties["bit_index"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitIndex))
        {
            return false;
        }

        return true;
    }

    internal static bool ApplyBitWriteback(UdlClientRuntime runtime, UdlClientDefinition definition, string moduleName, string channelName, int bitIndex, bool enabled)
        => ApplyBitWriteback(runtime, definition, UdlModuleExposureDefinitionCodec.ParseDefinitions(definition.UdlModuleExposureDefinitions), moduleName, channelName, bitIndex, enabled);

    internal static bool ApplyBitWriteback(
        UdlClientRuntime runtime,
        UdlClientDefinition definition,
        IReadOnlyList<UdlModuleExposureDefinition> definitions,
        string moduleName,
        string channelName,
        int bitIndex,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definitions);

        var effectiveChannelName = ResolveEffectiveWriteChannelName(runtime, definitions, moduleName, channelName);
        if (!TryResolveRuntimeChannel(runtime, new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = effectiveChannelName }, out var runtimeChannel)
            || runtimeChannel is null)
        {
            return false;
        }

        var currentWriteValue = GetChannelWriteValue(runtimeChannel);
        var currentMask = TryReadUnsignedInteger(currentWriteValue, out var currentValue) ? currentValue : 0u;
        var nextMask = enabled
            ? currentMask | (1u << bitIndex)
            : currentMask & ~(1u << bitIndex);
        if (nextMask != currentMask)
        {
            SetChannelWriteValue(runtimeChannel, ConvertMaskValue(currentWriteValue, nextMask));
        }

        SynchronizeBitValues(runtime, definition, definitions);
        return true;
    }

    internal static bool ApplyChannelValueUpdate(
        UdlClientRuntime runtime,
        UdlClientDefinition definition,
        string moduleName,
        string channelName,
        string? parameterName,
        ItemModel changedItem)
        => ApplyChannelValueUpdate(runtime, definition, UdlModuleExposureDefinitionCodec.ParseDefinitions(definition.UdlModuleExposureDefinitions), moduleName, channelName, parameterName, changedItem);

    internal static bool ApplyChannelValueUpdate(
        UdlClientRuntime runtime,
        UdlClientDefinition definition,
        IReadOnlyList<UdlModuleExposureDefinition> definitions,
        string moduleName,
        string channelName,
        string? parameterName,
        ItemModel changedItem)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(changedItem);

        if (!TryResolveRuntimeChannel(runtime, new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = channelName }, out var runtimeChannel)
            || runtimeChannel is null)
        {
            return false;
        }

        var changed = ApplyChannelValue(runtimeChannel, channelName, parameterName, changedItem);
        SynchronizeBitValues(runtime, definition, definitions);
        return changed;
    }

    internal static void SynchronizeBitValues(UdlClientRuntime runtime, UdlClientDefinition definition)
        => SynchronizeBitValues(runtime, definition, UdlModuleExposureDefinitionCodec.ParseDefinitions(definition.UdlModuleExposureDefinitions));

    internal static void SynchronizeBitValues(UdlClientRuntime runtime, UdlClientDefinition definition, IReadOnlyList<UdlModuleExposureDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definitions);

        if (!HasActiveBitExposures(definitions))
        {
            return;
        }

        var startTimestamp = Stopwatch.GetTimestamp();

        foreach (var exposureDefinition in definitions)
        {
            if (!exposureDefinition.ExposeBits
                || !TryResolveRuntimeChannel(runtime, exposureDefinition, out var runtimeChannel)
                || runtimeChannel is null
                || !runtimeChannel.Has("bits"))
            {
                continue;
            }

            var bitCount = ResolveBitCount(exposureDefinition, runtimeChannel);
            if (bitCount <= 0)
            {
                continue;
            }

            SynchronizeRuntimeExposureBitValues(runtime, runtimeChannel, exposureDefinition, bitCount);
        }

        _ = startTimestamp;
    }

    private static bool TryResolveRuntimeChannel(UdlClientRuntime runtime, UdlModuleExposureDefinition definition, out ItemModel? runtimeChannel)
    {
        var expectedRelativePath = $"{definition.ModuleName}.{NormalizeRuntimeChannelName(definition.ChannelName)}";
        return runtime.TryResolveRuntimeItem(expectedRelativePath, out runtimeChannel);
    }

    private static bool IsRuntimeChannelItem(ItemModel item)
        => EnhancedSignalPathHelper.SplitPathSegments(UdlPathHelper.GetRelativeRuntimePath(item.Path)).Count == 2;

    private static void UpsertRuntimeExposureBits(UdlClientRuntime runtime, ItemModel runtimeChannel, UdlModuleExposureDefinition definition, int bitCount)
    {
        if (!runtimeChannel.Has("bits"))
        {
            runtimeChannel["bits"] = new ItemModel("bits", path: runtimeChannel.Path);
        }

        var bitsRoot = runtimeChannel["bits"];
        SetPropertyValueIfDifferent(bitsRoot, "kind", "Group");
        SetPropertyValueIfDifferent(bitsRoot, "title", $"{definition.ModuleName}.{definition.ChannelName} Bits");

        var writeTargetChannel = ResolveExposureWriteTargetChannel(runtime, runtimeChannel, definition);
        var writable = writeTargetChannel.Properties.Has("write")
            || !writeTargetChannel.Properties.Has("writable")
            || TryReadBool(writeTargetChannel.Properties["writable"].Value, false);
        var labels = ParseBitLabels(definition.BitLabels);
        var desiredBitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
        {
            var bitName = $"bit{bitIndex}";
            desiredBitNames.Add(bitName);

            if (!bitsRoot.Has(bitName))
            {
                bitsRoot[bitName] = new ItemModel(bitName, path: bitsRoot.Path);
            }

            var bitItem = bitsRoot[bitName];
            var label = labels.TryGetValue(bitIndex, out var customLabel) ? customLabel : $"Bit {bitIndex}";

            SetPropertyValueIfDifferent(bitItem, "kind", "Bool");
            SetPropertyValueIfDifferent(bitItem, "format", "bool");
            SetPropertyValueIfDifferent(bitItem, "title", label);
            SetPropertyValueIfDifferent(bitItem, "text", label);
            SetPropertyValueIfDifferent(bitItem, "module_name", definition.ModuleName);
            SetPropertyValueIfDifferent(bitItem, "channel_name", definition.ChannelName);
            SetPropertyValueIfDifferent(bitItem, "bit_index", bitIndex);
            SetPropertyValueIfDifferent(bitItem, "source_path", runtimeChannel.Path ?? string.Empty);
            SetPropertyValueIfDifferent(bitItem, "writable", writable);
            SetPropertyValueIfDifferent(bitItem, "write_path", string.Empty);
            SetPropertyValueIfDifferent(bitItem, "write_mode", string.Empty);
        }

        foreach (var staleBitName in bitsRoot.GetDictionary().Keys.Except(desiredBitNames, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            bitsRoot.Remove(staleBitName);
        }

        SynchronizeRuntimeExposureBitValues(runtime, runtimeChannel, definition, bitCount);
    }

    private static bool ApplyChannelValue(ItemModel runtimeChannel, string channelName, string? parameterName, ItemModel changedItem)
    {
        if (string.Equals(parameterName, "value", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parameterName))
        {
            var changed = SetItemValueIfDifferent(runtimeChannel, changedItem.Value);
            if (string.Equals(channelName, "set", StringComparison.OrdinalIgnoreCase) && runtimeChannel.Properties.Has("write"))
            {
                changed |= SetPropertyValueIfDifferent(runtimeChannel, "write", changedItem.Value);
            }

            if (string.IsNullOrWhiteSpace(parameterName))
            {
                changed |= SynchronizeChannelProperty(runtimeChannel, changedItem, "read");
                changed |= SynchronizeChannelProperty(runtimeChannel, changedItem, "write");
            }

            return changed;
        }

        if (!changedItem.Properties.Has(parameterName))
        {
            return false;
        }

        return SetPropertyValueIfDifferent(runtimeChannel, parameterName, changedItem.Properties[parameterName].Value);
    }

    private static bool SynchronizeChannelProperty(ItemModel runtimeChannel, ItemModel changedItem, string propertyName)
    {
        if (!runtimeChannel.Properties.Has(propertyName) || !changedItem.Properties.Has(propertyName))
        {
            return false;
        }

        return SetPropertyValueIfDifferent(runtimeChannel, propertyName, changedItem.Properties[propertyName].Value);
    }

    private static void SynchronizeRuntimeExposureBitValues(UdlClientRuntime runtime, ItemModel runtimeChannel, UdlModuleExposureDefinition definition, int bitCount)
    {
        if (!runtimeChannel.Has("bits"))
        {
            return;
        }

        var bitsRoot = runtimeChannel["bits"];
        var writeTargetChannel = ResolveExposureWriteTargetChannel(runtime, runtimeChannel, definition);
        var sourceValue = ResolveExposureBitValueSourceValue(runtimeChannel, definition, writeTargetChannel);
        var rawValue = TryReadUnsignedInteger(sourceValue, out var currentValue) ? currentValue : 0u;

        for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
        {
            var bitName = $"bit{bitIndex}";
            if (!bitsRoot.Has(bitName))
            {
                continue;
            }

            var bitValue = ((rawValue >> bitIndex) & 1u) == 1u;
            SetItemValueIfDifferent(bitsRoot[bitName], bitValue);
        }
    }

    private static ItemModel ResolveExposureWriteTargetChannel(UdlClientRuntime runtime, ItemModel runtimeChannel, UdlModuleExposureDefinition definition)
    {
        if (!definition.RouteReadInputToSetRequest
            || !string.Equals(definition.ChannelName, "read", StringComparison.OrdinalIgnoreCase)
            || !TryResolveRuntimeChannel(runtime, new UdlModuleExposureDefinition { ModuleName = definition.ModuleName, ChannelName = "set" }, out var setChannel)
            || setChannel is null)
        {
            return runtimeChannel;
        }

        return setChannel;
    }

    private static object? ResolveExposureBitValueSourceValue(ItemModel runtimeChannel, UdlModuleExposureDefinition definition, ItemModel writeTargetChannel)
    {
        if (string.Equals(definition.ChannelName, "set", StringComparison.OrdinalIgnoreCase))
        {
            return GetChannelWriteValue(runtimeChannel);
        }

        if (definition.RouteReadInputToSetRequest && string.Equals(definition.ChannelName, "read", StringComparison.OrdinalIgnoreCase))
        {
            return GetChannelWriteValue(writeTargetChannel);
        }

        return GetChannelReadValue(runtimeChannel);
    }

    private static object? GetChannelReadValue(ItemModel runtimeChannel)
        => runtimeChannel.Properties.Has("read")
            ? runtimeChannel.Properties["read"].Value
            : null;

    private static object? GetChannelWriteValue(ItemModel runtimeChannel)
        => runtimeChannel.Properties.Has("write")
            ? runtimeChannel.Properties["write"].Value
            : null;

    private static void SetChannelWriteValue(ItemModel runtimeChannel, object? value)
    {
        if (runtimeChannel.Properties.Has("write"))
        {
            runtimeChannel.Properties["write"].Value = value!;
        }
    }

    private static int ResolveBitCount(UdlModuleExposureDefinition definition, ItemModel runtimeChannel)
    {
        if (definition.BitCount > 0)
        {
            return Math.Clamp(definition.BitCount, 1, 32);
        }

        if (!string.IsNullOrWhiteSpace(definition.Format))
        {
            var definitionBitCount = GetBitCount(definition.Format);
            if (definitionBitCount > 0)
            {
                return definitionBitCount;
            }
        }

        var runtimeFormat = runtimeChannel.Properties.Has("format")
            ? runtimeChannel.Properties["format"].Value?.ToString() ?? string.Empty
            : string.Empty;
        return GetBitCount(runtimeFormat);
    }

    private static bool RemoveRuntimeExposureBits(ItemModel runtimeChannel)
    {
        if (!runtimeChannel.Has("bits"))
        {
            return false;
        }

        runtimeChannel.Remove("bits");
        return true;
    }

    private static bool SetItemValueIfDifferent(ItemModel item, object? value)
    {
        if (ValuesEqual(item.Value, value))
        {
            return false;
        }

        item.Value = value!;
        return true;
    }

    private static bool SetPropertyValueIfDifferent(ItemModel item, string parameterName, object? value)
    {
        var parameter = item.Properties[parameterName];
        if (ValuesEqual(parameter.Value, value))
        {
            return false;
        }

        parameter.Value = value!;
        return true;
    }

    private static object ConvertMaskValue(object? existingValue, uint mask)
    {
        return existingValue switch
        {
            byte => (byte)mask,
            sbyte => unchecked((sbyte)mask),
            short => (short)mask,
            ushort => (ushort)mask,
            int => unchecked((int)mask),
            long => (long)mask,
            ulong => (ulong)mask,
            float => (float)mask,
            double => (double)mask,
            decimal => (decimal)mask,
            _ => unchecked((int)mask)
        };
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is double leftDouble && right is double rightDouble)
        {
            return leftDouble.Equals(rightDouble) || (double.IsNaN(leftDouble) && double.IsNaN(rightDouble));
        }

        if (left is float leftFloat && right is float rightFloat)
        {
            return leftFloat.Equals(rightFloat) || (float.IsNaN(leftFloat) && float.IsNaN(rightFloat));
        }

        return Equals(left, right);
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

    private static string ResolveEffectiveWriteChannelName(UdlClientRuntime runtime, IReadOnlyList<UdlModuleExposureDefinition> definitions, string moduleName, string channelName)
    {
        if (!string.Equals(channelName, "read", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRuntimeChannelName(channelName);
        }

        var routeDefinition = definitions.FirstOrDefault(candidate => string.Equals(candidate.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(candidate.ChannelName, channelName, StringComparison.OrdinalIgnoreCase));
        if (routeDefinition?.RouteReadInputToSetRequest != true)
        {
            return NormalizeRuntimeChannelName(channelName);
        }

        return TryResolveRuntimeChannel(runtime, new UdlModuleExposureDefinition { ModuleName = moduleName, ChannelName = "set" }, out _)
            ? "set"
            : NormalizeRuntimeChannelName(channelName);
    }

    private static bool TryReadUnsignedInteger(object? value, out uint parsed)
    {
        switch (value)
        {
            case byte byteValue:
                parsed = byteValue;
                return true;
            case sbyte sbyteValue:
                parsed = unchecked((uint)sbyteValue);
                return true;
            case short shortValue:
                parsed = unchecked((uint)shortValue);
                return true;
            case ushort ushortValue:
                parsed = ushortValue;
                return true;
            case int intValue:
                parsed = unchecked((uint)intValue);
                return true;
            case uint uintValue:
                parsed = uintValue;
                return true;
            case long longValue:
                parsed = unchecked((uint)longValue);
                return true;
            case float floatValue when floatValue >= 0f && floatValue <= uint.MaxValue:
                parsed = unchecked((uint)Math.Round(floatValue, MidpointRounding.AwayFromZero));
                return true;
            case double doubleValue when doubleValue >= 0d && doubleValue <= uint.MaxValue:
                parsed = unchecked((uint)Math.Round(doubleValue, MidpointRounding.AwayFromZero));
                return true;
            case decimal decimalValue when decimalValue >= 0m && decimalValue <= uint.MaxValue:
                parsed = unchecked((uint)Math.Round(decimalValue, MidpointRounding.AwayFromZero));
                return true;
            case ulong ulongValue:
                parsed = unchecked((uint)ulongValue);
                return true;
            case bool boolValue:
                parsed = boolValue ? 1u : 0u;
                return true;
            case string text when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue):
                parsed = stringValue;
                return true;
            default:
                parsed = 0;
                return false;
        }
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

    private static Dictionary<int, string> ParseBitLabels(string? rawLabels)
    {
        var labels = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(rawLabels))
        {
            return labels;
        }

        var lines = rawLabels
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!key.StartsWith("Bit", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(key[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitIndex)
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            labels[bitIndex] = value;
        }

        return labels;
    }

    private static string NormalizeRuntimeChannelName(string? channelName)
        => channelName?.Trim().ToLowerInvariant() ?? string.Empty;
}
