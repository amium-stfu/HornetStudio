using Amium.Item.Server.MultimasterDemo.Controllers;
using Amium.Item.Server.MultimasterDemo.Testing;

namespace Amium.Item.Server.MultimasterDemo;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--mesh-ui", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MeshMainForm());
            return 0;
        }

        if (args.Any(argument => string.Equals(argument, "--mesh-self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunMeshSelfTestAsync().ConfigureAwait(false);
        }

        if (args.Any(argument => string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunSelfTestAsync().ConfigureAwait(false);
        }

        Application.ThreadException += (_, args) => ShowFatalError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ShowFatalError(exception);
            }
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static void ShowFatalError(Exception exception)
    {
        MessageBox.Show(
            text: exception.ToString(),
            caption: "Multimaster Demo Error",
            buttons: MessageBoxButtons.OK,
            icon: MessageBoxIcon.Error);
    }

    private static async Task<int> RunSelfTestAsync()
    {
        try
        {
            await using var controller = new MultimasterDemoController();
            var runner = new MultimasterSelfTestRunner(
                controller: controller,
                options: MultimasterSelfTestOptions.CreateDefault());
            var result = await runner.RunAsync().ConfigureAwait(false);

            Console.WriteLine($"Self-test summary: {result.SummaryPath}");
            Console.WriteLine($"Self-test JSONL: {result.JsonLogPath}");

            return result.IsSuccess ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> RunMeshSelfTestAsync()
    {
        try
        {
            await using var controller = new MeshMultimasterDemoController();
            var runner = new MeshMultimasterSelfTestRunner(
                controller: controller,
                options: MultimasterSelfTestOptions.CreateMeshDefault());
            var result = await runner.RunAsync().ConfigureAwait(false);

            Console.WriteLine($"Mesh self-test summary: {result.SummaryPath}");
            Console.WriteLine($"Mesh self-test JSONL: {result.JsonLogPath}");

            return result.IsSuccess ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}