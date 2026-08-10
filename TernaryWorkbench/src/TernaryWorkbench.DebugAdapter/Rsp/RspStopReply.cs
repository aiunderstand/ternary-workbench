namespace TernaryWorkbench.DebugAdapter.Rsp;

public enum StopKind
{
    /// <summary>S/T stop reply: the hart halted (signal 5 = trap/step/break, 2 = interrupt).</summary>
    Stopped,
    /// <summary>W reply: the program ran to exit; ExitStatus carries the status trits' low byte.</summary>
    Exited,
}

public sealed record RspStopReply(StopKind Kind, int Signal, bool HwBreak, int ExitStatus)
{
    public static RspStopReply Parse(string payload)
    {
        if (payload.Length == 0)
            throw new FormatException("Empty stop reply");

        char type = payload[0];
        switch (type)
        {
            case 'W':
                return new RspStopReply(StopKind.Exited, 0, false,
                    Convert.ToInt32(payload[1..3], 16));
            case 'S':
                return new RspStopReply(StopKind.Stopped, Convert.ToInt32(payload[1..3], 16), false, 0);
            case 'T':
            {
                int signal = Convert.ToInt32(payload[1..3], 16);
                bool hwBreak = payload[3..].Contains("hwbreak:", StringComparison.Ordinal);
                return new RspStopReply(StopKind.Stopped, signal, hwBreak, 0);
            }
            default:
                throw new FormatException($"Unrecognized stop reply: \"{payload}\"");
        }
    }
}
