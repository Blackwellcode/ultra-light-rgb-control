namespace UltraLightRgb.Tests;

public sealed class SwitcherModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 14, 2, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "switcher-tests", Guid.NewGuid().ToString("N"));
    private readonly SwitcherModel _model;

    private string P(string name) => Path.Combine(_dir, name);

    public SwitcherModelTests()
    {
        Directory.CreateDirectory(_dir);
        _model = new SwitcherModel(P("settings.json"), P("theme.txt"), P("status.json"), P("switcher.alive"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Starts_from_the_saved_settings()
    {
        (UserSettings.Default with { AlarmC = 78 }).Save(P("settings.json"));
        _model.Load();
        Assert.Equal(78, _model.Settings.AlarmC);
        Assert.Null(_model.LoadProblem);
        Assert.False(_model.Dirty);
    }

    [Fact]
    public void A_broken_file_is_reported_and_defaults_are_shown()
    {
        File.WriteAllText(P("settings.json"), """{ "alarmC": 500 }""");
        _model.Load();
        Assert.Equal(UserSettings.Default, _model.Settings);
        Assert.Contains("alarmC", _model.LoadProblem);
    }

    [Fact]
    public void Valid_change_is_kept_and_written_once_on_flush()
    {
        _model.Load();
        Assert.True(_model.Update(s => s with { Brightness = s.Brightness with { Gpu = 40 } }, out var problem));
        Assert.Null(problem);
        Assert.True(_model.Dirty);

        Assert.True(_model.Flush());
        Assert.False(_model.Dirty);
        Assert.Equal(40, UserSettings.Load(P("settings.json"), out _).Brightness.Gpu);

        // nothing changed since: no second write
        Assert.False(_model.Flush());
    }

    [Fact]
    public void Invalid_change_is_refused_and_the_previous_settings_stay()
    {
        _model.Load();
        Assert.False(_model.Update(s => s with { ScaleMinC = 60, ScaleMaxC = 70 }, out var problem));
        Assert.Contains("scale", problem);
        Assert.Equal(UserSettings.Default, _model.Settings);
        Assert.False(_model.Dirty);
        Assert.False(File.Exists(P("settings.json")));
    }

    [Fact]
    public void Theme_choice_is_written_for_the_service()
    {
        _model.SetTheme("system");
        Assert.Equal("system", _model.Theme);
        Assert.Equal("system", File.ReadAllText(P("theme.txt")));
    }

    [Fact]
    public void Touch_creates_and_refreshes_the_alive_file()
    {
        _model.Touch(Now);
        Assert.Equal(Now, File.GetLastWriteTimeUtc(P("switcher.alive")));
        _model.Touch(Now.AddSeconds(2));
        Assert.Equal(Now.AddSeconds(2), File.GetLastWriteTimeUtc(P("switcher.alive")));
    }

    private void WriteStatus(DateTime written) => File.WriteAllText(P("status.json"), System.Text.Json.JsonSerializer.Serialize(
        new ServiceStatus
        {
            TimeUtc = written,
            Theme = "inferno",
            ThemeReason = "auto",
            CpuTemp = 61,
            CpuLoad = 20,
            GpuTemp = 55,
            GpuLoad = 90,
            RamLoad = 57,
            VramUsedMb = 6000,
            VramTotalMb = 12000,
            PumpRpm = 3090,
            Fans = [new FanReading("Fan #7", 3090)],
        }, ServiceStatus.Json));

    [Fact]
    public void Fresh_status_becomes_live_readings()
    {
        WriteStatus(Now.AddSeconds(-1));
        Assert.True(_model.RefreshStatus(Now));
        var r = _model.Live!.Value;
        Assert.Equal(61f, r.CpuTemp);
        Assert.Equal(90f, r.GpuLoad);
        Assert.Equal(57f, r.RamLoad);
        Assert.Equal(6000f, r.VramUsedMb);
        Assert.Equal(3090f, r.PumpRpm);
        Assert.Equal("Fan #7", r.Fans![0].Name);
        Assert.Equal("auto", _model.Status!.ThemeReason);
    }

    [Fact]
    public void Status_older_than_five_seconds_means_the_service_is_not_reachable()
    {
        WriteStatus(Now.AddSeconds(-6));
        Assert.False(_model.RefreshStatus(Now));
        Assert.NotNull(_model.Status);
    }

    [Fact]
    public void Missing_status_means_not_reachable_without_live_readings()
    {
        Assert.False(_model.RefreshStatus(Now));
        Assert.Null(_model.Live);
        Assert.Null(_model.StatusProblem);
    }
}
