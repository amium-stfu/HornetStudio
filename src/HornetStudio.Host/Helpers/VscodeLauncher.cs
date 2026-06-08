using System;
using System.Diagnostics;
using System.IO;
using HornetStudio.Host;
using System.Text.Json;
using System.Collections.Generic;

public static class VsCodeLauncher
{
    private static readonly string[] ProjectEntryFiles = ["project.aaep", "project.udlb", "Book.udlb"];

    public static bool OpenFolder(string folderPath, bool newWindow = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Ordner nicht gefunden: {folderPath}");

        string targetRootDirectory = Path.GetFullPath(folderPath);
        if (TryFindProjectRootDirectory(targetRootDirectory) is { } projectRootDirectory)
        {
            targetRootDirectory = projectRootDirectory;

            try
            {
                CreateVsCodeSettings(targetRootDirectory);
            }
            catch (Exception ex)
            {
                Core.LogWarn($"CreateVsCodeSettings failed for {targetRootDirectory}", ex);
            }

            try
            {
                Core.InitPipeCom(targetRootDirectory);
            }
            catch (Exception ex)
            {
                Core.LogWarn($"InitPipeCom failed for {targetRootDirectory}. Continuing with VS Code launch.", ex);
            }
        }

        string windowArg = newWindow ? "--new-window" : "--reuse-window";
        string? codeExe = TryFindVsCodeExe();

        if (!string.IsNullOrWhiteSpace(codeExe))
        {
            try
            {
                var psiExe = new ProcessStartInfo
                {
                    FileName = codeExe,
                    Arguments = $"{windowArg} \"{targetRootDirectory}\"",
                    WorkingDirectory = targetRootDirectory,
                    UseShellExecute = true
                };

                if (Process.Start(psiExe) is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Core.LogWarn($"VS Code executable launch failed ({codeExe}). Trying CLI fallback.", ex);
            }
        }

        try
        {
            var psiCli = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c code {windowArg} \"{targetRootDirectory}\"",
                WorkingDirectory = targetRootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process? p = Process.Start(psiCli);
            if (p != null)
            {
                p.WaitForExit(2000);
                if (p.ExitCode == 0)
                {
                    return true;
                }

                string stdOut = p.StandardOutput.ReadToEnd();
                string stdErr = p.StandardError.ReadToEnd();
                Core.LogWarn($"VS Code CLI launch failed for {targetRootDirectory}. ExitCode={p.ExitCode}. StdOut='{stdOut}'. StdErr='{stdErr}'.");
            }
        }
        catch (Exception ex)
        {
            Core.LogWarn($"VS Code CLI fallback failed for {targetRootDirectory}", ex);
        }

        return false;
    }

    /// <summary>
    /// Opens a Python environment folder in VS Code without attempting to
    /// locate an HornetStudio project root or initialize host-specific settings.
    ///
    /// Intended usage: open an Env root such as "<Project>/Python/ModbusClient".
    /// </summary>
    public static bool OpenPythonEnvironmentFolder(string folderPath, bool newWindow = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Ordner nicht gefunden: {folderPath}");

        string targetRootDirectory = Path.GetFullPath(folderPath);

        string windowArg = newWindow ? "--new-window" : "--reuse-window";
        string? codeExe = TryFindVsCodeExe();

        if (!string.IsNullOrWhiteSpace(codeExe))
        {
            try
            {
                var psiExe = new ProcessStartInfo
                {
                    FileName = codeExe,
                    Arguments = $"{windowArg} \"{targetRootDirectory}\"",
                    WorkingDirectory = targetRootDirectory,
                    UseShellExecute = true
                };

                if (Process.Start(psiExe) is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Core.LogWarn($"VS Code executable launch failed ({codeExe}) for Python env.", ex);
            }
        }

        try
        {
            var psiCli = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c code {windowArg} \"{targetRootDirectory}\"",
                WorkingDirectory = targetRootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process? p = Process.Start(psiCli);
            if (p != null)
            {
                p.WaitForExit(2000);
                if (p.ExitCode == 0)
                {
                    return true;
                }

                string stdOut = p.StandardOutput.ReadToEnd();
                string stdErr = p.StandardError.ReadToEnd();
                Core.LogWarn($"VS Code CLI launch failed for Python env {targetRootDirectory}. ExitCode={p.ExitCode}. StdOut='{stdOut}'. StdErr='{stdErr}'.");
            }
        }
        catch (Exception ex)
        {
            Core.LogWarn($"VS Code CLI fallback failed for Python env {targetRootDirectory}", ex);
        }

        return false;
    }

    public static bool OpenFile(string filePath, bool newWindow = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is required", nameof(filePath));

        var fullFilePath = Path.GetFullPath(filePath);
        if (!File.Exists(fullFilePath))
            throw new FileNotFoundException($"Datei nicht gefunden: {fullFilePath}", fullFilePath);

        var directory = Path.GetDirectoryName(fullFilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Ordner nicht gefunden: {directory}");

        string targetRootDirectory = Path.GetFullPath(directory);
        var isPythonFile = string.Equals(Path.GetExtension(fullFilePath), ".py", StringComparison.OrdinalIgnoreCase);
        if (!isPythonFile && TryFindProjectRootDirectory(targetRootDirectory) is { } projectRootDirectory)
        {
            targetRootDirectory = projectRootDirectory;

            try
            {
                CreateVsCodeSettings(targetRootDirectory);
            }
            catch (Exception ex)
            {
                Core.LogWarn($"CreateVsCodeSettings failed for {targetRootDirectory}", ex);
            }

            try
            {
                Core.InitPipeCom(targetRootDirectory);
            }
            catch (Exception ex)
            {
                Core.LogWarn($"InitPipeCom failed for {targetRootDirectory}. Continuing with VS Code launch.", ex);
            }
        }

        string windowArg = newWindow ? "--new-window" : "--reuse-window";
        string? codeExe = TryFindVsCodeExe();

        if (!string.IsNullOrWhiteSpace(codeExe))
        {
            try
            {
                var psiExe = new ProcessStartInfo
                {
                    FileName = codeExe,
                    Arguments = $"{windowArg} \"{targetRootDirectory}\" \"{fullFilePath}\"",
                    WorkingDirectory = targetRootDirectory,
                    UseShellExecute = true
                };

                if (Process.Start(psiExe) is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Core.LogWarn($"VS Code executable launch failed ({codeExe}). Trying CLI fallback.", ex);
            }
        }

        try
        {
            var psiCli = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c code {windowArg} \"{targetRootDirectory}\" \"{fullFilePath}\"",
                WorkingDirectory = targetRootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process? p = Process.Start(psiCli);
            if (p != null)
            {
                p.WaitForExit(2000);
                if (p.ExitCode == 0)
                {
                    return true;
                }

                string stdOut = p.StandardOutput.ReadToEnd();
                string stdErr = p.StandardError.ReadToEnd();
                Core.LogWarn($"VS Code CLI launch failed for {targetRootDirectory} file='{fullFilePath}'. ExitCode={p.ExitCode}. StdOut='{stdOut}'. StdErr='{stdErr}'.");
            }
        }
        catch (Exception ex)
        {
            Core.LogWarn($"VS Code CLI fallback failed for {targetRootDirectory} file='{fullFilePath}'", ex);
        }

        return false;
    }

    private static string? TryFindVsCodeExe()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userInstall = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string machineInstall = Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string machineInstallX86 = Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe");

        if (File.Exists(userInstall)) return userInstall;
        if (File.Exists(machineInstall)) return machineInstall;
        if (File.Exists(machineInstallX86)) return machineInstallX86;

        return null;
    }

    private static string? TryFindProjectRootDirectory(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
        {
            return null;
        }

        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (ProjectEntryFiles.Any(fileName => File.Exists(Path.Combine(current.FullName, fileName))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void CreateVsCodeSettings(string folderPath)
    {
        string vscodeDir = Path.Combine(folderPath, ".vscode");

        var launchData = new Dictionary<string, object>
        {
            { "version", "0.2.0" },
            { "configurations", new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "name", "qbook: Attach to host process" },
                        { "type", "coreclr" },
                        { "request", "attach" },
                        { "processId", Process.GetCurrentProcess().Id },
                        { "justMyCode", false },
                        { "requireExactSource", false },
                        { "suppressJITOptimizations", true },
                        { "logging", new Dictionary<string, object>
                            {
                                { "moduleLoad", true },
                                { "exceptions", true },
                                { "programOutput", true }
                            }
                        },
                        
                        { "symbolOptions", new Dictionary<string, object>
                            {
                                { "searchPaths", new[] { folderPath, AppContext.BaseDirectory } },
                                { "searchMicrosoftSymbolServer", false }
                            }
                        }
                    }
                }
            }
        };

        string launchJson = JsonSerializer.Serialize(launchData, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        if (!Directory.Exists(vscodeDir))
        {
            Directory.CreateDirectory(vscodeDir);
        }

        File.WriteAllText(Path.Combine(vscodeDir, "launch.json"), launchJson);

    }

}
