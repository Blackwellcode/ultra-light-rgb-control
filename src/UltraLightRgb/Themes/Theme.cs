using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace UltraLightRgb;

public readonly record struct Readings(float? CpuTemp, float? CpuLoad, float? GpuTemp, float? GpuLoad)
{
    public float? RamLoad { get; init; }
    public float? RamUsedGb { get; init; }
    public float? RamTotalGb { get; init; }
    public float? VramUsedMb { get; init; }
    public float? VramTotalMb { get; init; }

    /// <summary>Board fans first, then GPU fans; headers without a fan read 0.</summary>
    public FanReading[]? Fans { get; init; }

    /// <summary>The board fan the user marked as pump, if any.</summary>
    public float? PumpRpm { get; init; }
}

public sealed record FanReading(string Name, float Rpm);

/// <summary>Which reading an LED output follows.</summary>
public enum LedSource { Cpu, Gpu }

/// <summary>
/// LED colours of a theme: addressable outputs blend both along the strip, single-colour outputs
/// only show A. Flow moves the pattern along the strip, in LEDs per second at medium load.
/// </summary>
public readonly record struct LedLook(Color A, Color B, float Flow = 0)
{
    /// <summary>The colour wave repeats every this many LEDs, so it fits any strip length.</summary>
    public const int PatternLeds = 10;

    public LedLook(Color single) : this(single, single) { }

    /// <summary>Colour of LED <paramref name="index"/> once the pattern has moved by <paramref name="phase"/> LEDs.</summary>
    public Color At(int index, float phase)
    {
        float wave = 0.5f + 0.5f * MathF.Sin((index - phase) * MathF.Tau / PatternLeds);
        return Pal.Mix(A, B, wave);
    }

    /// <summary>The same look at <paramref name="percent"/> brightness; 0 is off.</summary>
    public LedLook Dim(int percent)
    {
        if (percent >= 100) return this;
        int p = Math.Max(0, percent);
        static Color Scale(Color c, int p) => Color.FromArgb(c.R * p / 100, c.G * p / 100, c.B * p / 100);
        return this with { A = Scale(A, p), B = Scale(B, p) };
    }
}

/// <summary>One look of the LCD, including the colours the LEDs take on while it is active.</summary>
public interface ITheme : IDisposable
{
    /// <summary>Target time between frames; sensors keep their own, slower pace.</summary>
    int FrameMs { get; }

    LedLook Led(LedSource source, float tempC);

    void Draw(Scene s);
}

/// <summary>What a theme draws on and with: the 320x240 canvas plus readings and their history.</summary>
public sealed class Scene : IDisposable
{
    public const int W = 320, H = 240, HistoryLen = W;

    public readonly Bitmap Bmp = new(W, H, PixelFormat.Format32bppRgb);
    public readonly Graphics G;
    public readonly float[] CpuTemps = Empty(), GpuTemps = Empty(), CpuLoads = Empty(), GpuLoads = Empty();
    public Readings Now;

    /// <summary>Seconds since start, for time-based animation.</summary>
    public float Time;

    private readonly SolidBrush _brush = new(Color.White);
    private readonly StringFormat _fmt = new(StringFormat.GenericTypographic);

    public Scene()
    {
        G = Graphics.FromImage(Bmp);
        G.SmoothingMode = SmoothingMode.AntiAlias;
        G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private static float[] Empty()
    {
        var a = new float[HistoryLen];
        Array.Fill(a, float.NaN);
        return a;
    }

    public void Push(Readings r)
    {
        Now = r;
        Shift(CpuTemps, r.CpuTemp);
        Shift(GpuTemps, r.GpuTemp);
        Shift(CpuLoads, r.CpuLoad);
        Shift(GpuLoads, r.GpuLoad);
    }

    private static void Shift(float[] a, float? v)
    {
        Array.Copy(a, 1, a, 0, a.Length - 1);
        a[^1] = v ?? float.NaN;
    }

    public float CpuLoad => Now.CpuLoad ?? 0;
    public float GpuLoad => Now.GpuLoad ?? 0;
    public float Hottest => MathF.Max(Now.CpuTemp ?? 0, Now.GpuTemp ?? 0);

    public static string Deg(float? t) => t is { } v ? $"{v:0}°" : "--";
    public static string Pct(float? l) => l is { } v ? $"{v:0} %" : "-- %";

    public void Text(string s, Font f, Color c, float x, float y,
                     StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near)
    {
        _brush.Color = c;
        _fmt.Alignment = h;
        _fmt.LineAlignment = v;
        G.DrawString(s, f, _brush, x, y, _fmt);
    }

    /// <summary>Text with a dark halo, for reading on top of busy effects.</summary>
    public void Shadow(string s, Font f, Color c, float x, float y,
                       StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near)
    {
        var dark = Color.FromArgb(170, 0, 0, 0);
        Text(s, f, dark, x + 2, y + 2, h, v);
        Text(s, f, dark, x - 1, y + 1, h, v);
        Text(s, f, c, x, y, h, v);
    }

    /// <summary>Paints a palette-indexed low-res grid over the whole canvas.</summary>
    public unsafe void Blit(byte[] cells, int cw, int ch, int scale, uint[] palette)
    {
        var d = Bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < H; y++)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                int rowStart = Math.Min(y / scale, ch - 1) * cw;
                for (int x = 0; x < W; x++)
                    row[x] = palette[cells[rowStart + Math.Min(x / scale, cw - 1)]];
            }
        }
        finally
        {
            Bmp.UnlockBits(d);
        }
    }

    public void Dispose()
    {
        G.Dispose();
        Bmp.Dispose();
        _brush.Dispose();
        _fmt.Dispose();
    }
}

public static class Pal
{
    private sealed record Limits(float ScaleMin, float ScaleMax, float Alarm);

    // swapped as a whole, so a reader on another thread never sees half an update
    private static volatile Limits _limits = Of(UserSettings.Default);

    private static Limits Of(UserSettings s) => new(s.ScaleMinC, s.ScaleMaxC, s.AlarmC);

    /// <summary>Takes over colour scale and alarm threshold from the user settings.</summary>
    public static void Configure(UserSettings s) => _limits = Of(s);

    /// <summary>From here on every theme, however calm, must show the problem.</summary>
    public static float AlarmC => _limits.Alarm;

    public static bool IsAlarm(float tempC) => tempC >= _limits.Alarm;

    public static readonly Color Alarm = Color.FromArgb(255, 40, 30);

    // gauge scale: cool -> warm, position 0..1 along the arc
    private static readonly (float Pos, Color C)[] ScaleStops =
    [
        (0f, Color.FromArgb(0, 200, 255)),
        (0.45f, Color.FromArgb(80, 230, 140)),
        (0.7f, Color.FromArgb(255, 190, 40)),
        (1f, Color.FromArgb(255, 60, 60)),
    ];

    public static float Pos(float tempC)
    {
        var l = _limits;
        return Math.Clamp((tempC - l.ScaleMin) / (l.ScaleMax - l.ScaleMin), 0, 1);
    }

    public static Color Gauge(float tempC) => Scale(Pos(tempC));

    public static Color Scale(float pos)
    {
        for (int i = 1; i < ScaleStops.Length; i++)
        {
            var (p1, c1) = ScaleStops[i];
            if (pos > p1) continue;
            var (p0, c0) = ScaleStops[i - 1];
            return Mix(c0, c1, (pos - p0) / (p1 - p0));
        }
        return ScaleStops[^1].C;
    }

    private static readonly Color[] IronStops =
    [
        Color.FromArgb(40, 0, 110), Color.FromArgb(150, 0, 160), Color.FromArgb(235, 30, 70),
        Color.FromArgb(255, 140, 0), Color.FromArgb(255, 235, 120),
    ];

    /// <summary>Thermal camera palette.</summary>
    public static Color Iron(float pos) => Ramp(pos, IronStops);

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    /// <summary>Position f (0..1) on evenly spaced colour stops.</summary>
    public static Color Ramp(float f, params Color[] stops)
    {
        float p = Math.Clamp(f, 0, 1) * (stops.Length - 1);
        int i = Math.Min((int)p, stops.Length - 2);
        return Mix(stops[i], stops[i + 1], p - i);
    }

    public static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    public static Font Font(string family, float px) => new(family, px, FontStyle.Regular, GraphicsUnit.Pixel);

    public const string Condensed = "Bahnschrift SemiBold Condensed", Semi = "Bahnschrift SemiBold",
                        Regular = "Bahnschrift", Light = "Bahnschrift Light";
}

public static class ThemeCatalog
{
    public const string DefaultId = "klassik";

    public static readonly (string Id, string Title, Func<ITheme> Create)[] All =
    [
        // ids are stored in the state file: they stay stable, only titles may change
        ("klassik", "Classic", () => new KlassikTheme()),
        ("inferno", "Inferno", () => new InfernoTheme()),
        ("schmelze", "Meltdown", () => new SchmelzeTheme()),
        ("hologramm", "Hologram", () => new HologrammTheme()),
        ("phosphor", "Phosphor", () => new PhosphorTheme()),
        ("lavalampe", "Lava Lamp", () => new LavalampeTheme()),
        ("waermeband", "Heat Band", () => new WaermebandTheme()),
        ("split", "Split", () => new SplitTheme()),
        ("nachtwache", "Night Watch", () => new NachtwacheTheme()),
        ("uhr", "Clock", () => new UhrTheme()),
        ("minimal", "Minimal", () => new MinimalTheme()),
        ("system", "System", () => new SystemTheme()),
    ];

    public static ITheme Create(string id)
    {
        foreach (var t in All)
            if (t.Id == id) return t.Create();
        throw new ArgumentException($"Unknown theme: {id}", nameof(id));
    }

    public static bool Exists(string id) => All.Any(t => t.Id == id);

    /// <summary>Machine-wide folder of the service: configuration (admins only), state and logs.</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UltraLightRgb");

    /// <summary>The only link between switcher and service: one word in a file the service polls.</summary>
    public static string StatePath { get; } = Path.Combine(DataDir, "state", "theme.txt");

    public static void WriteState(string id) => WriteState(StatePath, id);

    internal static void WriteState(string path, string id)
    {
        // replace in one step, so the service never reads half a file
        string temp = path + ".tmp";
        File.WriteAllText(temp, id);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Default when nothing is saved; anything unknown counts as nothing saved.</summary>
    public static string ReadState() => ReadState(StatePath);

    /// <summary>Upper bound for the state file: every user may write it, SYSTEM reads it.</summary>
    internal const int MaxStateBytes = 64;

    internal static string ReadState(string path)
    {
        if (!File.Exists(path)) return DefaultId;
        var buf = new byte[MaxStateBytes];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        // StreamReader honours a UTF-8 or UTF-16 byte order mark, like File.ReadAllText did
        using var reader = new StreamReader(new MemoryStream(buf, 0, n), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string id = reader.ReadToEnd().Trim();
        return Exists(id) ? id : DefaultId;
    }

    /// <summary>Plausible made-up load curve, for previews without hardware.</summary>
    public static Readings Synthetic(int i)
    {
        float ramp = Math.Clamp((i - 110) / 70f, 0, 1);
        float pump = 3100 + 15 * MathF.Sin(i / 3f);
        return new Readings(
            44 + 27 * ramp + (1 + 3 * ramp) * MathF.Sin(i / 9f) + MathF.Sin(i / 2.3f),
            6 + 56 * ramp + 11 * ramp * MathF.Sin(i / 5f) + 4 * MathF.Sin(i / 1.3f),
            41 + 23 * ramp + 1.2f * MathF.Sin(i / 17f),
            7 + 81 * ramp + 8 * ramp * MathF.Sin(i / 7f) + 3 * MathF.Sin(i / 1.7f))
        {
            RamLoad = 48 + 9 * ramp,
            RamUsedGb = 15.2f + 2.8f * ramp,
            RamTotalGb = 31.6f,
            VramUsedMb = 2400 + 5600 * ramp,
            VramTotalMb = 12227,
            PumpRpm = pump,
            Fans =
            [
                new("Fan #2", 950 + 420 * ramp),
                new("Fan #7", pump),
                new("GPU Fan 1", 1100 + 900 * ramp),
                new("GPU Fan 2", 1120 + 900 * ramp),
            ],
        };
    }
}
