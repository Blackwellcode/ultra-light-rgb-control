using System.Globalization;
using System.Runtime.InteropServices;

namespace UltraLightRgb.Switch;

/// <summary>
/// The switcher window: themes with a live preview, lighting, display scale, night window and
/// automatic, service status. It writes theme.txt and settings.json, which the service polls; while
/// it is open it touches the alive file so the service writes its status. Nothing stays in memory
/// after it closes.
/// </summary>
internal sealed class MainForm : Form
{
    private const int SideW = 236, Pad = 44, ContentW = 900, ContentH = 744, ContentTop = 128;
    private static readonly Color SideBg = Color.FromArgb(17, 17, 19);

    private readonly SwitcherModel _model = SwitcherModel.Default;
    private readonly List<NavItem> _nav = [];
    private readonly List<Panel> _pages = [];
    private readonly Dictionary<string, ThemeTile> _tiles = [];
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _frame = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _save = new() { Interval = 400 };

    private readonly Caption _sideTemps = new() { Style = Ui.Strong, Colour = Ui.Text };
    private readonly Caption _sideService = new() { Style = Ui.Small };
    private readonly Caption _footer = new() { Style = Ui.Small };

    // themes page
    private readonly Picture _picture = new();
    private readonly Caption _previewTitle = new() { Style = Ui.Title, Colour = Ui.Text };
    private readonly Caption _previewReason = new() { Wrap = true };
    private Renderer? _preview;
    private string _previewTheme = "";
    private bool _previewLive;

    // lighting page
    private readonly Dictionary<string, (Slider Slider, Swatch Swatch)> _levels = [];

    // display page
    private readonly Slider _alarm = new() { Minimum = 70, Maximum = 100, Format = v => $"{v} °C" };
    private readonly Slider _scaleMin = new() { Minimum = 0, Maximum = 60, Format = v => $"{v} °C" };
    private readonly Slider _scaleMax = new() { Minimum = 50, Maximum = 110, Format = v => $"{v} °C" };
    private readonly ScaleBar _scaleBar = new();
    private readonly ChoiceList _pump = new();

    // night & auto page
    private readonly Toggle _nightOn = new();
    private readonly TimeBox _nightFrom = new(), _nightTo = new();
    private readonly Slider _nightLevel = new() { Minimum = 0, Maximum = 100, Step = 5, Format = v => v == 0 ? "Off" : $"{v} %" };
    private readonly Picker _nightTheme = new();
    private readonly Toggle _autoOn = new();
    private readonly Slider _autoLoad = new() { Minimum = 30, Maximum = 100, Format = v => $"{v} %" };
    private readonly Slider _autoHold = new() { Minimum = 5, Maximum = 300, Step = 5, Format = v => $"{v} s" };
    private readonly Picker _autoTheme = new();

    // status page
    private readonly StatusView _statusView = new();

    private bool _loading;

    public MainForm()
    {
        Text = Program.Title;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        Font = Ui.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(SideW + ContentW, ContentH);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        DoubleBuffered = true;

        _model.Load();
        Pal.Configure(_model.Settings);

        BuildSidebar();
        AddPage("Themes", "Pick what the LCD shows. Your LEDs take on its colours.", BuildThemes());
        AddPage("Lighting", "Brightness per group. Your LEDs follow within a second.", BuildLighting());
        AddPage("Display", "Temperature colours of every theme, and the pump sensor.", BuildDisplay());
        AddPage("Night & Auto", "Calmer at night, louder while you play.", BuildNight());
        AddPage("Status", "What the service reports right now.", BuildStatus());
        ShowPage(0);

        _footer.SetBounds(SideW + Pad, ContentH - 34, ContentW - 2 * Pad, 22);
        Controls.Add(_footer);
        _footer.BringToFront();
        if (_model.LoadProblem is { } p) Say($"Saved settings were not usable ({p}). Defaults are shown; a change replaces the file.", Ui.Orange);

        LoadControls();
        _tick.Tick += (_, _) => Tick();
        _frame.Tick += (_, _) => DrawPreview();
        _save.Tick += (_, _) => Save();
        Tick();
        _tick.Start();
        _frame.Start();
    }

    // ---------------------------------------------------------------- title bar

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmUseDarkMode = 20, DwmBorderColor = 34, DwmCaptionColor = 35, DwmTextColor = 36;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // the title bar takes the window colour. Windows before 11 do not know the colour
        // attributes and answer with an error code: they keep their default title bar, which is fine.
        int dark = 1, caption = ColorTranslator.ToWin32(Ui.Bg), text = ColorTranslator.ToWin32(Ui.Text);
        _ = DwmSetWindowAttribute(Handle, DwmUseDarkMode, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, DwmCaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, DwmBorderColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, DwmTextColor, ref text, sizeof(int));
    }

    // ---------------------------------------------------------------- layout

    private void BuildSidebar()
    {
        var side = new Panel { Bounds = new Rectangle(0, 0, SideW, ContentH), BackColor = SideBg };
        var logo = new PictureBox
        {
            Bounds = new Rectangle(28, 34, 34, 34),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon?.ToBitmap(),
            BackColor = SideBg,
            AccessibleName = "logo",
        };
        var name1 = new Caption { Text = "Ultra Light RGB", Style = Ui.Head, Colour = Ui.Text, Bounds = new Rectangle(72, 32, 160, 22) };
        var name2 = new Caption { Text = "Control", Style = Ui.Small, Bounds = new Rectangle(72, 54, 160, 18) };
        side.Controls.AddRange([logo, name1, name2]);

        string[] titles = ["Themes", "Lighting", "Display", "Night & Auto", "Status"];
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            var item = new NavItem { Text = titles[i], AccessibleName = titles[i], Bounds = new Rectangle(0, 110 + i * 42, SideW, 38) };
            item.Click += (_, _) => ShowPage(index);
            _nav.Add(item);
            side.Controls.Add(item);
        }

        _sideTemps.SetBounds(28, ContentH - 82, SideW - 40, 24);
        _sideService.SetBounds(28, ContentH - 56, SideW - 40, 20);
        side.Controls.AddRange([_sideTemps, _sideService]);
        Controls.Add(side);
    }

    private void AddPage(string title, string subtitle, Panel page)
    {
        page.Bounds = new Rectangle(SideW, 0, ContentW, ContentH - 40);
        page.BackColor = Ui.Bg;
        page.AccessibleName = title;
        page.Controls.Add(new Caption { Text = title, Style = Ui.Hero, Colour = Ui.Text, Bounds = new Rectangle(Pad, 34, 600, 46) });
        page.Controls.Add(new Caption { Text = subtitle, Bounds = new Rectangle(Pad, 82, ContentW - 2 * Pad, 24) });
        _pages.Add(page);
        Controls.Add(page);
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _nav[i].Active = i == index;
        }
        _frame.Enabled = index == 0;
    }

    private static int W => ContentW - 2 * Pad;

    private static void Section(Panel page, int y, string title) =>
        page.Controls.Add(new Caption { Text = title.ToUpperInvariant(), Style = Ui.Small, Colour = Ui.Secondary, Bounds = new Rectangle(Pad + 4, y, 400, 20) });

    private static Group NewGroup(Panel page, int y, params int[] rowHeights)
    {
        var group = new Group { Bounds = new Rectangle(Pad, y, W, rowHeights.Sum()) };
        int at = 0;
        for (int i = 0; i < rowHeights.Length - 1; i++)
        {
            at += rowHeights[i];
            group.Separators.Add(at);
        }
        page.Controls.Add(group);
        return group;
    }

    /// <summary>One row of a group: title (and optional note) on the left, a control on the right.</summary>
    private static void Row(Group g, int y, int h, string title, string? note, Control control, int controlWidth)
    {
        // with a note the title moves up and the note sits below it
        g.Controls.Add(new Caption { Text = title, Style = Ui.Body, Colour = Ui.Text, Bounds = new Rectangle(20, note is null ? y : y + 10, 300, note is null ? h : 24) });
        if (note is not null) g.Controls.Add(new Caption { Text = note, Style = Ui.Small, Bounds = new Rectangle(20, y + 34, g.Width - 80 - controlWidth, 20) });
        control.SetBounds(g.Width - 20 - controlWidth, y + (h - control.Height) / 2, controlWidth, control.Height);
        g.Controls.Add(control);
    }

    private Panel BuildThemes()
    {
        var page = new Panel();
        _picture.Bounds = new Rectangle(Pad, ContentTop, 320, 240);
        _previewTitle.SetBounds(Pad, ContentTop + 260, 320, 38);
        _previewReason.SetBounds(Pad, ContentTop + 300, 320, 70);
        page.Controls.AddRange([_picture, _previewTitle, _previewReason]);

        const int cols = 3, tileW = 136, tileH = 130, gapX = 16, gapY = 14, x0 = Pad + 320 + 44;
        for (int i = 0; i < ThemeCatalog.All.Length; i++)
        {
            var (id, title, _) = ThemeCatalog.All[i];
            using var r = Renderer.Preview(id);
            var tile = new ThemeTile
            {
                Image = r.Snapshot(),
                Text = title,
                AccessibleName = $"Theme {title}",
                Bounds = new Rectangle(x0 + i % cols * (tileW + gapX), ContentTop + i / cols * (tileH + gapY), tileW, tileH),
            };
            tile.Click += (_, _) => PickTheme(id);
            _tiles[id] = tile;
            page.Controls.Add(tile);
        }
        return page;
    }

    private Panel BuildLighting()
    {
        var page = new Panel();
        (string Key, string Title, string Note)[] groups =
        [
            ("aura", "Cooler and fans", "ASUS AURA addressable header"),
            ("board", "Mainboard", "LEDs on the board itself"),
            ("gpu", "Graphics card", "MSI RTX 5070 Gaming Trio"),
            ("openRgb", "OpenRGB devices", "Devices driven through OpenRGB"),
        ];
        const int rowH = 76;
        var group = NewGroup(page, ContentTop, rowH, rowH, rowH, rowH);
        for (int i = 0; i < groups.Length; i++)
        {
            var (key, title, note) = groups[i];
            int y = i * rowH;
            var slider = new Slider { Minimum = 0, Maximum = 100, Step = 5, Format = v => v == 0 ? "Off" : $"{v} %", AccessibleName = $"{title} brightness" };
            Row(group, y, rowH, title, note, slider, 330);
            var swatch = new Swatch { Bounds = new Rectangle(group.Width - 20 - 330 - 44, y + (rowH - 22) / 2, 22, 22), AccessibleName = $"{title} colour" };
            group.Controls.Add(swatch);
            slider.ValueChanged += (_, _) => Change(s => s with { Brightness = SetLevel(s.Brightness, key, slider.Value) });
            _levels[key] = (slider, swatch);
        }
        page.Controls.Add(new Caption
        {
            Text = "Off switches a group dark. The night window dims all groups on top of this. The dot shows the colour right now.",
            Style = Ui.Small, Colour = Ui.Tertiary, Wrap = true,
            Bounds = new Rectangle(Pad + 4, ContentTop + 4 * rowH + 12, W, 40),
        });
        return page;
    }

    private static LedBrightness SetLevel(LedBrightness b, string key, int v) => key switch
    {
        "aura" => b with { Aura = v },
        "board" => b with { Board = v },
        "gpu" => b with { Gpu = v },
        _ => b with { OpenRgb = v },
    };

    private static int GetLevel(LedBrightness b, string key) => key switch
    {
        "aura" => b.Aura,
        "board" => b.Board,
        "gpu" => b.Gpu,
        _ => b.OpenRgb,
    };

    private Panel BuildDisplay()
    {
        var page = new Panel();
        Section(page, ContentTop, "Temperature colours");
        var scale = NewGroup(page, ContentTop + 26, 96, 56, 56, 56);
        _scaleBar.SetBounds(20, 14, scale.Width - 40, 72);
        scale.Controls.Add(_scaleBar);
        Row(scale, 96, 56, "Cold end", null, _scaleMin, 420);
        Row(scale, 152, 56, "Hot end", null, _scaleMax, 420);
        Row(scale, 208, 56, "Alarm from", null, _alarm, 420);
        _scaleMin.AccessibleName = "Colour scale cold end";
        _scaleMax.AccessibleName = "Colour scale hot end";
        _alarm.AccessibleName = "Alarm temperature";
        _scaleMin.ValueChanged += (_, _) => Change(s => s with { ScaleMinC = _scaleMin.Value });
        _scaleMax.ValueChanged += (_, _) => Change(s => s with { ScaleMaxC = _scaleMax.Value });
        _alarm.ValueChanged += (_, _) => Change(s => s with { AlarmC = _alarm.Value });

        int pumpY = ContentTop + 26 + 264 + 28;
        Section(page, pumpY, "Pump sensor");
        _pump.SetBounds(Pad, pumpY + 26, W, ChoiceList.RowH * 4);
        _pump.AccessibleName = "Pump fan header";
        _pump.Changed += (_, _) => Change(s => s with { PumpFan = _pump.Selected });
        page.Controls.Add(_pump);
        page.Controls.Add(new Caption
        {
            Text = "The board fan header the AIO pump is on. The System theme shows its speed and warns below 500 RPM.",
            Style = Ui.Small, Colour = Ui.Tertiary, Wrap = true,
            Bounds = new Rectangle(Pad + 4, pumpY + 26 + ChoiceList.RowH * 4 + 10, W, 40),
        });
        return page;
    }

    private Panel BuildNight()
    {
        var page = new Panel();
        Section(page, ContentTop, "Night");
        var night = NewGroup(page, ContentTop + 26, 66, 56, 56, 56);
        Row(night, 0, 66, "Night window", "Dims the LEDs, optionally a calmer theme. May span midnight.", _nightOn, _nightOn.Width);
        _nightOn.AccessibleName = "Night window";

        var times = new Panel { BackColor = Ui.Group, Size = new Size(96 + 40 + 96, 36) };
        _nightFrom.Location = new Point(0, 0);
        _nightTo.Location = new Point(136, 0);
        times.Controls.AddRange([_nightFrom, new Caption { Text = "to", Align = ContentAlignment.MiddleCenter, Bounds = new Rectangle(96, 0, 40, 36) }, _nightTo]);
        Row(night, 66, 56, "Time", null, times, times.Width);
        _nightFrom.AccessibleName = "Night from";
        _nightTo.AccessibleName = "Night to";
        Row(night, 122, 56, "Brightness", null, _nightLevel, 420);
        _nightLevel.AccessibleName = "Night brightness";
        Row(night, 178, 56, "Theme", null, _nightTheme, 240);
        _nightTheme.AccessibleName = "Night theme";
        _nightTheme.Add("", "Keep the chosen theme");

        int autoY = ContentTop + 26 + 234 + 28;
        Section(page, autoY, "Gaming automatic");
        var auto = NewGroup(page, autoY + 26, 66, 56, 56, 56);
        Row(auto, 0, 66, "Switch while playing", "Changes theme while the graphics card is busy. Spikes do not count.", _autoOn, _autoOn.Width);
        _autoOn.AccessibleName = "Gaming automatic";
        Row(auto, 66, 56, "GPU load from", null, _autoLoad, 420);
        Row(auto, 122, 56, "For at least", null, _autoHold, 420);
        Row(auto, 178, 56, "Theme", null, _autoTheme, 240);
        _autoLoad.AccessibleName = "Automatic GPU load threshold";
        _autoHold.AccessibleName = "Automatic hold time";
        _autoTheme.AccessibleName = "Automatic theme";

        foreach (var (id, title, _) in ThemeCatalog.All)
        {
            _nightTheme.Add(id, title);
            _autoTheme.Add(id, title);
        }

        _nightOn.Changed += (_, _) => Change(s => s with { Night = s.Night with { Enabled = _nightOn.On } });
        _nightFrom.Changed += (_, _) => Change(s => s with { Night = s.Night with { From = _nightFrom.Display } });
        _nightTo.Changed += (_, _) => Change(s => s with { Night = s.Night with { To = _nightTo.Display } });
        _nightLevel.ValueChanged += (_, _) => Change(s => s with { Night = s.Night with { Brightness = _nightLevel.Value } });
        _nightTheme.Changed += (_, _) => Change(s => s with { Night = s.Night with { Theme = _nightTheme.Selected } });
        _autoOn.Changed += (_, _) => Change(s => s with { Auto = s.Auto with { Enabled = _autoOn.On } });
        _autoLoad.ValueChanged += (_, _) => Change(s => s with { Auto = s.Auto with { GpuLoad = _autoLoad.Value } });
        _autoHold.ValueChanged += (_, _) => Change(s => s with { Auto = s.Auto with { HoldSeconds = _autoHold.Value } });
        _autoTheme.Changed += (_, _) => Change(s => s with { Auto = s.Auto with { Theme = _autoTheme.Selected } });
        return page;
    }

    private Panel BuildStatus()
    {
        var page = new Panel();
        _statusView.SetBounds(Pad, ContentTop, W, ContentH - ContentTop - 60);
        _statusView.AccessibleName = "Service status";
        page.Controls.Add(_statusView);
        return page;
    }

    // ---------------------------------------------------------------- state

    private void LoadControls()
    {
        _loading = true;
        var s = _model.Settings;
        foreach (var (key, (slider, _)) in _levels) slider.Value = GetLevel(s.Brightness, key);
        _alarm.Value = s.AlarmC;
        _scaleMin.Value = s.ScaleMinC;
        _scaleMax.Value = s.ScaleMaxC;
        _pump.Selected = s.PumpFan;
        _nightOn.On = s.Night.Enabled;
        _nightFrom.Minutes = Minutes(s.Night.From);
        _nightTo.Minutes = Minutes(s.Night.To);
        _nightLevel.Value = s.Night.Brightness;
        _nightTheme.Selected = s.Night.Theme;
        _autoOn.On = s.Auto.Enabled;
        _autoLoad.Value = s.Auto.GpuLoad;
        _autoHold.Value = s.Auto.HoldSeconds;
        _autoTheme.Selected = s.Auto.Theme;
        _loading = false;
    }

    private static int Minutes(string hhmm) =>
        TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.Hour * 60 + t.Minute : 0;

    private void Change(Func<UserSettings, UserSettings> change)
    {
        if (_loading) return;
        if (_model.Update(change, out var problem))
        {
            Pal.Configure(_model.Settings);
            _scaleBar.Invalidate();
            _save.Stop();
            _save.Start();
            return;
        }
        // refused: put the controls back to what is saved, say why
        LoadControls();
        Say($"Not applied: {problem}", Ui.Orange);
    }

    private void Save()
    {
        _save.Stop();
        try
        {
            if (_model.Flush()) Say("Saved. The service picks it up within a second.", Ui.Tertiary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Could not save: {e.Message}. Is the service installed?", Ui.Red);
        }
    }

    private void PickTheme(string id)
    {
        try
        {
            _model.SetTheme(id);
            Say($"{Title(id)} chosen.", Ui.Tertiary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Could not save the theme: {e.Message}. Is the service installed?", Ui.Red);
        }
        Tick();
    }

    private void Say(string text, Color colour)
    {
        _footer.Colour = colour;
        _footer.Text = text;
    }

    private static string Title(string id) => ThemeCatalog.All.FirstOrDefault(t => t.Id == id).Title ?? id;

    private void Tick()
    {
        var now = DateTime.UtcNow;
        try
        {
            _model.Touch(now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Cannot reach the service folder: {e.Message}", Ui.Red);
        }
        bool live = _model.RefreshStatus(now);
        var status = _model.Status;

        string chosen = _model.Theme;
        foreach (var (id, tile) in _tiles) tile.Active = id == chosen;

        string shown = live && status is not null && ThemeCatalog.Exists(status.Theme) ? status.Theme : chosen;
        _previewTitle.Text = Title(shown);
        _previewReason.Text = !live
            ? "The service is not reporting, so this preview uses sample data."
            : status!.ThemeReason switch
            {
                "night" => $"On the LCD because the night window is open. {Title(chosen)} returns afterwards.",
                "auto" => $"On the LCD because the graphics card is busy. {Title(chosen)} returns afterwards.",
                "pinned" => "Fixed by the --theme option of the service.",
                _ => "On your LCD now, with your real readings.",
            };
        if (shown != _previewTheme || live != _previewLive) NewPreview(shown, live);
        if (live && _model.Live is { } r) _preview?.Sample(r);

        _sideTemps.Text = live && _model.Live is { } n ? $"CPU {Scene.Deg(n.CpuTemp)}    GPU {Scene.Deg(n.GpuTemp)}" : "";
        _sideService.Colour = live ? Ui.Green : Ui.Orange;
        _sideService.Text = live ? "● Service live" : "● Service not reporting";

        _scaleBar.Readings = live ? _model.Live : null;
        _scaleBar.Invalidate();
        UpdateSwatches(live ? _model.Live : null, shown);
        _pump.SetFans(live ? _model.Live?.Fans : null, _model.Settings.PumpFan);
        _statusView.Show(_model, live, chosen);
    }

    private void NewPreview(string id, bool live)
    {
        _preview?.Dispose();
        if (live && _model.Live is { } r)
        {
            _preview = new Renderer(Scene.W, Scene.H, id);
            // a full history of the current reading, so graphs do not start empty
            for (int i = 0; i < Scene.HistoryLen; i++) _preview.Sample(r);
        }
        else
        {
            _preview = Renderer.Preview(id);
        }
        _previewTheme = id;
        _previewLive = live;
        _frame.Interval = Math.Clamp(_preview.FrameMs, 60, 1000);
        DrawPreview();
    }

    private void DrawPreview()
    {
        if (_preview is null) return;
        _preview.Draw();
        _picture.Show(_preview.Snapshot());
    }

    private void UpdateSwatches(Readings? r, string theme)
    {
        using var t = ThemeCatalog.Create(theme);
        foreach (var (key, (slider, swatch)) in _levels)
        {
            var source = key == "gpu" ? LedSource.Gpu : LedSource.Cpu;
            float temp = (source == LedSource.Gpu ? r?.GpuTemp : r?.CpuTemp ?? r?.GpuTemp) ?? 50;
            swatch.Colour = t.Led(source, temp).Dim(slider.Value).A;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _tick.Stop();
        _frame.Stop();
        Save();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _frame.Dispose();
            _save.Dispose();
            _preview?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Round colour sample of what an LED group shows right now.</summary>
internal sealed class Swatch : DarkControl
{
    private Color _colour = Color.Black;

    public Swatch()
    {
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.Graphic;
    }

    public Color Colour
    {
        get => _colour;
        set
        {
            if (value.ToArgb() == _colour.ToArgb()) return;
            _colour = value;
            AccessibleDescription = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Ui.Smooth(e.Graphics);
        Ui.Fill(e.Graphics, Ui.Separator, new RectangleF(0, 0, Width, Height), Width / 2f);
        Ui.Fill(e.Graphics, _colour, new RectangleF(2, 2, Width - 4, Height - 4), (Width - 4) / 2f);
    }
}

/// <summary>Colour scale from 0 to 110 °C with the alarm line and the current temperatures.</summary>
internal sealed class ScaleBar : DarkControl
{
    private const float Lo = 0, Hi = 110;

    public Readings? Readings { get; set; }

    public ScaleBar()
    {
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Colour scale preview";
    }

    private float X(float t) => 4 + (Width - 8) * Math.Clamp((t - Lo) / (Hi - Lo), 0, 1);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        const float y = 28, h = 10;
        var bar = new RectangleF(4, y, Width - 8, h);
        using (var clip = Ui.Round(bar, h / 2))
        {
            g.SetClip(clip);
            for (int px = 4; px < Width - 4; px++)
            {
                float t = Lo + (px - 4) / (float)(Width - 8) * (Hi - Lo);
                using var pen = new Pen(Pal.IsAlarm(t) ? Pal.Alarm : Pal.Gauge(t));
                g.DrawLine(pen, px, y, px, y + h);
            }
            g.ResetClip();
        }
        for (int t = 0; t <= 100; t += 20)
            // the outer labels must not run off the control
            Ui.Draw(g, $"{t}°", Ui.Small, Ui.Tertiary, new RectangleF(Math.Clamp(X(t) - 20, 0, Width - 40), 2, 40, 18), ContentAlignment.MiddleCenter);

        float ax = X(Pal.AlarmC);
        Ui.Fill(g, Ui.Text, new RectangleF(ax - 1, y - 5, 2, h + 10), 1);
        Ui.Draw(g, $"Alarm {Pal.AlarmC:0}°", Ui.Small, Ui.Text, new RectangleF(ax - 50, y + h + 8, 100, 18), ContentAlignment.MiddleCenter);

        if (Readings is { } r)
        {
            Marker(g, r.CpuTemp, "CPU", y, h);
            Marker(g, r.GpuTemp, "GPU", y, h);
        }
    }

    private void Marker(Graphics g, float? temp, string name, float y, float h)
    {
        if (temp is not { } t) return;
        float x = X(t);
        Ui.Fill(g, Ui.Bg, new RectangleF(x - 7, y + h / 2 - 7, 14, 14), 7);
        Ui.Fill(g, Ui.Text, new RectangleF(x - 5, y + h / 2 - 5, 10, 10), 5);
        Ui.Draw(g, name, Ui.Small, Ui.Secondary, new RectangleF(x - 24, y + h + 8, 48, 18), ContentAlignment.MiddleCenter);
    }
}

/// <summary>List of the board fans plus "None" in a grouped surface; a check marks the pump. Arrows move it.</summary>
internal sealed class ChoiceList : DarkControl
{
    public const int RowH = 46;
    private readonly List<(string Key, string Text, string Detail)> _items = [("", "None", "")];
    private string _selected = "";

    public event EventHandler? Changed;

    public ChoiceList()
    {
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    public string Selected
    {
        get => _selected;
        set { _selected = value; AccessibleDescription = value.Length == 0 ? "None" : value; Invalidate(); }
    }

    /// <summary>Board fans that turn, plus the one already chosen even if it reads nothing right now.</summary>
    public void SetFans(FanReading[]? fans, string chosen)
    {
        var items = new List<(string, string, string)> { ("", "None", "") };
        foreach (var f in fans ?? [])
        {
            if (f.Name.StartsWith("GPU", StringComparison.Ordinal)) continue;
            if (f.Rpm <= 0 && !f.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase)) continue;
            items.Add((f.Name, f.Name, $"{f.Rpm:0} RPM"));
        }
        if (chosen.Length > 0 && !items.Any(i => i.Item1.Equals(chosen, StringComparison.OrdinalIgnoreCase)))
            items.Add((chosen, chosen, "no reading"));
        if (fans is null && items.Count == 1) items.Add(("", "Fans appear here while the service runs.", ""));
        if (items.SequenceEqual(_items)) return;
        _items.Clear();
        _items.AddRange(items);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        int rows = Math.Min(_items.Count, Height / RowH);
        Ui.Fill(g, Ui.Group, new RectangleF(0, 0, Width, rows * RowH), 14);
        for (int i = 0; i < rows; i++)
        {
            var (key, text, detail) = _items[i];
            bool info = i > 0 && key.Length == 0;
            bool on = !info && key.Equals(_selected, StringComparison.OrdinalIgnoreCase);
            float y = i * RowH;
            if (i > 0) Ui.Hairline(g, 20, Width, y);
            Ui.Draw(g, text, Ui.Body, info ? Ui.Tertiary : Ui.Text, new RectangleF(20, y, Width - 200, RowH));
            Ui.Draw(g, detail, Ui.Body, Ui.Secondary, new RectangleF(Width - 200, y, 140, RowH), ContentAlignment.MiddleRight);
            if (on)
            {
                using var pen = new Pen(Ui.Blue, 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                float cx = Width - 32, cy = y + RowH / 2f;
                g.DrawLines(pen, new PointF[] { new(cx - 7, cy), new(cx - 2, cy + 5), new(cx + 7, cy - 6) });
            }
        }
        if (Focused && ShowFocusCues)
        {
            int i = Math.Max(0, _items.FindIndex(it => it.Key.Equals(_selected, StringComparison.OrdinalIgnoreCase)));
            FocusRing(g, new RectangleF(2, i * RowH + 2, Width - 4, RowH - 4), 10);
        }
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var (key, _, _) = _items[index];
        if (index > 0 && key.Length == 0) return;
        Selected = key;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseClick(MouseEventArgs e) { Focus(); Choose(e.Y / RowH); base.OnMouseClick(e); }

    // value: the chosen fan header ("" for none); setting it picks a listed header
    protected override string? AccessibleValue => _selected.Length == 0 ? "None" : _selected;

    protected override void SetAccessibleValue(string value)
    {
        string key = value.Equals("None", StringComparison.OrdinalIgnoreCase) ? "" : value.Trim();
        Choose(_items.FindIndex(it => it.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && (it.Key.Length > 0 || key.Length == 0)));
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int i = _items.FindIndex(it => it.Key.Equals(_selected, StringComparison.OrdinalIgnoreCase));
        if (e.KeyCode == Keys.Down) { Choose(i + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { Choose(i - 1); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

/// <summary>What the service reports, as a grouped list: label left, value right.</summary>
internal sealed class StatusView : DarkControl
{
    private const int RowH = 40;
    private readonly List<(string Label, string Value, Color Colour)> _rows = [];

    public StatusView()
    {
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.Table;
    }

    public void Show(SwitcherModel model, bool live, string chosen)
    {
        _rows.Clear();
        var s = model.Status;
        if (live && s is not null)
            _rows.Add(("Service", $"Live, updated {(DateTime.UtcNow - s.TimeUtc).TotalSeconds:0} s ago", Ui.Green));
        else
            _rows.Add(("Service", s is null ? "Not reporting. Is the scheduled task UltraLightRgb installed?" : $"Not reporting since {s.TimeUtc.ToLocalTime():HH:mm:ss}", Ui.Orange));
        if (model.StatusProblem is { } sp) _rows.Add(("Status file", sp, Ui.Red));
        if (s is not null)
        {
            _rows.Add(("Theme on the LCD", $"{ThemeCatalog.All.FirstOrDefault(t => t.Id == s.Theme).Title ?? s.Theme} ({s.ThemeReason})", Ui.Text));
            _rows.Add(("LCD", s.Lcd ? "Connected" : s.LcdError ?? "Not connected", s.Lcd ? Ui.Green : Ui.Orange));
            foreach (var o in s.Outputs)
                _rows.Add((o.Name, o.Error is { } err ? $"{o.State}: {err}" : o.State,
                    o.State == "Ok" ? Ui.Green : o.State == "Waiting" ? Ui.Secondary : Ui.Orange));
            if (s.SettingsProblem is { } p) _rows.Add(("Settings", $"Rejected: {p}", Ui.Red));
            _rows.Add(("CPU", $"{Scene.Deg(s.CpuTemp)}   {Scene.Pct(s.CpuLoad)}", Ui.Text));
            _rows.Add(("GPU", $"{Scene.Deg(s.GpuTemp)}   {Scene.Pct(s.GpuLoad)}", Ui.Text));
            if (s.RamLoad is { } ram) _rows.Add(("Memory", $"{ram:0} %", Ui.Text));
            if (s.VramUsedMb is { } vu && s.VramTotalMb is { } vt) _rows.Add(("Video memory", $"{vu / 1024:0.0} of {vt / 1024:0.0} GB", Ui.Text));
            _rows.Add(("Pump", s.PumpRpm is { } pr ? $"{pr:0} RPM" : "Not set (Display page)", s.PumpRpm is { } x && x < 500 ? Ui.Red : Ui.Text));
            foreach (var f in s.Fans.Where(f => f.Rpm > 0)) _rows.Add((f.Name, $"{f.Rpm:0} RPM", Ui.Secondary));
        }
        AccessibleDescription = string.Join("; ", _rows.Select(r => $"{r.Label}: {r.Value}"));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Smooth(g);
        int rows = Math.Min(_rows.Count, Height / RowH);
        Ui.Fill(g, Ui.Group, new RectangleF(0, 0, Width, rows * RowH), 14);
        for (int i = 0; i < rows; i++)
        {
            var (label, value, colour) = _rows[i];
            float y = i * RowH;
            if (i > 0) Ui.Hairline(g, 20, Width, y);
            Ui.Draw(g, label, Ui.Body, Ui.Text, new RectangleF(20, y, 240, RowH));
            Ui.Draw(g, value, Ui.Body, colour, new RectangleF(260, y, Width - 280, RowH), ContentAlignment.MiddleRight);
        }
    }
}
