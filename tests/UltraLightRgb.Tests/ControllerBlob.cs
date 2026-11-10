using System.Text;

namespace UltraLightRgb.Tests;

/// <summary>
/// Writes a controller data block field by field after the tables in OpenRGB's
/// Documentation/OpenRGBSDK.md, independent of the parser under test.
/// </summary>
internal static class ControllerBlob
{
    public static byte[] Build(uint version, string name, int leds)
    {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        void Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s + "\0");
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        w.Write(7); // type
        Str(name);
        if (version >= 1) Str("Vendor");
        Str("Description");
        Str("1.0");
        Str("SERIAL");
        Str("HID: /dev/x");

        w.Write((ushort)2); // num_modes
        w.Write(1); // active_mode
        for (int m = 0; m < 2; m++)
        {
            Str(m == 0 ? "Direct" : "Static");
            w.Write(m); // mode_value (protocol < 6)
            w.Write(0x20u); // flags
            w.Write(0u); w.Write(4u); // speed_min, speed_max
            if (version >= 3) { w.Write(0u); w.Write(100u); } // brightness_min, brightness_max
            w.Write(0u); w.Write(1u); // colors_min, colors_max
            w.Write(2u); // speed
            if (version >= 3) w.Write(100u); // brightness
            w.Write(0u); w.Write(1u); // direction, color_mode
            w.Write((ushort)m); // mode_num_colors: 0, then 1
            for (int c = 0; c < m; c++) w.Write(0x00FF0000u);
        }

        w.Write((ushort)2); // num_zones
        for (int z = 0; z < 2; z++)
        {
            Str(z == 0 ? "Strip" : "Matrix");
            w.Write(z == 0 ? 1 : 2); // zone_type
            w.Write(0u); w.Write(60u); w.Write(z == 0 ? 2u : 1u); // leds_min, leds_max, leds_count
            if (z == 0)
            {
                w.Write((ushort)0); // no matrix map
            }
            else
            {
                // 2 x 2 map: height, width, 4 entries
                w.Write((ushort)(8 + 4 * 4));
                w.Write(2u); w.Write(2u);
                for (int i = 0; i < 4; i++) w.Write(i == 0 ? 2u : 0xFFFFFFFFu);
            }
            if (version >= 4)
            {
                w.Write((ushort)1); // num_segments
                Str("Segment");
                w.Write(1); w.Write(0u); w.Write(1u); // type, start_idx, leds_count
            }
            if (version >= 5) w.Write(0u); // zone_flags
        }

        w.Write((ushort)leds); // num_leds
        for (int l = 0; l < leds; l++)
        {
            Str($"LED {l}");
            w.Write((uint)l); // led_value (protocol < 6)
        }

        w.Write((ushort)leds); // num_colors
        for (int c = 0; c < leds; c++) w.Write(0u);

        if (version >= 5)
        {
            w.Write((ushort)0); // num_led_display_names
            w.Write(0u); // flags
        }
        w.Flush();

        var block = body.ToArray();
        var packet = new byte[4 + block.Length];
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0); // data_size
        block.CopyTo(packet, 4);
        return packet;
    }
}
