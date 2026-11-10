using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UltraLightRgb;

/// <summary>Sci-fi targeting display: rings turn with the load, the whole thing shifts colour when it gets hot.</summary>
public sealed class HologrammTheme : ITheme
{
    private const float Cx = 160, Cy = 112;
    private static readonly Color Cyan = Color.FromArgb(0, 230, 255), Magenta = Color.FromArgb(255, 40, 170);

    private readonly Bitmap _background;
    private readonly Pen _pen = new(Cyan, 1);
    private readonly SolidBrush _brush = new(Cyan);
    private readonly Font _big = Pal.Font(Pal.Condensed, 62), _mid = Pal.Font(Pal.Semi, 15), _tiny = Pal.Font(Pal.Regular, 10);
    private float _spin, _counter;

    public int FrameMs => 100;

    private static Color Accent(float tempC) =>
        tempC >= Pal.AlarmC ? Pal.Alarm : tempC >= 75 ? Magenta : Cyan;

    public LedLook Led(LedSource source, float tempC) =>
        new(Accent(tempC), Color.FromArgb(20, 0, 255), 10);

    public HologrammTheme()
    {
        _background = new Bitmap(Scene.W, Scene.H, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(_background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, Scene.W, Scene.H),
                   Color.FromArgb(2, 12, 26), Color.FromArgb(0, 2, 7), LinearGradientMode.Vertical))
            g.FillRectangle(bg, 0, 0, Scene.W, Scene.H);

        // honeycomb, barely there
        using var hex = new Pen(Color.FromArgb(14, 46, 62), 1);
        const float r = 12;
        float dx = r * 1.732f, dy = r * 1.5f;
        for (int row = -1; row < 19; row++)
            for (int col = -1; col < 16; col++)
            {
                float x = col * dx + (row % 2 == 0 ? 0 : dx / 2), y = row * dy;
                var pts = new PointF[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = MathF.PI / 6 + i * MathF.PI / 3;
                    pts[i] = new PointF(x + r * MathF.Cos(a), y + r * MathF.Sin(a));
                }
                g.DrawPolygon(hex, pts);
            }

        // frame corners
        using var frame = new Pen(Cyan, 2);
        foreach (var (x, y, sx, sy) in new (float, float, int, int)[] { (5, 5, 1, 1), (315, 5, -1, 1), (5, 235, 1, -1), (315, 235, -1, -1) })
        {
            g.DrawLine(frame, x, y, x + 22 * sx, y);
            g.DrawLine(frame, x, y, x, y + 22 * sy);
        }
    }

    private void Arc(Graphics g, float r, float width, Color c, float start, float sweep)
    {
        if (sweep < 0.1f) return;
        _pen.Color = c;
        _pen.Width = width;
        g.DrawArc(_pen, Cx - r, Cy - r, r * 2, r * 2, start, sweep);
    }

    private void Dashes(Graphics g, float r, float width, Color c, float start, int count, float sweep)
    {
        for (int i = 0; i < count; i++) Arc(g, r, width, c, start + i * 360f / count, sweep);
    }

    private void LoadColumn(Graphics g, float x, float load, Color c)
    {
        const int n = 18;
        int lit = (int)MathF.Round(load / 100 * n);
        for (int i = 0; i < n; i++)
        {
            _brush.Color = i < lit ? c : Color.FromArgb(12, 40, 54);
            g.FillRectangle(_brush, x, 200 - i * 9, 12, 6);
        }
    }

    public void Draw(Scene s)
    {
        var g = s.G;
        g.DrawImageUnscaled(_background, 0, 0);

        float cpu = s.Now.CpuTemp ?? 0, gpu = s.Now.GpuTemp ?? 0;
        var accent = Accent(s.Hottest);
        var faint = Color.FromArgb(70, accent);

        _spin += 1.5f + s.CpuLoad * 0.11f;
        _counter -= 1 + s.GpuLoad * 0.08f;

        // sweep with a fading tail
        for (int i = 0; i < 14; i++)
        {
            _brush.Color = Color.FromArgb(16 - i, accent);
            g.FillPie(_brush, Cx - 66, Cy - 66, 132, 132, _spin * 2 - i * 5, 6);
        }

        Dashes(g, 106, 2, accent, _spin, 3, 62);
        Dashes(g, 100, 1, faint, _counter, 24, 7);
        Dashes(g, 66, 2, accent, _counter * 1.4f, 4, 34);

        const float start = 120, sweep = 300;
        Arc(g, 90, 8, Color.FromArgb(22, 58, 74), start, sweep);
        Arc(g, 77, 6, Color.FromArgb(22, 58, 74), start, sweep);
        Arc(g, 90, 15, Color.FromArgb(60, Pal.Gauge(cpu)), start, sweep * Pal.Pos(cpu));
        Arc(g, 90, 8, Pal.Gauge(cpu), start, sweep * Pal.Pos(cpu));
        Arc(g, 77, 6, Pal.Gauge(gpu), start, sweep * Pal.Pos(gpu));

        s.Text("CPU", _mid, accent, Cx, Cy - 44, StringAlignment.Center);
        s.Text(Scene.Deg(s.Now.CpuTemp), _big, Color.White, Cx + 3, Cy + 2, StringAlignment.Center, StringAlignment.Center);
        s.Text($"GPU {Scene.Deg(s.Now.GpuTemp)}", _mid, Pal.Gauge(gpu), Cx, Cy + 32, StringAlignment.Center);

        LoadColumn(g, 16, s.CpuLoad, accent);
        LoadColumn(g, Scene.W - 28, s.GpuLoad, accent);
        s.Text(Scene.Pct(s.Now.CpuLoad), _tiny, accent, 12, 216);
        s.Text(Scene.Pct(s.Now.GpuLoad), _tiny, accent, Scene.W - 12, 216, StringAlignment.Far);
        s.Text(s.Hottest >= Pal.AlarmC ? "WARNUNG" : "SYSTEM STABIL", _tiny, accent, Cx, 224, StringAlignment.Center);

        // scan beam
        float y = s.Time * 46 % (Scene.H + 40) - 20;
        _pen.Color = Color.FromArgb(40, accent);
        _pen.Width = 7;
        g.DrawLine(_pen, 0, y, Scene.W, y);
        _pen.Color = Color.FromArgb(120, accent);
        _pen.Width = 1;
        g.DrawLine(_pen, 0, y, Scene.W, y);
    }

    public void Dispose()
    {
        _background.Dispose();
        _pen.Dispose();
        _brush.Dispose();
        _big.Dispose();
        _mid.Dispose();
        _tiny.Dispose();
    }
}

/// <summary>Old green terminal: bitmap digits, then scanlines, vignette and a rolling band on top.</summary>
public sealed class PhosphorTheme : ITheme
{
    private static readonly Color Green = Color.FromArgb(70, 255, 130), Dim = Color.FromArgb(28, 120, 66);
    private static readonly Color Back = Color.FromArgb(2, 12, 7);

    // 5x7 digits, one int per row, highest of the five low bits is the left pixel
    private static readonly int[][] Digits =
    [
        [14, 17, 19, 21, 25, 17, 14], [4, 12, 4, 4, 4, 4, 14], [14, 17, 1, 2, 4, 8, 31], [31, 2, 4, 2, 1, 17, 14],
        [2, 6, 10, 18, 31, 2, 2], [31, 16, 30, 1, 1, 17, 14], [6, 8, 16, 30, 17, 17, 14], [31, 1, 2, 4, 8, 8, 8],
        [14, 17, 17, 14, 17, 17, 14], [14, 17, 17, 15, 1, 2, 12],
    ];

    private readonly ushort[] _gain = new ushort[Scene.W * Scene.H];
    private readonly Font _text = Pal.Font("Consolas", 14), _small = Pal.Font("Consolas", 12);
    private readonly SolidBrush _brush = new(Green);

    public int FrameMs => 150;

    public LedLook Led(LedSource source, float tempC)
    {
        if (tempC >= Pal.AlarmC) return new LedLook(Pal.Alarm);
        var c = Pal.Mix(Color.FromArgb(0, 120, 36), Color.FromArgb(40, 255, 110), Pal.Pos(tempC));
        return new LedLook(c, Pal.Mix(c, Color.Black, 0.9f), 2);
    }

    public PhosphorTheme()
    {
        for (int y = 0; y < Scene.H; y++)
            for (int x = 0; x < Scene.W; x++)
            {
                float nx = (x - 160) / 160f, ny = (y - 120) / 120f;
                float vignette = 1 - 0.55f * MathF.Pow(nx * nx + ny * ny, 1.5f) / 2.83f;
                float line = y % 2 == 0 ? 1f : 0.6f;
                _gain[y * Scene.W + x] = (ushort)(256 * vignette * line);
            }
    }

    private void Number(Graphics g, float? value, float x, float y, int px)
    {
        int v = value is { } t ? Math.Clamp((int)MathF.Round(t), 0, 99) : -1;
        int[] shown = v < 0 ? [] : [v / 10, v % 10];
        _brush.Color = Green;
        for (int d = 0; d < shown.Length; d++)
        {
            var rows = Digits[shown[d]];
            for (int r = 0; r < 7; r++)
                for (int c = 0; c < 5; c++)
                    if ((rows[r] >> (4 - c) & 1) != 0)
                        g.FillRectangle(_brush, x + d * 6 * px + c * px, y + r * px, px, px);
        }
        if (shown.Length > 0) g.DrawEllipse(Pens.LimeGreen, x + 12 * px + 1, y, 8, 8);
    }

    private static string Bar(float load, int width)
    {
        int n = (int)MathF.Round(Math.Clamp(load, 0, 100) / 100 * width);
        return new string('#', n) + new string('.', width - n);
    }

    private unsafe void PostProcess(Scene s)
    {
        // brightness wobble and the slow band of an old tube
        int flicker = 248 + (int)(s.Time * 37 % 1 * 8);
        int band = (int)(s.Time * 34 % (Scene.H + 60)) - 30;
        var bmp = s.Bmp;
        var d = bmp.LockBits(new Rectangle(0, 0, Scene.W, Scene.H), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < Scene.H; y++)
            {
                byte* p = (byte*)d.Scan0 + y * d.Stride;
                int dist = Math.Abs(y - band), boost = dist < 18 ? 256 + (18 - dist) * 5 : 256;
                int rowStart = y * Scene.W;
                for (int x = 0; x < Scene.W; x++, p += 4)
                {
                    int gain = (_gain[rowStart + x] * flicker >> 8) * boost >> 8;
                    p[0] = (byte)Math.Min(255, p[0] * gain >> 8);
                    p[1] = (byte)Math.Min(255, p[1] * gain >> 8);
                    p[2] = (byte)Math.Min(255, p[2] * gain >> 8);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(d);
        }
    }

    public void Draw(Scene s)
    {
        var g = s.G;
        g.Clear(s.Hottest >= Pal.AlarmC ? Color.FromArgb(40, 4, 2) : Back);
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;

        s.Text("TRLCD://SYSMON", _text, Green, 8, 6);
        s.Text(DateTime.Now.ToString("HH:mm:ss"), _text, Dim, Scene.W - 8, 6, StringAlignment.Far);
        s.Text("CPU", _small, Dim, 12, 28);
        s.Text("GPU", _small, Dim, 172, 28);
        Number(g, s.Now.CpuTemp, 12, 43, 9);
        Number(g, s.Now.GpuTemp, 172, 43, 9);

        s.Text($"CPU [{Bar(s.CpuLoad, 24)}]{s.CpuLoad,4:0}%", _small, Green, 8, 110);
        s.Text($"GPU [{Bar(s.GpuLoad, 24)}]{s.GpuLoad,4:0}%", _small, Green, 8, 126);

        // history as coarse block columns
        const int bars = 76, step = 4, bottom = 204;
        _brush.Color = Green;
        for (int i = 0; i < bars; i++)
        {
            float v = s.CpuTemps[i * (Scene.HistoryLen - 1) / (bars - 1)];
            if (float.IsNaN(v)) continue;
            int h = (int)(Math.Clamp((v - 35) / 60, 0, 1) * 54);
            for (int y = 0; y < h; y += 4) g.FillRectangle(_brush, 8 + i * step, bottom - y, 3, 3);
        }

        string cursor = (int)(s.Time * 2) % 2 == 0 ? "_" : " ";
        s.Text(s.Hottest >= Pal.AlarmC ? $"> WARNUNG: UEBERHITZUNG{cursor}" : $"> ALLE SYSTEME NOMINAL{cursor}", _small, Green, 8, 216);

        g.SmoothingMode = smoothing;
        PostProcess(s);
    }

    public void Dispose()
    {
        _text.Dispose();
        _small.Dispose();
        _brush.Dispose();
    }
}
