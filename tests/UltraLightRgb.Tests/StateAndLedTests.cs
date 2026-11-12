using System.Drawing;
using System.Text;

namespace UltraLightRgb.Tests;

public sealed class ThemeStateTests : IDisposable
{
    // next to the test binary, not in %TEMP%
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "state-tests", Guid.NewGuid().ToString("N"));

    public ThemeStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_(byte[] content)
    {
        string path = Path.Combine(_dir, "theme.txt");
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Missing_file_means_default()
    {
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.ReadState(Path.Combine(_dir, "none.txt")));
    }

    [Fact]
    public void Known_id_with_whitespace_is_read()
    {
        Assert.Equal("inferno", ThemeCatalog.ReadState(File_(Encoding.ASCII.GetBytes(" inferno\r\n"))));
    }

    [Fact]
    public void Utf8_and_utf16_byte_order_marks_are_understood()
    {
        Assert.Equal("phosphor", ThemeCatalog.ReadState(File_([0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes("phosphor")])));
        Assert.Equal("split", ThemeCatalog.ReadState(File_([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("split")])));
    }

    [Fact]
    public void Unknown_id_means_default()
    {
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.ReadState(File_(Encoding.ASCII.GetBytes("razer"))));
    }

    [Fact]
    public void Only_the_first_64_bytes_count()
    {
        // the file is writable by every user and read by SYSTEM: a huge file must not be loaded
        byte[] content = [.. Enumerable.Repeat((byte)' ', 64), .. Encoding.ASCII.GetBytes("hologramm")];
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.ReadState(File_(content)));
    }

    [Fact]
    public void Id_followed_by_a_megabyte_of_junk_is_unknown()
    {
        byte[] content = [.. Encoding.ASCII.GetBytes("inferno"), .. new byte[1_000_000]];
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.ReadState(File_(content)));
    }
}

public class LogThrottleTests
{
    [Fact]
    public void Same_message_is_logged_once_per_interval()
    {
        var t = new LogThrottle(60_000);
        Assert.True(t.ShouldLog("locked", now: 1_000));
        Assert.False(t.ShouldLog("locked", now: 2_000));
        Assert.False(t.ShouldLog("locked", now: 60_999));
        Assert.True(t.ShouldLog("locked", now: 61_000));
    }

    [Fact]
    public void A_different_message_is_logged_at_once()
    {
        var t = new LogThrottle(60_000);
        Assert.True(t.ShouldLog("locked", now: 1_000));
        Assert.True(t.ShouldLog("denied", now: 1_500));
    }

    [Fact]
    public void After_a_success_the_same_message_counts_as_new()
    {
        var t = new LogThrottle(60_000);
        Assert.True(t.ShouldLog("locked", now: 1_000));
        t.Reset();
        Assert.True(t.ShouldLog("locked", now: 1_200));
    }
}

public class TempLedTests
{
    private sealed class FakeLed(List<LedLook> sets, ManualResetEventSlim signal) : ILed
    {
        public void Set(LedLook look, float phase)
        {
            lock (sets) sets.Add(look);
            signal.Set();
        }

        public void Dispose() { }
    }

    private static readonly LedLook Cold = new(Color.FromArgb(0, 0, 255));

    [Fact]
    public void First_reading_is_shown_unsmoothed()
    {
        var sets = new List<LedLook>();
        using var signal = new ManualResetEventSlim();
        var temps = new List<float>();
        using (var led = new TempLed("test", LedSource.Cpu, () => new FakeLed(sets, signal), intervalMs: 1000))
        {
            led.Update(50f, 10f, t => { temps.Add(t); return Cold; });
            Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "the LED thread never set a colour");
        }
        Assert.Equal([50f], temps);
        Assert.Equal(Cold, sets[0]);
    }

    [Fact]
    public void Later_readings_drift_towards_the_new_temperature()
    {
        var temps = new List<float>();
        using var signal = new ManualResetEventSlim();
        using var led = new TempLed("test", LedSource.Gpu, () => new FakeLed([], signal), intervalMs: 1000);
        led.Update(50f, 0f, t => { temps.Add(t); return Cold; });
        led.Update(100f, 0f, t => { temps.Add(t); return Cold; });

        // 1 s interval, 8 s smoothing: 50 + (1 - e^(-1/8)) * 50 = 55.8752
        Assert.Equal(55.8752, temps[1], 3);
    }

    [Fact]
    public void Missing_reading_keeps_the_last_colour()
    {
        int looks = 0;
        using var signal = new ManualResetEventSlim();
        using var led = new TempLed("test", LedSource.Cpu, () => new FakeLed([], signal), intervalMs: 1000);
        led.Update(null, 5f, _ => { looks++; return Cold; });
        Assert.Equal(0, looks);
    }

    [Fact]
    public void Unsupported_hardware_is_opened_once_and_then_left_alone()
    {
        int opens = 0;
        using var opened = new ManualResetEventSlim();
        using (var led = new TempLed("test", LedSource.Cpu, () =>
               {
                   Interlocked.Increment(ref opens);
                   opened.Set();
                   throw new NotSupportedException("no such device");
               }, intervalMs: 1000))
        {
            led.Update(40f, 0f, _ => Cold);
            Assert.True(opened.Wait(TimeSpan.FromSeconds(5)), "the LED thread never tried to open the device");
            led.Update(41f, 0f, _ => Cold);
            Thread.Sleep(600); // three idle cycles of the LED thread
        }
        Assert.Equal(1, opens);
    }
}
