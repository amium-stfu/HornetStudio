using System.Globalization;
using System.Text;
using Amium.Item.Server.MultimasterDemo.Controllers;
using Amium.Item.Server.MultimasterDemo.Models;
using Amium.Items;

namespace Amium.Item.Server.MultimasterDemo;

internal sealed class MainForm : Form
{
    private readonly MultimasterDemoController _controller;
    private readonly Dictionary<string, NodeView> _nodeViews = new(StringComparer.OrdinalIgnoreCase);

    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _meshDemoButton = null!;
    private Label _statusValueLabel = null!;
    private Label _endpointValueLabel = null!;
    private TableLayoutPanel _actionsLayout = null!;
    private TextBox _crossWriteValueTextBox = null!;
    private ComboBox _addItemOwnerComboBox = null!;
    private TextBox _addItemSuffixTextBox = null!;
    private TextBox _addItemValueTextBox = null!;
    private CheckBox _addItemWritableCheckBox = null!;

    internal MainForm()
    {
        _controller = new MultimasterDemoController();
        _controller.NodeSnapshotChanged += HandleNodeSnapshotChanged;
        _controller.NodeEventLogged += HandleNodeEventLogged;

        BuildLayout();
        Text = "Item Server Multimaster Demo";
        MinimumSize = new Size(1500, 760);
        StartPosition = FormStartPosition.CenterScreen;

        _endpointValueLabel.Text = _controller.EndpointSummary;
        UpdateServiceState();

        foreach (var snapshot in _controller.CurrentSnapshots)
        {
            _nodeViews[snapshot.NodeId].ApplySnapshot(snapshot);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (!_controller.IsRunning)
        {
            await StartDemoAsync().ConfigureAwait(true);
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        _startButton.Enabled = false;
        _stopButton.Enabled = false;

        try
        {
            await _controller.DisposeAsync().ConfigureAwait(true);
        }
        finally
        {
            base.OnFormClosing(e);
        }
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 7,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _startButton = new Button
        {
            Text = "Start Demo",
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
        };
        _startButton.Click += async (_, _) => await StartDemoAsync().ConfigureAwait(true);

        _stopButton = new Button
        {
            Text = "Stop Demo",
            AutoSize = true,
            Margin = new Padding(0, 0, 16, 0),
        };
        _stopButton.Click += async (_, _) => await StopDemoAsync().ConfigureAwait(true);

        _meshDemoButton = new Button
        {
            Text = "Open Mesh Demo",
            AutoSize = true,
            Margin = new Padding(0, 0, 16, 0),
        };
        _meshDemoButton.Click += (_, _) => new MeshMainForm().Show(this);

        var statusLabel = new Label
        {
            Text = "Status:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0),
        };
        _statusValueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 16, 0),
            Font = new Font(Font, FontStyle.Bold),
        };

        var endpointLabel = new Label
        {
            Text = "Broker:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0),
        };
        _endpointValueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 0, 0),
        };

        toolbar.Controls.Add(_startButton, 0, 0);
        toolbar.Controls.Add(_stopButton, 1, 0);
        toolbar.Controls.Add(_meshDemoButton, 2, 0);
        toolbar.Controls.Add(statusLabel, 3, 0);
        toolbar.Controls.Add(_statusValueLabel, 4, 0);
        toolbar.Controls.Add(endpointLabel, 5, 0);
        toolbar.Controls.Add(_endpointValueLabel, 6, 0);

        _actionsLayout = BuildActionsLayout();

        var nodesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = _controller.Nodes.Count,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0),
        };

        for (var index = 0; index < _controller.Nodes.Count; index++)
        {
            nodesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _controller.Nodes.Count));
            var node = _controller.Nodes[index];
            var view = new NodeView(node.NodeId, node.DisplayName, value => WriteNodeValueAsync(node.NodeId, value));
            _nodeViews[node.NodeId] = view;
            nodesLayout.Controls.Add(view.Container, index, 0);
        }

        rootLayout.Controls.Add(toolbar, 0, 0);
        rootLayout.Controls.Add(_actionsLayout, 0, 1);
        rootLayout.Controls.Add(nodesLayout, 0, 2);

        Controls.Add(rootLayout);
        ResumeLayout(performLayout: true);
    }

    private TableLayoutPanel BuildActionsLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 12, 0, 0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

        var crossWriteGroup = new GroupBox
        {
            Text = "Cross-Write Actions",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 8, 0),
        };

        var crossWriteLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        crossWriteLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        crossWriteLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        crossWriteLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var crossWriteInfoLabel = new Label
        {
            Text = "Publish a remote write_test update from one node to another.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _crossWriteValueTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Optional custom payload; leave empty for an auto-generated value",
            Margin = new Padding(0, 0, 0, 8),
        };

        var crossWriteButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0),
        };

        foreach (var actor in _controller.Nodes)
        {
            foreach (var target in _controller.Nodes.Where(node => !string.Equals(node.NodeId, actor.NodeId, StringComparison.OrdinalIgnoreCase)))
            {
                var actorNodeId = actor.NodeId;
                var targetNodeId = target.NodeId;
                var button = new Button
                {
                    Text = $"{GetShortNodeLabel(actorNodeId)} -> {GetShortNodeLabel(targetNodeId)}",
                    AutoSize = true,
                    Margin = new Padding(0, 0, 8, 8),
                };
                button.Click += async (_, _) => await WriteCrossNodeValueAsync(actorNodeId, targetNodeId).ConfigureAwait(true);
                crossWriteButtons.Controls.Add(button);
            }
        }

        crossWriteLayout.Controls.Add(crossWriteInfoLabel, 0, 0);
        crossWriteLayout.Controls.Add(_crossWriteValueTextBox, 0, 1);
        crossWriteLayout.Controls.Add(crossWriteButtons, 0, 2);
        crossWriteGroup.Controls.Add(crossWriteLayout);

        var addItemGroup = new GroupBox
        {
            Text = "Add Runtime Item",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            Margin = new Padding(8, 0, 0, 0),
        };

        var addItemLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        addItemLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addItemLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _addItemOwnerComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _addItemOwnerComboBox.Items.AddRange(_controller.Nodes.Select(node => node.NodeId).Cast<object>().ToArray());
        _addItemOwnerComboBox.SelectedIndex = 0;

        _addItemSuffixTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = "added_item",
        };

        _addItemValueTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = $"runtime item @ {DateTime.Now:HH:mm:ss}",
        };

        _addItemWritableCheckBox = new CheckBox
        {
            Text = "Writable",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        var addItemButton = new Button
        {
            Text = "Add Item",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        addItemButton.Click += async (_, _) => await AddRuntimeItemAsync().ConfigureAwait(true);

        addItemLayout.Controls.Add(CreateActionLabel("Owner"), 0, 0);
        addItemLayout.Controls.Add(_addItemOwnerComboBox, 1, 0);
        addItemLayout.Controls.Add(CreateActionLabel("Runtime Suffix"), 0, 1);
        addItemLayout.Controls.Add(_addItemSuffixTextBox, 1, 1);
        addItemLayout.Controls.Add(CreateActionLabel("Initial Value"), 0, 2);
        addItemLayout.Controls.Add(_addItemValueTextBox, 1, 2);
        addItemLayout.Controls.Add(CreateActionLabel("Options"), 0, 3);
        addItemLayout.Controls.Add(_addItemWritableCheckBox, 1, 3);
        addItemLayout.Controls.Add(CreateActionLabel(string.Empty), 0, 4);
        addItemLayout.Controls.Add(addItemButton, 1, 4);

        addItemGroup.Controls.Add(addItemLayout);

        layout.Controls.Add(crossWriteGroup, 0, 0);
        layout.Controls.Add(addItemGroup, 1, 0);
        return layout;
    }

    private async Task StartDemoAsync()
    {
        if (_controller.IsRunning)
        {
            return;
        }

        _startButton.Enabled = false;
        _stopButton.Enabled = false;

        try
        {
            await _controller.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                text: ex.Message,
                caption: "Start Failed",
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Error);
        }
        finally
        {
            UpdateServiceState();
        }
    }

    private async Task StopDemoAsync()
    {
        _startButton.Enabled = false;
        _stopButton.Enabled = false;

        try
        {
            await _controller.StopAsync().ConfigureAwait(true);
        }
        finally
        {
            UpdateServiceState();
        }
    }

    private async Task WriteNodeValueAsync(string nodeId, string value)
    {
        try
        {
            await _controller.WriteTestAsync(nodeId, value).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                text: ex.Message,
                caption: "Write Failed",
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Warning);
        }
    }

    private async Task WriteCrossNodeValueAsync(string actorNodeId, string targetNodeId)
    {
        try
        {
            var value = string.IsNullOrWhiteSpace(_crossWriteValueTextBox.Text)
                ? BuildCrossWriteValue(actorNodeId, targetNodeId)
                : _crossWriteValueTextBox.Text.Trim();
            await _controller.WriteRemoteValueAsync(
                actorNodeId: actorNodeId,
                path: DemoNodeController.GetWriteTestItemPath(targetNodeId),
                value: value).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                text: ex.Message,
                caption: "Cross-Write Failed",
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Warning);
        }
    }

    private async Task AddRuntimeItemAsync()
    {
        try
        {
            var ownerNodeId = Convert.ToString(_addItemOwnerComboBox.SelectedItem, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(ownerNodeId))
            {
                throw new InvalidOperationException("Select an owner node before adding a runtime item.");
            }

            var suffix = _addItemSuffixTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(suffix))
            {
                throw new InvalidOperationException("Enter a runtime suffix before adding a runtime item.");
            }

            var path = BuildRuntimeItemPath(ownerNodeId, suffix);
            var value = _addItemValueTextBox.Text;

            await _controller.PublishRuntimeItemAsync(
                nodeId: ownerNodeId,
                path: path,
                value: value,
                writable: _addItemWritableCheckBox.Checked).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                text: ex.Message,
                caption: "Add Item Failed",
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Warning);
        }
    }

    private void HandleNodeSnapshotChanged(object? sender, DemoNodeSnapshot snapshot)
        => RunOnUiThread(() => _nodeViews[snapshot.NodeId].ApplySnapshot(snapshot));

    private void HandleNodeEventLogged(object? sender, DemoNodeEvent nodeEvent)
        => RunOnUiThread(() => _nodeViews[nodeEvent.NodeId].AppendEvent(nodeEvent));

    private void UpdateServiceState()
    {
        _statusValueLabel.Text = _controller.IsRunning ? "Running" : "Stopped";
        _statusValueLabel.ForeColor = _controller.IsRunning ? Color.DarkGreen : Color.Firebrick;
        _startButton.Enabled = !_controller.IsRunning;
        _stopButton.Enabled = _controller.IsRunning;
        _actionsLayout.Enabled = _controller.IsRunning;
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

    private static Label CreateActionLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0),
        };

    private static string BuildCrossWriteValue(string actorNodeId, string targetNodeId)
        => $"{actorNodeId}->{targetNodeId} @ {DateTime.Now:HH:mm:ss}";

    private static string BuildRuntimeItemPath(string ownerNodeId, string suffix)
        => $"nodes.{ownerNodeId}.runtime.{ItemPath.Normalize(suffix)}";

    private static string GetShortNodeLabel(string nodeId)
        => nodeId.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault()?.ToUpperInvariant() ?? nodeId.ToUpperInvariant();

    private sealed class NodeView
    {
        private readonly Func<string, Task> _writeAction;
        private readonly Label _statusValueLabel;
        private readonly Label _localDynamicValueLabel;
        private readonly Label _localDynamicMetaLabel;
        private readonly Label _localWriteValueLabel;
        private readonly Label _localWriteMetaLabel;
        private readonly TextBox _writeInputTextBox;
        private readonly TextBox _observedValuesTextBox;
        private readonly ListBox _eventListBox;

        internal NodeView(string nodeId, string displayName, Func<string, Task> writeAction)
        {
            NodeId = nodeId;
            DisplayName = displayName;
            _writeAction = writeAction;

            Container = new GroupBox
            {
                Text = displayName,
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                Margin = new Padding(6),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));

            _statusValueLabel = CreateValueLabel();
            _localDynamicValueLabel = CreateValueLabel();
            _localDynamicMetaLabel = CreateMetaLabel();
            _localWriteValueLabel = CreateValueLabel();
            _localWriteMetaLabel = CreateMetaLabel();

            _writeInputTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Enter a new write_test value",
                Text = $"{displayName} write @ {DateTime.Now:HH:mm:ss}",
            };

            var writeButton = new Button
            {
                Text = "Publish write_test",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            writeButton.Click += async (_, _) => await _writeAction(_writeInputTextBox.Text).ConfigureAwait(true);

            _observedValuesTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f, FontStyle.Regular),
            };

            _eventListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                HorizontalScrollbar = true,
                Font = new Font("Consolas", 9f, FontStyle.Regular),
            };

            layout.Controls.Add(CreateTitleLabel("Status"), 0, 0);
            layout.Controls.Add(_statusValueLabel, 1, 0);
            layout.Controls.Add(CreateTitleLabel("Local Dynamic Value"), 0, 1);
            layout.Controls.Add(_localDynamicValueLabel, 1, 1);
            layout.Controls.Add(CreateSpacerLabel(), 1, 2);
            layout.Controls.Add(_localDynamicMetaLabel, 1, 2);
            layout.Controls.Add(CreateTitleLabel("Local Write Test"), 0, 3);
            layout.Controls.Add(_localWriteValueLabel, 1, 3);
            layout.Controls.Add(CreateSpacerLabel(), 1, 4);
            layout.Controls.Add(_localWriteMetaLabel, 1, 4);
            layout.Controls.Add(_writeInputTextBox, 0, 5);
            layout.SetColumnSpan(_writeInputTextBox, 1);
            layout.Controls.Add(writeButton, 1, 5);

            var observedLabel = CreateTitleLabel("Observed Values");
            observedLabel.Margin = new Padding(0, 12, 0, 4);
            layout.Controls.Add(observedLabel, 0, 6);
            layout.SetColumnSpan(observedLabel, 2);
            layout.Controls.Add(_observedValuesTextBox, 0, 6);
            layout.SetColumnSpan(_observedValuesTextBox, 2);
            _observedValuesTextBox.Margin = new Padding(0, 36, 0, 8);

            var eventsLabel = CreateTitleLabel("Events");
            eventsLabel.Margin = new Padding(0, 0, 0, 4);
            layout.Controls.Add(eventsLabel, 0, 7);
            layout.SetColumnSpan(eventsLabel, 2);
            layout.Controls.Add(_eventListBox, 0, 7);
            layout.SetColumnSpan(_eventListBox, 2);
            _eventListBox.Margin = new Padding(0, 24, 0, 0);

            Container.Controls.Add(layout);
        }

        internal Control Container { get; }

        internal string NodeId { get; }

        internal string DisplayName { get; }

        internal void ApplySnapshot(DemoNodeSnapshot snapshot)
        {
            _statusValueLabel.Text = snapshot.StatusText;
            _statusValueLabel.ForeColor = snapshot.IsConnected ? Color.DarkGreen : Color.Firebrick;

            _localDynamicValueLabel.Text = snapshot.LocalDynamicValue.ToString("0.000", CultureInfo.InvariantCulture);
            _localDynamicMetaLabel.Text = FormatLocalMeta(snapshot.LocalDynamicSequence, snapshot.LocalDynamicUpdatedUtc);
            _localWriteValueLabel.Text = snapshot.LocalWriteTestValue;
            _localWriteMetaLabel.Text = FormatLocalMeta(snapshot.LocalWriteSequence, snapshot.LocalWriteUpdatedUtc);
            _observedValuesTextBox.Text = FormatObservedValues(snapshot.ObservedNodes);
        }

        internal void AppendEvent(DemoNodeEvent nodeEvent)
        {
            var entry = $"[{nodeEvent.TimestampUtc.ToLocalTime():HH:mm:ss}] {nodeEvent.Message}";
            _eventListBox.Items.Insert(0, entry);
            while (_eventListBox.Items.Count > 120)
            {
                _eventListBox.Items.RemoveAt(_eventListBox.Items.Count - 1);
            }
        }

        private static Label CreateTitleLabel(string text)
            => new()
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 6, 8, 0),
            };

        private static Label CreateValueLabel()
            => new()
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 0),
            };

        private static Label CreateMetaLabel()
            => new()
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 0),
            };

        private static Label CreateSpacerLabel()
            => new()
            {
                AutoSize = true,
                Text = string.Empty,
                Margin = new Padding(0),
            };

        private static string FormatLocalMeta(int sequence, DateTimeOffset? timestampUtc)
        {
            if (timestampUtc is null)
            {
                return $"Sequence: {sequence} | Updated: -";
            }

            return $"Sequence: {sequence} | Updated: {timestampUtc.Value.ToLocalTime():HH:mm:ss}";
        }

        private static string FormatObservedValues(IReadOnlyList<DemoObservedNodeState> observedNodes)
        {
            var builder = new StringBuilder();
            foreach (var node in observedNodes)
            {
                builder.AppendLine(node.DisplayName);
                builder.AppendLine($"  dynamic_value : {FormatObservedValue(node.DynamicValue)}");
                builder.AppendLine($"  write_test    : {FormatObservedValue(node.WriteTestValue)}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string FormatObservedValue(DemoObservedValue value)
        {
            var updatedText = value.LastUpdatedUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";
            var status = value.IsAvailable ? "visible" : "missing";
            return $"{value.ValueText} | updates={value.UpdateCount} | at={updatedText} | {status} | src={value.SourceClientId}";
        }
    }
}