using System.Buffers.Binary;
using HidSharp;

namespace UltraLightRgb;

/// <summary>
/// Thermalright "USBDISPLAY" HID type 2 (VID 0416, PID 5302).
/// Wire format per rejeb/linux-thermalright-lcd-control (Apache-2.0; device_probe.py, hid_devices.py),
/// itself derived from the vendor's Windows application.
/// </summary>
public sealed class LcdDevice : IDisposable
{
    public const int Vid = 0x0416;
    public const int Pid = 0x5302;

    private const int ChunkSize = 512;
    private const int HeaderSize = 22;
    private static readonly byte[] Magic = [0xDA, 0xDB, 0xDC, 0xDD];

    // FBL -> (width, height, jpeg); subset of the TRCC screen table
    private static readonly Dictionary<int, (int W, int H, bool Jpeg)> FblScreens = new()
    {
        [36] = (240, 240, false), [37] = (240, 240, false),
        [50] = (320, 240, false), [51] = (320, 240, false), [52] = (320, 240, false),
        [53] = (320, 240, false), [54] = (360, 360, true), [58] = (320, 240, false),
        [64] = (640, 480, false), [72] = (480, 480, false),
        [100] = (320, 320, false), [101] = (320, 320, false), [102] = (320, 320, false),
        [129] = (480, 480, false),
    };

    private static readonly Dictionary<int, int> PmToFblOverrides = new()
    {
        [5] = 50, [7] = 64, [14] = 64, [32] = 100, [50] = 50,
    };

    private readonly HidStream _stream;
    private readonly byte[] _report;

    public int Width { get; }
    public int Height { get; }
    public int Pm { get; }
    public int Sub { get; }

    private LcdDevice(HidStream stream, int outLen, int width, int height, int pm, int sub)
    {
        _stream = stream;
        _report = new byte[outLen];
        Width = width;
        Height = height;
        Pm = pm;
        Sub = sub;
    }

    public static LcdDevice Open()
    {
        var dev = DeviceList.Local.GetHidDevices(Vid, Pid).FirstOrDefault()
                  ?? throw new IOException($"LCD {Vid:X4}:{Pid:X4} not found");

        int outLen = dev.GetMaxOutputReportLength();
        int inLen = dev.GetMaxInputReportLength();
        if (outLen < ChunkSize + 1)
            throw new NotSupportedException($"Unexpected output report length {outLen} (expected >= {ChunkSize + 1})");

        var stream = dev.Open();
        try
        {
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;

            // Handshake: report id 0 + magic + 8x00 + 01 00 00 00, zero-padded to 512
            var init = new byte[outLen];
            Magic.CopyTo(init, 1);
            init[1 + 12] = 0x01;
            stream.Write(init, 0, outLen);

            var resp = new byte[inLen];
            int n = stream.Read(resp, 0, inLen);
            if (n < 21) throw new IOException($"Handshake answer too short ({n} bytes)");
            // Windows prepends the report id byte to every input report
            var payload = resp.AsSpan(1, n - 1);
            if (!payload[..4].SequenceEqual(Magic) || payload[12] != 0x01)
                throw new IOException("Invalid handshake answer: " + Convert.ToHexString(resp, 0, Math.Min(n, 24)));

            int pm = payload[5], sub = payload[4];
            int fbl = PmToFblOverrides.GetValueOrDefault(pm, pm);
            if (!FblScreens.TryGetValue(fbl, out var screen))
                throw new NotSupportedException($"Unknown screen code PM={pm} SUB={sub} FBL={fbl}");
            if (screen.Jpeg)
                throw new NotSupportedException($"Panel FBL={fbl} expects JPEG frames, which are not implemented");

            return new LcdDevice(stream, outLen, screen.W, screen.H, pm, sub);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Allocates a frame buffer with the header pre-filled; pixel data starts at <see cref="HeaderSize"/>.</summary>
    public byte[] CreateFrameBuffer()
    {
        int payload = Width * Height * 2;
        var buf = new byte[HeaderSize + payload];
        Magic.CopyTo(buf, 0);
        var s = buf.AsSpan(4);
        BinaryPrimitives.WriteUInt16LittleEndian(s[0..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(s[4..], (ushort)Width);
        BinaryPrimitives.WriteUInt16LittleEndian(s[6..], (ushort)Height);
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(s[10..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..], (uint)payload);
        BinaryPrimitives.WriteUInt16LittleEndian(s[16..], 0);
        return buf;
    }

    public static int PixelOffset => HeaderSize;

    public void SendFrame(byte[] frame)
    {
        // Each output report: report id 0 + 512 data bytes; last chunk zero-padded
        for (int off = 0; off < frame.Length; off += ChunkSize)
        {
            int len = Math.Min(ChunkSize, frame.Length - off);
            Array.Clear(_report);
            Buffer.BlockCopy(frame, off, _report, 1, len);
            _stream.Write(_report, 0, _report.Length);
        }
    }

    public void Dispose() => _stream.Dispose();
}
