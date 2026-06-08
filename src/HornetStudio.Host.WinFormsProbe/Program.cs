using Amium.Items;
using HornetStudio.Host.Registries;

namespace HornetStudio.Host.WinFormsProbe;

internal static class Program
{
    private static System.Threading.Timer? _valueUpdateTimer;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var registry = new HostItemRegistry();
        var counterItem = CreateItem("runtime.probe.counter", 0, "Integer value updated by a background timer.");
        var clockItem = CreateItem("runtime.probe.clock", DateTime.Now.ToString("O"), "Current local timestamp.");
        var statusItem = CreateItem("runtime.probe.status", "Ready", "Writable status text.");
        var noteItem = CreateItem("runtime.probe.note", "Writes update Item.Value directly.", "Current HostItemRegistry semantics.");

        registry.Register(counterItem);
        registry.Register(clockItem);
        registry.Register(statusItem);
        registry.Register(noteItem);

        _valueUpdateTimer = new System.Threading.Timer(
            _ =>
            {
                counterItem.Value = counterItem.Value is int currentCounter
                    ? currentCounter + 1
                    : 1;
                clockItem.Value = DateTime.Now.ToString("O");
            },
            null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromSeconds(1));

        Application.ApplicationExit += (_, _) => _valueUpdateTimer?.Dispose();

        Application.Run(new MainForm(registry));
    }

    private static Item CreateItem(string path, object? value, string description)
    {
        var item = ItemExtension.CreateWithPath(path, value);
        item.Properties["text"].Value = description;
        item.Properties["type"].Value = value?.GetType().Name ?? "null";
        return item;
    }
}
