using System.Drawing;
using System.Runtime.InteropServices;

namespace UltraLightRgb;

/// <summary>
/// RGB of the MSI RTX 5070 Gaming Trio: ITE controller at I2C 0x68 on GPU port 1, reached through
/// the driver's NVAPI. Register map per OpenRGB (MSIGPUv2Controller). The save register 0x3F is
/// never written, so the stored effect stays untouched.
/// </summary>
public sealed unsafe class MsiGpuLed : ILed
{
    private const byte ChipAddr = 0x68, Port = 1;
    private const byte RegUnknown = 0x2E, RegMode = 0x22, RegR = 0x30, RegG = 0x31, RegB = 0x32, RegBrightness = 0x36;
    private const byte ModeIdle = 0x1C, ModeOff = 0x01, ModeStatic = 0x13, BrightnessMax = 100;
    private const int WriteGapMs = 20; // OpenRGB waits this long after every register write

    // address 0x68 means something else on other cards, so writes stay gated on this exact card
    private const uint DeviceId = 0x2F0410DE, SubsystemId = 0x53221462;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr Query(uint id);

    [StructLayout(LayoutKind.Sequential)]
    private struct I2cInfo
    {
        public uint Version, DisplayMask;
        public byte IsDdc, DevAddr;
        public byte* RegAddr;
        public uint RegSize;
        public byte* Data;
        public uint Size, Speed, SpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;
    }

    private readonly IntPtr _gpu;
    private readonly delegate* unmanaged<IntPtr, I2cInfo*, uint*, int> _write;

    private MsiGpuLed(IntPtr gpu, delegate* unmanaged<IntPtr, I2cInfo*, uint*, int> write)
    {
        _gpu = gpu;
        _write = write;
    }

    public static bool IsVerified(uint device, uint subsystem) => device == DeviceId && subsystem == SubsystemId;

    /// <summary>PCI ids of the first NVIDIA GPU. Reads only, no I2C access.</summary>
    public static (uint Device, uint Subsystem) PciIds()
    {
        var (_, dev, sub) = FirstGpu();
        return (dev, sub);
    }

    private static (IntPtr Gpu, uint Device, uint Subsystem) FirstGpu()
    {
        try
        {
            var init = (delegate* unmanaged<int>)Query(0x0150E828);
            var enumGpus = (delegate* unmanaged<IntPtr*, int*, int>)Query(0xE5AC921F);
            var pciIds = (delegate* unmanaged<IntPtr, uint*, uint*, uint*, uint*, int>)Query(0x2DDFB66E);
            if (init == null || enumGpus == null || pciIds == null)
                throw new NotSupportedException("NVAPI lacks the GPU enumeration functions");

            int st = init();
            if (st != 0) throw new IOException($"NvAPI_Initialize: {st}");

            var handles = stackalloc IntPtr[64];
            int count = 0;
            st = enumGpus(handles, &count);
            if (st != 0 || count < 1) throw new IOException($"No NVIDIA GPU found (status {st})");

            uint dev, sub, rev, ext;
            st = pciIds(handles[0], &dev, &sub, &rev, &ext);
            if (st != 0) throw new IOException($"GPU PCI ids not readable (status {st})");
            return (handles[0], dev, sub);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new NotSupportedException($"NVAPI not available (no NVIDIA driver?): {e.Message}", e);
        }
    }

    public static MsiGpuLed Open()
    {
        var (gpu, dev, sub) = FirstGpu();
        // wrong card: retrying cannot help, and any write could hit an unrelated chip
        if (!IsVerified(dev, sub))
            throw new NotSupportedException($"GPU 0x{dev:X8}/0x{sub:X8} is not on the verified list (MSI RTX 5070 Gaming Trio 0x{DeviceId:X8}/0x{SubsystemId:X8})");

        var write = (delegate* unmanaged<IntPtr, I2cInfo*, uint*, int>)Query(0x283AC65A);
        if (write == null) throw new NotSupportedException("NVAPI lacks the I2C write function");

        var led = new MsiGpuLed(gpu, write);
        led.Write(RegUnknown, 0x00);
        led.Write(RegMode, ModeIdle);
        led.Write(RegBrightness, BrightnessMax);
        // after a cold start the static colour was ignored until the card had passed through off
        led.Write(RegMode, ModeOff);
        led.Write(RegUnknown, 0x00);
        led.Write(RegMode, ModeIdle);
        return led;
    }

    public void Set(LedLook look, float phase)
    {
        var c = look.A;
        Write(RegR, c.R);
        Write(RegG, c.G);
        Write(RegB, c.B);
        Write(RegMode, ModeStatic); // the colour registers only take effect with this write
    }

    private void Write(byte reg, byte val)
    {
        uint unknown = 0;
        var info = new I2cInfo
        {
            Version = (3u << 16) | (uint)sizeof(I2cInfo),
            DevAddr = ChipAddr << 1,
            RegAddr = &reg,
            RegSize = 1,
            Data = &val,
            Size = 1,
            Speed = 0xFFFF,
            PortId = Port,
            IsPortIdSet = 1,
        };
        int st = _write(_gpu, &info, &unknown);
        if (st != 0) throw new IOException($"NVAPI I2C error {st} on register 0x{reg:X2}");
        Thread.Sleep(WriteGapMs);
    }

    public void Dispose()
    {
        // nothing to release: the GPU handle belongs to NVAPI, which LibreHardwareMonitor shares
    }
}
