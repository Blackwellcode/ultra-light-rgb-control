using System.Drawing;

namespace UltraLightRgb.Tests;

public class BrightnessTests
{
    private static readonly LedLook Look = new(Color.FromArgb(200, 100, 51), Color.FromArgb(10, 20, 30), Flow: 4);

    [Fact]
    public void Full_brightness_leaves_the_look_alone()
    {
        Assert.Equal(Look, Look.Dim(100));
    }

    [Fact]
    public void Half_brightness_halves_every_channel_of_both_colours()
    {
        var half = Look.Dim(50);
        Assert.Equal(Color.FromArgb(100, 50, 25).ToArgb(), half.A.ToArgb());
        Assert.Equal(Color.FromArgb(5, 10, 15).ToArgb(), half.B.ToArgb());
        Assert.Equal(4, half.Flow);
    }

    [Fact]
    public void Zero_is_off()
    {
        var off = Look.Dim(0);
        Assert.Equal(Color.Black.ToArgb(), off.A.ToArgb());
        Assert.Equal(Color.Black.ToArgb(), off.B.ToArgb());
    }

    [Fact]
    public void Aura_report_of_a_dimmed_look_carries_the_dimmed_bytes()
    {
        var report = new byte[AuraLed.ReportLen];
        AuraLed.DirectReport(report, 4, 0, 2, apply: true, new LedLook(Color.FromArgb(200, 100, 50)).Dim(25), 0);
        Assert.Equal(new byte[] { 0xEC, 0x40, 0x84, 0, 2, 50, 25, 12, 50, 25, 12 }, report[..11]);
    }

    [Fact]
    public void Aura_report_when_off_is_all_black()
    {
        var report = new byte[AuraLed.ReportLen];
        AuraLed.DirectReport(report, 0, 0, 20, apply: false, new LedLook(Color.White).Dim(0), 0);
        Assert.All(report[5..65], b => Assert.Equal(0, b));
    }
}
