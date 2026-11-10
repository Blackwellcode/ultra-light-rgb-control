using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UltraLightRgb;

/// <summary>
/// Two ring gauges plus a temperature history.
/// Everything static (gradient, tracks, ticks, labels, panel) is pre-rendered once.
/// </summary>
public sealed class KlassikTheme : ITheme
{
    private const float ArcStart = 135f, ArcSweep = 270f, SegDeg = 3f;
    private const int HistoryLen = 90;

    private static readonly Color Dim = Color.FromArgb(120, 126, 145);
    private static readonly Color Track = Color.FromArgb(30, 34, 46);
    private static readonly Color Bright = Color.FromArgb(225, 230, 240);
    private static readonly Color CpuAccent = Color.FromArgb(0, 200, 255);
    private static readonly Color GpuAccent = Color.FromArgb(170, 110, 255);

    private readonly Bitmap _background;
    private readonly Font _value, _label, _small, _tiny;
    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen[] _segPens, _glowPens;
    private readonly Pen _loadPen, _cpuLine, _gpuLine;
    private readonly StringFormat _center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private readonly StringFormat _right = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };

    public int FrameMs => 1000;

    public LedLook Led(LedSource source, float tempC) => new(Pal.Gauge(tempC));

    public KlassikTheme()
    {
        _value = Pal.Font(Pal.Condensed, 42);
        _label = Pal.Font(Pal.Semi, 12);
        _small = Pal.Font(Pal.Semi, 15);
        _tiny = Pal.Font(Pal.Regular, 10);

        // segment colours depend only on arc position, so the pens are built once
        int segs = (int)(ArcSweep / SegDeg);
        _segPens = new Pen[segs];
        _glowPens = new Pen[segs];
        for (int i = 0; i < segs; i++)
        {
            var c = Pal.Scale((i + 0.5f) / segs);
            _segPens[i] = new Pen(c, 9);
            _glowPens[i] = new Pen(Color.FromArgb(45, c), 17);
        }
        _loadPen = new Pen(Bright, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        _cpuLine = new Pen(CpuAccent, 2) { LineJoin = LineJoin.Round };
        _gpuLine = new Pen(GpuAccent, 2) { LineJoin = LineJoin.Round };

        _background = BuildBackground();
    }

    private static (float Cx, float Cy, float R) Gauge(int i) => (Scene.W * (0.25f + 0.5f * i), 80, 58);

    private static RectangleF Circle(float cx, float cy, float r) => new(cx - r, cy - r, 2 * r, 2 * r);

    private static RectangleF Panel => new(8, 156, Scene.W - 16, Scene.H - 164);

    private static RectangleF Plot
    {
        get
        {
            var p = Panel;
            return new RectangleF(p.X + 8, p.Y + 18, p.Width - 16, p.Height - 24);
        }
    }

    private Bitmap BuildBackground()
    {
        var bg = new Bitmap(Scene.W, Scene.H, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using (var grad = new LinearGradientBrush(new Rectangle(0, 0, Scene.W, Scene.H),
                   Color.FromArgb(16, 19, 28), Color.FromArgb(5, 6, 10), LinearGradientMode.Vertical))
            g.FillRectangle(grad, 0, 0, Scene.W, Scene.H);

        using var trackPen = new Pen(Track, 9);
        using var innerTrack = new Pen(Track, 3);
        using var tickPen = new Pen(Color.FromArgb(58, 64, 82), 1.5f);
        using var brush = new SolidBrush(Dim);
        string[] names = ["CPU", "GPU"];
        Color[] accents = [CpuAccent, GpuAccent];

        for (int i = 0; i < 2; i++)
        {
            var (cx, cy, r) = Gauge(i);
            g.DrawArc(trackPen, Circle(cx, cy, r), ArcStart, ArcSweep);
            g.DrawArc(innerTrack, Circle(cx, cy, r - 13), ArcStart, ArcSweep);
            for (int t = 0; t <= 10; t++)
            {
                double a = (ArcStart + t * ArcSweep / 10) * Math.PI / 180;
                float r0 = r + 7, r1 = r + (t % 5 == 0 ? 12 : 10);
                g.DrawLine(tickPen,
                    cx + r0 * (float)Math.Cos(a), cy + r0 * (float)Math.Sin(a),
                    cx + r1 * (float)Math.Cos(a), cy + r1 * (float)Math.Sin(a));
            }
            brush.Color = accents[i];
            g.DrawString(names[i], _label, brush, new PointF(cx, cy + 22), _center);
        }

        var panel = Panel;
        using (var path = RoundedRect(panel, 8))
        using (var fill = new SolidBrush(Color.FromArgb(11, 13, 20)))
        using (var border = new Pen(Track, 1))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var plot = Plot;
        using (var grid = new Pen(Color.FromArgb(26, 30, 40), 1) { DashStyle = DashStyle.Dot })
            for (int k = 0; k <= 2; k++)
            {
                float y = plot.Y + plot.Height * k / 2;
                g.DrawLine(grid, plot.X, y, plot.Right, y);
            }

        float lx = panel.X + 8, ly = panel.Y + 4;
        brush.Color = CpuAccent;
        g.FillEllipse(brush, lx, ly + 3, 6, 6);
        g.DrawString("CPU", _tiny, brush, lx + 8, ly);
        brush.Color = GpuAccent;
        g.FillEllipse(brush, lx + 40, ly + 3, 6, 6);
        g.DrawString("GPU", _tiny, brush, lx + 48, ly);
        brush.Color = Dim;
        g.DrawString($"TEMP · {HistoryLen} s", _tiny, brush, lx + 82, ly);
        return bg;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Draw(Scene s)
    {
        s.G.DrawImageUnscaled(_background, 0, 0);
        DrawGauge(s.G, 0, "CPU", s.Now.CpuTemp, s.Now.CpuLoad);
        DrawGauge(s.G, 1, "GPU", s.Now.GpuTemp, s.Now.GpuLoad);
        DrawHistory(s);
    }

    private void DrawGauge(Graphics g, int i, string name, float? temp, float? load)
    {
        var (cx, cy, rad) = Gauge(i);
        var rect = Circle(cx, cy, rad);

        if (temp is { } t)
        {
            float end = Pal.Pos(t) * ArcSweep;
            // GDI+ throws OutOfMemoryException on near-zero sweeps, so the float remainder is cut off
            int n = 0;
            while (n < _segPens.Length && end - n * SegDeg >= 0.1f) n++;
            // glow without overlap (alpha would stack), solid with slight overlap (hides AA seams)
            for (int s = 0; s < n; s++)
                g.DrawArc(_glowPens[s], rect, ArcStart + s * SegDeg, Math.Min(SegDeg, end - s * SegDeg));
            for (int s = 0; s < n; s++)
                g.DrawArc(_segPens[s], rect, ArcStart + s * SegDeg, Math.Min(SegDeg + 0.6f, end - s * SegDeg));

            double a = (ArcStart + end) * Math.PI / 180;
            float ex = cx + rad * (float)Math.Cos(a), ey = cy + rad * (float)Math.Sin(a), d = 7;
            _brush.Color = Color.White;
            g.FillEllipse(_brush, ex - d / 2, ey - d / 2, d, d);
        }

        if (load is { } l && l >= 0.5f)
            g.DrawArc(_loadPen, Circle(cx, cy, rad - 13), ArcStart, ArcSweep * Math.Clamp(l, 0, 100) / 100f);

        // same scale as the arc, so the number and the arc tip always agree
        _brush.Color = temp is { } tc ? Pal.Gauge(tc) : Dim;
        g.DrawString(temp is { } tv ? $"{tv:0}°" : "--", _value, _brush, new PointF(cx + 5, cy - 6), _center);

        _brush.Color = Bright;
        g.DrawString(load is { } lv ? $"{lv:0}%" : "--%", _small, _brush, new PointF(cx, cy + 45), _center);
    }

    private void DrawHistory(Scene s)
    {
        int first = s.CpuTemps.Length - HistoryLen;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int k = first; k < s.CpuTemps.Length; k++)
        {
            foreach (float v in (ReadOnlySpan<float>)[s.CpuTemps[k], s.GpuTemps[k]])
                if (!float.IsNaN(v)) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        }
        if (lo > hi) return;

        lo = MathF.Floor(lo - 3);
        hi = MathF.Ceiling(hi + 3);
        if (hi - lo < 10) { float mid = (hi + lo) / 2; lo = mid - 5; hi = mid + 5; }

        var plot = Plot;
        DrawSeries(s.G, s.GpuTemps, first, _gpuLine, GpuAccent, plot, lo, hi);
        DrawSeries(s.G, s.CpuTemps, first, _cpuLine, CpuAccent, plot, lo, hi);

        var panel = Panel;
        _brush.Color = Dim;
        s.G.DrawString($"{lo:0}–{hi:0}°", _tiny, _brush, panel.Right - 8, panel.Y + 4, _right);
    }

    private static void DrawSeries(Graphics g, float[] hist, int first, Pen line, Color accent, RectangleF plot, float lo, float hi)
    {
        var pts = new List<PointF>(HistoryLen);
        for (int k = 0; k <= HistoryLen; k++)
        {
            if (k < HistoryLen && !float.IsNaN(hist[first + k]))
            {
                float v = hist[first + k];
                pts.Add(new PointF(plot.X + plot.Width * k / (HistoryLen - 1),
                                   plot.Bottom - plot.Height * (v - lo) / (hi - lo)));
                continue;
            }
            // gap (sensor missing) or end: flush the current run
            if (pts.Count >= 2)
            {
                using var area = new GraphicsPath();
                area.AddLines(pts.ToArray());
                area.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, plot.Bottom);
                area.AddLine(pts[^1].X, plot.Bottom, pts[0].X, plot.Bottom);
                area.CloseFigure();
                using var fill = new LinearGradientBrush(new PointF(0, plot.Y - 1), new PointF(0, plot.Bottom + 1),
                    Color.FromArgb(80, accent), Color.FromArgb(0, accent));
                g.FillPath(fill, area);
                g.DrawLines(line, pts.ToArray());
            }
            pts.Clear();
        }
    }

    public void Dispose()
    {
        foreach (var p in _segPens) p.Dispose();
        foreach (var p in _glowPens) p.Dispose();
        _loadPen.Dispose();
        _cpuLine.Dispose();
        _gpuLine.Dispose();
        _value.Dispose();
        _label.Dispose();
        _small.Dispose();
        _tiny.Dispose();
        _brush.Dispose();
        _center.Dispose();
        _right.Dispose();
        _background.Dispose();
    }
}
