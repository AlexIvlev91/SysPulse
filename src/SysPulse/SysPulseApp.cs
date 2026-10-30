using SysPulse.Monitoring;
using SysPulse.Native;
using SysPulse.UI;

namespace SysPulse;

/// <summary>
/// Owns everything that lives for the whole session: the sensor thread, tray
/// icon, overlay, global hotkey, and the main window (hidden to the tray on close).
/// </summary>
internal sealed class SysPulseApp : ApplicationContext
{
    private readonly WindowsFormsSynchronizationContext _ui = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _turboItem;
    private readonly OverlayForm _overlay = new();
    private readonly HotkeyWindow _hotkey;
    private MainForm? _main;
    private OverlaySettingsForm? _overlaySettings;
    private readonly bool _headless;
    private bool? _trayTurbo;
    private Icon? _trayIcon;

    /// <param name="headless">For --snapshot: no tray icon, overlay window, hotkey or activation listener.</param>
    public SysPulseApp(bool startInTray, bool headless = false)
    {
        _headless = headless;
        Monitor = new SystemMonitor();
        Monitor.Updated += (s, h) => _ui.Post(_ => OnSnapshot(s, h), null);
        Monitor.ProcessesUpdated += groups => _ui.Post(_ => _main?.UpdateProcesses(groups), null);

        _overlayItem = new ToolStripMenuItem("Show overlay", null, (_, _) => SetOverlay(!OverlayEnabled)) { ShortcutKeyDisplayString = "Ctrl+Shift+O" };
        _turboItem = new ToolStripMenuItem("Turbo boost", null, (_, _) => SetTurbo(!(Last?.Snapshot.Turbo.Enabled ?? false)));
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, Font = Theme.Body };
        menu.Items.Add(new ToolStripMenuItem("Open SysPulse", null, (_, _) => ShowMain()) { Font = Theme.BodyBold });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(_turboItem);
        menu.Items.Add(new ToolStripMenuItem("Overlay settings…", null, (_, _) => ShowOverlaySettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
        menu.Opening += (_, _) =>
        {
            _overlayItem.Checked = OverlayEnabled;
            _turboItem.Checked = Last?.Snapshot.Turbo.Enabled ?? false;
            _turboItem.Enabled = Last?.Snapshot.Turbo.Supported ?? false;
        };

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.AppIcon,
            Text = "SysPulse",
            ContextMenuStrip = menu,
            Visible = !headless,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowMain();
        };

        _overlay.Dismissed += (_, _) => SetOverlay(false);
        _hotkey = new HotkeyWindow(headless ? null : () => SetOverlay(!OverlayEnabled));
        if (!headless)
        {
            if (OverlayEnabled)
                _overlay.Show();
            SingleInstance.ListenForActivation(() => _ui.Post(_ => ShowMain(), null));
            if (AppSettings.GetBool("startup", false))
                StartupRegistration.RepairPath();
        }

        if (!startInTray)
            ShowMain();
    }

    public SystemMonitor Monitor { get; }

    public MainForm? Main => _main;

    public bool Headless => _headless;

    public OverlayForm Overlay => _overlay;

    public (Snapshot Snapshot, MonitorHistory History)? Last { get; private set; }

    public bool Exiting { get; private set; }

    public bool OverlayEnabled => AppSettings.GetBool("overlay", true);

    private void OnSnapshot(Snapshot s, MonitorHistory h)
    {
        if (Exiting)
            return;
        Last = (s, h);
        _overlay.UpdateSnapshot(s);
        if (_main is { Visible: true, WindowState: not FormWindowState.Minimized })
            _main.UpdateSnapshot(s, h);
        UpdateTray(s);
    }

    private void UpdateTray(Snapshot s)
    {
        if (_trayTurbo != s.Turbo.Enabled)
        {
            _trayTurbo = s.Turbo.Enabled;
            var old = _trayIcon;
            _tray.Icon = _trayIcon = TrayIcons.Create(s.Turbo.Enabled, SystemInformation.SmallIconSize.Width * 2);
            old?.Dispose();
        }
        string cpuTemp = s.Cpu.TempC is double ct ? $" {ct:0}°C" : "";
        string gpu = s.Gpu.Sleeping ? "sleeping" : $"{s.Gpu.Usage ?? 0:0}%{(s.Gpu.TempC is double gt ? $" {gt:0}°C" : "")}";
        string text = $"SysPulse\nCPU {s.Cpu.Usage ?? 0:0}%{cpuTemp} · GPU {gpu}\nRAM {s.Memory.LoadPercent}% · Turbo {(s.Turbo.Enabled ? "on" : "off")}";
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    public void ShowMain()
    {
        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(this);
            if (_headless)
            {
                _main.StartPosition = FormStartPosition.Manual;
                _main.Location = new Point(-20000, -20000);
                _main.ShowInTaskbar = false;
            }
        }
        if (!_main.Visible)
            _main.Show();
        if (_main.WindowState == FormWindowState.Minimized)
            _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    /// <summary>Layout, size, opacity and position; turns the overlay on so changes are visible.</summary>
    public void ShowOverlaySettings()
    {
        if (!OverlayEnabled)
            SetOverlay(true);
        if (_overlaySettings is { IsDisposed: false })
        {
            _overlaySettings.Activate();
            return;
        }
        var owner = _main is { Visible: true } ? _main : null;
        _overlaySettings = new OverlaySettingsForm(_overlay);
        _overlaySettings.PlaceNear(owner);
        _overlaySettings.Show(owner);
    }

    public void SetOverlay(bool on)
    {
        AppSettings.Set("overlay", on);
        if (on && !_overlay.Visible)
            _overlay.Show();
        else if (!on && _overlay.Visible)
            _overlay.Hide();
        _main?.SetOverlaySwitch(on);
    }

    /// <summary>Returns null on success, or an error message.</summary>
    public string? SetTurbo(bool on)
    {
        string? err = TurboControl.Set(on);
        Monitor.RefreshNow();
        if (err is not null)
            _tray.ShowBalloonTip(4000, "Turbo boost", err, ToolTipIcon.Warning);
        return err;
    }

    public void NotifyStillRunning()
    {
        if (AppSettings.GetBool("tray.hintShown", false))
            return;
        AppSettings.Set("tray.hintShown", true);
        _tray.ShowBalloonTip(5000, "SysPulse is still running",
            "The overlay and turbo switch stay available from the tray icon. Right-click it to exit.", ToolTipIcon.Info);
    }

    public void RestartElevated()
    {
        if (Elevation.TryRestartElevated(showWindow: true))
            ExitApp();
    }

    public void ExitApp()
    {
        if (Exiting)
            return;
        Exiting = true;
        _tray.Visible = false;
        _overlaySettings?.Close();
        _main?.Close();
        _overlay.Close();
        _hotkey.Dispose();
        Monitor.Dispose();
        _tray.Dispose();
        if (!_headless)
            SingleInstance.Release();
        ExitThread();
    }

    /// <summary>Receives the global Ctrl+Shift+O hotkey.</summary>
    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int Id = 1;
        private readonly Action? _onHotkey;

        public HotkeyWindow(Action? onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
            if (onHotkey is not null)
                Win32.RegisterHotKey(Handle, Id, Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, (uint)Keys.O);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY && (int)m.WParam == Id)
                _onHotkey?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Win32.UnregisterHotKey(Handle, Id);
            DestroyHandle();
        }
    }
}
