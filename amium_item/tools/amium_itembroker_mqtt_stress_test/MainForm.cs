using System.Globalization;

namespace Amium.Item.Server.MqttStressTest;

internal sealed partial class MainForm : Form
{
    private readonly StressTestController _controller = new();
    private readonly System.Windows.Forms.Timer _metricsTimer = new();

    public MainForm()
    {
        InitializeComponent();
        signalCountNumericUpDown.ValueChanged += LoadSettings_ValueChanged;
        messageRateNumericUpDown.ValueChanged += LoadSettings_ValueChanged;
        _controller.LogMessage += HandleLogMessage;
        _controller.RunningChanged += HandleRunningChanged;

        _metricsTimer.Interval = 500;
        _metricsTimer.Tick += MetricsTimer_Tick;
        _metricsTimer.Start();

        UpdateAverageUpdatesPerValue();
        RenderSnapshot(_controller.CreateSnapshot());
        UpdateRunButtons(isRunning: false);
        AppendLog("Ready.");
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        _metricsTimer.Stop();
        startButton.Enabled = false;
        stopButton.Enabled = false;
        resetButton.Enabled = false;

        try
        {
            await _controller.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog($"Shutdown warning: {ex.Message}");
        }

        base.OnFormClosing(e);
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        StressTestRunSettings settings;
        try
        {
            settings = ReadSettings();
        }
        catch (Exception ex)
        {
            AppendLog($"Invalid settings: {ex.Message}");
            return;
        }

        UpdateRunButtons(isRunning: true);
        try
        {
            await _controller.StartAsync(settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog($"Start failed: {ex.Message}");
            UpdateRunButtons(isRunning: false);
        }
    }

    private async void StopButton_Click(object? sender, EventArgs e)
    {
        stopButton.Enabled = false;
        try
        {
            await _controller.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog($"Stop failed: {ex.Message}");
        }
    }

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _controller.Reset();
            RenderSnapshot(_controller.CreateSnapshot());
        }
        catch (Exception ex)
        {
            AppendLog($"Reset failed: {ex.Message}");
        }
    }

    private StressTestRunSettings ReadSettings()
        => StressTestRunSettings.Create(
            host: hostTextBox.Text,
            port: (int)portNumericUpDown.Value,
            baseTopic: baseTopicTextBox.Text,
            stressRootPath: stressRootTextBox.Text,
            signalCount: (int)signalCountNumericUpDown.Value,
            messagesPerSecond: (int)messageRateNumericUpDown.Value,
            durationSeconds: (int)durationNumericUpDown.Value,
            retained: retainedCheckBox.Checked);

    private void MetricsTimer_Tick(object? sender, EventArgs e)
        => RenderSnapshot(_controller.CreateSnapshot());

    private void LoadSettings_ValueChanged(object? sender, EventArgs e)
        => UpdateAverageUpdatesPerValue();

    private void HandleLogMessage(object? sender, string message)
        => RunOnUiThread(() => AppendLog(message));

    private void HandleRunningChanged(object? sender, bool isRunning)
        => RunOnUiThread(() => UpdateRunButtons(isRunning));

    private void UpdateRunButtons(bool isRunning)
    {
        startButton.Enabled = !isRunning;
        stopButton.Enabled = isRunning;
        resetButton.Enabled = !isRunning;
        statusValueLabel.Text = isRunning ? "Running" : "Stopped";
    }

    private void RenderSnapshot(StressTestMetricsSnapshot snapshot)
    {
        publishedValueLabel.Text = FormatInteger(snapshot.Published);
        receivedValueLabel.Text = FormatInteger(snapshot.Received);
        pendingValueLabel.Text = FormatInteger(snapshot.Pending);
        maxPendingValueLabel.Text = FormatInteger(snapshot.MaxPending);
        duplicatesValueLabel.Text = FormatInteger(snapshot.Duplicates);
        outOfOrderValueLabel.Text = FormatInteger(snapshot.OutOfOrder);
        publishRateValueLabel.Text = FormatRate(snapshot.PublishRatePerSecond);
        receiveRateValueLabel.Text = FormatRate(snapshot.ReceiveRatePerSecond);
        averageLatencyValueLabel.Text = FormatMilliseconds(snapshot.AverageLatencyMilliseconds);
        maxLatencyValueLabel.Text = FormatMilliseconds(snapshot.MaxLatencyMilliseconds);
        p95LatencyValueLabel.Text = FormatMilliseconds(snapshot.P95LatencyMilliseconds);
        p99LatencyValueLabel.Text = FormatMilliseconds(snapshot.P99LatencyMilliseconds);
        elapsedValueLabel.Text = snapshot.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void UpdateAverageUpdatesPerValue()
    {
        var signalCount = decimal.ToDouble(signalCountNumericUpDown.Value);
        var totalUpdatesPerSecond = decimal.ToDouble(messageRateNumericUpDown.Value);
        var averageUpdatesPerValue = signalCount <= 0 ? 0 : totalUpdatesPerSecond / signalCount;
        averageUpdatesPerValueValueLabel.Text = FormatRate(averageUpdatesPerValue);
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        logTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
    }

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

    private static string FormatInteger(long value)
        => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatRate(double value)
        => value.ToString("N1", CultureInfo.InvariantCulture) + "/s";

    private static string FormatMilliseconds(double value)
        => value.ToString("N2", CultureInfo.InvariantCulture) + " ms";
}
