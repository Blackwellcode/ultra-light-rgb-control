using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using HidSharp;

namespace UltraLightRgb;

internal static class Program
{
    private const long GpuMinSendMs = 5000; // every colour change costs four I2C writes on the GPU's bus
    private const long OpenRgbMinSendMs = 1000; // some OpenRGB devices are slow to write
    private const int LcdRetryMs = 5000;

    private const string Usage = """
        Ultra Light RGB Control: CPU/GPU temperature on an LCD and on RGB lighting.

          (no option)            run: LCD plus every configured LED output
          --service              as above, and log to the machine-wide log file
          --config <file>        configuration (default: %ProgramData%\UltraLightRgb\devices.json)
          --theme <id>           fixed theme for this run, ignores the switcher
          --interval <ms>        sensor interval, minimum 250 (default 1000)
          --no-led               leave all LEDs untouched
          --devices              list supported hardware found on this machine, writes nothing
          --openrgb-list         list the controllers of the configured OpenRGB servers
          --test-color RRGGBB    set every configured LED output to one colour and exit
          --sensors              print the temperature and load sensors
          --once [--preview f]   draw one frame on the LCD, optionally save it as PNG
          --preview-all <dir>    render every theme with made-up readings as PNG
          --bench                measure every theme on the real LCD
        """;

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            Console.WriteLine(Usage);
            return 0;
        }
        if (args.Contains("--service")) Log.ToFile(Path.Combine(ThemeCatalog.DataDir, "logs", "service.log"));

        int intervalMs = int.TryParse(Arg(args, "--interval"), out var ms) ? Math.Max(250, ms) : 1000;
        string configPath = Arg(args, "--config") ?? AppConfig.DefaultPath;

        // --theme pins a look for this run and ignores the switcher
        string? pinned = Arg(args, "--theme");
        if (pinned is not null && !ThemeCatalog.Exists(pinned))
        {
            Log.Write($"Unknown theme: {pinned}. Known: {string.Join(", ", ThemeCatalog.All.Select(t => t.Id))}");
            return 2;
        }

        // design previews with synthetic readings: touch neither the LCD nor the sensors
        if (Arg(args, "--preview-all") is { } dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var (id, _, _) in ThemeCatalog.All)
            {
                using var preview = Renderer.Preview(id);
                preview.SavePreview(Path.Combine(dir, id + ".png"));
            }
            return 0;
        }

        var config = AppConfig.Load(configPath);
        if (args.Contains("--devices")) return Devices(config, configPath);
        if (args.Contains("--openrgb-list")) return OpenRgbList(config);
        if (args.Contains("--test-color")) return TestColor(config, Arg(args, "--test-color"));

        using var self = Process.GetCurrentProcess();
        self.PriorityClass = ProcessPriorityClass.BelowNormal;

        using var sensors = new Sensors();
        if (args.Contains("--sensors"))
        {
            sensors.Dump(Console.Out);
            return 0;
        }
        if (args.Contains("--bench")) return Bench(sensors, config, 6);

        bool once = args.Contains("--once");
        bool leds = !once && !args.Contains("--no-led");
        var outputs = leds ? config.Outputs.Select(o => CreateOutput(o, intervalMs)).ToList() : [];
        try
        {
            return Run(args, sensors, outputs, config.Lcd || once, once, pinned, intervalMs);
        }
        finally
        {
            foreach (var o in outputs) o.Dispose();
        }
    }

    private static int Run(string[] args, Sensors sensors, List<TempLed> outputs, bool useLcd, bool once, string? pinned, int intervalMs)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        using var renderer = new Renderer(Scene.W, Scene.H, pinned ?? ReadTheme());
        var watch = new ThemeWatch();
        LcdDevice? lcd = null;
        byte[]? frame = null;
        string? lcdError = null;
        long nextSample = 0, nextLcdTry = 0, nextFrame = 0;
        Log.Write($"Started: LCD {(useLcd ? "on" : "off")}, {outputs.Count} LED output(s), theme {renderer.ThemeId}");

        try
        {
            while (!stop.IsCancellationRequested)
            {
                long now = Environment.TickCount64;
                if (now >= nextSample)
                {
                    nextSample = now + intervalMs;
                    var r = sensors.Read();
                    renderer.Sample(r);
                    foreach (var o in outputs)
                    {
                        if (o.Source == LedSource.Gpu) o.Update(r.GpuTemp, r.GpuLoad, t => renderer.Led(LedSource.Gpu, t));
                        else o.Update(r.CpuTemp ?? r.GpuTemp, r.CpuLoad, t => renderer.Led(LedSource.Cpu, t));
                    }
                    if (pinned is null && watch.Poll(out var id) && id != renderer.ThemeId)
                    {
                        renderer.SetTheme(id);
                        Log.Write($"Theme: {id}");
                    }
                }

                if (useLcd && lcd is null && now >= nextLcdTry)
                {
                    try
                    {
                        lcd = LcdDevice.Open();
                        if (lcd.Width != Scene.W || lcd.Height != Scene.H)
                            throw new NotSupportedException($"Themes are built for {Scene.W}x{Scene.H}, the LCD reports {lcd.Width}x{lcd.Height}");
                        frame = lcd.CreateFrameBuffer();
                        Log.Write($"LCD connected: {lcd.Width}x{lcd.Height} (PM={lcd.Pm} SUB={lcd.Sub})");
                        lcdError = null;
                    }
                    catch (NotSupportedException e)
                    {
                        Log.Write($"LCD disabled: {e.Message}");
                        lcd?.Dispose();
                        lcd = null;
                        useLcd = false;
                        if (once) return 2;
                    }
                    catch (Exception e) when (e is IOException or TimeoutException)
                    {
                        // unplugged, asleep or held by the vendor tool: try again, but say it only once
                        string msg = $"{e.GetType().Name}: {e.Message}";
                        if (msg != lcdError) Log.Write($"LCD: {msg} (retry every {LcdRetryMs / 1000} s)");
                        lcdError = msg;
                        lcd?.Dispose();
                        lcd = null;
                        nextLcdTry = now + LcdRetryMs;
                        if (once) return 1;
                    }
                }

                if (lcd is not null && frame is not null && now >= nextFrame)
                {
                    nextFrame = now + renderer.FrameMs;
                    try
                    {
                        renderer.Render(frame, LcdDevice.PixelOffset);
                        lcd.SendFrame(frame);
                        if (once)
                        {
                            if (Arg(args, "--preview") is { } png) renderer.SavePreview(png);
                            return 0;
                        }
                    }
                    catch (Exception e) when (e is IOException or TimeoutException)
                    {
                        Log.Write($"LCD lost: {e.GetType().Name}: {e.Message}");
                        lcd.Dispose();
                        lcd = null;
                        nextLcdTry = now + LcdRetryMs;
                        if (once) return 1;
                    }
                }

                // sleep until the next frame, sample or LCD retry, whichever comes first
                long next = nextSample;
                if (lcd is not null) next = Math.Min(next, nextFrame);
                else if (useLcd) next = Math.Min(next, nextLcdTry);
                stop.Token.WaitHandle.WaitOne((int)Math.Clamp(next - Environment.TickCount64, 1, intervalMs));
            }
            return 0;
        }
        finally
        {
            lcd?.Dispose();
        }
    }

    private static TempLed CreateOutput(OutputConfig o, int intervalMs) => o.Driver.ToLowerInvariant() switch
    {
        "aura" => new TempLed($"AURA header {o.Header}", o.SourceKind,
            () => AuraLed.Open((byte)o.Header, o.Board), intervalMs, canFlow: true),
        "msi-gpu" => new TempLed("MSI GPU", o.SourceKind, MsiGpuLed.Open, intervalMs, GpuMinSendMs),
        "openrgb" => new TempLed($"OpenRGB \"{o.Match}\"", o.SourceKind,
            () => OpenRgbLed.Open(o), intervalMs, o.Animate ? 0 : OpenRgbMinSendMs, canFlow: o.Animate),
        _ => throw new ArgumentException($"unknown driver {o.Driver}"), // AppConfig.Parse filters these out
    };

    private static ILed OpenOutput(OutputConfig o) => o.Driver.ToLowerInvariant() switch
    {
        "aura" => AuraLed.Open((byte)o.Header, o.Board),
        "msi-gpu" => MsiGpuLed.Open(),
        "openrgb" => OpenRgbLed.Open(o),
        _ => throw new ArgumentException($"unknown driver {o.Driver}"),
    };

    private static string ReadTheme()
    {
        try
        {
            return ThemeCatalog.ReadState();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Theme file not readable, using {ThemeCatalog.DefaultId}: {e.Message}");
            return ThemeCatalog.DefaultId;
        }
    }

    /// <summary>Notices a new theme.txt through its timestamp, so polling costs no file read.</summary>
    private sealed class ThemeWatch
    {
        private DateTime _stamp = DateTime.MinValue;

        public bool Poll(out string id)
        {
            id = ThemeCatalog.DefaultId;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(ThemeCatalog.StatePath);
                if (stamp == _stamp) return false;
                id = ThemeCatalog.ReadState();
                _stamp = stamp;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // the switcher may be replacing the file right now: the next poll picks it up
                Log.Write($"Theme file: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }
    }

    private static int TestColor(AppConfig config, string? hex)
    {
        if (hex is not { Length: 6 } || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            Log.Write("--test-color expects RRGGBB");
            return 2;
        }
        var look = new LedLook(Color.FromArgb(255, Color.FromArgb(rgb)));
        int failed = 0;
        foreach (var o in config.Outputs)
        {
            try
            {
                using var led = OpenOutput(o);
                led.Set(look, 0);
                Log.Write($"{o.Driver}: set to {hex}");
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException
                                         or InvalidDataException or NotSupportedException)
            {
                Log.Write($"{o.Driver}: {e.GetType().Name}: {e.Message}");
                failed++;
            }
        }
        if (config.Outputs.Length == 0) Log.Write("No LED output configured");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Read-only inventory, meant for bug reports: nothing here writes to a device.</summary>
    private static int Devices(AppConfig config, string configPath)
    {
        Console.WriteLine($"Config: {configPath} ({(File.Exists(configPath) ? "found" : "missing, defaults in use")})");
        Console.WriteLine($"  LCD: {(config.Lcd ? "on" : "off")}");
        foreach (var o in config.Outputs) Console.WriteLine($"  output: {o.Driver} source={o.Source} header={o.Header} board={o.Board} match={o.Match}");

        bool Hid(int vid, int pid) => DeviceList.Local.GetHidDevices(vid, pid).Any();
        Console.WriteLine($"LCD {LcdDevice.Vid:X4}:{LcdDevice.Pid:X4}: {(Hid(LcdDevice.Vid, LcdDevice.Pid) ? "present" : "not found")}");
        Console.WriteLine($"AURA {AuraLed.Vid:X4}:{AuraLed.Pid:X4}: {(Hid(AuraLed.Vid, AuraLed.Pid) ? "present" : "not found")}");
        try
        {
            var (dev, sub) = MsiGpuLed.PciIds();
            Console.WriteLine($"NVIDIA GPU 0x{dev:X8}/0x{sub:X8}: {(MsiGpuLed.IsVerified(dev, sub) ? "verified MSI RGB" : "no verified RGB support")}");
        }
        catch (Exception e) when (e is IOException or NotSupportedException)
        {
            Console.WriteLine($"NVIDIA GPU: {e.Message}");
        }
        return OpenRgbList(config, quiet: true);
    }

    private static int OpenRgbList(AppConfig config, bool quiet = false)
    {
        var servers = config.Outputs.Where(o => o.Driver.Equals("openrgb", StringComparison.OrdinalIgnoreCase))
                                    .Select(o => (o.Host, o.Port)).Distinct().ToList();
        if (servers.Count == 0)
        {
            if (quiet) return 0;
            servers.Add(("127.0.0.1", 6742));
        }
        int failed = 0;
        foreach (var (host, port) in servers)
        {
            try
            {
                using var client = OpenRgbClient.Connect(host, port);
                var list = client.Controllers();
                Console.WriteLine($"OpenRGB {host}:{port}, protocol {client.Version}, {list.Count} controller(s):");
                foreach (var (id, info) in list) Console.WriteLine($"  [{id}] {info.Name} ({info.Colors} LEDs)");
            }
            catch (Exception e) when (e is IOException or TimeoutException or InvalidDataException)
            {
                Console.WriteLine($"OpenRGB {host}:{port}: {e.Message}");
                failed++;
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static int Bench(Sensors sensors, AppConfig config, int seconds)
    {
        using var self = Process.GetCurrentProcess();
        using var lcd = LcdDevice.Open();
        using var renderer = new Renderer(lcd.Width, lcd.Height, ThemeCatalog.DefaultId);
        var outputs = config.Outputs.Select(o => CreateOutput(o, 1000)).ToList();
        try
        {
            var frame = lcd.CreateFrameBuffer();
            Console.WriteLine("theme frames fps render_ms send_ms cpu_pct_total");
            foreach (var (id, _, _) in ThemeCatalog.All)
            {
                renderer.SetTheme(id);
                var render = new Stopwatch();
                var send = new Stopwatch();
                int frames = 0;
                long end = Environment.TickCount64 + seconds * 1000, nextSample = 0;
                self.Refresh();
                var cpu0 = self.TotalProcessorTime;
                var wall = Stopwatch.StartNew();
                while (Environment.TickCount64 < end)
                {
                    long now = Environment.TickCount64;
                    if (now >= nextSample)
                    {
                        nextSample = now + 1000;
                        var r = sensors.Read();
                        renderer.Sample(r);
                        foreach (var o in outputs)
                        {
                            if (o.Source == LedSource.Gpu) o.Update(r.GpuTemp, r.GpuLoad, t => renderer.Led(LedSource.Gpu, t));
                            else o.Update(r.CpuTemp ?? r.GpuTemp, r.CpuLoad, t => renderer.Led(LedSource.Cpu, t));
                        }
                    }
                    render.Start();
                    renderer.Render(frame, LcdDevice.PixelOffset);
                    render.Stop();
                    send.Start();
                    lcd.SendFrame(frame);
                    send.Stop();
                    frames++;
                    int wait = renderer.FrameMs - (int)(Environment.TickCount64 - now);
                    if (wait > 0) Thread.Sleep(wait);
                }
                self.Refresh();
                double cpuPct = (self.TotalProcessorTime - cpu0).TotalMilliseconds / wall.Elapsed.TotalMilliseconds
                              / Environment.ProcessorCount * 100;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{id} {frames} {frames / wall.Elapsed.TotalSeconds:0.0} {render.Elapsed.TotalMilliseconds / frames:0.0} {send.Elapsed.TotalMilliseconds / frames:0.0} {cpuPct:0.000}"));
            }
            return 0;
        }
        finally
        {
            foreach (var o in outputs) o.Dispose();
        }
    }
}
