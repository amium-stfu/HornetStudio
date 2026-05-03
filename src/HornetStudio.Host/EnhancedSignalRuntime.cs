using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Amium.Items;
using HornetStudio.Editor.Models;

namespace HornetStudio.Host;

public sealed class EnhancedSignalRuntime : IDisposable
{
    private static readonly string[] ForwardedChannelNames = ["Read", "Set", "Out", "Command"];

    private readonly ExtendedSignalDefinition _definition;
    private readonly ExtendedSignalModule _module;
    private readonly string _folderName;
    private readonly string _registryPath;
    private readonly string _setRequestPath;
    private readonly string _commandRequestPath;
    private readonly string _kalmanRequestPath;
    private readonly string _adjustmentRequestPath;
    private readonly string _adjustmentEnabledPath;
    private readonly string _adjustmentMappingModePath;
    private readonly string _adjustmentSplineInterpolationModePath;
    private readonly string _adjustmentSplinePath;
    private readonly string _adjustmentGainPath;
    private readonly string _adjustmentOffsetPath;
    private readonly string _adjustmentSupportsInverseMappingPath;
    private readonly string _statisticsResetPath;
    private readonly string[] _sourceReadCandidates;
    private readonly Queue<SignalSample> _samples = new();
    private readonly Queue<SignalSample> _dynamicSamples = new();
    private readonly object _sync = new();
    private readonly ATimer _sampleTimer;
    private bool _disposed;
    private bool _isUpdating;
    private double? _emaValue;
    private object? _latestSourceValue;
    private double? _lastAcceptedRawValue;
    private double? _currentSmoothingAlpha;
    private DateTimeOffset? _peakStartedAt;
    private DateTimeOffset? _classicDynamicUntil;
    private DateTimeOffset? _kalmanDynamicUntil;
    private bool _sourceAvailable;
    private bool _smoothingPrimed;
    private double _lastDynamicSlope;
    private double _lastDynamicResidual;
    private double _lastDynamicThreshold;
    private int _lastEffectiveFilterTimeMs;
    private DateTimeOffset? _smoothingHistoryCutoff;
    private int _recordIntervalCounter;
    private double? _kalmanEstimate;
    private double? _kalmanErrorCovariance;
    private double _lastKalmanGain;
    private double _lastKalmanInnovation;
    private bool _kalmanTeachActive;
    private string _kalmanTeachState = "Idle";
    private DateTimeOffset? _kalmanTeachStartedAt;
    private DateTimeOffset? _kalmanTeachLastTickAt;
    private double _kalmanTeachAccumulatedMs;
    private int _kalmanTeachSampleCount;
    private double _kalmanTeachMean;
    private double _kalmanTeachM2;
    private double _kalmanTeachLearnedMeasurementNoiseR;
    private double _kalmanTeachDerivedProcessNoiseQ;
    private ExtendedSignalDefinition? _pendingPersistedDefinition;
    private double _effectiveKalmanProcessNoiseQ;
    private bool _adaptiveKalmanQActive;
    private DateTimeOffset? _adaptiveKalmanQUntil;
    private double _lastKalmanInnovationAbs;
    private double _adaptiveKalmanQIntensity;
    private double _lastDynamicReferenceValue;
    private double _lastDynamicEffectiveReferenceValue;
    private double _lastDynamicNoiseReferenceValue;
    private double _lastDynamicMaxSlope;
    private double _lastDynamicTrendConfidence;
    private double _lastDynamicRelativeChange;
    private double _lastDynamicRawAngleDegrees;
    private double _lastDynamicAngleDegrees;
    private double _lastDynamicMaxAngleDegrees;
    private string _lastDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
    private double _lastDynamicResidualWeight;
    private DateTimeOffset? _statisticsResetAt;
    private double? _statisticsMinValue;
    private DateTimeOffset? _statisticsMinTimestamp;
    private double? _statisticsMaxValue;
    private DateTimeOffset? _statisticsMaxTimestamp;
    private long _statisticsAverageSampleCount;
    private double _statisticsAverageMean;
    private SignalSample? _lastStatisticsIntegralSample;
    private double? _statisticsIntegralAccumulatedRaw;
    private DateTimeOffset? _lastDiagnosticsPublishedAt;
    private bool _lastPublishedDynamicState;
    private bool _lastPublishedAdaptiveQActive;
    private double _lastPublishedEffectiveKalmanQ;

    public EnhancedSignalRuntime(string folderName, ExtendedSignalDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);

        _folderName = folderName;
        _definition = definition.Clone();
        _registryPath = BuildRegistryPath(folderName, _definition);
        _setRequestPath = _registryPath + ".Set.Request";
        _commandRequestPath = _registryPath + ".Command.Request";
        _kalmanRequestPath = _registryPath + ".Kalman.Request";
        _adjustmentRequestPath = _registryPath + ".Adjustment.Request";
        _adjustmentEnabledPath = _registryPath + ".Adjustment.Enabled";
        _adjustmentMappingModePath = _registryPath + ".Adjustment.MappingMode";
        _adjustmentSplineInterpolationModePath = _registryPath + ".Adjustment.SplineInterpolationMode";
        _adjustmentSplinePath = _registryPath + ".Adjustment.Spline";
        _adjustmentGainPath = _registryPath + ".Adjustment.Gain";
        _adjustmentOffsetPath = _registryPath + ".Adjustment.Offset";
        _adjustmentSupportsInverseMappingPath = _registryPath + ".Adjustment.SupportsInverseMapping";
        _statisticsResetPath = _registryPath + ".Statistics.Reset";
        _sourceReadCandidates = EnhancedSignalPathHelper.EnumerateResolutionCandidates(_definition.SourcePath, folderName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _latestSourceValue = ResolveSourceValue();

        _module = CreateModule(_definition, _registryPath);
        _sampleTimer = new ATimer($"EnhancedSignalRuntime-{folderName}-{_definition.Name}", ComputeSampleIntervalMs(_definition));
        _sampleTimer.Tick += OnSampleTimerTick;
        ApplyConfiguration();
        RefreshReadState(captureSample: true);
        PublishSnapshot();
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
        _sampleTimer.Start();
    }

    public string RegistryPath => _registryPath;

    public ExtendedSignalDefinition Definition => _definition;

    public object? CurrentRawValue => _module.Raw.Value;

    public object? CurrentOutputValue => _module.Read.Value;

    public object? CurrentSetValue => _module.Set.Value;

    public object? CurrentCommandValue => _module.Command.Value;

    public object? CurrentAlertValue => _module.Alert.Value;

    public bool MatchesPath(string path)
    {
        if (EnhancedSignalPathHelper.PathsEqual(path, _registryPath))
        {
            return true;
        }

        return EnhancedSignalPathHelper.IsDescendantPath(path, _registryPath)
            || EnhancedSignalPathHelper.IsDescendantPath(_registryPath, path)
            || _sourceReadCandidates.Any(candidate => EnhancedSignalPathHelper.PathsEqual(candidate, path)
                || EnhancedSignalPathHelper.IsDescendantPath(candidate, path)
                || EnhancedSignalPathHelper.IsDescendantPath(path, candidate));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        _sampleTimer.Tick -= OnSampleTimerTick;
        _sampleTimer.Dispose();
        HostRegistries.Data.Remove(_registryPath);
    }

    public static string BuildRegistryPath(string folderName, ExtendedSignalDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);
        var normalizedFolder = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName).Replace('/', '.');
        var normalizedName = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(definition.Name).Replace('/', '.');
        return $"Studio.{normalizedFolder}.EnhancedSignals.{normalizedName}";
    }

    public static string BuildRegistryPath(string folderName, string definitionName)
    {
        return BuildRegistryPath(folderName, new ExtendedSignalDefinition { Name = definitionName });
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (_disposed || _isUpdating)
        {
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _setRequestPath))
        {
            ProcessSetRequest(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _commandRequestPath))
        {
            ProcessCommandRequest(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _kalmanRequestPath))
        {
            ProcessKalmanRequest(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentRequestPath))
        {
            ProcessAdjustmentRequest(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentEnabledPath))
        {
            ProcessAdjustmentEnabledWrite(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentMappingModePath))
        {
            ProcessAdjustmentMappingModeWrite(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentSplineInterpolationModePath))
        {
            ProcessAdjustmentSplineInterpolationModeWrite(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentSplinePath))
        {
            ProcessAdjustmentSplineWrite(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentGainPath))
        {
            ProcessAdjustmentScalarWrite(e.Item, isGain: true);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentOffsetPath))
        {
            ProcessAdjustmentScalarWrite(e.Item, isGain: false);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _adjustmentSupportsInverseMappingPath))
        {
            ProcessAdjustmentSupportsInverseMappingWrite(e.Item);
            return;
        }

        if (EnhancedSignalPathHelper.PathsEqual(e.Key, _statisticsResetPath))
        {
            ProcessStatisticsResetWrite(e.Item);
            return;
        }

        if (_definition.ForwardChildWritesToSource && TryForwardChildWrite(e))
        {
            return;
        }

        if (_sourceReadCandidates.Any(candidate => EnhancedSignalPathHelper.PathsEqual(candidate, e.Key)
            || EnhancedSignalPathHelper.IsDescendantPath(candidate, e.Key)
            || EnhancedSignalPathHelper.IsDescendantPath(e.Key, candidate)))
        {
            lock (_sync)
            {
                _latestSourceValue = ExtractValue(e.Item);
            }
        }
    }

    private void OnSampleTimerTick()
    {
        if (_disposed)
        {
            return;
        }

        var resolvedSourceValue = ResolveSourceValue();
        var captureSample = resolvedSourceValue is not null;
        RefreshReadState(captureSample, resolvedSourceValue, resolvedSourceValue is not null);
    }

    private void RefreshReadState(bool captureSample)
    {
        RefreshReadState(captureSample, resolvedSourceValue: null, useResolvedSourceValue: false);
    }

    private void RefreshReadState(bool captureSample, object? resolvedSourceValue, bool useResolvedSourceValue)
    {
        object? rawValue;
        object? preprocessedRawValue;
        object? acceptedRawValue;
        object? filteredValue;
        var hasSource = false;
        var now = DateTimeOffset.UtcNow;
        var isDynamic = false;
        var peakSuppressed = false;

        lock (_sync)
        {
            rawValue = useResolvedSourceValue ? resolvedSourceValue : ResolveSourceValue();
            hasSource = rawValue is not null;
            if (rawValue is null && !useResolvedSourceValue)
            {
                rawValue = _latestSourceValue;
                hasSource = rawValue is not null;
            }

            _latestSourceValue = rawValue;
            if (!hasSource)
            {
                ResetFilterState();
            }

            preprocessedRawValue = PreprocessRawValue(rawValue);
            acceptedRawValue = RecordAcceptedRawValue(preprocessedRawValue, now, captureSample, out peakSuppressed);
            filteredValue = ComputeSmoothedValue(acceptedRawValue, now, captureSample, out isDynamic);
        }

        LogSourceAvailabilityTransition(hasSource);

        _isUpdating = true;
        try
        {
            _module.Value = filteredValue!;
            _module.Raw.Value = rawValue!;
            _module.Read.Value = filteredValue!;
            _module.State.Value = string.IsNullOrWhiteSpace(_definition.SourcePath)
                ? "No source"
                : hasSource ? "Active" : "Waiting for source";
            _module.Alert.Value = string.Empty;
            if (ShouldPublishDiagnostics(now, isDynamic))
            {
                PublishDiagnostics(now, isDynamic, acceptedRawValue, peakSuppressed);
            }
            PublishSnapshot();
        }
        finally
        {
            _isUpdating = false;
        }

        FlushPendingPersistedDefinitionUpdate();
    }

    private void ProcessSetRequest(Item requestItem)
    {
        if (string.IsNullOrWhiteSpace(_definition.SourcePath))
        {
            SetAlert("SourcePath not configured");
            return;
        }

        var requestValue = requestItem.Params.Has("Value") ? requestItem.Params["Value"].Value : requestItem.Value;
        object? routedValue = requestValue;

        if (!TryApplyInverseAdjustment(requestValue, out routedValue, out string errorMessage))
        {
            SetAlert(errorMessage);
            return;
        }

        var routed = _definition.ForwardChildWritesToSource
            ? ForwardChildValue("Set.Request", routedValue)
            : UpdateTargetValue(_definition.SourcePath, routedValue);

        if (!routed)
        {
            SetAlert($"Unable to route set to '{_definition.SourcePath}'");
            return;
        }

        _isUpdating = true;
        try
        {
            _module.Set.Value = requestValue!;
            _module.State.Value = "Set routed";
            _module.Alert.Value = string.Empty;
            PublishSnapshot();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void ProcessCommandRequest(Item requestItem)
    {
        var requestValue = requestItem.Params.Has("Value") ? requestItem.Params["Value"].Value : requestItem.Value;
        if (string.IsNullOrWhiteSpace(_definition.SourcePath))
        {
            SetAlert("SourcePath not configured");
            return;
        }

        var routed = _definition.ForwardChildWritesToSource
            ? ForwardChildValue("Command.Request", requestValue)
            : UpdateTargetValue(_definition.SourcePath, requestValue);

        if (!routed)
        {
            SetAlert($"Unable to route command to '{_definition.SourcePath}'");
            return;
        }

        _isUpdating = true;
        try
        {
            _module.Command.Value = requestValue!;
            _module.State.Value = "Command routed";
            _module.Alert.Value = string.Empty;
            PublishSnapshot();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void ProcessKalmanRequest(Item requestItem)
    {
        var requestValue = requestItem.Params.Has("Value") ? requestItem.Params["Value"].Value : requestItem.Value;
        var command = NormalizeRequestCommand(requestValue);
        if (string.IsNullOrWhiteSpace(command))
        {
            SetAlert("Kalman request is empty");
            return;
        }

        lock (_sync)
        {
            switch (command)
            {
                case "startteach":
                    if (!_definition.KalmanEnabled)
                    {
                        SetAlert("Kalman is disabled");
                        return;
                    }

                    StartKalmanTeach(DateTimeOffset.UtcNow);
                    break;
                case "stopteach":
                    if (!_kalmanTeachActive)
                    {
                        SetAlert("Kalman teach is not active");
                        return;
                    }

                    FinalizeKalmanTeach(applyResults: true);
                    break;
                case "resetkalman":
                    ResetKalmanRuntimeState(clearTeachState: true);
                    _kalmanTeachState = "Reset";
                    break;
                default:
                    SetAlert($"Unknown Kalman request '{requestValue}'");
                    return;
            }
        }

        FlushPendingPersistedDefinitionUpdate();
    }

    private void ProcessAdjustmentRequest(Item requestItem)
    {
        if (!TryParseAdjustmentRequest(requestItem, out var action, out var targetValue, out var errorMessage))
        {
            SetAlert(errorMessage);
            return;
        }

        if (!TryApplyAdjustmentAction(action, targetValue, out errorMessage))
        {
            SetAlert(errorMessage);
            return;
        }

        RefreshAdjustmentState();
    }

    private void ProcessAdjustmentScalarWrite(Item item, bool isGain)
    {
        var numericValue = ToNullableDouble(ExtractValue(item));
        if (numericValue is null)
        {
            SetAlert($"Adjustment {(isGain ? "Gain" : "Offset")} must be numeric");
            return;
        }

        lock (_sync)
        {
            if (isGain)
            {
                _definition.Adjustment.Gain = numericValue.Value;
            }
            else
            {
                _definition.Adjustment.Offset = numericValue.Value;
            }

            _pendingPersistedDefinition = _definition.Clone();
            ResetFilterState();
        }

        RefreshAdjustmentState();
    }

    private void ProcessAdjustmentEnabledWrite(Item item)
    {
        var enabled = ToNullableBoolean(ExtractValue(item));
        if (enabled is null)
        {
            SetAlert("Adjustment Enabled must be boolean");
            return;
        }

        lock (_sync)
        {
            _definition.Adjustment.Enabled = enabled.Value;
            _pendingPersistedDefinition = _definition.Clone();
            ResetFilterState();
        }

        RefreshAdjustmentState();
    }

    private void ProcessAdjustmentMappingModeWrite(Item item)
    {
        var rawValue = ExtractValue(item)?.ToString() ?? string.Empty;
        if (!TryParseAdjustmentMappingMode(rawValue, out var mappingMode))
        {
            SetAlert("Adjustment MappingMode must be None or Spline");
            return;
        }

        lock (_sync)
        {
            _definition.Adjustment.MappingMode = mappingMode;
            _pendingPersistedDefinition = _definition.Clone();
            ResetFilterState();
        }

        RefreshAdjustmentState();
    }

    private void ProcessAdjustmentSupportsInverseMappingWrite(Item item)
    {
        var supportsInverseMapping = ToNullableBoolean(ExtractValue(item));
        if (supportsInverseMapping is null)
        {
            SetAlert("Adjustment SupportsInverseMapping must be boolean");
            return;
        }

        lock (_sync)
        {
            _definition.Adjustment.SupportsInverseMapping = supportsInverseMapping.Value;
            _pendingPersistedDefinition = _definition.Clone();
        }

        RefreshAdjustmentState();
    }

    private void ProcessAdjustmentSplineInterpolationModeWrite(Item item)
    {
        var rawValue = ExtractValue(item)?.ToString() ?? string.Empty;
        if (!TryParseSplineInterpolationMode(rawValue, out var interpolationMode))
        {
            SetAlert("Adjustment SplineInterpolationMode must be Linear or CatmullRom");
            return;
        }

        lock (_sync)
        {
            _definition.Adjustment.SplineInterpolationMode = interpolationMode;
            _pendingPersistedDefinition = _definition.Clone();
            ResetFilterState();
        }

        RefreshAdjustmentState();
    }

    private void ProcessAdjustmentSplineWrite(Item item)
    {
        if (!TryParseSplinePoints(ExtractValue(item), out var splinePoints, out var errorMessage))
        {
            SetAlert(errorMessage);
            return;
        }

        lock (_sync)
        {
            _definition.Adjustment.SplinePoints = splinePoints.ToList();
            _pendingPersistedDefinition = _definition.Clone();
            ResetFilterState();
        }

        RefreshAdjustmentState();
    }

    private void ProcessStatisticsResetWrite(Item item)
    {
        var resetRequested = ToNullableBoolean(ExtractValue(item));
        if (resetRequested is null)
        {
            SetAlert("Statistics Reset must be boolean");
            return;
        }

        if (!resetRequested.Value)
        {
            _isUpdating = true;
            try
            {
                EnsureStatisticsBranch()["Reset"].Value = false;
                PublishSnapshot();
            }
            finally
            {
                _isUpdating = false;
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        StatisticsSnapshot statistics;
        lock (_sync)
        {
            ResetStatisticsState(now);
            statistics = CaptureStatisticsSnapshot(now);
        }

        _isUpdating = true;
        try
        {
            _module.Alert.Value = string.Empty;
            if (statistics.Enabled)
            {
                PublishStatisticsSnapshot(statistics);
            }

            PublishSnapshot();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private object? ResolveSourceValue()
    {
        foreach (var candidate in _sourceReadCandidates)
        {
            if (TryResolveRegistryItem(candidate, out var item) && item is not null)
            {
                return ExtractValue(item);
            }
        }

        return null;
    }

    private static bool TryResolveRegistryItem(string path, out Item? item)
    {
        if (HostRegistries.Data.TryGet(path, out item) && item is not null)
        {
            return true;
        }

        var rootKey = HostRegistries.Data.GetAllKeys()
            .Where(existingKey => path.StartsWith(existingKey + ".", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(existingKey + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(existingKey => existingKey.Length)
            .FirstOrDefault();

        if (rootKey is null || !HostRegistries.Data.TryGet(rootKey, out var rootItem) || rootItem is null)
        {
            item = null;
            return false;
        }

        var current = rootItem;
        foreach (var segment in path[(rootKey.Length + 1)..].Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!current.Has(segment))
            {
                item = null;
                return false;
            }

            current = current[segment];
        }

        item = current;
        return true;
    }

    private static object? ExtractValue(Item item)
    {
        if (item.Params.Has("Value"))
        {
            return item.Params["Value"].Value;
        }

        if (item.Value is not null)
        {
            return item.Value;
        }

        if (item.Has("Out"))
        {
            var outItem = item["Out"];
            return outItem.Params.Has("Value") ? outItem.Params["Value"].Value : outItem.Value;
        }

        if (item.Has("Read"))
        {
            var readItem = item["Read"];
            return readItem.Params.Has("Value") ? readItem.Params["Value"].Value : readItem.Value;
        }

        return null;
    }

    private object? ApplyAdjustment(object? rawValue)
    {
        var numericValue = ToNullableDouble(rawValue);
        if (!_definition.Adjustment.Enabled || numericValue is null)
        {
            return rawValue;
        }

        var mappedValue = ApplyAdjustmentMapping(numericValue.Value);
        return ApplyAdjustmentAffine(mappedValue);
    }

    private double ApplyAdjustmentMapping(double rawValue)
    {
        var adjustment = _definition.Adjustment;
        return adjustment.MappingMode switch
        {
            ExtendedSignalAdjustmentMode.Spline => ApplySpline(rawValue, adjustment.SplinePoints, adjustment.SplineInterpolationMode),
            _ => rawValue
        };
    }

    private double ApplyAdjustmentAffine(double mappedValue)
    {
        var adjustment = _definition.Adjustment;
        return (mappedValue * adjustment.Gain) + adjustment.Offset;
    }

    private object? PreprocessRawValue(object? rawValue)
    {
        var correctedValue = ApplyAdjustment(rawValue);
        return ApplyZeroClipping(correctedValue);
    }

    private object? ApplyZeroClipping(object? value)
    {
        return value;
    }

    private object? RecordAcceptedRawValue(object? preprocessedRawValue, DateTimeOffset now, bool captureSample, out bool peakSuppressed)
    {
        peakSuppressed = false;
        var numericValue = ToNullableDouble(preprocessedRawValue);
        if (numericValue is null)
        {
            return preprocessedRawValue;
        }

        var acceptedRawValue = ApplyPeakFilterToRaw(numericValue.Value, now, out peakSuppressed);
        if (captureSample)
        {
            _dynamicSamples.Enqueue(new SignalSample(now, acceptedRawValue));
            if (ShouldRecordFilterSample())
            {
                PrimeSmoothingState(now, acceptedRawValue);
                _samples.Enqueue(new SignalSample(now, acceptedRawValue));
            }

            AccumulateRunningStatistics(new SignalSample(now, acceptedRawValue));
            _lastAcceptedRawValue = acceptedRawValue;
        }

        TrimSamples(now);

        return acceptedRawValue;
    }

    private void PrimeSmoothingState(DateTimeOffset now, double acceptedRawValue)
    {
        if (_smoothingPrimed)
        {
            return;
        }

        var targetCount = GetTargetSmoothingSampleCount(GetEffectiveFilterTimeMs(now));
        if (!_definition.FillMissingWithLastValue)
        {
            _emaValue = acceptedRawValue;
            _lastAcceptedRawValue = acceptedRawValue;
            _smoothingPrimed = true;
            return;
        }

        var intervalMs = GetFilterRecordIntervalMs();
        for (var index = targetCount - 1; index >= 1; index--)
        {
            _samples.Enqueue(new SignalSample(now.AddMilliseconds(-intervalMs * index), acceptedRawValue));
        }

        _emaValue = acceptedRawValue;
        _lastAcceptedRawValue = acceptedRawValue;
        _smoothingPrimed = true;
    }

    private void ResetFilterState()
    {
        ResetStatisticsState();
        _samples.Clear();
        _dynamicSamples.Clear();
        _emaValue = null;
        _lastAcceptedRawValue = null;
        _currentSmoothingAlpha = null;
        _peakStartedAt = null;
        _classicDynamicUntil = null;
        _kalmanDynamicUntil = null;
        _lastDynamicSlope = 0d;
        _lastDynamicResidual = 0d;
        _lastDynamicThreshold = 0d;
        _lastDynamicRawAngleDegrees = 0d;
        _lastDynamicAngleDegrees = 0d;
        _lastDynamicMaxAngleDegrees = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleMaxDeg);
        _lastEffectiveFilterTimeMs = 0;
        _smoothingHistoryCutoff = null;
        _smoothingPrimed = false;
        _recordIntervalCounter = 0;
        ResetKalmanRuntimeState(clearTeachState: true);
    }

    private object? ComputeSmoothedValue(object? acceptedRawValue, DateTimeOffset now, bool captureSample, out bool isDynamic)
    {
        var numericValue = ToNullableDouble(acceptedRawValue);
        if (numericValue is null)
        {
            isDynamic = false;
            return acceptedRawValue;
        }

        if (!_definition.KalmanEnabled && _samples.Count == 0)
        {
            isDynamic = false;
            return numericValue.Value;
        }

        var classicDynamic = UpdateClassicDynamicState(now);
        if (IsKalmanDynamicDetectionEnabled())
        {
            UpdateKalmanDynamicState(now);
        }

        isDynamic = _definition.KalmanEnabled ? IsKalmanDynamicActive(now) : classicDynamic;
        UpdateSmoothingWindowState(now);
        var filteredValue = ApplyFilterCore(numericValue.Value, captureSample, now, classicDynamic);
        return filteredValue;
    }

    private double ApplyFilterCore(double currentValue, bool captureSample, DateTimeOffset now, bool isDynamic)
    {
        if (_definition.KalmanEnabled)
        {
            _currentSmoothingAlpha = null;
            return ApplyKalmanFilter(currentValue, captureSample, now);
        }

        var smoothingSamples = GetSmoothingSamples(now, currentValue);
        return _definition.FilterMode switch
        {
            ExtendedSignalFilterMode.Raw => currentValue,
            ExtendedSignalFilterMode.Average => smoothingSamples.Count == 0 ? currentValue : smoothingSamples.Average(sample => sample.Value),
            ExtendedSignalFilterMode.Wma => ApplyWeightedAverage(smoothingSamples),
            ExtendedSignalFilterMode.Ema => ApplyExponentialAverage(currentValue, captureSample, smoothingSamples.Count),
            ExtendedSignalFilterMode.EmaWma => ApplyWeightedAverage(ProjectExponentialSeries(smoothingSamples)),
            _ => currentValue
        };
    }

    private double ApplyPeakFilterToRaw(double candidateValue, DateTimeOffset now, out bool peakSuppressed)
    {
        peakSuppressed = false;
        if (!_definition.PeakFilter.Enabled || _lastAcceptedRawValue is null)
        {
            return candidateValue;
        }

        var difference = Math.Abs(candidateValue - _lastAcceptedRawValue.Value);
        if (difference <= _definition.PeakFilter.Threshold)
        {
            _peakStartedAt = null;
            return candidateValue;
        }

        _peakStartedAt ??= now;
        if ((now - _peakStartedAt.Value).TotalMilliseconds < _definition.PeakFilter.MaxLengthMs)
        {
            peakSuppressed = true;
            return _lastAcceptedRawValue.Value;
        }

        _peakStartedAt = null;
        return candidateValue;
    }

    private bool UpdateClassicDynamicState(DateTimeOffset now)
    {
        if (!_definition.DynamicFilter.Enabled)
        {
            _classicDynamicUntil = null;
            return false;
        }

        var detectionWindowMs = Math.Max(100, _definition.DynamicFilter.DetectionWindowMs);
        var relevantSamples = _dynamicSamples.Where(sample => (now - sample.Timestamp).TotalMilliseconds <= detectionWindowMs).ToArray();
        if (relevantSamples.Length < 2)
        {
            return _classicDynamicUntil is not null && now <= _classicDynamicUntil.Value;
        }

        var analysis = AnalyzeDynamicWindow(relevantSamples);
        var referenceValue = Math.Max(relevantSamples.Average(sample => Math.Abs(sample.Value)), 0.000001d);
        var relativeThreshold = referenceValue * (_definition.DynamicFilter.RelativeSlopeThresholdPercent / 100d);
        var threshold = Math.Max(_definition.DynamicFilter.SlopeThreshold, relativeThreshold);
        var isDynamic = _classicDynamicUntil is not null && now <= _classicDynamicUntil.Value;
        if (Math.Abs(analysis.SlopePerSecond) >= threshold)
        {
            if (!isDynamic)
            {
                _classicDynamicUntil = now.AddMilliseconds(Math.Max(0, _definition.DynamicFilter.HoldTimeMs));
            }

            return true;
        }

        return isDynamic;
    }

    private bool UpdateKalmanDynamicState(DateTimeOffset now)
    {
        if (!IsKalmanDynamicDetectionEnabled())
        {
            _kalmanDynamicUntil = null;
            _lastDynamicSlope = 0d;
            _lastDynamicResidual = 0d;
            _lastDynamicThreshold = 0d;
            _lastDynamicReferenceValue = 0d;
            _lastDynamicEffectiveReferenceValue = 0d;
            _lastDynamicNoiseReferenceValue = 0d;
            _lastDynamicMaxSlope = 0d;
            _lastDynamicTrendConfidence = 0d;
            _lastDynamicRelativeChange = 0d;
            _lastDynamicRawAngleDegrees = 0d;
            _lastDynamicAngleDegrees = 0d;
            _lastDynamicMaxAngleDegrees = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleMaxDeg);
            _lastDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
            _lastDynamicResidualWeight = Math.Clamp(_definition.KalmanDynamicResidualWeight, 0d, 1d);
            return false;
        }

        var detectionWindowMs = Math.Max(100, _definition.KalmanDynamicDetectionWindowMs);
        var relevantSamples = _dynamicSamples.Where(sample => (now - sample.Timestamp).TotalMilliseconds <= detectionWindowMs).ToArray();
        if (relevantSamples.Length < 2)
        {
            _lastDynamicSlope = 0d;
            _lastDynamicResidual = 0d;
            _lastDynamicThreshold = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleThresholdDeg);
            _lastDynamicReferenceValue = 0d;
            _lastDynamicEffectiveReferenceValue = 0d;
            _lastDynamicNoiseReferenceValue = 0d;
            _lastDynamicMaxSlope = 0d;
            _lastDynamicTrendConfidence = 0d;
            _lastDynamicRelativeChange = 0d;
            _lastDynamicRawAngleDegrees = 0d;
            _lastDynamicAngleDegrees = 0d;
            _lastDynamicMaxAngleDegrees = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleMaxDeg);
            _lastDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
            _lastDynamicResidualWeight = Math.Clamp(_definition.KalmanDynamicResidualWeight, 0d, 1d);
            return _kalmanDynamicUntil is not null && now <= _kalmanDynamicUntil.Value;
        }

        var analysis = AnalyzeDynamicWindow(relevantSamples);
        _lastDynamicSlope = analysis.SlopePerSecond;
        _lastDynamicResidual = analysis.RootMeanSquareResidual;
        var detectionWindowSeconds = Math.Max(0.001d, detectionWindowMs / 1000d);
        var trendSpan = Math.Abs(analysis.SlopePerSecond) * detectionWindowSeconds;
        var normalization = CalculateKalmanDynamicNormalization(analysis, relevantSamples, trendSpan);
        _lastDynamicReferenceValue = normalization.ReferenceValue;
        _lastDynamicEffectiveReferenceValue = normalization.EffectiveReferenceValue;
        _lastDynamicNoiseReferenceValue = normalization.NoiseReferenceValue;
        _lastDynamicNormalizationMode = normalization.Mode.ToString();
        _lastDynamicResidualWeight = normalization.ResidualWeight;
        _lastDynamicMaxSlope = normalization.EffectiveReferenceValue / detectionWindowSeconds;
        _lastDynamicRelativeChange = trendSpan / normalization.EffectiveReferenceValue;
        _lastDynamicRawAngleDegrees = RelativeChangeToAngleDegrees(_lastDynamicRelativeChange);
        var angleResponseAlpha = _lastDynamicRawAngleDegrees >= _lastDynamicAngleDegrees ? 0.35d : 0.12d;
        _lastDynamicAngleDegrees += (_lastDynamicRawAngleDegrees - _lastDynamicAngleDegrees) * angleResponseAlpha;
        _lastDynamicAngleDegrees = Math.Clamp(_lastDynamicAngleDegrees, 0d, 89d);
        _lastDynamicMaxAngleDegrees = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleMaxDeg);
        _lastDynamicThreshold = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleThresholdDeg);
        var confidenceDenominator = trendSpan + (analysis.RootMeanSquareResidual * 2d);
        _lastDynamicTrendConfidence = confidenceDenominator <= double.Epsilon ? 0d : Math.Clamp(trendSpan / confidenceDenominator, 0d, 1d);

        _lastDynamicAngleDegrees = Math.Clamp(_lastDynamicRawAngleDegrees * _lastDynamicTrendConfidence, 0d, 89d);

        var isDynamic = _kalmanDynamicUntil is not null && now <= _kalmanDynamicUntil.Value;

        if (_lastDynamicAngleDegrees >= _lastDynamicThreshold)
        {
            if (!isDynamic)
            {
                _kalmanDynamicUntil = now.AddMilliseconds(Math.Max(0, _definition.KalmanDynamicQHoldMs));
            }

            return true;
        }

        return isDynamic;
    }

    private void PublishDiagnostics(DateTimeOffset now, bool isDynamic, object? acceptedRawValue, bool peakSuppressed)
    {
        int rawBufferCount;
        int smoothingWindowCount;
        int effectiveFilterTimeMs;
        int effectiveSampleCount;
        double currentAlpha;
        double dynamicSlope;
        double dynamicResidual;
        double dynamicThreshold;
        int dynamicBufferCount;
        bool peakActive;
        double remainingHoldMs;
        bool kalmanEnabled;
        double kalmanEstimate;
        double kalmanGain;
        double kalmanInnovation;
        double kalmanMeasurementNoiseR;
        double kalmanProcessNoiseQ;
        double kalmanInitialErrorCovarianceP;
        bool kalmanTeachActive;
        string kalmanTeachState;
        int kalmanTeachSampleCount;
        double kalmanTeachProgress;
        double kalmanTeachLearnedMeasurementNoiseR;
        double kalmanTeachDerivedProcessNoiseQ;
        double effectiveKalmanProcessNoiseQ;
        bool adaptiveKalmanQActive;
        double adaptiveKalmanQHoldRemainingMs;
        double adaptiveKalmanQMin;
        double adaptiveKalmanQMax;
        double kalmanInnovationAbs;
        double adaptiveKalmanQIntensity;
        double dynamicSlopeRatio;
        double dynamicReferenceValue;
        double dynamicEffectiveReferenceValue;
        double dynamicNoiseReferenceValue;
        double dynamicMaxSlope;
        double dynamicTrendConfidence;
        double dynamicRawAngleDegrees;
        double dynamicAngleDegrees;
        double dynamicMaxAngleDegrees;
        string dynamicNormalizationMode;
        double dynamicResidualWeight;
        StatisticsSnapshot statistics;

        lock (_sync)
        {
            var smoothingSamples = GetSmoothingSamples(now, ToNullableDouble(acceptedRawValue));
            rawBufferCount = _samples.Count;
            smoothingWindowCount = smoothingSamples.Count;
            effectiveFilterTimeMs = GetEffectiveFilterTimeMs(now);
            effectiveSampleCount = GetEffectiveSmoothingSampleCount(smoothingSamples.Count);
            currentAlpha = _currentSmoothingAlpha ?? 0d;
            dynamicSlope = _lastDynamicSlope;
            dynamicResidual = _lastDynamicResidual;
            dynamicThreshold = _lastDynamicThreshold;
            dynamicBufferCount = _dynamicSamples.Count;
            peakActive = _peakStartedAt is not null;
            remainingHoldMs = _definition.KalmanEnabled ? (_kalmanDynamicUntil is null ? 0 : Math.Max(0, (_kalmanDynamicUntil.Value - now).TotalMilliseconds)) : (_classicDynamicUntil is null ? 0 : Math.Max(0, (_classicDynamicUntil.Value - now).TotalMilliseconds));
            kalmanEnabled = _definition.KalmanEnabled;
            kalmanEstimate = _kalmanEstimate ?? ToNullableDouble(acceptedRawValue) ?? 0d;
            kalmanGain = _lastKalmanGain;
            kalmanInnovation = _lastKalmanInnovation;
            kalmanMeasurementNoiseR = _definition.KalmanMeasurementNoiseR;
            kalmanProcessNoiseQ = _definition.KalmanProcessNoiseQ;
            kalmanInitialErrorCovarianceP = _definition.KalmanInitialErrorCovarianceP;
            kalmanTeachActive = _kalmanTeachActive;
            kalmanTeachState = _kalmanTeachState;
            kalmanTeachSampleCount = _kalmanTeachSampleCount;
            kalmanTeachProgress = _definition.KalmanTeachWindowMs <= 0
                ? 0d
                : Math.Min(100d, (_kalmanTeachAccumulatedMs / _definition.KalmanTeachWindowMs) * 100d);
            kalmanTeachLearnedMeasurementNoiseR = _kalmanTeachLearnedMeasurementNoiseR;
            kalmanTeachDerivedProcessNoiseQ = _kalmanTeachDerivedProcessNoiseQ;
            effectiveKalmanProcessNoiseQ = _effectiveKalmanProcessNoiseQ;
            adaptiveKalmanQActive = _adaptiveKalmanQActive;
            adaptiveKalmanQHoldRemainingMs = _adaptiveKalmanQUntil is null ? 0d : Math.Max(0d, (_adaptiveKalmanQUntil.Value - now).TotalMilliseconds);
            adaptiveKalmanQMin = _definition.KalmanDynamicQMin;
            adaptiveKalmanQMax = _definition.KalmanDynamicQMax;
            kalmanInnovationAbs = _lastKalmanInnovationAbs;
            adaptiveKalmanQIntensity = _adaptiveKalmanQIntensity;
            dynamicSlopeRatio = _lastDynamicRelativeChange;
            dynamicReferenceValue = _lastDynamicReferenceValue;
            dynamicEffectiveReferenceValue = _lastDynamicEffectiveReferenceValue;
            dynamicNoiseReferenceValue = _lastDynamicNoiseReferenceValue;
            dynamicMaxSlope = _lastDynamicMaxSlope;
            dynamicTrendConfidence = _lastDynamicTrendConfidence;
            dynamicRawAngleDegrees = _lastDynamicRawAngleDegrees;
            dynamicAngleDegrees = _lastDynamicAngleDegrees;
            dynamicMaxAngleDegrees = _lastDynamicMaxAngleDegrees;
            dynamicNormalizationMode = _lastDynamicNormalizationMode;
            dynamicResidualWeight = _lastDynamicResidualWeight;
            statistics = CaptureStatisticsSnapshot(now);
        }

        var acceptedRawText = acceptedRawValue switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => acceptedRawValue.ToString() ?? string.Empty
        };

        _module.Config.Params["RawBufferCount"].Value = rawBufferCount;
        _module.Config.Params["SmoothingWindowCount"].Value = smoothingWindowCount;
        _module.Config.Params["EffectiveFilterTimeMs"].Value = effectiveFilterTimeMs;
        _module.Config.Params["EffectiveSampleCount"].Value = effectiveSampleCount;
        _module.Config.Params["CurrentAlpha"].Value = currentAlpha;
        _module.Config.Params["AcceptedRawValue"].Value = acceptedRawText;
        _module.Config.Params["DynamicSlope"].Value = dynamicSlope;
        _module.Config.Params["DynamicResidual"].Value = dynamicResidual;
        _module.Config.Params["DynamicThreshold"].Value = dynamicThreshold;
        _module.Config.Params["DynamicRawAngleDeg"].Value = dynamicRawAngleDegrees;
        _module.Config.Params["DynamicAngleDeg"].Value = dynamicAngleDegrees;
        _module.Config.Params["DynamicAngleThresholdDeg"].Value = dynamicThreshold;
        _module.Config.Params["DynamicAngleMaxDeg"].Value = dynamicMaxAngleDegrees;
        _module.Config.Params["DynamicRelativeChange"].Value = dynamicSlopeRatio;
        _module.Config.Params["DynamicReferenceValue"].Value = dynamicReferenceValue;
        _module.Config.Params["DynamicEffectiveReferenceValue"].Value = dynamicEffectiveReferenceValue;
        _module.Config.Params["DynamicNoiseReferenceValue"].Value = dynamicNoiseReferenceValue;
        _module.Config.Params["KalmanDynamicNormalizationMode"].Value = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
        _module.Config.Params["KalmanDynamicResidualWeight"].Value = _definition.KalmanDynamicResidualWeight;
        _module.Config.Params["ScanIntervalMs"].Value = _definition.ScanIntervalMs;
        _module.Config.Params["RecordInterval"].Value = _definition.RecordInterval;
        _module.Config.Params["DynamicBufferCount"].Value = dynamicBufferCount;
        _module.Config.Params["KalmanEnabled"].Value = kalmanEnabled;
        _module.Config.Params["KalmanMeasurementNoiseR"].Value = kalmanMeasurementNoiseR;
        _module.Config.Params["KalmanProcessNoiseQ"].Value = kalmanProcessNoiseQ;
        _module.Config.Params["KalmanEffectiveProcessNoiseQ"].Value = effectiveKalmanProcessNoiseQ;
        _module.Config.Params["KalmanInitialErrorCovarianceP"].Value = kalmanInitialErrorCovarianceP;
        _module.Config.Params["KalmanDynamicQEnabled"].Value = _definition.KalmanDynamicQEnabled;

        if (_definition.PeakFilter.Enabled)
        {
            _module["Peak"].Params["Enabled"].Value = true;
            _module["Peak"].Params["Threshold"].Value = _definition.PeakFilter.Threshold;
            _module["Peak"].Params["MaxLengthMs"].Value = _definition.PeakFilter.MaxLengthMs;
            _module["Peak"].Params["Active"].Value = peakActive;
            _module["Peak"].Params["Suppressed"].Value = peakSuppressed;
            _module["Peak"].Params["AcceptedRaw"].Value = acceptedRawText;
        }
        else if (_module.Has("Peak"))
        {
            _module.Remove("Peak");
        }

        if (ShouldPublishDynamicDiagnostics())
        {
            var dynamic = EnsureDynamicBranch();
            dynamic.Params["Enabled"].Value = true;
            dynamic.Params["DetectionWindowMs"].Value = _definition.KalmanEnabled ? _definition.KalmanDynamicDetectionWindowMs : _definition.DynamicFilter.DetectionWindowMs;
            dynamic.Params["AngleThresholdDeg"].Value = dynamicThreshold;
            dynamic.Params["AngleMaxDeg"].Value = dynamicMaxAngleDegrees;
            dynamic.Params["AppliedThreshold"].Value = dynamicThreshold;
            dynamic.Params["DynamicFilterTimeMs"].Value = _definition.DynamicFilter.DynamicFilterTimeMs;
            dynamic.Params["HoldTimeMs"].Value = _definition.DynamicFilter.HoldTimeMs;
            dynamic["Active"].Value = isDynamic;
            dynamic["Slope"].Value = dynamicSlope;
            dynamic["Residual"].Value = dynamicResidual;
            dynamic["RawAngleDeg"].Value = dynamicRawAngleDegrees;
            dynamic["AngleDeg"].Value = dynamicAngleDegrees;
            dynamic["RelativeChange"].Value = dynamicSlopeRatio;
            dynamic["ReferenceValue"].Value = dynamicReferenceValue;
            dynamic["EffectiveReferenceValue"].Value = dynamicEffectiveReferenceValue;
            dynamic["NoiseReferenceValue"].Value = dynamicNoiseReferenceValue;
            dynamic["NormalizationMode"].Value = dynamicNormalizationMode;
            dynamic["ResidualWeight"].Value = dynamicResidualWeight;
            dynamic["RemainingHoldMs"].Value = remainingHoldMs;
        }
        else if (_module.Has("Dynamic"))
        {
            _module.Remove("Dynamic");
        }

        if (kalmanEnabled || kalmanTeachActive || !string.Equals(kalmanTeachState, "Idle", StringComparison.OrdinalIgnoreCase))
        {
            var kalman = EnsureKalmanBranch();
            kalman.Params["Enabled"].Value = kalmanEnabled;
            kalman.Params["Estimate"].Value = kalmanEstimate;
            kalman.Params["Gain"].Value = kalmanGain;
            kalman.Params["Innovation"].Value = kalmanInnovation;
            kalman.Params["MeasurementNoiseR"].Value = kalmanMeasurementNoiseR;
            kalman.Params["ProcessNoiseQ"].Value = kalmanProcessNoiseQ;
            kalman.Params["EffectiveProcessNoiseQ"].Value = effectiveKalmanProcessNoiseQ;
            kalman.Params["InitialErrorCovarianceP"].Value = kalmanInitialErrorCovarianceP;
            kalman.Params["TeachActive"].Value = kalmanTeachActive;
            kalman.Params["TeachState"].Value = kalmanTeachState;
            kalman.Params["TeachProgress"].Value = kalmanTeachProgress;
            kalman.Params["TeachSampleCount"].Value = kalmanTeachSampleCount;
            kalman.Params["LearnedMeasurementNoiseR"].Value = kalmanTeachLearnedMeasurementNoiseR;
            kalman.Params["DerivedProcessNoiseQ"].Value = kalmanTeachDerivedProcessNoiseQ;
            kalman.Params["AdaptiveQActive"].Value = adaptiveKalmanQActive;
            kalman.Params["AdaptiveQHoldRemainingMs"].Value = adaptiveKalmanQHoldRemainingMs;
            kalman.Params["AdaptiveQMin"].Value = adaptiveKalmanQMin;
            kalman.Params["AdaptiveQMax"].Value = adaptiveKalmanQMax;
            kalman.Params["InnovationAbs"].Value = kalmanInnovationAbs;
            kalman.Params["AdaptiveQIntensity"].Value = adaptiveKalmanQIntensity;
            kalman.Params["DynamicSlopeRatio"].Value = dynamicSlopeRatio;
            kalman.Params["DynamicRawAngleDeg"].Value = dynamicRawAngleDegrees;
            kalman.Params["DynamicAngleDeg"].Value = dynamicAngleDegrees;
            kalman.Params["DynamicReferenceValue"].Value = dynamicReferenceValue;
            kalman.Params["DynamicEffectiveReferenceValue"].Value = dynamicEffectiveReferenceValue;
            kalman.Params["DynamicNoiseReferenceValue"].Value = dynamicNoiseReferenceValue;
            kalman.Params["DynamicMaxSlope"].Value = dynamicMaxSlope;
            kalman.Params["DynamicMaxAngleDeg"].Value = dynamicMaxAngleDegrees;
            kalman.Params["DynamicTrendConfidence"].Value = dynamicTrendConfidence;
            kalman.Params["DynamicNormalizationMode"].Value = dynamicNormalizationMode;
            kalman.Params["DynamicResidualWeight"].Value = dynamicResidualWeight;
            kalman["Estimate"].Value = kalmanEstimate;
            kalman["MeasurementNoiseR"].Value = kalmanMeasurementNoiseR;
            kalman["ProcessNoiseQ"].Value = kalmanProcessNoiseQ;
            kalman["EffectiveProcessNoiseQ"].Value = effectiveKalmanProcessNoiseQ;
            kalman["AdaptiveQActive"].Value = adaptiveKalmanQActive;
            kalman["AdaptiveQHoldRemainingMs"].Value = adaptiveKalmanQHoldRemainingMs;
            kalman["DynamicTriggerActive"].Value = isDynamic;
            kalman["InnovationAbs"].Value = kalmanInnovationAbs;
            kalman["AdaptiveQIntensity"].Value = adaptiveKalmanQIntensity;
            kalman["DynamicSlopeRatio"].Value = dynamicSlopeRatio;
            kalman["DynamicRawAngleDeg"].Value = dynamicRawAngleDegrees;
            kalman["DynamicAngleDeg"].Value = dynamicAngleDegrees;
            kalman["DynamicReferenceValue"].Value = dynamicReferenceValue;
            kalman["DynamicEffectiveReferenceValue"].Value = dynamicEffectiveReferenceValue;
            kalman["DynamicNoiseReferenceValue"].Value = dynamicNoiseReferenceValue;
            kalman["DynamicMaxSlope"].Value = dynamicMaxSlope;
            kalman["DynamicMaxAngleDeg"].Value = dynamicMaxAngleDegrees;
            kalman["DynamicTrendConfidence"].Value = dynamicTrendConfidence;
            kalman["DynamicNormalizationMode"].Value = dynamicNormalizationMode;
            kalman["DynamicResidualWeight"].Value = dynamicResidualWeight;
        }
        else if (_module.Has("Kalman"))
        {
            _module.Remove("Kalman");
        }

        if (statistics.Enabled)
        {
            PublishStatisticsSnapshot(statistics);
        }
        else if (_module.Has("Statistics"))
        {
            _module.Remove("Statistics");
        }
    }

    private StatisticsSnapshot CaptureStatisticsSnapshot(DateTimeOffset now)
    {
        if (!_definition.Statistics.Enabled)
        {
            return StatisticsSnapshot.Disabled;
        }

        const int retentionWindowMs = 0;
        var stdDevWindowMs = Math.Max(1, _definition.Statistics.StdDevWindowMs);
        var stdDevSamples = GetStatisticsSamples(now, stdDevWindowMs);

        return new StatisticsSnapshot(
            Enabled: true,
            PublishMin: _definition.Statistics.PublishMin,
            PublishMax: _definition.Statistics.PublishMax,
            PublishAverage: _definition.Statistics.PublishAverage,
            PublishStdDev: _definition.Statistics.PublishStdDev,
            PublishIntegral: _definition.Statistics.PublishIntegral,
            RetentionWindowMs: retentionWindowMs,
            StdDevWindowMs: stdDevWindowMs,
            IntegralDivisorMs: _definition.Statistics.IntegralDivisorMs,
            MinValue: _statisticsMinValue,
            MinTimestampUnixMs: _statisticsMinTimestamp?.ToUnixTimeMilliseconds(),
            MaxValue: _statisticsMaxValue,
            MaxTimestampUnixMs: _statisticsMaxTimestamp?.ToUnixTimeMilliseconds(),
            AverageValue: _statisticsAverageSampleCount > 0 ? _statisticsAverageMean : null,
            StdDevValue: ComputeStandardDeviation(stdDevSamples),
            IntegralValue: GetRunningStatisticsIntegral(_definition.Statistics.IntegralDivisorMs));
    }

    private bool TryApplyInverseAdjustment(object? value, out object? result, out string error)
    {
        error = string.Empty;
        result = value;

        if (!_definition.Adjustment.Enabled)
        {
            return true;
        }

        var numericValue = ToNullableDouble(value);
        if (numericValue is null)
        {
            error = "Set forwarding expects a numeric value";
            return false;
        }

        var correction = _definition.Adjustment;
        if (!correction.SupportsInverseMapping)
        {
            error = "Inverse mapping not supported";
            return false;
        }

        if (Math.Abs(correction.Gain) < double.Epsilon)
        {
            error = "Inverse mapping requires Gain != 0";
            return false;
        }

        var affineInverted = (numericValue.Value - correction.Offset) / correction.Gain;
        switch (correction.MappingMode)
        {
            case ExtendedSignalAdjustmentMode.None:
                result = affineInverted;
                return true;
            case ExtendedSignalAdjustmentMode.Spline:
                if (!TryApplyInverseSpline(
                    affineInverted,
                    correction.SplinePoints,
                    correction.SplineInterpolationMode,
                    out var splineInverted,
                    out error))
                {
                    return false;
                }

                result = splineInverted;
                return true;
            default:
                error = $"Inverse mapping for {correction.MappingMode} is not implemented";
                return false;
        }
    }

    private bool UpdateTargetValue(string configuredPath, object? value)
    {
        foreach (var candidate in EnhancedSignalPathHelper.EnumerateResolutionCandidates(configuredPath, _folderName))
        {
            if (HostRegistries.Data.UpdateValue(candidate, value))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryForwardChildWrite(DataChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_definition.SourcePath))
        {
            return false;
        }

        var relativePath = TryGetRelativePath(e.Key);
        if (string.IsNullOrWhiteSpace(relativePath) || !IsForwardedChildPath(relativePath))
        {
            return false;
        }

        if (e.ChangeKind == DataChangeKind.ParameterUpdated)
        {
            if (string.IsNullOrWhiteSpace(e.ParameterName)
                || string.Equals(e.ParameterName, "Value", StringComparison.Ordinal)
                || !e.Item.Params.Has(e.ParameterName))
            {
                return false;
            }

            return ForwardChildParameter(relativePath, e.ParameterName, e.Item.Params[e.ParameterName].Value, e.Timestamp);
        }

        if (e.ChangeKind != DataChangeKind.ValueUpdated)
        {
            return false;
        }

        return ForwardChildValue(relativePath, ExtractValue(e.Item), e.Timestamp);
    }

    private bool ForwardChildValue(string relativePath, object? value, ulong? timestamp = null)
    {
        foreach (var candidate in EnumerateForwardTargetCandidates(relativePath))
        {
            if (HostRegistries.Data.UpdateValue(candidate, value, timestamp))
            {
                return true;
            }
        }

        return false;
    }

    private bool ForwardChildParameter(string relativePath, string parameterName, object? value, ulong? timestamp)
    {
        foreach (var candidate in EnumerateForwardTargetCandidates(relativePath))
        {
            if (HostRegistries.Data.UpdateParameter(candidate, parameterName, value, timestamp))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> EnumerateForwardTargetCandidates(string relativePath)
    {
        var targetPath = JoinNormalizedPath(_definition.SourcePath, relativePath);
        return EnhancedSignalPathHelper.EnumerateResolutionCandidates(targetPath, _folderName);
    }

    private string TryGetRelativePath(string path)
    {
        var normalizedPath = EnhancedSignalPathHelper.SplitPathSegments(path);
        var normalizedRoot = EnhancedSignalPathHelper.SplitPathSegments(_registryPath);
        if (normalizedPath.Count <= normalizedRoot.Count)
        {
            return string.Empty;
        }

        for (var index = 0; index < normalizedRoot.Count; index++)
        {
            if (!string.Equals(normalizedPath[index], normalizedRoot[index], StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }

        return string.Join('.', normalizedPath.Skip(normalizedRoot.Count));
    }

    private static bool IsForwardedChildPath(string relativePath)
    {
        var segments = EnhancedSignalPathHelper.SplitPathSegments(relativePath);
        if (segments.Count == 0)
        {
            return false;
        }

        return ForwardedChannelNames.Contains(segments[0], StringComparer.OrdinalIgnoreCase);
    }

    private static string JoinNormalizedPath(string left, string right)
    {
        var normalizedLeft = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(left);
        var normalizedRight = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(right);
        if (string.IsNullOrWhiteSpace(normalizedLeft))
        {
            return normalizedRight;
        }

        if (string.IsNullOrWhiteSpace(normalizedRight))
        {
            return normalizedLeft;
        }

        return $"{normalizedLeft}.{normalizedRight}";
    }

    private void ApplyConfiguration()
    {
        _module.Params["Title"].Value = _definition.Name;
        _module.Params["Unit"].Value = _definition.Unit;
        _module.Params["Format"].Value = _definition.Format;
        _module.Params["PrimaryValuePath"].Value = _registryPath;
        _module.Raw.Params["Text"].Value = $"{_definition.Name} Raw";
        _module.Read.Params["Text"].Value = $"{_definition.Name} Read";
        _module.Set.Params["Text"].Value = $"{_definition.Name} Set";
        _module.Out.Params["Text"].Value = $"{_definition.Name} Out (reserved)";
        _module.State.Params["Text"].Value = $"{_definition.Name} State";
        _module.Alert.Params["Text"].Value = $"{_definition.Name} Alert";
        _module.Command.Params["Text"].Value = $"{_definition.Name} Command";
        _module.Config.Params["Text"].Value = $"{_definition.Name} Config";
        _module.Params["Kind"].Value = "EnhancedSignal";
        _module.Params["Writable"].Value = _definition.IsWritable;
        _module.Params["WritePath"].Value = ResolveEffectiveWritePath();
        _module.Params["WriteMode"].Value = _definition.WriteMode.ToString();
        _module.Config.Params["Enabled"].Value = _definition.Enabled;
        _module.Config.Params["SourcePath"].Value = _definition.SourcePath;
        _module.Config.Params["ForwardChildWritesToSource"].Value = _definition.ForwardChildWritesToSource;
        _module.Config.Params["FilterMode"].Value = _definition.FilterMode.ToString();
        _module.Config.Params["ScanIntervalMs"].Value = _definition.ScanIntervalMs;
        _module.Config.Params["RecordInterval"].Value = _definition.RecordInterval;
        _module.Config.Params["FilterTimeMs"].Value = _definition.FilterTimeMs;
        _module.Config.Params["FillMissingWithLastValue"].Value = _definition.FillMissingWithLastValue;
        _module.Config.Params["KalmanEnabled"].Value = _definition.KalmanEnabled;
        _module.Config.Params["KalmanMeasurementNoiseR"].Value = _definition.KalmanMeasurementNoiseR;
        _module.Config.Params["KalmanProcessNoiseQ"].Value = _definition.KalmanProcessNoiseQ;
        _module.Config.Params["KalmanInitialErrorCovarianceP"].Value = _definition.KalmanInitialErrorCovarianceP;
        _module.Config.Params["KalmanTeachWindowMs"].Value = _definition.KalmanTeachWindowMs;
        _module.Config.Params["KalmanTeachPauseOnDynamic"].Value = _definition.KalmanTeachPauseOnDynamic;
        _module.Config.Params["KalmanTeachQFactor"].Value = _definition.KalmanTeachQFactor;
        _module.Config.Params["KalmanTeachAutoApply"].Value = _definition.KalmanTeachAutoApply;
        _module.Config.Params["KalmanDynamicQEnabled"].Value = _definition.KalmanDynamicQEnabled;
        _module.Config.Params["KalmanDynamicQMin"].Value = _definition.KalmanDynamicQMin;
        _module.Config.Params["KalmanDynamicQMax"].Value = _definition.KalmanDynamicQMax;
        _module.Config.Params["KalmanDynamicQHoldMs"].Value = _definition.KalmanDynamicQHoldMs;
        _module.Config.Params["KalmanDynamicDetectionWindowMs"].Value = _definition.KalmanDynamicDetectionWindowMs;
        _module.Config.Params["KalmanDynamicAngleThresholdDeg"].Value = _definition.KalmanDynamicAngleThresholdDeg;
        _module.Config.Params["KalmanDynamicAngleMaxDeg"].Value = _definition.KalmanDynamicAngleMaxDeg;
        _module.Config.Params["KalmanDynamicReferenceFloor"].Value = _definition.KalmanDynamicReferenceFloor;
        _module.Config.Params["KalmanDynamicNormalizationMode"].Value = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
        _module.Config.Params["KalmanDynamicResidualWeight"].Value = _definition.KalmanDynamicResidualWeight;
        _module.Config.Params["KalmanDynamicQUseExistingDynamic"].Value = _definition.KalmanDynamicQUseExistingDynamic;
        _module.Config.Params["AdjustmentMode"].Value = _definition.Adjustment.MappingMode.ToString();
        _module.Config.Params["AdjustmentMappingMode"].Value = _definition.Adjustment.MappingMode.ToString();
        _module.Config.Params["AdjustmentOffset"].Value = _definition.Adjustment.Offset;
        _module.Config.Params["AdjustmentGain"].Value = _definition.Adjustment.Gain;
        _module.Config.Params["AdjustmentSplineInterpolationMode"].Value = _definition.Adjustment.SplineInterpolationMode.ToString();
        _module.Config.Params["SupportsInverseMapping"].Value = _definition.Adjustment.SupportsInverseMapping;
        _module.Config.Params["PipelineMode"].Value = "TwoStage";

        var adjustment = EnsureAdjustmentBranch();
        adjustment["Enabled"].Value = _definition.Adjustment.Enabled;
        adjustment["MappingMode"].Value = _definition.Adjustment.MappingMode.ToString();
        adjustment["Offset"].Value = _definition.Adjustment.Offset;
        adjustment["Gain"].Value = _definition.Adjustment.Gain;
        adjustment["SplineInterpolationMode"].Value = _definition.Adjustment.SplineInterpolationMode.ToString();
        adjustment["SupportsInverseMapping"].Value = _definition.Adjustment.SupportsInverseMapping;
        adjustment["Spline"].Value = JsonSerializer.Serialize(_definition.Adjustment.SplinePoints);

        if (_definition.KalmanEnabled)
        {
            EnsureKalmanBranch();
        }

        Core.LogDebug($"[EnhancedSignal:{_definition.Name}] configured folder={_folderName} source={_definition.SourcePath} mode={_definition.FilterMode} filterTimeMs={_definition.FilterTimeMs}");
    }

    private bool ShouldRecordFilterSample()
    {
        var recordInterval = Math.Max(1, _definition.RecordInterval);
        _recordIntervalCounter++;
        if (_recordIntervalCounter >= recordInterval)
        {
            _recordIntervalCounter = 0;
            return true;
        }

        return false;
    }

    private void PublishSnapshot()
    {
        HostRegistries.Data.UpsertSnapshot(_registryPath, _module, pruneMissingMembers: true);
    }

    private string ResolveEffectiveWritePath()
    {
        if (!_definition.IsWritable)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(_definition.WritePath))
        {
            return _definition.WritePath;
        }

        return _definition.WriteMode == SignalWriteMode.Request
            ? _registryPath + ".Set"
            : _definition.SourcePath;
    }

    private void SetAlert(string message)
    {
        _isUpdating = true;
        try
        {
            _module.State.Value = "Alert";
            _module.Alert.Value = message;
            PublishSnapshot();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void TrimSamples(DateTimeOffset now)
    {
        TrimSampleQueue(_samples, now, GetRetentionWindowMs(), GetRetainedSampleLimit());
        TrimSampleQueue(_dynamicSamples, now, GetDynamicRetentionWindowMs(), 0);
    }

    private static void TrimSampleQueue(Queue<SignalSample> queue, DateTimeOffset now, int retentionTimeMs, int sampleLimit)
    {
        var minimumTimestamp = retentionTimeMs > 0 ? now.AddMilliseconds(-retentionTimeMs) : DateTimeOffset.MinValue;
        while (queue.Count > 0)
        {
            if (sampleLimit > 0 && queue.Count > sampleLimit)
            {
                queue.Dequeue();
                continue;
            }

            if (queue.Count > 1 && queue.Peek().Timestamp < minimumTimestamp)
            {
                queue.Dequeue();
                continue;
            }

            break;
        }
    }

    private int GetRetainedSampleLimit()
    {
        if (GetRetentionWindowMs() > 0)
        {
            return 0;
        }

        return 1;
    }

    private int GetRetentionWindowMs()
    {
        return new[]
        {
            _definition.FilterTimeMs,
            _definition.Statistics.Enabled ? _definition.Statistics.StdDevWindowMs : 0,
            _definition.DynamicFilter.Enabled ? _definition.DynamicFilter.DynamicFilterTimeMs : 0,
            GetTargetSmoothingWindowMs()
        }.Max();
    }

    private int GetDynamicRetentionWindowMs()
    {
        var classicWindowMs = _definition.DynamicFilter.Enabled ? Math.Max(100, _definition.DynamicFilter.DetectionWindowMs) : 0;
        var kalmanWindowMs = IsKalmanDynamicDetectionEnabled() ? Math.Max(100, _definition.KalmanDynamicDetectionWindowMs) : 0;
        return Math.Max(classicWindowMs, kalmanWindowMs);
    }

    private int GetEffectiveFilterTimeMs()
    {
        return GetEffectiveFilterTimeMs(DateTimeOffset.UtcNow);
    }

    private int GetEffectiveFilterTimeMs(DateTimeOffset now)
    {
        var isDynamic = _classicDynamicUntil is not null && now <= _classicDynamicUntil.Value;
        if (isDynamic && _definition.DynamicFilter.Enabled && _definition.DynamicFilter.DynamicFilterTimeMs > 0)
        {
            return _definition.DynamicFilter.DynamicFilterTimeMs;
        }

        return _definition.FilterTimeMs;
    }

    private void UpdateSmoothingWindowState(DateTimeOffset now)
    {
        var effectiveFilterTimeMs = GetEffectiveFilterTimeMs(now);
        if (_lastEffectiveFilterTimeMs > 0 && effectiveFilterTimeMs > _lastEffectiveFilterTimeMs)
        {
            _smoothingHistoryCutoff = now.AddMilliseconds(-_lastEffectiveFilterTimeMs);
        }
        else if (_smoothingHistoryCutoff is not null && effectiveFilterTimeMs > 0 && _smoothingHistoryCutoff.Value <= now.AddMilliseconds(-effectiveFilterTimeMs))
        {
            _smoothingHistoryCutoff = null;
        }
        else if (effectiveFilterTimeMs <= 0)
        {
            _smoothingHistoryCutoff = null;
        }

        _lastEffectiveFilterTimeMs = effectiveFilterTimeMs;
    }

    private IReadOnlyList<SignalSample> GetSmoothingSamples(DateTimeOffset now, double? paddingValue = null)
    {
        var sampleArray = _samples.ToArray();
        if (sampleArray.Length == 0)
        {
            return sampleArray;
        }

        var effectiveFilterTimeMs = GetEffectiveFilterTimeMs(now);
        if (effectiveFilterTimeMs > 0)
        {
            var minimumTimestamp = now.AddMilliseconds(-effectiveFilterTimeMs);
            var smoothingHistoryCutoff = _smoothingHistoryCutoff;
            var useTransitionPadding = smoothingHistoryCutoff is not null && smoothingHistoryCutoff.Value > minimumTimestamp;
            if (useTransitionPadding)
            {
                minimumTimestamp = smoothingHistoryCutoff!.Value;
            }

            var selectedSamples = sampleArray
                .Where(sample => sample.Timestamp >= minimumTimestamp)
                .ToArray();

            var targetCount = GetTargetSmoothingSampleCount(effectiveFilterTimeMs);
            var shouldPadSamples = _definition.FillMissingWithLastValue
                && targetCount > 0
                && selectedSamples.Length < targetCount;

            if (!useTransitionPadding && !shouldPadSamples)
            {
                return selectedSamples;
            }

            return NormalizeSmoothingSamples(selectedSamples, targetCount, now, paddingValue ?? sampleArray[^1].Value);
        }

        return [sampleArray[^1]];
    }

    private IReadOnlyList<SignalSample> GetStatisticsSamples(DateTimeOffset now, int windowMs)
    {
        var sampleArray = _samples.ToArray();
        if (sampleArray.Length == 0)
        {
            return Array.Empty<SignalSample>();
        }

        if (_statisticsResetAt is not null)
        {
            sampleArray = sampleArray
                .Where(sample => sample.Timestamp >= _statisticsResetAt.Value)
                .ToArray();
            if (sampleArray.Length == 0)
            {
                return Array.Empty<SignalSample>();
            }
        }

        if (windowMs <= 0)
        {
            return sampleArray;
        }

        var minimumTimestamp = now.AddMilliseconds(-windowMs);
        return sampleArray
            .Where(sample => sample.Timestamp >= minimumTimestamp)
            .ToArray();
    }

    private IReadOnlyList<SignalSample> NormalizeSmoothingSamples(IReadOnlyList<SignalSample> samples, int targetCount, DateTimeOffset now, double paddingValue)
    {
        if (targetCount <= 0)
        {
            return samples.ToArray();
        }

        if (samples.Count >= targetCount)
        {
            return samples.Skip(samples.Count - targetCount).ToArray();
        }

        if (!_definition.FillMissingWithLastValue)
        {
            return samples.ToArray();
        }

        return PadSamples(samples, targetCount, now, paddingValue);
    }

    private IReadOnlyList<SignalSample> PadSamples(IReadOnlyList<SignalSample> samples, int targetCount, DateTimeOffset now, double paddingValue)
    {
        if (samples.Count >= targetCount)
        {
            return samples.ToArray();
        }

        var intervalMs = GetFilterRecordIntervalMs();
        var paddedSamples = new List<SignalSample>(targetCount);
        var missingCount = targetCount - samples.Count;
        var anchorTimestamp = samples.Count > 0
            ? samples[0].Timestamp
            : now;

        for (var index = missingCount; index > 0; index--)
        {
            paddedSamples.Add(new SignalSample(anchorTimestamp.AddMilliseconds(-intervalMs * index), paddingValue));
        }

        paddedSamples.AddRange(samples);
        return paddedSamples;
    }

    private int GetTargetSmoothingSampleCount(int effectiveFilterTimeMs)
    {
        if (effectiveFilterTimeMs > 0)
        {
            var intervalMs = GetFilterRecordIntervalMs();
            return Math.Max(1, (int)Math.Ceiling((double)effectiveFilterTimeMs / intervalMs));
        }

        return 1;
    }

    private static double ApplyWeightedAverage(IEnumerable<SignalSample> samples)
    {
        var sampleArray = samples.ToArray();
        if (sampleArray.Length == 0)
        {
            return 0d;
        }

        var weight = 1d;
        var totalWeight = 0d;
        var weightedSum = 0d;
        foreach (var sample in sampleArray)
        {
            weightedSum += sample.Value * weight;
            totalWeight += weight;
            weight += 1d;
        }

        return totalWeight <= 0d ? 0d : weightedSum / totalWeight;
    }

    private static double? ComputeStandardDeviation(IReadOnlyList<SignalSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        var average = samples.Average(sample => sample.Value);
        var variance = samples.Average(sample => Math.Pow(sample.Value - average, 2d));
        return Math.Sqrt(Math.Max(0d, variance));
    }

    private void ResetStatisticsState(DateTimeOffset? resetAt = null)
    {
        _statisticsResetAt = resetAt;
        _statisticsMinValue = null;
        _statisticsMinTimestamp = null;
        _statisticsMaxValue = null;
        _statisticsMaxTimestamp = null;
        _statisticsAverageSampleCount = 0;
        _statisticsAverageMean = 0d;
        _lastStatisticsIntegralSample = null;
        _statisticsIntegralAccumulatedRaw = null;
    }

    private void AccumulateRunningStatistics(SignalSample currentSample)
    {
        if (!_definition.Statistics.Enabled)
        {
            return;
        }

        if (_statisticsMinValue is null || currentSample.Value < _statisticsMinValue.Value)
        {
            _statisticsMinValue = currentSample.Value;
            _statisticsMinTimestamp = currentSample.Timestamp;
        }

        if (_statisticsMaxValue is null || currentSample.Value > _statisticsMaxValue.Value)
        {
            _statisticsMaxValue = currentSample.Value;
            _statisticsMaxTimestamp = currentSample.Timestamp;
        }

        _statisticsAverageSampleCount++;
        _statisticsAverageMean += (currentSample.Value - _statisticsAverageMean) / _statisticsAverageSampleCount;

        AccumulateStatisticsIntegral(currentSample);
    }

    private void AccumulateStatisticsIntegral(SignalSample currentSample)
    {
        if (!_definition.Statistics.Enabled)
        {
            return;
        }

        if (_lastStatisticsIntegralSample is not SignalSample previousSample)
        {
            _lastStatisticsIntegralSample = currentSample;
            return;
        }

        var deltaMs = (currentSample.Timestamp - previousSample.Timestamp).TotalMilliseconds;
        if (deltaMs <= 0d)
        {
            _lastStatisticsIntegralSample = currentSample;
            return;
        }

        var integralContribution = ((previousSample.Value + currentSample.Value) * 0.5d) * deltaMs;
        _statisticsIntegralAccumulatedRaw = (_statisticsIntegralAccumulatedRaw ?? 0d) + integralContribution;
        _lastStatisticsIntegralSample = currentSample;
    }

    private double? GetRunningStatisticsIntegral(double integralDivisorMs)
    {
        return _statisticsIntegralAccumulatedRaw is double rawIntegral
            ? rawIntegral / integralDivisorMs
            : null;
    }

    private double ApplyExponentialAverage(double currentValue, bool captureSample, int smoothingSampleCount)
    {
        var effectiveSampleCount = GetEffectiveSmoothingSampleCount(smoothingSampleCount);
        var alpha = 2d / (effectiveSampleCount + 1d);
        _currentSmoothingAlpha = alpha;
        if (_emaValue is null)
        {
            _emaValue = currentValue;
        }
        else if (captureSample)
        {
            _emaValue = _emaValue.Value + alpha * (currentValue - _emaValue.Value);
        }

        return _emaValue.Value;
    }

    private double ApplyKalmanFilter(double currentValue, bool captureSample, DateTimeOffset now)
    {
        UpdateKalmanTeachState(currentValue, captureSample, now, IsKalmanDynamicActive(now));

        if (!captureSample)
        {
            return _kalmanEstimate ?? currentValue;
        }

        var measurementNoiseR = Math.Max(0.000000001d, _definition.KalmanMeasurementNoiseR);
        var processNoiseQ = GetEffectiveKalmanProcessNoiseQ(now);
        var initialErrorCovarianceP = Math.Max(0.000000001d, _definition.KalmanInitialErrorCovarianceP);

        var predictedEstimate = _kalmanEstimate ?? currentValue;
        var predictedErrorCovariance = (_kalmanErrorCovariance ?? initialErrorCovarianceP) + processNoiseQ;
        var innovation = currentValue - predictedEstimate;
        var denominator = predictedErrorCovariance + measurementNoiseR;
        var kalmanGain = denominator <= double.Epsilon ? 0d : predictedErrorCovariance / denominator;
        var updatedEstimate = predictedEstimate + (kalmanGain * innovation);
        var updatedErrorCovariance = Math.Max(0.000000001d, (1d - kalmanGain) * predictedErrorCovariance);

        _kalmanEstimate = updatedEstimate;
        _kalmanErrorCovariance = updatedErrorCovariance;
        _lastKalmanGain = kalmanGain;
        _lastKalmanInnovation = innovation;
        _lastKalmanInnovationAbs = Math.Abs(innovation);

        return updatedEstimate;
    }

    private double GetEffectiveKalmanProcessNoiseQ(DateTimeOffset now)
    {
        var baseQ = Math.Max(0.000000001d, _definition.KalmanProcessNoiseQ);
        var minQ = Math.Max(0.000000001d, _definition.KalmanDynamicQMin);
        var maxQ = Math.Max(minQ, _definition.KalmanDynamicQMax);

        if (!_definition.KalmanDynamicQEnabled)
        {
            _adaptiveKalmanQActive = false;
            _adaptiveKalmanQUntil = null;
            _adaptiveKalmanQIntensity = 0d;
            _effectiveKalmanProcessNoiseQ = baseQ;
            return baseQ;
        }

        var holdMs = Math.Max(0, _definition.KalmanDynamicQHoldMs);
        var minAngle = NormalizeDynamicAngleThreshold(_lastDynamicThreshold);
        var maxAngle = Math.Max(minAngle, _lastDynamicMaxAngleDegrees);
        var normalizedRelativeChange = _lastDynamicAngleDegrees <= minAngle
            ? 0d
            : (_lastDynamicAngleDegrees - minAngle) / Math.Max(0.000000001d, maxAngle - minAngle);
        var rawIntensity = Math.Clamp(normalizedRelativeChange, 0d, 1d) * Math.Clamp(_lastDynamicTrendConfidence, 0d, 1d);

        if (IsKalmanDynamicActive(now))
        {
            _adaptiveKalmanQUntil = holdMs > 0 ? now.AddMilliseconds(holdMs) : null;
        }

        var holdIntensity = 0d;
        if (_adaptiveKalmanQUntil is not null && now <= _adaptiveKalmanQUntil.Value && holdMs > 0)
        {
            holdIntensity = Math.Clamp((_adaptiveKalmanQUntil.Value - now).TotalMilliseconds / holdMs, 0d, 1d);
        }
        else
        {
            _adaptiveKalmanQUntil = null;
        }

        var targetIntensity = Math.Max(rawIntensity, holdIntensity);
        var responseAlpha = targetIntensity >= _adaptiveKalmanQIntensity ? 0.35d : 0.12d;
        _adaptiveKalmanQIntensity += (targetIntensity - _adaptiveKalmanQIntensity) * responseAlpha;
        _adaptiveKalmanQIntensity = Math.Clamp(_adaptiveKalmanQIntensity, 0d, 1d);
        _adaptiveKalmanQActive = _adaptiveKalmanQIntensity > 0.01d;

        if (!_adaptiveKalmanQActive)
        {
            _effectiveKalmanProcessNoiseQ = minQ;
            return minQ;
        }

        _effectiveKalmanProcessNoiseQ = minQ + ((maxQ - minQ) * _adaptiveKalmanQIntensity);
        return _effectiveKalmanProcessNoiseQ;
    }

    private int GetEffectiveSmoothingSampleCount(int smoothingSampleCount)
    {
        return Math.Max(1, smoothingSampleCount);
    }

    private IEnumerable<SignalSample> ProjectExponentialSeries(IEnumerable<SignalSample> samples)
    {
        double? ema = null;
        var sampleArray = samples.ToArray();
        if (sampleArray.Length == 0)
        {
            yield break;
        }

        var alpha = 2d / (GetEffectiveSmoothingSampleCount(sampleArray.Length) + 1d);
        foreach (var sample in sampleArray)
        {
            ema = ema is null ? sample.Value : (ema.Value + alpha * (sample.Value - ema.Value));
            yield return new SignalSample(sample.Timestamp, ema.Value);
        }
    }

    private static int ComputeSampleIntervalMs(ExtendedSignalDefinition definition)
    {
        if (definition.ScanIntervalMs > 0)
        {
            return Math.Max(10, definition.ScanIntervalMs);
        }

        if (definition.FilterTimeMs > 0)
        {
            return Math.Max(10, definition.FilterTimeMs);
        }

        return 100;
    }

    private int GetFilterRecordIntervalMs()
    {
        return Math.Max(1, ComputeSampleIntervalMs(_definition) * Math.Max(1, _definition.RecordInterval));
    }

    private int GetTargetSmoothingWindowMs()
    {
        if (_definition.FilterTimeMs > 0)
        {
            return _definition.FilterTimeMs;
        }

        return 0;
    }

    private void StartKalmanTeach(DateTimeOffset now)
    {
        _kalmanTeachActive = true;
        _kalmanTeachState = "Active";
        _kalmanTeachStartedAt = now;
        _kalmanTeachLastTickAt = null;
        _kalmanTeachAccumulatedMs = 0d;
        _kalmanTeachSampleCount = 0;
        _kalmanTeachMean = 0d;
        _kalmanTeachM2 = 0d;
        _kalmanTeachLearnedMeasurementNoiseR = 0d;
        _kalmanTeachDerivedProcessNoiseQ = 0d;
    }

    private void UpdateKalmanTeachState(double currentValue, bool captureSample, DateTimeOffset now, bool isDynamic)
    {
        if (!_kalmanTeachActive || !captureSample)
        {
            return;
        }

        var previousTick = _kalmanTeachLastTickAt;
        _kalmanTeachLastTickAt = now;
        var deltaMs = previousTick is null
            ? ComputeSampleIntervalMs(_definition)
            : Math.Max(0d, (now - previousTick.Value).TotalMilliseconds);

        if (_definition.KalmanTeachPauseOnDynamic && isDynamic)
        {
            _kalmanTeachState = "PausedDynamic";
            return;
        }

        _kalmanTeachState = "Active";
        _kalmanTeachAccumulatedMs += deltaMs;
        _kalmanTeachSampleCount++;

        var delta = currentValue - _kalmanTeachMean;
        _kalmanTeachMean += delta / _kalmanTeachSampleCount;
        _kalmanTeachM2 += delta * (currentValue - _kalmanTeachMean);

        if (_kalmanTeachAccumulatedMs >= Math.Max(1, _definition.KalmanTeachWindowMs))
        {
            FinalizeKalmanTeach(applyResults: true);
        }
    }

    private void FinalizeKalmanTeach(bool applyResults)
    {
        if (!_kalmanTeachActive)
        {
            return;
        }

        _kalmanTeachActive = false;
        _kalmanTeachLastTickAt = null;

        if (_kalmanTeachSampleCount < 2)
        {
            _kalmanTeachState = "NotEnoughSamples";
            return;
        }

        var learnedMeasurementNoiseR = Math.Max(0.000000001d, _kalmanTeachM2 / (_kalmanTeachSampleCount - 1));
        var derivedProcessNoiseQ = Math.Max(0.000000001d, learnedMeasurementNoiseR * Math.Max(0d, _definition.KalmanTeachQFactor));

        _kalmanTeachLearnedMeasurementNoiseR = learnedMeasurementNoiseR;
        _kalmanTeachDerivedProcessNoiseQ = derivedProcessNoiseQ;
        _definition.KalmanMeasurementNoiseR = learnedMeasurementNoiseR;
        _definition.KalmanProcessNoiseQ = derivedProcessNoiseQ;
        ApplyConfiguration();

        if (applyResults && _definition.KalmanTeachAutoApply)
        {
            _pendingPersistedDefinition = _definition.Clone();
            _kalmanTeachState = "CompletedAutoApply";
            return;
        }

        _kalmanTeachState = applyResults ? "Completed" : "Stopped";
    }

    private void ResetKalmanRuntimeState(bool clearTeachState)
    {
        _kalmanEstimate = null;
        _kalmanErrorCovariance = null;
        _lastKalmanGain = 0d;
        _lastKalmanInnovation = 0d;
        _lastKalmanInnovationAbs = 0d;
        _effectiveKalmanProcessNoiseQ = _definition.KalmanDynamicQEnabled
            ? Math.Max(0.000000001d, _definition.KalmanDynamicQMin)
            : Math.Max(0.000000001d, _definition.KalmanProcessNoiseQ);
        _adaptiveKalmanQActive = false;
        _adaptiveKalmanQUntil = null;
        _adaptiveKalmanQIntensity = 0d;
        _lastDynamicReferenceValue = 0d;
        _lastDynamicEffectiveReferenceValue = 0d;
        _lastDynamicNoiseReferenceValue = 0d;
        _lastDynamicMaxSlope = 0d;
        _lastDynamicTrendConfidence = 0d;
        _lastDynamicRelativeChange = 0d;
        _lastDynamicRawAngleDegrees = 0d;
        _lastDynamicAngleDegrees = 0d;
        _lastDynamicMaxAngleDegrees = NormalizeDynamicAngleThreshold(_definition.KalmanDynamicAngleMaxDeg);
        _lastDynamicNormalizationMode = KalmanDynamicNormalizationMode.HybridReferenceFloor.ToString();
        _lastDynamicResidualWeight = Math.Clamp(_definition.KalmanDynamicResidualWeight, 0d, 1d);
        _lastDiagnosticsPublishedAt = null;
        _lastPublishedDynamicState = false;
        _lastPublishedAdaptiveQActive = false;
        _lastPublishedEffectiveKalmanQ = _effectiveKalmanProcessNoiseQ;
        _pendingPersistedDefinition = null;

        if (!clearTeachState)
        {
            return;
        }

        _kalmanTeachActive = false;
        _kalmanTeachStartedAt = null;
        _kalmanTeachLastTickAt = null;
        _kalmanTeachAccumulatedMs = 0d;
        _kalmanTeachSampleCount = 0;
        _kalmanTeachMean = 0d;
        _kalmanTeachM2 = 0d;
        _kalmanTeachLearnedMeasurementNoiseR = 0d;
        _kalmanTeachDerivedProcessNoiseQ = 0d;
        _kalmanTeachState = "Idle";
    }

    private void FlushPendingPersistedDefinitionUpdate()
    {
        ExtendedSignalDefinition? pendingDefinition;
        lock (_sync)
        {
            pendingDefinition = _pendingPersistedDefinition;
            _pendingPersistedDefinition = null;
        }

        if (pendingDefinition is null)
        {
            return;
        }

        if (!EnhancedSignalRuntimeManager.TryUpdateDefinition(_folderName, pendingDefinition))
        {
            SetAlert("Kalman teach auto-apply could not persist the learned values");
        }
    }

    private Item EnsureKalmanBranch()
    {
        if (!_module.Has("Kalman"))
        {
            _module.AddItem("Kalman");
        }

        var kalman = _module["Kalman"];
        kalman.Params["Text"].Value = $"{_definition.Name} Kalman";
        if (!kalman.Has("Request"))
        {
            kalman.AddItem("Request");
            kalman["Request"].Params["Text"].Value = "Kalman Request";
            kalman["Request"].Value = string.Empty;
        }

        EnsureKalmanValueItem(kalman, "Estimate", "Kalman Estimate");
        EnsureKalmanValueItem(kalman, "MeasurementNoiseR", "Kalman Measurement Noise R");
        EnsureKalmanValueItem(kalman, "ProcessNoiseQ", "Kalman Process Noise Q");
        EnsureKalmanValueItem(kalman, "EffectiveProcessNoiseQ", "Kalman Effective Process Noise Q");
        EnsureKalmanValueItem(kalman, "AdaptiveQActive", "Kalman Adaptive Q Active");
        EnsureKalmanValueItem(kalman, "AdaptiveQHoldRemainingMs", "Kalman Adaptive Q Hold Remaining");
        EnsureKalmanValueItem(kalman, "DynamicTriggerActive", "Kalman Dynamic Trigger Active");
        EnsureKalmanValueItem(kalman, "InnovationAbs", "Kalman Innovation Abs");
        EnsureKalmanValueItem(kalman, "AdaptiveQIntensity", "Kalman Adaptive Q Intensity");
        EnsureKalmanValueItem(kalman, "DynamicSlopeRatio", "Kalman Dynamic Relative Change");
        EnsureKalmanValueItem(kalman, "DynamicRawAngleDeg", "Kalman Dynamic Raw Angle Deg");
        EnsureKalmanValueItem(kalman, "DynamicAngleDeg", "Kalman Dynamic Angle Deg");
        EnsureKalmanValueItem(kalman, "DynamicReferenceValue", "Kalman Dynamic Reference Value");
        EnsureKalmanValueItem(kalman, "DynamicEffectiveReferenceValue", "Kalman Dynamic Effective Reference Value");
        EnsureKalmanValueItem(kalman, "DynamicNoiseReferenceValue", "Kalman Dynamic Noise Reference Value");
        EnsureKalmanValueItem(kalman, "DynamicMaxSlope", "Kalman Dynamic Max Slope");
        EnsureKalmanValueItem(kalman, "DynamicMaxAngleDeg", "Kalman Dynamic Max Angle Deg");
        EnsureKalmanValueItem(kalman, "DynamicTrendConfidence", "Kalman Dynamic Trend Confidence");
        EnsureKalmanValueItem(kalman, "DynamicNormalizationMode", "Kalman Dynamic Normalization Mode");
        EnsureKalmanValueItem(kalman, "DynamicResidualWeight", "Kalman Dynamic Residual Weight");

        return kalman;
    }

    private Item EnsureDynamicBranch()
    {
        if (!_module.Has("Dynamic"))
        {
            _module.AddItem("Dynamic");
        }

        var dynamic = _module["Dynamic"];
        dynamic.Params["Text"].Value = $"{_definition.Name} Dynamic";
        EnsureDynamicValueItem(dynamic, "Active", "Dynamic Active");
        EnsureDynamicValueItem(dynamic, "Slope", "Dynamic Slope");
        EnsureDynamicValueItem(dynamic, "Residual", "Dynamic Residual");
        EnsureDynamicValueItem(dynamic, "RawAngleDeg", "Dynamic Raw Angle Deg");
        EnsureDynamicValueItem(dynamic, "AngleDeg", "Dynamic Angle Deg");
        EnsureDynamicValueItem(dynamic, "RelativeChange", "Dynamic Relative Change");
        EnsureDynamicValueItem(dynamic, "ReferenceValue", "Dynamic Reference Value");
        EnsureDynamicValueItem(dynamic, "EffectiveReferenceValue", "Dynamic Effective Reference Value");
        EnsureDynamicValueItem(dynamic, "NoiseReferenceValue", "Dynamic Noise Reference Value");
        EnsureDynamicValueItem(dynamic, "NormalizationMode", "Dynamic Normalization Mode");
        EnsureDynamicValueItem(dynamic, "ResidualWeight", "Dynamic Residual Weight");
        EnsureDynamicValueItem(dynamic, "RemainingHoldMs", "Dynamic Remaining Hold Ms");
        return dynamic;
    }

    private Item EnsureAdjustmentBranch()
    {
        if (!_module.Has("Adjustment"))
        {
            _module.AddItem("Adjustment");
        }

        var adjustment = _module["Adjustment"];
        adjustment.Params["Text"].Value = $"{_definition.Name} Adjustment";
        if (!adjustment.Has("Request"))
        {
            adjustment.AddItem("Request");
            adjustment["Request"].Params["Text"].Value = "Adjustment Request";
            adjustment["Request"].Value = string.Empty;
        }

        EnsureAdjustmentValueItem(adjustment, "Enabled", "Adjustment Enabled");
        EnsureAdjustmentValueItem(adjustment, "MappingMode", "Adjustment Mapping Mode");
        EnsureAdjustmentValueItem(adjustment, "Offset", "Adjustment Offset");
        EnsureAdjustmentValueItem(adjustment, "Gain", "Adjustment Gain");
        EnsureAdjustmentValueItem(adjustment, "SplineInterpolationMode", "Adjustment Spline Interpolation Mode");
        EnsureAdjustmentValueItem(adjustment, "SupportsInverseMapping", "Adjustment Supports Inverse Mapping");
        EnsureAdjustmentValueItem(adjustment, "Spline", "Adjustment Spline Points");
        return adjustment;
    }

    private Item EnsureStatisticsBranch()
    {
        if (!_module.Has("Statistics"))
        {
            _module.AddItem("Statistics");
        }

        var statistics = _module["Statistics"];
        statistics.Params["Text"].Value = $"{_definition.Name} Statistics";
        EnsureStatisticsExtremaItem(statistics, "Min", "Statistics Min", "Statistics Min Timestamp");
        EnsureStatisticsExtremaItem(statistics, "Max", "Statistics Max", "Statistics Max Timestamp");
        EnsureStatisticsValueItem(statistics, "Average", "Statistics Average");
        EnsureStatisticsValueItem(statistics, "StdDev", "Statistics StdDev");
        EnsureStatisticsValueItem(statistics, "Integral", "Statistics Integral");
        EnsureStatisticsResetItem(statistics);
        return statistics;
    }

    private void PublishStatisticsSnapshot(StatisticsSnapshot statistics)
    {
        var statisticsBranch = EnsureStatisticsBranch();
        statisticsBranch.Params["Enabled"].Value = true;
        statisticsBranch.Params["RetentionWindowMs"].Value = statistics.RetentionWindowMs;
        statisticsBranch.Params["StdDevWindowMs"].Value = statistics.StdDevWindowMs;
        statisticsBranch.Params["IntegralDivisorMs"].Value = statistics.IntegralDivisorMs;
        statisticsBranch.Params["PublishMin"].Value = statistics.PublishMin;
        statisticsBranch.Params["PublishMax"].Value = statistics.PublishMax;
        statisticsBranch.Params["PublishAverage"].Value = statistics.PublishAverage;
        statisticsBranch.Params["PublishStdDev"].Value = statistics.PublishStdDev;
        statisticsBranch.Params["PublishIntegral"].Value = statistics.PublishIntegral;

        statisticsBranch["Min"].Value = (statistics.PublishMin && statistics.MinValue.HasValue) ? statistics.MinValue.Value : null!;
        statisticsBranch["Min"]["TimeStamp"].Value = (statistics.PublishMin && statistics.MinTimestampUnixMs.HasValue) ? statistics.MinTimestampUnixMs.Value : null!;
        statisticsBranch["Max"].Value = (statistics.PublishMax && statistics.MaxValue.HasValue) ? statistics.MaxValue.Value : null!;
        statisticsBranch["Max"]["TimeStamp"].Value = (statistics.PublishMax && statistics.MaxTimestampUnixMs.HasValue) ? statistics.MaxTimestampUnixMs.Value : null!;
        statisticsBranch["Average"].Value = (statistics.PublishAverage && statistics.AverageValue.HasValue) ? statistics.AverageValue.Value : null!;
        statisticsBranch["StdDev"].Value = (statistics.PublishStdDev && statistics.StdDevValue.HasValue) ? statistics.StdDevValue.Value : null!;
        statisticsBranch["Integral"].Value = (statistics.PublishIntegral && statistics.IntegralValue.HasValue) ? statistics.IntegralValue.Value : null!;
        statisticsBranch["Reset"].Value = false;
    }

    private static void EnsureDynamicValueItem(Item dynamic, string itemName, string text)
    {
        if (dynamic.Has(itemName))
        {
            return;
        }

        dynamic.AddItem(itemName);
        dynamic[itemName].Params["Text"].Value = text;
    }

    private static void EnsureAdjustmentValueItem(Item adjustment, string itemName, string text)
    {
        if (adjustment.Has(itemName))
        {
            return;
        }

        adjustment.AddItem(itemName);
        adjustment[itemName].Params["Text"].Value = text;
    }

    private static void EnsureStatisticsExtremaItem(Item statistics, string itemName, string text, string timeStampText)
    {
        if (!statistics.Has(itemName))
        {
            statistics.AddItem(itemName);
            statistics[itemName].Params["Text"].Value = text;
        }

        if (statistics[itemName].Has("TimeStamp"))
        {
            return;
        }

        statistics[itemName].AddItem("TimeStamp");
        statistics[itemName]["TimeStamp"].Params["Text"].Value = timeStampText;
    }

    private static void EnsureStatisticsValueItem(Item statistics, string itemName, string text)
    {
        if (statistics.Has(itemName))
        {
            return;
        }

        statistics.AddItem(itemName);
        statistics[itemName].Params["Text"].Value = text;
    }

    private static void EnsureStatisticsResetItem(Item statistics)
    {
        if (statistics.Has("Reset"))
        {
            return;
        }

        statistics.AddItem("Reset");
        statistics["Reset"].Params["Text"].Value = "Statistics Reset";
        statistics["Reset"].Value = false;
    }

    private bool ShouldPublishDiagnostics(DateTimeOffset now, bool isDynamic)
    {
        var shouldPublish = _lastDiagnosticsPublishedAt is null
            || (now - _lastDiagnosticsPublishedAt.Value).TotalMilliseconds >= 100
            || isDynamic != _lastPublishedDynamicState
            || _adaptiveKalmanQActive != _lastPublishedAdaptiveQActive
            || Math.Abs(_effectiveKalmanProcessNoiseQ - _lastPublishedEffectiveKalmanQ) >= 0.01d;

        if (!shouldPublish)
        {
            return false;
        }

        _lastDiagnosticsPublishedAt = now;
        _lastPublishedDynamicState = isDynamic;
        _lastPublishedAdaptiveQActive = _adaptiveKalmanQActive;
        _lastPublishedEffectiveKalmanQ = _effectiveKalmanProcessNoiseQ;
        return true;
    }

    private static void EnsureKalmanValueItem(Item kalman, string itemName, string text)
    {
        if (kalman.Has(itemName))
        {
            return;
        }

        kalman.AddItem(itemName);
        kalman[itemName].Params["Text"].Value = text;
    }

    private bool TryParseAdjustmentRequest(Item requestItem, out string action, out double targetValue, out string error)
    {
        action = string.Empty;
        targetValue = 0d;
        error = string.Empty;

        if (requestItem.Params.Has("Action"))
        {
            action = requestItem.Params["Action"].Value?.ToString() ?? string.Empty;
            var requestValue = requestItem.Params.Has("Value") ? requestItem.Params["Value"].Value : requestItem.Value;
            var numericValue = ToNullableDouble(requestValue);
            if (string.IsNullOrWhiteSpace(action))
            {
                error = "Adjustment request action is empty";
                return false;
            }

            if (numericValue is null)
            {
                error = "Adjustment request value must be numeric";
                return false;
            }

            targetValue = numericValue.Value;
            return true;
        }

        var requestText = requestItem.Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestText))
        {
            error = "Adjustment request is empty";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(requestText);
            if (!document.RootElement.TryGetProperty("Action", out JsonElement actionProperty))
            {
                error = "Adjustment request requires an Action property";
                return false;
            }

            action = actionProperty.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(action))
            {
                error = "Adjustment request action is empty";
                return false;
            }

            if (!document.RootElement.TryGetProperty("Value", out JsonElement valueProperty)
                || !TryGetJsonDouble(valueProperty, out targetValue))
            {
                error = "Adjustment request Value must be numeric";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Adjustment request must be valid JSON with Action and Value";
            return false;
        }
    }

    private bool TryApplyAdjustmentAction(string action, double targetValue, out string error)
    {
        error = string.Empty;
        if (!TryGetAdjustmentActionBaseValue(out var baseValue, out error))
        {
            return false;
        }

        lock (_sync)
        {
            switch (NormalizeRequestCommand(action))
            {
                case "adjustspan":
                    if (Math.Abs(baseValue) < double.Epsilon)
                    {
                        error = "AdjustSpan requires a non-zero current input basis";
                        return false;
                    }

                    _definition.Adjustment.Gain = (targetValue - _definition.Adjustment.Offset) / baseValue;
                    break;
                case "adjustoffset":
                    _definition.Adjustment.Offset = targetValue - (baseValue * _definition.Adjustment.Gain);
                    break;
                default:
                    error = $"Unknown adjustment request '{action}'";
                    return false;
            }

            _pendingPersistedDefinition = _definition.Clone();
            ResetFilterState();
        }

        return true;
    }

    private bool TryGetAdjustmentActionBaseValue(out double baseValue, out string error)
    {
        error = string.Empty;
        baseValue = 0d;

        object? rawValue;
        lock (_sync)
        {
            rawValue = _latestSourceValue ?? ResolveSourceValue() ?? _module.Raw.Value;
        }

        var numericRaw = ToNullableDouble(rawValue);
        if (numericRaw is null)
        {
            error = "Adjustment action requires a numeric current raw value";
            return false;
        }

        baseValue = ApplyAdjustmentMapping(numericRaw.Value);
        return true;
    }

    private void RefreshAdjustmentState()
    {
        ApplyConfiguration();
        RefreshReadState(captureSample: true);
        FlushPendingPersistedDefinitionUpdate();
    }

    private static double NormalizeDynamicAngleThreshold(double value)
    {
        return Math.Clamp(value, 0d, 89d);
    }

    private static double RelativeChangeToAngleDegrees(double relativeChange)
    {
        return Math.Atan(Math.Max(0d, relativeChange)) * 180d / Math.PI;
    }

    private DynamicNormalizationResult CalculateKalmanDynamicNormalization(DynamicWindowAnalysis analysis, IReadOnlyList<SignalSample> samples, double trendSpan)
    {
        var referenceValue = Math.Max(samples.Average(sample => Math.Abs(sample.Value)), 0d);
        var referenceFloor = Math.Max(referenceValue, Math.Max(0.000001d, _definition.KalmanDynamicReferenceFloor));
        var noiseReferenceValue = Math.Max(analysis.RootMeanSquareResidual, 0.000001d);
        const KalmanDynamicNormalizationMode mode = KalmanDynamicNormalizationMode.HybridReferenceFloor;
        var residualWeight = Math.Clamp(_definition.KalmanDynamicResidualWeight, 0d, 1d);
        var effectiveReferenceValue = referenceFloor;

        return new DynamicNormalizationResult(
            mode,
            referenceValue,
            Math.Max(effectiveReferenceValue, 0.000001d),
            noiseReferenceValue,
            residualWeight,
            trendSpan);
    }

    private static DynamicWindowAnalysis AnalyzeDynamicWindow(IReadOnlyList<SignalSample> samples)
    {
        var baseTimestamp = samples[0].Timestamp;
        var meanX = 0d;
        var meanY = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            meanX += (samples[index].Timestamp - baseTimestamp).TotalMilliseconds;
            meanY += samples[index].Value;
        }

        meanX /= samples.Count;
        meanY /= samples.Count;

        var covariance = 0d;
        var varianceX = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            var x = (samples[index].Timestamp - baseTimestamp).TotalMilliseconds;
            var deltaX = x - meanX;
            var deltaY = samples[index].Value - meanY;
            covariance += deltaX * deltaY;
            varianceX += deltaX * deltaX;
        }

        var slopePerMillisecond = varianceX <= double.Epsilon ? 0d : covariance / varianceX;
        var intercept = meanY - (slopePerMillisecond * meanX);

        var squaredResiduals = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            var x = (samples[index].Timestamp - baseTimestamp).TotalMilliseconds;
            var expected = intercept + (slopePerMillisecond * x);
            var residual = samples[index].Value - expected;
            squaredResiduals += residual * residual;
        }

        return new DynamicWindowAnalysis(
            slopePerMillisecond * 1000d,
            Math.Sqrt(squaredResiduals / samples.Count));
    }

    private readonly record struct DynamicNormalizationResult(
        KalmanDynamicNormalizationMode Mode,
        double ReferenceValue,
        double EffectiveReferenceValue,
        double NoiseReferenceValue,
        double ResidualWeight,
        double TrendSpan);

    private bool IsDynamicDetectionEnabled()
    {
        return _definition.DynamicFilter.Enabled || IsKalmanDynamicDetectionEnabled();
    }

    private bool IsKalmanDynamicDetectionEnabled()
    {
        return _definition.KalmanEnabled && _definition.KalmanDynamicQEnabled;
    }

    private bool IsKalmanDynamicActive(DateTimeOffset now)
    {
        return (_kalmanDynamicUntil is not null && now <= _kalmanDynamicUntil.Value)
            || _lastDynamicAngleDegrees >= _lastDynamicThreshold;
    }

    private bool ShouldPublishDynamicDiagnostics()
    {
        return IsDynamicDetectionEnabled();
    }

    private void LogSourceAvailabilityTransition(bool hasSource)
    {
        if (_sourceAvailable == hasSource)
        {
            return;
        }

        _sourceAvailable = hasSource;
        Core.LogDebug(
            $"[EnhancedSignal:{_definition.Name}] source {(hasSource ? "available" : "missing")} candidates=[{string.Join(", ", _sourceReadCandidates)}]");
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        var leftNumber = ToNullableDouble(left);
        var rightNumber = ToNullableDouble(right);
        if (leftNumber is not null && rightNumber is not null)
        {
            return Math.Abs(leftNumber.Value - rightNumber.Value) < 0.0000001d;
        }

        return Equals(left, right);
    }

    private static string NormalizeRequestCommand(object? requestValue)
    {
        return requestValue?.ToString()?
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            ?? string.Empty;
    }

    private static bool TryGetJsonDouble(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonValueKind.String:
                return double.TryParse(element.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
            default:
                value = 0d;
                return false;
        }
    }

    private static bool? ToNullableBoolean(object? value)
    {
        return value switch
        {
            null => null,
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt) => parsedInt != 0,
            sbyte number => number != 0,
            byte number => number != 0,
            short number => number != 0,
            ushort number => number != 0,
            int number => number != 0,
            uint number => number != 0,
            long number => number != 0,
            ulong number => number != 0,
            _ => null
        };
    }

    private static bool TryParseAdjustmentMappingMode(string rawValue, out ExtendedSignalAdjustmentMode mappingMode)
    {
        if (Enum.TryParse<ExtendedSignalAdjustmentMode>(rawValue, true, out mappingMode))
        {
            if (mappingMode == ExtendedSignalAdjustmentMode.Linear)
            {
                mappingMode = ExtendedSignalAdjustmentMode.None;
            }

            return true;
        }

        mappingMode = ExtendedSignalAdjustmentMode.None;
        return false;
    }

    private static bool TryParseSplineInterpolationMode(string rawValue, out ExtendedSignalSplineInterpolationMode interpolationMode)
    {
        if (Enum.TryParse<ExtendedSignalSplineInterpolationMode>(rawValue, true, out interpolationMode))
        {
            return true;
        }

        interpolationMode = ExtendedSignalSplineInterpolationMode.Linear;
        return false;
    }

    private static bool TryParseSplinePoints(object? rawValue, out IReadOnlyList<ExtendedSignalSplinePoint> points, out string errorMessage)
    {
        points = Array.Empty<ExtendedSignalSplinePoint>();
        errorMessage = string.Empty;

        if (rawValue is null)
        {
            return true;
        }

        try
        {
            List<ExtendedSignalSplinePoint>? parsed = rawValue switch
            {
                string text when string.IsNullOrWhiteSpace(text) => [],
                string text => JsonSerializer.Deserialize<List<ExtendedSignalSplinePoint>>(text),
                JsonElement { ValueKind: JsonValueKind.Array } element => element.Deserialize<List<ExtendedSignalSplinePoint>>(),
                _ => JsonSerializer.Deserialize<List<ExtendedSignalSplinePoint>>(JsonSerializer.Serialize(rawValue))
            };

            points = (parsed ?? [])
                .OrderBy(static point => point.Input)
                .ToArray();

            for (var index = 1; index < points.Count; index++)
            {
                if (Math.Abs(points[index].Input - points[index - 1].Input) < double.Epsilon)
                {
                    errorMessage = $"Spline input '{points[index].Input.ToString(CultureInfo.InvariantCulture)}' is duplicated. Each input value must be unique.";
                    points = Array.Empty<ExtendedSignalSplinePoint>();
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Adjustment Spline must be valid JSON. {ex.Message}";
            points = Array.Empty<ExtendedSignalSplinePoint>();
            return false;
        }
    }

    private static double? ToNullableDouble(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) => parsed,
            IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static double ApplySpline(double value, IReadOnlyList<ExtendedSignalSplinePoint> points, ExtendedSignalSplineInterpolationMode interpolationMode)
    {
        if (points.Count == 0)
        {
            return value;
        }

        var ordered = points.OrderBy(static point => point.Input).ToArray();
        if (interpolationMode == ExtendedSignalSplineInterpolationMode.CatmullRom && ordered.Length >= 3)
        {
            return ApplyCatmullRomSpline(value, ordered);
        }

        return ApplyLinearSpline(value, ordered);
    }

    private static bool TryApplyInverseSpline(
        double value,
        IReadOnlyList<ExtendedSignalSplinePoint> points,
        ExtendedSignalSplineInterpolationMode interpolationMode,
        out double result,
        out string error)
    {
        error = string.Empty;
        result = value;

        if (points.Count == 0)
        {
            return true;
        }

        var ordered = points.OrderBy(static point => point.Input).ToArray();
        if (ordered.Length == 1)
        {
            if (AreClose(value, ordered[0].Output))
            {
                result = ordered[0].Input;
                return true;
            }

            error = "Value does not match the single spline point output";
            return false;
        }

        if (interpolationMode == ExtendedSignalSplineInterpolationMode.CatmullRom && ordered.Length >= 3)
        {
            return TryApplyInverseCatmullRomSpline(value, ordered, out result, out error);
        }

        return TryApplyInverseLinearSpline(value, ordered, out result, out error);
    }

    private static double ApplyLinearSpline(double value, IReadOnlyList<ExtendedSignalSplinePoint> ordered)
    {
        if (value <= ordered[0].Input)
        {
            return ordered[0].Output;
        }

        for (var index = 1; index < ordered.Count; index++)
        {
            var left = ordered[index - 1];
            var right = ordered[index];
            if (value <= right.Input)
            {
                var range = right.Input - left.Input;
                if (Math.Abs(range) < double.Epsilon)
                {
                    return right.Output;
                }

                var progress = (value - left.Input) / range;
                return left.Output + ((right.Output - left.Output) * progress);
            }
        }

        return ordered[^1].Output;
    }

    private static bool TryApplyInverseLinearSpline(double value, IReadOnlyList<ExtendedSignalSplinePoint> ordered, out double result, out string error)
    {
        error = string.Empty;
        result = value;

        for (var index = 1; index < ordered.Count; index++)
        {
            var left = ordered[index - 1];
            var right = ordered[index];
            var minOutput = Math.Min(left.Output, right.Output);
            var maxOutput = Math.Max(left.Output, right.Output);
            if (!ContainsInclusive(minOutput, maxOutput, value))
            {
                continue;
            }

            var outputRange = right.Output - left.Output;
            if (AreClose(outputRange, 0d))
            {
                result = left.Input;
                return true;
            }

            var progress = (value - left.Output) / outputRange;
            result = left.Input + ((right.Input - left.Input) * progress);
            return true;
        }

        error = "Value is outside the spline output range";
        return false;
    }

    private static double ApplyCatmullRomSpline(double value, IReadOnlyList<ExtendedSignalSplinePoint> ordered)
    {
        if (value <= ordered[0].Input)
        {
            return ordered[0].Output;
        }

        for (var index = 1; index < ordered.Count; index++)
        {
            var current = ordered[index];
            if (value > current.Input)
            {
                continue;
            }

            var p0 = ordered[Math.Max(0, index - 2)];
            var p1 = ordered[index - 1];
            var p2 = current;
            var p3 = ordered[Math.Min(ordered.Count - 1, index + 1)];
            var range = p2.Input - p1.Input;
            if (Math.Abs(range) < double.Epsilon)
            {
                return p2.Output;
            }

            var t = Math.Clamp((value - p1.Input) / range, 0d, 1d);
            return EvaluateCatmullRomSegment(t, p0, p1, p2, p3);
        }

        return ordered[^1].Output;
    }

    private static bool TryApplyInverseCatmullRomSpline(double value, IReadOnlyList<ExtendedSignalSplinePoint> ordered, out double result, out string error)
    {
        const int sampleCount = 64;
        const int bisectionSteps = 32;

        error = string.Empty;
        result = value;

        for (var segmentIndex = 0; segmentIndex < ordered.Count - 1; segmentIndex++)
        {
            var p0 = ordered[Math.Max(0, segmentIndex - 1)];
            var p1 = ordered[segmentIndex];
            var p2 = ordered[segmentIndex + 1];
            var p3 = ordered[Math.Min(ordered.Count - 1, segmentIndex + 2)];

            double previousT = 0d;
            double previousOutput = EvaluateCatmullRomSegment(previousT, p0, p1, p2, p3);
            if (AreClose(previousOutput, value))
            {
                result = p1.Input;
                return true;
            }

            for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                var currentT = (double)sampleIndex / sampleCount;
                var currentOutput = EvaluateCatmullRomSegment(currentT, p0, p1, p2, p3);

                if (AreClose(currentOutput, value))
                {
                    result = p1.Input + ((p2.Input - p1.Input) * currentT);
                    return true;
                }

                var segmentMin = Math.Min(previousOutput, currentOutput);
                var segmentMax = Math.Max(previousOutput, currentOutput);
                if (ContainsInclusive(segmentMin, segmentMax, value))
                {
                    var lowerT = previousT;
                    var upperT = currentT;
                    var lowerOutput = previousOutput;

                    for (var step = 0; step < bisectionSteps; step++)
                    {
                        var midT = (lowerT + upperT) / 2d;
                        var midOutput = EvaluateCatmullRomSegment(midT, p0, p1, p2, p3);
                        if (AreClose(midOutput, value))
                        {
                            lowerT = midT;
                            upperT = midT;
                            break;
                        }

                        if (ContainsInclusive(Math.Min(lowerOutput, midOutput), Math.Max(lowerOutput, midOutput), value))
                        {
                            upperT = midT;
                        }
                        else
                        {
                            lowerT = midT;
                            lowerOutput = midOutput;
                        }
                    }

                    var resolvedT = (lowerT + upperT) / 2d;
                    result = p1.Input + ((p2.Input - p1.Input) * resolvedT);
                    return true;
                }

                previousT = currentT;
                previousOutput = currentOutput;
            }
        }

        error = "Value is outside the spline output range";
        return false;
    }

    private static bool ContainsInclusive(double min, double max, double value)
    {
        return value >= (min - 1e-9) && value <= (max + 1e-9);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }

    private static double EvaluateCatmullRomSegment(double t, ExtendedSignalSplinePoint p0, ExtendedSignalSplinePoint p1, ExtendedSignalSplinePoint p2, ExtendedSignalSplinePoint p3)
    {
        var range = p2.Input - p1.Input;
        if (Math.Abs(range) < double.Epsilon)
        {
            return p2.Output;
        }

        var t2 = t * t;
        var t3 = t2 * t;
        var m1 = ComputeCatmullRomTangent(p0, p1, p2);
        var m2 = ComputeCatmullRomTangent(p1, p2, p3);
        var h00 = (2d * t3) - (3d * t2) + 1d;
        var h10 = t3 - (2d * t2) + t;
        var h01 = (-2d * t3) + (3d * t2);
        var h11 = t3 - t2;

        return (h00 * p1.Output)
            + (h10 * range * m1)
            + (h01 * p2.Output)
            + (h11 * range * m2);
    }

    private static double ComputeCatmullRomTangent(ExtendedSignalSplinePoint previous, ExtendedSignalSplinePoint current, ExtendedSignalSplinePoint next)
    {
        var leftRange = current.Input - previous.Input;
        var rightRange = next.Input - current.Input;

        if (Math.Abs(leftRange) < double.Epsilon && Math.Abs(rightRange) < double.Epsilon)
        {
            return 0d;
        }

        if (Math.Abs(leftRange) < double.Epsilon)
        {
            return (next.Output - current.Output) / rightRange;
        }

        if (Math.Abs(rightRange) < double.Epsilon)
        {
            return (current.Output - previous.Output) / leftRange;
        }

        var leftSlope = (current.Output - previous.Output) / leftRange;
        var rightSlope = (next.Output - current.Output) / rightRange;
        return ((rightRange * leftSlope) + (leftRange * rightSlope)) / (leftRange + rightRange);
    }

    private static ExtendedSignalModule CreateModule(ExtendedSignalDefinition definition, string path)
    {
        var normalizedPath = path.Replace('/', '.').Replace('\\', '.').Trim('.');
        var lastSeparatorIndex = normalizedPath.LastIndexOf('.');
        var parentPath = lastSeparatorIndex >= 0 ? normalizedPath[..lastSeparatorIndex] : null;
        return new ExtendedSignalModule(definition.Name, parentPath);
    }

    private readonly record struct SignalSample(DateTimeOffset Timestamp, double Value);

    private readonly record struct StatisticsSnapshot(
        bool Enabled,
        bool PublishMin,
        bool PublishMax,
        bool PublishAverage,
        bool PublishStdDev,
        bool PublishIntegral,
        int RetentionWindowMs,
        int StdDevWindowMs,
        double IntegralDivisorMs,
        double? MinValue,
        long? MinTimestampUnixMs,
        double? MaxValue,
        long? MaxTimestampUnixMs,
        double? AverageValue,
        double? StdDevValue,
        double? IntegralValue)
    {
        public static StatisticsSnapshot Disabled => new(
            Enabled: false,
            PublishMin: false,
            PublishMax: false,
            PublishAverage: false,
            PublishStdDev: false,
            PublishIntegral: false,
            RetentionWindowMs: 0,
            StdDevWindowMs: 0,
            IntegralDivisorMs: 1d,
            MinValue: null,
            MinTimestampUnixMs: null,
            MaxValue: null,
            MaxTimestampUnixMs: null,
            AverageValue: null,
            StdDevValue: null,
            IntegralValue: null);
    }

    private readonly record struct DynamicWindowAnalysis(double SlopePerSecond, double RootMeanSquareResidual);
}
