using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

namespace HornetStudio;

internal sealed class Program
{
    private const string SingleInstanceMutexName = "Local\\HornetStudio.SingleInstance";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (!TryAcquireSingleInstance())
        {
            ShowAlreadyRunningMessage();
            return;
        }

        try
        {
			App.SetStartupArguments(args);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            ReleaseSingleInstance();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static bool TryAcquireSingleInstance()
    {
#if DEBUG
        return true;
#else
        if (IsAnotherInstanceRunning())
        {
            return false;
        }

        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
            return createdNew;
        }
        catch
        {
            return true;
        }
#endif
    }

    private static bool IsAnotherInstanceRunning()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var currentProcessId = currentProcess.Id;
            var currentProcessName = currentProcess.ProcessName;
            var currentProcessPath = currentProcess.MainModule?.FileName;

            foreach (var process in Process.GetProcessesByName(currentProcessName))
            {
                using (process)
                {
                    if (process.Id == currentProcessId)
                    {
                        continue;
                    }

                    try
                    {
                        var otherProcessPath = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(currentProcessPath)
                            && !string.IsNullOrWhiteSpace(otherProcessPath)
                            && string.Equals(
                                Path.GetFullPath(otherProcessPath),
                                Path.GetFullPath(currentProcessPath),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }

    private static void ShowAlreadyRunningMessage()
    {
        TryActivateExistingWindow();
    }

    private static void TryActivateExistingWindow()
    {
        const int restoreWindow = 9;
        var windowHandle = FindWindowW(null, "HornetStudio");
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(windowHandle, restoreWindow);
        SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}