using System.Globalization;

namespace UltraLightRgb;

/// <summary>The theme to show, why, and whether the night window is open.</summary>
public readonly record struct Mode(string Theme, string Reason, bool Night);

/// <summary>
/// Decides which theme is shown and how bright the LEDs are, from the user's choice, the night
/// window and the GPU load. Precedence: pinned (--theme) over automatic over night over the user.
/// Called once per sensor sample; <see cref="Brightness"/> may be called from the LED threads.
/// </summary>
public sealed class ModeResolver
{
    private volatile int _nightPercent = 100;
    private bool _busy;
    private long? _changingSince;

    public Mode Resolve(string userTheme, string? pinned, UserSettings s, DateTime localNow, long nowMs, float? gpuLoad)
    {
        bool night = IsNight(s.Night, TimeOnly.FromDateTime(localNow));
        _nightPercent = night ? s.Night.Brightness : 100;
        UpdateBusy(s.Auto, nowMs, gpuLoad);

        if (pinned is not null) return new Mode(pinned, "pinned", night);
        if (_busy) return new Mode(s.Auto.Theme, "auto", night);
        if (night && s.Night.Theme is { Length: > 0 } nightTheme) return new Mode(nightTheme, "night", night);
        return new Mode(userTheme, "user", night);
    }

    /// <summary>
    /// Hysteresis on time: busy only after the load stayed at or above the threshold for the hold
    /// time, idle again only after it stayed below for the hold time. Loading screens and short
    /// spikes therefore change nothing. A missing reading counts as below.
    /// </summary>
    private void UpdateBusy(AutoSettings auto, long nowMs, float? gpuLoad)
    {
        if (!auto.Enabled)
        {
            _busy = false;
            _changingSince = null;
            return;
        }
        bool above = gpuLoad is { } load && load >= auto.GpuLoad;
        if (above == _busy)
        {
            _changingSince = null;
            return;
        }
        _changingSince ??= nowMs;
        if (nowMs - _changingSince.Value < auto.HoldSeconds * 1000L) return;
        _busy = above;
        _changingSince = null;
    }

    /// <summary><paramref name="percent"/> as it applies right now: reduced while the night window is open.</summary>
    public int Brightness(int percent) => percent * _nightPercent / 100;

    /// <summary>From is inclusive, to exclusive; a window whose end is before its start spans midnight.</summary>
    private static bool IsNight(NightSettings n, TimeOnly now)
    {
        if (!n.Enabled) return false;
        var from = TimeOnly.ParseExact(n.From, "HH:mm", CultureInfo.InvariantCulture);
        var to = TimeOnly.ParseExact(n.To, "HH:mm", CultureInfo.InvariantCulture);
        if (from == to) return false;
        return from < to ? now >= from && now < to : now >= from || now < to;
    }
}
