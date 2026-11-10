using System.Drawing;
using System.Text.Json;

namespace UltraLightRgb.Tests;

public class ConfigTests
{
    [Fact]
    public void Default_drives_the_verified_hardware()
    {
        Assert.Equal(["aura", "msi-gpu"], AppConfig.Default.Outputs.Select(o => o.Driver));
        Assert.True(AppConfig.Default.Lcd);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void Local_openrgb_hosts_are_accepted(string host)
    {
        var cfg = AppConfig.Parse($$"""{ "outputs": [ { "driver": "openrgb", "host": "{{host}}" } ] }""");
        Assert.Single(cfg.Outputs);
    }

    [Theory]
    [InlineData("""{ "driver": "openrgb", "host": "192.168.0.10" }""")]
    [InlineData("""{ "driver": "openrgb", "port": 0 }""")]
    [InlineData("""{ "driver": "openrgb", "match": " " }""")]
    [InlineData("""{ "driver": "aura", "header": 8 }""")]
    [InlineData("""{ "driver": "aura", "source": "ram" }""")]
    [InlineData("""{ "driver": "razer" }""")]
    public void Unusable_outputs_are_dropped(string output)
    {
        var cfg = AppConfig.Parse($$"""{ "lcd": false, "outputs": [ {{output}}, { "driver": "msi-gpu", "source": "gpu" } ] }""");
        Assert.Equal(["msi-gpu"], cfg.Outputs.Select(o => o.Driver));
        Assert.False(cfg.Lcd);
    }

    [Fact]
    public void Misspelt_option_is_an_error_not_silently_ignored()
    {
        Assert.Throws<JsonException>(() => AppConfig.Parse("""{ "outputs": [ { "driver": "aura", "headr": 1 } ] }"""));
    }

    [Fact]
    public void Shipped_example_configuration_is_valid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "devices.example.json"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var cfg = AppConfig.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "devices.example.json")));
        Assert.Equal(["aura", "msi-gpu", "openrgb"], cfg.Outputs.Select(o => o.Driver));
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var cfg = AppConfig.Parse("""
            {
              // the cooler chain
              "outputs": [ { "driver": "aura", "header": 1, "board": false }, ],
            }
            """);
        Assert.Equal(1, cfg.Outputs[0].Header);
        Assert.False(cfg.Outputs[0].Board);
    }
}

public class AuraTests
{
    [Fact]
    public void Header_n_switches_mode_on_channel_n_plus_one()
    {
        Assert.Equal(1, AuraLed.EffectChannel(0));
        Assert.Equal(3, AuraLed.EffectChannel(2));
    }

    [Fact]
    public void Direct_report_layout()
    {
        var report = new byte[AuraLed.ReportLen];
        AuraLed.DirectReport(report, 0, 20, 2, apply: true, new LedLook(Color.FromArgb(1, 2, 3)), 0);

        Assert.Equal(new byte[] { 0xEC, 0x40, 0x80, 20, 2, 1, 2, 3, 1, 2, 3, 0 }, report[..12]);
    }

    [Fact]
    public void Direct_report_takes_at_most_twenty_leds()
    {
        var report = new byte[AuraLed.ReportLen];
        Assert.Throws<ArgumentOutOfRangeException>(() => AuraLed.DirectReport(report, 0, 0, 21, false, new LedLook(Color.Red), 0));
    }
}

public class ThemeTests
{
    [Fact]
    public void Theme_ids_are_unique()
    {
        var ids = ThemeCatalog.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains(ThemeCatalog.DefaultId, ids);
    }

    [Fact]
    public void Every_theme_renders_and_has_led_colours()
    {
        foreach (var (id, _, _) in ThemeCatalog.All)
        {
            using var r = Renderer.Preview(id);
            using var snap = r.Snapshot();
            Assert.Equal(Scene.W, snap.Width);
            foreach (var src in new[] { LedSource.Cpu, LedSource.Gpu })
                foreach (float t in new[] { 25f, 60f, 95f })
                    _ = r.Led(src, t);
        }
    }

    [Fact]
    public void Single_colour_look_is_flat_along_the_strip()
    {
        var look = new LedLook(Color.FromArgb(9, 8, 7));
        for (int i = 0; i < 30; i++) Assert.Equal(Color.FromArgb(9, 8, 7).ToArgb(), look.At(i, 3.5f).ToArgb());
    }
}
