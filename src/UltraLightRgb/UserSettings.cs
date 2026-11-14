using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraLightRgb;

/// <summary>Brightness per LED group in percent; 0 switches the group off.</summary>
public sealed record LedBrightness
{
    /// <summary>The addressable AURA header (cooler and fan chain).</summary>
    public int Aura { get; init; } = 100;

    /// <summary>The LEDs on the mainboard itself.</summary>
    public int Board { get; init; } = 100;

    public int Gpu { get; init; } = 100;

    public int OpenRgb { get; init; } = 100;
}

/// <summary>Time window with dimmed LEDs and, optionally, a calm theme. The window may span midnight.</summary>
public sealed record NightSettings
{
    public bool Enabled { get; init; }

    public string From { get; init; } = "23:00";

    public string To { get; init; } = "07:00";

    /// <summary>Percent of the normal brightness during the window.</summary>
    public int Brightness { get; init; } = 20;

    /// <summary>Theme during the window; empty keeps the chosen one.</summary>
    public string Theme { get; init; } = "";
}

/// <summary>Switches to a theme while the GPU is busy (a game), and back once it calms down.</summary>
public sealed record AutoSettings
{
    public bool Enabled { get; init; }

    /// <summary>GPU load in percent that counts as busy.</summary>
    public int GpuLoad { get; init; } = 80;

    /// <summary>How long the load must stay above or below the threshold before the theme changes.</summary>
    public int HoldSeconds { get; init; } = 15;

    public string Theme { get; init; } = "inferno";
}

/// <summary>
/// What a normal user may change from the switcher. The file is writable by every user and read by
/// the SYSTEM service, so it is capped in size, links are refused, unknown keys and out-of-range
/// values reject the whole file. A rejected file means defaults, never a partial mix.
/// </summary>
public sealed record UserSettings
{
    public const int MaxBytes = 4096;

    public LedBrightness Brightness { get; init; } = new();

    /// <summary>From here on every theme shows the alarm colour.</summary>
    public int AlarmC { get; init; } = 85;

    /// <summary>Temperature at the cold end of the colour scale.</summary>
    public int ScaleMinC { get; init; } = 20;

    /// <summary>Temperature at the hot end of the colour scale.</summary>
    public int ScaleMaxC { get; init; } = 95;

    public NightSettings Night { get; init; } = new();

    public AutoSettings Auto { get; init; } = new();

    public static UserSettings Default { get; } = new();

    public static string DefaultPath => Path.Combine(ThemeCatalog.DataDir, "state", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    /// <summary>Missing file: defaults, no problem. Anything unusable: defaults plus the reason.</summary>
    public static UserSettings Load(string path, out string? problem)
    {
        problem = null;
        var info = new FileInfo(path);
        if (!info.Exists && info.LinkTarget is null) return Default;

        // a link could make SYSTEM read a file the user cannot read
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            problem = $"{info.Name} is a symbolic link or junction, refused";
            return Default;
        }
        if (info.Length > MaxBytes)
        {
            problem = $"{info.Name} has {info.Length} bytes, more than {MaxBytes}";
            return Default;
        }

        var buf = new byte[MaxBytes + 1];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        if (n > MaxBytes)
        {
            problem = $"{info.Name} grew beyond {MaxBytes} bytes while reading";
            return Default;
        }

        using var reader = new StreamReader(new MemoryStream(buf, 0, n), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        try
        {
            var s = JsonSerializer.Deserialize<UserSettings>(text, Json) ?? throw new JsonException("file is empty or null");
            problem = s.Problem();
            return problem is null ? s : Default;
        }
        catch (JsonException e)
        {
            problem = $"{info.Name} is not valid: {e.Message}";
            return Default;
        }
    }

    /// <summary>Writes the file in one step, so the service never reads half of it.</summary>
    public void Save(string path)
    {
        if (Problem() is { } why) throw new ArgumentException($"settings not saved: {why}");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Null when every value is usable, otherwise the first offending key and why.</summary>
    public string? Problem()
    {
        static string? Range(string key, int v, int min, int max) =>
            v < min || v > max ? $"{key} {v} is outside {min}..{max}" : null;

        static bool IsTime(string? s) =>
            TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        if (Brightness is null) return "brightness is missing";
        if (Night is null) return "night is missing";
        if (Auto is null) return "auto is missing";

        return Range("brightness.aura", Brightness.Aura, 0, 100)
            ?? Range("brightness.board", Brightness.Board, 0, 100)
            ?? Range("brightness.gpu", Brightness.Gpu, 0, 100)
            ?? Range("brightness.openRgb", Brightness.OpenRgb, 0, 100)
            ?? Range("alarmC", AlarmC, 70, 100)
            ?? Range("scaleMinC", ScaleMinC, 0, 60)
            ?? Range("scaleMaxC", ScaleMaxC, 50, 110)
            ?? (ScaleMaxC - ScaleMinC < 20 ? $"scale {ScaleMinC}..{ScaleMaxC} is narrower than 20 degrees" : null)
            ?? (IsTime(Night.From) ? null : $"night.from \"{Night.From}\" is not HH:mm")
            ?? (IsTime(Night.To) ? null : $"night.to \"{Night.To}\" is not HH:mm")
            ?? Range("night.brightness", Night.Brightness, 0, 100)
            ?? (Night.Theme is "" || (Night.Theme is not null && ThemeCatalog.Exists(Night.Theme)) ? null : $"night.theme \"{Night.Theme}\" is unknown")
            ?? Range("auto.gpuLoad", Auto.GpuLoad, 30, 100)
            ?? Range("auto.holdSeconds", Auto.HoldSeconds, 5, 300)
            ?? (Auto.Theme is not null && ThemeCatalog.Exists(Auto.Theme) ? null : $"auto.theme \"{Auto.Theme}\" is unknown");
    }
}
