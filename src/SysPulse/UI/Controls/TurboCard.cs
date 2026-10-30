using System.Drawing.Drawing2D;
using SysPulse.Monitoring;

namespace SysPulse.UI.Controls;

/// <summary>Turbo boost state with an on/off button, live clock, and the Windows power mode.</summary>
internal sealed class TurboCard : Control
{
    private readonly ModernButton _toggle = new("Turn turbo on", ButtonKind.Primary, Glyph.Rocket) { AutoSize = false, BaseHeight = 36 };
    private readonly Segmented _mode = new("Efficiency", "Balanced", "Performance");
    private TurboReading? _turbo;
    private CpuReading? _cpu;

    public TurboCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Margin = Theme.S(0, 0, 0, 12);
        _toggle.Click += (_, _) => ToggleClicked?.Invoke(this, EventArgs.Empty);
        _mode.SelectionCommitted += (_, _) => PowerModeChanged?.Invoke(this, EventArgs.Empty);
        _mode.Tip("Windows power mode (Settings › System › Power). Overlays the power plan.");
        Controls.Add(_toggle);
        Controls.Add(_mode);
    }

    public event EventHandler? ToggleClicked;
    public event EventHandler? PowerModeChanged;

    public PowerMode SelectedPowerMode => (PowerMode)_mode.SelectedIndex;

    public bool Busy
    {
        set => _toggle.Enabled = !value;
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Theme.S(250), Theme.S(260));

    public void Update(TurboReading turbo, CpuReading cpu, PowerMode? mode)
    {
        _turbo = turbo;
        _cpu = cpu;
        _toggle.Text = turbo.Enabled ? "Turn turbo off" : "Turn turbo on";
        _toggle.Kind = turbo.Enabled ? ButtonKind.Secondary : ButtonKind.Primary;
        _toggle.Glyph = turbo.Enabled ? Glyph.Leaf : Glyph.Rocket;
        _toggle.Enabled = turbo.Supported;
        _mode.Visible = mode is not null;
        if (mode is PowerMode m)
            _mode.SelectedIndex = (int)m;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Theme.S(16), w = Width - pad * 2;
        _mode.Bounds = new Rectangle(pad, Height - Theme.S(14) - Theme.S(28), w, Theme.S(28));
        _toggle.Bounds = new Rectangle(pad, _mode.Top - Theme.S(30) - Theme.S(36), w, Theme.S(36));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
        using (var brush = new SolidBrush(Theme.Surface))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        int pad = Theme.S(16), x = pad, w = Width - pad * 2, y = Theme.S(14);
        Theme.DrawGlyph(g, Glyph.Rocket, 10f, Theme.TextMuted, new Rectangle(x, y, Theme.S(18), Theme.S(20)));
        Theme.DrawText(g, "Turbo boost", Theme.Section, Theme.Text, new Rectangle(x + Theme.S(24), y, w, Theme.S(20)));
        y += Theme.S(20);
        Theme.DrawText(g, "CPU boost above base clock", Theme.Small, Theme.TextSubtle, new Rectangle(x, y, w, Theme.S(18)));
        y += Theme.S(24);

        if (_turbo is null)
            return;

        // Hero: ON / OFF with a pill showing the active boost mode.
        string state = !_turbo.Supported ? "N/A" : _turbo.Enabled ? "ON" : "OFF";
        int sw = Theme.TextWidth(state, Theme.Hero);
        Theme.DrawText(g, state, Theme.Hero, Theme.Text, new Rectangle(x - Theme.S(2), y, sw + 4, Theme.S(40)));
        if (_turbo.Supported)
        {
            string pill = TurboReading.ModeName(_turbo.Current);
            int pw = Theme.TextWidth(pill, Theme.SmallBold) + Theme.S(18);
            var pr = new Rectangle(x + sw + Theme.S(12), y + Theme.S(11), pw, Theme.S(22));
            using var pillPath = Theme.RoundedRect(pr, Theme.S(11));
            using var pillBrush = new SolidBrush(_turbo.Enabled ? Theme.AccentSoft : Theme.SurfaceAlt);
            using var pillPen = new Pen(_turbo.Enabled ? Theme.Accent : Theme.BorderStrong);
            g.FillPath(pillBrush, pillPath);
            g.DrawPath(pillPen, pillPath);
            Theme.DrawText(g, pill, Theme.SmallBold, Theme.Text, pr, Theme.LeftLine | TextFormatFlags.HorizontalCenter);
        }
        y += Theme.S(40);

        string clock = _cpu?.ClockGhz is double ghz ? $"Running at {ghz:0.00} GHz" : "Running";
        string baseClock = _cpu is { BaseGhz: > 0 } c ? $" · base {c.BaseGhz:0.0} GHz" : "";
        Theme.DrawText(g, clock + baseClock, Theme.Small, Theme.TextMuted, new Rectangle(x, y, w, Theme.S(18)));
        y += Theme.S(18);
        string sources = _turbo.Ac == _turbo.Dc
            ? $"Plugged in and on battery: {TurboReading.ModeName(_turbo.Ac).ToLowerInvariant()}"
            : $"Plugged in: {TurboReading.ModeName(_turbo.Ac).ToLowerInvariant()} · Battery: {TurboReading.ModeName(_turbo.Dc).ToLowerInvariant()}";
        Theme.DrawText(g, sources, Theme.Small, Theme.TextSubtle, new Rectangle(x, y, w, Theme.S(18)));

        if (_mode.Visible)
            Theme.DrawText(g, "Power mode", Theme.Label, Theme.TextSubtle, new Rectangle(x, _mode.Top - Theme.S(22), w, Theme.S(18)));
    }
}
