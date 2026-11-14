using System.Drawing.Drawing2D;
using System.Globalization;

namespace UltraLightRgb.Switch;

/// <summary>
/// Look of the switcher: black ground, grouped surfaces and system colours in the manner of a dark
/// macOS / keynote slide, Segoe UI Variable for crisp text. One accent colour, used sparingly.
/// </summary>
internal static class Ui
{
    public static readonly Color Bg = Color.FromArgb(0, 0, 0);
    public static readonly Color Group = Color.FromArgb(28, 28, 30);
    public static readonly Color Raised = Color.FromArgb(44, 44, 46);
    public static readonly Color Hover = Color.FromArgb(36, 36, 38);
    public static readonly Color Separator = Color.FromArgb(56, 56, 58);
    public static readonly Color Text = Color.FromArgb(245, 245, 247);
    public static readonly Color Secondary = Color.FromArgb(152, 152, 157);
    public static readonly Color Tertiary = Color.FromArgb(99, 99, 102);
    public static readonly Color Blue = Color.FromArgb(10, 132, 255);
    public static readonly Color Green = Color.FromArgb(48, 209, 88);
    public static readonly Color Orange = Color.FromArgb(255, 159, 10);
    public static readonly Color Red = Color.FromArgb(255, 69, 58);

    private const string Display = "Segoe UI Variable Display Semib", TextFace = "Segoe UI Variable Text",
                         TextBold = "Segoe UI Variable Text Semibold";

    public static Font F(float px, string family) => new(family, px, FontStyle.Regular, GraphicsUnit.Pixel);

    public static readonly Font Hero = F(34, Display);
    public static readonly Font Title = F(28, Display);
    public static readonly Font Head = F(17, TextBold);
    public static readonly Font Body = F(15, TextFace);
    public static readonly Font Strong = F(15, TextBold);
    public static readonly Font Small = F(13, TextFace);
    public static readonly Font Time = F(20, Display);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius);
        g.FillPath(b, p);
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        // NoPrefix: an "&" is text here, not a keyboard shortcut marker
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | align switch
        {
            ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleRight => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
            ContentAlignment.TopLeft => TextFormatFlags.Top | TextFormatFlags.WordBreak,
            _ => TextFormatFlags.VerticalCenter,
        };
        TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, flags);
    }

    public static void Hairline(Graphics g, float x0, float x1, float y)
    {
        using var pen = new Pen(Separator, 1);
        g.DrawLine(pen, x0, y + 0.5f, x1, y + 0.5f);
    }
}

/// <summary>Base for the owner-drawn controls: double buffered, transparent, focus ring, accessibility.</summary>
internal abstract class DarkControl : Control
{
    protected bool Hover;

    protected DarkControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Ui.Text;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected void FocusRing(Graphics g, RectangleF r, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        using var pen = new Pen(Color.FromArgb(200, Ui.Blue), 2f);
        using var p = Ui.Round(r, radius);
        g.DrawPath(pen, p);
    }

    // What screen readers and UI automation see: owner-drawn controls have no native behaviour,
    // so each one says what its default action does and, where it has one, exposes its value.
    protected virtual string? AccessibleAction => null;
    protected virtual void DoAccessibleAction() { }
    protected virtual string? AccessibleValue => null;
    protected virtual void SetAccessibleValue(string value) { }

    protected override AccessibleObject CreateAccessibilityInstance() => new DarkAccessible(this);

    private sealed class DarkAccessible(DarkControl owner) : ControlAccessibleObject(owner)
    {
        public override string? DefaultAction => owner.AccessibleAction ?? base.DefaultAction;

        public override void DoDefaultAction()
        {
            if (owner.AccessibleAction is null) base.DoDefaultAction();
            else owner.DoAccessibleAction();
        }

        public override string? Value
        {
            get => owner.AccessibleValue ?? base.Value;
            set
            {
                if (owner.AccessibleValue is null) base.Value = value;
                else owner.SetAccessibleValue(value ?? "");
            }
        }
    }
}

/// <summary>Slider with its value on the right. Arrows, Page keys, Home/End and the wheel work.</summary>
internal sealed class Slider : DarkControl
{
    private const int ValueWidth = 64;
    private int _value;

    public int Minimum { get; init; }
    public int Maximum { get; init; } = 100;
    public int Step { get; init; } = 1;
    public Func<int, string> Format { get; init; } = v => v.ToString(CultureInfo.InvariantCulture);

    public event EventHandler? ValueChanged;

    public Slider()
    {
        TabStop = true;
        Height = 30;
        AccessibleRole = AccessibleRole.Slider;
    }

    public int Value
    {
        get => _value;
        set => SetValue(value, notify: false);
    }

    private void SetValue(int v, bool notify)
    {
        v = Math.Clamp((int)Math.Round(v / (double)Step) * Step, Minimum, Maximum);
        if (v == _value) return;
        _value = v;
        AccessibleDescription = Format(v);
        Invalidate();
        if (notify) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private RectangleF Track => new(11, Height / 2f - 2, Width - ValueWidth - 30, 4);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        var t = Track;
        float f = Maximum == Minimum ? 0 : (_value - Minimum) / (float)(Maximum - Minimum);
        Ui.Fill(g, Ui.Separator, t, 2);
        if (f > 0) Ui.Fill(g, Ui.Blue, new RectangleF(t.X, t.Y, t.Width * f, t.Height), 2);
        float cx = t.X + t.Width * f, cy = Height / 2f;
        Ui.Fill(g, Color.FromArgb(90, 0, 0, 0), new RectangleF(cx - 11, cy - 10, 22, 22), 11);
        Ui.Fill(g, Ui.Text, new RectangleF(cx - 11, cy - 11, 22, 22), 11);
        FocusRing(g, new RectangleF(cx - 14, cy - 14, 28, 28), 14);
        Ui.Draw(g, Format(_value), Ui.Body, Ui.Secondary, new RectangleF(Width - ValueWidth, 0, ValueWidth, Height), ContentAlignment.MiddleRight);
    }

    protected override string? AccessibleValue => _value.ToString(CultureInfo.InvariantCulture);

    protected override void SetAccessibleValue(string value)
    {
        if (int.TryParse(value.TrimEnd('%', ' ', 's', '°', 'C'), out int v)) SetValue(v, notify: true);
    }

    private void SetFromX(int x)
    {
        var t = Track;
        float f = Math.Clamp((x - t.X) / t.Width, 0, 1);
        SetValue(Minimum + (int)Math.Round(f * (Maximum - Minimum)), notify: true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left) SetFromX(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        SetValue(_value + Math.Sign(e.Delta) * Step, notify: true);
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int big = Math.Max(Step, (Maximum - Minimum) / 10);
        int? next = e.KeyCode switch
        {
            Keys.Left or Keys.Down => _value - Step,
            Keys.Right or Keys.Up => _value + Step,
            Keys.PageDown => _value - big,
            Keys.PageUp => _value + big,
            Keys.Home => Minimum,
            Keys.End => Maximum,
            _ => null,
        };
        if (next is { } v)
        {
            SetValue(v, notify: true);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

/// <summary>On/off switch; green when on. Space and Enter toggle.</summary>
internal sealed class Toggle : DarkControl
{
    private bool _on;

    public event EventHandler? Changed;

    public Toggle()
    {
        TabStop = true;
        Size = new Size(46, 28);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public bool On
    {
        get => _on;
        set { _on = value; AccessibleDescription = value ? "on" : "off"; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        var pill = new RectangleF(1, 1, Width - 2, Height - 2);
        Ui.Fill(g, _on ? Ui.Green : Color.FromArgb(57, 57, 61), pill, pill.Height / 2);
        float d = pill.Height - 4, x = _on ? pill.Right - d - 2 : pill.X + 2;
        Ui.Fill(g, Ui.Text, new RectangleF(x, pill.Y + 2, d, d), d / 2);
        FocusRing(g, RectangleF.Inflate(pill, 1, 1), pill.Height / 2 + 1);
    }

    private void Flip()
    {
        On = !_on;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override string? AccessibleAction => _on ? "Switch off" : "Switch on";

    protected override void DoAccessibleAction() => Flip();

    protected override void OnMouseClick(MouseEventArgs e) { Focus(); Flip(); base.OnMouseClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { Flip(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

/// <summary>Time of day as HH:mm. Click the upper or lower half of hours or minutes; arrows change by 15 minutes.</summary>
internal sealed class TimeBox : DarkControl
{
    private int _minutes;

    public event EventHandler? Changed;

    public TimeBox()
    {
        TabStop = true;
        Size = new Size(96, 36);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.SpinButton;
    }

    public int Minutes
    {
        get => _minutes;
        set { _minutes = ((value % 1440) + 1440) % 1440; AccessibleDescription = Display; Invalidate(); }
    }

    public string Display => $"{_minutes / 60:00}:{_minutes % 60:00}";

    private void Change(int delta)
    {
        Minutes = _minutes + delta;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        var box = new RectangleF(0, 0, Width, Height);
        Ui.Fill(g, Hover ? Color.FromArgb(54, 54, 56) : Ui.Raised, box, 8);
        FocusRing(g, RectangleF.Inflate(box, -1, -1), 8);
        Ui.Draw(g, Display, Ui.Time, Enabled ? Ui.Text : Ui.Tertiary, box, ContentAlignment.MiddleCenter);
    }

    protected override string? AccessibleValue => Display;

    protected override void SetAccessibleValue(string value)
    {
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return;
        Minutes = t.Hour * 60 + t.Minute;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // left half: hours, right half: five minutes; upper half up, lower half down
    private int StepAt(int x) => x < Width / 2 ? 60 : 5;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        Focus();
        Change(e.Y < Height / 2 ? StepAt(e.X) : -StepAt(e.X));
        base.OnMouseClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Change(Math.Sign(e.Delta) * StepAt(e.X));
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) { Change(15); e.Handled = true; }
        else if (e.KeyCode == Keys.Down) { Change(-15); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

/// <summary>Entry of the side navigation: plain text, a soft pill when selected.</summary>
internal sealed class NavItem : DarkControl
{
    private bool _active;

    public NavItem()
    {
        TabStop = true;
        Height = 38;
        Cursor = Cursors.Hand;
        // a button rather than a tab: UI automation can press a button without moving the mouse
        AccessibleRole = AccessibleRole.PushButton;
    }

    public bool Active
    {
        get => _active;
        set { _active = value; AccessibleDescription = value ? "current page" : null; Invalidate(); }
    }

    protected override string? AccessibleAction => "Open page";

    protected override void DoAccessibleAction() => OnClick(EventArgs.Empty);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        var r = new RectangleF(14, 1, Width - 28, Height - 2);
        if (_active) Ui.Fill(g, Ui.Raised, r, 8);
        else if (Hover) Ui.Fill(g, Ui.Hover, r, 8);
        FocusRing(g, r, 8);
        Ui.Draw(g, Text, _active ? Ui.Strong : Ui.Body, _active ? Ui.Text : Ui.Secondary, new RectangleF(r.X + 14, r.Y, r.Width - 14, r.Height));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

/// <summary>Grouped surface, like a section in the macOS settings; optional hairlines between rows.</summary>
internal sealed class Group : Panel
{
    /// <summary>Y positions where a separator is drawn, inset from the left.</summary>
    public List<int> Separators { get; } = [];

    public Group()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        Ui.Fill(g, Ui.Group, new RectangleF(0, 0, Width, Height), 14);
        foreach (int y in Separators) Ui.Hairline(g, 20, Width, y);
        base.OnPaint(e);
    }
}

/// <summary>Text on the dark ground: transparent, wraps when asked to.</summary>
internal sealed class Caption : DarkControl
{
    public Font Style { get; set; } = Ui.Body;
    public Color Colour { get; set; } = Ui.Secondary;
    public bool Wrap { get; init; }
    public ContentAlignment Align { get; init; } = ContentAlignment.MiddleLeft;

    public Caption()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e) =>
        Ui.Draw(e.Graphics, Text, Style, Colour, new RectangleF(0, 0, Width, Height), Wrap ? ContentAlignment.TopLeft : Align);
}

/// <summary>Pop-up choice: shows the selection, opens a dark menu with all options.</summary>
internal sealed class Picker : DarkControl
{
    private readonly List<(string Id, string Title)> _items = [];
    private string _selected = "";

    public event EventHandler? Changed;

    public Picker()
    {
        TabStop = true;
        Size = new Size(240, 36);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    public void Add(string id, string title) => _items.Add((id, title));

    public string Selected
    {
        get => _selected;
        set { _selected = value; AccessibleDescription = TitleOf(value); Invalidate(); }
    }

    private string TitleOf(string id) => _items.FirstOrDefault(i => i.Id == id).Title ?? id;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        var box = new RectangleF(0, 0, Width, Height);
        Ui.Fill(g, Hover ? Color.FromArgb(54, 54, 56) : Ui.Raised, box, 8);
        FocusRing(g, RectangleF.Inflate(box, -1, -1), 8);
        Ui.Draw(g, TitleOf(_selected), Ui.Body, Ui.Text, new RectangleF(14, 0, Width - 44, Height));
        // chevron
        using var pen = new Pen(Ui.Secondary, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float cx = Width - 20, cy = Height / 2f;
        g.DrawLines(pen, new PointF[] { new(cx - 4, cy - 2), new(cx, cy + 2), new(cx + 4, cy - 2) });
    }

    private void Open()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Ui.Body };
        foreach (var (id, title) in _items)
        {
            var item = new ToolStripMenuItem(title) { Checked = id == _selected, ForeColor = Ui.Text, Padding = new Padding(4, 6, 4, 6) };
            item.Click += (_, _) => Choose(id);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, new Point(0, Height + 4));
    }

    private void Choose(string id)
    {
        if (id == _selected) return;
        Selected = id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override string? AccessibleValue => TitleOf(_selected);

    protected override void SetAccessibleValue(string value)
    {
        var match = _items.FirstOrDefault(i => i.Title.Equals(value, StringComparison.OrdinalIgnoreCase) || i.Id == value);
        if (match.Title is not null) Choose(match.Id);
    }

    protected override void OnMouseClick(MouseEventArgs e) { Focus(); Open(); base.OnMouseClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.F4) { Open(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private sealed class DarkMenuRenderer() : ToolStripProfessionalRenderer(new Colors())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Ui.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            Ui.Smooth(g);
            var r = e.ImageRectangle;
            using var pen = new Pen(Ui.Blue, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new PointF[] { new(r.X + 3, r.Y + r.Height / 2f), new(r.X + r.Width / 2.5f, r.Bottom - 4), new(r.Right - 2, r.Y + 3) });
        }
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Ui.Group;
        public override Color MenuBorder => Ui.Separator;
        public override Color MenuItemBorder => Ui.Raised;
        public override Color MenuItemSelected => Ui.Raised;
        public override Color ImageMarginGradientBegin => Ui.Group;
        public override Color ImageMarginGradientMiddle => Ui.Group;
        public override Color ImageMarginGradientEnd => Ui.Group;
        public override Color CheckBackground => Ui.Group;
        public override Color CheckSelectedBackground => Ui.Raised;
        public override Color CheckPressedBackground => Ui.Raised;
    }
}

/// <summary>Theme thumbnail with rounded corners; the active one gets a blue ring.</summary>
internal sealed class ThemeTile : DarkControl
{
    private bool _active;

    public required Bitmap Image { get; init; }

    public ThemeTile()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }

    public bool Active
    {
        get => _active;
        set { _active = value; AccessibleDescription = value ? "active" : null; Invalidate(); }
    }

    protected override string? AccessibleAction => "Show theme";

    protected override void DoAccessibleAction() => OnClick(EventArgs.Empty);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        int imgH = Height - 28;
        var img = new RectangleF(3, 3, Width - 6, imgH - 6);
        if (_active) Ui.Fill(g, Ui.Blue, new RectangleF(0, 0, Width, imgH), 11);
        using (var clip = Ui.Round(img, 8))
        {
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Image, img);
            if (Hover && !_active)
                using (var veil = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(veil, img);
            g.ResetClip();
        }
        FocusRing(g, new RectangleF(0, 0, Width, imgH), 11);
        Ui.Draw(g, Text, _active ? Ui.Strong : Ui.Small, _active ? Ui.Text : Ui.Secondary, new RectangleF(3, imgH, Width - 6, 28));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Image.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Shows a bitmap 1:1 with rounded corners; used for the live LCD preview.</summary>
internal sealed class Picture : DarkControl
{
    private Bitmap? _frame;

    public Picture()
    {
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.Graphic;
    }

    public void Show(Bitmap frame)
    {
        _frame?.Dispose();
        _frame = frame;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_frame is null) return;
        var g = e.Graphics;
        Ui.Smooth(g);
        var r = new RectangleF((Width - _frame.Width) / 2f, (Height - _frame.Height) / 2f, _frame.Width, _frame.Height);
        using var clip = Ui.Round(r, 12);
        g.SetClip(clip);
        g.DrawImageUnscaled(_frame, (int)r.X, (int)r.Y);
        g.ResetClip();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frame?.Dispose();
        base.Dispose(disposing);
    }
}
