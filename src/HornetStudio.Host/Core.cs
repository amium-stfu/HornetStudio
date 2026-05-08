using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Logging;

namespace HornetStudio.Host;


public enum CoreState
{
    Init,
    Run,
    Stop,
    Rebuild,
    None
}


public static class Core
{
    private static bool _roslynInitialized;
    private static readonly object RoslynSync = new();

    //State Heartbeat to keep the editor updated on the current state of the host, especially during long operations like build and run. The editor can use this to show appropriate status messages or indicators.
    static Core()
    {
        EnsureRoslynInitialized();
    }
    
    public static System.Threading.Timer? PipeHeartbeat;
    public static CoreState State { get; private set; } = CoreState.Init;
    public static bool HasAlert { get; private set; }
    static string stateMessage = HasAlert ? "Alert:" + State.ToString() : "Status:" + State.ToString();


    private static readonly SemaphoreSlim BuildSemaphore = new(1, 1);
    private static readonly ProjectRoslynCompiler Compiler = new();
    private static readonly object PipeSync = new();
    private static readonly object RuntimeSync = new();

    public static ServerSide? ComChannel { get; private set; }
    public static string? OpenedDirectory { get; private set; }
    public static ProjectBuildResult? LastBuildResult { get; private set; }
    public static string? PipeInfoFilePath { get; private set; }
    public static bool IsRuntimeRunning { get; private set; }
    private static Type? CurrentProgramType { get; set; }
    public static event Action<string, ProjectModel?>? UiStateChanged;

    private static void EnsureRoslynInitialized()
    {
        lock (RoslynSync)
        {
            if (_roslynInitialized)
            {
                return;
            }

            ProjectLoader.ReferencePathResolver = projectRoot =>
            {
                HostPluginCatalog.EnsureLoaded();
                return HostPluginCatalog.GetProjectReferencePaths(projectRoot);
            };

            ProjectRoslynCompiler.AdditionalReferenceResolver = () =>
            {
                HostPluginCatalog.EnsureLoaded();
                return HostPluginCatalog.GetProjectReferencePaths(AppContext.BaseDirectory);
            };

            ProjectRoslynCompiler.InfoLogger = LogInfo;
            ProjectRoslynCompiler.DebugLogger = message => LogDebug(message);
            _roslynInitialized = true;
        }
    }

    public static void LogInfo(string message)
    {
        HostLogger.Log.Information(message);
        SendToEditor("LogInfo", message);
    }

    public static void LogDebug(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            HostLogger.Log.Debug(message);
        }
        else
        {
            HostLogger.Log.Debug(ex, message);
        }

        SendToEditor("LogDebug", ex is null ? message : $"{message}{Environment.NewLine}{ex}");
    }

    public static void LogWarn(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            HostLogger.Log.Warning(message);
        }
        else
        {
            HostLogger.Log.Warning(ex, message);
        }

        SendToEditor("LogWarn", ex is null ? message : $"{message}{Environment.NewLine}{ex}");
    }

    public static void LogError(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            HostLogger.Log.Error(message);
        }
        else
        {
            HostLogger.Log.Error(ex, message);
        }

        SendToEditor("LogError", ex is null ? message : $"{message}{Environment.NewLine}{ex}");
    }

    public static void LogFatal(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            HostLogger.Log.Fatal(message);
        }
        else
        {
            HostLogger.Log.Fatal(ex, message);
        }

        SendToEditor("LogFatal", ex is null ? message : $"{message}{Environment.NewLine}{ex}");
    }

    public static string InitPipeCom(string directory)
    {
       HostPluginCatalog.EnsureLoaded();
       if(PipeHeartbeat != null)
        {
            PipeHeartbeat.Dispose();
        }
       
       PipeHeartbeat = new System.Threading.Timer((e) =>
        {
            SendToEditor("Status:" + State.ToString(), System.DateTime.Now.ToString());
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

       
        var targetDirectory = ResolveProjectDirectory(directory);
        var pipeInfoFilePath = Path.Combine(targetDirectory, "pipes.json");

        lock (PipeSync)
        {
            if (ComChannel is not null && string.Equals(PipeInfoFilePath, pipeInfoFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return pipeInfoFilePath;
            }

            if (ComChannel is not null)
            {
                ShutdownAsync().AsTask().GetAwaiter().GetResult();
            }

            PipeNames.ResetPipes();
            PipeNames.SavePipesToFile(pipeInfoFilePath);
            PipeInfoFilePath = pipeInfoFilePath;

            ComChannel = new ServerSide();
            ComChannel.OnReceived += command => _ = HandlePipeCommandAsync(command);

            OpenedDirectory = targetDirectory;

            LogInfo($"Pipe server initialized. Server={PipeNames.Server} Client={PipeNames.Client} File={pipeInfoFilePath}");
            SendToEditor("PipeReady", PipeNames.Server, PipeNames.Client, pipeInfoFilePath);
            return pipeInfoFilePath;
        }
    }

    public static async ValueTask ShutdownAsync()
    {
        if (ComChannel is null)
        {
            return;
        }

        LogInfo("Pipe server shutdown requested");
        await ComChannel.DisposeAsync();
        ComChannel = null;
        PipeInfoFilePath = null;
    }

    public static void SetOpenedDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        OpenedDirectory = ResolveProjectDirectory(directory);
        LogInfo($"Opened directory set to {OpenedDirectory}");
    }

    public static async Task<ProjectBuildResult> LoadAndRunAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        if (IsRuntimeRunning)
        {
            LogInfo("LoadAndRun requested while runtime is active. Destroying current runtime first.");
            DestroyRuntime();
        }

        var result = await RebuildAsync(directory, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            LogWarn($"LoadAndRun aborted because build failed for {result.Project.RootDirectory}");
            return result;
        }

        RunRuntime();
        return result;
    }

    public static Task<ProjectBuildResult> LoadAndRunProjectAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        return LoadAndRunAsync(path, cancellationToken);
    }

    public static async Task<ProjectBuildResult> RebuildAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        var targetDirectory = ResolveProjectDirectory(directory);
        HostPluginCatalog.EnsureLoaded();
        State = CoreState.Rebuild;
        HasAlert = false;

        await BuildSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRuntimeRunning)
            {
                LogInfo("Rebuild requested while runtime is active. Destroying current runtime first.");
                DestroyRuntime();
                State = CoreState.Rebuild;
                HasAlert = false;
            }

            OpenedDirectory = targetDirectory;
            var project = ProjectLoader.Load(targetDirectory);
            SendProjectLoaded(project);
            SendToEditor("BuildStarted", targetDirectory);
            LogInfo($"Build started for {project.RootDirectory} with {project.SourceFiles.Count} source files");

            var result = await Compiler.BuildAsync(project, cancellationToken).ConfigureAwait(false);
            LastBuildResult = result;
            if (!result.Success)
            {
                HasAlert = true;
            }

            LogInfo($"Build finished for {result.Project.RootDirectory}. Success={result.Success} Errors={result.ErrorCount} Warnings={result.WarningCount}");

            SendToEditor(
                "BuildCompleted",
                result.Success ? "Success" : "Failure",
                result.Project.RootDirectory,
                result.Project.ProjectName,
                result.AssemblyPath,
                result.PdbPath,
                result.ErrorCount.ToString(),
                result.WarningCount.ToString(),
                result.Project.Folders.Count.ToString(),
                result.Project.SourceFiles.Count.ToString(),
                result.Project.UiFiles.Count.ToString());

            foreach (var diagnostic in result.Diagnostics)
            {
                var span = diagnostic.Location.GetMappedLineSpan();
                var path = string.IsNullOrWhiteSpace(span.Path) ? diagnostic.Location.SourceTree?.FilePath ?? string.Empty : span.Path;

                LogDiagnostic(diagnostic, path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);

                SendToEditor(
                    "BuildDiagnostic",
                    diagnostic.Severity.ToString(),
                    diagnostic.Id,
                    diagnostic.GetMessage(),
                    path,
                    (span.StartLinePosition.Line + 1).ToString(),
                    (span.StartLinePosition.Character + 1).ToString());
            }

            return result;
        }
        finally
        {
            BuildSemaphore.Release();
        }
    }

    public static Task<ProjectBuildResult> RebuildProjectAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        return RebuildAsync(path, cancellationToken);
    }

    public static void SendToEditor(string command, params string[] args)
    {
        try
        {
            ComChannel?.Send(new PipeCommand
            {
                Command = command,
                Args = args
            });
        }
        catch (Exception)
        {
           // HostLogger.Log.Error(ex, "SendToEditor failed for command {Command}", command);
        }
    }

    private static async Task HandlePipeCommandAsync(PipeCommand command)
    {
        if (command.Command is null)
        {
            return;
        }

        try
        {
            LogInfo($"Received pipe command {command.Command} with {command.Args?.Length ?? 0} args");
            switch (command.Command)
            {
                case "Rebuild":
                {
                    var directory = command.Args?.FirstOrDefault();
                    await RebuildAsync(directory).ConfigureAwait(false);
                    break;
                }
                case "Destroy":
                {
                    DestroyRuntime();
                    SendToEditor("Destroyed");
                    break;
                }
                case "Run":
                {
                    RunRuntime();
                    SendToEditor("Running");
                    break;
                }
                case "OpenDirectory":
                {
                    var directory = command.Args?.FirstOrDefault();
                    SetOpenedDirectory(directory);
                    if (!string.IsNullOrWhiteSpace(OpenedDirectory))
                    {
                        SendToEditor("DirectoryOpened", OpenedDirectory!);
                        SendProjectLoaded(ProjectLoader.Load(OpenedDirectory!));
                    }

                    break;
                }
                case "Ping":
                {
                    SendToEditor("Pong", OpenedDirectory ?? string.Empty);
                    break;
                }
                default:
                {
                    LogWarn($"Unknown pipe command {command.Command}");
                    SendToEditor("UnknownCommand", command.Command);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            HasAlert = true;
            LogError($"Pipe command failed: {command.Command}", ex);
            SendToEditor("BuildError", ex.Message);
        }
    }

    private static string ResolveProjectDirectory(string? directory)
    {
        var candidate = !string.IsNullOrWhiteSpace(directory)
            ? directory
            : OpenedDirectory;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException("No project directory is configured for rebuild.");
        }

        var fullPath = Path.GetFullPath(candidate);

        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath)
                ?? throw new DirectoryNotFoundException(fullPath);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        var current = new DirectoryInfo(fullPath);
        while (current is not null)
        {
            var hasFolders = Directory.Exists(Path.Combine(current.FullName, "Folders"))
                || Directory.Exists(Path.Combine(current.FullName, "Pages"));
            var hasProgram = File.Exists(Path.Combine(current.FullName, "Program.cs"));
            var hasProjectManifest = File.Exists(Path.Combine(current.FullName, "project.json"))
                || File.Exists(Path.Combine(current.FullName, "Book.json"));

            if (hasFolders && (hasProgram || hasProjectManifest))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not determine project root from '{fullPath}'.");
    }

    private static void SendProjectLoaded(ProjectModel project)
    {
        LogInfo($"Project loaded {project.ProjectName} Folders={project.Folders.Count} Sources={project.SourceFiles.Count} UiFiles={project.UiFiles.Count}");

        SendToEditor(
            "ProjectLoaded",
            project.RootDirectory,
            project.ProjectName,
            project.TargetFramework,
            project.Folders.Count.ToString(),
            project.SourceFiles.Count.ToString(),
            project.UiFiles.Count.ToString());
    }

    private static void LogDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic, string path, int line, int column)
    {
        var severity = diagnostic.Severity.ToString();
        var message = diagnostic.GetMessage();

        switch (diagnostic.Severity)
        {
            case Microsoft.CodeAnalysis.DiagnosticSeverity.Error:
                LogError($"Build diagnostic {diagnostic.Id} {severity} {path}:{line}:{column} {message}");
                break;
            case Microsoft.CodeAnalysis.DiagnosticSeverity.Warning:
                LogWarn($"Build diagnostic {diagnostic.Id} {severity} {path}:{line}:{column} {message}");
                break;
            default:
                LogInfo($"Build diagnostic {diagnostic.Id} {severity} {path}:{line}:{column} {message}");
                break;
        }
    }

    private static void DestroyRuntime()
    {
        ProjectModel? project = null;
        State = CoreState.Stop;
        HasAlert = false;

        lock (RuntimeSync)
        {
            try
            {
                project = LastBuildResult?.Project;
                if (CurrentProgramType is not null)
                {
                    InvokeProgramMethod(CurrentProgramType, "Destroy");
                }
            }
            finally
            {
                CleanupManagedRuntimeResources();
                CurrentProgramType = null;
                IsRuntimeRunning = false;
                Compiler.UnloadRuntimeAssembly();
                if (LastBuildResult is not null)
                {
                    LastBuildResult = new ProjectBuildResult(
                        LastBuildResult.Project,
                        LastBuildResult.Success,
                        LastBuildResult.Diagnostics,
                        null,
                        LastBuildResult.AssemblyPath,
                        LastBuildResult.PdbPath,
                        LastBuildResult.AssemblyImage,
                        LastBuildResult.PdbImage);
                }
            }
        }

        LogInfo("Runtime destroyed");
        UiStateChanged?.Invoke("Destroy", project);
    }

    private static void CleanupManagedRuntimeResources()
    {
        RuntimeResourceScope.DisposeCurrent("DestroyRuntime");
    }

    private static void RunRuntime()
    {
        ProjectModel? project;

        lock (RuntimeSync)
        {
            if (LastBuildResult is null || !LastBuildResult.Success)
            {
                HasAlert = true;
                throw new InvalidOperationException("No successful build is available to run.");
            }

            State = CoreState.Rebuild;
            HasAlert = false;

            if (IsRuntimeRunning)
            {
                DestroyRuntime();
                State = CoreState.Rebuild;
                HasAlert = false;
            }

            var assembly = LastBuildResult.Assembly;
            if (assembly is null)
            {
                if (string.IsNullOrWhiteSpace(LastBuildResult.AssemblyPath) || !File.Exists(LastBuildResult.AssemblyPath))
                {
                    HasAlert = true;
                    throw new InvalidOperationException("The last build does not contain a runnable assembly file.");
                }

                LogInfo($"RunRuntime loading assembly from {LastBuildResult.AssemblyPath}");
                assembly = Compiler.LoadRuntimeAssembly(LastBuildResult.AssemblyPath);
                LastBuildResult = new ProjectBuildResult(
                    LastBuildResult.Project,
                    LastBuildResult.Success,
                    LastBuildResult.Diagnostics,
                    assembly,
                    LastBuildResult.AssemblyPath,
                    LastBuildResult.PdbPath,
                    LastBuildResult.AssemblyImage,
                    LastBuildResult.PdbImage);
            }
            else
            {
                LogInfo($"RunRuntime using cached assembly. FullName={assembly.FullName} Location={assembly.Location}");
            }

            var programType = assembly.GetType("QB.Program")
                ?? throw new InvalidOperationException("Type 'QB.Program' was not found in the built assembly.");

            LogInfo($"RunRuntime resolved QB.Program. Assembly={assembly.FullName} Location={assembly.Location}");

            RuntimeResourceScope.BeginNewScope("RunRuntime");

            try
            {
                InvokeProgramMethod(programType, "Initialize");
                InvokeProgramMethod(programType, "Run");
            }
            catch
            {
                RuntimeResourceScope.DisposeCurrent("RunRuntime failed");
                throw;
            }

            CurrentProgramType = programType;
            IsRuntimeRunning = true;
            State = CoreState.Run;
            project = LastBuildResult.Project;
        }

        LogInfo("Runtime started");
        UiStateChanged?.Invoke("Run", project);
    }

    private static void InvokeProgramMethod(Type programType, string methodName)
    {
        var method = programType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        if (method is null)
        {
            HasAlert = true;
            LogWarn($"QB.Program.{methodName}() not found");
            return;
        }

        try
        {
            LogInfo($"Invoking QB.Program.{methodName}()");
            method.Invoke(null, null);
            LogInfo($"Invoked QB.Program.{methodName}() successfully");
        }
        catch
        {
            HasAlert = true;
            throw;
        }
    }
}


