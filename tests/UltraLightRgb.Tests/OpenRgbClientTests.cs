using System.Net;
using System.Net.Sockets;
using System.Drawing;
using P = UltraLightRgb.OpenRgbProtocol;

namespace UltraLightRgb.Tests;

/// <summary>Runs the real client against a minimal SDK server on the loopback interface.</summary>
public sealed class OpenRgbClientTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<(uint Dev, uint Id, byte[] Data)> _received = [];
    private readonly ManualResetEventSlim _gotLeds = new();

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public OpenRgbClientTests() => _listener.Start();

    /// <param name="version">Protocol the server speaks; 0 means it ignores the version request.</param>
    private Task Serve(uint version, string[] names)
    {
        return Task.Run(() =>
        {
            using var client = _listener.AcceptTcpClient();
            using var s = client.GetStream();
            var header = new byte[P.HeaderSize];
            while (true)
            {
                try
                {
                    s.ReadExactly(header);
                }
                catch (EndOfStreamException)
                {
                    return;
                }
                var h = P.ParseHeader(header);
                var data = new byte[h.Size];
                s.ReadExactly(data);
                lock (_received) _received.Add((h.DeviceId, h.PacketId, data));

                switch (h.PacketId)
                {
                    case P.RequestProtocolVersion when version > 0:
                        s.Write(P.Packet(0, P.RequestProtocolVersion, P.U32(version)));
                        break;
                    case P.RequestControllerCount:
                        s.Write(P.Packet(0, P.RequestControllerCount, P.U32((uint)names.Length)));
                        break;
                    case P.RequestControllerData:
                        uint asked = data.Length >= 4 ? BitConverter.ToUInt32(data, 0) : 0;
                        s.Write(P.Packet(h.DeviceId, P.RequestControllerData, ControllerBlob.Build(asked, names[h.DeviceId], 4)));
                        break;
                    case P.UpdateLeds:
                        _gotLeds.Set();
                        break;
                }
            }
        });
    }

    [Fact]
    public async Task Matching_controllers_get_custom_mode_and_colours()
    {
        var server = Serve(5, ["Corsair Fan Hub", "Keyboard"]);
        var cfg = new OutputConfig { Driver = "openrgb", Host = "127.0.0.1", Port = Port, Match = "fan" };

        using (var led = OpenRgbLed.Open(cfg))
        {
            led.Set(new LedLook(Color.FromArgb(10, 20, 30)), 0);
            Assert.True(_gotLeds.Wait(2000), "no UpdateLEDs packet arrived");
        }
        await server.WaitAsync(TimeSpan.FromSeconds(2));

        lock (_received)
        {
            Assert.Contains(_received, p => p.Id == P.SetCustomMode && p.Dev == 0);
            Assert.DoesNotContain(_received, p => p.Id == P.SetCustomMode && p.Dev == 1);
            var leds = _received.Single(p => p.Id == P.UpdateLeds);
            Assert.Equal(0u, leds.Dev);
            Assert.Equal((ushort)4, BitConverter.ToUInt16(leds.Data, 4));
            Assert.Equal(new byte[] { 10, 20, 30, 0 }, leds.Data[6..10]);
            // the client asked for controller data in the negotiated version
            Assert.All(_received.Where(p => p.Id == P.RequestControllerData), p => Assert.Equal(5u, BitConverter.ToUInt32(p.Data, 0)));
        }
    }

    [Fact]
    public async Task Newer_server_is_spoken_to_in_the_newest_supported_version()
    {
        var server = Serve(6, ["Strip"]);
        using (var client = OpenRgbClient.Connect("127.0.0.1", Port))
        {
            Assert.Equal(P.MaxVersion, client.Version);
            Assert.Single(client.Controllers());
        }
        await server.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Protocol_zero_server_is_detected_by_its_silence()
    {
        var server = Serve(0, ["Old Strip"]);
        using (var client = OpenRgbClient.Connect("127.0.0.1", Port))
        {
            Assert.Equal(0u, client.Version);
            var list = client.Controllers();
            Assert.Equal("Old Strip", list[0].Info.Name);
        }
        await server.WaitAsync(TimeSpan.FromSeconds(2));
        lock (_received)
            Assert.All(_received.Where(p => p.Id == P.RequestControllerData), p => Assert.Empty(p.Data));
    }

    [Fact]
    public async Task No_match_is_an_io_error_the_retry_loop_understands()
    {
        var server = Serve(5, ["Keyboard"]);
        var cfg = new OutputConfig { Driver = "openrgb", Host = "127.0.0.1", Port = Port, Match = "fan" };
        Assert.Throws<IOException>(() => OpenRgbLed.Open(cfg));
        await server.WaitAsync(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _listener.Stop();
        _gotLeds.Dispose();
    }
}
