#nullable enable
namespace Amium.Item.Server.MqttStressTest;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private GroupBox connectionGroupBox = null!;
    private GroupBox loadGroupBox = null!;
    private GroupBox metricsGroupBox = null!;
    private GroupBox logGroupBox = null!;
    private TextBox hostTextBox = null!;
    private NumericUpDown portNumericUpDown = null!;
    private TextBox baseTopicTextBox = null!;
    private TextBox stressRootTextBox = null!;
    private NumericUpDown signalCountNumericUpDown = null!;
    private NumericUpDown messageRateNumericUpDown = null!;
    private NumericUpDown durationNumericUpDown = null!;
    private CheckBox retainedCheckBox = null!;
    private Button startButton = null!;
    private Button stopButton = null!;
    private Button resetButton = null!;
    private Label averageUpdatesPerValueValueLabel = null!;
    private Label statusValueLabel = null!;
    private Label publishedValueLabel = null!;
    private Label receivedValueLabel = null!;
    private Label pendingValueLabel = null!;
    private Label maxPendingValueLabel = null!;
    private Label duplicatesValueLabel = null!;
    private Label outOfOrderValueLabel = null!;
    private Label publishRateValueLabel = null!;
    private Label receiveRateValueLabel = null!;
    private Label averageLatencyValueLabel = null!;
    private Label maxLatencyValueLabel = null!;
    private Label p95LatencyValueLabel = null!;
    private Label p99LatencyValueLabel = null!;
    private Label elapsedValueLabel = null!;
    private TextBox logTextBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _metricsTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        connectionGroupBox = new GroupBox();
        loadGroupBox = new GroupBox();
        metricsGroupBox = new GroupBox();
        logGroupBox = new GroupBox();
        hostTextBox = new TextBox();
        portNumericUpDown = new NumericUpDown();
        baseTopicTextBox = new TextBox();
        stressRootTextBox = new TextBox();
        signalCountNumericUpDown = new NumericUpDown();
        messageRateNumericUpDown = new NumericUpDown();
        durationNumericUpDown = new NumericUpDown();
        retainedCheckBox = new CheckBox();
        startButton = new Button();
        stopButton = new Button();
        resetButton = new Button();
        averageUpdatesPerValueValueLabel = new Label();
        statusValueLabel = new Label();
        publishedValueLabel = new Label();
        receivedValueLabel = new Label();
        pendingValueLabel = new Label();
        maxPendingValueLabel = new Label();
        duplicatesValueLabel = new Label();
        outOfOrderValueLabel = new Label();
        publishRateValueLabel = new Label();
        receiveRateValueLabel = new Label();
        averageLatencyValueLabel = new Label();
        maxLatencyValueLabel = new Label();
        p95LatencyValueLabel = new Label();
        p99LatencyValueLabel = new Label();
        elapsedValueLabel = new Label();
        logTextBox = new TextBox();
        ((System.ComponentModel.ISupportInitialize)portNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)signalCountNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)messageRateNumericUpDown).BeginInit();
        ((System.ComponentModel.ISupportInitialize)durationNumericUpDown).BeginInit();
        SuspendLayout();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(245, 247, 250);
        ClientSize = new Size(1120, 760);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Amium ItemBroker MQTT Stress Test";

        connectionGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        connectionGroupBox.Location = new Point(20, 18);
        connectionGroupBox.Name = "connectionGroupBox";
        connectionGroupBox.Size = new Size(1080, 116);
        connectionGroupBox.TabIndex = 0;
        connectionGroupBox.TabStop = false;
        connectionGroupBox.Text = "Connection";

        AddLabel(connectionGroupBox, text: "Host", x: 20, y: 31);
        hostTextBox.Location = new Point(118, 28);
        hostTextBox.Name = "hostTextBox";
        hostTextBox.Size = new Size(180, 23);
        hostTextBox.TabIndex = 0;
        hostTextBox.Text = "127.0.0.1";

        AddLabel(connectionGroupBox, text: "Port", x: 326, y: 31);
        portNumericUpDown.Location = new Point(384, 28);
        portNumericUpDown.Maximum = 65535;
        portNumericUpDown.Minimum = 1;
        portNumericUpDown.Name = "portNumericUpDown";
        portNumericUpDown.Size = new Size(88, 23);
        portNumericUpDown.TabIndex = 1;
        portNumericUpDown.Value = 1883;

        AddLabel(connectionGroupBox, text: "Base topic", x: 500, y: 31);
        baseTopicTextBox.Location = new Point(594, 28);
        baseTopicTextBox.Name = "baseTopicTextBox";
        baseTopicTextBox.Size = new Size(160, 23);
        baseTopicTextBox.TabIndex = 2;
        baseTopicTextBox.Text = "hornet";

        AddLabel(connectionGroupBox, text: "Stress root", x: 20, y: 72);
        stressRootTextBox.Location = new Point(118, 69);
        stressRootTextBox.Name = "stressRootTextBox";
        stressRootTextBox.Size = new Size(180, 23);
        stressRootTextBox.TabIndex = 3;
        stressRootTextBox.Text = "stress";

        startButton.BackColor = Color.FromArgb(34, 116, 165);
        startButton.FlatStyle = FlatStyle.Flat;
        startButton.ForeColor = Color.White;
        startButton.Location = new Point(796, 24);
        startButton.Name = "startButton";
        startButton.Size = new Size(84, 32);
        startButton.TabIndex = 4;
        startButton.Text = "Start";
        startButton.UseVisualStyleBackColor = false;
        startButton.Click += StartButton_Click;

        stopButton.BackColor = Color.FromArgb(110, 118, 129);
        stopButton.FlatStyle = FlatStyle.Flat;
        stopButton.ForeColor = Color.White;
        stopButton.Location = new Point(890, 24);
        stopButton.Name = "stopButton";
        stopButton.Size = new Size(84, 32);
        stopButton.TabIndex = 5;
        stopButton.Text = "Stop";
        stopButton.UseVisualStyleBackColor = false;
        stopButton.Click += StopButton_Click;

        resetButton.BackColor = Color.FromArgb(88, 100, 112);
        resetButton.FlatStyle = FlatStyle.Flat;
        resetButton.ForeColor = Color.White;
        resetButton.Location = new Point(984, 24);
        resetButton.Name = "resetButton";
        resetButton.Size = new Size(76, 32);
        resetButton.TabIndex = 6;
        resetButton.Text = "Reset";
        resetButton.UseVisualStyleBackColor = false;
        resetButton.Click += ResetButton_Click;

        AddLabel(connectionGroupBox, text: "Status", x: 796, y: 74);
        statusValueLabel.AutoSize = true;
        statusValueLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        statusValueLabel.Location = new Point(856, 74);
        statusValueLabel.Name = "statusValueLabel";
        statusValueLabel.Size = new Size(50, 15);
        statusValueLabel.TabIndex = 7;
        statusValueLabel.Text = "Stopped";

        connectionGroupBox.Controls.Add(hostTextBox);
        connectionGroupBox.Controls.Add(portNumericUpDown);
        connectionGroupBox.Controls.Add(baseTopicTextBox);
        connectionGroupBox.Controls.Add(stressRootTextBox);
        connectionGroupBox.Controls.Add(startButton);
        connectionGroupBox.Controls.Add(stopButton);
        connectionGroupBox.Controls.Add(resetButton);
        connectionGroupBox.Controls.Add(statusValueLabel);

        loadGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        loadGroupBox.Location = new Point(20, 148);
        loadGroupBox.Name = "loadGroupBox";
        loadGroupBox.Size = new Size(1080, 122);
        loadGroupBox.TabIndex = 1;
        loadGroupBox.TabStop = false;
        loadGroupBox.Text = "Load";

        AddLabel(loadGroupBox, text: "Signal values", x: 20, y: 38);
        signalCountNumericUpDown.Location = new Point(118, 35);
        signalCountNumericUpDown.Maximum = 1000000;
        signalCountNumericUpDown.Minimum = 1;
        signalCountNumericUpDown.Name = "signalCountNumericUpDown";
        signalCountNumericUpDown.Size = new Size(100, 23);
        signalCountNumericUpDown.TabIndex = 0;
        signalCountNumericUpDown.Value = 100;

        AddLabel(loadGroupBox, text: "Total updates/s", x: 250, y: 38);
        messageRateNumericUpDown.Location = new Point(344, 35);
        messageRateNumericUpDown.Maximum = 1000000;
        messageRateNumericUpDown.Minimum = 1;
        messageRateNumericUpDown.Name = "messageRateNumericUpDown";
        messageRateNumericUpDown.Size = new Size(100, 23);
        messageRateNumericUpDown.TabIndex = 1;
        messageRateNumericUpDown.Value = 100;

        AddLabel(loadGroupBox, text: "Duration s", x: 476, y: 38);
        durationNumericUpDown.Location = new Point(570, 35);
        durationNumericUpDown.Maximum = 86400;
        durationNumericUpDown.Minimum = 1;
        durationNumericUpDown.Name = "durationNumericUpDown";
        durationNumericUpDown.Size = new Size(100, 23);
        durationNumericUpDown.TabIndex = 2;
        durationNumericUpDown.Value = 60;

        retainedCheckBox.AutoSize = true;
        retainedCheckBox.Location = new Point(704, 37);
        retainedCheckBox.Name = "retainedCheckBox";
        retainedCheckBox.Size = new Size(71, 19);
        retainedCheckBox.TabIndex = 3;
        retainedCheckBox.Text = "Retained";
        retainedCheckBox.UseVisualStyleBackColor = true;

        AddLabel(loadGroupBox, text: "Avg updates/s per value", x: 20, y: 79);
        averageUpdatesPerValueValueLabel.AutoSize = true;
        averageUpdatesPerValueValueLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
        averageUpdatesPerValueValueLabel.Location = new Point(190, 76);
        averageUpdatesPerValueValueLabel.Name = "averageUpdatesPerValueValueLabel";
        averageUpdatesPerValueValueLabel.Size = new Size(39, 19);
        averageUpdatesPerValueValueLabel.TabIndex = 4;
        averageUpdatesPerValueValueLabel.Text = "0.0/s";

        loadGroupBox.Controls.Add(signalCountNumericUpDown);
        loadGroupBox.Controls.Add(messageRateNumericUpDown);
        loadGroupBox.Controls.Add(durationNumericUpDown);
        loadGroupBox.Controls.Add(retainedCheckBox);
        loadGroupBox.Controls.Add(averageUpdatesPerValueValueLabel);

        metricsGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        metricsGroupBox.Location = new Point(20, 284);
        metricsGroupBox.Name = "metricsGroupBox";
        metricsGroupBox.Size = new Size(1080, 218);
        metricsGroupBox.TabIndex = 2;
        metricsGroupBox.TabStop = false;
        metricsGroupBox.Text = "Metrics";

        AddMetric(metricsGroupBox, "Published", publishedValueLabel, x: 20, y: 32);
        AddMetric(metricsGroupBox, "Received", receivedValueLabel, x: 20, y: 68);
        AddMetric(metricsGroupBox, "Pending", pendingValueLabel, x: 20, y: 104);
        AddMetric(metricsGroupBox, "Max pending", maxPendingValueLabel, x: 20, y: 140);
        AddMetric(metricsGroupBox, "Duplicates", duplicatesValueLabel, x: 374, y: 32);
        AddMetric(metricsGroupBox, "Out of order", outOfOrderValueLabel, x: 374, y: 68);
        AddMetric(metricsGroupBox, "Publish rate", publishRateValueLabel, x: 374, y: 104);
        AddMetric(metricsGroupBox, "Receive rate", receiveRateValueLabel, x: 374, y: 140);
        AddMetric(metricsGroupBox, "Avg latency", averageLatencyValueLabel, x: 728, y: 32);
        AddMetric(metricsGroupBox, "Max latency", maxLatencyValueLabel, x: 728, y: 68);
        AddMetric(metricsGroupBox, "P95 latency", p95LatencyValueLabel, x: 728, y: 104);
        AddMetric(metricsGroupBox, "P99 latency", p99LatencyValueLabel, x: 728, y: 140);
        AddMetric(metricsGroupBox, "Elapsed", elapsedValueLabel, x: 728, y: 176);

        logGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
    logGroupBox.Location = new Point(20, 516);
        logGroupBox.Name = "logGroupBox";
    logGroupBox.Size = new Size(1080, 222);
        logGroupBox.TabIndex = 3;
        logGroupBox.TabStop = false;
        logGroupBox.Text = "Log";

        logTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logTextBox.BackColor = Color.FromArgb(18, 26, 33);
        logTextBox.BorderStyle = BorderStyle.FixedSingle;
        logTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        logTextBox.ForeColor = Color.FromArgb(227, 232, 237);
        logTextBox.Location = new Point(20, 28);
        logTextBox.Multiline = true;
        logTextBox.Name = "logTextBox";
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.Size = new Size(1040, 174);
        logTextBox.TabIndex = 0;
        logGroupBox.Controls.Add(logTextBox);

        Controls.Add(connectionGroupBox);
        Controls.Add(loadGroupBox);
        Controls.Add(metricsGroupBox);
        Controls.Add(logGroupBox);
        ((System.ComponentModel.ISupportInitialize)portNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)signalCountNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)messageRateNumericUpDown).EndInit();
        ((System.ComponentModel.ISupportInitialize)durationNumericUpDown).EndInit();
        ResumeLayout(false);
    }

    private static void AddLabel(Control parent, string text, int x, int y)
    {
        var label = new Label
        {
            AutoSize = true,
            Location = new Point(x, y),
            Text = text,
        };
        parent.Controls.Add(label);
    }

    private static void AddMetric(Control parent, string title, Label valueLabel, int x, int y)
    {
        var titleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(x, y + 4),
            Size = new Size(100, 15),
            Text = title,
        };
        valueLabel.AutoSize = true;
        valueLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
        valueLabel.Location = new Point(x + 130, y);
        valueLabel.Name = title.Replace(" ", string.Empty) + "ValueLabel";
        valueLabel.Size = new Size(14, 19);
        valueLabel.Text = "0";
        parent.Controls.Add(titleLabel);
        parent.Controls.Add(valueLabel);
    }
}
#nullable restore
