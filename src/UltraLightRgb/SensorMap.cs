namespace UltraLightRgb;

/// <summary>One raw sensor value. Group is "cpu", "gpu" or "board"; Kind is LibreHardwareMonitor's sensor type name.</summary>
public readonly record struct SensorValue(string Group, string Kind, string Name, float? Value);

/// <summary>Physical memory as Windows reports it.</summary>
public readonly record struct MemoryInfo(float LoadPercent, float UsedGb, float TotalGb);

/// <summary>Turns raw sensor values into the readings the themes and LEDs use. No hardware access.</summary>
public static class SensorMap
{
    private static readonly string[] CpuTempNames = ["Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)"];

    public static Readings ToReadings(IReadOnlyCollection<SensorValue> values, MemoryInfo? memory, string pumpFan)
    {
        float? Find(string group, string kind, params string[] names)
        {
            foreach (var name in names)
                foreach (var v in values)
                    if (v.Group == group && v.Kind == kind && v.Name == name && v.Value is { } x && !float.IsNaN(x))
                        // without the PawnIO driver the Ryzen Tctl sensor exists but stays at 0
                        return kind == "Temperature" && x <= 0 ? null : x;
            return null;
        }

        FanReading[] fans =
        [
            .. Fans(values, "board"),
            .. Fans(values, "gpu"),
        ];
        float? pump = pumpFan.Length == 0
            ? null
            : fans.FirstOrDefault(f => f.Name.Equals(pumpFan, StringComparison.OrdinalIgnoreCase) && !f.Name.StartsWith("GPU", StringComparison.Ordinal))?.Rpm;

        return new Readings(
            Find("cpu", "Temperature", CpuTempNames),
            Find("cpu", "Load", "CPU Total"),
            Find("gpu", "Temperature", "GPU Core"),
            Find("gpu", "Load", "GPU Core"))
        {
            VramUsedMb = Find("gpu", "SmallData", "GPU Memory Used"),
            VramTotalMb = Find("gpu", "SmallData", "GPU Memory Total"),
            RamLoad = memory?.LoadPercent,
            RamUsedGb = memory?.UsedGb,
            RamTotalGb = memory?.TotalGb,
            Fans = fans,
            PumpRpm = pump,
        };
    }

    private static IEnumerable<FanReading> Fans(IEnumerable<SensorValue> values, string group) =>
        values.Where(v => v.Group == group && v.Kind == "Fan" && v.Value is { } x && !float.IsNaN(x))
              .Select(v => new FanReading(v.Name, v.Value!.Value));
}
