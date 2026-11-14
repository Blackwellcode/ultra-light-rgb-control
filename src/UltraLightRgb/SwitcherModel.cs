namespace UltraLightRgb;

/// <summary>
/// Everything the switcher window does besides drawing: settings it edits, the theme choice, the
/// service status it shows and the alive file that asks the service for that status.
/// </summary>
public sealed class SwitcherModel(string settingsPath, string themePath, string statusPath, string alivePath)
{
    private static readonly TimeSpan LiveWindow = TimeSpan.FromSeconds(5);

    public static SwitcherModel Default { get; } = new(UserSettings.DefaultPath, ThemeCatalog.StatePath,
        ServiceStatus.DefaultPath, Path.Combine(ThemeCatalog.DataDir, "state", "switcher.alive"));

    public UserSettings Settings { get; private set; } = UserSettings.Default;

    /// <summary>Why the saved settings were not usable; the window shows it, and saving replaces the file.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>Changed since the last save.</summary>
    public bool Dirty { get; private set; }

    public ServiceStatus? Status { get; private set; }

    public string? StatusProblem { get; private set; }

    /// <summary>The last status as readings for the live previews, null until the service wrote one.</summary>
    public Readings? Live { get; private set; }

    public string Theme => ThemeCatalog.ReadState(themePath);

    public void Load()
    {
        Settings = UserSettings.Load(settingsPath, out var problem);
        LoadProblem = problem;
        Dirty = false;
    }

    /// <summary>Applies <paramref name="change"/> unless the result is invalid; then nothing changes.</summary>
    public bool Update(Func<UserSettings, UserSettings> change, out string? problem)
    {
        var next = change(Settings);
        problem = next.Problem();
        if (problem is not null) return false;
        if (next == Settings) return true;
        Settings = next;
        Dirty = true;
        return true;
    }

    /// <summary>Writes the settings if they changed; true when something was written.</summary>
    public bool Flush()
    {
        if (!Dirty) return false;
        Settings.Save(settingsPath);
        Dirty = false;
        LoadProblem = null;
        return true;
    }

    public void SetTheme(string id)
    {
        if (!ThemeCatalog.Exists(id)) throw new ArgumentException($"Unknown theme: {id}", nameof(id));
        ThemeCatalog.WriteState(themePath, id);
    }

    /// <summary>Tells the service that a window wants the status; it stops writing 5 s after the last touch.</summary>
    public void Touch(DateTime nowUtc)
    {
        if (!File.Exists(alivePath)) File.WriteAllText(alivePath, "");
        File.SetLastWriteTimeUtc(alivePath, nowUtc);
    }

    /// <summary>Reads the status file; true when the service wrote it within the last 5 s.</summary>
    public bool RefreshStatus(DateTime nowUtc)
    {
        var status = ServiceStatus.Read(statusPath, out var problem);
        StatusProblem = problem;
        if (status is not null)
        {
            Status = status;
            Live = ToReadings(status);
        }
        return Status is { } s && (nowUtc - s.TimeUtc).Duration() <= LiveWindow;
    }

    public static Readings ToReadings(ServiceStatus s) => new(s.CpuTemp, s.CpuLoad, s.GpuTemp, s.GpuLoad)
    {
        RamLoad = s.RamLoad,
        VramUsedMb = s.VramUsedMb,
        VramTotalMb = s.VramTotalMb,
        PumpRpm = s.PumpRpm,
        Fans = s.Fans,
    };
}
