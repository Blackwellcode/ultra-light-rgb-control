using LibreHardwareMonitor.Hardware;

namespace UltraLightRgb;

/// <summary>Reads only CPU + GPU through LibreHardwareMonitorLib; all other hardware groups stay disabled.</summary>
public sealed class Sensors : IDisposable
{
    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;

    public Sensors()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
               ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
    }

    public Readings Read()
    {
        _cpu?.Update();
        _gpu?.Update();
        return new Readings(
            Find(_cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)"),
            Find(_cpu, SensorType.Load, "CPU Total"),
            Find(_gpu, SensorType.Temperature, "GPU Core"),
            Find(_gpu, SensorType.Load, "GPU Core"));
    }

    private static float? Find(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw is null) return null;
        foreach (var name in names)
            foreach (var s in hw.Sensors)
                if (s.SensorType == type && s.Name == name && s.Value is { } v && !float.IsNaN(v))
                    // without the PawnIO driver the Ryzen Tctl sensor exists but stays at 0
                    return type == SensorType.Temperature && v <= 0 ? null : v;
        return null;
    }

    public void Dump(TextWriter w)
    {
        foreach (var hw in new[] { _cpu, _gpu })
        {
            if (hw is null) continue;
            hw.Update();
            w.WriteLine($"[{hw.HardwareType}] {hw.Name}");
            foreach (var s in hw.Sensors.Where(s => s.SensorType is SensorType.Temperature or SensorType.Load))
                w.WriteLine($"  {s.SensorType,-12} {s.Name,-28} {s.Value?.ToString("0.0") ?? "null"}");
        }
    }

    public void Dispose() => _computer.Close();
}
