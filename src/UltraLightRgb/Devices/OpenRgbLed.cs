using System.Net.Sockets;
using P = UltraLightRgb.OpenRgbProtocol;

namespace UltraLightRgb;

/// <summary>Connection to a local OpenRGB SDK server: handshake, controller list, raw packets.</summary>
public sealed class OpenRgbClient : IDisposable
{
    private const string ClientName = "Ultra Light RGB Control";
    private const int TimeoutMs = 2000, VersionTimeoutMs = 1000;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly byte[] _header = new byte[P.HeaderSize];

    public uint Version { get; }

    /// <summary>Set when the server announced a changed device list; indices are stale from then on.</summary>
    public bool ListChanged { get; private set; }

    private OpenRgbClient(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _stream.ReadTimeout = TimeoutMs;
        _stream.WriteTimeout = TimeoutMs;
        Send(0, P.SetClientName, P.ClientName(ClientName));
        Version = Math.Min(P.MaxVersion, NegotiateVersion());
    }

    public static OpenRgbClient Connect(string host, int port)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            if (!tcp.ConnectAsync(host, port).Wait(TimeoutMs))
                throw new TimeoutException($"OpenRGB server {host}:{port} did not answer");
            return new OpenRgbClient(tcp);
        }
        catch (AggregateException e)
        {
            tcp.Dispose();
            var inner = e.InnerException ?? e;
            throw new IOException($"OpenRGB server {host}:{port} not reachable ({inner.Message}). Is OpenRGB running with the SDK server enabled?", inner);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private uint NegotiateVersion()
    {
        Send(0, P.RequestProtocolVersion, P.U32(P.MaxVersion));
        _stream.ReadTimeout = VersionTimeoutMs;
        try
        {
            return ReceiveU32(P.RequestProtocolVersion);
        }
        catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            // protocol 0 servers never answer this request
            return 0;
        }
        finally
        {
            _stream.ReadTimeout = TimeoutMs;
        }
    }

    public List<(uint Id, P.ControllerInfo Info)> Controllers()
    {
        Send(0, P.RequestControllerCount, []);
        uint count = ReceiveU32(P.RequestControllerCount);
        var list = new List<(uint, P.ControllerInfo)>();
        for (uint i = 0; i < count; i++)
        {
            Send(i, P.RequestControllerData, Version >= 1 ? P.U32(Version) : []);
            list.Add((i, P.ParseController(Receive(P.RequestControllerData), Version)));
        }
        return list;
    }

    public void Send(uint deviceId, uint packetId, ReadOnlySpan<byte> data) => _stream.Write(P.Packet(deviceId, packetId, data));

    /// <summary>Reads packets the server sent on its own; only a device list change matters here.</summary>
    public void Drain()
    {
        while (_stream.DataAvailable) ReadPacket();
    }

    private uint ReceiveU32(uint packetId)
    {
        var data = Receive(packetId);
        if (data.Length < 4) throw new InvalidDataException($"OpenRGB packet {packetId} has {data.Length} bytes, 4 expected");
        return BitConverter.ToUInt32(data, 0);
    }

    private byte[] Receive(uint packetId)
    {
        while (true)
        {
            var (header, data) = ReadPacket();
            if (header.PacketId == packetId) return data;
        }
    }

    private (P.Header Header, byte[] Data) ReadPacket()
    {
        _stream.ReadExactly(_header);
        var h = P.ParseHeader(_header);
        if (h.Size > 16 * 1024 * 1024) throw new InvalidDataException($"OpenRGB packet of {h.Size} bytes refused");
        var data = new byte[h.Size];
        _stream.ReadExactly(data);
        if (h.PacketId == P.DeviceListUpdated) ListChanged = true;
        return (h, data);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }
}

/// <summary>Every OpenRGB controller whose name matches, painted with the theme colours.</summary>
public sealed class OpenRgbLed : ILed
{
    private readonly OpenRgbClient _client;
    private readonly List<(uint Id, P.ControllerInfo Info)> _targets;

    private OpenRgbLed(OpenRgbClient client, List<(uint, P.ControllerInfo)> targets)
    {
        _client = client;
        _targets = targets;
    }

    public static OpenRgbLed Open(OutputConfig cfg)
    {
        var client = OpenRgbClient.Connect(cfg.Host, cfg.Port);
        try
        {
            var targets = client.Controllers()
                .Where(c => c.Info.Colors > 0
                            && (cfg.Match == "*" || c.Info.Name.Contains(cfg.Match, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (targets.Count == 0)
                throw new IOException($"No OpenRGB controller with LEDs matches \"{cfg.Match}\" (protocol {client.Version})");

            // custom mode is the direct mode of each device; nothing is saved to the device
            foreach (var (id, _) in targets) client.Send(id, P.SetCustomMode, []);
            Log.Write($"OpenRGB (protocol {client.Version}): {string.Join(", ", targets.Select(t => $"{t.Info.Name} [{t.Info.Colors}]"))}");
            return new OpenRgbLed(client, targets);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Set(LedLook look, float phase)
    {
        _client.Drain();
        if (_client.ListChanged) throw new IOException("OpenRGB device list changed, reconnecting");
        foreach (var (id, info) in _targets)
            _client.Send(id, P.UpdateLeds, P.UpdateLedsData(info.Colors, i => look.At(i, phase)));
    }

    public void Dispose() => _client.Dispose();
}
