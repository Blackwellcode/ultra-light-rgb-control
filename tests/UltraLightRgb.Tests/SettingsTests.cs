using System.Text;

namespace UltraLightRgb.Tests;

/// <summary>Runs only where this account may create symbolic links (admin or Developer Mode), skipped otherwise.</summary>
public sealed class SymlinkFactAttribute : FactAttribute
{
    public SymlinkFactAttribute()
    {
        if (!CanCreateSymlinks()) Skip = "creating symbolic links needs admin rights or Developer Mode";
    }

    private static bool CanCreateSymlinks()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "symlink-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "t"), "");
            File.CreateSymbolicLink(Path.Combine(dir, "l"), Path.Combine(dir, "t"));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static UserSettings Load(string path, out string? problem) => UserSettings.Load(path, out problem);

    [Fact]
    public void Missing_file_means_defaults_without_a_problem()
    {
        var s = Load(Path.Combine(_dir, "none.json"), out var problem);
        Assert.Equal(UserSettings.Default, s);
        Assert.Null(problem);
        Assert.Equal(85, s.AlarmC);
        Assert.Equal(100, s.Brightness.Aura);
        Assert.False(s.Night.Enabled);
        Assert.False(s.Auto.Enabled);
    }

    [Fact]
    public void Written_settings_are_read_back_unchanged()
    {
        var s = UserSettings.Default with
        {
            Brightness = new LedBrightness { Aura = 40, Board = 0, Gpu = 75, OpenRgb = 10 },
            AlarmC = 80,
            ScaleMinC = 30,
            ScaleMaxC = 90,
            Night = new NightSettings { Enabled = true, From = "22:30", To = "06:45", Brightness = 5, Theme = "nachtwache" },
            Auto = new AutoSettings { Enabled = true, GpuLoad = 70, HoldSeconds = 20, Theme = "inferno" },
        };
        string path = Path.Combine(_dir, "settings.json");
        s.Save(path);

        Assert.Equal(s, Load(path, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void Keys_left_out_keep_their_defaults()
    {
        var s = Load(Write("""{ "alarmC": 90, "brightness": { "gpu": 0 } }"""), out var problem);
        Assert.Null(problem);
        Assert.Equal(90, s.AlarmC);
        Assert.Equal(0, s.Brightness.Gpu);
        Assert.Equal(100, s.Brightness.Aura);
        Assert.Equal(95, s.ScaleMaxC);
    }

    [Theory]
    [InlineData("""{ "brightness": { "aura": 101 } }""", "brightness.aura")]
    [InlineData("""{ "brightness": { "board": -1 } }""", "brightness.board")]
    [InlineData("""{ "alarmC": 69 }""", "alarmC")]
    [InlineData("""{ "alarmC": 101 }""", "alarmC")]
    [InlineData("""{ "scaleMinC": 61 }""", "scaleMinC")]
    [InlineData("""{ "scaleMaxC": 111 }""", "scaleMaxC")]
    [InlineData("""{ "scaleMinC": 50, "scaleMaxC": 60 }""", "scale")]
    [InlineData("""{ "night": { "from": "25:00" } }""", "night.from")]
    [InlineData("""{ "night": { "to": "7 Uhr" } }""", "night.to")]
    [InlineData("""{ "night": { "theme": "razer" } }""", "night.theme")]
    [InlineData("""{ "night": { "brightness": 200 } }""", "night.brightness")]
    [InlineData("""{ "auto": { "gpuLoad": 20 } }""", "auto.gpuLoad")]
    [InlineData("""{ "auto": { "holdSeconds": 1 } }""", "auto.holdSeconds")]
    [InlineData("""{ "auto": { "theme": "" } }""", "auto.theme")]
    [InlineData("""{ "night": null }""", "night")]
    public void Out_of_range_value_rejects_the_file_and_names_the_key(string json, string key)
    {
        var s = Load(Write(json), out var problem);
        Assert.Equal(UserSettings.Default, s);
        Assert.NotNull(problem);
        Assert.Contains(key, problem);
    }

    [Fact]
    public void Unknown_key_rejects_the_file()
    {
        var s = Load(Write("""{ "alarmC": 90, "pumpSpeed": 100 }"""), out var problem);
        Assert.Equal(UserSettings.Default, s);
        Assert.NotNull(problem);
    }

    [Fact]
    public void Broken_json_rejects_the_file()
    {
        var s = Load(Write("""{ "alarmC": """), out var problem);
        Assert.Equal(UserSettings.Default, s);
        Assert.NotNull(problem);
    }

    [Fact]
    public void File_larger_than_4_kb_is_not_parsed()
    {
        // every user may write this file and SYSTEM reads it: size is capped before parsing
        string json = "{ \"alarmC\": 90" + new string(' ', 5000) + "}";
        var s = Load(Write(json), out var problem);
        Assert.Equal(UserSettings.Default, s);
        Assert.Contains("4096", problem);
    }

    [Fact]
    public void Utf8_byte_order_mark_is_accepted()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""{ "alarmC": 75 }""")]);
        Assert.Equal(75, Load(path, out var problem).AlarmC);
        Assert.Null(problem);
    }

    [SymlinkFact]
    public void Symbolic_link_is_refused()
    {
        string target = Path.Combine(_dir, "elsewhere.json");
        File.WriteAllText(target, """{ "alarmC": 90 }""");
        string link = Path.Combine(_dir, "settings.json");
        File.CreateSymbolicLink(link, target);

        var s = Load(link, out var problem);
        Assert.Equal(UserSettings.Default, s);
        Assert.Contains("link", problem);
    }
}
