namespace UltraLightRgb;

/// <summary>
/// Keeps a repeating error from flooding the log: the same message is written at most once per
/// interval, a different message at once. <see cref="Reset"/> after a success, so the next failure
/// is reported again.
/// </summary>
internal sealed class LogThrottle(long intervalMs)
{
    private string? _last;
    private long _lastAt;

    public bool ShouldLog(string message, long now)
    {
        if (message == _last && now - _lastAt < intervalMs) return false;
        _last = message;
        _lastAt = now;
        return true;
    }

    public void Reset() => _last = null;
}
