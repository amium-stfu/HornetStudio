using Amium.Item.Server;

using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;

    if (!cancellationSource.IsCancellationRequested)
    {
        cancellationSource.Cancel();
    }
};

await ItemServerServiceHost.RunAsync(cancellationSource.Token);