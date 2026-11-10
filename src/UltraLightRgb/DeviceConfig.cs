using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraLightRgb;

/// <summary>One LED output: which driver writes it and which reading it follows.</summary>
public sealed record OutputConfig
{
    /// <summary>"aura", "msi-gpu" or "openrgb".</summary>
    public string Driver { get; init; } = "";

    /// <summary>"cpu" or "gpu": the temperature that picks the colour.</summary>
    public string Source { get; init; } = "cpu";

    /// <summary>aura: addressable header number, counted from 0.</summary>
    public int Header { get; init; }

    /// <summary>aura: also drive the LEDs on the mainboard itself.</summary>
    public bool Board { get; init; } = true;

    /// <summary>openrgb: server address. Only the local machine is accepted.</summary>
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 6742;

    /// <summary>openrgb: part of the controller name, case-insensitive; "*" means every controller.</summary>
    public string Match { get; init; } = "*";

    /// <summary>openrgb: run the moving pattern. Off by default, because some devices are slow to write.</summary>
    public bool Animate { get; init; }

    public LedSource SourceKind => Source.Equals("gpu", StringComparison.OrdinalIgnoreCase) ? LedSource.Gpu : LedSource.Cpu;

    /// <summary>Null when the entry is usable, otherwise why not.</summary>
    public string? Problem()
    {
        if (!Source.Equals("cpu", StringComparison.OrdinalIgnoreCase) && !Source.Equals("gpu", StringComparison.OrdinalIgnoreCase))
            return $"source must be \"cpu\" or \"gpu\", not \"{Source}\"";
        switch (Driver.ToLowerInvariant())
        {
            case "aura":
                return Header is < 0 or > 7 ? $"header {Header} is outside 0..7" : null;
            case "msi-gpu":
                return null;
            case "openrgb":
                if (Port is < 1 or > 65535) return $"port {Port} is outside 1..65535";
                if (string.IsNullOrWhiteSpace(Match)) return "match must not be empty, use \"*\" for every controller";
                // the service runs as SYSTEM: it must not parse data from other machines
                bool local = Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                             || (IPAddress.TryParse(Host, out var ip) && IPAddress.IsLoopback(ip));
                return local ? null : $"host \"{Host}\" is not the local machine";
            default:
                return $"unknown driver \"{Driver}\"";
        }
    }
}

public sealed record AppConfig
{
    public bool Lcd { get; init; } = true;

    public OutputConfig[] Outputs { get; init; } = [];

    /// <summary>What runs without a configuration file: the hardware this project was verified on.</summary>
    public static AppConfig Default { get; } = new()
    {
        Outputs =
        [
            new OutputConfig { Driver = "aura", Source = "cpu", Header = 0, Board = true },
            new OutputConfig { Driver = "msi-gpu", Source = "gpu" },
        ],
    };

    public static string DefaultPath => Path.Combine(ThemeCatalog.DataDir, "devices.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Missing file: defaults. A broken file never falls back to the defaults, because that could
    /// switch on outputs the owner turned off: the LCD keeps running, the LEDs stay untouched.
    /// </summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return Default;
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            Log.Write($"Config {path} is invalid, LEDs stay untouched: {e.Message}");
            return new AppConfig();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Config {path} is not readable, LEDs stay untouched: {e.GetType().Name}: {e.Message}");
            return new AppConfig();
        }
    }

    /// <summary>Parses and drops unusable outputs, each with a log line.</summary>
    public static AppConfig Parse(string json)
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, Json) ?? throw new JsonException("file is empty or null");
        var usable = new List<OutputConfig>();
        foreach (var o in cfg.Outputs ?? [])
        {
            if (o.Problem() is { } why) Log.Write($"Config: output skipped ({o.Driver}): {why}");
            else usable.Add(o);
        }
        return cfg with { Outputs = [.. usable] };
    }
}
