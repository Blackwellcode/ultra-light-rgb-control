namespace UltraLightRgb.Tests;

public class SensorMapTests
{
    private static SensorValue Cpu(string kind, string name, float? v) => new("cpu", kind, name, v);
    private static SensorValue Gpu(string kind, string name, float? v) => new("gpu", kind, name, v);
    private static SensorValue Board(string kind, string name, float? v) => new("board", kind, name, v);

    // as read on a ROG STRIX B650E-F with RTX 5070 (Nuvoton NCT6799D)
    private static readonly SensorValue[] Machine =
    [
        Cpu("Temperature", "Core (Tctl/Tdie)", 61.1f),
        Cpu("Load", "CPU Total", 12f),
        Gpu("Temperature", "GPU Core", 46.1f),
        Gpu("Load", "GPU Core", 30f),
        Gpu("SmallData", "GPU Memory Used", 5843f),
        Gpu("SmallData", "GPU Memory Total", 12227f),
        Gpu("Fan", "GPU Fan 1", 1134f),
        Gpu("Fan", "GPU Fan 2", 1157f),
        Board("Fan", "Fan #1", 0f),
        Board("Fan", "Fan #2", 954.1f),
        Board("Fan", "Fan #7", 3103.4f),
        Board("Control", "Fan #7", 100f),
    ];

    private static readonly MemoryInfo Ram = new(LoadPercent: 60, UsedGb: 19.0f, TotalGb: 31.6f);

    [Fact]
    public void Temperatures_loads_and_memory_are_mapped()
    {
        var r = SensorMap.ToReadings(Machine, Ram, pumpFan: "");
        Assert.Equal(61.1f, r.CpuTemp);
        Assert.Equal(12f, r.CpuLoad);
        Assert.Equal(46.1f, r.GpuTemp);
        Assert.Equal(30f, r.GpuLoad);
        Assert.Equal(5843f, r.VramUsedMb);
        Assert.Equal(12227f, r.VramTotalMb);
        Assert.Equal(60f, r.RamLoad);
        Assert.Equal(19.0f, r.RamUsedGb);
        Assert.Equal(31.6f, r.RamTotalGb);
    }

    [Fact]
    public void Cpu_temperature_falls_back_to_the_package_sensor()
    {
        var r = SensorMap.ToReadings([Cpu("Temperature", "CPU Package", 55f)], null, "");
        Assert.Equal(55f, r.CpuTemp);
    }

    [Fact]
    public void Zero_degrees_counts_as_missing()
    {
        // without the PawnIO driver the Ryzen Tctl sensor exists but stays at 0
        var r = SensorMap.ToReadings([Cpu("Temperature", "Core (Tctl/Tdie)", 0f), Gpu("Temperature", "GPU Core", null)], null, "");
        Assert.Null(r.CpuTemp);
        Assert.Null(r.GpuTemp);
    }

    [Fact]
    public void Every_fan_is_listed_board_first_and_controls_are_not_fans()
    {
        var r = SensorMap.ToReadings(Machine, Ram, "");
        Assert.Equal(
            ["Fan #1", "Fan #2", "Fan #7", "GPU Fan 1", "GPU Fan 2"],
            r.Fans!.Select(f => f.Name));
        Assert.Equal(3103.4f, r.Fans!.Single(f => f.Name == "Fan #7").Rpm);
    }

    [Theory]
    [InlineData("Fan #7", 3103.4f)]
    [InlineData("fan #7", 3103.4f)]
    [InlineData("Fan #9", null)]
    [InlineData("", null)]
    public void Pump_is_the_fan_chosen_by_name(string pump, float? rpm)
    {
        Assert.Equal(rpm, SensorMap.ToReadings(Machine, Ram, pump).PumpRpm);
    }

    [Fact]
    public void Missing_memory_info_and_vram_stay_empty()
    {
        var r = SensorMap.ToReadings([Cpu("Temperature", "Core (Tctl/Tdie)", 50f)], null, "");
        Assert.Null(r.RamLoad);
        Assert.Null(r.VramUsedMb);
        Assert.Empty(r.Fans!);
    }
}
