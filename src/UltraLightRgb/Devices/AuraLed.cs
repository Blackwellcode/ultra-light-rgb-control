using HidSharp;

namespace UltraLightRgb;

/// <summary>
/// One addressable header, optionally plus the board's own LEDs, on the ASUS AURA USB controller
/// (0B05:19AF) in Direct mode. Protocol per OpenRGB (AsusAuraUSBController). The commit command
/// (EC 3F 55) is never sent, so the profile stored in the controller stays untouched.
/// </summary>
public sealed class AuraLed : ILed
{
    public const int Vid = 0x0B05, Pid = 0x19AF;
    public const int ReportLen = 65;

    private const int LedsPerPacket = 20;
    // strip length is unknown: surplus entries run off the end of the chain
    private const int LedCount = 120;
    private const byte ModeDirect = 0xFF, ApplyFlag = 0x80;
    private const byte BoardEffectChannel = 0, BoardDirectChannel = 4;
    private const int BoardLeds = 6; // the count the reference board was tested with, it lit up completely

    private readonly HidStream _stream;
    private readonly byte _header;
    private readonly bool _board;
    private readonly byte[] _report = new byte[ReportLen];

    private AuraLed(HidStream stream, byte header, bool board)
    {
        _stream = stream;
        _header = header;
        _board = board;
    }

    public static AuraLed Open(byte header, bool board)
    {
        var dev = DeviceList.Local.GetHidDevices(Vid, Pid)
            .FirstOrDefault(d => d.GetMaxOutputReportLength() >= ReportLen)
            ?? throw new IOException($"AURA controller {Vid:X4}:{Pid:X4} not found");

        var stream = dev.Open();
        try
        {
            stream.WriteTimeout = 2000;
            var led = new AuraLed(stream, header, board);
            led.Send(0xEC, 0x52, 0x53, 0x00, 0x01); // Gen1 mode, required by OpenRGB before Direct
            // Mode channels as tested on a ROG STRIX B650E-F (firmware AULA3-AR32-0309), against OpenRGB's
            // "AR32 starts at 0": 0 is the board, ARGB headers follow from 1. A wrong channel fails
            // silently, the power-on rainbow just keeps running.
            led.Send(0xEC, 0x35, EffectChannel(header), 0x00, 0x00, ModeDirect);
            if (board) led.Send(0xEC, 0x35, BoardEffectChannel, 0x00, 0x00, ModeDirect);
            return led;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static byte EffectChannel(byte header) => (byte)(header + 1);

    public void Set(LedLook look, float phase)
    {
        for (int done = 0; done < LedCount; done += LedsPerPacket)
        {
            int n = Math.Min(LedsPerPacket, LedCount - done);
            DirectReport(_report, _header, done, n, done + n == LedCount, look, phase);
            _stream.Write(_report);
        }
        if (!_board) return;
        DirectReport(_report, BoardDirectChannel, 0, BoardLeds, true, look, phase);
        _stream.Write(_report);
    }

    /// <summary>Fills one Direct report: EC 40, channel (bit 7 applies), first LED, count, then R G B per LED.</summary>
    public static void DirectReport(byte[] report, byte channel, int start, int n, bool apply, LedLook look, float phase)
    {
        if (n is < 1 or > LedsPerPacket) throw new ArgumentOutOfRangeException(nameof(n));
        Array.Clear(report);
        report[0] = 0xEC;
        report[1] = 0x40;
        report[2] = (byte)(channel | (apply ? ApplyFlag : 0));
        report[3] = (byte)start;
        report[4] = (byte)n;
        for (int i = 0; i < n; i++)
        {
            var c = look.At(start + i, phase);
            report[5 + i * 3] = c.R;
            report[6 + i * 3] = c.G;
            report[7 + i * 3] = c.B;
        }
    }

    private void Send(params byte[] head)
    {
        Array.Clear(_report);
        head.CopyTo(_report, 0);
        _stream.Write(_report);
    }

    public void Dispose() => _stream.Dispose();
}
