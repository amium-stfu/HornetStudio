using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Amium.Items;
using HornetStudio.Editor.Helpers;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Widgets;
using HornetStudio.Host;
using HornetStudio.Host.Python.Client;
using HornetStudio.Logging;
using Serilog.Events;
using ItemModel = Amium.Items.Item;

namespace HornetStudio.Editor.Monitoring;

/// <summary>
/// Runs one monitor rule independently from any UI control and publishes its runtime state.
/// </summary>
public sealed class MonitorRuleRuntime : IDisposable
{
    private readonly string _folderName;
    private readonly string _runtimePath;
    private readonly Action _activeStateChanged;
    private readonly object _evaluationSyncRoot = new();
    private readonly Timer _evaluationTimer;
    private readonly FolderItemModel _resolutionItem;
    private DateTimeOffset? _conditionStartedUtc;
    private bool _isActive;
    private string _statusText = "Inactive";
    private MonitorPublishedRuntimeState? _lastPublishedRuntimeState;

    /// <summary>
    /// Initializes a new monitor runtime instance for one folder-scoped rule.
    /// </summary>
    /// <param name="folderName">The owning folder name.</param>
    /// <param name="definition">The monitor rule definition.</param>
    /// <param name="activeStateChanged">A callback invoked when the active state changes.</param>
    public MonitorRuleRuntime(string? folderName, MonitorDefinition definition, Action activeStateChanged)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(activeStateChanged);

        _folderName = folderName?.Trim() ?? string.Empty;
        Definition = definition.Clone();
        _activeStateChanged = activeStateChanged;
        _runtimePath = string.IsNullOrWhiteSpace(_folderName)
            ? string.Empty
            : MonitorRegistry.BuildRulePath(_folderName, Definition.Name);
        _resolutionItem = new FolderItemModel
        {
            Kind = ControlKind.Monitor,
            Name = "monitor_runtime"
        };
        if (!string.IsNullOrWhiteSpace(_folderName))
        {
            _resolutionItem.SetHierarchy(_folderName, parentItem: null);
        }
        _evaluationTimer = new Timer(
            callback: OnEvaluationTimerTick,
            state: null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromMilliseconds(Math.Max(100, Definition.RefreshRateMs)));
    }

    /// <summary>
    /// Raised when the published active or status state changes.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets the immutable runtime definition snapshot.
    /// </summary>
    public MonitorDefinition Definition { get; }

    /// <summary>
    /// Gets the technical rule name.
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// Gets a value indicating whether the rule is currently active.
    /// </summary>
    public bool IsActive => _isActive;

    /// <summary>
    /// Gets the current user-facing status text.
    /// </summary>
    public string StatusText => _statusText;

    /// <summary>
    /// Re-evaluates the rule immediately.
    /// </summary>
    public void Evaluate()
    {
        MonitorEvaluation evaluation;
        bool wasActive;
        bool activeChanged;
        bool statusChanged;

        lock (_evaluationSyncRoot)
        {
            using var diagnosticsScope = UiResponsivenessDiagnostics.TrackSteadyStateOperation(
                owner: null,
                category: "SteadyStateMonitor",
                name: $"MonitorRuleRuntime[{Definition.Name}].Evaluate",
                threshold: TimeSpan.FromMilliseconds(10),
                stateFactory: () => new Dictionary<string, object?>
                {
                    ["SourcePath"] = Definition.SourcePath,
                    ["RefreshRateMs"] = Definition.RefreshRateMs
                });

            evaluation = EvaluateState();
            wasActive = _isActive;
            activeChanged = _isActive != evaluation.IsActive;
            statusChanged = !string.Equals(_statusText, evaluation.StatusText, StringComparison.Ordinal);

            PublishRuntime(evaluation);
            ExecuteTransitionActions(wasActive, evaluation);

            _isActive = evaluation.IsActive;
            _statusText = evaluation.StatusText;
        }

        if (activeChanged)
        {
            _activeStateChanged();
        }

        if (activeChanged || statusChanged)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _evaluationTimer.Dispose();
        if (!string.IsNullOrWhiteSpace(_runtimePath))
        {
            HostRegistries.Data.Remove(_runtimePath);
        }
    }

    private void OnEvaluationTimerTick(object? state)
    {
        Evaluate();
    }

    private MonitorEvaluation EvaluateState()
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackSteadyStateOperation(
            owner: null,
            category: "SteadyStateMonitor",
            name: $"MonitorRuleRuntime[{Definition.Name}].EvaluateState",
            threshold: TimeSpan.FromMilliseconds(10),
            stateFactory: () => new Dictionary<string, object?>
            {
                ["SourcePath"] = Definition.SourcePath,
                ["Mode"] = Definition.Mode.ToString()
            });

        var activationReasons = new List<string>();
        var notes = new List<string>();
        var now = DateTimeOffset.UtcNow;
        TryResolveSourceItem(Definition.SourcePath, _folderName, out var sourceItem);

        if (Definition.TimeoutMs.HasValue && Definition.TimeoutMs.Value > 0)
        {
            if (sourceItem is null || !TryReadItemEpoch(sourceItem, out var epoch))
            {
                activationReasons.Add($"Timeout > {Definition.TimeoutMs.Value} ms (epoch unavailable)");
            }
            else
            {
                var ageMs = Math.Max(0, now.ToUnixTimeMilliseconds() - (long)epoch);
                if (ageMs > Definition.TimeoutMs.Value)
                {
                    activationReasons.Add($"Timeout > {Definition.TimeoutMs.Value} ms");
                }
            }
        }

        object? value = sourceItem?.Value;
        if (Definition.Mode == MonitorRuleMode.Default)
        {
            EvaluateNumericLimit(Definition.LowerLimit, value, static (current, limit) => current < limit, "Lower limit", activationReasons, notes);
            EvaluateNumericLimit(Definition.UpperLimit, value, static (current, limit) => current > limit, "Upper limit", activationReasons, notes);
        }
        else
        {
            EvaluateCustomFormula(value, activationReasons, notes);
        }

        var rawActive = activationReasons.Count > 0;
        if (rawActive)
        {
            _conditionStartedUtc ??= now;
        }
        else
        {
            _conditionStartedUtc = null;
        }

        var effectiveInhibitMs = Math.Max(0, Definition.InhibitMs);
        var isActive = rawActive && (_conditionStartedUtc is not null) && (now - _conditionStartedUtc.Value).TotalMilliseconds >= effectiveInhibitMs;
        var statusText = BuildStatusText(rawActive, isActive, activationReasons, notes, effectiveInhibitMs, now);

        return new MonitorEvaluation(isActive, statusText, value);
    }

    private void EvaluateCustomFormula(object? sourceValue, List<string> activationReasons, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(Definition.CustomFormula))
        {
            notes.Add("Formula missing");
            return;
        }

        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = sourceValue,
            ["source"] = sourceValue
        };

        foreach (var variable in Definition.CustomVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                continue;
            }

            if (TryResolveSourceItem(variable.SourcePath, _folderName, out var variableItem) && variableItem is not null)
            {
                variables[variable.Name] = variableItem.Value;
            }
            else
            {
                variables[variable.Name] = null;
            }
        }

        if (!CustomSignalFormulaEngine.TryEvaluateBooleanExpression(Definition.CustomFormula, variables, out var expressionActive, out var errorMessage))
        {
            notes.Add($"Formula invalid: {errorMessage}");
            return;
        }

        if (expressionActive)
        {
            activationReasons.Add("Formula matched");
        }
    }

    private void PublishRuntime(MonitorEvaluation evaluation)
    {
        if (string.IsNullOrWhiteSpace(_runtimePath))
        {
            return;
        }

        var nextPublishedState = new MonitorPublishedRuntimeState(evaluation.IsActive, evaluation.StatusText);
        if (_lastPublishedRuntimeState is { } lastPublishedState && lastPublishedState == nextPublishedState)
        {
            return;
        }

        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackSteadyStateOperation(
            owner: null,
            category: "SteadyStateMonitor",
            name: $"MonitorRuleRuntime[{Definition.Name}].PublishRuntime",
            threshold: TimeSpan.FromMilliseconds(10),
            stateFactory: () => new Dictionary<string, object?>
            {
                ["RuntimePath"] = _runtimePath,
                ["IsActive"] = evaluation.IsActive
            });

        var segments = TargetPathHelper.SplitPathSegments(_runtimePath);
        if (segments.Count == 0)
        {
            return;
        }

        var nameSegment = segments[^1];
        var parentPath = segments.Count > 1 ? string.Join('.', segments.Take(segments.Count - 1)) : string.Empty;
        var snapshot = string.IsNullOrWhiteSpace(parentPath)
            ? new ItemModel(nameSegment, evaluation.IsActive)
            : new ItemModel(nameSegment, evaluation.IsActive, parentPath);

        var title = string.IsNullOrWhiteSpace(Definition.EventText) ? Definition.Name : Definition.EventText;
        snapshot.Properties["path"].Value = _runtimePath;
        snapshot.Properties["kind"].Value = "MonitorState";
        snapshot.Properties["text"].Value = title;
        snapshot.Properties["title"].Value = title;
        snapshot["active"].Value = evaluation.IsActive;
        snapshot["active"].Properties["text"].Value = "Active";
        snapshot["message"].Value = evaluation.StatusText;
        snapshot["message"].Properties["text"].Value = "Message";
        snapshot["event_id"].Value = Definition.EventId;
        snapshot["event_id"].Properties["text"].Value = "EventId";
        snapshot["event_text"].Value = Definition.EventText;
        snapshot["event_text"].Properties["text"].Value = "EventText";
        snapshot["log_level"].Value = Definition.LogLevel.ToString();
        snapshot["log_level"].Properties["text"].Value = "LogLevel";
        snapshot["source_path"].Value = Definition.SourcePath;
        snapshot["source_path"].Properties["text"].Value = "SourcePath";
        snapshot["mode"].Value = Definition.Mode.ToString();
        snapshot["mode"].Properties["text"].Value = "Mode";
        snapshot["refresh_rate_ms"].Value = Definition.RefreshRateMs;
        snapshot["refresh_rate_ms"].Properties["text"].Value = "RefreshRateMs";
        snapshot["action_count"].Value = Definition.Actions.Count;
        snapshot["action_count"].Properties["text"].Value = "ActionCount";
        HostRegistries.Data.UpsertSnapshot(_runtimePath, snapshot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);
        _lastPublishedRuntimeState = nextPublishedState;
    }

    private void ExecuteTransitionActions(bool wasActive, MonitorEvaluation evaluation)
    {
        if (!wasActive && evaluation.IsActive)
        {
            ExecuteActions(MonitorActionTrigger.OnActivated, evaluation);
            return;
        }

        if (wasActive && !evaluation.IsActive)
        {
            ExecuteActions(MonitorActionTrigger.OnCleared, evaluation);
        }
    }

    private void ExecuteActions(MonitorActionTrigger trigger, MonitorEvaluation evaluation)
    {
        foreach (var action in Definition.Actions.Where(action => action.Trigger == trigger))
        {
            Core.LogInfo(
                $"[MonitorAction] trigger={trigger} rule={Definition.Name} action={action.ActionType} active={evaluation.IsActive} target_log={action.TargetLog} target_path={action.TargetPath} function={action.FunctionName} argument={action.Argument}");

            switch (action.ActionType)
            {
                case MonitorActionType.WriteLog:
                    WriteProcessLog(action, evaluation);
                    break;
                case MonitorActionType.SetValue:
                    ExecuteSetValue(action);
                    break;
                case MonitorActionType.InvokeFunction:
                    ExecuteInvokeFunction(action);
                    break;
            }
        }
    }

    private void ExecuteSetValue(MonitorActionDefinition action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetPath))
        {
            return;
        }

        if (!TryResolveActionTarget(action.TargetPath, out var targetItem) || targetItem is null)
        {
            Core.LogWarn($"[Monitor] SetValue target '{action.TargetPath}' for rule '{Definition.Name}' could not be resolved.");
            return;
        }

        if (!TryApplyActionWrite(targetItem, action.TargetPath, action.Argument, out var errorMessage))
        {
            Core.LogWarn($"[Monitor] SetValue target '{action.TargetPath}' for rule '{Definition.Name}' failed: {errorMessage}");
            return;
        }

        Core.LogInfo($"[MonitorAction] SetValue applied rule={Definition.Name} target={action.TargetPath} argument={action.Argument}");
    }

    private void ExecuteInvokeFunction(MonitorActionDefinition action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetPath) || string.IsNullOrWhiteSpace(action.FunctionName))
        {
            return;
        }

        var resolvedTargetPath = ApplicationExplorerRuntime.ResolveInteractionTargetPath(_resolutionItem, action.TargetPath);
        if (!PythonClientRuntimeRegistry.TryGetClient(resolvedTargetPath, out var client) || client is null)
        {
            Core.LogWarn($"[Monitor] InvokeFunction target '{action.TargetPath}' for rule '{Definition.Name}' is not active.");
            return;
        }

        try
        {
            var result = client.InvokeFunctionAsync(action.FunctionName, BuildPythonArgumentPayload(action.Argument))
                .GetAwaiter()
                .GetResult();

            if (!result.Success)
            {
                var errorMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? $"Function '{action.FunctionName}' failed."
                    : result.Message!;
                if (ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var failedRow))
                {
                    failedRow?.SetInvocationError(ApplicationErrorDetails.FromResultPayload(failedRow.Name, errorMessage, result.Payload));
                }

                Core.LogWarn($"[Monitor] InvokeFunction '{action.FunctionName}' in '{resolvedTargetPath}' for rule '{Definition.Name}' failed: {errorMessage}");
                return;
            }

            if (ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var successRow))
            {
                successRow?.ClearInvocationError();
            }
        }
        catch (Exception ex)
        {
            if (ApplicationEntryRegistry.TryGetByInteractionTargetPath(resolvedTargetPath, out var failedRow))
            {
                failedRow?.SetInvocationError(ex.Message);
            }

            Core.LogWarn($"[Monitor] InvokeFunction '{action.FunctionName}' in '{resolvedTargetPath}' for rule '{Definition.Name}' threw an exception: {ex.Message}", ex);
        }
    }

    private void WriteProcessLog(MonitorActionDefinition action, MonitorEvaluation evaluation)
    {
        if (string.IsNullOrWhiteSpace(action.TargetLog))
        {
            return;
        }

        if (!TryResolveProcessLog(action.TargetLog, _folderName, out var processLog) || processLog is null)
        {
            Core.LogWarn($"[Monitor] WriteLog target '{action.TargetLog}' for rule '{Definition.Name}' could not be resolved.");
            return;
        }

        var message = string.IsNullOrWhiteSpace(Definition.EventText)
            ? evaluation.StatusText
            : $"[{Definition.EventId}] {Definition.EventText}";
        processLog.WriteEntry(ToLogEventLevel(Definition.LogLevel), message);
    }

    private string BuildStatusText(bool rawActive, bool isActive, IReadOnlyList<string> activationReasons, IReadOnlyList<string> notes, int inhibitMs, DateTimeOffset now)
    {
        if (!rawActive)
        {
            return notes.Count == 0 ? "Inactive" : $"Inactive: {string.Join(" | ", notes)}";
        }

        if (!isActive)
        {
            var elapsedMs = _conditionStartedUtc.HasValue ? Math.Max(0, (int)(now - _conditionStartedUtc.Value).TotalMilliseconds) : 0;
            var remainingMs = Math.Max(0, inhibitMs - elapsedMs);
            var messages = activationReasons.Concat(notes).ToArray();
            return messages.Length == 0
                ? $"Inhibit active ({remainingMs} ms remaining)"
                : $"Inhibit active ({remainingMs} ms remaining): {string.Join(" | ", messages)}";
        }

        var activeMessages = activationReasons.Concat(notes).ToArray();
        return activeMessages.Length == 0 ? "Active" : $"Active: {string.Join(" | ", activeMessages)}";
    }

    private static void EvaluateNumericLimit(string rawConfigured, object? value, Func<double, double, bool> compare, string label, List<string> activationReasons, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(rawConfigured))
        {
            return;
        }

        if (!double.TryParse(rawConfigured, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var limit))
        {
            notes.Add($"{label} invalid");
            return;
        }

        if (!TryConvertToDouble(value, out var numericValue))
        {
            notes.Add($"{label} skipped (source not numeric)");
            return;
        }

        if (compare(numericValue, limit))
        {
            activationReasons.Add($"{label} {limit.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static bool TryConvertToDouble(object? value, out double numericValue)
    {
        switch (value)
        {
            case byte byteValue:
                numericValue = byteValue;
                return true;
            case sbyte signedByteValue:
                numericValue = signedByteValue;
                return true;
            case short shortValue:
                numericValue = shortValue;
                return true;
            case ushort unsignedShortValue:
                numericValue = unsignedShortValue;
                return true;
            case int intValue:
                numericValue = intValue;
                return true;
            case uint unsignedIntValue:
                numericValue = unsignedIntValue;
                return true;
            case long longValue:
                numericValue = longValue;
                return true;
            case ulong unsignedLongValue:
                numericValue = unsignedLongValue;
                return true;
            case float floatValue:
                numericValue = floatValue;
                return true;
            case double doubleValue:
                numericValue = doubleValue;
                return true;
            case decimal decimalValue:
                numericValue = (double)decimalValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed):
                numericValue = parsed;
                return true;
            default:
                numericValue = 0;
                return false;
        }
    }

    private static bool TryResolveSourceItem(string targetPath, string? folderName, out ItemModel? item)
    {
        foreach (var candidatePath in TargetPathHelper.EnumerateResolutionCandidates(targetPath, folderName))
        {
            if (HostRegistries.Data.TryResolve(candidatePath, out item) && item is not null)
            {
                return true;
            }
        }

        foreach (var candidatePath in TargetPathHelper.EnumerateItemBrokerRuntimeCandidates(targetPath))
        {
            if (HostRegistries.Data.TryResolve(candidatePath, out item) && item is not null)
            {
                return true;
            }
        }

        item = null;
        return false;
    }

    private bool TryResolveActionTarget(string? targetPath, out ItemModel? item)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            item = null;
            return false;
        }

        return TryResolveSourceItem(targetPath, _folderName, out item);
    }

    private static bool TryApplyActionWrite(ItemModel targetItem, string? targetPath, object? rawValue, out string error)
    {
        if (!IsDeclaredWritable(targetItem))
        {
            error = "Target is not writable.";
            return false;
        }

        var writeParameter = ResolveActionWriteParameter(targetItem);
        var readParameter = ResolveActionReadParameter(targetItem);
        var writeTargetItem = ResolveActionWriteTargetItem(targetItem);
        if (writeParameter is null)
        {
            error = "No write parameter was found for the action target.";
            return false;
        }

        try
        {
            var convertedValue = ConvertActionValue(rawValue, writeParameter.Value?.GetType() ?? readParameter?.Value?.GetType());
            if (!string.Equals(writeParameter.Name, "read", StringComparison.OrdinalIgnoreCase)
                && !HostRegistryPropertyPolicy.CanUserWriteProperty(writeParameter.Name))
            {
                error = $"Parameter '{writeParameter.Name}' is protected and cannot be written.";
                return false;
            }

            var resolvedTargetPath = writeTargetItem.Path ?? targetItem.Path ?? targetPath ?? string.Empty;
            var forceWriteNotification = string.Equals(writeParameter.Name, "write", StringComparison.OrdinalIgnoreCase);
            var updated = string.Equals(writeParameter.Name, "read", StringComparison.OrdinalIgnoreCase)
                ? HostRegistries.Data.UpdateValue(resolvedTargetPath, convertedValue)
                : HostRegistries.Data.TryUpdateUserProperty(resolvedTargetPath, writeParameter.Name, convertedValue, forceChangeNotification: forceWriteNotification);
            if (!updated)
            {
                if (string.Equals(writeParameter.Name, "read", StringComparison.OrdinalIgnoreCase))
                {
                    writeTargetItem.Value = convertedValue!;
                }
                else
                {
                    writeParameter.Value = convertedValue!;
                }

                PublishActionSnapshot(targetItem);
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ItemProperty? ResolveActionReadParameter(ItemModel targetItem)
    {
        if (targetItem.Properties.Has("read"))
        {
            return targetItem.Properties["read"];
        }

        var firstParameter = targetItem.Properties.GetDictionary().Keys
            .Where(HostRegistryPropertyPolicy.CanShowInUserPicker)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return firstParameter is null ? null : targetItem.Properties[firstParameter];
    }

    private static ItemProperty? ResolveActionWriteParameter(ItemModel targetItem)
    {
        if (targetItem.Properties.Has("write"))
        {
            return targetItem.Properties["write"];
        }

        if (TryResolveDeclaredWriteBinding(targetItem, out var declaredTarget))
        {
            return declaredTarget.Properties.Has("write")
                ? declaredTarget.Properties["write"]
                : ResolveValueParameter(declaredTarget);
        }

        return ResolveActionReadParameter(targetItem);
    }

    private static ItemModel ResolveActionWriteTargetItem(ItemModel targetItem)
        => TryResolveDeclaredWriteBinding(targetItem, out var declaredTarget) ? declaredTarget : targetItem;

    private static bool IsDeclaredWritable(ItemModel? item)
    {
        if (item is null)
        {
            return false;
        }

        if (item.Properties.Has("write"))
        {
            return true;
        }

        if (item.Properties.Has("writable"))
        {
            return ToBooleanLikeValue(item.Properties["writable"].Value);
        }

        return true;
    }

    private static ItemProperty? ResolveValueParameter(ItemModel item)
        => item.Properties.Has("read") ? item.Properties["read"] : null;

    private static bool TryResolveDeclaredWriteBinding(ItemModel sourceItem, out ItemModel writeTargetItem)
    {
        writeTargetItem = null!;
        if (sourceItem.Properties.Has("write"))
        {
            writeTargetItem = sourceItem;
            return true;
        }

        if (!sourceItem.Properties.Has("write_path"))
        {
            return false;
        }

        var writePath = sourceItem.Properties["write_path"].Value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(writePath))
        {
            return false;
        }

        if (!HostRegistries.Data.TryResolve(writePath, out ItemModel? resolvedItem) || resolvedItem is null)
        {
            return false;
        }

        writeTargetItem = resolvedItem!;
        return true;
    }

    private static object? ConvertActionValue(object? rawValue, Type? targetType)
    {
        if (targetType is null || rawValue is null)
        {
            return rawValue;
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(rawValue))
        {
            return rawValue;
        }

        if (effectiveType.IsEnum)
        {
            return rawValue switch
            {
                string text when Enum.TryParse(effectiveType, text, ignoreCase: true, out var parsedEnum) => parsedEnum,
                _ => TryConvertEnumNumeric(rawValue, effectiveType)
            };
        }

        if (rawValue is string textValue)
        {
            if (effectiveType == typeof(string))
            {
                return textValue;
            }

            if (effectiveType == typeof(bool))
            {
                if (bool.TryParse(textValue, out var boolResult))
                {
                    return boolResult;
                }

                if (long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericBool))
                {
                    return numericBool != 0;
                }

                return rawValue;
            }

            if (effectiveType == typeof(byte))
            {
                return byte.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(sbyte))
            {
                return sbyte.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(short))
            {
                return short.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(ushort))
            {
                return ushort.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(int))
            {
                return int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(uint))
            {
                return uint.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(long))
            {
                return long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(ulong))
            {
                return ulong.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(float))
            {
                return float.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(double))
            {
                return double.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }

            if (effectiveType == typeof(decimal))
            {
                return decimal.TryParse(textValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : rawValue;
            }
        }

        try
        {
            return Convert.ChangeType(rawValue, effectiveType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return rawValue;
        }
    }

    private static object? TryConvertEnumNumeric(object rawValue, Type enumType)
    {
        try
        {
            return Enum.ToObject(enumType, Convert.ToInt64(rawValue, CultureInfo.InvariantCulture));
        }
        catch
        {
            return rawValue;
        }
    }

    private static void PublishActionSnapshot(ItemModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        HostRegistries.Data.UpsertSnapshot(item.Path!, item.Clone(), DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
    }

    private static JsonNode BuildPythonArgumentPayload(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return new JsonObject();
        }

        var trimmed = argument.Trim();
        try
        {
            var parsed = JsonNode.Parse(trimmed);
            if (parsed is JsonObject or JsonArray)
            {
                return parsed;
            }

            return new JsonObject
            {
                ["value"] = parsed
            };
        }
        catch
        {
            return new JsonObject
            {
                ["value"] = trimmed
            };
        }
    }

    private static bool ToBooleanLikeValue(object? value)
        => value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong) => parsedLong != 0,
            byte numeric => numeric != 0,
            sbyte numeric => numeric != 0,
            short numeric => numeric != 0,
            ushort numeric => numeric != 0,
            int numeric => numeric != 0,
            uint numeric => numeric != 0,
            long numeric => numeric != 0,
            ulong numeric => numeric != 0,
            float numeric => Math.Abs(numeric) > float.Epsilon,
            double numeric => Math.Abs(numeric) > double.Epsilon,
            decimal numeric => numeric != 0,
            _ => false
        };

    private static bool TryReadItemEpoch(ItemModel item, out ulong epoch)
    {
        if (item.Properties.Has("epoch"))
        {
            var value = item.Properties["epoch"].Value;
            if (value is ulong ulongValue)
            {
                epoch = ulongValue;
                return true;
            }

            if (ulong.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out ulong parsed))
            {
                epoch = parsed;
                return true;
            }
        }

        epoch = 0;
        return false;
    }

    private static bool TryResolveProcessLog(string? targetLog, string? folderName, out ProcessLog? resolved)
    {
        resolved = null;
        if (string.IsNullOrWhiteSpace(targetLog))
        {
            return false;
        }

        var normalized = NormalizeLogTargetPath(targetLog);
        foreach (var candidate in EnumerateProcessLogResolutionCandidates(normalized, folderName))
        {
            if (HostRegistries.Data.TryResolve(candidate, out var item) && item?.Value is ProcessLog processLog)
            {
                resolved = processLog;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateProcessLogResolutionCandidates(string normalizedTargetLog, string? folderName)
    {
        if (string.IsNullOrWhiteSpace(normalizedTargetLog))
        {
            yield break;
        }

        yield return normalizedTargetLog;

        var normalizedFolder = TargetPathHelper.NormalizeConfiguredTargetPath(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolder))
        {
            yield break;
        }

        if (normalizedTargetLog.StartsWith("logs.", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"studio.{normalizedFolder}.{normalizedTargetLog}";
        }
    }

    private static string NormalizeLogTargetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = TargetPathHelper.NormalizeConfiguredTargetPath(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.Contains('.', StringComparison.Ordinal)
            ? normalized
            : $"Logs.{normalized}";
    }

    private static LogEventLevel ToLogEventLevel(MonitorLogLevel level)
    {
        return level switch
        {
            MonitorLogLevel.Debug => LogEventLevel.Debug,
            MonitorLogLevel.Info => LogEventLevel.Information,
            MonitorLogLevel.Warning => LogEventLevel.Warning,
            MonitorLogLevel.Error => LogEventLevel.Error,
            MonitorLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Warning
        };
    }

    private sealed record MonitorEvaluation(bool IsActive, string StatusText, object? Value);

    private sealed record MonitorPublishedRuntimeState(bool IsActive, string StatusText);
}