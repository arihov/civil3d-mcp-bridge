using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Civil3DMcpBridge.UI;

/// <summary>
/// WinForms UserControl hosted inside the Civil 3D dockable palette.
/// Three tabs: Live status + per-tool stats, recent calls grid, full
/// tool catalogue. A 1Hz timer refreshes from <see cref="InvocationLog"/>.
/// </summary>
internal sealed class McpPaletteControl : UserControl
{
    private readonly Label _statusLine;
    private readonly Label _summaryLine;
    private readonly DataGridView _recentGrid;
    private readonly DataGridView _statsGrid;
    private readonly DataGridView _toolsGrid;
    private readonly TabControl _tabs;
    private readonly Timer _refreshTimer;
    private long _renderedCalls = -1;

    public McpPaletteControl()
    {
        BackColor = SystemColors.Control;
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // --- Header ---------------------------------------------------------
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.FromArgb(0xF0, 0xF4, 0xFC),
            Padding = new Padding(6),
        };
        _statusLine = new Label
        {
            Text = "Bridge: …",
            AutoSize = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x0B, 0x3D, 0x91),
        };
        _summaryLine = new Label { Text = "Tools: 0   Calls: 0   Errors: 0", AutoSize = true };
        header.Controls.Add(_statusLine);
        header.Controls.Add(_summaryLine);
        root.Controls.Add(header, 0, 0);

        // --- Tabs -----------------------------------------------------------
        _tabs = new TabControl { Dock = DockStyle.Fill };

        // Recent calls tab
        var recentTab = new TabPage("Recent calls");
        _recentGrid = MakeGrid();
        _recentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", Width = 70 });
        _recentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tool", HeaderText = "Tool", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _recentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "ms", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _recentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 60 });
        _recentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Error", HeaderText = "Error", Width = 240 });
        recentTab.Controls.Add(_recentGrid);
        _tabs.TabPages.Add(recentTab);

        // Per-tool stats tab
        var statsTab = new TabPage("Per-tool stats");
        _statsGrid = MakeGrid();
        _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tool", HeaderText = "Tool", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Calls", HeaderText = "Calls", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Errors", HeaderText = "Errors", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MeanMs", HeaderText = "Mean (ms)", Width = 75, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _statsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ErrorRate", HeaderText = "Error %", Width = 65, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        statsTab.Controls.Add(_statsGrid);
        _tabs.TabPages.Add(statsTab);

        // Tool catalogue tab
        var toolsTab = new TabPage("Tool catalogue");
        _toolsGrid = MakeGrid();
        _toolsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Tool", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        toolsTab.Controls.Add(_toolsGrid);
        _tabs.TabPages.Add(toolsTab);

        root.Controls.Add(_tabs, 0, 1);

        // --- Button row -----------------------------------------------------
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 0),
            WrapContents = false,
        };
        buttons.Controls.Add(MakeButton("Start", (_, _) => SafeCommand("MCPSTART")));
        buttons.Controls.Add(MakeButton("Stop", (_, _) => SafeCommand("MCPSTOP")));
        buttons.Controls.Add(MakeButton("Restart", (_, _) => SafeCommand("MCPRESTART")));
        buttons.Controls.Add(MakeButton("Refresh", (_, _) => RefreshNow()));
        buttons.Controls.Add(MakeButton("Copy log", (_, _) => CopyLogToClipboard()));
        buttons.Controls.Add(MakeButton("Copy tools", (_, _) => SafeCommand("MCPCOPYTOOLS")));
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);

        _refreshTimer = new Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshNow();

        // Initial paint + recent-grid double-click shows error details.
        _recentGrid.CellDoubleClick += OnRecentRowDoubleClick;

        Load += (_, _) => { _refreshTimer.Start(); RefreshNow(); };
        Disposed += (_, _) => _refreshTimer.Dispose();
    }

    /// <summary>Force a re-pull from <see cref="InvocationLog"/>.</summary>
    public void RefreshNow()
    {
        if (IsDisposed) return;

        // Header.
        _statusLine.Text = Plugin.IsBridgeRunning
            ? $"Bridge: Listening on {Plugin.BridgeUrl}"
            : "Bridge: Stopped";
        _statusLine.ForeColor = Plugin.IsBridgeRunning
            ? Color.FromArgb(0x0B, 0x6E, 0x1F)
            : Color.FromArgb(0x99, 0x22, 0x22);
        _summaryLine.Text = $"Tools: {ToolRegistry.Count}   " +
            $"Calls: {InvocationLog.TotalCalls}   " +
            $"Errors: {InvocationLog.TotalErrors}";

        // Skip grid redraw when nothing changed (cheap polling without flicker).
        if (_renderedCalls == InvocationLog.TotalCalls && _toolsGrid.RowCount == ToolRegistry.Count)
            return;
        _renderedCalls = InvocationLog.TotalCalls;

        // Recent grid.
        var entries = InvocationLog.Recent();
        _recentGrid.SuspendLayout();
        _recentGrid.Rows.Clear();
        foreach (var e in entries)
        {
            var row = _recentGrid.Rows.Add(
                e.Timestamp.ToString("HH:mm:ss"),
                e.Tool,
                e.DurationMs.ToString("F0"),
                e.Ok ? "OK" : "ERR",
                e.Error ?? "");
            var statusCell = _recentGrid.Rows[row].Cells["Status"];
            statusCell.Style.ForeColor = e.Ok
                ? Color.FromArgb(0x0B, 0x6E, 0x1F)
                : Color.FromArgb(0x99, 0x22, 0x22);
            statusCell.Style.Font = new Font(Font, FontStyle.Bold);
        }
        _recentGrid.ResumeLayout();

        // Stats grid.
        var stats = InvocationLog.Aggregates();
        _statsGrid.SuspendLayout();
        _statsGrid.Rows.Clear();
        foreach (var kv in stats.OrderByDescending(kv => kv.Value.Calls))
        {
            _statsGrid.Rows.Add(
                kv.Key,
                kv.Value.Calls,
                kv.Value.Errors,
                kv.Value.MeanMs.ToString("F1"),
                (kv.Value.ErrorRate * 100).ToString("F1"));
        }
        _statsGrid.ResumeLayout();

        // Tool catalogue.
        _toolsGrid.SuspendLayout();
        _toolsGrid.Rows.Clear();
        foreach (var name in ToolRegistry.Names.OrderBy(n => n))
            _toolsGrid.Rows.Add(name);
        _toolsGrid.ResumeLayout();
    }

    private static DataGridView MakeGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.FromArgb(0xE8, 0xED, 0xF5),
            },
            EnableHeadersVisualStyles = false,
            GridColor = Color.FromArgb(0xD0, 0xD7, 0xE5),
            BorderStyle = BorderStyle.None,
            BackgroundColor = Color.White,
            RowTemplate = { Height = 20 },
            DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Consolas", 9f) },
        };
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.System,
            Padding = new Padding(4, 2, 4, 2),
        };
        b.Click += onClick;
        return b;
    }

    private void OnRecentRowDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _recentGrid.Rows[e.RowIndex];
        var tool = row.Cells["Tool"].Value?.ToString() ?? "";
        var status = row.Cells["Status"].Value?.ToString() ?? "";
        var duration = row.Cells["Duration"].Value?.ToString() ?? "";
        var error = row.Cells["Error"].Value?.ToString() ?? "";
        var msg = $"Tool: {tool}\nStatus: {status}\nDuration: {duration} ms\n\n{error}";
        MessageBox.Show(msg, "Invocation details", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyLogToClipboard()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("time\ttool\tms\tok\terror");
        foreach (var e in InvocationLog.Recent())
        {
            sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append('\t')
              .Append(e.Tool).Append('\t')
              .Append(e.DurationMs.ToString("F1")).Append('\t')
              .Append(e.Ok ? "OK" : "ERR").Append('\t')
              .AppendLine(e.Error?.Replace("\t", " ").Replace("\n", " ") ?? "");
        }
        try { Clipboard.SetText(sb.ToString()); }
        catch { /* clipboard occasionally locked; swallow */ }
    }

    private static void SafeCommand(string cmd)
    {
        try
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute($"{cmd}\n", true, false, true);
        }
        catch { /* ignore */ }
    }
}
