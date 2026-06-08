using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HornetStudio.Host.Registries;

#if USE_PYTHONNET
using Python.Runtime;
#endif

namespace HornetStudio.Host.Python.Legacy;

public static class PythonScriptHost
{
    private static readonly ConcurrentDictionary<string, DateTime> _lastWriteTimes = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool IsLegacyScriptCompatible(string scriptPath, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            reason = "Kein Python-Skript konfiguriert.";
            return false;
        }

        var fullPath = ResolvePath(scriptPath);
        return IsLegacyScriptCompatibleResolvedPath(fullPath, out reason);
    }

    public static void ExecuteButtonScript(string scriptPath)
    {
        var context = CreateDefaultContext();
        ExecuteButtonScript(scriptPath, context);
    }

    public static object? ExecuteSignalScript(string scriptPath)
    {
        var context = CreateDefaultContext();
        return ExecuteSignalScript(scriptPath, context);
    }

    internal static void ExecuteButtonScript(string scriptPath, IScriptContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(scriptPath)) return;

        var fullPath = ResolvePath(scriptPath);
        if (!File.Exists(fullPath))
        {
            Core.LogWarn($"Python button script not found: {fullPath}");
            return;
        }

        if (!IsLegacyScriptCompatibleResolvedPath(fullPath, out var incompatibilityReason))
        {
            LogLegacyIncompatibleScript(fullPath, incompatibilityReason);
            return;
        }

        Core.LogInfo($"Python button script invoked: {fullPath}");

#if USE_PYTHONNET
        try
        {
            RunPythonFunction(fullPath, "on_click", context, expectResult: false);
        }
        catch (Exception ex)
        {
            Core.LogError($"Python button script failed: {fullPath}", ex);
        }
#else
        try
        {
            RunExternalPython(fullPath, "on_click", context, expectResult: false);
        }
        catch (Exception ex)
        {
            Core.LogError($"External Python button script failed: {fullPath}", ex);
        }
#endif
    }

    internal static object? ExecuteSignalScript(string scriptPath, IScriptContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(scriptPath)) return null;

        var fullPath = ResolvePath(scriptPath);
        if (!File.Exists(fullPath))
        {
            Core.LogWarn($"Python signal script not found: {fullPath}");
            return null;
        }

        if (!IsLegacyScriptCompatibleResolvedPath(fullPath, out var incompatibilityReason))
        {
            LogLegacyIncompatibleScript(fullPath, incompatibilityReason);
            return null;
        }

        // Signal scripts can run at high frequency; avoid flooding the host log.

#if USE_PYTHONNET
        try
        {
            return RunPythonFunction(fullPath, "read_value", context, expectResult: true);
        }
        catch (Exception ex)
        {
            Core.LogError($"Python signal script failed: {fullPath}", ex);
            return null;
        }
#else
        try
        {
            return RunExternalPython(fullPath, "read_value", context, expectResult: true);
        }
        catch (Exception ex)
        {
            Core.LogError($"External Python signal script failed: {fullPath}", ex);
            return null;
        }
#endif
    }

    private static IScriptContext CreateDefaultContext()
    {
        return new ScriptContext(HostRegistries.Signals);
    }

#if USE_PYTHONNET
    private static readonly object EngineSync = new();
    private static bool _engineInitialized;

    private static void EnsureEngineInitialized()
    {
        lock (EngineSync)
        {
            if (_engineInitialized)
            {
                return;
            }

            PythonEngine.Initialize();
            _engineInitialized = true;
        }
    }

    private static object? RunPythonFunction(string fullPath, string functionName, IScriptContext context, bool expectResult)
    {
        EnsureEngineInitialized();

        using (Py.GIL())
        {
            using var scope = Py.CreateScope();
            var code = File.ReadAllText(fullPath);
            scope.Set("ctx", context);
            scope.Exec(code);

            if (!scope.Contains(functionName))
            {
                if (expectResult)
                {
                    Core.LogWarn($"Python function '{functionName}' not found in script '{fullPath}'.");
                }

                return null;
            }

            using var pyFunc = scope.Get(functionName);
            using var pyResult = pyFunc.Invoke(new PyObject[] { context.ToPython() });

            if (!expectResult)
            {
                return null;
            }

            return pyResult.AsManagedObject(typeof(object));
        }
    }

#else
    private const string PythonExecutable = "python";

    private static object? RunExternalPython(string fullPath, string functionName, IScriptContext context, bool expectResult)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = PythonExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var escapedPath = fullPath
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);
            var escapedFunc = functionName.Replace("'", "\\'", StringComparison.Ordinal);

            // Mehrzeiligen Python-Code erzeugen, damit das if-Statement syntaktisch gueltig ist.
            var code = string.Join("\n", new[]
            {
                // Wir rufen eine benannte Funktion aus dem Userskript auf und
                // unterstuetzen sowohl Signaturen ohne Argumente als auch mit
                // einem einfachen Kontextobjekt (ctx).
                "import importlib.util, sys, inspect, os",
                $"p='{escapedPath}'",
                "script_dir = os.path.dirname(p)",
                "if script_dir and script_dir not in sys.path:",
                "    sys.path.insert(0, script_dir)",
                "spec = importlib.util.spec_from_file_location('user_script', p)",
                "mod = importlib.util.module_from_spec(spec)",
                "spec.loader.exec_module(mod)",
                $"func = getattr(mod, '{escapedFunc}', None)",
                "if func is None:",
                "    sys.exit(0)",
                "class _DummyCtx:",
                "    def Log(self, message):",
                "        # Log-Ausgaben des Scripts landen auf stderr, Host zeigt sie als Warnung an.",
                "        sys.stderr.write(str(message) + \"\\n\")",
                "    def GetSignal(self, name):",
                "        # Platzhalter: externe Runner-Variante hat noch keinen direkten Signalzugriff.",
                "        return None",
                "try:",
                "    sig = inspect.signature(func)",
                "    if len(sig.parameters) == 0:",
                "        result = func()",
                "    else:",
                "        result = func(_DummyCtx())",
                "except Exception as exc:",
                "    import traceback",
                "    traceback.print_exc()",
                "    sys.exit(1)",
                "sys.stdout.write(str(result) if result is not None else '')"
            });

            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(code);

            using var process = Process.Start(psi);
            if (process is null)
            {
                Core.LogError("Failed to start Python process. Ensure 'python' is available on PATH.");
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignorieren; wir versuchen nur haengende Prozesse zu beenden.
                }

                Core.LogWarn($"Python script '{fullPath}' did not finish within timeout.");
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Core.LogWarn($"Python script '{fullPath}' stderr: {stderr}");
            }

            if (!expectResult)
            {
                return null;
            }

            var output = stdout.Trim();
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            if (long.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            {
                return longValue;
            }

            if (double.TryParse(output, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return doubleValue;
            }

            if (bool.TryParse(output, out var boolValue))
            {
                return boolValue;
            }

            return output;
        }
        catch (Exception ex)
        {
            Core.LogError($"External Python execution failed for script '{fullPath}'.", ex);
            return null;
        }
    }

#endif

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.String => element.GetString(),
            _ => element.ToString()
        };
    }

    private static string ResolvePath(string scriptPath)
    {
        if (Path.IsPathRooted(scriptPath))
        {
            return scriptPath;
        }

        // Normalize separators so we can safely combine paths.
        scriptPath = scriptPath.Replace('/', Path.DirectorySeparatorChar);

        var baseDir = Core.OpenedDirectory ?? AppContext.BaseDirectory;
        var folderRoot = Path.Combine(baseDir, "Folder");

        // If the script path already contains directory segments (e.g. "Scripts/foo.py",
        // "Skript/foo.py" oder "../Scripts/foo.py"), interpret it as relative to the
        // Folder root so bestehende Pfade weiter funktionieren.
        if (scriptPath.Contains(Path.DirectorySeparatorChar))
        {
            return Path.GetFullPath(Path.Combine(folderRoot, scriptPath));
        }

        // Nur ein Dateiname ohne Pfadanteile: bevorzugt den neuen Standardordner
        // "Folder/Scripts" und faellt fuer bestehende Projekte auf "Folder/Skript" zurueck.
        var preferredPath = Path.Combine(folderRoot, "Scripts", scriptPath);
        var legacyPath = Path.Combine(folderRoot, "Skript", scriptPath);
        return File.Exists(preferredPath) || !File.Exists(legacyPath)
            ? preferredPath
            : legacyPath;
    }

    private static bool IsLegacyScriptCompatibleResolvedPath(string fullPath, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            return true;
        }

        try
        {
            var scriptText = File.ReadAllText(fullPath);
            if (!LooksLikePythonClientScript(scriptText))
            {
                return true;
            }

            reason = $"Legacy Python script skipped because '{Path.GetFileName(fullPath)}' looks like a PythonClient script. Use a PythonClient control instead.";
            return false;
        }
        catch (Exception ex)
        {
            Core.LogWarn($"Failed to inspect Python script '{fullPath}'. Continuing with legacy execution.", ex);
            return true;
        }
    }

    private static bool LooksLikePythonClientScript(string scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            return false;
        }

        return scriptText.Contains("from ui_python_client import", StringComparison.Ordinal)
            || scriptText.Contains("import ui_python_client", StringComparison.Ordinal)
            || scriptText.Contains("PythonClient(", StringComparison.Ordinal)
            || scriptText.Contains("client.run()", StringComparison.Ordinal)
            || scriptText.Contains("@client.function", StringComparison.Ordinal);
    }

    private static void LogLegacyIncompatibleScript(string fullPath, string reason)
    {
        try
        {
            var writeTimeUtc = File.GetLastWriteTimeUtc(fullPath);
            if (_lastWriteTimes.TryGetValue(fullPath, out var knownWriteTimeUtc) && knownWriteTimeUtc == writeTimeUtc)
            {
                return;
            }

            _lastWriteTimes[fullPath] = writeTimeUtc;
        }
        catch
        {
        }

        Core.LogWarn(reason);
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Win32Exception)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch
        {
        }
    }

    private static bool TryGetExitCode(Process process, out int exitCode)
    {
        try
        {
            exitCode = process.ExitCode;
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }

        exitCode = 0;
        return false;
    }
}