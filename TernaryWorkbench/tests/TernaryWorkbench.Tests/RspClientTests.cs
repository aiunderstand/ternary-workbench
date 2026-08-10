using System.Net.Sockets;
using FluentAssertions;
using TernaryWorkbench.DebugAdapter.Rsp;
using Xunit;

namespace TernaryWorkbench.Tests;

public class RspClientTests : IDisposable
{
    private readonly MockRspServer _server = new();
    private readonly TcpClient _tcp = new();
    private readonly RspClient _client;

    public RspClientTests()
    {
        _tcp.Connect("127.0.0.1", _server.Port);
        _client = new RspClient(_tcp.GetStream());
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    [Fact]
    public void Negotiate_ReportsInitialStop()
    {
        RspStopReply reply = _client.Negotiate();
        reply.Kind.Should().Be(StopKind.Stopped);
        reply.Signal.Should().Be(5);
    }

    [Fact]
    public void RegistersRoundTrip()
    {
        _server.Registers[10] = 42;          // a0
        _server.Registers[32] = 7;           // pc
        _client.Negotiate();

        long[] all = _client.ReadAllRegisters();
        all.Should().HaveCount(33);
        all[10].Should().Be(42);
        all[32].Should().Be(7);

        _client.WriteRegister(5, -141_214_768_240);
        _client.ReadRegister(5).Should().Be(-141_214_768_240);
        _server.Registers[5].Should().Be(-141_214_768_240);
    }

    [Fact]
    public void Breakpoints_ExhaustAtFourTriggers()
    {
        _client.Negotiate();
        for (long address = 0; address < 4; address++)
            _client.TrySetBreakpoint(address).Should().BeTrue();
        _client.TrySetBreakpoint(4).Should().BeFalse();

        _client.ClearBreakpoint(0);
        _client.TrySetBreakpoint(4).Should().BeTrue();
        _server.ArmedTriggers.Should().BeEquivalentTo(new long[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void ContinueAndStep_ParseStopReplies()
    {
        _client.Negotiate();
        RspStopReply hit = _client.Continue();
        hit.Kind.Should().Be(StopKind.Stopped);
        hit.HwBreak.Should().BeTrue();

        RspStopReply stepped = _client.Step();
        stepped.Kind.Should().Be(StopKind.Stopped);
        stepped.HwBreak.Should().BeFalse();

        _server.ContinueReply = "W2a";
        RspStopReply exited = _client.Continue();
        exited.Kind.Should().Be(StopKind.Exited);
        exited.ExitStatus.Should().Be(42);
    }

    [Fact]
    public void Monitor_RoundTripsHexText()
    {
        _client.Negotiate();
        _client.Monitor("info").Should().Contain("tritflips = 12");
        _server.MonitorCommands.Should().Contain("info");
    }

    [Fact]
    public void ReadMemory_ReturnsTryteLowBytes()
    {
        _server.Memory[3] = (byte)'H';
        _server.Memory[4] = (byte)'i';
        _client.Negotiate();
        _client.ReadMemory(3, 2).Should().Equal((byte)'H', (byte)'i');
    }
}
