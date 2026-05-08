using System.Globalization;

namespace Amium.Item.Server;

/// <summary>
/// Provides value coercion helpers for broker writes into existing typed item values.
/// </summary>
public static class ItemServerValueCoercion
{
    /// <summary>
    /// Tries to coerce an incoming value to the existing target value runtime type.
    /// </summary>
    /// <param name="value">The incoming value.</param>
    /// <param name="existingValue">The existing target value.</param>
    /// <param name="convertedValue">The converted value when conversion succeeds.</param>
    /// <returns><see langword="true"/> when the value can be assigned; otherwise, <see langword="false"/>.</returns>
    public static bool TryConvertForExistingValue(object? value, object? existingValue, out object? convertedValue)
    {
        convertedValue = value;
        if (value is null || existingValue is null)
        {
            return true;
        }

        var targetType = existingValue.GetType();
        var valueType = value.GetType();
        if (targetType == valueType)
        {
            return true;
        }

        try
        {
            if (targetType == typeof(string))
            {
                convertedValue = Convert.ToString(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (targetType.IsEnum)
            {
                convertedValue = value is string text
                    ? Enum.Parse(targetType, text, ignoreCase: true)
                    : Enum.ToObject(targetType, value);
                return true;
            }

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
            {
                convertedValue = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            convertedValue = value;
            return false;
        }

        return false;
    }
}