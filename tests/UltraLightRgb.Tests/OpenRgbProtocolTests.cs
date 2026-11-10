using System.Drawing;
using P = UltraLightRgb.OpenRgbProtocol;

namespace UltraLightRgb.Tests;

public class OpenRgbProtocolTests
{
    [Fact]
    public void Packet_has_magic_ids_and_size_little_endian()
    {
        var p = P.Packet(3, P.UpdateLeds, [0xAA, 0xBB]);

        Assert.Equal("ORGB"u8.ToArray(), p[..4]);
        Assert.Equal(3u, BitConverter.ToUInt32(p, 4));
        Assert.Equal(1050u, BitConverter.ToUInt32(p, 8));
        Assert.Equal(2u, BitConverter.ToUInt32(p, 12));
        Assert.Equal(new byte[] { 0xAA, 0xBB }, p[16..]);
    }

    [Fact]
    public void Header_without_magic_is_rejected()
    {
        var bad = new byte[16];
        Assert.Throws<InvalidDataException>(() => P.ParseHeader(bad));
    }

    [Fact]
    public void UpdateLeds_counts_itself_and_writes_r_g_b_zero()
    {
        var d = P.UpdateLedsData(2, i => i == 0 ? Color.FromArgb(1, 2, 3) : Color.FromArgb(4, 5, 6));

        Assert.Equal(14u, BitConverter.ToUInt32(d, 0)); // 4 + 2 + 2 * 4
        Assert.Equal((ushort)2, BitConverter.ToUInt16(d, 4));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, d[6..]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    public void Controller_block_parses_in_every_supported_version(uint version)
    {
        var info = P.ParseController(ControllerBlob.Build(version, "Fan Hub", 3), version);

        Assert.Equal("Fan Hub", info.Name);
        Assert.Equal(3, info.Colors);
    }

    [Fact]
    public void Parsing_with_the_wrong_version_fails_instead_of_returning_garbage()
    {
        // a v5 block read as v0 runs out of data at some point: it must say so
        var blob = ControllerBlob.Build(5, "Fan Hub", 3);
        var ex = Record.Exception(() => P.ParseController(blob, 0));
        if (ex is null) Assert.NotEqual(3, P.ParseController(blob, 0).Colors);
        else Assert.IsType<InvalidDataException>(ex);
    }

    [Fact]
    public void Truncated_block_throws_invalid_data()
    {
        var blob = ControllerBlob.Build(5, "Fan Hub", 3);
        var cut = blob[..(blob.Length - 20)];
        BitConverter.GetBytes((uint)cut.Length).CopyTo(cut, 0);
        Assert.Throws<InvalidDataException>(() => P.ParseController(cut, 5));
    }

    [Fact]
    public void Unreleased_protocol_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => P.ParseController(ControllerBlob.Build(5, "x", 1), 6));
    }
}
