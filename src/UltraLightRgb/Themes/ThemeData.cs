using System.Drawing;
using System.Drawing.Drawing2D;

namespace UltraLightRgb;

/// <summary>Five minutes as a band: bar height is load, colour is temperature, newest value on the right.</summary>
public sealed class WaermebandTheme : ITheme
{
    private const int BandH = 119;

    private readonly Pen _pen = new(Color.White);
    private readonly Font _big = Pal.Font(Pal.Condensed, 58), _label = Pal.Font(Pal.Semi, 13), _tiny = Pal.Font(Pal.Regular, 10);
    private readonly LinearGradientBrush _fadeTop = Fade(0), _fadeBottom = Fade(121);

    private static LinearGradientBrush Fade(int y) =>
        new(new RectangleF(0, y, 170, BandH), Color.FromArgb(235, 4, 5, 9), Color.FromArgb(0, 4, 5, 9), LinearGradientMode.Horizontal);

    public int FrameMs => 1000;

    private static Color Heat(float tempC) => Pal.Iron(Pal.Pos(tempC));

    public LedLook Led(LedSource source, float tempC) =>
        new(Pal.Iron(Pal.Pos(tempC) + 0.3f), Pal.Iron(Pal.Pos(tempC) - 0.3f), 3);

    private void Band(Scene s, float y0, float[] temps, float[] loads, LinearGradientBrush fade, string label, float? now, float? load)
    {
        var g = s.G;
        for (int x = 0; x < Scene.W; x++)
        {
            float t = temps[x], l = loads[x];
            if (float.IsNaN(t) || float.IsNaN(l)) continue;
            var c = Heat(t);
            _pen.Color = Pal.Mix(c, Color.Black, 0.84f);
            g.DrawLine(_pen, x, y0, x, y0 + BandH);
            _pen.Color = c;
            g.DrawLine(_pen, x, y0 + BandH, x, y0 + BandH - l / 100f * BandH);
        }
        g.FillRectangle(fade, 0, y0, 170, BandH);
        s.Text(label, _label, now is { } v ? Heat(v) : Color.Gray, 10, y0 + 8);
        s.Text(Scene.Deg(now), _big, Color.FromArgb(232, 236, 244), 8, y0 + 24);
        s.Text(Scene.Pct(load), _label, Color.FromArgb(232, 236, 244), 90, y0 + 8);
    }

    public void Draw(Scene s)
    {
        var g = s.G;
        var smoothing = g.SmoothingMode;
        g.Clear(Color.FromArgb(4, 5, 9));
        g.SmoothingMode = SmoothingMode.None;
        Band(s, 0, s.CpuTemps, s.CpuLoads, _fadeTop, "CPU", s.Now.CpuTemp, s.Now.CpuLoad);
        Band(s, 121, s.GpuTemps, s.GpuLoads, _fadeBottom, "GPU", s.Now.GpuTemp, s.Now.GpuLoad);
        g.SmoothingMode = smoothing;
        s.Text("−5 MIN", _tiny, Color.FromArgb(124, 130, 148), 10, 104);
        s.Text("JETZT", _tiny, Color.White, Scene.W - 6, 5, StringAlignment.Far);
    }

    public void Dispose()
    {
        _pen.Dispose();
        _big.Dispose();
        _label.Dispose();
        _tiny.Dispose();
        _fadeTop.Dispose();
        _fadeBottom.Dispose();
    }
}

/// <summary>Poster: two colour fields, giant digits, the LEDs repeat the fields in the case.</summary>
public sealed class SplitTheme : ITheme
{
    private static readonly Color Ink = Color.FromArgb(10, 11, 16);
    private static readonly PointF[] Left = [new(0, 0), new(186, 0), new(134, 240), new(0, 240)];
    private static readonly PointF[] Right = [new(190, 0), new(320, 0), new(320, 240), new(138, 240)];

    private readonly Font _big = Pal.Font(Pal.Condensed, 104), _label = Pal.Font(Pal.Semi, 15), _small = Pal.Font(Pal.Semi, 13);
    private readonly SolidBrush _ink = new(Ink), _inkSoft = new(Color.FromArgb(56, Ink));

    public int FrameMs => 1000;

    private static readonly Color CpuTeam = Color.FromArgb(255, 70, 0), GpuTeam = Color.FromArgb(0, 210, 255);

    // each side keeps its hue, heat only turns it up
    private static Color Team(Color c, float tempC) =>
        Pal.Mix(Pal.Mix(c, Color.Black, 0.55f), c, Math.Min(1, Pal.Pos(tempC) * 1.6f));

    public LedLook Led(LedSource source, float tempC) =>
        tempC >= Pal.AlarmC ? new LedLook(Pal.Alarm) : new LedLook(Team(source == LedSource.Cpu ? CpuTeam : GpuTeam, tempC));

    private static void Field(Graphics g, PointF[] area, Color c)
    {
        using var b = new LinearGradientBrush(new Rectangle(0, 0, Scene.W, Scene.H), c,
            Pal.Mix(c, Color.Black, 0.28f), LinearGradientMode.Vertical);
        g.FillPolygon(b, area);
    }

    public void Draw(Scene s)
    {
        var g = s.G;
        g.Clear(Color.Black);
        Field(g, Left, s.Now.CpuTemp is { } c ? Team(CpuTeam, c) : Color.Gray);
        Field(g, Right, s.Now.GpuTemp is { } t ? Team(GpuTeam, t) : Color.Gray);

        s.Text("CPU", _label, Ink, 12, 10);
        s.Text(Scene.Deg(s.Now.CpuTemp), _big, Ink, 6, 118, StringAlignment.Near, StringAlignment.Center);
        s.Text("GPU", _label, Ink, Scene.W - 12, 10, StringAlignment.Far);
        s.Text(Scene.Deg(s.Now.GpuTemp), _big, Ink, Scene.W - 6, 118, StringAlignment.Far, StringAlignment.Center);

        float cpu = 110 * Math.Clamp(s.CpuLoad, 0, 100) / 100, gpu = 110 * Math.Clamp(s.GpuLoad, 0, 100) / 100;
        g.FillRectangle(_inkSoft, 12, 204, 110, 6);
        g.FillRectangle(_ink, 12, 204, cpu, 6);
        s.Text(Scene.Pct(s.Now.CpuLoad), _small, Ink, 12, 216);
        g.FillRectangle(_inkSoft, Scene.W - 122, 204, 110, 6);
        g.FillRectangle(_ink, Scene.W - 12 - gpu, 204, gpu, 6);
        s.Text(Scene.Pct(s.Now.GpuLoad), _small, Ink, Scene.W - 12, 216, StringAlignment.Far);
    }

    public void Dispose()
    {
        _big.Dispose();
        _label.Dispose();
        _small.Dispose();
        _ink.Dispose();
        _inkSoft.Dispose();
    }
}

/// <summary>Stays out of the way in a dark room, until something overheats.</summary>
public sealed class NachtwacheTheme : ITheme
{
    private const float LineY = 150, X0 = 28, X1 = Scene.W - 28;

    private readonly Font _big = Pal.Font(Pal.Light, 44), _label = Pal.Font(Pal.Regular, 11);
    private readonly Pen _hair = new(Color.FromArgb(40, 43, 52), 1);
    private readonly SolidBrush _brush = new(Color.White);

    public int FrameMs => 1000;

    public LedLook Led(LedSource source, float tempC) =>
        new(tempC >= Pal.AlarmC ? Pal.Alarm : Color.FromArgb(4, 2, 20));

    private void Mark(Graphics g, float x, Color c, bool above)
    {
        float d = above ? -7 : 7, tip = above ? -2 : 2;
        _brush.Color = c;
        g.FillPolygon(_brush, new PointF[] { new(x, LineY + tip), new(x - 3, LineY + d), new(x + 3, LineY + d) });
    }

    public void Draw(Scene s)
    {
        var g = s.G;
        bool alarm = s.Hottest >= Pal.AlarmC;
        var text = alarm ? Pal.Alarm : Color.FromArgb(104, 110, 122);
        var label = alarm ? Pal.Alarm : Color.FromArgb(70, 74, 84);
        g.Clear(Color.Black);

        s.Text("CPU", _label, label, 30, 38);
        s.Text(Scene.Deg(s.Now.CpuTemp), _big, text, 28, 54);
        s.Text("GPU", _label, label, Scene.W - 30, 38, StringAlignment.Far);
        s.Text(Scene.Deg(s.Now.GpuTemp), _big, text, Scene.W - 28, 54, StringAlignment.Far);

        g.DrawLine(_hair, X0, LineY, X1, LineY);
        for (int t = 20; t <= 95; t += 15)
        {
            float x = X0 + Pal.Pos(t) * (X1 - X0);
            g.DrawLine(_hair, x, LineY - 3, x, LineY + 3);
        }
        if (s.Now.CpuTemp is { } c) Mark(g, X0 + Pal.Pos(c) * (X1 - X0), text, true);
        if (s.Now.GpuTemp is { } gpu) Mark(g, X0 + Pal.Pos(gpu) * (X1 - X0), text, false);
    }

    public void Dispose()
    {
        _big.Dispose();
        _label.Dispose();
        _hair.Dispose();
        _brush.Dispose();
    }
}
