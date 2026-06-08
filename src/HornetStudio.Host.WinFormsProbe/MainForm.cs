using HornetStudio.Host.Registries;

namespace HornetStudio.Host.WinFormsProbe;

internal sealed class MainForm : Form
{
    private readonly HostItemRegistry _registry;

    private readonly ListBox _pathList = new();
    private readonly TextBox _currentValueTextBox = new();
    private readonly TextBox _writeValueTextBox = new();
    private readonly TextBox _logTextBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _writeButton = new();

    public MainForm(HostItemRegistry registry)
    {
        _registry = registry;

        Text = "Host Item Registry Probe";
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();
        RefreshPaths();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        _pathList.Dock = DockStyle.Fill;

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(12, 0, 0, 0)
        };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _currentValueTextBox.ReadOnly = true;
        _currentValueTextBox.Dock = DockStyle.Top;

        _writeValueTextBox.Dock = DockStyle.Top;

        _refreshButton.Text = "Refresh";
        _refreshButton.AutoSize = true;

        _writeButton.Text = "Write";
        _writeButton.AutoSize = true;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true
        };
        buttons.Controls.Add(_refreshButton);
        buttons.Controls.Add(_writeButton);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;

        details.Controls.Add(new Label { Text = "Current value", AutoSize = true });
        details.Controls.Add(_currentValueTextBox);
        details.Controls.Add(new Label { Text = "Write value", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        details.Controls.Add(_writeValueTextBox);
        details.Controls.Add(buttons);
        details.Controls.Add(new Label { Text = "Log", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        details.Controls.Add(_logTextBox);

        root.Controls.Add(_pathList, 0, 0);
        root.Controls.Add(details, 1, 0);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        _pathList.SelectedIndexChanged += (_, _) => RefreshSelectedValue();
        _refreshButton.Click += (_, _) => RefreshSelectedValue();
        _writeButton.Click += (_, _) => WriteSelectedValue();
    }

    private void RefreshPaths()
    {
        _pathList.Items.Clear();
        foreach (var path in _registry.Paths)
        {
            _pathList.Items.Add(path);
        }

        if (_pathList.Items.Count > 0)
        {
            _pathList.SelectedIndex = 0;
        }
    }

    private void RefreshSelectedValue()
    {
        var path = GetSelectedPath();
        if (path is null)
        {
            _currentValueTextBox.Text = string.Empty;
            return;
        }

        _currentValueTextBox.Text = _registry.TryRead(path, out var value)
            ? value?.ToString() ?? string.Empty
            : "Path is not readable.";
        AppendLog($"Read {path} = {_currentValueTextBox.Text}");
    }

    private void WriteSelectedValue()
    {
        var path = GetSelectedPath();
        if (path is null)
        {
            MessageBox.Show(this, "Select a path before writing.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var convertedValue = ConvertWriteValue(path, _writeValueTextBox.Text);
        if (!_registry.TryWrite(path, convertedValue))
        {
            MessageBox.Show(this, "The selected path could not be written.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppendLog($"Write rejected for {path}");
            return;
        }

        AppendLog($"Wrote {path} = {convertedValue}");
        RefreshSelectedValue();
    }

    private object? ConvertWriteValue(string path, string rawValue)
    {
        if (!_registry.TryGetItem(path, out var item) || item is null)
        {
            return rawValue;
        }

        return item.Value switch
        {
            int when int.TryParse(rawValue, out var parsedInteger) => parsedInteger,
            long when long.TryParse(rawValue, out var parsedLong) => parsedLong,
            double when double.TryParse(rawValue, out var parsedDouble) => parsedDouble,
            float when float.TryParse(rawValue, out var parsedFloat) => parsedFloat,
            decimal when decimal.TryParse(rawValue, out var parsedDecimal) => parsedDecimal,
            bool when bool.TryParse(rawValue, out var parsedBoolean) => parsedBoolean,
            _ => rawValue
        };
    }

    private string? GetSelectedPath()
    {
        return _pathList.SelectedItem as string;
    }

    private void AppendLog(string message)
    {
        _logTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
    }
}
