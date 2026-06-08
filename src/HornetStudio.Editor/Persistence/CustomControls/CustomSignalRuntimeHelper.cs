using System;
using System.Globalization;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Persistence.CustomControls;

internal static class CustomSignalRuntimeHelper
{
    public static string BuildRegistryPath(FolderItemModel ownerItem, CustomSignalDefinition definition)
        => BuildRegistryPath(ownerItem.FolderName, definition);

    public static string BuildRegistryPath(string? folderName, CustomSignalDefinition definition)
    {
        var normalizedFolder = string.IsNullOrWhiteSpace(folderName)
            ? "folder"
            : TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        var signalName = TargetPathHelper.NormalizePathSegment(definition.Name, "signal");
        return $"studio.{normalizedFolder}.signals.custom.{signalName}";
    }

    public static string BuildManualTriggerPath(FolderItemModel ownerItem, CustomSignalDefinition definition)
        => BuildManualTriggerPath(BuildRegistryPath(ownerItem, definition));

    public static string BuildManualTriggerPath(string registryPath)
        => $"{registryPath}.trigger";

    public static object? ParseLiteral(string? valueText, CustomSignalDataType dataType)
    {
        return dataType switch
        {
            CustomSignalDataType.Boolean => ToBool(valueText),
            CustomSignalDataType.Number => ToNullableDouble(valueText) ?? 0d,
            _ => valueText ?? string.Empty
        };
    }

    public static object? ConvertToDataType(object? value, CustomSignalDataType dataType)
    {
        return dataType switch
        {
            CustomSignalDataType.Boolean => ToBool(value),
            CustomSignalDataType.Number => ToDouble(value),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public static double ToDouble(object? value)
        => ToNullableDouble(value) ?? 0d;

    public static double? ToNullableDouble(object? value)
    {
        return value switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            bool boolValue => boolValue ? 1d : 0d,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) => parsed,
            IConvertible convertible => TryConvertToDouble(convertible),
            _ => null
        };
    }

    public static bool ToBool(object? value)
    {
        return value switch
        {
            null => false,
            bool boolValue => boolValue,
            string text when string.IsNullOrWhiteSpace(text) => false,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedNumber) => Math.Abs(parsedNumber) > double.Epsilon,
            IConvertible convertible => Math.Abs(TryConvertToDouble(convertible) ?? 0d) > double.Epsilon,
            _ => false
        };
    }

    private static double? TryConvertToDouble(IConvertible convertible)
    {
        try
        {
            return convertible.ToDouble(CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
