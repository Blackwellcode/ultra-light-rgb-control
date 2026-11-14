namespace UltraLightRgb.Tests;

public class ModeTests
{
    private static readonly DateTime Day = new(2026, 11, 14);

    private static DateTime At(int h, int m) => Day.AddHours(h).AddMinutes(m);

    private static UserSettings Night(string from, string to, int brightness = 20, string theme = "") =>
        UserSettings.Default with { Night = new NightSettings { Enabled = true, From = from, To = to, Brightness = brightness, Theme = theme } };

    private static Mode Resolve(ModeResolver r, UserSettings s, DateTime local, string? pinned = null) =>
        r.Resolve("klassik", pinned, s, local, nowMs: 0, gpuLoad: null);

    [Theory]
    [InlineData(22, 30, true)]
    [InlineData(21, 59, false)]
    [InlineData(23, 0, false)]
    public void Night_window_within_one_day(int h, int m, bool night)
    {
        Assert.Equal(night, Resolve(new ModeResolver(), Night("22:00", "23:00"), At(h, m)).Night);
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(3, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(22, 59, false)]
    public void Night_window_across_midnight(int h, int m, bool night)
    {
        Assert.Equal(night, Resolve(new ModeResolver(), Night("23:00", "07:00"), At(h, m)).Night);
    }

    [Fact]
    public void Disabled_night_is_never_active()
    {
        var s = Night("00:00", "23:59") with { Night = new NightSettings { Enabled = false, From = "00:00", To = "23:59" } };
        Assert.False(Resolve(new ModeResolver(), s, At(12, 0)).Night);
    }

    [Fact]
    public void Equal_start_and_end_means_no_window()
    {
        Assert.False(Resolve(new ModeResolver(), Night("22:00", "22:00"), At(22, 0)).Night);
    }

    [Fact]
    public void Night_theme_replaces_the_chosen_theme_during_the_window()
    {
        var s = Night("23:00", "07:00", theme: "nachtwache");
        Assert.Equal(new Mode("nachtwache", "night", true), Resolve(new ModeResolver(), s, At(1, 0)));
        Assert.Equal(new Mode("klassik", "user", false), Resolve(new ModeResolver(), s, At(12, 0)));
    }

    [Fact]
    public void Night_without_a_theme_keeps_the_chosen_one_but_still_dims()
    {
        var r = new ModeResolver();
        Assert.Equal(new Mode("klassik", "user", true), Resolve(r, Night("23:00", "07:00", brightness: 20), At(1, 0)));
        Assert.Equal(20, r.Brightness(100));
        Assert.Equal(10, r.Brightness(50));
        Assert.Equal(0, r.Brightness(0));
    }

    [Fact]
    public void Brightness_is_untouched_outside_the_window()
    {
        var r = new ModeResolver();
        Resolve(r, Night("23:00", "07:00", brightness: 20), At(12, 0));
        Assert.Equal(75, r.Brightness(75));
    }

    private static UserSettings Auto(int load = 80, int hold = 15, string theme = "inferno") =>
        UserSettings.Default with { Auto = new AutoSettings { Enabled = true, GpuLoad = load, HoldSeconds = hold, Theme = theme } };

    /// <summary>Feeds one GPU load sample per second from <paramref name="startSecond"/> on; returns the last mode.</summary>
    private static Mode Feed(ModeResolver r, UserSettings s, int startSecond, int seconds, float? load)
    {
        Mode m = default;
        for (int i = 0; i < seconds; i++)
            m = r.Resolve("klassik", null, s, At(12, 0), nowMs: (startSecond + i) * 1000L, gpuLoad: load);
        return m;
    }

    [Fact]
    public void Sustained_gpu_load_switches_after_the_hold_time()
    {
        var r = new ModeResolver();
        var s = Auto(load: 80, hold: 15);
        // samples at 0..14 s: above the threshold for 14 s, not yet 15
        Assert.Equal("user", Feed(r, s, 0, 15, 90).Reason);
        Assert.Equal(new Mode("inferno", "auto", false), Feed(r, s, 15, 1, 90));
    }

    [Fact]
    public void Short_spike_does_not_switch()
    {
        var r = new ModeResolver();
        var s = Auto(load: 80, hold: 15);
        Feed(r, s, 0, 10, 95);
        Assert.Equal("user", Feed(r, s, 10, 30, 40).Reason);
    }

    [Fact]
    public void Theme_returns_only_after_the_load_stayed_low_for_the_hold_time()
    {
        var r = new ModeResolver();
        var s = Auto(load: 80, hold: 15);
        Feed(r, s, 0, 16, 90); // busy since 15 s
        Assert.Equal("auto", Feed(r, s, 16, 15, 20).Reason); // low at 16..30 s: 14 s
        Assert.Equal("user", Feed(r, s, 31, 1, 20).Reason); // low for 15 s
    }

    [Fact]
    public void A_dip_while_busy_does_not_switch_back()
    {
        var r = new ModeResolver();
        var s = Auto(load: 80, hold: 15);
        Feed(r, s, 0, 16, 90);
        Feed(r, s, 16, 5, 30); // loading screen
        Assert.Equal("auto", Feed(r, s, 21, 30, 90).Reason);
    }

    [Fact]
    public void Missing_gpu_reading_counts_as_idle()
    {
        var r = new ModeResolver();
        var s = Auto(load: 80, hold: 5);
        Assert.Equal("user", Feed(r, s, 0, 20, null).Reason);
    }

    [Fact]
    public void Disabling_the_automatic_returns_at_once()
    {
        var r = new ModeResolver();
        Feed(r, Auto(hold: 5), 0, 6, 90);
        Assert.Equal("user", Feed(r, UserSettings.Default, 6, 1, 90).Reason);
    }

    [Fact]
    public void Automatic_wins_over_night()
    {
        var r = new ModeResolver();
        var s = Night("23:00", "07:00", theme: "nachtwache") with { Auto = Auto(hold: 5).Auto };
        Mode m = default;
        for (int i = 0; i < 6; i++) m = r.Resolve("klassik", null, s, At(1, 0), i * 1000L, 95);
        Assert.Equal(new Mode("inferno", "auto", true), m);
    }

    [Fact]
    public void Pinned_theme_wins_over_night()
    {
        var s = Night("23:00", "07:00", theme: "nachtwache");
        Assert.Equal(new Mode("inferno", "pinned", true), Resolve(new ModeResolver(), s, At(1, 0), pinned: "inferno"));
    }
}
