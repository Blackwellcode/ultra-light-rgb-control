using System.Buffers.Binary;
using System.Drawing;
using System.Text;

namespace UltraLightRgb;

/// <summary>
/// Packet format of the OpenRGB SDK (Documentation/OpenRGBSDK.md in the OpenRGB repository).
/// Pure functions without I/O, so the byte layout can be tested without a server.
/// </summary>
public static class OpenRgbProtocol
{
    /// <summary>Newest released protocol (OpenRGB 1.0rc1); version 6 is unreleased and changes several blocks.</summary>
    public const uint MaxVersion = 5;

    public const int HeaderSize = 16;

    public const uint RequestControllerCount = 0, RequestControllerData = 1, RequestProtocolVersion = 40,
                      SetClientName = 50, DeviceListUpdated = 100, UpdateLeds = 1050, SetCustomMode = 1100;

    private static ReadOnlySpan<byte> Magic => "ORGB"u8;

    public readonly record struct Header(uint DeviceId, uint PacketId, uint Size);

    /// <summary>What this project needs of a controller: its name and how many colours UpdateLEDs takes.</summary>
    public readonly record struct ControllerInfo(string Name, int Colors);

    public static byte[] Packet(uint deviceId, uint packetId, ReadOnlySpan<byte> data)
    {
        var p = new byte[HeaderSize + data.Length];
        Magic.CopyTo(p);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), deviceId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), (uint)data.Length);
        data.CopyTo(p.AsSpan(HeaderSize));
        return p;
    }

    public static byte[] U32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        return b;
    }

    public static byte[] ClientName(string name)
    {
        var b = new byte[Encoding.ASCII.GetByteCount(name) + 1];
        Encoding.ASCII.GetBytes(name, b);
        return b; // the server expects the terminating zero
    }

    public static Header ParseHeader(ReadOnlySpan<byte> h)
    {
        if (h.Length < HeaderSize || !h[..4].SequenceEqual(Magic))
            throw new InvalidDataException("not an OpenRGB packet (magic \"ORGB\" missing)");
        return new Header(
            BinaryPrimitives.ReadUInt32LittleEndian(h[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[12..]));
    }

    /// <summary>
    /// UpdateLEDs data block: data_size (counting itself), number of colours, then R, G, B and a zero
    /// byte per LED (OpenRGB's RGBColor is 0x00BBGGRR, little endian).
    /// </summary>
    public static byte[] UpdateLedsData(int count, Func<int, Color> colorOf)
    {
        if (count is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(count));
        int size = 4 + 2 + 4 * count;
        var d = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(d, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(4), (ushort)count);
        for (int i = 0; i < count; i++)
        {
            var c = colorOf(i);
            int o = 6 + 4 * i;
            d[o] = c.R;
            d[o + 1] = c.G;
            d[o + 2] = c.B;
        }
        return d;
    }

    /// <summary>Reads name and colour count out of a controller data block of the given protocol version.</summary>
    public static ControllerInfo ParseController(ReadOnlySpan<byte> payload, uint version)
    {
        if (version > MaxVersion) throw new ArgumentOutOfRangeException(nameof(version), $"protocol {version} is not supported");
        var r = new Reader(payload);
        uint dataSize = r.U32();
        if (dataSize > payload.Length) throw new InvalidDataException($"data_size {dataSize} exceeds the packet ({payload.Length} bytes)");

        r.Skip(4); // type
        string name = r.Str();
        if (version >= 1) r.Str(); // vendor
        for (int i = 0; i < 4; i++) r.Str(); // description, version, serial, location

        int modes = r.U16();
        r.Skip(4); // active_mode
        for (int m = 0; m < modes; m++)
        {
            r.Str();
            r.Skip(4 * 4); // value, flags, speed_min, speed_max
            if (version >= 3) r.Skip(4 * 2); // brightness_min, brightness_max
            r.Skip(4 * 3); // colors_min, colors_max, speed
            if (version >= 3) r.Skip(4); // brightness
            r.Skip(4 * 2); // direction, color_mode
            r.Skip(4 * r.U16()); // mode colours
        }

        int zones = r.U16();
        for (int z = 0; z < zones; z++)
        {
            r.Str();
            r.Skip(4 * 4); // type, leds_min, leds_max, leds_count
            r.Skip(r.U16()); // matrix map, its length counts height, width and data
            if (version >= 4)
            {
                int segments = r.U16();
                for (int s = 0; s < segments; s++)
                {
                    r.Str();
                    r.Skip(4 * 3); // type, start_idx, leds_count
                }
            }
            if (version >= 5) r.Skip(4); // zone flags
        }

        int leds = r.U16();
        for (int l = 0; l < leds; l++)
        {
            r.Str();
            r.Skip(4); // led value
        }

        int colors = r.U16();
        r.Skip(4 * colors);
        return new ControllerInfo(name, colors);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;

        public Reader(ReadOnlySpan<byte> data) => _data = data;

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || _pos + n > _data.Length)
                throw new InvalidDataException($"controller data ends early: {n} bytes needed at offset {_pos} of {_data.Length}");
            var s = _data.Slice(_pos, n);
            _pos += n;
            return s;
        }

        public void Skip(int n) => Take(n);

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public int U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public string Str()
        {
            var s = Take(U16());
            int end = s.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end >= 0 ? s[..end] : s);
        }
    }
}
