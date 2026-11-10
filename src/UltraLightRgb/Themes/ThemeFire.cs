using System.Drawing;
using System.Drawing.Drawing2D;

namespace UltraLightRgb;

/// <summary>
/// Cell fire simulation: load is the fuel and sets the flame height, high temperatures push
/// the colour towards white heat.
/// </summary>
public sealed class InfernoTheme : ITheme
{
    private const int CellW = 107, CellH = 80, Cell = 3;

    private static readonly Color Black = Color.Black;
    private static readonly Color[] Flame =
        [Black, Color.FromArgb(128, 12, 0), Color.FromArgb(255, 88, 0), Color.FromArgb(255, 205, 36), Color.FromArgb(255, 255, 215)];
    private static readonly Color[] White =
        [Black, Color.FromArgb(190, 16, 40), Color.FromArgb(255, 110, 40), Color.FromArgb(255, 240, 170), Color.White];

    // two extra rows below the visible grid hold the embers
    private readonly byte[] _heat = new byte[CellW * (CellH + 2)];
    private readonly uint[] _palette = new uint[256];
    private readonly Font _big = Pal.Font(Pal.Condensed, 104), _mid = Pal.Font(Pal.Semi, 15), _label = Pal.Font(Pal.Semi, 13);
    private readonly SolidBrush _strip = new(Color.FromArgb(150, 0, 0, 0));
    private int _paletteTemp = int.MinValue;
    private uint _rand = 2463534242;

    public int FrameMs => 70;

    public LedLook Led(LedSource source, float tempC) => tempC >= Pal.AlarmC
        ? new LedLook(Pal.Alarm)
        : new LedLook(ColorAt(tempC, 0.62f), ColorAt(tempC, 0.22f), 7);

    /// <summary>Position f on the flame palette belonging to this temperature.</summary>
    private static Color ColorAt(float tempC, float f) =>
        Pal.Mix(Pal.Ramp(f, Flame), Pal.Ramp(f, White), Math.Clamp((tempC - 74) / 16, 0, 1));

    private uint Next()
    {
        _rand ^= _rand << 13;
        _rand ^= _rand >> 17;
        _rand ^= _rand << 5;
        return _rand;
    }

    private void Step(float load)
    {
        int fuel = (int)(170 + load * 0.85f);
        // embers come in patches, single cells would only give a uniform wall
        for (int x = 0; x < CellW;)
        {
            int run = 3 + (int)(Next() & 7);
            byte ember = (Next() & 3) == 0 ? (byte)(Next() & 31) : (byte)Math.Min(255, fuel + (int)(Next() & 31));
            for (int i = 0; i < run && x < CellW; i++, x++)
            {
                _heat[CellH * CellW + x] = ember;
                _heat[(CellH + 1) * CellW + x] = ember;
            }
        }

        // less load burns out sooner, so the flames stay low
        int decay = 1 + (int)((100 - load) / 18);
        for (int y = 0; y < CellH; y++)
        {
            int row = y * CellW, below = row + CellW, below2 = below + CellW;
            for (int x = 0; x < CellW; x++)
            {
                // the sideways drift is what breaks the rows up into tongues
                int drift = (Next() & 7) switch { 0 => -1, 1 => 1, _ => 0 };
                int c = (x + drift + CellW) % CellW;
                int l = (c + CellW - 1) % CellW, r = (c + 1) % CellW;
                int v = (_heat[below + l] + _heat[below + c] + _heat[below + r] + _heat[below2 + c]) >> 2;
                _heat[row + x] = (byte)Math.Max(0, v - decay - (int)(Next() & 3));
            }
        }
    }

    public void Draw(Scene s)
    {
        int t = (int)MathF.Round(s.Now.CpuTemp ?? 30);
        if (t != _paletteTemp)
        {
            _paletteTemp = t;
            for (int i = 0; i < 256; i++) _palette[i] = Pal.Rgb(ColorAt(t, i / 255f));
        }

        Step(s.CpuLoad);
        s.Blit(_heat, CellW, CellH, Cell, _palette);

        s.Shadow("CPU", _label, Color.White, 12, 8);
        s.Shadow(Scene.Deg(s.Now.CpuTemp), _big, Color.White, 164, 62, StringAlignment.Center, StringAlignment.Center);

        s.G.FillRectangle(_strip, 0, 214, Scene.W, 26);
        s.Text($"CPU {Scene.Pct(s.Now.CpuLoad)}", _mid, Color.White, 12, 219);
        s.Text($"GPU {Scene.Deg(s.Now.GpuTemp)}  {Scene.Pct(s.Now.GpuLoad)}", _mid, Color.White, Scene.W - 12, 219, StringAlignment.Far);
    }

    public void Dispose()
    {
        _big.Dispose();
        _mid.Dispose();
        _label.Dispose();
        _strip.Dispose();
    }
}
