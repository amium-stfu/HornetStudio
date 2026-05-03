using Amium.ItemBroker.Mqtt;
using Amium.ItemBroker.Mqtt.Client;
using Amium.ItemBroker.MqttDemoWinForms.Controllers;
using MqttTopicMapper = Amium.ItemBroker.Mqtt.MqttItemTopicMapper;

namespace Amium.ItemBroker.MqttDemoWinForms;

internal sealed partial class MainForm : Form
{
    private readonly ServiceHostController _serviceHostController;
    private readonly DemoPublisherController _demoPublisherController;
    private readonly WritableDemoController _writableDemoController;

    internal MainForm()
    {
        InitializeComponent();

        _serviceHostController = new ServiceHostController(
            new MqttItemBrokerOptions
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = 1883,
                BaseTopic = "hornet",
                SubscriptionRootPath = "Demo",
                PublishHealth = true,
            });
        _demoPublisherController = new DemoPublisherController(
            new MqttItemBrokerClientOptions
            {
                Host = "127.0.0.1",
                Port = 1883,
                ClientId = "DemoPublisher",
                BaseTopic = "hornet",
            });
        _writableDemoController = new WritableDemoController(clientId: "WritableOwner");

        _serviceHostController.LogMessage += HandleLogMessage;
        _demoPublisherController.LogMessage += HandleLogMessage;
        _writableDemoController.LogMessage += HandleLogMessage;
        _demoPublisherController.StateChanged += HandlePublisherStateChanged;
        _writableDemoController.StateChanged += HandleWritableStateChanged;

        serviceEndpointValueLabel.Text = _serviceHostController.EndpointSummary;
        writablePathValueLabel.Text = GetTopicForItemPath(_writableDemoController.ItemPath);
        temperatureTopicValueLabel.Text = GetTopicForItemPath(_demoPublisherController.TemperaturePath);
        pressureTopicValueLabel.Text = GetTopicForItemPath(_demoPublisherController.PressurePath);
        UpdateServiceState();
        AppendLog("Ready. Start the local service host to begin the demo.");
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        startButton.Enabled = false;
        stopButton.Enabled = false;

        try
        {
            await StopDemoAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog($"Shutdown warning: {ex.Message}");
        }

        await _demoPublisherController.DisposeAsync().ConfigureAwait(true);
        await _writableDemoController.DisposeAsync().ConfigureAwait(true);
        await _serviceHostController.DisposeAsync().ConfigureAwait(true);

        base.OnFormClosing(e);
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (_serviceHostController.IsRunning)
        {
            AppendLog("The demo is already running.");
            return;
        }

        startButton.Enabled = false;
        stopButton.Enabled = false;

        try
        {
            await _serviceHostController.StartAsync().ConfigureAwait(true);
            await _writableDemoController.StartAsync(_serviceHostController.Broker!).ConfigureAwait(true);
            await _demoPublisherController.StartAsync().ConfigureAwait(true);
            UpdateServiceState();
        }
        catch (Exception ex)
        {
            AppendLog($"Start failed: {ex.Message}");
            await StopDemoAsync().ConfigureAwait(true);
            UpdateServiceState();
        }
        finally
        {
            startButton.Enabled = !_serviceHostController.IsRunning;
            stopButton.Enabled = _serviceHostController.IsRunning;
        }
    }

    private async void StopButton_Click(object? sender, EventArgs e)
    {
        startButton.Enabled = false;
        stopButton.Enabled = false;

        try
        {
            await StopDemoAsync().ConfigureAwait(true);
            UpdateServiceState();
        }
        catch (Exception ex)
        {
            AppendLog($"Stop failed: {ex.Message}");
        }
        finally
        {
            startButton.Enabled = !_serviceHostController.IsRunning;
            stopButton.Enabled = _serviceHostController.IsRunning;
        }
    }

    private async Task StopDemoAsync()
    {
        await _demoPublisherController.StopAsync().ConfigureAwait(true);
        await _writableDemoController.StopAsync().ConfigureAwait(true);
        await _serviceHostController.StopAsync().ConfigureAwait(true);
    }

    private void HandlePublisherStateChanged(object? sender, DemoPublisherState state)
    {
        RunOnUiThread(() =>
        {
            temperatureValueLabel.Text = $"{state.Temperature:0.0} degC";
            pressureValueLabel.Text = $"{state.Pressure:0.0} hPa";
            publisherUpdatedValueLabel.Text = state.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        });
    }

    private void HandleWritableStateChanged(object? sender, WritableDemoState state)
    {
        RunOnUiThread(() =>
        {
            writableValueLabel.Text = $"{state.Value:0.0} %";
            writableSourceValueLabel.Text = state.LastWriteSource;
            writableUpdatedValueLabel.Text = state.LastWriteUtc.ToLocalTime().ToString("HH:mm:ss");
        });
    }

    private void HandleLogMessage(object? sender, string message)
        => RunOnUiThread(() => AppendLog(message));

    private void UpdateServiceState()
    {
        serviceStatusValueLabel.Text = _serviceHostController.IsRunning ? "Running" : "Stopped";
        startButton.Enabled = !_serviceHostController.IsRunning;
        stopButton.Enabled = _serviceHostController.IsRunning;
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        logTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
    }

    private string GetTopicForItemPath(string path)
        => new MqttTopicMapper(baseTopic: "hornet").ToTopic(path, parameterName: "Value");

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }
}
