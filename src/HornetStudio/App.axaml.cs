using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Host;
using HornetStudio.Logging;
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace HornetStudio;

public partial class App : Application
{
	private static int _formatExceptionLogCount;
	private static int _globalExceptionHandlersRegistered;
	private static string[] _startupArgs = Array.Empty<string>();

	public static void SetStartupArguments(string[] args)
	{
		_startupArgs = args ?? Array.Empty<string>();
	}

	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		HostLogger.Initialize("HornetStudio");
		UiResponsivenessDiagnostics.InitializeFromEnvironment(_startupArgs);
		HostLogger.Log.Information("HornetStudio startup. BaseDirectory={BaseDirectory}", AppContext.BaseDirectory);
		RegisterGlobalExceptionHandlers();

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
			UiResponsivenessDiagnostics.StartProbe();

			desktop.Exit += (_, _) =>
			{
				try
				{
					UiResponsivenessDiagnostics.StopProbe();
					HostShutdownManager.ShutdownApplication("Application exit");
				}
				catch
				{
				}
				finally
				{
					HostLogger.Shutdown();
					// Harte Sicherheitsleine, falls noch Foreground-Threads leben.
					Environment.Exit(0);
				}
			};

			var splashScreen = new SplashScreenWindow();
			splashScreen.UpdateProgress(15);
			splashScreen.Show();

			Dispatcher.UIThread.Post(() => CompleteDesktopStartup(desktop, splashScreen));
		}

		base.OnFrameworkInitializationCompleted();
	}

	private static void CompleteDesktopStartup(IClassicDesktopStyleApplicationLifetime desktop, SplashScreenWindow splashScreen)
	{
		try
		{
			splashScreen.UpdateProgress(65);
			var viewModel = new ViewModels.MainWindowViewModel(_startupArgs);
			splashScreen.DataContext = viewModel;

			var mainWindow = new MainWindow
			{
				DataContext = viewModel
			};

			desktop.MainWindow = mainWindow;
			splashScreen.UpdateProgress(100);
			mainWindow.Show();
			mainWindow.Activate();
			splashScreen.Close();
		}
		catch (Exception exception)
		{
			HostLogger.Log.Fatal(exception, "Failed to complete desktop startup");
			splashScreen.Close();
			throw;
		}
	}

	private static void RegisterGlobalExceptionHandlers()
	{
		if (Interlocked.Exchange(ref _globalExceptionHandlersRegistered, 1) != 0)
		{
			return;
		}

		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

		HostLogger.Log.Information("Global exception handlers registered");
	}

	private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
	{
		HostLogger.Log.Fatal(e.Exception, "Unhandled UI thread exception");
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		HostLogger.Log.Error(e.Exception, "Unobserved task exception");
	}

	private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception exception)
		{
			HostLogger.Log.Fatal(exception, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", e.IsTerminating);
			return;
		}

		HostLogger.Log.Fatal("Unhandled AppDomain exception (non-Exception payload). IsTerminating={IsTerminating} Payload={Payload}", e.IsTerminating, e.ExceptionObject);
	}

	private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
	{
		if (e.Exception is not FormatException formatException)
		{
			return;
		}

		var stackTrace = formatException.StackTrace ?? string.Empty;
		if (!stackTrace.Contains("Avalonia", StringComparison.OrdinalIgnoreCase)
			&& !stackTrace.Contains("HornetStudio.Editor", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var count = Interlocked.Increment(ref _formatExceptionLogCount);
		if (count > 20)
		{
			if (count == 21)
			{
				HostLogger.Log.Warning("Further first-chance FormatException logs suppressed after {Count} entries", count - 1);
			}

			return;
		}

		HostLogger.Log.Warning(formatException, "First-chance FormatException #{Count}: {Message}", count, formatException.Message);
	}
}
