using System.Text;
using System.Text.Json;

namespace UltraLightRgb;

public sealed record OutputStatus(string Name, string State, string? Error);

/// <summary>Snapshot the service hands to the switcher: readings, active theme, device health.</summary>
public sealed record ServiceStatus
{
    /// <summary>Upper bound when reading: the file is only ever a few hundred bytes.</summary>
    public const int MaxBytes = 64 * 1024;

    public DateTime TimeUtc { get; init; }

    public string Theme { get; init; } = "";

    /// <summary>Why this theme is shown: "user", "night", "auto" or "pinned".</summary>
    public string ThemeReason { get; init; } = "user";

    public float? CpuTemp { get; init; }
    public float? CpuLoad { get; init; }
    public float? GpuTemp { get; init; }
    public float? GpuLoad { get; init; }
    public float? RamLoad { get; init; }
    public float? VramUsedMb { get; init; }
    public float? VramTotalMb { get; init; }
    public float? PumpRpm { get; init; }

    /// <summary>Every fan with its speed, so the switcher can offer them as pump.</summary>
    public FanReading[] Fans { get; init; } = [];

    public bool Lcd { get; init; }

    public string? LcdError { get; init; }

    public OutputStatus[] Outputs { get; init; } = [];

    /// <summary>Why the user settings were rejected, if they were.</summary>
    public string? SettingsProblem { get; init; }

    public static string DefaultPath => Path.Combine(ThemeCatalog.DataDir, "status", "status.json");

    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ServiceStatus? Read(string path) => Read(path, out _);

    /// <summary>
    /// Null when there is no status yet or it cannot be read; <paramref name="problem"/> then says why
    /// (null for "not written yet"), so the switcher can show it instead of failing silently.
    /// </summary>
    public static ServiceStatus? Read(string path, out string? problem)
    {
        problem = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.Length > MaxBytes)
            {
                problem = $"{info.Name} has {info.Length} bytes, more than {MaxBytes}";
                return null;
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<ServiceStatus>(text, Json);
        }
        catch (JsonException e)
        {
            // the service replaces the file in one step, so this is a damaged file, not a race
            problem = $"status file is damaged: {e.Message}";
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"status file not readable: {e.GetType().Name}: {e.Message}";
            return null;
        }
    }
}

/// <summary>
/// Writes the status only while a switcher window is open: the switcher touches the alive file
/// every few seconds, and without a fresh touch the service writes nothing at all.
/// </summary>
public sealed class StatusWriter(string statusPath, string alivePath)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(5);

    public static StatusWriter Default { get; } = new(ServiceStatus.DefaultPath,
        Path.Combine(ThemeCatalog.DataDir, "state", "switcher.alive"));

    public bool IsWatched(DateTime nowUtc)
    {
        var info = new FileInfo(alivePath);
        if (!info.Exists) return false;
        var age = nowUtc - info.LastWriteTimeUtc;
        // a timestamp in the future would otherwise keep the writes going forever
        return age >= -Fresh && age <= Fresh;
    }

    public bool WriteIfWatched(ServiceStatus status, DateTime nowUtc)
    {
        if (!IsWatched(nowUtc)) return false;
        string temp = statusPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status, ServiceStatus.Json), new UTF8Encoding(false));
        File.Move(temp, statusPath, overwrite: true);
        return true;
    }
}
