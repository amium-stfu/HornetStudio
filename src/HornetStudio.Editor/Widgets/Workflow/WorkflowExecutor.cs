using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Persistence.CustomControls;

namespace HornetStudio.Editor.Widgets.Workflow;

/// <summary>
/// Provides callbacks and context required to execute one workflow.
/// </summary>
public sealed class FunctionExecutionEnvironment
{
    public Func<FunctionSetValueStepDefinition, CancellationToken, ValueTask>? SetValueAsync { get; init; }

    public Func<FunctionLogStepDefinition, CancellationToken, ValueTask>? WriteLogAsync { get; init; }

    public Func<string, CancellationToken, ValueTask<FunctionConditionVariableResolutionResult>>? ResolveConditionSourceValueAsync { get; init; }

    public Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, ValueTask<bool>>? EvaluateConditionAsync { get; init; }

    public IReadOnlyDictionary<string, object?> ConditionVariables { get; init; } = new Dictionary<string, object?>();

    public FunctionExecutionStopController? StopController { get; init; }

    public Action<FunctionExecutionStatus>? StatusChanged { get; init; }
}

/// <summary>
/// Provides controlled external stop signaling for one running function execution.
/// </summary>
public sealed class FunctionExecutionStopController
{
    private int _stopRequested;

    /// <summary>
    /// Requests that the current function execution stop after the next safe checkpoint.
    /// </summary>
    public void RequestStop()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
    }

    /// <summary>
    /// Gets a value indicating whether a controlled stop was requested.
    /// </summary>
    public bool IsStopRequested => Volatile.Read(ref _stopRequested) != 0;
}

/// <summary>
/// Represents one workflow execution status update.
/// </summary>
/// <param name="State">The current workflow state.</param>
/// <param name="StepIndex">The zero-based active step index.</param>
/// <param name="ErrorMessage">The optional error message.</param>
public sealed record FunctionExecutionStatus(FunctionState State, int StepIndex, string ErrorMessage);

/// <summary>
/// Represents the final result of one workflow execution.
/// </summary>
/// <param name="State">The final workflow state.</param>
/// <param name="ErrorMessage">The optional error message.</param>
public sealed record FunctionExecutionResult(FunctionState State, string ErrorMessage);

/// <summary>
/// Represents the outcome of resolving one condition variable source path.
/// </summary>
/// <param name="Found">Indicates whether the configured source path could be resolved.</param>
/// <param name="Value">The resolved runtime value.</param>
public sealed record FunctionConditionVariableResolutionResult(bool Found, object? Value);

internal sealed class FunctionControlledStopException : Exception
{
}

/// <summary>
/// Executes declarative workflow step lists sequentially.
/// </summary>
public static class FunctionExecutor
{
    /// <summary>
    /// Executes one workflow definition until completion, failure, or cancellation.
    /// </summary>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="environment">The execution environment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final workflow execution result.</returns>
    public static async Task<FunctionExecutionResult> ExecuteAsync(
        FunctionDefinition definition,
        FunctionExecutionEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(environment);

        var currentStatus = new FunctionExecutionStatus(FunctionState.Idle, -1, string.Empty);

        void Publish(FunctionState state, int stepIndex, string errorMessage = "")
        {
            currentStatus = new FunctionExecutionStatus(state, stepIndex, errorMessage ?? string.Empty);
            environment.StatusChanged?.Invoke(currentStatus);
        }

        using var registration = cancellationToken.Register(() =>
        {
            if (currentStatus.State == FunctionState.Running)
            {
                Publish(FunctionState.Stopping, currentStatus.StepIndex, currentStatus.ErrorMessage);
            }
        });

        Publish(FunctionState.Running, -1);

        try
        {
            await ExecuteStepsAsync(definition.Steps, environment, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfStopRequested(environment);
            Publish(FunctionState.Done, definition.Steps.Count - 1);
            return new FunctionExecutionResult(FunctionState.Done, string.Empty);
        }
        catch (FunctionControlledStopException)
        {
            Publish(FunctionState.Done, currentStatus.StepIndex);
            return new FunctionExecutionResult(FunctionState.Done, string.Empty);
        }
        catch (OperationCanceledException)
        {
            Publish(FunctionState.Canceled, currentStatus.StepIndex);
            return new FunctionExecutionResult(FunctionState.Canceled, string.Empty);
        }
        catch (Exception ex)
        {
            Publish(FunctionState.Failed, currentStatus.StepIndex, ex.Message);
            return new FunctionExecutionResult(FunctionState.Failed, ex.Message);
        }

        async Task ExecuteStepsAsync(IReadOnlyList<FunctionStepDefinition> steps, FunctionExecutionEnvironment executionEnvironment, CancellationToken token)
        {
            for (var index = 0; index < steps.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                ThrowIfStopRequested(executionEnvironment);
                Publish(FunctionState.Running, index);
                await ExecuteStepAsync(steps[index], executionEnvironment, token).ConfigureAwait(false);
            }
        }
    }

    private static async Task ExecuteStepAsync(FunctionStepDefinition step, FunctionExecutionEnvironment environment, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case FunctionSetValueStepDefinition setValue:
                if (string.IsNullOrWhiteSpace(setValue.Target))
                {
                    throw new InvalidOperationException("SetValue step requires a target.");
                }

                if (environment.SetValueAsync is null)
                {
                    throw new InvalidOperationException("Workflow execution environment does not provide SetValue handling.");
                }

                await environment.SetValueAsync(setValue, cancellationToken).ConfigureAwait(false);
                return;

            case FunctionDelayStepDefinition delay:
                if (delay.Milliseconds < 0)
                {
                    throw new InvalidOperationException("Delay milliseconds must be zero or greater.");
                }

                await Task.Delay(delay.Milliseconds, cancellationToken).ConfigureAwait(false);
                return;

            case FunctionIfThenElseStepDefinition conditional:
            {
                if (string.IsNullOrWhiteSpace(conditional.Condition))
                {
                    throw new InvalidOperationException("IfThenElse step requires a condition.");
                }

                var conditionVariables = await BuildConditionVariablesAsync(conditional, environment, cancellationToken).ConfigureAwait(false);
                var branchResult = await EvaluateConditionAsync(conditional.Condition, conditionVariables, environment, cancellationToken).ConfigureAwait(false);
                var branch = branchResult ? conditional.Then : conditional.Else;
                foreach (var nestedStep in branch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteStepAsync(nestedStep, environment, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            case FunctionWhileStepDefinition loop:
            {
                if (string.IsNullOrWhiteSpace(loop.Condition))
                {
                    throw new InvalidOperationException("While step requires a condition.");
                }

                if (!loop.Steps.OfType<FunctionDelayStepDefinition>().Any(static step => step.Milliseconds > 0))
                {
                    throw new InvalidOperationException("While step requires at least one positive Delay step in its body.");
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfStopRequested(environment);
                    var loopVariables = await BuildConditionVariablesAsync(loop, environment, cancellationToken).ConfigureAwait(false);
                    var shouldContinue = await EvaluateConditionAsync(loop.Condition, loopVariables, environment, cancellationToken).ConfigureAwait(false);
                    if (!shouldContinue)
                    {
                        break;
                    }

                    foreach (var nestedStep in loop.Steps)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ThrowIfStopRequested(environment);
                        await ExecuteStepAsync(nestedStep, environment, cancellationToken).ConfigureAwait(false);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfStopRequested(environment);
                }

                return;
            }

            case FunctionLogStepDefinition log:
                if (string.IsNullOrWhiteSpace(log.TargetLog))
                {
                    throw new InvalidOperationException("Log step requires an explicit target log.");
                }

                if (environment.WriteLogAsync is null)
                {
                    throw new InvalidOperationException("Workflow execution environment does not provide log handling.");
                }

                await environment.WriteLogAsync(log, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new InvalidOperationException($"Workflow step type '{step.GetType().Name}' is not supported.");
        }
    }

    private static void ThrowIfStopRequested(FunctionExecutionEnvironment environment)
    {
        if (environment.StopController?.IsStopRequested == true)
        {
            throw new FunctionControlledStopException();
        }
    }

    private static async ValueTask<bool> EvaluateConditionAsync(
        string condition,
        IReadOnlyDictionary<string, object?> variables,
        FunctionExecutionEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (environment.EvaluateConditionAsync is not null)
        {
            return await environment.EvaluateConditionAsync(condition, variables, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!CustomSignalFormulaEngine.TryEvaluateBooleanExpression(condition, variables, out var result, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return result;
    }

    private static async ValueTask<IReadOnlyDictionary<string, object?>> BuildConditionVariablesAsync(
        FunctionStepDefinition conditional,
        FunctionExecutionEnvironment environment,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object?>(environment.ConditionVariables, StringComparer.OrdinalIgnoreCase);
        var stepVariables = conditional switch
        {
            FunctionIfThenElseStepDefinition ifThenElse => ifThenElse.Variables,
            FunctionWhileStepDefinition loop => loop.Variables,
            _ => throw new InvalidOperationException($"Workflow step type '{conditional.GetType().Name}' does not support condition variables.")
        };

        if (stepVariables.Count == 0)
        {
            return variables;
        }

        if (environment.ResolveConditionSourceValueAsync is null)
        {
            throw new InvalidOperationException("Workflow execution environment does not provide condition variable resolution.");
        }

        foreach (var variable in stepVariables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                throw new InvalidOperationException("Condition variable name is required.");
            }

            if (string.IsNullOrWhiteSpace(variable.SourcePath))
            {
                throw new InvalidOperationException($"Condition variable '{variable.Name}' requires a source path.");
            }

            var resolution = await environment.ResolveConditionSourceValueAsync(variable.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!resolution.Found)
            {
                throw new InvalidOperationException($"Condition variable source '{variable.SourcePath}' could not be resolved.");
            }

            variables[variable.Name] = resolution.Value;
        }

        return variables;
    }
}
