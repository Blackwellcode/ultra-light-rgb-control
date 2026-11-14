using System.Drawing;

namespace UltraLightRgb.Tests;

/// <summary>Tests that read or change the process-wide palette run one after another.</summary>
[CollectionDefinition("Palette", DisableParallelization = true)]
public sealed class PaletteCollection;

[Collection("Palette")]
public sealed class PaletteTests : IDisposable
{
    public void Dispose() => Pal.Configure(UserSettings.Default);

    private static UserSettings Scale(int alarm, int min, int max) =>
        UserSettings.Default with { AlarmC = alarm, ScaleMinC = min, ScaleMaxC = max };

    [Fact]
    public void Defaults_put_the_ends_of_the_scale_at_20_and_95_degrees()
    {
        Pal.Configure(UserSettings.Default);
        Assert.Equal(Color.FromArgb(0, 200, 255).ToArgb(), Pal.Gauge(20).ToArgb());
        Assert.Equal(Color.FromArgb(255, 60, 60).ToArgb(), Pal.Gauge(95).ToArgb());
        Assert.False(Pal.IsAlarm(84.9f));
        Assert.True(Pal.IsAlarm(85));
    }

    [Fact]
    public void Configured_scale_moves_both_ends()
    {
        Pal.Configure(Scale(80, 30, 90));
        Assert.Equal(Color.FromArgb(0, 200, 255).ToArgb(), Pal.Gauge(30).ToArgb());
        Assert.Equal(Color.FromArgb(255, 60, 60).ToArgb(), Pal.Gauge(90).ToArgb());
        // 60 degrees is the middle: a fifth of the way from green (0.45) to amber (0.7)
        Assert.Equal(Color.FromArgb(115, 222, 120).ToArgb(), Pal.Gauge(60).ToArgb());
    }

    [Fact]
    public void Configured_alarm_threshold_applies()
    {
        Pal.Configure(Scale(75, 20, 95));
        Assert.False(Pal.IsAlarm(74.9f));
        Assert.True(Pal.IsAlarm(75));
    }

    [Fact]
    public void Themes_use_the_configured_alarm_colour()
    {
        Pal.Configure(Scale(75, 20, 95));
        using var split = ThemeCatalog.Create("split");
        Assert.Equal(Pal.Alarm.ToArgb(), split.Led(LedSource.Cpu, 76).A.ToArgb());
        Assert.NotEqual(Pal.Alarm.ToArgb(), split.Led(LedSource.Cpu, 74).A.ToArgb());
    }
}
