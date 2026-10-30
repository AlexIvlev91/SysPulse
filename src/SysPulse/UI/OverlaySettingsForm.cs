using SysPulse.Native;
using SysPulse.UI.Controls;

namespace SysPulse.UI;

/// <summary>Overlay layout, size, opacity, backplate and position. Every change applies live.</summary>
internal sealed class OverlaySettingsForm : Form
{
    private const int NudgeStep = 4;

    private readonly OverlayForm _overlay;
    private readonly Segmented _layout = new("Stacked", "One line");
    private readonly Segmented _size = new("Small", "Medium", "Large");
    private readonly Slider _opacity = new(OverlayOptions.MinOpacity, 100, 5);
    private readonly Label _opacityValue = new() { AutoSize = false, Font = Theme.BodyBold, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight };
    private readonly Segmented _background = new("None", "Dark");
    private readonly AnchorPicker _anchor = new();
    private readonly Label _offsets = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextSubtle, Margin = Theme.S(0, 6, 0, 0) };

    public OverlaySettingsForm(OverlayForm overlay)
    {
        _overlay = overlay;
        Text = "Overlay settings";
        Icon = TrayIcons.AppIcon;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Padding = Theme.S(20, 16, 20, 16);
        ClientSize = Theme.S(460, 452);

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, BackColor = Theme.Surface };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(110)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "Layout", Fit(_layout, 220));
        AddRow(grid, "Size", Fit(_size, 260));
        AddRow(grid, "Opacity", OpacityRow());
        AddRow(grid, "Background", Fit(_background, 160));
        AddRow(grid, "Position", PositionRow());

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(40),
            Font = Theme.Small,
            ForeColor = Theme.TextSubtle,
            Text = "Arrow keys move it 1 px (Shift: 10 px). You can also hold Ctrl over the overlay to drag it or close it; Ctrl+Shift+O shows or hides it.",
        };
        var done = new ModernButton("Done", ButtonKind.Primary) { Anchor = AnchorStyles.Right };
        done.Click += (_, _) => Close();
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(40), BackColor = Theme.Surface };
        done.Margin = Padding.Empty;
        footer.Controls.Add(done);
        AcceptButton = done;
        CancelButton = done;

        Controls.Add(grid);
        Controls.Add(hint);
        Controls.Add(footer);

        LoadFrom(_overlay.Options);
        _layout.SelectionCommitted += (_, _) => Apply(o => o with { Layout = (OverlayLayout)_layout.SelectedIndex });
        _size.SelectionCommitted += (_, _) => Apply(o => o with { Size = (OverlaySize)_size.SelectedIndex });
        _opacity.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = $"{_opacity.Value}%";
            Apply(o => o with { Opacity = _opacity.Value });
        };
        _background.SelectionCommitted += (_, _) => Apply(o => o with { Background = _background.SelectedIndex == 1 });
        _anchor.SelectionCommitted += (_, _) =>
        {
            _overlay.SetAnchor(_anchor.Selected);
            ShowOffsets();
        };
        _overlay.OptionsChanged += OnOverlayMoved;
    }

    private void Apply(Func<OverlayOptions, OverlayOptions> change) => _overlay.Options = change(_overlay.Options);

    private void OnOverlayMoved(object? sender, EventArgs e)
    {
        _anchor.Selected = _overlay.Options.Anchor;
        ShowOffsets();
    }

    private void LoadFrom(OverlayOptions o)
    {
        _layout.SelectedIndex = (int)o.Layout;
        _size.SelectedIndex = (int)o.Size;
        _opacity.Value = o.Opacity;
        _opacityValue.Text = $"{o.Opacity}%";
        _background.SelectedIndex = o.Background ? 1 : 0;
        _anchor.Selected = o.Anchor;
        ShowOffsets();
    }

    private void ShowOffsets()
    {
        var o = _overlay.Options;
        int col = (int)o.Anchor % 3, row = (int)o.Anchor / 3;
        string h = col switch { 0 => $"{o.OffsetX} px from left", 1 => $"{Signed(o.OffsetX)} px from center", _ => $"{o.OffsetX} px from right" };
        string v = row switch { 0 => $"{o.OffsetY} px from top", 1 => $"{Signed(o.OffsetY)} px from middle", _ => $"{o.OffsetY} px from bottom" };
        _offsets.Text = $"{h} · {v}";
    }

    private static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString(System.Globalization.CultureInfo.CurrentCulture);

    // ---- rows --------------------------------------------------------------
    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = Theme.BodyBold,
            ForeColor = Theme.TextMuted,
            Margin = Theme.S(0, 7, 0, 14),
        }, 0, row);
        control.Margin = Theme.S(0, 0, 0, 14);
        grid.Controls.Add(control, 1, row);
    }

    private static Control Fit(Control c, int width)
    {
        c.Size = new Size(Theme.S(width), Theme.S(30));
        return c;
    }

    private Control OpacityRow()
    {
        var panel = new Panel { Size = Theme.S(300, 30), BackColor = Theme.Surface };
        _opacity.Bounds = new Rectangle(0, Theme.S(1), Theme.S(240), Theme.S(28));
        _opacityValue.Bounds = new Rectangle(Theme.S(248), 0, Theme.S(52), Theme.S(30));
        panel.Controls.Add(_opacity);
        panel.Controls.Add(_opacityValue);
        return panel;
    }

    private Control PositionRow()
    {
        var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        var top = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = Padding.Empty };
        _anchor.Margin = Theme.S(0, 0, 18, 0);
        _anchor.Tip("Which corner or edge the overlay sticks to");
        top.Controls.Add(_anchor);
        top.Controls.Add(NudgePad());
        column.Controls.Add(top);
        column.Controls.Add(_offsets);

        var reset = new ModernButton("Reset position", ButtonKind.Ghost) { Margin = Theme.S(-8, 4, 0, 0) };
        reset.Click += (_, _) => _overlay.ResetPosition();
        column.Controls.Add(reset);
        return column;
    }

    /// <summary>Up / left / down / right buttons; hold one to keep moving.</summary>
    private Control NudgePad()
    {
        int b = Theme.S(34), gap = Theme.S(4);
        var pad = new Panel { Size = new Size(b * 3 + gap * 2, b * 2 + gap), BackColor = Theme.Surface, Margin = Theme.S(0, 2, 0, 0) };
        pad.Controls.Add(NudgeButton("", 0, -1, "Move up", new Point(b + gap, 0)));
        pad.Controls.Add(NudgeButton("", -1, 0, "Move left", new Point(0, b + gap)));
        pad.Controls.Add(NudgeButton("", 0, 1, "Move down", new Point(b + gap, b + gap)));
        pad.Controls.Add(NudgeButton("", 1, 0, "Move right", new Point((b + gap) * 2, b + gap)));
        return pad;
    }

    private ModernButton NudgeButton(string glyph, int dx, int dy, string tip, Point location)
    {
        var button = new ModernButton("", ButtonKind.Secondary, glyph) { AutoSize = false, BaseHeight = 34, Location = location, Size = Theme.S(34, 34), TabStop = false };
        button.Tip(tip);
        var repeat = new System.Windows.Forms.Timer { Interval = 350 };
        void Step() => _overlay.Nudge(dx * NudgeStep, dy * NudgeStep);
        repeat.Tick += (_, _) =>
        {
            repeat.Interval = 40;
            Step();
        };
        button.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            Step();
            repeat.Interval = 350;
            repeat.Start();
        };
        button.MouseUp += (_, _) => repeat.Stop();
        button.MouseLeave += (_, _) => repeat.Stop();
        button.Disposed += (_, _) => repeat.Dispose();
        return button;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Arrow keys move the overlay (the slider keeps them for itself).
        if (ActiveControl is not Slider)
        {
            int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
            (int dx, int dy) = (keyData & Keys.KeyCode) switch
            {
                Keys.Left => (-step, 0),
                Keys.Right => (step, 0),
                Keys.Up => (0, -step),
                Keys.Down => (0, step),
                _ => (0, 0),
            };
            if (dx != 0 || dy != 0)
            {
                _overlay.Nudge(dx, dy);
                return true;
            }
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.UseDarkTitleBar(Handle);
    }

    public void PlaceNear(Form? owner)
    {
        var area = owner is { Visible: true } ? owner.Bounds : (Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _overlay.OptionsChanged -= OnOverlayMoved;
        base.OnFormClosed(e);
    }
}
