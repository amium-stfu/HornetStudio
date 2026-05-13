using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HornetStudio.Editor.Models;

public enum ExtendedSignalFilterMode
{
    Raw,
    Average,
    Ema,
    Wma,
    EmaWma
}

public enum ExtendedSignalAdjustmentMode
{
    None,
    Linear,
    Spline
}

public enum KalmanDynamicNormalizationMode
{
    HybridReferenceFloor,
    PureResidual,
    AdaptiveResidualBlend
}

public sealed class ExtendedSignalSplinePoint
{
    public double Input { get; set; }

    public double Output { get; set; }
}

public enum ExtendedSignalSplineInterpolationMode
{
    Linear,
    CatmullRom
}

public sealed class ExtendedSignalAdjustmentDefinition
{
    public bool Enabled { get; set; }

    public ExtendedSignalAdjustmentMode MappingMode { get; set; } = ExtendedSignalAdjustmentMode.None;

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyMode { get; set; }

    public double Offset { get; set; }

    public double Gain { get; set; } = 1.0;

    public List<ExtendedSignalSplinePoint> SplinePoints { get; set; } = [];

    public ExtendedSignalSplineInterpolationMode SplineInterpolationMode { get; set; } = ExtendedSignalSplineInterpolationMode.Linear;

    public bool SupportsInverseMapping { get; set; } = true;

    public ExtendedSignalAdjustmentDefinition Clone()
    {
        return new ExtendedSignalAdjustmentDefinition
        {
            Enabled = Enabled,
            MappingMode = MappingMode,
            Offset = Offset,
            Gain = Gain,
            SplinePoints = SplinePoints
                .Select(static point => new ExtendedSignalSplinePoint { Input = point.Input, Output = point.Output })
                .ToList(),
            SplineInterpolationMode = SplineInterpolationMode,
            SupportsInverseMapping = SupportsInverseMapping
        };
    }

    public ExtendedSignalAdjustmentDefinition NormalizeLegacyFields()
    {
        if (!string.IsNullOrWhiteSpace(LegacyMode)
            && Enum.TryParse<ExtendedSignalAdjustmentMode>(LegacyMode, true, out var parsedMode))
        {
            MappingMode = parsedMode == ExtendedSignalAdjustmentMode.Linear
                ? ExtendedSignalAdjustmentMode.None
                : parsedMode;
        }
        else if (MappingMode == ExtendedSignalAdjustmentMode.Linear)
        {
            MappingMode = ExtendedSignalAdjustmentMode.None;
        }

        LegacyMode = null;
        return this;
    }
}

public sealed class ExtendedSignalPeakFilterDefinition
{
    public bool Enabled { get; set; }

    public double Threshold { get; set; } = 10.0;

    public int MaxLengthMs { get; set; } = 200;

    public ExtendedSignalPeakFilterDefinition Clone()
    {
        return new ExtendedSignalPeakFilterDefinition
        {
            Enabled = Enabled,
            Threshold = Threshold,
            MaxLengthMs = MaxLengthMs
        };
    }
}

public sealed class ExtendedSignalDynamicFilterDefinition
{
    public bool Enabled { get; set; }

    public int DetectionWindowMs { get; set; } = 1000;

    public double SlopeThreshold { get; set; } = 5.0;

    public double RelativeSlopeThresholdPercent { get; set; } = 0.0;

    public double DynamicAngleMaxDegrees { get; set; } = 45.0;

    public int DynamicFilterTimeMs { get; set; } = 1000;

    public int HoldTimeMs { get; set; } = 1500;

    public ExtendedSignalDynamicFilterDefinition Clone()
    {
        return new ExtendedSignalDynamicFilterDefinition
        {
            Enabled = Enabled,
            DetectionWindowMs = DetectionWindowMs,
            SlopeThreshold = SlopeThreshold,
            RelativeSlopeThresholdPercent = RelativeSlopeThresholdPercent,
            DynamicAngleMaxDegrees = DynamicAngleMaxDegrees,
            DynamicFilterTimeMs = DynamicFilterTimeMs,
            HoldTimeMs = HoldTimeMs
        };
    }
}

public sealed class ExtendedSignalStatisticsDefinition
{
    public bool Enabled { get; set; }

    public bool PublishMin { get; set; }

    public bool PublishMax { get; set; }

    public bool PublishAverage { get; set; }

    public bool PublishStdDev { get; set; }

    public bool PublishIntegral { get; set; }

    public int StdDevWindowMs { get; set; } = 10000;

    public double IntegralDivisorMs { get; set; } = 1.0;

    public ExtendedSignalStatisticsDefinition Clone()
    {
        return new ExtendedSignalStatisticsDefinition
        {
            Enabled = Enabled,
            PublishMin = PublishMin,
            PublishMax = PublishMax,
            PublishAverage = PublishAverage,
            PublishStdDev = PublishStdDev,
            PublishIntegral = PublishIntegral,
            StdDevWindowMs = StdDevWindowMs,
            IntegralDivisorMs = IntegralDivisorMs
        };
    }
}

public sealed class ExtendedSignalDefinition
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool IsWritable { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string WritePath { get; set; } = string.Empty;

    public SignalWriteMode WriteMode { get; set; } = SignalWriteMode.Direct;

    public bool ForwardChildWritesToSource { get; set; }

    [JsonPropertyName("sourceReadPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySourceReadPath { get; set; }

    [JsonPropertyName("sourceSetPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySourceSetPath { get; set; }

    [JsonPropertyName("sourceCommandPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySourceCommandPath { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public ExtendedSignalFilterMode FilterMode { get; set; } = ExtendedSignalFilterMode.Raw;

    public int ScanIntervalMs { get; set; } = 100;

    public int RecordInterval { get; set; } = 1;

    public int FilterTimeMs { get; set; }

    public bool FillMissingWithLastValue { get; set; } = true;

    public bool KalmanEnabled { get; set; }

    public double KalmanMeasurementNoiseR { get; set; } = 1.0;

    public double KalmanProcessNoiseQ { get; set; } = 0.05;

    public double KalmanInitialErrorCovarianceP { get; set; } = 1.0;

    public int KalmanTeachWindowMs { get; set; } = 5000;

    public bool KalmanTeachPauseOnDynamic { get; set; } = true;

    public double KalmanTeachQFactor { get; set; } = 0.05;

    public bool KalmanTeachAutoApply { get; set; } = true;

    public bool KalmanDynamicQEnabled { get; set; }

    public double KalmanDynamicQMin { get; set; } = 0.05;

    public double KalmanDynamicQMax { get; set; } = 0.5;

    public int KalmanDynamicQHoldMs { get; set; } = 250;

    public int KalmanDynamicDetectionWindowMs { get; set; } = 1000;

    public double KalmanDynamicAngleThresholdDeg { get; set; } = 5.0;

    public double KalmanDynamicAngleMaxDeg { get; set; } = 45.0;

    public double KalmanDynamicReferenceFloor { get; set; } = 1.0;

    public KalmanDynamicNormalizationMode KalmanDynamicNormalizationMode { get; set; } = KalmanDynamicNormalizationMode.HybridReferenceFloor;

    public double KalmanDynamicResidualWeight { get; set; } = 0.35;

    public bool KalmanDynamicQUseExistingDynamic { get; set; } = true;

    public ExtendedSignalAdjustmentDefinition Adjustment { get; set; } = new();

    [JsonPropertyName("correction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtendedSignalAdjustmentDefinition? LegacyCorrection { get; set; }

    public ExtendedSignalPeakFilterDefinition PeakFilter { get; set; } = new();

    public ExtendedSignalDynamicFilterDefinition DynamicFilter { get; set; } = new();

    public ExtendedSignalStatisticsDefinition Statistics { get; set; } = new();

    public ExtendedSignalDefinition Clone()
    {
        return new ExtendedSignalDefinition
        {
            Name = Name,
            Enabled = Enabled,
            IsWritable = IsWritable,
            SourcePath = SourcePath,
            WritePath = WritePath,
            WriteMode = WriteMode,
            ForwardChildWritesToSource = ForwardChildWritesToSource,
            Unit = Unit,
            Format = Format,
            FilterMode = FilterMode,
            ScanIntervalMs = ScanIntervalMs,
            RecordInterval = RecordInterval,
            FilterTimeMs = FilterTimeMs,
            FillMissingWithLastValue = FillMissingWithLastValue,
            KalmanEnabled = KalmanEnabled,
            KalmanMeasurementNoiseR = KalmanMeasurementNoiseR,
            KalmanProcessNoiseQ = KalmanProcessNoiseQ,
            KalmanInitialErrorCovarianceP = KalmanInitialErrorCovarianceP,
            KalmanTeachWindowMs = KalmanTeachWindowMs,
            KalmanTeachPauseOnDynamic = KalmanTeachPauseOnDynamic,
            KalmanTeachQFactor = KalmanTeachQFactor,
            KalmanTeachAutoApply = KalmanTeachAutoApply,
            KalmanDynamicQEnabled = KalmanDynamicQEnabled,
            KalmanDynamicQMin = KalmanDynamicQMin,
            KalmanDynamicQMax = KalmanDynamicQMax,
            KalmanDynamicQHoldMs = KalmanDynamicQHoldMs,
            KalmanDynamicDetectionWindowMs = KalmanDynamicDetectionWindowMs,
            KalmanDynamicAngleThresholdDeg = KalmanDynamicAngleThresholdDeg,
            KalmanDynamicAngleMaxDeg = KalmanDynamicAngleMaxDeg,
            KalmanDynamicReferenceFloor = KalmanDynamicReferenceFloor,
            KalmanDynamicNormalizationMode = KalmanDynamicNormalizationMode,
            KalmanDynamicResidualWeight = KalmanDynamicResidualWeight,
            KalmanDynamicQUseExistingDynamic = KalmanDynamicQUseExistingDynamic,
            Adjustment = Adjustment.Clone(),
            PeakFilter = PeakFilter.Clone(),
            DynamicFilter = DynamicFilter.Clone(),
            Statistics = Statistics.Clone()
        };
    }

    public ExtendedSignalDefinition NormalizeLegacyFields()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            SourcePath = LegacySourceReadPath
                ?? LegacySourceSetPath
                ?? LegacySourceCommandPath
                ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(WritePath))
        {
            WritePath = LegacySourceSetPath
                ?? LegacySourceCommandPath
                ?? string.Empty;
        }

        if (!IsWritable && !string.IsNullOrWhiteSpace(WritePath))
        {
            IsWritable = true;
        }

        WriteMode = WriteMode == SignalWriteMode.Request
            ? SignalWriteMode.Direct
            : WriteMode;

        Adjustment ??= new ExtendedSignalAdjustmentDefinition();
        Adjustment = Adjustment.NormalizeLegacyFields();

        if (Adjustment.MappingMode != ExtendedSignalAdjustmentMode.None
            && Adjustment.MappingMode != ExtendedSignalAdjustmentMode.Spline)
        {
            throw new InvalidOperationException("Polynomial adjustment is no longer supported.");
        }

        if (LegacyCorrection is not null)
        {
            var normalizedLegacyCorrection = LegacyCorrection.NormalizeLegacyFields();
            if (!Adjustment.Enabled
                && Adjustment.MappingMode == ExtendedSignalAdjustmentMode.None
                && Math.Abs(Adjustment.Offset) < double.Epsilon
                && Math.Abs(Adjustment.Gain - 1d) < double.Epsilon
                && Adjustment.SplinePoints.Count == 0)
            {
                Adjustment = normalizedLegacyCorrection;
            }
        }

        if (KalmanDynamicNormalizationMode != KalmanDynamicNormalizationMode.HybridReferenceFloor)
        {
            KalmanDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor;
        }

        LegacySourceReadPath = null;
        LegacySourceSetPath = null;
        LegacySourceCommandPath = null;
        LegacyCorrection = null;
        return this;
    }
}

public static class ExtendedSignalDefinitionJsonCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<ExtendedSignalDefinition> ParseDefinitions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<ExtendedSignalDefinition>();
        }

        EnsurePolynomialRemoved(raw);

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ExtendedSignalDefinition>>(raw, JsonOptions);
            return parsed?
                .Where(static definition => definition is not null)
                .Select(static definition => definition!.NormalizeLegacyFields())
                .ToArray()
                ?? Array.Empty<ExtendedSignalDefinition>();
        }
        catch
        {
            return Array.Empty<ExtendedSignalDefinition>();
        }
    }

    public static string SerializeDefinitions(IEnumerable<ExtendedSignalDefinition>? definitions)
    {
        var normalized = definitions?
            .Where(static definition => definition is not null)
            .Select(static definition => definition!)
            .Where(static definition => !string.IsNullOrWhiteSpace(definition.Name))
            .ToArray()
            ?? Array.Empty<ExtendedSignalDefinition>();

        return normalized.Length == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private static void EnsurePolynomialRemoved(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        if (ContainsPolynomial(document.RootElement))
        {
            throw new InvalidOperationException("Polynomial adjustment is no longer supported.");
        }
    }

    private static bool ContainsPolynomial(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (ContainsPolynomial(child))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("polynomialCoefficients"))
                    {
                        return true;
                    }

                    if ((property.NameEquals("mappingMode") || property.NameEquals("mode"))
                        && property.Value.ValueKind == JsonValueKind.String
                        && string.Equals(property.Value.GetString(), "Polynomial", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (ContainsPolynomial(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}