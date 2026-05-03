using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Amium.Items;
using HornetStudio.Logging;

namespace HornetStudio.Host.Python.Client;

/// <summary>
/// Out-of-process Python client host that communicates over stdin/stdout
/// using line-delimited JSON messages.
///
/// Responsibilities (host side):
/// - Owns lifecycle of the Python process.
/// - Performs versioned handshake (hello/init/ready).
/// - Provides a simple function registry and invocation surface.
/// - Routes log messages into host and per-client ProcessLog.
///
/// This is an internal building block; higher-level integration with
/// signals, UI, and project scope will be added on top of this type.
/// </summary>
public sealed class PythonClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly PythonClientOptions _options;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly TaskCompletionSource _handshakeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PythonFunctionResult>> _pendingInvocations = new();
    private readonly ConcurrentDictionary<string, PythonFunctionInfo> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _valueRegistryKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HostValueDefinitionPayload> _hostValuesByPath = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _stateLock = new();
    private Process? _process;
    private Task? _stdoutLoop;
    private Task? _stderrLoop;
    private bool _hostValueBridgeAttached;
    private bool _ready;
    private bool _disposed;

    public PythonClient(PythonClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var clientLogDirectory = Path.Combine(HostLogger.LogDirectory, "python-clients", SanitizeName(_options.Name));
        Directory.CreateDirectory(clientLogDirectory);

        ClientLog = new ProcessLog();
        ClientLog.InitializeLog(clientLogDirectory);

        HostLogger.Log.Information("PythonClient created. Name={Name} Type={Type} Script={Script}", _options.Name, _options.ClientType, _options.ScriptPath);
        ClientLog.Info($"PythonClient created. Name={_options.Name} Type={_options.ClientType} Script={_options.ScriptPath}");
    }

    public string Name => _options.Name;
    public string ClientType => _options.ClientType;
    public string BridgeVersion => _options.BridgeVersion;

    /// <summary>
    /// Dedicated per-client ProcessLog for diagnostic output from this PythonClient.
    /// </summary>
    public ProcessLog ClientLog { get; }

    /// <summary>
    /// Registered Python functions (declared by define_function messages).
    /// </summary>
    public IReadOnlyCollection<PythonFunctionInfo> Functions => _functions.Values.ToArray();

    /// <summary>
    /// Starts the Python process and completes the handshake (hello/init/ready).
    /// Throws if the handshake cannot be completed within the configured timeout.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_process is not null)
            {
                throw new InvalidOperationException("PythonClient already started.");
            }
        }

        var scriptPath = ResolveScriptPath(_options.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Python client script not found: {scriptPath}", scriptPath);
        }

        var workingDirectory = !string.IsNullOrWhiteSpace(_options.WorkingDirectory)
            ? _options.WorkingDirectory!
            : Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

        var psi = new ProcessStartInfo
        {
            FileName = _options.PythonExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // -u: unbuffered binary stdout and stderr; keeps latency low for line-delimited JSON.
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(scriptPath);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start Python process.");
            }
        }
        catch (Exception ex)
        {
            HostLogger.Log.Error(ex, "PythonClient failed to start. Name={Name} Script={Script}", _options.Name, scriptPath);
            ClientLog.Error($"Failed to start Python process: {ex.Message}", ex);
            throw;
        }

        lock (_stateLock)
        {
            _process = process;
        }

        HostLogger.Log.Information("PythonClient process started. Name={Name} Pid={Pid}", _options.Name, process.Id);
        ClientLog.Info($"Python process started. Pid={process.Id}");

        _stdoutLoop = Task.Run(() => ReadStdoutLoopAsync(process, _lifetimeCts.Token));
        _stderrLoop = Task.Run(() => ReadStderrLoopAsync(process, _lifetimeCts.Token));

        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        handshakeCts.CancelAfter(_options.HandshakeTimeout);

        try
        {
            await _handshakeCompleted.Task.WaitAsync(handshakeCts.Token).ConfigureAwait(false);

            if (!_ready)
            {
                throw BuildHandshakeFailureException(process, scriptPath);
            }
        }
        catch (OperationCanceledException)
        {
            HostLogger.Log.Error("PythonClient handshake timed out. Name={Name}", _options.Name);
            ClientLog.Error("Handshake timed out.", new TimeoutException("Python client handshake timeout."));
            await HardStopInternalAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> WaitForProcessExitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Process? process;
        lock (_stateLock)
        {
            process = _process;
        }

        if (process is null)
        {
            return 0;
        }

        await WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return process.ExitCode;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Requests a graceful stop (soft stop). The client may react to a
    /// "stop" message and shut down on its own. If it does not exit within
    /// the configured timeout, a hard stop is applied.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        HostLogger.Log.Information("PythonClient soft stop requested. Name={Name}", _options.Name);
        ClientLog.Info("Soft stop requested.");

        try
        {
            await SendAsync(new PythonMessageEnvelope
            {
                Type = "stop",
                BridgeVersion = _options.BridgeVersion,
                Payload = null
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process is already gone; nothing left to stop gracefully.
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        timeoutCts.CancelAfter(_options.SoftStopTimeout);

        try
        {
            await WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            HostLogger.Log.Warning("PythonClient soft stop timed out. Escalating to hard stop. Name={Name}", _options.Name);
            ClientLog.Info("Soft stop timed out. Escalating to hard stop.");
            await HardStopInternalAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Immediately kills the Python process and its process tree.
    /// </summary>
    public Task HardStopAsync()
    {
        ThrowIfDisposed();
        return HardStopInternalAsync();
    }

    /// <summary>
    /// Invokes a Python function previously registered via define_function.
    /// The call is executed via the JSON bridge and completed when a
    /// corresponding result message arrives or when the timeout elapses.
    /// </summary>
    public Task<PythonFunctionResult> InvokeFunctionAsync(
        string functionName,
        JsonNode? args = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("Function name must not be empty.", nameof(functionName));
        }

        if (!_ready)
        {
            throw new InvalidOperationException("PythonClient is not ready. Handshake not completed.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<PythonFunctionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingInvocations.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException("Failed to register pending invocation.");
        }

        var payload = new JsonObject
        {
            ["function_name"] = functionName,
            ["args"] = args
        };

        var envelope = new PythonMessageEnvelope
        {
            Type = "invoke",
            RequestId = requestId,
            BridgeVersion = _options.BridgeVersion,
            Payload = payload
        };

        var effectiveTimeout = timeout ?? _options.DefaultInvokeTimeout;

        var sendTask = SendAsync(envelope, cancellationToken);

        return CompleteInvocationAsync(requestId, tcs, sendTask, effectiveTimeout, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        DetachHostValueBridge();

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
            await HardStopInternalAsync().ConfigureAwait(false);
        }

        _lifetimeCts.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReadStdoutLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = process.StandardOutput;
            while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                HandleIncomingLine(line);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            HostLogger.Log.Error(ex, "PythonClient stdout loop failed. Name={Name}", _options.Name);
            ClientLog.Error($"Stdout loop failed: {ex.Message}", ex);
        }
        finally
        {
            CompleteHandshakeIfNotDone();
        }
    }

    private async Task ReadStderrLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = process.StandardError;
            while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // stderr is reserved for diagnostics.
                HostLogger.Log.Warning("[Python stderr] {Line}", line);
                ClientLog.Debug($"stderr: {line}");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            HostLogger.Log.Error(ex, "PythonClient stderr loop failed. Name={Name}", _options.Name);
            ClientLog.Error($"Stderr loop failed: {ex.Message}", ex);
        }
    }

    private void HandleIncomingLine(string line)
    {
        PythonMessageEnvelope? envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize<PythonMessageEnvelope>(line, JsonOptions);
        }
        catch (Exception ex)
        {
            HostLogger.Log.Error(ex, "Failed to parse PythonClient message. Name={Name} Line={Line}", _options.Name, line);
            ClientLog.Error($"Failed to parse message: {ex.Message}. Line={line}", ex);
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            return;
        }

        switch (envelope.Type)
        {
            case "hello":
                HandleHello(envelope);
                break;
            case "ready":
                HandleReady(envelope);
                break;
            case "log":
                HandleLog(envelope);
                break;
            case "define_function":
                HandleDefineFunction(envelope);
                break;
            case "define_value":
                HandleDefineValue(envelope);
                break;
            case "value_update":
                HandleValueUpdate(envelope);
                break;
            case "host_value_write":
                HandleHostValueWrite(envelope);
                break;
            case "result":
                HandleResult(envelope);
                break;
            case "error":
                HandleError(envelope);
                break;
            default:
                HostLogger.Log.Debug("Unhandled PythonClient message type. Name={Name} Type={Type}", _options.Name, envelope.Type);
                ClientLog.Debug($"Unhandled message type: {envelope.Type}");
                break;
        }
    }

    private void HandleHello(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<HelloPayload>(envelope.Payload);
        if (payload is null)
        {
            return;
        }

        var pythonBridgeVersion = !string.IsNullOrWhiteSpace(payload.BridgeVersion)
            ? payload.BridgeVersion
            : envelope.BridgeVersion ?? string.Empty;

        HostLogger.Log.Information(
            "PythonClient hello received. Name={Name} ClientName={ClientName} Bridge={Bridge} Capabilities={Caps}",
            _options.Name,
            payload.ClientName,
            pythonBridgeVersion,
            payload.Capabilities is { Length: > 0 } caps ? string.Join(",", caps) : "-");

        ClientLog.Info($"hello received: client={payload.ClientName} bridge={pythonBridgeVersion}");

        if (!IsBridgeVersionCompatible(pythonBridgeVersion, _options.BridgeVersion))
        {
            HostLogger.Log.Warning("PythonClient bridge version incompatible. Name={Name} Python={PythonBridge} Host={HostBridge}", _options.Name, pythonBridgeVersion, _options.BridgeVersion);
            ClientLog.Info($"Bridge version incompatible. Python={pythonBridgeVersion} Host={_options.BridgeVersion}");

            var rejectPayload = new JsonObject
            {
                ["reason"] = "bridge_version_incompatible",
                ["expected_bridge_version"] = _options.BridgeVersion,
                ["actual_bridge_version"] = pythonBridgeVersion
            };

            _ = SendAsync(new PythonMessageEnvelope
            {
                Type = "reject",
                BridgeVersion = _options.BridgeVersion,
                Payload = rejectPayload
            }, CancellationToken.None);

            _ = HardStopInternalAsync();
            CompleteHandshakeIfNotDone();
            return;
        }

        var initPayload = new InitPayload
        {
            BridgeVersion = _options.BridgeVersion,
            SessionId = Guid.NewGuid().ToString("N"),
            Configuration = _options.Configuration,
            AllowedCapabilities = _options.AllowedCapabilities?.ToArray() ?? Array.Empty<string>(),
            Timeouts = new InitTimeoutConfig
            {
                HandshakeMs = (int)_options.HandshakeTimeout.TotalMilliseconds,
                SoftStopMs = (int)_options.SoftStopTimeout.TotalMilliseconds,
                DefaultInvokeMs = (int)_options.DefaultInvokeTimeout.TotalMilliseconds
            },
            RuntimeScopeMetadata = _options.RuntimeScopeMetadata,
            HostValues = BuildHostValueProjection().ToArray()
        };

        EnsureHostValueBridgeAttached();

        var envelopeOut = new PythonMessageEnvelope
        {
            Type = "init",
            BridgeVersion = _options.BridgeVersion,
            Payload = JsonSerializer.SerializeToNode(initPayload, JsonOptions)
        };

        _ = SendAsync(envelopeOut, CancellationToken.None);
    }

    private void HandleReady(PythonMessageEnvelope envelope)
    {
        _ready = true;
        HostLogger.Log.Information("PythonClient ready. Name={Name}", _options.Name);
        ClientLog.Info("ready received.");
        CompleteHandshakeIfNotDone();
    }

    private void HandleLog(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<LogPayload>(envelope.Payload);
        if (payload is null)
        {
            return;
        }

        var level = (payload.Level ?? "info").Trim().ToLowerInvariant();
        var message = payload.Message ?? string.Empty;

        switch (level)
        {
            case "debug":
                HostLogger.Log.Debug("[Python {Name}] {Message}", _options.Name, message);
                ClientLog.Debug(message);
                break;
            case "warning":
            case "warn":
                HostLogger.Log.Warning("[Python {Name}] {Message}", _options.Name, message);
                ClientLog.Info(message);
                break;
            case "error":
                HostLogger.Log.Error("[Python {Name}] {Message}", _options.Name, message);
                ClientLog.Error(message, new InvalidOperationException("Python error"));
                break;
            default:
                HostLogger.Log.Information("[Python {Name}] {Message}", _options.Name, message);
                ClientLog.Info(message);
                break;
        }
    }

    private void HandleDefineFunction(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<DefineFunctionPayload>(envelope.Payload);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return;
        }

        var info = new PythonFunctionInfo(payload.Name.Trim(), payload.Description ?? string.Empty, payload.Category ?? string.Empty);
        _functions[info.Name] = info;

        HostLogger.Log.Information("PythonClient function registered. Name={ClientName} Function={Function}", _options.Name, info.Name);
        ClientLog.Info($"Function registered: {info.Name}");
    }

    private void HandleDefineValue(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<DefineValuePayload>(envelope.Payload);
        if (payload is null)
        {
            return;
        }

        var valueName = !string.IsNullOrWhiteSpace(payload.Name)
            ? payload.Name.Trim()
            : GetLastPathSegment(payload.Path);

        if (string.IsNullOrWhiteSpace(valueName))
        {
            return;
        }

        var registryPath = ResolveRegistryValuePath(valueName, payload.Path);
        _valueRegistryKeys[valueName] = registryPath;

        var snapshot = BuildValueSnapshot(
            registryPath,
            payload.Title ?? valueName,
            payload.Unit,
            ConvertJsonNodeToValue(payload.InitialValue),
            payload.ValueType);

        HostRegistries.Data.UpsertSnapshot(registryPath, snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: false);

        HostLogger.Log.Information("PythonClient value registered. Name={ClientName} Value={ValueName} Path={Path}", _options.Name, valueName, registryPath);
        ClientLog.Info($"Value registered: {valueName} -> {registryPath}");
    }

    private void HandleValueUpdate(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<ValueUpdatePayload>(envelope.Payload);
        if (payload is null)
        {
            return;
        }

        var valueName = !string.IsNullOrWhiteSpace(payload.Name)
            ? payload.Name.Trim()
            : GetLastPathSegment(payload.Path);

        if (string.IsNullOrWhiteSpace(valueName))
        {
            return;
        }

        var registryPath = ResolveRegistryValuePath(valueName, payload.Path);
        var value = ConvertJsonNodeToValue(payload.Value);

        if (!HostRegistries.Data.UpdateValue(registryPath, value, payload.Timestamp))
        {
            var snapshot = BuildValueSnapshot(registryPath, valueName, null, value, null);
            HostRegistries.Data.UpsertSnapshot(registryPath, snapshot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: false);
            HostRegistries.Data.UpdateValue(registryPath, value, payload.Timestamp);
        }

//        HostLogger.Log.Debug("PythonClient value updated. Name={ClientName} Value={ValueName} Path={Path} Value={Value}", _options.Name, valueName, registryPath, value);
        ClientLog.Debug($"Value updated: {valueName}={value}");
    }

    private void HandleHostValueWrite(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<HostValueWritePayload>(envelope.Payload);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Path))
        {
            return;
        }

        var path = NormalizeRegistryPath(payload.Path);
        var value = ConvertJsonNodeToValue(payload.Value);

        if (!HostRegistries.Data.UpdateValue(path, value, payload.Timestamp))
        {
            HostLogger.Log.Warning("PythonClient host value write ignored. Name={Name} Path={Path}", _options.Name, path);
            ClientLog.Info($"Host value write ignored for unknown path: {path}");
            return;
        }

        ClientLog.Debug($"Host value write: {path}={value}");
    }

    private void HandleResult(PythonMessageEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.RequestId))
        {
            return;
        }

        if (!_pendingInvocations.TryRemove(envelope.RequestId, out var tcs))
        {
            return;
        }

        var payload = DeserializePayload<FunctionResultPayload>(envelope.Payload) ?? new FunctionResultPayload
        {
            Success = false,
            Message = "Missing or invalid result payload.",
            Payload = null
        };

        var result = new PythonFunctionResult(payload.Success, payload.Message, payload.Payload);
        if (!payload.Success)
        {
            var message = string.IsNullOrWhiteSpace(payload.Message)
                ? "Python function invocation failed without an error message."
                : payload.Message!;
            HostLogger.Log.Warning("PythonClient function invocation failed. Name={Name} RequestId={RequestId} Message={Message}", _options.Name, envelope.RequestId, message);
            ClientLog.Error($"Function invocation failed. RequestId={envelope.RequestId} Message={message}", new InvalidOperationException(message));
        }

        tcs.TrySetResult(result);
    }

    private void HandleError(PythonMessageEnvelope envelope)
    {
        var payload = DeserializePayload<ErrorPayload>(envelope.Payload);
        if (payload is null)
        {
            return;
        }

        var message = payload.Message ?? "Python client reported error.";
        HostLogger.Log.Error("PythonClient error. Name={Name} Code={Code} Message={Message}", _options.Name, payload.Code, message);
        ClientLog.Error($"Error from client. Code={payload.Code} Message={message}", new InvalidOperationException(message));

        if (!string.IsNullOrWhiteSpace(envelope.RequestId) && _pendingInvocations.TryRemove(envelope.RequestId, out var tcs))
        {
            var result = new PythonFunctionResult(false, message, payload.Details);
            tcs.TrySetResult(result);
        }
    }

    private async Task SendAsync(PythonMessageEnvelope envelope, CancellationToken cancellationToken)
    {
        Process? process;
        lock (_stateLock)
        {
            process = _process;
        }

        if (process is null || process.HasExited)
        {
            throw new InvalidOperationException("PythonClient process is not running.");
        }

        var stdin = process.StandardInput;
        if (stdin.BaseStream is null)
        {
            throw new InvalidOperationException("PythonClient stdin is not available.");
        }

        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stdin.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stdin.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            HostLogger.Log.Error(ex, "Failed to send message to PythonClient. Name={Name}", _options.Name);
            ClientLog.Error($"Failed to send message: {ex.Message}", ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (_stateLock)
        {
            process = _process;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private async Task HardStopInternalAsync()
    {
        DetachHostValueBridge();
        _ready = false;

        Process? process;
        lock (_stateLock)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // No process is associated anymore; treat this as already stopped.
        }
        catch (Exception ex)
        {
            HostLogger.Log.Error(ex, "Failed to kill PythonClient process. Name={Name}", _options.Name);
            ClientLog.Error($"Failed to kill process: {ex.Message}", ex);
        }

        try
        {
            process.Dispose();
        }
        catch
        {
            // ignore
        }

        CompleteHandshakeIfNotDone();
    }

    private Exception BuildHandshakeFailureException(Process process, string scriptPath)
    {
        try
        {
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                var message = $"Python client exited before handshake completed. ExitCode={exitCode} Script={scriptPath}";
                HostLogger.Log.Error("PythonClient handshake failed. Name={Name} ExitCode={ExitCode} Script={Script}", _options.Name, exitCode, scriptPath);
                ClientLog.Error(message, new InvalidOperationException(message));
                return new InvalidOperationException(message);
            }
        }
        catch
        {
            // Ignore exit inspection errors and fall back to generic handshake failure.
        }

        var genericMessage = $"Python client handshake failed before ready. Script={scriptPath}";
        HostLogger.Log.Error("PythonClient handshake failed before ready. Name={Name} Script={Script}", _options.Name, scriptPath);
        ClientLog.Error(genericMessage, new InvalidOperationException(genericMessage));
        return new InvalidOperationException(genericMessage);
    }

    private static string SanitizeName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
        }
        return builder.ToString();
    }

    private string ResolveRegistryValuePath(string valueName, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return NormalizeRegistryPath(path);
        }

        if (_valueRegistryKeys.TryGetValue(valueName, out var existingPath))
        {
            return existingPath;
        }

        var registryRootPath = NormalizeRegistryPath(_options.RegistryRootPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(registryRootPath))
        {
            return $"{registryRootPath}.{SanitizePathSegment(valueName)}";
        }

        return $"PythonClients.{SanitizePathSegment(_options.Name)}.{SanitizePathSegment(valueName)}";
    }

    private static string NormalizeRegistryPath(string path)
    {
        var segments = path
            .Replace('\\', '.')
            .Replace('/', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizePathSegment)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment));

        return string.Join('.', segments);
    }

    private static string SanitizePathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "item";
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private static string GetLastPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = NormalizeRegistryPath(path);
        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? string.Empty : segments[^1];
    }

    private Item BuildValueSnapshot(string fullPath, string title, string? unit, object? initialValue, string? valueType)
    {
        var segments = NormalizeRegistryPath(fullPath)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            throw new InvalidOperationException("Registry path must not be empty.");
        }

        var leafName = segments[^1];
        var parentPath = segments.Length > 1
            ? string.Join('.', segments.Take(segments.Length - 1))
            : null;

        var current = string.IsNullOrWhiteSpace(parentPath)
            ? new Item(leafName, initialValue)
            : new Item(leafName, initialValue, parentPath);

        current.Params["Path"].Value = fullPath;
        current.Params["Kind"].Value = string.IsNullOrWhiteSpace(valueType) ? "PythonValue" : valueType!;
        current.Params["Text"].Value = title;
        current.Params["Title"].Value = title;
        if (!string.IsNullOrWhiteSpace(unit))
        {
            current.Params["Unit"].Value = unit;
        }

        return current;
    }

    private IReadOnlyList<HostValueDefinitionPayload> BuildHostValueProjection()
    {
        var definitions = new List<HostValueDefinitionPayload>();

        foreach (var rootKey in HostRegistries.Data.GetKeysByCapability(DataRegistryItemCapabilities.Display).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            if (!ShouldProjectHostValuePath(rootKey) || !HostRegistries.Data.TryGet(rootKey, out var rootItem) || rootItem is null)
            {
                continue;
            }

            CollectHostValueDefinitions(rootItem, rootKey, definitions);
        }

        AssignHostValueAliases(definitions);

        _hostValuesByPath.Clear();
        foreach (var definition in definitions)
        {
            _hostValuesByPath[definition.Path] = definition;
        }

        return definitions;
    }

    private void CollectHostValueDefinitions(Item item, string currentPath, ICollection<HostValueDefinitionPayload> definitions)
    {
        if (!ShouldProjectHostValuePath(currentPath))
        {
            return;
        }

        var children = item.GetDictionary();
        var hasChildren = children.Count > 0;
        var currentValue = item.Params.Has("Value") ? item.Params["Value"].Value : item.Value;

        if (!hasChildren || currentValue is not null)
        {
            definitions.Add(new HostValueDefinitionPayload
            {
                Path = currentPath,
                Title = GetItemDisplayTitle(item, currentPath),
                Unit = GetOptionalItemParameter(item, "Unit"),
                Format = GetOptionalItemParameter(item, "Format"),
                Kind = GetOptionalItemParameter(item, "Kind"),
                DataType = InferHostValueType(currentValue),
                IsWritable = true,
                Value = ConvertValueToJsonNode(currentValue)
            });
        }

        foreach (var childEntry in children.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            CollectHostValueDefinitions(childEntry.Value, currentPath + "." + childEntry.Key, definitions);
        }
    }

    private void EnsureHostValueBridgeAttached()
    {
        if (_hostValueBridgeAttached)
        {
            return;
        }

        HostRegistries.Data.ItemChanged += OnHostRegistryItemChanged;
        _hostValueBridgeAttached = true;
    }

    private void DetachHostValueBridge()
    {
        if (!_hostValueBridgeAttached)
        {
            return;
        }

        HostRegistries.Data.ItemChanged -= OnHostRegistryItemChanged;
        _hostValueBridgeAttached = false;
    }

    private void OnHostRegistryItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (!_ready || _disposed)
        {
            return;
        }

        if (e.ChangeKind == DataChangeKind.ValueUpdated)
        {
            SendProjectedHostValueUpdate(e.Key, e.Item, e.Timestamp);
            return;
        }

        if (e.ChangeKind != DataChangeKind.SnapshotUpserted)
        {
            return;
        }

        foreach (var projected in _hostValuesByPath.Values.Where(projected => projected.Path.Equals(e.Key, StringComparison.OrdinalIgnoreCase) || projected.Path.StartsWith(e.Key + ".", StringComparison.OrdinalIgnoreCase)))
        {
            if (!HostRegistries.Data.TryResolve(projected.Path, out var item) || item is null)
            {
                continue;
            }

            SendProjectedHostValueUpdate(projected.Path, item, e.Timestamp);
        }
    }

    private void SendProjectedHostValueUpdate(string path, Item item, ulong? timestamp)
    {
        if (!_hostValuesByPath.TryGetValue(path, out var projected))
        {
            return;
        }

        var currentValue = item.Params.Has("Value") ? item.Params["Value"].Value : item.Value;
        projected.Value = ConvertValueToJsonNode(currentValue);

        _ = SendAsync(new PythonMessageEnvelope
        {
            Type = "host_value_update",
            BridgeVersion = _options.BridgeVersion,
            Payload = JsonSerializer.SerializeToNode(new HostValueUpdatePayload
            {
                Alias = projected.Alias,
                Path = projected.Path,
                Value = projected.Value,
                Timestamp = timestamp
            }, JsonOptions)
        }, CancellationToken.None);
    }

    private static bool ShouldProjectHostValuePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path);
    }

    private static void AssignHostValueAliases(IReadOnlyList<HostValueDefinitionPayload> definitions)
    {
        var preferredCounts = definitions
            .Select(static definition => CreatePythonAlias(GetLastPathSegment(definition.Path)))
            .GroupBy(static alias => alias, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        var usedAliases = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var preferredAlias = CreatePythonAlias(GetLastPathSegment(definition.Path));
            var alias = preferredAlias;

            if (string.IsNullOrWhiteSpace(alias)
                || (preferredCounts.TryGetValue(preferredAlias, out var count) && count > 1)
                || !usedAliases.Add(alias))
            {
                alias = CreateUniquePythonAlias(definition.Path, usedAliases);
            }

            definition.Alias = alias;
        }
    }

    private static string CreateUniquePythonAlias(string path, ISet<string> usedAliases)
    {
        var baseAlias = CreatePythonAlias(path.Replace('/', '_').Replace('.', '_'));
        if (usedAliases.Add(baseAlias))
        {
            return baseAlias;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseAlias}_{suffix}";
            if (usedAliases.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string CreatePythonAlias(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "value";
        }

        var builder = new StringBuilder(rawName.Length + 4);
        foreach (var character in rawName.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? char.ToLowerInvariant(character) : '_');
        }

        var alias = builder.ToString().Trim('_');
        while (alias.Contains("__", StringComparison.Ordinal))
        {
            alias = alias.Replace("__", "_", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = "value";
        }

        if (char.IsDigit(alias[0]))
        {
            alias = "v_" + alias;
        }

        return alias switch
        {
            "class" or "def" or "for" or "if" or "import" or "in" or "is" or "lambda" or "not" or "or" or "pass" or "raise" or "return" or "try" or "while" or "with" or "yield" or "true" or "false" or "none" => alias + "_value",
            _ => alias
        };
    }

    private static string GetItemDisplayTitle(Item item, string path)
        => GetOptionalItemParameter(item, "Title")
            ?? GetOptionalItemParameter(item, "Text")
            ?? item.Name
            ?? GetLastPathSegment(path);

    private static string? GetOptionalItemParameter(Item item, string parameterName)
        => item.Params.Has(parameterName)
            ? item.Params[parameterName].Value?.ToString()
            : null;

    private static string InferHostValueType(object? value)
    {
        if (value is null)
        {
            return "unknown";
        }

        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type.IsEnum)
        {
            return "enum";
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => "int",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "float",
            _ => "object"
        };
    }

    private static JsonNode? ConvertValueToJsonNode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.SerializeToNode(value, JsonOptions);
        }
        catch
        {
            return JsonValue.Create(value.ToString());
        }
    }

    private static object? ConvertJsonNodeToValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }

            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }
        }

        return node.ToJsonString();
    }

    private static string ResolveScriptPath(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path must not be empty.", nameof(scriptPath));
        }

        return Path.IsPathRooted(scriptPath)
            ? scriptPath
            : Path.GetFullPath(scriptPath, Environment.CurrentDirectory);
    }

    private static bool IsBridgeVersionCompatible(string pythonVersion, string hostVersion)
    {
        if (string.IsNullOrWhiteSpace(pythonVersion) || string.IsNullOrWhiteSpace(hostVersion))
        {
            return false;
        }

        static int ParseMajor(string version)
        {
            var dotIndex = version.IndexOf('.');
            var majorPart = dotIndex >= 0 ? version[..dotIndex] : version;
            return int.TryParse(majorPart, out var major) ? major : 0;
        }

        return ParseMajor(pythonVersion) == ParseMajor(hostVersion);
    }

    private static TPayload? DeserializePayload<TPayload>(JsonNode? payload) where TPayload : class
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            return payload.Deserialize<TPayload>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void CompleteHandshakeIfNotDone()
    {
        if (_handshakeCompleted.Task.IsCompleted)
        {
            return;
        }

        _handshakeCompleted.TrySetResult();
    }

    private async Task<PythonFunctionResult> CompleteInvocationAsync(
        string requestId,
        TaskCompletionSource<PythonFunctionResult> tcs,
        Task sendTask,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await sendTask.ConfigureAwait(false);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
            if (completedTask != tcs.Task)
            {
                if (_pendingInvocations.TryRemove(requestId, out var pending))
                {
                    pending.TrySetResult(new PythonFunctionResult(false, "Invocation timed out.", null));
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (_pendingInvocations.TryRemove(requestId, out var pending))
            {
                pending.TrySetResult(new PythonFunctionResult(false, "Invocation timed out.", null));
            }
        }
        catch (Exception ex)
        {
            if (_pendingInvocations.TryRemove(requestId, out var pending))
            {
                pending.TrySetResult(new PythonFunctionResult(false, ex.Message, null));
            }
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PythonClient));
        }
    }
}

public sealed class PythonFunctionInfo
{
    public PythonFunctionInfo(string name, string description, string category)
    {
        Name = name;
        Description = description;
        Category = category;
    }

    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
}

public sealed record PythonFunctionResult(bool Success, string? Message, JsonNode? Payload);

internal sealed class PythonMessageEnvelope
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("bridge_version")]
    public string? BridgeVersion { get; set; }

    [JsonPropertyName("payload")]
    public JsonNode? Payload { get; set; }
}

internal sealed class HelloPayload
{
    [JsonPropertyName("bridge_version")]
    public string BridgeVersion { get; set; } = string.Empty;

    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = string.Empty;

    [JsonPropertyName("client_type")]
    public string? ClientType { get; set; }

    [JsonPropertyName("capabilities")]
    public string[]? Capabilities { get; set; }

    [JsonPropertyName("client_version")]
    public string? ClientVersion { get; set; }

    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; set; }
}

internal sealed class InitPayload
{
    [JsonPropertyName("bridge_version")]
    public string BridgeVersion { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("configuration")]
    public JsonNode? Configuration { get; set; }

    [JsonPropertyName("allowed_capabilities")]
    public string[] AllowedCapabilities { get; set; } = Array.Empty<string>();

    [JsonPropertyName("timeouts")]
    public InitTimeoutConfig? Timeouts { get; set; }

    [JsonPropertyName("runtime_scope_metadata")]
    public JsonNode? RuntimeScopeMetadata { get; set; }

    [JsonPropertyName("host_values")]
    public HostValueDefinitionPayload[] HostValues { get; set; } = Array.Empty<HostValueDefinitionPayload>();
}

internal sealed class InitTimeoutConfig
{
    [JsonPropertyName("handshake_ms")]
    public int HandshakeMs { get; set; }

    [JsonPropertyName("soft_stop_ms")]
    public int SoftStopMs { get; set; }

    [JsonPropertyName("default_invoke_ms")]
    public int DefaultInvokeMs { get; set; }
}

internal sealed class LogPayload
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class DefineFunctionPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

internal sealed class FunctionResultPayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("payload")]
    public JsonNode? Payload { get; set; }
}

internal sealed class ErrorPayload
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    public JsonNode? Details { get; set; }
}

internal sealed class DefineValuePayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("type")]
    public string? ValueType { get; set; }

    [JsonPropertyName("initial_value")]
    public JsonNode? InitialValue { get; set; }
}

internal sealed class ValueUpdatePayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("value")]
    public JsonNode? Value { get; set; }

    [JsonPropertyName("timestamp")]
    public ulong? Timestamp { get; set; }
}

internal sealed class HostValueDefinitionPayload
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("data_type")]
    public string DataType { get; set; } = "unknown";

    [JsonPropertyName("is_writable")]
    public bool IsWritable { get; set; }

    [JsonPropertyName("value")]
    public JsonNode? Value { get; set; }
}

internal sealed class HostValueUpdatePayload
{
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonNode? Value { get; set; }

    [JsonPropertyName("timestamp")]
    public ulong? Timestamp { get; set; }
}

internal sealed class HostValueWritePayload
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("value")]
    public JsonNode? Value { get; set; }

    [JsonPropertyName("timestamp")]
    public ulong? Timestamp { get; set; }
}
