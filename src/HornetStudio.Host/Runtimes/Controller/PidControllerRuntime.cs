using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Amium.Items;
using HornetStudio.Editor.Models;
using HornetStudio.Host.Registries;
using HornetStudio.Host.Runtimes.EnhancedSignal;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Host.Runtimes.Controller;

/// <summary>
/// Publishes and executes a runtime PID controller for a folder-local controller definition.
/// </summary>
public sealed class PidControllerRuntime : IDisposable
{
    private readonly object _sync = new();
    private readonly ControllerDefinition _definition;
    private readonly string _folderName;
    private readonly string _registryPath;
    private readonly string _runPath;
    private readonly string _setRuntimePath;
    private readonly string[] _sourceCandidates;
    private readonly string[] _outputCandidates;
    private readonly ItemModel _snapshot;
    private readonly ATimer _timer;
    private bool _disposed;
    private bool _isUpdating;
    private bool _run;
    private double _previousError;
    private double _secondPreviousError;
    private double _previousInternalOutput;
    private double _filteredDerivativeTimeSeconds;
    private double? _lastOutputPercent;
    private double? _lastScaledOutputValue;
    private DateTimeOffset? _lastOutputPublishedAt;
    private double? _currentSourceValue;
    private double? _currentSetpointValue;
    private object? _ownedSetpointRequest;
    private string _currentState = "Stopped";
    private bool _currentAlert;

    /// <summary>
    /// Initializes a new instance of the <see cref="PidControllerRuntime"/> class.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    /// <param name="definition">The controller definition.</param>
    public PidControllerRuntime(string folderName, ControllerDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);

        _folderName = folderName;
        _definition = definition.Clone().Normalize();
        _registryPath = BuildRegistryPath(folderName, _definition);
        _runPath = _registryPath + ".run";
        _setRuntimePath = _registryPath + ".set";
        _sourceCandidates = EnhancedSignalPathHelper.EnumerateResolutionCandidates(_definition.SourcePath, folderName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _outputCandidates = EnhancedSignalPathHelper.EnumerateResolutionCandidates(_definition.OutputPath, folderName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var legacySetpointCandidates = EnhancedSignalPathHelper.EnumerateResolutionCandidates(_definition.SetpointPath, folderName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ownedSetpointRequest = ResolveInitialSetpointRequest(legacySetpointCandidates, _definition.Pid);
        _snapshot = CreateSnapshot();

        _timer = new ATimer($"PidControllerRuntime-{folderName}-{_definition.Name}", Math.Max(1, _definition.Pid.ComputeIntervalMs));
        _timer.Tick += OnTimerTick;
        HostRegistries.Data.ItemChanged += OnRegistryItemChanged;
        HostRegistries.Data.UpsertSnapshot(_registryPath, _snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
        RefreshInputState();
        PublishSnapshot();
    }

    /// <summary>
    /// Gets the controller registry path.
    /// </summary>
    public string RegistryPath => _registryPath;

    /// <summary>
    /// Gets the normalized controller definition.
    /// </summary>
    public ControllerDefinition Definition => _definition;

    /// <summary>
    /// Gets a value indicating whether the controller is currently enabled for execution.
    /// </summary>
    public bool IsRunning => _run;

    /// <summary>
    /// Gets the last resolved source value.
    /// </summary>
    public double? CurrentSourceValue => _currentSourceValue;

    /// <summary>
    /// Gets the last resolved setpoint value.
    /// </summary>
    public double? CurrentSetpointValue => _currentSetpointValue;

    /// <summary>
    /// Gets the last calculated output value.
    /// </summary>
    public double? CurrentOutputValue => _lastScaledOutputValue;

    /// <summary>
    /// Gets the current runtime state text.
    /// </summary>
    public string CurrentStateValue => _currentState;

    /// <summary>
    /// Gets a value indicating whether the runtime is currently in a fault state.
    /// </summary>
    public bool CurrentAlertValue => _currentAlert;

    /// <summary>
    /// Builds the runtime registry path for a controller definition.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    /// <param name="definition">The controller definition.</param>
    /// <returns>The canonical runtime registry path.</returns>
    public static string BuildRegistryPath(string folderName, ControllerDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(definition);
        var normalizedFolder = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(folderName).Replace('/', '.');
        var normalizedName = EnhancedSignalPathHelper.NormalizeConfiguredTargetPath(definition.Name).Replace('/', '.');
        return $"studio.{normalizedFolder}.controller.{normalizedName}";
    }

    /// <summary>
    /// Determines whether the provided path belongs to this runtime or its configured I/O bindings.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is related to this runtime; otherwise <see langword="false"/>.</returns>
    public bool MatchesPath(string path)
    {
        if (EnhancedSignalPathHelper.PathsEqual(path, _registryPath)
            || EnhancedSignalPathHelper.IsDescendantPath(path, _registryPath)
            || EnhancedSignalPathHelper.IsDescendantPath(_registryPath, path))
        {
            return true;
        }

        return _sourceCandidates.Concat(_outputCandidates)
            .Any(candidate => EnhancedSignalPathHelper.PathsEqual(candidate, path)
                || EnhancedSignalPathHelper.IsDescendantPath(candidate, path)
                || EnhancedSignalPathHelper.IsDescendantPath(path, candidate));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HostRegistries.Data.ItemChanged -= OnRegistryItemChanged;
        _timer.Tick -= OnTimerTick;
        _timer.Dispose();
        HostRegistries.Data.Remove(_registryPath);
    }

    private void OnRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (_disposed || _isUpdating)
        {
            return;
        }

        if (IsRegistryItemChange(e, _runPath))
        {
            if (TryReadRunRequest(e.ItemModel, preferLegacyWrite: IsWritePropertyChange(e), out var requestedRun))
            {
                ApplyRunState(requestedRun);
                MirrorRunItem(e.ItemModel, requestedRun);
                PublishSnapshot();
            }

            return;
        }

        if (IsRegistryItemChange(e, _setRuntimePath))
        {
            _ownedSetpointRequest = ReadSetpointRequest(e.ItemModel, preferLegacyWrite: IsWritePropertyChange(e));
            PublishSnapshot();
            return;
        }

        if (IsSourceCandidateChange(e))
        {
            RefreshInputState();
            PublishSnapshot();
        }
    }

    private void OnTimerTick()
    {
        if (_disposed)
        {
            return;
        }

        EvaluateController();
    }

    private void EvaluateController()
    {
        SynchronizeRunState();
        RefreshInputState();

        if (!_run)
        {
            _currentState = "Stopped";
            _currentAlert = false;
            PublishSnapshot();
            return;
        }

        if (!_currentSourceValue.HasValue)
        {
            _currentState = "Waiting for source: Source value must be numeric and readable.";
            _currentAlert = true;
            PublishSnapshot();
            return;
        }

        if (!_currentSetpointValue.HasValue)
        {
            _currentState = "Waiting for setpoint: Setpoint value must be numeric.";
            _currentAlert = true;
            PublishSnapshot();
            return;
        }

        var derived = ComputeDerivedParameters(_definition.Pid, out var derivedAlert);
        if (derived is null)
        {
            _currentState = $"Invalid parameters: {derivedAlert}";
            _currentAlert = true;
            PublishSnapshot();
            return;
        }

        var sourceValue = _currentSourceValue.Value;
        var setpointValue = _currentSetpointValue.Value;

        if (!IsValidNumber(sourceValue))
        {
            _currentState = "Waiting for source: Source value must be finite.";
            _currentAlert = true;
            PublishSnapshot(derived);
            return;
        }

        if (!IsValidNumber(setpointValue))
        {
            _currentState = "Waiting for setpoint: Setpoint value must be finite.";
            _currentAlert = true;
            PublishSnapshot(derived);
            return;
        }

        var normalizedSource = NormalizeProcessValue(sourceValue, _definition.Pid);
        var normalizedSetpoint = NormalizeProcessValue(setpointValue, _definition.Pid);
        var error = normalizedSetpoint - normalizedSource;
        var computeIntervalSeconds = Math.Max(0.001, _definition.Pid.ComputeIntervalMs / 1000.0);

        lock (_sync)
        {
            var tauSeconds = Math.Max(0.0, _definition.Pid.DFilterTauMs) / 1000.0;
            var derivativeTimeSeconds = derived.TvSeconds;
            if (tauSeconds > 0.0)
            {
                var alpha = tauSeconds / (tauSeconds + computeIntervalSeconds);
                _filteredDerivativeTimeSeconds = alpha * _filteredDerivativeTimeSeconds + (1.0 - alpha) * derivativeTimeSeconds;
                derivativeTimeSeconds = _filteredDerivativeTimeSeconds;
            }

            var b0 = derived.Kr * (1.0 + (computeIntervalSeconds / (2.0 * derived.TnSeconds)) + (derivativeTimeSeconds / computeIntervalSeconds));
            var b1 = -derived.Kr * (1.0 - (computeIntervalSeconds / (2.0 * derived.TnSeconds)) + (2.0 * (derivativeTimeSeconds / computeIntervalSeconds)));
            var b2 = derived.Kr * (derivativeTimeSeconds / computeIntervalSeconds);
            var rawInternalOutput = _previousInternalOutput + (error * b0) + (_previousError * b1) + (_secondPreviousError * b2);
            var internalOutput = Clamp(rawInternalOutput, minimum: 0.0, maximum: 100.0);
            _previousInternalOutput = rawInternalOutput + (internalOutput - rawInternalOutput);
            _secondPreviousError = _previousError;
            _previousError = error;

            var scaledOutputValue = ScaleOutput(internalOutput, _definition.Pid);
            _lastOutputPercent = internalOutput;
            _lastScaledOutputValue = scaledOutputValue;

            if (!IsValidNumber(scaledOutputValue))
            {
                _currentState = "Invalid output: Calculated output must be finite.";
                _currentAlert = true;
                PublishSnapshot(derived);
                return;
            }

            var shouldPublishOutput = !_lastOutputPublishedAt.HasValue
                || DateTimeOffset.UtcNow - _lastOutputPublishedAt.Value >= TimeSpan.FromMilliseconds(Math.Max(1, _definition.Pid.OutputIntervalMs));

            if (shouldPublishOutput)
            {
                if (!TryWriteValue(_outputCandidates, scaledOutputValue, timestamp: null))
                {
                    _currentState = "Output unavailable: Output target not found or not writable.";
                    _currentAlert = true;
                    PublishSnapshot(derived);
                    return;
                }

                _lastOutputPublishedAt = DateTimeOffset.UtcNow;
            }

            _currentState = "Running";
            _currentAlert = false;
            PublishSnapshot(derived);
        }
    }

    private void PublishSnapshot(DerivedPidParameters? derived = null)
    {
        var setpointDisplayValue = TryConvertToDouble(_ownedSetpointRequest, out var requestedSetpointValue)
            ? requestedSetpointValue
            : _currentSetpointValue ?? _definition.Pid.SetMin;
        EnsureSnapshotExists();

        _isUpdating = true;
        try
        {
            PublishChannelValue(_runPath, _run);
            PublishChannelValue($"{_registryPath}.read", _currentSourceValue);
            PublishChannelValue(_setRuntimePath, setpointDisplayValue);
            PublishChannelValue($"{_registryPath}.out", _lastOutputPercent);
            PublishChannelValue($"{_registryPath}.out.scaled", _lastScaledOutputValue);
            PublishChannelValue($"{_registryPath}.state", _currentState);
            PublishChannelValue($"{_registryPath}.alert", _currentAlert);
            PublishValue($"{_registryPath}.parameters.ks", _definition.Pid.Ks);
            PublishValue($"{_registryPath}.parameters.tu", _definition.Pid.Tu);
            PublishValue($"{_registryPath}.parameters.tg", _definition.Pid.Tg);
            PublishValue($"{_registryPath}.parameters.d_filter_tau_ms", _definition.Pid.DFilterTauMs);
            PublishValue($"{_registryPath}.parameters.set_min", _definition.Pid.SetMin);
            PublishValue($"{_registryPath}.parameters.set_max", _definition.Pid.SetMax);
            PublishValue($"{_registryPath}.parameters.out_min", _definition.Pid.OutMin);
            PublishValue($"{_registryPath}.parameters.out_max", _definition.Pid.OutMax);
            PublishValue($"{_registryPath}.parameters.compute_interval_ms", _definition.Pid.ComputeIntervalMs);
            PublishValue($"{_registryPath}.parameters.output_interval_ms", _definition.Pid.OutputIntervalMs);
            PublishValue($"{_registryPath}.parameters.kr", derived?.Kr ?? 0.0);
            PublishValue($"{_registryPath}.parameters.tn_s", derived?.TnSeconds ?? 0.0);
            PublishValue($"{_registryPath}.parameters.tv_s", derived?.TvSeconds ?? 0.0);
            PublishValue($"{_registryPath}.parameters.kp", derived?.Kr ?? 0.0);
            PublishValue($"{_registryPath}.parameters.ti_s", derived?.TnSeconds ?? 0.0);
            PublishValue($"{_registryPath}.parameters.td_s", derived?.TvSeconds ?? 0.0);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private ItemModel CreateSnapshot()
    {
        var snapshot = ItemExtension.CreateWithPath(_registryPath, false);
        snapshot.Properties["kind"].Value = "PidControllerRuntime";
        snapshot.Properties["title"].Value = _definition.Name;
        snapshot.Properties["text"].Value = _definition.Name;
        snapshot.Properties["controller_type"].Value = _definition.Type.ToString();
        snapshot["run"].Value = false;
        snapshot["run"].Properties["read"].Value = false;
        snapshot["read"].Value = 0.0;
        snapshot["read"].Properties["read"].Value = 0.0;
        snapshot["set"].Value = _definition.Pid.SetMin;
        snapshot["set"].Properties["read"].Value = _definition.Pid.SetMin;
        snapshot["out"].Value = 0.0;
        snapshot["out"].Properties["read"].Value = 0.0;
        snapshot["out"]["scaled"].Value = 0.0;
        snapshot["out"]["scaled"].Properties["read"].Value = 0.0;
        snapshot["state"].Value = _currentState;
        snapshot["state"].Properties["read"].Value = _currentState;
        snapshot["alert"].Value = false;
        snapshot["alert"].Properties["read"].Value = false;
        snapshot["parameters"]["ks"].Value = _definition.Pid.Ks;
        snapshot["parameters"]["tu"].Value = _definition.Pid.Tu;
        snapshot["parameters"]["tg"].Value = _definition.Pid.Tg;
        snapshot["parameters"]["d_filter_tau_ms"].Value = _definition.Pid.DFilterTauMs;
        snapshot["parameters"]["set_min"].Value = _definition.Pid.SetMin;
        snapshot["parameters"]["set_max"].Value = _definition.Pid.SetMax;
        snapshot["parameters"]["out_min"].Value = _definition.Pid.OutMin;
        snapshot["parameters"]["out_max"].Value = _definition.Pid.OutMax;
        snapshot["parameters"]["compute_interval_ms"].Value = _definition.Pid.ComputeIntervalMs;
        snapshot["parameters"]["output_interval_ms"].Value = _definition.Pid.OutputIntervalMs;
        snapshot["parameters"]["kr"].Value = 0.0;
        snapshot["parameters"]["tn_s"].Value = 0.0;
        snapshot["parameters"]["tv_s"].Value = 0.0;
        snapshot["parameters"]["kp"].Value = 0.0;
        snapshot["parameters"]["ti_s"].Value = 0.0;
        snapshot["parameters"]["td_s"].Value = 0.0;
        return snapshot;
    }

    private void EnsureSnapshotExists()
    {
        if (HostRegistries.Data.TryResolve(_registryPath, out var existing) && existing is not null)
        {
            return;
        }

        HostRegistries.Data.UpsertSnapshot(_registryPath, _snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
    }

    private static void PublishValue(string path, object? value)
    {
        HostRegistries.Data.UpdateValue(path, value);
    }

    private static void PublishChannelValue(string path, object? value)
    {
        HostRegistries.Data.UpdateValue(path, value);
        HostRegistries.Data.UpdateProperty(path, "read", value);
    }

    private void RefreshInputState()
    {
        var sourceAvailable = TryResolveNumericValue(_sourceCandidates, out var sourceValue);
        var setpointAvailable = TryReadOwnedSetpointValue(out var setpointValue);
        _currentSourceValue = sourceAvailable ? sourceValue : null;
        _currentSetpointValue = setpointAvailable ? setpointValue : null;
    }

    private bool IsSourceCandidateChange(DataChangedEventArgs e)
    {
        foreach (var candidate in _sourceCandidates)
        {
            if (EnhancedSignalPathHelper.PathsEqual(e.Key, candidate)
                || EnhancedSignalPathHelper.IsDescendantPath(candidate, e.Key))
            {
                return true;
            }
        }

        return false;
    }

    private void SynchronizeRunState()
    {
        if (!HostRegistries.Data.TryResolve(_runPath, out var runItem) || runItem is null)
        {
            return;
        }

        if (!TryReadRunRequest(runItem, preferLegacyWrite: false, out var requestedRun))
        {
            return;
        }

        ApplyRunState(requestedRun);
        MirrorRunItem(runItem, requestedRun);
    }

    private bool IsRegistryItemChange(DataChangedEventArgs e, string itemPath)
    {
        if (EnhancedSignalPathHelper.PathsEqual(e.Key, itemPath))
        {
            return true;
        }

        return HostRegistries.Data.TryResolve(itemPath, out var item)
            && item is not null
            && ReferenceEquals(e.ItemModel, item);
    }

    private static bool IsWritePropertyChange(DataChangedEventArgs e)
        => e.ChangeKind == DataChangeKind.PropertyUpdated
           && string.Equals(e.ParameterName, "write", StringComparison.OrdinalIgnoreCase);

    private void ApplyRunState(bool requestedRun)
    {
        lock (_sync)
        {
            if (_run == requestedRun)
            {
                return;
            }

            _run = requestedRun;
            if (_run)
            {
                _timer.Start();
                return;
            }

            _timer.Stop();
            ResetControllerState();
        }
    }

    private static void MirrorRunItem(ItemModel item, bool requestedRun)
    {
        item.Value = requestedRun;
        item.Properties["read"].Value = requestedRun;
    }

    private static bool TryReadRunRequest(ItemModel item, bool preferLegacyWrite, out bool requestedRun)
    {
        if (preferLegacyWrite
            && TryGetPropertyValue(item, "write", out var writeValue)
            && TryConvertToBoolean(writeValue, out requestedRun))
        {
            return true;
        }

        if (TryConvertToBoolean(item.Value, out requestedRun))
        {
            return true;
        }

        if (TryGetPropertyValue(item, "write", out var legacyWriteValue)
            && TryConvertToBoolean(legacyWriteValue, out requestedRun))
        {
            return true;
        }

        return TryConvertToBoolean(ExtractValue(item), out requestedRun);
    }

    private bool TryReadOwnedSetpointValue(out double setValue)
    {
        return TryConvertToDouble(_ownedSetpointRequest, out setValue);
    }

    private static DerivedPidParameters? ComputeDerivedParameters(PidControllerDefinition definition, out string alert)
    {
        alert = string.Empty;
        if (!IsValidNumber(definition.Ks) || definition.Ks <= 0.0)
        {
            alert = "Ks must be greater than zero.";
            return null;
        }

        if (!IsValidNumber(definition.Tu) || definition.Tu <= 0.0)
        {
            alert = "Tu must be greater than zero.";
            return null;
        }

        if (!IsValidNumber(definition.Tg) || definition.Tg <= 0.0)
        {
            alert = "Tg must be greater than zero.";
            return null;
        }

        if (!IsValidNumber(definition.SetMin) || !IsValidNumber(definition.SetMax) || definition.SetMax <= definition.SetMin)
        {
            alert = "SetMax must be greater than SetMin.";
            return null;
        }

        if (!IsValidNumber(definition.OutMin) || !IsValidNumber(definition.OutMax))
        {
            alert = "Output limits must be finite.";
            return null;
        }

        var kr = 0.95 * definition.Tg / (definition.Ks * definition.Tu);
        var tnSeconds = 2.4 * definition.Tu;
        var tvSeconds = 0.42 * definition.Tu;
        return new DerivedPidParameters(kr, tnSeconds, tvSeconds);
    }

    private void ResetControllerState()
    {
        _previousError = 0.0;
        _secondPreviousError = 0.0;
        _previousInternalOutput = 0.0;
        _filteredDerivativeTimeSeconds = 0.0;
    }

    private static double NormalizeProcessValue(double value, PidControllerDefinition definition)
    {
        var normalized = (value - definition.SetMin) * 100.0 / (definition.SetMax - definition.SetMin);
        return Clamp(normalized, minimum: 0.0, maximum: 100.0);
    }

    private static double ScaleOutput(double internalOutput, PidControllerDefinition definition)
    {
        var clampedOutput = Clamp(internalOutput, minimum: 0.0, maximum: 100.0);
        return definition.OutMin + (clampedOutput / 100.0) * (definition.OutMax - definition.OutMin);
    }

    private static bool IsValidNumber(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool TryWriteValue(string[] candidates, double value, ulong? timestamp)
    {
        foreach (var candidate in candidates)
        {
            if (!HostRegistries.Data.TryResolve(candidate, out var target) || target is null)
            {
                continue;
            }

            if (target.Properties.Has("write"))
            {
                if (HostRegistries.Data.UpdateProperty(candidate, "write", value, timestamp))
                {
                    return true;
                }
            }
            else if (HostRegistries.Data.UpdateValue(candidate, value, timestamp))
            {
                return true;
            }
        }

        return false;
    }

    private static object ResolveInitialSetpointRequest(string[] legacyCandidates, PidControllerDefinition definition)
    {
        if (TryResolveNumericValue(legacyCandidates, out var legacySetpointValue))
        {
            return legacySetpointValue;
        }

        return definition.SetMin;
    }

    private static object? ReadSetpointRequest(ItemModel item, bool preferLegacyWrite)
    {
        if (preferLegacyWrite && TryGetPropertyValue(item, "write", out var writeValue))
        {
            return writeValue;
        }

        if (item.Value is not null)
        {
            return item.Value;
        }

        var extractedValue = ExtractValue(item);
        if (extractedValue is not null)
        {
            return extractedValue;
        }

        if (TryGetPropertyValue(item, "write", out var legacyWriteValue))
        {
            return legacyWriteValue;
        }

        return null;
    }

    private static bool TryResolveNumericValue(string[] candidates, out double value)
    {
        foreach (var candidate in candidates)
        {
            if (!HostRegistries.Data.TryResolve(candidate, out var item) || item is null)
            {
                continue;
            }

            if (TryConvertToDouble(ExtractValue(item), out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static object? ExtractValue(ItemModel item)
    {
        if (item.Value is not null)
        {
            return item.Value;
        }

        if (TryExtractChildChannelValue(item, "read", out var readValue))
        {
            return readValue;
        }

        if (TryExtractChildChannelValue(item, "out", out var outValue))
        {
            return outValue;
        }

        if (TryGetPropertyValue(item, "read", out var readPropertyValue))
        {
            return readPropertyValue;
        }

        return TryGetPropertyValue(item, "value", out var valuePropertyValue)
            ? valuePropertyValue
            : null;
    }

    private static bool TryExtractChildChannelValue(ItemModel item, string childName, out object? value)
    {
        value = null;
        var matchingChildName = item.GetDictionary().Keys
            .FirstOrDefault(key => string.Equals(key, childName, StringComparison.OrdinalIgnoreCase));
        if (matchingChildName is null)
        {
            return false;
        }

        var child = item[matchingChildName];
        if (TryGetPropertyValue(child, "read", out value))
        {
            return true;
        }

        if (TryGetPropertyValue(child, "value", out value))
        {
            return true;
        }

        value = child.Value;
        return value is not null;
    }

    private static bool TryGetPropertyValue(ItemModel item, string propertyName, out object? value)
    {
        value = null;
        if (!item.Properties.Has(propertyName))
        {
            return false;
        }

        value = item.Properties[propertyName].Value;
        return value is not null;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                try
                {
                    result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
        }
    }

    private static bool TryConvertToBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool boolValue:
                result = boolValue;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                result = parsed;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericText):
                result = Math.Abs(numericText) > double.Epsilon;
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                result = Math.Abs(Convert.ToDouble(value, CultureInfo.InvariantCulture)) > double.Epsilon;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static double Clamp(double value, double minimum, double maximum)
        => Math.Max(minimum, Math.Min(maximum, value));

    private sealed record DerivedPidParameters(double Kr, double TnSeconds, double TvSeconds);
}
