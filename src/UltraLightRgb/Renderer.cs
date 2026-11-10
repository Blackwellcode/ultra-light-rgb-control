using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace UltraLightRgb;

/// <summary>
/// Owns the canvas and the active theme, and encodes the result into the device frame
/// (RGB565 LE, column-major, bottom-up).
/// </summary>
public sealed class Renderer : IDisposable
{
    // ordered dither: without it RGB565 turns every soft gradient into visible bands
    private static readonly byte[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    private readonly Scene _scene = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ITheme _theme;

    public string ThemeId { get; private set; }

    public Renderer(int width, int height, string themeId)
    {
        if (width != Scene.W || height != Scene.H)
            throw new NotSupportedException($"Themes are built for {Scene.W}x{Scene.H}, the display reports {width}x{height}");
        ThemeId = themeId;
        _theme = ThemeCatalog.Create(themeId);
    }

    public int FrameMs => _theme.FrameMs;

    public LedLook Led(LedSource source, float tempC) => _theme.Led(source, tempC);

    public void SetTheme(string id)
    {
        var next = ThemeCatalog.Create(id);
        _theme.Dispose();
        _theme = next;
        ThemeId = id;
    }

    public void Sample(Readings r) => _scene.Push(r);

    public void Draw()
    {
        _scene.Time = (float)_clock.Elapsed.TotalSeconds;
        _theme.Draw(_scene);
    }

    public void Render(byte[] frame, int pixelOffset)
    {
        Draw();
        Encode(frame, pixelOffset);
    }

    private unsafe void Encode(byte[] frame, int offset)
    {
        var bmp = _scene.Bmp;
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            byte* scan = (byte*)data.Scan0;
            int stride = data.Stride;
            fixed (byte* dst = frame)
            {
                byte* o = dst + offset;
                for (int x = 0; x < w; x++)
                {
                    // column x, from bottom row to top row; the device expects the last pixel of each column zeroed
                    for (int k = 0; k < h; k++, o += 2)
                    {
                        if (k == h - 1)
                        {
                            o[0] = o[1] = 0;
                            continue;
                        }
                        int y = h - 1 - k;
                        byte* p = scan + y * stride + x * 4;
                        int t = Bayer[(y & 3) << 2 | (x & 3)];
                        int b = Math.Min(255, p[0] + (t >> 1)), g = Math.Min(255, p[1] + (t >> 2)), r = Math.Min(255, p[2] + (t >> 1));
                        int v = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
                        o[0] = (byte)v;
                        o[1] = (byte)(v >> 8);
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public void SavePreview(string path) => _scene.Bmp.Save(path, ImageFormat.Png);

    public Bitmap Snapshot() => new(_scene.Bmp);

    public void Dispose()
    {
        _theme.Dispose();
        _scene.Dispose();
    }

    /// <summary>Renders a theme with made-up readings, for previews without hardware.</summary>
    public static Renderer Preview(string themeId)
    {
        var r = new Renderer(Scene.W, Scene.H, themeId);
        for (int i = 0; i < Scene.HistoryLen; i++) r.Sample(ThemeCatalog.Synthetic(i));
        // simulations need a moment until the effect has built up
        for (int i = 0; i < 90; i++) r.Draw();
        return r;
    }
}
