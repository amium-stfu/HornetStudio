#nullable enable
namespace Amium.Item.Server.MqttDemoWinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Button startButton = null!;
    private Button stopButton = null!;
    private Label serviceStatusTitleLabel = null!;
    private Label serviceStatusValueLabel = null!;
    private Label serviceEndpointTitleLabel = null!;
    private Label serviceEndpointValueLabel = null!;
    private GroupBox serviceGroupBox = null!;
    private GroupBox publisherGroupBox = null!;
    private GroupBox writableGroupBox = null!;
    private GroupBox logGroupBox = null!;
    private Label temperatureTitleLabel = null!;
    private Label temperatureValueLabel = null!;
    private Label pressureTitleLabel = null!;
    private Label pressureValueLabel = null!;
    private Label publisherUpdatedTitleLabel = null!;
    private Label publisherUpdatedValueLabel = null!;
    private Label temperatureTopicTitleLabel = null!;
    private Label temperatureTopicValueLabel = null!;
    private Label pressureTopicTitleLabel = null!;
    private Label pressureTopicValueLabel = null!;
    private Label writableValueTitleLabel = null!;
    private Label writableValueLabel = null!;
    private Label writableSourceTitleLabel = null!;
    private Label writableSourceValueLabel = null!;
    private Label writableUpdatedTitleLabel = null!;
    private Label writableUpdatedValueLabel = null!;
    private Label writablePathTitleLabel = null!;
    private Label writablePathValueLabel = null!;
    private TextBox logTextBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        startButton = new Button();
        stopButton = new Button();
        serviceStatusTitleLabel = new Label();
        serviceStatusValueLabel = new Label();
        serviceEndpointTitleLabel = new Label();
        serviceEndpointValueLabel = new Label();
        serviceGroupBox = new GroupBox();
        publisherGroupBox = new GroupBox();
        writableGroupBox = new GroupBox();
        logGroupBox = new GroupBox();
        temperatureTitleLabel = new Label();
        temperatureValueLabel = new Label();
        pressureTitleLabel = new Label();
        pressureValueLabel = new Label();
        publisherUpdatedTitleLabel = new Label();
        publisherUpdatedValueLabel = new Label();
        temperatureTopicTitleLabel = new Label();
        temperatureTopicValueLabel = new Label();
        pressureTopicTitleLabel = new Label();
        pressureTopicValueLabel = new Label();
        writableValueTitleLabel = new Label();
        writableValueLabel = new Label();
        writableSourceTitleLabel = new Label();
        writableSourceValueLabel = new Label();
        writableUpdatedTitleLabel = new Label();
        writableUpdatedValueLabel = new Label();
        writablePathTitleLabel = new Label();
        writablePathValueLabel = new Label();
        logTextBox = new TextBox();
        SuspendLayout();
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(245, 247, 250);
        ClientSize = new Size(980, 720);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        MinimumSize = new Size(920, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Amium Item Server MQTT Demo";

        serviceGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        serviceGroupBox.Location = new Point(20, 18);
        serviceGroupBox.Name = "serviceGroupBox";
        serviceGroupBox.Size = new Size(940, 122);
        serviceGroupBox.TabIndex = 0;
        serviceGroupBox.TabStop = false;
        serviceGroupBox.Text = "Service Host";

        startButton.BackColor = Color.FromArgb(34, 116, 165);
        startButton.FlatStyle = FlatStyle.Flat;
        startButton.ForeColor = Color.White;
        startButton.Location = new Point(20, 34);
        startButton.Name = "startButton";
        startButton.Size = new Size(120, 34);
        startButton.TabIndex = 0;
        startButton.Text = "Start Service";
        startButton.UseVisualStyleBackColor = false;
        startButton.Click += StartButton_Click;

        stopButton.BackColor = Color.FromArgb(110, 118, 129);
        stopButton.FlatStyle = FlatStyle.Flat;
        stopButton.ForeColor = Color.White;
        stopButton.Location = new Point(152, 34);
        stopButton.Name = "stopButton";
        stopButton.Size = new Size(120, 34);
        stopButton.TabIndex = 1;
        stopButton.Text = "Stop Service";
        stopButton.UseVisualStyleBackColor = false;
        stopButton.Click += StopButton_Click;

        serviceStatusTitleLabel.AutoSize = true;
        serviceStatusTitleLabel.Location = new Point(302, 42);
        serviceStatusTitleLabel.Name = "serviceStatusTitleLabel";
        serviceStatusTitleLabel.Size = new Size(42, 15);
        serviceStatusTitleLabel.TabIndex = 2;
        serviceStatusTitleLabel.Text = "Status:";

        serviceStatusValueLabel.AutoSize = true;
        serviceStatusValueLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        serviceStatusValueLabel.Location = new Point(352, 42);
        serviceStatusValueLabel.Name = "serviceStatusValueLabel";
        serviceStatusValueLabel.Size = new Size(49, 15);
        serviceStatusValueLabel.TabIndex = 3;
        serviceStatusValueLabel.Text = "Stopped";

        serviceEndpointTitleLabel.AutoSize = true;
        serviceEndpointTitleLabel.Location = new Point(20, 84);
        serviceEndpointTitleLabel.Name = "serviceEndpointTitleLabel";
        serviceEndpointTitleLabel.Size = new Size(58, 15);
        serviceEndpointTitleLabel.TabIndex = 4;
        serviceEndpointTitleLabel.Text = "Endpoint:";

        serviceEndpointValueLabel.AutoSize = true;
        serviceEndpointValueLabel.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        serviceEndpointValueLabel.Location = new Point(86, 84);
        serviceEndpointValueLabel.Name = "serviceEndpointValueLabel";
        serviceEndpointValueLabel.Size = new Size(112, 14);
        serviceEndpointValueLabel.TabIndex = 5;
        serviceEndpointValueLabel.Text = "127.0.0.1:1883";

        serviceGroupBox.Controls.Add(startButton);
        serviceGroupBox.Controls.Add(stopButton);
        serviceGroupBox.Controls.Add(serviceStatusTitleLabel);
        serviceGroupBox.Controls.Add(serviceStatusValueLabel);
        serviceGroupBox.Controls.Add(serviceEndpointTitleLabel);
        serviceGroupBox.Controls.Add(serviceEndpointValueLabel);

        publisherGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        publisherGroupBox.Location = new Point(20, 154);
        publisherGroupBox.Name = "publisherGroupBox";
        publisherGroupBox.Size = new Size(940, 170);
        publisherGroupBox.TabIndex = 1;
        publisherGroupBox.TabStop = false;
        publisherGroupBox.Text = "Published Demo Items";

        temperatureTitleLabel.AutoSize = true;
        temperatureTitleLabel.Location = new Point(20, 35);
        temperatureTitleLabel.Name = "temperatureTitleLabel";
        temperatureTitleLabel.Size = new Size(76, 15);
        temperatureTitleLabel.TabIndex = 0;
        temperatureTitleLabel.Text = "Temperature:";

        temperatureValueLabel.AutoSize = true;
        temperatureValueLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point);
        temperatureValueLabel.Location = new Point(136, 30);
        temperatureValueLabel.Name = "temperatureValueLabel";
        temperatureValueLabel.Size = new Size(71, 21);
        temperatureValueLabel.TabIndex = 1;
        temperatureValueLabel.Text = "22.0 degC";

        pressureTitleLabel.AutoSize = true;
        pressureTitleLabel.Location = new Point(20, 69);
        pressureTitleLabel.Name = "pressureTitleLabel";
        pressureTitleLabel.Size = new Size(54, 15);
        pressureTitleLabel.TabIndex = 2;
        pressureTitleLabel.Text = "Pressure:";

        pressureValueLabel.AutoSize = true;
        pressureValueLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point);
        pressureValueLabel.Location = new Point(136, 64);
        pressureValueLabel.Name = "pressureValueLabel";
        pressureValueLabel.Size = new Size(73, 21);
        pressureValueLabel.TabIndex = 3;
        pressureValueLabel.Text = "1012 hPa";

        publisherUpdatedTitleLabel.AutoSize = true;
        publisherUpdatedTitleLabel.Location = new Point(20, 103);
        publisherUpdatedTitleLabel.Name = "publisherUpdatedTitleLabel";
        publisherUpdatedTitleLabel.Size = new Size(82, 15);
        publisherUpdatedTitleLabel.TabIndex = 4;
        publisherUpdatedTitleLabel.Text = "Last published:";

        publisherUpdatedValueLabel.AutoSize = true;
        publisherUpdatedValueLabel.Location = new Point(136, 103);
        publisherUpdatedValueLabel.Name = "publisherUpdatedValueLabel";
        publisherUpdatedValueLabel.Size = new Size(10, 15);
        publisherUpdatedValueLabel.TabIndex = 5;
        publisherUpdatedValueLabel.Text = "-";

        temperatureTopicTitleLabel.AutoSize = true;
        temperatureTopicTitleLabel.Location = new Point(410, 35);
        temperatureTopicTitleLabel.Name = "temperatureTopicTitleLabel";
        temperatureTopicTitleLabel.Size = new Size(97, 15);
        temperatureTopicTitleLabel.TabIndex = 6;
        temperatureTopicTitleLabel.Text = "Temperature topic:";

        temperatureTopicValueLabel.AutoSize = true;
        temperatureTopicValueLabel.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        temperatureTopicValueLabel.Location = new Point(526, 36);
        temperatureTopicValueLabel.Name = "temperatureTopicValueLabel";
        temperatureTopicValueLabel.Size = new Size(21, 14);
        temperatureTopicValueLabel.TabIndex = 7;
        temperatureTopicValueLabel.Text = "-";

        pressureTopicTitleLabel.AutoSize = true;
        pressureTopicTitleLabel.Location = new Point(410, 69);
        pressureTopicTitleLabel.Name = "pressureTopicTitleLabel";
        pressureTopicTitleLabel.Size = new Size(75, 15);
        pressureTopicTitleLabel.TabIndex = 8;
        pressureTopicTitleLabel.Text = "Pressure topic:";

        pressureTopicValueLabel.AutoSize = true;
        pressureTopicValueLabel.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        pressureTopicValueLabel.Location = new Point(526, 70);
        pressureTopicValueLabel.Name = "pressureTopicValueLabel";
        pressureTopicValueLabel.Size = new Size(21, 14);
        pressureTopicValueLabel.TabIndex = 9;
        pressureTopicValueLabel.Text = "-";

        publisherGroupBox.Controls.Add(temperatureTitleLabel);
        publisherGroupBox.Controls.Add(temperatureValueLabel);
        publisherGroupBox.Controls.Add(pressureTitleLabel);
        publisherGroupBox.Controls.Add(pressureValueLabel);
        publisherGroupBox.Controls.Add(publisherUpdatedTitleLabel);
        publisherGroupBox.Controls.Add(publisherUpdatedValueLabel);
        publisherGroupBox.Controls.Add(temperatureTopicTitleLabel);
        publisherGroupBox.Controls.Add(temperatureTopicValueLabel);
        publisherGroupBox.Controls.Add(pressureTopicTitleLabel);
        publisherGroupBox.Controls.Add(pressureTopicValueLabel);

        writableGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        writableGroupBox.Location = new Point(20, 338);
        writableGroupBox.Name = "writableGroupBox";
        writableGroupBox.Size = new Size(940, 170);
        writableGroupBox.TabIndex = 2;
        writableGroupBox.TabStop = false;
        writableGroupBox.Text = "Writable Demo Item";

        writableValueTitleLabel.AutoSize = true;
        writableValueTitleLabel.Location = new Point(20, 35);
        writableValueTitleLabel.Name = "writableValueTitleLabel";
        writableValueTitleLabel.Size = new Size(74, 15);
        writableValueTitleLabel.TabIndex = 0;
        writableValueTitleLabel.Text = "Current value:";

        writableValueLabel.AutoSize = true;
        writableValueLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point);
        writableValueLabel.Location = new Point(136, 30);
        writableValueLabel.Name = "writableValueLabel";
        writableValueLabel.Size = new Size(45, 21);
        writableValueLabel.TabIndex = 1;
        writableValueLabel.Text = "50.0%";

        writableSourceTitleLabel.AutoSize = true;
        writableSourceTitleLabel.Location = new Point(20, 69);
        writableSourceTitleLabel.Name = "writableSourceTitleLabel";
        writableSourceTitleLabel.Size = new Size(98, 15);
        writableSourceTitleLabel.TabIndex = 2;
        writableSourceTitleLabel.Text = "Last write source:";

        writableSourceValueLabel.AutoSize = true;
        writableSourceValueLabel.Location = new Point(136, 69);
        writableSourceValueLabel.Name = "writableSourceValueLabel";
        writableSourceValueLabel.Size = new Size(61, 15);
        writableSourceValueLabel.TabIndex = 3;
        writableSourceValueLabel.Text = "Not written";

        writableUpdatedTitleLabel.AutoSize = true;
        writableUpdatedTitleLabel.Location = new Point(20, 103);
        writableUpdatedTitleLabel.Name = "writableUpdatedTitleLabel";
        writableUpdatedTitleLabel.Size = new Size(104, 15);
        writableUpdatedTitleLabel.TabIndex = 4;
        writableUpdatedTitleLabel.Text = "Last write received:";

        writableUpdatedValueLabel.AutoSize = true;
        writableUpdatedValueLabel.Location = new Point(136, 103);
        writableUpdatedValueLabel.Name = "writableUpdatedValueLabel";
        writableUpdatedValueLabel.Size = new Size(10, 15);
        writableUpdatedValueLabel.TabIndex = 5;
        writableUpdatedValueLabel.Text = "-";

        writablePathTitleLabel.AutoSize = true;
        writablePathTitleLabel.Location = new Point(410, 35);
        writablePathTitleLabel.Name = "writablePathTitleLabel";
        writablePathTitleLabel.Size = new Size(99, 15);
        writablePathTitleLabel.TabIndex = 6;
        writablePathTitleLabel.Text = "Write to this topic:";

        writablePathValueLabel.AutoSize = true;
        writablePathValueLabel.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        writablePathValueLabel.Location = new Point(526, 36);
        writablePathValueLabel.Name = "writablePathValueLabel";
        writablePathValueLabel.Size = new Size(21, 14);
        writablePathValueLabel.TabIndex = 7;
        writablePathValueLabel.Text = "-";

        writableGroupBox.Controls.Add(writableValueTitleLabel);
        writableGroupBox.Controls.Add(writableValueLabel);
        writableGroupBox.Controls.Add(writableSourceTitleLabel);
        writableGroupBox.Controls.Add(writableSourceValueLabel);
        writableGroupBox.Controls.Add(writableUpdatedTitleLabel);
        writableGroupBox.Controls.Add(writableUpdatedValueLabel);
        writableGroupBox.Controls.Add(writablePathTitleLabel);
        writableGroupBox.Controls.Add(writablePathValueLabel);

        logGroupBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logGroupBox.Location = new Point(20, 522);
        logGroupBox.Name = "logGroupBox";
        logGroupBox.Size = new Size(940, 180);
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
        logTextBox.Size = new Size(900, 134);
        logTextBox.TabIndex = 0;

        logGroupBox.Controls.Add(logTextBox);

        Controls.Add(serviceGroupBox);
        Controls.Add(publisherGroupBox);
        Controls.Add(writableGroupBox);
        Controls.Add(logGroupBox);
        ResumeLayout(false);
    }
}
#nullable restore
