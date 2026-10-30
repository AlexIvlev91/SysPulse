using System.Diagnostics;
using System.Globalization;
using SysPulse.Monitoring;
using SysPulse.Native;
using SysPulse.UI.Controls;

namespace SysPulse.UI;

internal sealed class MainForm : Form
{
    private readonly SysPulseApp _app;

    private readonly Label _subtitle = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextSubtle, Margin = Theme.S(2, 0, 0, 0) };
    private readonly ToggleSwitch _overlaySwitch = new("Overlay");
    private readonly ToggleSwitch _startupSwitch = new("Start with Windows");
    private readonly ModernButton _overlaySettings = new("", ButtonKind.Ghost, Glyph.Settings) { Margin = Theme.S(2, 0, 0, 0) };
    private readonly ModernButton _adminButton = new("Run as administrator", ButtonKind.Ghost, Glyph.Shield);
    private readonly Label _adminChip = new() { AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.TextMuted, Text = "Administrator", Padding = Theme.S(0, 8, 0, 0) };

    private readonly MetricCard _cpu = new("CPU", Glyph.Cpu, Theme.CpuSeries) { Dock = DockStyle.Fill, TempLink = "Show CPU temp", ValueCaption = "usage" };
    private readonly MetricCard _gpu = new("GPU", Glyph.Gpu, Theme.GpuSeries) { Dock = DockStyle.Fill, ValueCaption = "usage" };
    private readonly MetricCard _memory = new("Memory", Glyph.Memory, Theme.MemorySeries) { Dock = DockStyle.Fill, ShowTemperature = false };
    private readonly TurboCard _turbo = new() { Dock = DockStyle.Fill };
    private readonly StatStrip _stats = new() { Dock = DockStyle.Fill };

    private readonly Label _procSummary = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextMuted, Margin = Theme.S(0, 7, 12, 0) };
    private readonly Segmented _procSort = new("By memory", "By CPU") { SelectedIndex = 0 };
    private readonly ProcessList _processes = new() { Dock = DockStyle.Fill };

    private List<AppGroup> _lastGroups = [];
    private Snapshot? _snapshot;

    public MainForm(SysPulseApp app)
    {
        _app = app;
        Text = "SysPulse";
        Icon = TrayIcons.AppIcon;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        MinimumSize = Theme.S(980, 700);
        Size = Theme.S(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = Theme.S(20, 14, 20, 8);
        LoadSavedBounds();

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Window, Margin = Padding.Empty };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(272)));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(96)));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildCards(), 0, 0);
        body.Controls.Add(_stats, 0, 1);
        body.Controls.Add(BuildProcessCard(), 0, 2);

        Controls.Add(body);
        Controls.Add(BuildHeader());

        _cpu.TempLinkClicked += (_, _) => ShowCpuTempHelp();
        _turbo.ToggleClicked += (_, _) => ToggleTurbo();
        _turbo.PowerModeChanged += (_, _) =>
        {
            if (!TurboControl.SetPowerMode(_turbo.SelectedPowerMode))
                Warn("Couldn't change the power mode", "Windows refused the change. Power modes only apply to the Balanced power plan.");
            _app.Monitor.RefreshNow();
        };
        _procSort.SelectionCommitted += (_, _) => ShowProcesses(_lastGroups);
        _processes.ActionClicked += CloseApp;

        _overlaySwitch.Checked = _app.OverlayEnabled;
        _overlaySwitch.CheckedChanged += (_, _) => _app.SetOverlay(_overlaySwitch.Checked);
        _startupSwitch.Checked = StartupRegistration.IsEnabled;
        _startupSwitch.Toggling += (_, e) => e.Cancel = !SetStartup(!_startupSwitch.Checked);
        _adminButton.Click += (_, _) => _app.RestartElevated();
        _overlaySettings.Click += (_, _) => _app.ShowOverlaySettings();
        _overlaySettings.Tip("Overlay settings: layout, size, opacity, position");
        _overlaySwitch.Tip("White readout in the top-right corner (Ctrl+Shift+O). Hold Ctrl over it to move or close it.");
        _startupSwitch.Tip(Elevation.IsAdministrator
            ? "Starts SysPulse as administrator at sign-in (no UAC prompt), so CPU temperature works."
            : "Starts SysPulse in the tray when you sign in.");
        _adminButton.Tip("CPU temperature needs administrator rights (and the PawnIO driver).");
    }

    public void SetOverlaySwitch(bool on) => _overlaySwitch.Checked = on;

    // ---- layout ------------------------------------------------------------
    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(62), BackColor = Theme.Window };
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(0, Theme.S(4)), BackColor = Theme.Window };
        titles.Controls.Add(new Label { Text = "SysPulse", AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Margin = Padding.Empty });
        titles.Controls.Add(_subtitle);
        _subtitle.Text = "Starting sensors…";

        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Window,
            Padding = Theme.S(0, 10, 0, 0),
        };
        right.Controls.Add(_startupSwitch);
        right.Controls.Add(_overlaySettings);
        right.Controls.Add(_overlaySwitch);
        if (Elevation.IsAdministrator)
            right.Controls.Add(_adminChip);
        else
            right.Controls.Add(_adminButton);

        header.Controls.Add(titles);
        header.Controls.Add(right);
        return header;
    }

    private Control BuildCards()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Theme.Window, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cards.Controls.Add(_cpu, 0, 0);
        cards.Controls.Add(_gpu, 1, 0);
        cards.Controls.Add(_memory, 2, 0);
        cards.Controls.Add(_turbo, 3, 0);
        return cards;
    }

    private Control BuildProcessCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = Theme.S(0, 0, 0, 12), Padding = Theme.S(16, 12, 6, 8) };
        var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(52), BackColor = Theme.Surface };
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Location = Point.Empty };
        titles.Controls.Add(new Label { Text = "Free up memory and CPU", AutoSize = true, Font = Theme.Section, ForeColor = Theme.Text, Margin = Padding.Empty });
        titles.Controls.Add(new Label
        {
            Text = "Open apps close normally and can ask to save. Background apps are ended. Windows and system processes are never listed.",
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.TextSubtle,
            Margin = Theme.S(0, 3, 0, 0),
        });
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Padding = Theme.S(0, 2, 10, 0) };
        _procSort.Size = _procSort.GetPreferredSize(Size.Empty);
        right.Controls.Add(_procSort);
        right.Controls.Add(_procSummary);
        head.Controls.Add(titles);
        head.Controls.Add(right);

        card.Controls.Add(_processes);
        card.Controls.Add(head);
        return card;
    }

    // ---- live data ---------------------------------------------------------
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.UseDarkTitleBar(Handle);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateScanState();
        if (Visible && _app.Last is { } last)
            UpdateSnapshot(last.Snapshot, last.History);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScanState();
    }

    private void UpdateScanState() =>
        _app.Monitor.ScanProcesses = Visible && WindowState != FormWindowState.Minimized;

    public void UpdateSnapshot(Snapshot s, MonitorHistory h)
    {
        _snapshot = s;
        _subtitle.Text = $"{s.Cpu.Name}  ·  {s.Gpu.Name}  ·  {Ui.Bytes(s.Memory.Total)} RAM";

        // CPU
        _cpu.Subtitle = s.Cpu.Name;
        _cpu.Value = s.Cpu.Usage is double cu ? $"{cu:0}" : "—";
        _cpu.ValueUnit = "%";
        _cpu.Temperature = s.Cpu.TempC;
        var cpuDetails = new List<(string, string)>
        {
            ("Clock", s.Cpu.ClockGhz is double ghz ? $"{ghz:0.00} GHz" : "—"),
            ("Base", s.Cpu.BaseGhz > 0 ? $"{s.Cpu.BaseGhz:0.0} GHz" : "—"),
            ("Threads", Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture)),
            ("Power", s.Cpu.PowerW is double cw ? $"{cw:0} W" : "—"),
        };
        _cpu.Details = [.. cpuDetails];
        _cpu.SetHistory(h.Cpu);

        // GPU
        _gpu.Subtitle = s.Gpu.Name;
        _gpu.Notice = !s.Gpu.Present ? "No GPU sensors found" : s.Gpu.Sleeping ? "Sleeping — saving power" : null;
        _gpu.Value = s.Gpu.Sleeping ? "0" : s.Gpu.Usage is double gu ? $"{gu:0}" : "—";
        _gpu.ValueUnit = "%";
        _gpu.Temperature = s.Gpu.Sleeping ? null : s.Gpu.TempC;
        _gpu.TempCaption = "temperature";
        _gpu.Details =
        [
            ("VRAM", s.Gpu.VramUsed is long vu && s.Gpu.VramTotal is long vt ? $"{Gb(vu)} / {Gb(vt)} GB" : "—"),
            ("Power", s.Gpu.PowerW is double gw ? $"{gw:0} W" : "—"),
            ("Clock", s.Gpu.ClockMhz is int mhz ? $"{mhz:N0} MHz" : "—"),
            ("State", !s.Gpu.Present ? "—" : s.Gpu.Sleeping ? "Off (idle)" : "Active"),
        ];
        _gpu.SetHistory(h.Gpu);

        // Memory
        var m = s.Memory;
        _memory.Subtitle = $"{Ui.Bytes(m.Total)} installed";
        _memory.Value = m.Total > 0 ? $"{m.Used * 100.0 / m.Total:0}" : "—";
        _memory.ValueUnit = "%";
        _memory.ValueCaption = $"{Ui.Bytes(m.Used)} of {Ui.Bytes(m.Total)} in use";
        var top = _lastGroups.OrderByDescending(g => g.Memory).FirstOrDefault();
        _memory.Details =
        [
            ("In use", Ui.Bytes(m.Used)),
            ("Free", Ui.Bytes(m.Total - m.Used)),
            ("Commit", $"{Gb(m.CommitUsed)} / {Gb(m.CommitLimit)} GB"),
            ("Top", top is null ? "—" : top.Name),
        ];
        _memory.SetHistory(h.Memory);

        _turbo.Update(s.Turbo, s.Cpu, s.Power.Mode);

        // Strip
        var d = s.Disk;
        string battery = s.Power.BatteryPercent is int bp
            ? $"{bp}%{(s.Power.Charging ? " · charging" : "")}"
            : "No battery";
        _stats.SetTiles(
            new StatStrip.Tile(Glyph.Drive, $"Disk {d.Drive}", d.Total > 0 ? $"{Ui.Bytes(d.Free)} free" : "—",
                $"of {Ui.Bytes(d.Total)} · {(d.ActivePercent is double a ? $"{a:0}% active" : "idle")}"),
            new StatStrip.Tile(Glyph.Network, "Network", $"↓ {Ui.Rate(s.Network.DownBps)}", $"↑ {Ui.Rate(s.Network.UpBps)}"),
            new StatStrip.Tile(s.Power.OnBattery ? Glyph.Battery : Glyph.Plug, "Power", s.Power.OnBattery ? "On battery" : "Plugged in", battery),
            new StatStrip.Tile(Glyph.Speed, "Power plan", s.Power.PlanName ?? "—", s.Power.Mode is PowerMode pm ? ModeLabel(pm) : ""),
            new StatStrip.Tile(Glyph.Clock, "Up time", Ui.Uptime(s.Uptime), $"since {DateTime.Now - s.Uptime:ddd HH:mm}"));

        _cpu.Invalidate();
        _gpu.Invalidate();
        _memory.Invalidate();
    }

    private static string ModeLabel(PowerMode m) => m switch
    {
        PowerMode.BestEfficiency => "Best power efficiency",
        PowerMode.BestPerformance => "Best performance",
        _ => "Balanced mode",
    };

    private static string Gb(long bytes) => (bytes / 1073741824.0).ToString(bytes >= 10L << 30 ? "0" : "0.0", CultureInfo.CurrentCulture);

    public void UpdateProcesses(List<AppGroup> groups)
    {
        _lastGroups = groups;
        ShowProcesses(groups);
    }

    private void ShowProcesses(List<AppGroup> groups)
    {
        var sorted = _procSort.SelectedIndex == 1
            ? groups.OrderByDescending(g => g.Cpu).ThenByDescending(g => g.Memory)
            : groups.OrderByDescending(g => g.Memory);
        var list = sorted.Take(40).ToList();
        _processes.EmptyText = "Nothing worth closing right now.";
        _processes.SetItems(list);

        long background = groups.Where(g => g.Kind == AppKind.Background).Sum(g => g.Memory);
        int bgCount = groups.Count(g => g.Kind == AppKind.Background);
        _procSummary.Text = bgCount == 0 ? "" : $"{bgCount} background apps use {Ui.Bytes(background)}";
    }

    // ---- actions -----------------------------------------------------------
    private void ToggleTurbo()
    {
        if (_snapshot is null)
            return;
        bool on = !_snapshot.Turbo.Enabled;
        _turbo.Busy = true;
        string? err = _app.SetTurbo(on);
        _turbo.Busy = false;
        if (err is not null)
            Warn("Couldn't change turbo boost", err);
    }

    private bool SetStartup(bool on)
    {
        string? err = on ? StartupRegistration.Enable() : StartupRegistration.Disable();
        if (err is not null)
        {
            Warn(on ? "Couldn't turn on Start with Windows" : "Couldn't turn off Start with Windows", err);
            return false;
        }
        return true;
    }

    private void CloseApp(AppGroup app)
    {
        bool open = app.Kind == AppKind.OpenApp;
        string procs = app.Pids.Count == 1 ? "1 process" : $"{app.Pids.Count} processes";
        var page = new TaskDialogPage
        {
            Caption = "SysPulse",
            Heading = open ? $"Close {app.Name}?" : $"End {app.Name}?",
            Text = open
                ? $"It will close normally, like clicking its X, so it can still ask you to save your work. Frees about {Ui.Bytes(app.Memory)}."
                : $"Ends {procs} running in the background ({Ui.Bytes(app.Memory)}). Unsaved changes in it are lost. It may start again when you open it or sign in.",
            Icon = open ? TaskDialogIcon.Information : TaskDialogIcon.Warning,
            Buttons = { new TaskDialogButton(open ? "Close" : "End"), TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };
        if (TaskDialog.ShowDialog(this, page) == TaskDialogButton.Cancel)
            return;

        if (open)
        {
            ProcessAdvisor.CloseWindows(app);
        }
        else
        {
            int failed = ProcessAdvisor.EndProcesses(app);
            if (failed > 0)
                Warn($"Couldn't end all of {app.Name}",
                    $"{failed} of {procs} refused to end. They may be protected or running as administrator" +
                    (Elevation.IsAdministrator ? "." : " — try again after Run as administrator."));
        }
        _processes.MarkPending(app);
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            _app.Monitor.RefreshNow(processes: true);
        };
        timer.Start();
    }

    private void ShowCpuTempHelp()
    {
        var status = _snapshot?.Cpu.TempStatus ?? CpuTempStatus.NeedsAdminAndPawnIo;
        var install = new TaskDialogCommandLinkButton("Install PawnIO", "Opens a terminal running: winget install namazso.PawnIO");
        var site = new TaskDialogCommandLinkButton("Open pawnio.eu", "The driver's official site, with a manual installer");
        var restart = Elevation.IsAdministrator
            ? new TaskDialogCommandLinkButton("Restart SysPulse", "Do this after PawnIO is installed")
            : new TaskDialogCommandLinkButton("Restart as administrator", "SysPulse restarts with a UAC prompt");

        var page = new TaskDialogPage
        {
            Caption = "SysPulse",
            Heading = "Show CPU temperature",
            Icon = TaskDialogIcon.Information,
            Footnote = new TaskDialogFootnote("Tip: turn on \"Start with Windows\" while running as administrator and SysPulse starts with CPU temperature at every sign-in, without a UAC prompt."),
        };
        const string why = "Windows doesn't let regular apps read the CPU's temperature sensor. SysPulse reads it through PawnIO, a free, signed driver also used by LibreHardwareMonitor and FanControl, while running as administrator.";
        switch (status)
        {
            case CpuTempStatus.NeedsAdmin:
                page.Text = why + "\n\nPawnIO is installed — SysPulse just needs to run as administrator.";
                page.Buttons.Add(restart);
                break;
            case CpuTempStatus.NeedsPawnIo:
                page.Text = why + "\n\nSysPulse is running as administrator. Install PawnIO, then restart SysPulse.";
                page.Buttons.Add(install);
                page.Buttons.Add(site);
                page.Buttons.Add(restart);
                break;
            case CpuTempStatus.Failed:
                page.Text = "PawnIO is installed and SysPulse is running as administrator, but the CPU sensor didn't answer:\n\n" +
                            (_app.Monitor.CpuSensorError ?? "Unknown error") + "\n\nUpdating PawnIO or restarting Windows usually fixes this.";
                page.Icon = TaskDialogIcon.Warning;
                page.Buttons.Add(site);
                break;
            default:
                page.Text = why + "\n\n1. Install PawnIO (one time).\n2. Restart SysPulse as administrator.";
                page.Buttons.Add(install);
                page.Buttons.Add(site);
                page.Buttons.Add(restart);
                break;
        }
        page.Buttons.Add(TaskDialogButton.Close);

        var result = TaskDialog.ShowDialog(this, page);
        if (result == install)
            StartProcess("cmd.exe", "/k winget install --id namazso.PawnIO -e && echo. && echo Done. Close this window and restart SysPulse as administrator.");
        else if (result == site)
            StartProcess("https://pawnio.eu", null);
        else if (result == restart)
            _app.RestartElevated();
    }

    private void StartProcess(string file, string? args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Warn("Couldn't start it", ex.Message);
        }
    }

    private void Warn(string heading, string text) =>
        TaskDialog.ShowDialog(this, new TaskDialogPage
        {
            Caption = "SysPulse",
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
            Buttons = { TaskDialogButton.OK },
        });

    // ---- window ------------------------------------------------------------
    private void LoadSavedBounds()
    {
        if (AppSettings.Get("window") is not string saved)
            return;
        var parts = saved.Split(',').Select(p => int.TryParse(p, out int v) ? v : (int?)null).ToArray();
        if (parts.Length != 4 || parts.Any(p => p is null))
            return;
        var r = new Rectangle(parts[0]!.Value, parts[1]!.Value, parts[2]!.Value, parts[3]!.Value);
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (!_app.Headless)
            AppSettings.Set("window", $"{r.X},{r.Y},{r.Width},{r.Height}");
        if (e.CloseReason == CloseReason.UserClosing && !_app.Exiting)
        {
            // Closing the window keeps SysPulse running in the tray (overlay, turbo, hotkey).
            e.Cancel = true;
            Hide();
            _app.NotifyStillRunning();
        }
        base.OnFormClosing(e);
    }
}
