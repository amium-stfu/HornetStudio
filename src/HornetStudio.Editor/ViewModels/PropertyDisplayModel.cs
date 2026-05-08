using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using ItemModel = Amium.Items.Item;
using Amium.Items;

namespace HornetStudio.Editor.ViewModels;

public sealed class PropertyDisplayModel
{
    private const string DefaultEpochDateTimePattern = "yyyy-MM-dd'T'HH:mm:sszzz";

    public static PropertyDisplayModel Empty { get; } = new(property: null, label: string.Empty, format: string.Empty, unitText: string.Empty, fallbackText: string.Empty);

    public PropertyDisplayModel(ItemProperty? property, string label, string format, string unitText, string fallbackText)
    {
        Property = property;
        Label = label;
        Format = format;
        UnitText = unitText;
        FallbackText = fallbackText;
        Definition = PropertyFormatDefinition.Parse(format, property?.Value);
        ValueText = BuildValueText();
        BoolOptions = BuildBoolOptions();
        BitOptions = BuildBitOptions();
        ColorText = BuildColorText();
        ColorBrush = BuildColorBrush();
    }

    public ItemProperty? Property { get; }

    public string Label { get; }

    public string Format { get; }

    public string UnitText { get; }

    public string FallbackText { get; }

    public PropertyFormatDefinition Definition { get; }

    public string ValueText { get; }

    public string ColorText { get; }

    public IBrush ColorBrush { get; }

    public IReadOnlyList<PropertyChoiceState> BoolOptions { get; }

    public IReadOnlyList<PropertyChoiceState> BitOptions { get; }

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    public bool IsColor => Definition.Kind == PropertyVisualKind.Color;

    public bool IsBool => Definition.Kind == PropertyVisualKind.Bool;

    public bool IsBits => Definition.Kind == PropertyVisualKind.Bits;

    public bool IsBoolToggle => IsBool && Definition.UseSingleToggle;

    public bool IsText => !IsColor && !IsBool && !IsBits;

    public bool ShowUnit => IsText && Definition.Kind != PropertyVisualKind.Text && !string.IsNullOrWhiteSpace(UnitText);

    private string BuildValueText()
    {
        var value = Property?.Value;
        if (value is null)
        {
            // Wenn kein Parameterwert verfuegbar ist, aber ein numerisches Format
            // konfiguriert wurde, versuchen wir, den Fallback-Text als Zahl zu
            // interpretieren und entsprechend zu formatieren (z.B. fuer Script-Werte).
            if (Definition.Kind == PropertyVisualKind.Numeric
                && !string.IsNullOrWhiteSpace(FallbackText)
                && double.TryParse(FallbackText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numericFallback))
            {
                var formatPattern = string.IsNullOrWhiteSpace(Definition.PatternOrOptionsText) ? "0.##" : Definition.PatternOrOptionsText;
                return numericFallback.ToString(formatPattern, CultureInfo.InvariantCulture) ?? FallbackText;
            }

            if (Definition.Kind == PropertyVisualKind.EpochDateTime)
            {
                return TryFormatEpochDateTime(FallbackText, out var formattedFallback)
                    ? formattedFallback
                    : FallbackText;
            }

            if (Definition.Kind == PropertyVisualKind.Hex
                && !string.IsNullOrWhiteSpace(FallbackText))
            {
                var fallbackNumber = ToUInt64(FallbackText);
                var fallbackHexValue = Definition.PatternOrOptionsText.Length > 0
                    ? fallbackNumber.ToString($"X{Definition.PatternOrOptionsText}", CultureInfo.InvariantCulture)
                    : fallbackNumber.ToString("X", CultureInfo.InvariantCulture);
                return $"0x{fallbackHexValue}";
            }

            return FallbackText;
        }

        if (Definition.Kind == PropertyVisualKind.Hex)
        {
            var number = ToUInt64(value);
            var hexValue = Definition.PatternOrOptionsText.Length > 0
                ? number.ToString($"X{Definition.PatternOrOptionsText}", CultureInfo.InvariantCulture)
                : number.ToString("X", CultureInfo.InvariantCulture);
            return $"0x{hexValue}";
        }

        if (Definition.Kind == PropertyVisualKind.Numeric && value is IFormattable formattable)
        {
            var formatPattern = string.IsNullOrWhiteSpace(Definition.PatternOrOptionsText) ? "0.##" : Definition.PatternOrOptionsText;
            return formattable.ToString(formatPattern, CultureInfo.InvariantCulture) ?? FallbackText;
        }

        if (Definition.Kind == PropertyVisualKind.EpochDateTime)
        {
            string formattedDateTime;
            return TryFormatEpochDateTime(value, out formattedDateTime)
                ? formattedDateTime
                : value.ToString() ?? FallbackText;
        }

        if (value is float f)
        {
            return f.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (value is double d)
        {
            return d.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? FallbackText;
    }

    private IReadOnlyList<PropertyChoiceState> BuildBoolOptions()
    {
        if (Definition.Kind != PropertyVisualKind.Bool)
        {
            return [];
        }

        var effectiveValue = Property?.Value ?? FallbackText;
        var boolValue = ToBool(effectiveValue);
        var trueLabel = Definition.Options.Count > 0 ? Definition.Options[0] : "True";
        var falseLabel = Definition.Options.Count > 1 ? Definition.Options[1] : "False";

        if (Definition.UseSingleToggle)
        {
            var activeLabel = boolValue ? trueLabel : falseLabel;
            return
            [
                CreateChoice(activeLabel, boolValue, boolValue ? 0 : 1, boolValue ? falseLabel : trueLabel)
            ];
        }

        return
        [
            CreateChoice(trueLabel, boolValue, 1),
            CreateChoice(falseLabel, !boolValue, 0)
        ];
    }

    private IReadOnlyList<PropertyChoiceState> BuildBitOptions()
    {
        if (Definition.Kind != PropertyVisualKind.Bits)
        {
            return [];
        }

        var effectiveValue = Property?.Value ?? FallbackText;
        var raw = ToUInt64(effectiveValue);
        var options = new List<PropertyChoiceState>();
        for (var index = Definition.BitCount - 1; index >= 0; index--)
        {
            var label = index < Definition.Options.Count && !string.IsNullOrWhiteSpace(Definition.Options[index])
                ? Definition.Options[index]
                : (index + 1).ToString(CultureInfo.InvariantCulture);
            var isActive = ((raw >> index) & 1UL) == 1UL;
            options.Add(CreateChoice(label, isActive, index));
        }

        return options;
    }

    private string BuildColorText()
    {
        var value = Property?.Value;
        if (Definition.Kind != PropertyVisualKind.Color || value is null)
        {
            return string.Empty;
        }

        if (value is Color color)
        {
            return ToHex(color);
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Color.TryParse(text, out Color parsedColor) ? ToHex(parsedColor) : text;
    }

    private IBrush BuildColorBrush()
    {
        if (Definition.Kind != PropertyVisualKind.Color)
        {
            return Brushes.Transparent;
        }

        if (Property?.Value is Color color)
        {
            return new SolidColorBrush(color);
        }

        if (Color.TryParse(ColorText, out var parsedColor))
        {
            return new SolidColorBrush(parsedColor);
        }

        return Brushes.Transparent;
    }

    private static PropertyChoiceState CreateChoice(string label, bool isActive, int index = -1, string? alternateLabel = null)
    {
        return new PropertyChoiceState(
            label,
            isActive,
            isActive ? "#F59E0B" : "#F8FAFC",
            isActive ? "#FFFFFF" : "#111827",
            isActive ? "#D97706" : "#CBD5E1",
            index,
            alternateLabel);
    }

    private bool TryFormatEpochDateTime(object? value, out string formatted)
    {
        formatted = string.Empty;
        if (!TryGetEpochMilliseconds(value, out var epochMilliseconds))
        {
            return false;
        }

        try
        {
            var pattern = string.IsNullOrWhiteSpace(Definition.PatternOrOptionsText)
                ? DefaultEpochDateTimePattern
                : Definition.PatternOrOptionsText;
            var dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds).ToLocalTime();
            formatted = dateTimeOffset.ToString(pattern, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetEpochMilliseconds(object? value, out long epochMilliseconds)
    {
        epochMilliseconds = 0;
        switch (value)
        {
            case null:
                return false;
            case DateTime dateTime:
                epochMilliseconds = new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
                return true;
            case DateTimeOffset dateTimeOffset:
                epochMilliseconds = dateTimeOffset.ToUnixTimeMilliseconds();
                return true;
            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTextLong):
                epochMilliseconds = NormalizeEpochMilliseconds(parsedTextLong);
                return true;
            case string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedTextDouble):
                epochMilliseconds = NormalizeEpochMilliseconds((long)System.Math.Round(parsedTextDouble, MidpointRounding.AwayFromZero));
                return true;
            case byte byteValue:
                epochMilliseconds = NormalizeEpochMilliseconds(byteValue);
                return true;
            case sbyte sbyteValue:
                epochMilliseconds = NormalizeEpochMilliseconds(sbyteValue);
                return true;
            case short shortValue:
                epochMilliseconds = NormalizeEpochMilliseconds(shortValue);
                return true;
            case ushort ushortValue:
                epochMilliseconds = NormalizeEpochMilliseconds(ushortValue);
                return true;
            case int intValue:
                epochMilliseconds = NormalizeEpochMilliseconds(intValue);
                return true;
            case uint uintValue:
                epochMilliseconds = NormalizeEpochMilliseconds((long)uintValue);
                return true;
            case long longValue:
                epochMilliseconds = NormalizeEpochMilliseconds(longValue);
                return true;
            case ulong ulongValue when ulongValue <= long.MaxValue:
                epochMilliseconds = NormalizeEpochMilliseconds((long)ulongValue);
                return true;
            case float floatValue:
                epochMilliseconds = NormalizeEpochMilliseconds((long)System.Math.Round(floatValue, MidpointRounding.AwayFromZero));
                return true;
            case double doubleValue:
                epochMilliseconds = NormalizeEpochMilliseconds((long)System.Math.Round(doubleValue, MidpointRounding.AwayFromZero));
                return true;
            case decimal decimalValue:
                epochMilliseconds = NormalizeEpochMilliseconds((long)System.Math.Round(decimalValue, MidpointRounding.AwayFromZero));
                return true;
            default:
                return false;
        }
    }

    private static long NormalizeEpochMilliseconds(long rawValue)
    {
        var absolute = System.Math.Abs(rawValue);
        return absolute > 0 && absolute < 100_000_000_000L
            ? rawValue * 1000L
            : rawValue;
    }

    private static bool ToBool(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            byte byteValue => byteValue != 0,
            short shortValue => shortValue != 0,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }

    private static ulong ToUInt64(object? value)
    {
        return value switch
        {
            byte byteValue => byteValue,
            sbyte sbyteValue => unchecked((ulong)sbyteValue),
            short shortValue => unchecked((ulong)shortValue),
            ushort ushortValue => ushortValue,
            int intValue => unchecked((ulong)intValue),
            uint uintValue => uintValue,
            long longValue => unchecked((ulong)longValue),
            ulong ulongValue => ulongValue,
            float floatValue => unchecked((ulong)floatValue),
            double doubleValue => unchecked((ulong)doubleValue),
            decimal decimalValue => unchecked((ulong)decimalValue),
            string text when ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0UL
        };
    }

    private static string ToHex(Color color)
        => color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}

public sealed class PropertyChoiceState
{
    public PropertyChoiceState(string label, bool isActive, string background, string foreground, string borderBrush, int index = -1, string? alternateLabel = null)
    {
        Label = label;
        IsActive = isActive;
        Background = background;
        Foreground = foreground;
        BorderBrush = borderBrush;
        Index = index;
        AlternateLabel = alternateLabel ?? string.Empty;
    }

    public string Label { get; }

    public bool IsActive { get; }

    public string Background { get; }

    public string Foreground { get; }

    public string BorderBrush { get; }

    public int Index { get; }

    public string AlternateLabel { get; }
}

public sealed class PropertyFormatDefinition
{
    private const string BoolToggleOptionMarker = "toggle";
    private const string DefaultEpochDateTimePattern = "yyyy-MM-dd'T'HH:mm:sszzz";

    private PropertyFormatDefinition(PropertyVisualKind kind, int precision = 0, int bitCount = 0, string? patternOrOptionsText = null, IReadOnlyList<string>? options = null, bool useSingleToggle = false)
    {
        Kind = kind;
        Precision = precision;
        BitCount = bitCount;
        PatternOrOptionsText = patternOrOptionsText ?? string.Empty;
        Options = options ?? [];
        UseSingleToggle = useSingleToggle;
    }

    public PropertyVisualKind Kind { get; }

    public int Precision { get; }

    public int BitCount { get; }

    public string PatternOrOptionsText { get; }

    public IReadOnlyList<string> Options { get; }

    public bool UseSingleToggle { get; }

    public static PropertyFormatDefinition Parse(string? format, object? value)
    {
        if (value is Color)
        {
            return new PropertyFormatDefinition(PropertyVisualKind.Color);
        }

        if (value is string textValue && Color.TryParse(textValue, out Color _))
        {
            return new PropertyFormatDefinition(PropertyVisualKind.Color);
        }

        if (!string.IsNullOrWhiteSpace(format))
        {
            var trimmed = format.Trim();
            if (trimmed.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Bool, options: ["True", "False"]);
            }

            if (trimmed.StartsWith("bool:", StringComparison.OrdinalIgnoreCase))
            {
                var boolParameter = trimmed[5..].Trim();
                if (boolParameter.Contains(BoolToggleOptionMarker, StringComparison.OrdinalIgnoreCase))
                {
                    var toggleIndex = boolParameter.IndexOf(BoolToggleOptionMarker, StringComparison.OrdinalIgnoreCase);
                    var toggleOptions = toggleIndex >= 0
                        ? boolParameter[(toggleIndex + BoolToggleOptionMarker.Length)..].TrimStart(':', ' ')
                        : string.Empty;
                    return new PropertyFormatDefinition(
                        PropertyVisualKind.Bool,
                        options: SplitOptions(toggleOptions),
                        useSingleToggle: true);
                }

                return new PropertyFormatDefinition(PropertyVisualKind.Bool, options: SplitOptions(boolParameter));
            }

            if (trimmed.StartsWith("b", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 2 && char.IsDigit(trimmed[1]))
            {
                var parts = trimmed.Split(':', 2, StringSplitOptions.TrimEntries);
                var countText = new string(parts[0].Skip(1).ToArray());
                var count = int.TryParse(countText, out var parsedCount) ? parsedCount : 8;
                var options = parts.Length > 1 ? SplitOptions(parts[1]) : [];
                return new PropertyFormatDefinition(PropertyVisualKind.Bits, bitCount: count, options: options);
            }

            if (trimmed.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Hex, patternOrOptionsText: trimmed[4..].Trim());
            }

            if (string.Equals(trimmed, "hex", StringComparison.OrdinalIgnoreCase))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Hex);
            }

            if (trimmed.StartsWith("numeric:", StringComparison.OrdinalIgnoreCase))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Numeric, patternOrOptionsText: trimmed[8..].Trim());
            }

            if (string.Equals(trimmed, "numeric", StringComparison.OrdinalIgnoreCase))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Numeric, patternOrOptionsText: "0.##");
            }

            if (trimmed.Equals("EpochToDatetime", StringComparison.OrdinalIgnoreCase))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.EpochDateTime, patternOrOptionsText: DefaultEpochDateTimePattern);
            }

            if (trimmed.StartsWith("EpochToDatetime:", StringComparison.OrdinalIgnoreCase))
            {
                var epochParameter = trimmed[16..].Trim();
                return new PropertyFormatDefinition(
                    PropertyVisualKind.EpochDateTime,
                    patternOrOptionsText: string.IsNullOrWhiteSpace(epochParameter) || string.Equals(epochParameter, "UtcDefault", StringComparison.OrdinalIgnoreCase)
                        ? DefaultEpochDateTimePattern
                        : epochParameter);
            }

            if (trimmed.StartsWith("h", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 2 && int.TryParse(trimmed[1..], out var hexPrecision))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Hex, patternOrOptionsText: hexPrecision.ToString(CultureInfo.InvariantCulture));
            }

            if (trimmed.StartsWith("D", StringComparison.Ordinal) && trimmed.Length >= 2 && int.TryParse(trimmed[1..], out var decPrecision))
            {
                return new PropertyFormatDefinition(PropertyVisualKind.Numeric, patternOrOptionsText: new string('0', decPrecision));
            }

            if (trimmed.StartsWith("F", StringComparison.Ordinal) && trimmed.Length >= 2 && int.TryParse(trimmed[1..], out var floatPrecision))
            {
                var decimals = new string('0', floatPrecision);
                var pattern = floatPrecision > 0 ? $"0.{decimals}" : "0";
                return new PropertyFormatDefinition(PropertyVisualKind.Numeric, patternOrOptionsText: pattern);
            }
        }

        if (value is bool)
        {
            return new PropertyFormatDefinition(PropertyVisualKind.Bool, options: ["True", "False"]);
        }

        return new PropertyFormatDefinition(PropertyVisualKind.Text);
    }

    private static IReadOnlyList<string> SplitOptions(string input)
        => input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public enum PropertyVisualKind
{
    Text,
    Numeric,
    Hex,
    EpochDateTime,
    Color,
    Bool,
    Bits
}
