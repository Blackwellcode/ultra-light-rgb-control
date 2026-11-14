using System.Drawing;
using System.Globalization;

namespace UltraLightRgb;

/// <summary>Shared look of the information themes: dark ground, quiet labels, values in the scale colours.</summary>
internal static class InfoStyle
{
    public static readonly Color Ground = Color.FromArgb(6, 7, 11);
    public static readonly Color Label = Color.FromArgb(108, 114, 132);
    public static readonly Color Text = Color.FromArgb(232, 236, 244);
    public static readonly Color Track = Color.FromArgb(28, 32, 44);
    public static readonly Color Hair = Color.FromArgb(34, 38, 50);

    public static Color Temp(float? t) => t is { } v ? (Pal.IsAlarm(v) ? Pal.Alarm : Pal.Gauge(v)) : Label;

    public static LedLook Led(float tempC) => new(Pal.IsAlarm(tempC) ? Pal.Alarm : Pal.Gauge(tempC));

    public static string Num(float v) => v.ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>Time first, temperatures second: for a case that sits in view all day.</summary>
public sealed class UhrTheme : ITheme
{
    private readonly Font _time = Pal.Font(Pal.Light, 92), _date = Pal.Font(Pal.Semi, 13),
                          _value = Pal.Font(Pal.Condensed, 34), _label = Pal.Font(Pal.Semi, 11), _load = Pal.Font(Pal.Regular, 12);
    private readonly SolidBrush _brush = new(Color.White);

    public int FrameMs => 1000;

    public LedLook Led(LedSource source, float tempC) => InfoStyle.Led(tempC);

    public void Draw(Scene s)
    {
        var g = s.G;
        var now = DateTime.Now;
        g.Clear(InfoStyle.Ground);

        s.Text(now.ToString("HH:mm", CultureInfo.InvariantCulture), _time, InfoStyle.Text, Scene.W / 2f, 66,
            StringAlignment.Center, StringAlignment.Center);

        // seconds as a thin line, in the colour of the hotter part
        const float x0 = 52, x1 = Scene.W - 52, y = 122;
        _brush.Color = InfoStyle.Track;
        g.FillRectangle(_brush, x0, y, x1 - x0, 2);
        _brush.Color = InfoStyle.Temp(s.Hottest > 0 ? s.Hottest : null);
        g.FillRectangle(_brush, x0, y, (x1 - x0) * (now.Second + 1) / 60f, 2);

        s.Text(now.ToString("ddd  dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), _date, InfoStyle.Label,
            Scene.W / 2f, 134, StringAlignment.Center);

        Column(s, Scene.W * 0.27f, "CPU", s.Now.CpuTemp, s.Now.CpuLoad);
        Column(s, Scene.W * 0.73f, "GPU", s.Now.GpuTemp, s.Now.GpuLoad);
        _brush.Color = InfoStyle.Hair;
        g.FillRectangle(_brush, Scene.W / 2f, 176, 1, 44);
    }

    private void Column(Scene s, float cx, string name, float? temp, float? load)
    {
        s.Text(name, _label, InfoStyle.Label, cx, 170, StringAlignment.Center);
        s.Text(Scene.Deg(temp), _value, InfoStyle.Temp(temp), cx, 184, StringAlignment.Center);
        s.Text(Scene.Pct(load), _load, InfoStyle.Label, cx, 222, StringAlignment.Center);
    }

    public void Dispose()
    {
        _time.Dispose();
        _date.Dispose();
        _value.Dispose();
        _label.Dispose();
        _load.Dispose();
        _brush.Dispose();
    }
}

/// <summary>Two numbers, readable from across the room. Nothing else.</summary>
public sealed class MinimalTheme : ITheme
{
    private readonly Font _big = Pal.Font(Pal.Condensed, 118), _label = Pal.Font(Pal.Semi, 13), _load = Pal.Font(Pal.Regular, 13);
    private readonly SolidBrush _hair = new(InfoStyle.Hair);

    public int FrameMs => 1000;

    public LedLook Led(LedSource source, float tempC) => InfoStyle.Led(tempC);

    public void Draw(Scene s)
    {
        s.G.Clear(Color.Black);
        Half(s, Scene.W * 0.25f, "CPU", s.Now.CpuTemp, s.Now.CpuLoad);
        Half(s, Scene.W * 0.75f, "GPU", s.Now.GpuTemp, s.Now.GpuLoad);
        s.G.FillRectangle(_hair, Scene.W / 2f, 54, 1, 132);
    }

    private void Half(Scene s, float cx, string name, float? temp, float? load)
    {
        s.Text(name, _label, InfoStyle.Label, cx, 30, StringAlignment.Center);
        s.Text(temp is { } t ? InfoStyle.Num(t) : "--", _big, InfoStyle.Temp(temp), cx, 120,
            StringAlignment.Center, StringAlignment.Center);
        s.Text(Scene.Pct(load), _load, InfoStyle.Label, cx, 196, StringAlignment.Center);
    }

    public void Dispose()
    {
        _big.Dispose();
        _label.Dispose();
        _load.Dispose();
        _hair.Dispose();
    }
}

/// <summary>Everything the cooling depends on: temperatures, pump, fans, memory.</summary>
public sealed class SystemTheme : ITheme
{
    private const float Pad = 8, Gap = 6;

    // below this the pump is probably stuck or unplugged: the most important warning of an AIO
    private const float PumpLowRpm = 500;

    private static readonly Color Panel = Color.FromArgb(14, 16, 24);
    private static readonly Color Ok = Color.FromArgb(80, 230, 140), Warn = Color.FromArgb(255, 190, 40);

    private readonly Font _value = Pal.Font(Pal.Condensed, 34), _label = Pal.Font(Pal.Semi, 11),
                          _small = Pal.Font(Pal.Semi, 13), _tiny = Pal.Font(Pal.Regular, 11);
    private readonly SolidBrush _brush = new(Color.White);

    public int FrameMs => 1000;

    public LedLook Led(LedSource source, float tempC) => InfoStyle.Led(tempC);

    public void Draw(Scene s)
    {
        var g = s.G;
        g.Clear(InfoStyle.Ground);
        float half = (Scene.W - 2 * Pad - Gap) / 2;

        Chip(s, Pad, Pad, half, "CPU", s.Now.CpuTemp, s.Now.CpuLoad);
        Chip(s, Pad + half + Gap, Pad, half, "GPU", s.Now.GpuTemp, s.Now.GpuLoad);
        Cooling(s, Pad, 82, Scene.W - 2 * Pad, 70);

        var n = s.Now;
        Meter(s, Pad, 160, half, "RAM", n.RamLoad,
            n.RamUsedGb is { } u && n.RamTotalGb is { } t ? $"{u:0.0} / {t:0} GB" : "");
        Meter(s, Pad + half + Gap, 160, half, "VRAM",
            n.VramUsedMb is { } vu && n.VramTotalMb is { } vt && vt > 0 ? vu / vt * 100 : null,
            n.VramUsedMb is { } a && n.VramTotalMb is { } b ? $"{a / 1024:0.0} / {b / 1024:0} GB" : "");
    }

    private void Box(Graphics g, float x, float y, float w, float h)
    {
        _brush.Color = Panel;
        g.FillRectangle(_brush, x, y, w, h);
    }

    private void Chip(Scene s, float x, float y, float w, string name, float? temp, float? load)
    {
        Box(s.G, x, y, w, 68);
        s.Text(name, _label, InfoStyle.Label, x + 10, y + 7);
        s.Text(Scene.Deg(temp), _value, InfoStyle.Temp(temp), x + 8, y + 22);
        s.Text(Scene.Pct(load), _small, InfoStyle.Text, x + w - 10, y + 7, StringAlignment.Far);
        Bar(s.G, x + w - 62, y + 50, 52, load ?? 0, InfoStyle.Text);
    }

    private void Cooling(Scene s, float x, float y, float w, float h)
    {
        var n = s.Now;
        Box(s.G, x, y, w, h);
        s.Text("PUMP", _label, InfoStyle.Label, x + 10, y + 7);
        if (n.PumpRpm is { } rpm)
        {
            var state = rpm < PumpLowRpm ? Pal.Alarm : Ok;
            _brush.Color = state;
            s.G.FillEllipse(_brush, x + 48, y + 10, 7, 7);
            s.Text(InfoStyle.Num(rpm), _value, rpm < PumpLowRpm ? Pal.Alarm : InfoStyle.Text, x + 8, y + 22);
            s.Text("RPM", _tiny, InfoStyle.Label, x + 96, y + 44);
        }
        else
        {
            s.Text("not set", _small, InfoStyle.Label, x + 10, y + 32);
        }

        // board fans that turn, without the pump, then the GPU fans as one average
        var fans = (n.Fans ?? [])
            .Where(f => !f.Name.StartsWith("GPU", StringComparison.Ordinal) && f.Rpm > 0 && f.Rpm != n.PumpRpm)
            .Take(2)
            .Select(f => (Name: f.Name.Replace("Fan #", "FAN ").ToUpperInvariant(), f.Rpm))
            .ToList();
        var gpuFans = (n.Fans ?? []).Where(f => f.Name.StartsWith("GPU", StringComparison.Ordinal)).ToList();
        if (gpuFans.Count > 0) fans.Add(("GPU FANS", gpuFans.Average(f => f.Rpm)));

        float rowY = y + 9;
        foreach (var (name, fanRpm) in fans)
        {
            s.Text(name, _label, InfoStyle.Label, x + 150, rowY);
            s.Text(InfoStyle.Num(fanRpm), _small, fanRpm > 0 ? InfoStyle.Text : Warn, x + w - 10, rowY - 2, StringAlignment.Far);
            rowY += 19;
        }
    }

    private void Meter(Scene s, float x, float y, float w, string name, float? percent, string detail)
    {
        Box(s.G, x, y, w, 72);
        s.Text(name, _label, InfoStyle.Label, x + 10, y + 7);
        s.Text(Scene.Pct(percent), _small, InfoStyle.Text, x + w - 10, y + 6, StringAlignment.Far);
        Bar(s.G, x + 10, y + 32, w - 20, percent ?? 0, Pal.Scale((percent ?? 0) / 100f));
        s.Text(detail, _tiny, InfoStyle.Label, x + 10, y + 46);
    }

    private void Bar(Graphics g, float x, float y, float w, float percent, Color fill)
    {
        _brush.Color = InfoStyle.Track;
        g.FillRectangle(_brush, x, y, w, 6);
        _brush.Color = fill;
        g.FillRectangle(_brush, x, y, w * Math.Clamp(percent, 0, 100) / 100, 6);
    }

    public void Dispose()
    {
        _value.Dispose();
        _label.Dispose();
        _small.Dispose();
        _tiny.Dispose();
        _brush.Dispose();
    }
}
