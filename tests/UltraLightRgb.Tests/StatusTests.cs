namespace UltraLightRgb.Tests;

public sealed class StatusTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 14, 1, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "status-tests", Guid.NewGuid().ToString("N"));
    private readonly string _status, _alive;
    private readonly StatusWriter _writer;

    public StatusTests()
    {
        Directory.CreateDirectory(_dir);
        _status = Path.Combine(_dir, "status.json");
        _alive = Path.Combine(_dir, "switcher.alive");
        _writer = new StatusWriter(_status, _alive);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly ServiceStatus Sample = new()
    {
        TimeUtc = Now,
        Theme = "inferno",
        ThemeReason = "user",
        CpuTemp = 61.5f,
        CpuLoad = 12f,
        GpuTemp = 48f,
        GpuLoad = null,
        Lcd = true,
        Outputs = [new OutputStatus("AURA header 0", "Ok", null), new OutputStatus("MSI GPU", "Retrying", "I2C error")],
    };

    private void Alive(DateTime touched)
    {
        File.WriteAllText(_alive, "");
        File.SetLastWriteTimeUtc(_alive, touched);
    }

    [Fact]
    public void Nothing_is_written_while_the_switcher_is_closed()
    {
        Assert.False(_writer.WriteIfWatched(Sample, Now));
        Assert.False(File.Exists(_status));
    }

    [Fact]
    public void Open_switcher_gets_the_status()
    {
        Alive(Now.AddSeconds(-2));
        Assert.True(_writer.WriteIfWatched(Sample, Now));

        var read = ServiceStatus.Read(_status);
        Assert.NotNull(read);
        Assert.Equal("inferno", read!.Theme);
        Assert.Equal(61.5f, read.CpuTemp);
        Assert.Null(read.GpuLoad);
        Assert.True(read.Lcd);
        Assert.Equal(2, read.Outputs.Length);
        Assert.Equal("Retrying", read.Outputs[1].State);
        Assert.Equal("I2C error", read.Outputs[1].Error);
    }

    [Fact]
    public void Switcher_gone_for_longer_than_five_seconds_stops_the_writes()
    {
        Alive(Now.AddSeconds(-6));
        Assert.False(_writer.WriteIfWatched(Sample, Now));
        Assert.False(File.Exists(_status));
    }

    [Fact]
    public void Timestamp_in_the_future_does_not_keep_the_writes_going()
    {
        // the alive file is user-writable: a far future date must not mean "watched forever"
        Alive(Now.AddMinutes(10));
        Assert.False(_writer.WriteIfWatched(Sample, Now));
    }

    [Fact]
    public void Missing_status_reads_as_null_without_a_problem()
    {
        Assert.Null(ServiceStatus.Read(_status, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void Broken_status_reads_as_null_and_says_why()
    {
        File.WriteAllText(_status, "{ not json");
        Assert.Null(ServiceStatus.Read(_status, out var problem));
        Assert.Contains("damaged", problem);
    }
}
