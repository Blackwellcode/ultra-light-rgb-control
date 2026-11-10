using System.Drawing;
using System.Drawing.Imaging;

namespace UltraLightRgb;

/// <summary>
/// Giant digits cut out of a sine plasma. The field is computed once and only the palette
/// index moves per frame, the classic trick that keeps this cheap: load sets the flow speed,
/// temperature the colour.
/// </summary>
public sealed class SchmelzeTheme : ITheme
{
    private const int W = Scene.W, H = Scene.H;

    private readonly byte[] _field = new byte[W * H], _mask = new byte[W * H];
    private readonly uint[] _bright = new uint[256], _dim = new uint[256];
    private readonly Bitmap _maskBmp = new(W, H, PixelFormat.Format24bppRgb);
    private readonly Graphics _maskG;
    private readonly Font _huge = Pal.Font(Pal.Condensed, 196), _mid = Pal.Font(Pal.Condensed, 72), _small = Pal.Font(Pal.Semi, 15);
    private readonly SolidBrush _white = new(Color.White);
    private readonly StringFormat _fmt = new(StringFormat.GenericTypographic);
    private string _maskKey = "";
    private int _paletteTemp = int.MinValue;
    private float _flow;

    public int FrameMs => 80;

    private static readonly Color Violet = Color.FromArgb(110, 0, 255), Pink = Color.FromArgb(255, 0, 170),
                                  Orange = Color.FromArgb(255, 110, 0), Depth = Color.FromArgb(60, 0, 150);

    private static Color Hot(float tempC) => Pal.Ramp(Pal.Pos(tempC), Violet, Pink, Orange);

    public LedLook Led(LedSource source, float tempC) => tempC >= Pal.AlarmC
        ? new LedLook(Pal.Alarm)
        : new LedLook(Hot(tempC), Color.FromArgb(0, 70, 255), 4);

    public SchmelzeTheme()
    {
        _maskG = Graphics.FromImage(_maskBmp);
        _maskG.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        _maskG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = MathF.Sin(x / 21f) + MathF.Sin(y / 13f) + MathF.Sin((x + y) / 27f)
                        + MathF.Sin(MathF.Sqrt((x - 160) * (x - 160) + (y - 120) * (y - 120)) / 11f);
                _field[y * W + x] = (byte)((v + 4) * 31.8f);
            }
    }

    private void BuildPalette(int tempC)
    {
        var hot = Hot(tempC);
        var deep = Pal.Mix(hot, Depth, 0.8f);
        var flash = Pal.Mix(hot, Color.White, 0.35f);
        for (int i = 0; i < 256; i++)
        {
            // one full cycle, so the wrap at 255 stays invisible
            float f = 0.5f + 0.5f * MathF.Sin(i / 256f * MathF.Tau * 2);
            var c = Pal.Ramp(f, deep, hot, flash);
            _bright[i] = Pal.Rgb(c);
            _dim[i] = Pal.Rgb(Pal.Mix(Color.FromArgb(3, 3, 7), c, 0.09f));
        }
    }

    private void Text(string s, Font f, float x, float y, StringAlignment h)
    {
        _fmt.Alignment = h;
        _maskG.DrawString(s, f, _white, x, y, _fmt);
    }

    private unsafe void BuildMask(Scene s)
    {
        _maskG.Clear(Color.Black);
        Text(s.Now.CpuTemp is { } c ? $"{c:0}" : "--", _huge, 6, 14, StringAlignment.Near);
        Text(Scene.Deg(s.Now.GpuTemp), _mid, W - 8, 16, StringAlignment.Far);
        Text("GPU", _small, W - 10, 0, StringAlignment.Far);
        Text("CPU", _small, 10, 0, StringAlignment.Near);

        // load bars are part of the cut-out too
        _maskG.FillRectangle(_white, 214, 112, 96 * s.CpuLoad / 100, 9);
        _maskG.FillRectangle(_white, 214, 146, 96 * s.GpuLoad / 100, 9);
        Text($"CPU {Scene.Pct(s.Now.CpuLoad)}", _small, 214, 92, StringAlignment.Near);
        Text($"GPU {Scene.Pct(s.Now.GpuLoad)}", _small, 214, 126, StringAlignment.Near);

        var d = _maskBmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int y = 0; y < H; y++)
            {
                byte* row = (byte*)d.Scan0 + y * d.Stride;
                for (int x = 0; x < W; x++) _mask[y * W + x] = row[x * 3];
            }
        }
        finally
        {
            _maskBmp.UnlockBits(d);
        }
    }

    public unsafe void Draw(Scene s)
    {
        int t = (int)MathF.Round(s.Now.CpuTemp ?? 30);
        if (t != _paletteTemp)
        {
            _paletteTemp = t;
            BuildPalette(t);
        }

        string key = $"{s.Now.CpuTemp:0}|{s.Now.GpuTemp:0}|{s.Now.CpuLoad:0}|{s.Now.GpuLoad:0}";
        if (key != _maskKey)
        {
            _maskKey = key;
            BuildMask(s);
        }

        _flow += 2 + s.CpuLoad * 0.11f;
        byte shift = (byte)_flow;

        var bmp = s.Bmp;
        var d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < H; y++)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                int start = y * W;
                for (int x = 0; x < W; x++)
                {
                    byte i = (byte)(_field[start + x] + shift);
                    int m = _mask[start + x];
                    if (m == 0) row[x] = _dim[i];
                    else if (m == 255) row[x] = _bright[i];
                    else row[x] = Blend(_dim[i], _bright[i], m);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(d);
        }
    }

    private static uint Blend(uint a, uint b, int m)
    {
        int n = 255 - m;
        uint rb = ((a & 0xFF00FF) * (uint)n + (b & 0xFF00FF) * (uint)m) >> 8 & 0xFF00FF;
        uint g = ((a & 0x00FF00) * (uint)n + (b & 0x00FF00) * (uint)m) >> 8 & 0x00FF00;
        return rb | g;
    }

    public void Dispose()
    {
        _maskG.Dispose();
        _maskBmp.Dispose();
        _huge.Dispose();
        _mid.Dispose();
        _small.Dispose();
        _white.Dispose();
        _fmt.Dispose();
    }
}

/// <summary>Metaballs: CPU and GPU are the two big blobs, load makes the small ones race.</summary>
public sealed class LavalampeTheme : ITheme
{
    private const int CellW = 107, CellH = 80, Cell = 3;

    private readonly byte[] _cells = new byte[CellW * CellH];
    private readonly uint[] _palette = new uint[256];
    private readonly Font _big = Pal.Font(Pal.Condensed, 74), _mid = Pal.Font(Pal.Condensed, 42), _label = Pal.Font(Pal.Semi, 13);
    private int _paletteTemp = int.MinValue;
    private float _phase;

    public int FrameMs => 70;

    private static readonly Color Rose = Color.FromArgb(255, 0, 110), Ember = Color.FromArgb(255, 40, 30),
                                  Gold = Color.FromArgb(255, 150, 0), Glass = Color.FromArgb(30, 0, 255);

    private static Color Hot(float tempC) => Pal.Ramp(Pal.Pos(tempC), Rose, Ember, Gold);

    public LedLook Led(LedSource source, float tempC) => tempC >= Pal.AlarmC
        ? new LedLook(Pal.Alarm)
        : new LedLook(Hot(tempC), Glass, 2);

    private void BuildPalette(int tempC)
    {
        var hot = Hot(tempC);
        var wall = Color.FromArgb(4, 5, 12);
        var halo = Pal.Mix(wall, hot, 0.3f);
        var rim = Pal.Mix(hot, Color.White, 0.6f);
        var core = Pal.Mix(hot, Color.FromArgb(120, 0, 70), 0.75f);
        for (int i = 0; i < 256; i++)
        {
            float f = i / 255f;
            // sharp rim where the field crosses the threshold, that is what reads as a liquid
            var c = f < 0.36f ? Pal.Mix(wall, halo, f / 0.36f)
                  : f < 0.40f ? Pal.Mix(halo, rim, (f - 0.36f) / 0.04f)
                  : Pal.Ramp((f - 0.40f) / 0.60f, rim, hot, core);
            _palette[i] = Pal.Rgb(c);
        }
    }

    public void Draw(Scene s)
    {
        int t = (int)MathF.Round(s.Hottest);
        if (t != _paletteTemp)
        {
            _paletteTemp = t;
            BuildPalette(t);
        }

        float load = MathF.Max(s.CpuLoad, s.GpuLoad);
        _phase += 0.015f + load * 0.0009f;
        float p = _phase;

        float cpuR = 6 + Pal.Pos(s.Now.CpuTemp ?? 30) * 10, gpuR = 6 + Pal.Pos(s.Now.GpuTemp ?? 30) * 10;
        float small = 3 + load * 0.03f;
        Span<(float X, float Y, float R2)> balls =
        [
            (30 + 14 * MathF.Sin(p * 1.1f), 40 + 26 * MathF.Sin(p * 0.7f), cpuR * cpuR),
            (77 + 14 * MathF.Sin(p * 0.9f + 2), 40 + 26 * MathF.Sin(p * 0.8f + 1), gpuR * gpuR),
            (53 + 44 * MathF.Sin(p * 1.7f), 40 + 34 * MathF.Sin(p * 2.3f + 1), small * small),
            (53 + 46 * MathF.Sin(p * 2.1f + 2), 40 + 32 * MathF.Sin(p * 1.3f), small * small),
            (53 + 40 * MathF.Sin(p * 1.3f + 4), 40 + 36 * MathF.Sin(p * 1.9f + 3), small * small),
        ];

        for (int y = 0; y < CellH; y++)
        {
            for (int x = 0; x < CellW; x++)
            {
                float f = 0;
                foreach (var b in balls)
                {
                    float dx = x - b.X, dy = y - b.Y;
                    f += b.R2 / (dx * dx + dy * dy + 1);
                }
                _cells[y * CellW + x] = (byte)Math.Min(255f, f * 70);
            }
        }
        s.Blit(_cells, CellW, CellH, Cell, _palette);

        s.Shadow("CPU", _label, Color.White, 12, 8);
        s.Shadow(Scene.Deg(s.Now.CpuTemp), _big, Color.White, 10, 24);
        s.Shadow(Scene.Pct(s.Now.CpuLoad), _label, Color.White, 12, 104);
        s.Shadow("GPU", _label, Color.White, Scene.W - 12, 142, StringAlignment.Far);
        s.Shadow(Scene.Deg(s.Now.GpuTemp), _mid, Color.White, Scene.W - 10, 158, StringAlignment.Far);
        s.Shadow(Scene.Pct(s.Now.GpuLoad), _label, Color.White, Scene.W - 12, 206, StringAlignment.Far);
    }

    public void Dispose()
    {
        _big.Dispose();
        _mid.Dispose();
        _label.Dispose();
    }
}
