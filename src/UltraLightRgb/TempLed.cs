namespace UltraLightRgb;

public interface ILed : IDisposable
{
    /// <param name="look">Colours to show.</param>
    /// <param name="phase">How far the pattern has moved along the strip, in LEDs.</param>
    void Set(LedLook look, float phase);
}

/// <summary>Any single-brightness output at the brightness the user picked, asked on every write.</summary>
public sealed class DimmedLed(ILed inner, Func<int> level) : ILed
{
    public void Set(LedLook look, float phase) => inner.Set(look.Dim(level()), phase);

    public void Dispose() => inner.Dispose();
}

/// <summary>What an output is doing, for the switcher's status page.</summary>
public enum OutputState { Waiting, Ok, Retrying, Disabled }

/// <summary>
/// Lets one LED output follow a temperature with the colours of the active theme. All device
/// access runs on its own thread: a slow or failing LED must never touch the LCD loop.
/// </summary>
public sealed class TempLed : IDisposable
{
    private const float SmoothingMs = 8000f; // Tctl jumps within a second, the light should drift
    private const long RefreshMs = 30_000; // doubles as the health check while the colour is stable
    private const long RetryMs = 30_000;
    private const int IdleMs = 200, FlowMs = 80;

    private readonly string _name;
    private readonly Func<ILed> _open;
    private readonly float _alpha;
    private readonly long _minSendMs;
    private readonly bool _canFlow;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly object _gate = new();
    private LedLook? _wanted;
    private float _load;
    private float? _smoothed;
    private volatile bool _resend;
    private volatile OutputState _state = OutputState.Waiting;
    private volatile string? _error;

    public LedSource Source { get; }

    public string Name => _name;

    public OutputState State => _state;

    /// <summary>Last error message while retrying or after being disabled, otherwise null.</summary>
    public string? Error => _error;

    /// <summary>Sends the current look again at the next cycle, e.g. after a brightness change.</summary>
    public void Invalidate() => _resend = true;

    public TempLed(string name, LedSource source, Func<ILed> open, int intervalMs, long minSendMs = 0, bool canFlow = false)
    {
        _name = name;
        Source = source;
        _open = open;
        _alpha = 1 - MathF.Exp(-intervalMs / SmoothingMs);
        _minSendMs = minSendMs;
        _canFlow = canFlow;
        _thread = new Thread(Run) { IsBackground = true, Name = name + " LED" };
        _thread.Start();
    }

    public void Update(float? tempC, float? load, Func<float, LedLook> lookOf)
    {
        if (tempC is not { } t) return; // no reading: keep the last colour

        float s = _smoothed is { } prev ? prev + _alpha * (t - prev) : t;
        _smoothed = s;
        var look = lookOf(s);
        lock (_gate)
        {
            _wanted = look;
            _load = load ?? 0;
        }
    }

    private void Run()
    {
        ILed? led = null;
        LedLook? sent = null;
        string? lastError = null;
        long nextSend = 0, nextRefresh = 0, nextTry = 0, last = Environment.TickCount64;
        float phase = 0;

        while (!_stop.IsCancellationRequested)
        {
            LedLook? wanted;
            float load;
            lock (_gate)
            {
                wanted = _wanted;
                load = _load;
            }

            long now = Environment.TickCount64;
            if (_resend)
            {
                _resend = false;
                sent = null;
                nextSend = 0;
            }
            float flow = _canFlow && wanted is { } w ? w.Flow : 0;
            // idle creeps along, full load runs at double the theme's speed
            phase += flow * (0.4f + load * 0.016f) * (now - last) / 1000f;
            last = now;

            bool due = flow > 0 || (wanted != sent ? now >= nextSend : now >= nextRefresh);
            if (wanted is { } look && due && now >= nextTry)
            {
                try
                {
                    led ??= _open();
                    led.Set(look, phase);
                    if (lastError is not null)
                    {
                        Log.Write($"{_name}: working again");
                        lastError = null;
                    }
                    _state = OutputState.Ok;
                    _error = null;
                    sent = look;
                    nextSend = now + _minSendMs;
                    nextRefresh = now + RefreshMs;
                }
                catch (NotSupportedException e)
                {
                    // wrong or unknown hardware: retrying cannot help, so this output stops for good
                    Log.Write($"{_name}: disabled, {e.Message}");
                    _error = e.Message;
                    _state = OutputState.Disabled;
                    break;
                }
                catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
                {
                    // a missing device fails the same way every 30 s: only a new message is worth a line
                    string msg = $"{e.GetType().Name}: {e.Message}";
                    if (msg != lastError) Log.Write($"{_name}: {msg} (retry every {RetryMs / 1000} s)");
                    lastError = msg;
                    _error = msg;
                    _state = OutputState.Retrying;
                    led?.Dispose();
                    led = null;
                    sent = null;
                    nextTry = now + RetryMs;
                }
            }
            _stop.Token.WaitHandle.WaitOne(flow > 0 ? FlowMs : IdleMs);
        }
        led?.Dispose();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join();
        _stop.Dispose();
    }
}
