using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TernaryWorkbench.Tests;

/// <summary>
/// In-process stand-in for the R2R RSP stub (rspserver.cpp): same framing, ack, and
/// packet surface, backed by a 33-register array, a tryte "memory", and the 4-trigger
/// limit, so the DAP adapter can be exercised end-to-end without the C++ simulator.
/// </summary>
public sealed class MockRspServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Thread _thread;
    private readonly List<long> _triggers = new();
    private bool _noAckMode;
    private volatile bool _stopped;

    public int Port { get; }
    public long[] Registers { get; } = new long[33];
    public byte[] Memory { get; } = new byte[256];
    public List<string> MonitorCommands { get; } = new();

    /// <summary>Stop reply sent for the next c packet; W-replies mark the program exited.</summary>
    public string ContinueReply { get; set; } = "T05hwbreak:;";

    public MockRspServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(Serve) { IsBackground = true };
        _thread.Start();
    }

    public IReadOnlyList<long> ArmedTriggers => _triggers;

    private void Serve()
    {
        try
        {
            using TcpClient client = _listener.AcceptTcpClient();
            client.NoDelay = true;
            NetworkStream stream = client.GetStream();
            while (!_stopped)
            {
                string? payload = ReadPacket(stream);
                if (payload is null)
                    return;
                if (!Handle(stream, payload))
                    return;
            }
        }
        catch (Exception)
        {
            // Listener disposed or client gone - test over.
        }
    }

    private bool Handle(NetworkStream stream, string payload)
    {
        switch (payload)
        {
            case var p when p.StartsWith("qSupported", StringComparison.Ordinal):
                Send(stream, "PacketSize=4096;QStartNoAckMode+;hwbreak+;swbreak+");
                return true;
            case "QStartNoAckMode":
                Send(stream, "OK");
                _noAckMode = true;
                return true;
            case "?":
                Send(stream, "S05");
                return true;
            case "g":
            {
                var reply = new StringBuilder();
                foreach (long value in Registers)
                    reply.Append(ToLeHex(value));
                Send(stream, reply.ToString());
                return true;
            }
            case var p when p.StartsWith('p'):
            {
                int regnum = Convert.ToInt32(p[1..], 16);
                if (regnum >= Registers.Length)
                {
                    Send(stream, "E01");
                    return true;
                }
                Send(stream, ToLeHex(Registers[regnum]));
                return true;
            }
            case var p when p.StartsWith('P'):
            {
                int eq = p.IndexOf('=');
                int regnum = Convert.ToInt32(p[1..eq], 16);
                Registers[regnum] = FromLeHex(p[(eq + 1)..]);
                Send(stream, "OK");
                return true;
            }
            case var p when p.StartsWith("Z1,", StringComparison.Ordinal) || p.StartsWith("Z0,", StringComparison.Ordinal):
            {
                long address = ParseBreakpointAddress(p);
                if (_triggers.Count >= 4)
                {
                    Send(stream, "E28");
                    return true;
                }
                _triggers.Add(address);
                Send(stream, "OK");
                return true;
            }
            case var p when p.StartsWith("z1,", StringComparison.Ordinal) || p.StartsWith("z0,", StringComparison.Ordinal):
                _triggers.Remove(ParseBreakpointAddress(p));
                Send(stream, "OK");
                return true;
            case "c":
                Send(stream, ContinueReply);
                return true;
            case "s":
                Registers[32]++;
                Send(stream, "S05");
                return true;
            case var p when p.StartsWith("m", StringComparison.Ordinal) && p.Length > 1 && Uri.IsHexDigit(p[1]):
            {
                string[] parts = p[1..].Split(',');
                long address = Convert.ToInt64(parts[0], 16);
                int length = Convert.ToInt32(parts[1], 16);
                var reply = new StringBuilder();
                for (int i = 0; i < length; i++)
                    reply.Append(Memory[address + i].ToString("x2"));
                Send(stream, reply.ToString());
                return true;
            }
            case var p when p.StartsWith("qRcmd,", StringComparison.Ordinal):
            {
                string command = FromHexText(p["qRcmd,".Length..]);
                MonitorCommands.Add(command);
                string text = command switch
                {
                    "reg mepc" => "mepc (X-2) = 77 = " + new string('0', 20) + "+-+-\n",
                    "info" => "pc = 3  mode = M  retired = 3  tritflips = 12\n",
                    _ => $"mock: {command}\n",
                };
                Send(stream, ToHexText(text));
                return true;
            }
            case "D":
                Send(stream, "OK");
                return false;
            case "k":
                return false;
            default:
                Send(stream, "");
                return true;
        }
    }

    private static long ParseBreakpointAddress(string payload)
    {
        string[] parts = payload.Split(',');
        return Convert.ToInt64(parts[1], 16);
    }

    private string? ReadPacket(NetworkStream stream)
    {
        var payload = new StringBuilder();
        while (true)
        {
            int c = stream.ReadByte();
            if (c < 0)
                return null;
            if (c != '$')
                continue;

            payload.Clear();
            byte sum = 0;
            while (true)
            {
                c = stream.ReadByte();
                if (c < 0)
                    return null;
                if (c == '#')
                    break;
                sum += (byte)c;
                payload.Append((char)c);
            }
            int hi = stream.ReadByte();
            int lo = stream.ReadByte();
            if (hi < 0 || lo < 0)
                return null;
            if (!_noAckMode)
                stream.WriteByte((byte)'+');
            return payload.ToString();
        }
    }

    private void Send(NetworkStream stream, string payload)
    {
        byte sum = 0;
        foreach (char c in payload)
            sum += (byte)c;
        byte[] frame = Encoding.ASCII.GetBytes($"${payload}#{sum:x2}");
        stream.Write(frame, 0, frame.Length);
    }

    private static string ToLeHex(long value)
    {
        ulong u = (ulong)value;
        var s = new StringBuilder(16);
        for (int b = 0; b < 8; b++)
            s.Append(((u >> (8 * b)) & 0xFF).ToString("x2"));
        return s.ToString();
    }

    private static long FromLeHex(string hex)
    {
        ulong u = 0;
        for (int b = 0; b < 8; b++)
            u |= (ulong)Convert.ToByte(hex.Substring(b * 2, 2), 16) << (8 * b);
        return (long)u;
    }

    private static string ToHexText(string text)
    {
        var s = new StringBuilder(text.Length * 2);
        foreach (char c in text)
            s.Append(((byte)c).ToString("x2"));
        return s.ToString();
    }

    private static string FromHexText(string hex)
    {
        var s = new StringBuilder(hex.Length / 2);
        for (int i = 0; i + 1 < hex.Length; i += 2)
            s.Append((char)Convert.ToByte(hex.Substring(i, 2), 16));
        return s.ToString();
    }

    public void Dispose()
    {
        _stopped = true;
        _listener.Dispose();
    }
}
