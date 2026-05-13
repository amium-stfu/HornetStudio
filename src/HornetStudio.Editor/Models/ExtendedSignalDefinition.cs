using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HornetStudio.Editor.Helpers;

namespace HornetStudio.Editor.Models;

public sealed class ExtendedSignalAdjustmentDocument
{
    public bool Enabled { get; init; }

    public ExtendedSignalAdjustmentMode MappingMode { get; init; } = ExtendedSignalAdjustmentMode.None;

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyMode { get; init; }

    public double Offset { get; init; }

    public double Gain { get; init; } = 1.0;

    public List<ExtendedSignalSplinePoint> SplinePoints { get; init; } = [];

    public ExtendedSignalSplineInterpolationMode SplineInterpolationMode { get; init; } = ExtendedSignalSplineInterpolationMode.Linear;

    public bool SupportsInverseMapping { get; init; } = true;
}

public sealed class ExtendedSignalStatisticsDocument
{
    public bool Enabled { get; init; }

    public bool PublishMin { get; init; }

    public bool PublishMax { get; init; }

    public bool PublishAverage { get; init; }

    public bool PublishStdDev { get; init; }

    public bool PublishIntegral { get; init; }

    public int StdDevWindowMs { get; init; } = 10000;

    public double IntegralDivisorMs { get; init; } = 1.0;

    [JsonPropertyName("integralFactor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyIntegralFactor { get; init; }
}

public sealed class ExtendedSignalPeakFilterDocument
{
    public bool Enabled { get; init; }

    public double Threshold { get; init; }

    public int MaxLengthMs { get; init; }
}

public sealed class ExtendedSignalDynamicFilterDocument
{
    public bool Enabled { get; init; }

    public int DetectionWindowMs { get; init; }

    public double SlopeThreshold { get; init; }

    public double RelativeSlopeThresholdPercent { get; init; }

    public double DynamicAngleMaxDegrees { get; init; } = 45.0;

    public int DynamicFilterTimeMs { get; init; }

    public int HoldTimeMs { get; init; }
}

public sealed class ExtendedSignalDefinitionDocument
{
    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public bool IsWritable { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public string WritePath { get; init; } = string.Empty;

    public SignalWriteMode WriteMode { get; init; } = SignalWriteMode.Direct;

    public bool ForwardChildWritesToSource { get; init; }

    [JsonPropertyName("sourceReadPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySourceReadPath { get; init; }

    public string Unit { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public ExtendedSignalFilterMode FilterMode { get; init; } = ExtendedSignalFilterMode.Raw;

    public int ScanIntervalMs { get; init; } = 100;

    public int RecordInterval { get; init; } = 1;

    public int FilterTimeMs { get; init; }

    public bool FillMissingWithLastValue { get; init; } = true;

    public bool KalmanEnabled { get; init; }

    public double KalmanMeasurementNoiseR { get; init; } = 1.0;

    public double KalmanProcessNoiseQ { get; init; } = 0.05;

    public double KalmanInitialErrorCovarianceP { get; init; } = 1.0;

    public int KalmanTeachWindowMs { get; init; } = 5000;

    public bool KalmanTeachPauseOnDynamic { get; init; } = true;

    public double KalmanTeachQFactor { get; init; } = 0.05;

    public bool KalmanTeachAutoApply { get; init; } = true;

    public bool KalmanDynamicQEnabled { get; init; }

    public double KalmanDynamicQMin { get; init; } = 0.05;

    public double KalmanDynamicQMax { get; init; } = 0.5;

    public int KalmanDynamicQHoldMs { get; init; } = 250;

    public int KalmanDynamicDetectionWindowMs { get; init; } = 1000;

    public double KalmanDynamicAngleThresholdDeg { get; init; } = 5.0;

    public double KalmanDynamicAngleMaxDeg { get; init; } = 45.0;

    public double KalmanDynamicReferenceFloor { get; init; } = 1.0;

    public KalmanDynamicNormalizationMode KalmanDynamicNormalizationMode { get; init; } = KalmanDynamicNormalizationMode.HybridReferenceFloor;

    public double KalmanDynamicResidualWeight { get; init; } = 0.35;

    public bool KalmanDynamicQUseExistingDynamic { get; init; } = true;

    public ExtendedSignalAdjustmentDocument Adjustment { get; init; } = new();

    [JsonPropertyName("correction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtendedSignalAdjustmentDocument? LegacyCorrection { get; init; }

    public ExtendedSignalPeakFilterDocument PeakFilter { get; init; } = new();

    public ExtendedSignalDynamicFilterDocument DynamicFilter { get; init; } = new();

    public ExtendedSignalStatisticsDocument Statistics { get; init; } = new();
}

public static class ExtendedSignalDefinitionCodec
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

    public static List<ExtendedSignalDefinitionDocument> ToDocuments(string? rawDefinitions, string? folderName)
    {
        return ParseDefinitions(rawDefinitions)
            .Select(definition => ToDocument(definition, folderName))
            .ToList();
    }

    public static string FromDocuments(IEnumerable<ExtendedSignalDefinitionDocument>? documents, string? folderName)
    {
        if (documents is null)
        {
            return string.Empty;
        }

        var definitions = documents
            .Where(static document => document is not null)
            .Select(document => FromDocument(document!, folderName))
            .ToArray();

        return SerializeDefinitions(definitions);
    }

    public static JsonArray ToJsonArray(string? rawDefinitions, string? folderName)
    {
        var array = new JsonArray();
        foreach (var definition in ParseDefinitions(rawDefinitions))
        {
            array.Add(JsonSerializer.SerializeToNode(ToDocument(definition, folderName), JsonOptions));
        }

        return array;
    }

    public static string FromJsonNode(JsonNode? node, string? folderName)
    {
        if (node is JsonArray array)
        {
            var definitions = array
                .Select(SanitizeDefinitionNode)
                .Select(child => child?.Deserialize<ExtendedSignalDefinitionDocument>(JsonOptions))
                .Where(static document => document is not null)
                .Select(document => FromDocument(document!, folderName));

            return SerializeDefinitions(definitions);
        }

        return node?.GetValue<string>() ?? string.Empty;
    }

    private static JsonNode? SanitizeDefinitionNode(JsonNode? node)
    {
        if (node is not JsonObject definition)
        {
            return node;
        }

        EnsurePolynomialRemoved(definition);

        EnsureObject(definition, "adjustment");
        EnsureObject(definition, "peakFilter");
        EnsureObject(definition, "dynamicFilter");
        EnsureObject(definition, "statistics");

        if (definition["adjustment"] is JsonObject adjustment)
        {
            EnsureArray(adjustment, "splinePoints");
        }

        return definition;
    }

    private static void EnsureObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is not JsonObject)
        {
            parent[propertyName] = new JsonObject();
        }
    }

    private static void EnsureArray(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is not JsonArray)
        {
            parent[propertyName] = new JsonArray();
        }
    }

    private static ExtendedSignalDefinitionDocument ToDocument(ExtendedSignalDefinition definition, string? folderName)
    {
        return new ExtendedSignalDefinitionDocument
        {
            Name = definition.Name,
            Enabled = definition.Enabled,
            IsWritable = definition.IsWritable,
            SourcePath = ToPersistedTargetPath(definition.SourcePath, folderName),
            WritePath = ToPersistedTargetPath(definition.WritePath, folderName),
            WriteMode = definition.WriteMode,
            ForwardChildWritesToSource = definition.ForwardChildWritesToSource,
            Unit = definition.Unit,
            Format = definition.Format,
            FilterMode = definition.FilterMode,
            ScanIntervalMs = definition.ScanIntervalMs,
            RecordInterval = definition.RecordInterval,
            FilterTimeMs = definition.FilterTimeMs,
            FillMissingWithLastValue = definition.FillMissingWithLastValue,
            KalmanEnabled = definition.KalmanEnabled,
            KalmanMeasurementNoiseR = definition.KalmanMeasurementNoiseR,
            KalmanProcessNoiseQ = definition.KalmanProcessNoiseQ,
            KalmanInitialErrorCovarianceP = definition.KalmanInitialErrorCovarianceP,
            KalmanTeachWindowMs = definition.KalmanTeachWindowMs,
            KalmanTeachPauseOnDynamic = definition.KalmanTeachPauseOnDynamic,
            KalmanTeachQFactor = definition.KalmanTeachQFactor,
            KalmanTeachAutoApply = definition.KalmanTeachAutoApply,
            KalmanDynamicQEnabled = definition.KalmanDynamicQEnabled,
            KalmanDynamicQMin = definition.KalmanDynamicQMin,
            KalmanDynamicQMax = definition.KalmanDynamicQMax,
            KalmanDynamicQHoldMs = definition.KalmanDynamicQHoldMs,
            KalmanDynamicDetectionWindowMs = definition.KalmanDynamicDetectionWindowMs,
            KalmanDynamicAngleThresholdDeg = definition.KalmanDynamicAngleThresholdDeg,
            KalmanDynamicAngleMaxDeg = definition.KalmanDynamicAngleMaxDeg,
            KalmanDynamicReferenceFloor = definition.KalmanDynamicReferenceFloor,
            KalmanDynamicNormalizationMode = definition.KalmanDynamicNormalizationMode,
            KalmanDynamicResidualWeight = definition.KalmanDynamicResidualWeight,
            KalmanDynamicQUseExistingDynamic = definition.KalmanDynamicQUseExistingDynamic,
            Adjustment = new ExtendedSignalAdjustmentDocument
            {
                Enabled = definition.Adjustment.Enabled,
                MappingMode = definition.Adjustment.MappingMode,
                Offset = definition.Adjustment.Offset,
                Gain = definition.Adjustment.Gain,
                SplinePoints = definition.Adjustment.SplinePoints
                    .Select(static point => new ExtendedSignalSplinePoint { Input = point.Input, Output = point.Output })
                    .ToList(),
                SplineInterpolationMode = definition.Adjustment.SplineInterpolationMode,
                SupportsInverseMapping = definition.Adjustment.SupportsInverseMapping
            },
            PeakFilter = new ExtendedSignalPeakFilterDocument
            {
                Enabled = definition.PeakFilter.Enabled,
                Threshold = definition.PeakFilter.Threshold,
                MaxLengthMs = definition.PeakFilter.MaxLengthMs
            },
            DynamicFilter = new ExtendedSignalDynamicFilterDocument
            {
                Enabled = definition.DynamicFilter.Enabled,
                DetectionWindowMs = definition.DynamicFilter.DetectionWindowMs,
                SlopeThreshold = definition.DynamicFilter.SlopeThreshold,
                RelativeSlopeThresholdPercent = definition.DynamicFilter.RelativeSlopeThresholdPercent,
                DynamicAngleMaxDegrees = definition.DynamicFilter.DynamicAngleMaxDegrees,
                DynamicFilterTimeMs = definition.DynamicFilter.DynamicFilterTimeMs,
                HoldTimeMs = definition.DynamicFilter.HoldTimeMs
            },
            Statistics = new ExtendedSignalStatisticsDocument
            {
                Enabled = definition.Statistics.Enabled,
                PublishMin = definition.Statistics.PublishMin,
                PublishMax = definition.Statistics.PublishMax,
                PublishAverage = definition.Statistics.PublishAverage,
                PublishStdDev = definition.Statistics.PublishStdDev,
                PublishIntegral = definition.Statistics.PublishIntegral,
                StdDevWindowMs = definition.Statistics.StdDevWindowMs,
                IntegralDivisorMs = definition.Statistics.IntegralDivisorMs
            }
        };
    }

    private static ExtendedSignalDefinition FromDocument(ExtendedSignalDefinitionDocument document, string? folderName)
    {
        return new ExtendedSignalDefinition
        {
            Name = document.Name,
            Enabled = document.Enabled,
            IsWritable = document.IsWritable,
            SourcePath = NormalizeTargetPath(
                string.IsNullOrWhiteSpace(document.SourcePath)
                    ? document.LegacySourceReadPath ?? string.Empty
                    : document.SourcePath,
                folderName),
            WritePath = NormalizeTargetPath(document.WritePath, folderName),
            WriteMode = document.WriteMode,
            ForwardChildWritesToSource = document.ForwardChildWritesToSource,
            Unit = document.Unit,
            Format = document.Format,
            FilterMode = document.FilterMode,
            ScanIntervalMs = document.ScanIntervalMs,
            RecordInterval = document.RecordInterval,
            FilterTimeMs = document.FilterTimeMs,
            FillMissingWithLastValue = document.FillMissingWithLastValue,
            KalmanEnabled = document.KalmanEnabled,
            KalmanMeasurementNoiseR = document.KalmanMeasurementNoiseR,
            KalmanProcessNoiseQ = document.KalmanProcessNoiseQ,
            KalmanInitialErrorCovarianceP = document.KalmanInitialErrorCovarianceP,
            KalmanTeachWindowMs = document.KalmanTeachWindowMs,
            KalmanTeachPauseOnDynamic = document.KalmanTeachPauseOnDynamic,
            KalmanTeachQFactor = document.KalmanTeachQFactor,
            KalmanTeachAutoApply = document.KalmanTeachAutoApply,
            KalmanDynamicQEnabled = document.KalmanDynamicQEnabled,
            KalmanDynamicQMin = document.KalmanDynamicQMin,
            KalmanDynamicQMax = document.KalmanDynamicQMax,
            KalmanDynamicQHoldMs = document.KalmanDynamicQHoldMs,
            KalmanDynamicDetectionWindowMs = document.KalmanDynamicDetectionWindowMs,
            KalmanDynamicAngleThresholdDeg = document.KalmanDynamicAngleThresholdDeg,
            KalmanDynamicAngleMaxDeg = document.KalmanDynamicAngleMaxDeg,
            KalmanDynamicReferenceFloor = document.KalmanDynamicReferenceFloor,
            KalmanDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor,
            KalmanDynamicResidualWeight = document.KalmanDynamicResidualWeight,
            KalmanDynamicQUseExistingDynamic = document.KalmanDynamicQUseExistingDynamic,
            Adjustment = new ExtendedSignalAdjustmentDefinition
            {
                Enabled = (document.LegacyCorrection ?? document.Adjustment).Enabled,
                MappingMode = NormalizeAdjustmentMappingMode(document.LegacyCorrection ?? document.Adjustment),
                Offset = (document.LegacyCorrection ?? document.Adjustment).Offset,
                Gain = (document.LegacyCorrection ?? document.Adjustment).Gain,
                SplinePoints = (document.LegacyCorrection ?? document.Adjustment).SplinePoints
                    .Select(static point => new ExtendedSignalSplinePoint { Input = point.Input, Output = point.Output })
                    .ToList(),
                SplineInterpolationMode = (document.LegacyCorrection ?? document.Adjustment).SplineInterpolationMode,
                SupportsInverseMapping = (document.LegacyCorrection ?? document.Adjustment).SupportsInverseMapping
            },
            PeakFilter = new ExtendedSignalPeakFilterDefinition
            {
                Enabled = document.PeakFilter.Enabled,
                Threshold = document.PeakFilter.Threshold,
                MaxLengthMs = document.PeakFilter.MaxLengthMs
            },
            DynamicFilter = new ExtendedSignalDynamicFilterDefinition
            {
                Enabled = document.DynamicFilter.Enabled,
                DetectionWindowMs = document.DynamicFilter.DetectionWindowMs,
                SlopeThreshold = document.DynamicFilter.SlopeThreshold,
                RelativeSlopeThresholdPercent = document.DynamicFilter.RelativeSlopeThresholdPercent,
                DynamicAngleMaxDegrees = document.DynamicFilter.DynamicAngleMaxDegrees,
                DynamicFilterTimeMs = document.DynamicFilter.DynamicFilterTimeMs,
                HoldTimeMs = document.DynamicFilter.HoldTimeMs
            },
            Statistics = new ExtendedSignalStatisticsDefinition
            {
                Enabled = document.Statistics.Enabled,
                PublishMin = document.Statistics.PublishMin,
                PublishMax = document.Statistics.PublishMax,
                PublishAverage = document.Statistics.PublishAverage,
                PublishStdDev = document.Statistics.PublishStdDev,
                PublishIntegral = document.Statistics.PublishIntegral,
                StdDevWindowMs = document.Statistics.StdDevWindowMs,
                IntegralDivisorMs = document.Statistics.LegacyIntegralFactor is > 0
                    ? 1d / document.Statistics.LegacyIntegralFactor.Value
                    : document.Statistics.IntegralDivisorMs
            }
        };
    }

    private static string ToPersistedTargetPath(string? value, string? folderName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : TargetPathHelper.ToPersistedLayoutTargetPath(value, folderName);
    }

    private static string NormalizeTargetPath(string? value, string? folderName)
    {
        _ = folderName;
        return TargetPathHelper.NormalizeConfiguredTargetPath(value ?? string.Empty);
    }

    private static ExtendedSignalAdjustmentMode NormalizeAdjustmentMappingMode(ExtendedSignalAdjustmentDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.LegacyMode)
            && Enum.TryParse<ExtendedSignalAdjustmentMode>(document.LegacyMode, true, out var parsedMode))
        {
            if (string.Equals(document.LegacyMode, "Polynomial", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Polynomial adjustment is no longer supported.");
            }

            return parsedMode == ExtendedSignalAdjustmentMode.Linear
                ? ExtendedSignalAdjustmentMode.None
                : parsedMode;
        }

        if (document.MappingMode != ExtendedSignalAdjustmentMode.None
            && document.MappingMode != ExtendedSignalAdjustmentMode.Spline
            && document.MappingMode != ExtendedSignalAdjustmentMode.Linear)
        {
            throw new InvalidOperationException("Polynomial adjustment is no longer supported.");
        }

        return document.MappingMode == ExtendedSignalAdjustmentMode.Linear
            ? ExtendedSignalAdjustmentMode.None
            : document.MappingMode;
    }

    private static void EnsurePolynomialRemoved(string raw)
    {
        var node = JsonNode.Parse(raw);
        if (node is not null)
        {
            EnsurePolynomialRemoved(node);
        }
    }

    private static void EnsurePolynomialRemoved(JsonNode node)
    {
        if (ContainsPolynomial(node))
        {
            throw new InvalidOperationException("Polynomial adjustment is no longer supported.");
        }
    }

    private static bool ContainsPolynomial(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                return array.Any(ContainsPolynomial);
            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (string.Equals(property.Key, "polynomialCoefficients", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if ((string.Equals(property.Key, "mappingMode", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(property.Key, "mode", StringComparison.OrdinalIgnoreCase))
                        && string.Equals(property.Value?.GetValue<string>(), "Polynomial", StringComparison.OrdinalIgnoreCase))
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