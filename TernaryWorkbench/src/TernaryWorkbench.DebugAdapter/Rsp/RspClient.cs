using System.Text;

namespace TernaryWorkbench.DebugAdapter.Rsp;

/// <summary>
/// GDB Remote Serial Protocol client for the R2R REBEL-6 stub (rebel6-debug.md,
/// tooling appendix). Register model: gdb regnums 0..31 are X0..X31, 32 is pc, all
/// 64-bit sign-extended balanced values in little-endian hex. Runs synchronously:
/// Continue/Step block until the stub reports a stop; Interrupt() may be called from
/// another thread to inject the 0x03 break byte while a resume is in flight.
/// </summary>
public sealed class RspClient : IDisposable
{
    public const int PcRegnum = 32;
    public const int RegisterCount = 33;

    public static readonly string[] AbiNames =
    {
        "zero", "ra", "sp", "gp", "tp",
        "t0", "t1", "t2", "s0", "s1",
        "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7",
        "s2", "s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10", "s11",
        "t3", "t4", "t5", "t6",
    };

    public static readonly string[] SystemRegisterNames =
    {
        "mtvec", "mepc", "mcause", "mstatus", "mscratch",
        "mie", "mip", "mhartid", "mcycle", "minstret",
    };

    private readonly Stream _stream;
    private readonly object _writeLock = new();
    private bool _noAckMode;

    public RspClient(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>qSupported + no-ack negotiation and the initial '?' stop query.</summary>
    public RspStopReply Negotiate()
    {
        Exchange("qSupported:hwbreak+;swbreak+;vContSupported+");
        if (Exchange("QStartNoAckMode") == "OK")
            _noAckMode = true;
        return RspStopReply.Parse(Exchange("?"));
    }

    public long[] ReadAllRegisters()
    {
        string reply = Exchange("g");
        if (reply.Length < RegisterCount * 16)
            throw new InvalidOperationException($"Short g reply: {reply.Length} chars");
        var values = new long[RegisterCount];
        for (int i = 0; i < RegisterCount; i++)
            values[i] = FromLeHex(reply.Substring(i * 16, 16));
        return values;
    }

    public long ReadRegister(int regnum)
    {
        string reply = Exchange($"p{regnum:x}");
        if (reply.StartsWith('E'))
            throw new InvalidOperationException($"Register read failed: {reply}");
        return FromLeHex(reply);
    }

    public void WriteRegister(int regnum, long value)
    {
        string reply = Exchange($"P{regnum:x}={ToLeHex(value)}");
        if (reply != "OK")
            throw new InvalidOperationException($"Register write failed: {reply}");
    }

    /// <summary>Arms an execute trigger (Z1). False when the 4 triggers are exhausted.</summary>
    public bool TrySetBreakpoint(long address)
    {
        return Exchange($"Z1,{address:x},0") == "OK";
    }

    public void ClearBreakpoint(long address)
    {
        Exchange($"z1,{address:x},0");
    }

    public RspStopReply Continue() => RspStopReply.Parse(Exchange("c"));

    public RspStopReply Step() => RspStopReply.Parse(Exchange("s"));

    /// <summary>
    /// Injects the RSP break byte; the blocked Continue() returns with signal 2
    /// once the stub's interrupt poll picks it up.
    /// </summary>
    public void Interrupt()
    {
        lock (_writeLock)
        {
            _stream.WriteByte(0x03);
            _stream.Flush();
        }
    }

    /// <summary>qRcmd passthrough; returns the stub's rendered text.</summary>
    public string Monitor(string command)
    {
        string reply = Exchange("qRcmd," + ToHex(command));
        if (reply == "OK")
            return string.Empty;
        if (reply.StartsWith('E') && reply.Length == 3)
            throw new InvalidOperationException($"Monitor command failed: {reply}");
        return FromHex(reply);
    }

    /// <summary>One byte per tryte address: each tryte's low 8 bits (ABI string layout).</summary>
    public byte[] ReadMemory(long address, int length)
    {
        string reply = Exchange($"m{address:x},{length:x}");
        if (reply.StartsWith('E'))
            throw new InvalidOperationException($"Memory read failed: {reply}");
        var bytes = new byte[reply.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(reply.Substring(i * 2, 2), 16);
        return bytes;
    }

    /// <summary>Detach: the stub runs the program to completion, then acknowledges.</summary>
    public void Detach()
    {
        Exchange("D");
    }

    /// <summary>Kill: fire-and-forget, the stub stops serving.</summary>
    public void Kill()
    {
        SendPacket("k");
    }

    public void Dispose() => _stream.Dispose();

    private string Exchange(string payload)
    {
        SendPacket(payload);
        return ReceivePacket();
    }

    private void SendPacket(string payload)
    {
        byte sum = 0;
        foreach (char c in payload)
            sum += (byte)c;
        string frame = $"${payload}#{sum:x2}";
        byte[] bytes = Encoding.ASCII.GetBytes(frame);
        lock (_writeLock)
        {
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
        if (!_noAckMode)
        {
            // The stub acks every packet; consume the '+' (a '-' would mean resend,
            // which a loopback TCP link to our own stub never produces).
            int ack = ReadByteChecked();
            if (ack == '-')
                throw new InvalidOperationException("RSP checksum rejected by the stub");
        }
    }

    private string ReceivePacket()
    {
        var payload = new StringBuilder();
        while (true)
        {
            int c = ReadByteChecked();
            if (c != '$')
                continue; // Skip acks and stray bytes between packets

            payload.Clear();
            byte sum = 0;
            while (true)
            {
                c = ReadByteChecked();
                if (c == '#')
                    break;
                sum += (byte)c;
                payload.Append((char)c);
            }
            int hi = ReadByteChecked();
            int lo = ReadByteChecked();
            byte expected = Convert.ToByte($"{(char)hi}{(char)lo}", 16);
            bool ok = expected == sum;
            if (!_noAckMode)
            {
                lock (_writeLock)
                {
                    _stream.WriteByte((byte)(ok ? '+' : '-'));
                    _stream.Flush();
                }
            }
            if (ok)
                return payload.ToString();
        }
    }

    private int ReadByteChecked()
    {
        int b = _stream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException("RSP connection closed");
        return b;
    }

    internal static string ToLeHex(long value)
    {
        ulong u = (ulong)value;
        var s = new StringBuilder(16);
        for (int b = 0; b < 8; b++)
            s.Append(((u >> (8 * b)) & 0xFF).ToString("x2"));
        return s.ToString();
    }

    internal static long FromLeHex(string hex)
    {
        if (hex.Length != 16)
            throw new FormatException($"Expected 16 hex chars, got \"{hex}\"");
        ulong u = 0;
        for (int b = 0; b < 8; b++)
            u |= (ulong)Convert.ToByte(hex.Substring(b * 2, 2), 16) << (8 * b);
        return (long)u;
    }

    private static string ToHex(string text)
    {
        var s = new StringBuilder(text.Length * 2);
        foreach (char c in text)
            s.Append(((byte)c).ToString("x2"));
        return s.ToString();
    }

    private static string FromHex(string hex)
    {
        var s = new StringBuilder(hex.Length / 2);
        for (int i = 0; i + 1 < hex.Length; i += 2)
            s.Append((char)Convert.ToByte(hex.Substring(i, 2), 16));
        return s.ToString();
    }
}
