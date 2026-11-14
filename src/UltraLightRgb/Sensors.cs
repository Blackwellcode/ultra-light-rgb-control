using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace UltraLightRgb;

/// <summary>
/// Reads CPU, GPU and the board's Super I/O chip (fans, pump) through LibreHardwareMonitorLib, and
/// physical memory straight from Windows. Memory, storage, network and controller groups stay off:
/// the memory group would also read the RAM modules over SMBus.
/// </summary>
public sealed class Sensors : IDisposable
{
    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;
    private readonly IHardware? _board;
    private readonly List<SensorValue> _values = new(64);

    public Sensors()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };
        _computer.Open();
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
               ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
        _board = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
    }

    /// <param name="pumpFan">Board fan header that drives the pump, see <see cref="UserSettings.PumpFan"/>.</param>
    public Readings Read(string pumpFan = "")
    {
        _values.Clear();
        Collect(_cpu, "cpu");
        Collect(_gpu, "gpu");
        Collect(_board, "board");
        return SensorMap.ToReadings(_values, Memory(), pumpFan);
    }

    private void Collect(IHardware? hw, string group)
    {
        if (hw is null) return;
        hw.Update();
        foreach (var s in hw.Sensors) _values.Add(new SensorValue(group, s.SensorType.ToString(), s.Name, s.Value));
        foreach (var sub in hw.SubHardware)
        {
            sub.Update();
            foreach (var s in sub.Sensors) _values.Add(new SensorValue(group, s.SensorType.ToString(), s.Name, s.Value));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private static MemoryInfo? Memory()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return null;
        const float Gb = 1024f * 1024 * 1024;
        return new MemoryInfo(m.MemoryLoad, (m.TotalPhys - m.AvailPhys) / Gb, m.TotalPhys / Gb);
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

    /// <summary>
    /// Every sensor of every hardware group LibreHardwareMonitor can open here, including the board's
    /// Super I/O chip (fans, pump), plus how long one update of each group takes. Read-only.
    /// </summary>
    public static void DumpAll(TextWriter w, int timingRounds = 20)
    {
        var computer = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
            IsMemoryEnabled = true, IsControllerEnabled = true,
        };
        computer.Open();
        try
        {
            foreach (var hw in computer.Hardware)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < timingRounds; i++)
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();
                }
                watch.Stop();
                w.WriteLine($"[{hw.HardwareType}] {hw.Name}  update_ms={watch.Elapsed.TotalMilliseconds / timingRounds:0.00}");
                foreach (var part in new[] { hw }.Concat(hw.SubHardware))
                {
                    if (part != hw) w.WriteLine($"  <{part.HardwareType}> {part.Name}");
                    foreach (var s in part.Sensors)
                        w.WriteLine($"    {s.SensorType,-12} {s.Name,-32} {s.Value?.ToString("0.0") ?? "null"}");
                }
            }
        }
        finally
        {
            computer.Close();
        }
    }

    public void Dispose() => _computer.Close();
}
