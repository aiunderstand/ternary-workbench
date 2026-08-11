using FluentAssertions;
using TernaryWorkbench.RebelAssembler;
using Xunit;
using Asm = TernaryWorkbench.RebelAssembler.Rebel6Assembler;

namespace TernaryWorkbench.Tests;

/// <summary>
/// Tests for the REBEL-6 program-level assembler: ABI and platform register names
/// (docs/rebel6-abi.md identity map, docs/rebel6-platform.md standard layout) and the
/// directive/data model (.text/.data/.word/.zero) with its flat pre-linker data layout.
/// </summary>
public class Rebel6ProgramAssemblerTests
{
    // =========================================================================
    // 1. ABI register names — identity map xN ↔ X+N (docs/rebel6-abi.md)
    // =========================================================================

    [Theory]
    [InlineData("zero", "X0")]
    [InlineData("ra",   "X1")]
    [InlineData("sp",   "X2")]
    [InlineData("tp",   "X4")]
    [InlineData("t0",   "X5")]
    [InlineData("t2",   "X7")]
    [InlineData("s0",   "X8")]
    [InlineData("fp",   "X8")]
    [InlineData("s1",   "X9")]
    [InlineData("a0",   "X10")]
    [InlineData("a7",   "X17")]
    [InlineData("s2",   "X18")]
    [InlineData("s11",  "X27")]
    [InlineData("t3",   "X28")]
    [InlineData("t6",   "X31")]
    [InlineData("e0",   "X32")]
    [InlineData("e332", "X364")]
    public void AbiRegisterName_EncodesIdenticallyToArchitecturalName(string abi, string arch)
    {
        Asm.Translate($"MV.T {abi}, {abi}").Should().Be(Asm.Translate($"MV.T {arch}, {arch}"));
    }

    [Fact]
    public void Gp_IsRetired_AndRejected()
    {
        var act = () => Asm.Translate("MV.T gp, X1");
        act.Should().Throw<InvalidOperationException>(
            because: "the ABI retires gp — the zero window replaces it and X3 is a reserved platform register");
    }

    // =========================================================================
    // 2. Platform system-register names — standard layout (docs/rebel6-platform.md)
    // =========================================================================

    [Theory]
    [InlineData("mtvec",    "X-1")]
    [InlineData("mepc",     "X-2")]
    [InlineData("mcause",   "X-3")]
    [InlineData("mstatus",  "X-4")]
    [InlineData("mscratch", "X-5")]
    [InlineData("mie",      "X-6")]
    [InlineData("mip",      "X-7")]
    [InlineData("mhartid",  "X-8")]
    [InlineData("sstatus",  "X-9")]
    [InlineData("mcycle",   "X-10")]
    [InlineData("minstret", "X-11")]
    [InlineData("stream0",  "X-12")]
    [InlineData("stream1",  "X-13")]
    [InlineData("stream2",  "X-14")]
    [InlineData("stvec",    "X-15")]
    [InlineData("sepc",     "X-16")]
    [InlineData("scause",   "X-17")]
    [InlineData("sscratch", "X-18")]
    [InlineData("medeleg",  "X-19")]
    [InlineData("mideleg",  "X-20")]
    [InlineData("sie",      "X-21")]
    [InlineData("sip",      "X-22")]
    public void PlatformRegisterName_EncodesIdenticallyToNegativeIndex(string name, string arch)
    {
        Asm.Translate($"MV.T X1, {name}").Should().Be(Asm.Translate($"MV.T X1, {arch}"));
    }

    [Fact]
    public void CsrName_InZicsrImmediateField_StillResolvesAsCsrNumber()
    {
        // mstatus is both a platform register alias (X-4) and a CSR name (0x300);
        // the Zicsr imm12 field must keep resolving it as the CSR number.
        var mc = Asm.Translate("CSRRW X5, mstatus, X6");
        var explicitNumber = Asm.Translate($"CSRRW X5, {0x300}, X6");
        mc.Should().Be(explicitNumber);
    }

    [Fact]
    public void Label_ShadowingAnAliasName_IsRejected()
    {
        var act = () => Asm.AssembleProgram("sp:\nNOP.T");
        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with a register name*");
    }

    // =========================================================================
    // 3. Directive/data model — .text/.data/.word/.zero
    // =========================================================================

    private const string SmallProgram = """
        .text
        main:
            li.t sp, buf
            nop.t
        .data
        buf:
            .word 730
        """;

    [Fact]
    public void DataBase_IsInstructionImageRoundedToNextWordBoundary()
    {
        // 2 instructions = 64 trits = ceil(64/6) = 11 trytes → next 4-tryte boundary = 12.
        Asm.AssembleProgram(SmallProgram).DataBaseAddress.Should().Be(12);

        // 3 instructions = 96 trits = 16 trytes, already aligned → strictly greater boundary = 20
        // (matching the R2R reference toolchain's rounding).
        var threeInstr = Asm.AssembleProgram("""
            nop.t
            nop.t
            nop.t
            .data
            w: .word 1
            """);
        threeInstr.DataBaseAddress.Should().Be(20);
    }

    [Fact]
    public void Word_EmitsFourTrytes_LittleEndian()
    {
        var program = Asm.AssembleProgram(SmallProgram);
        program.Data.Should().HaveCount(1);
        var datum = program.Data[0];
        datum.Address.Should().Be(12);
        // 730 = 3^6 + 1: least significant tryte first.
        datum.Trytes.Should().Equal("00000+", "00000+", "000000", "000000");
    }

    [Fact]
    public void Zero_EmitsThatManyZeroTrytes()
    {
        var program = Asm.AssembleProgram("""
            nop.t
            .data
            buf: .zero 5
            """);
        program.Data.Should().HaveCount(1);
        program.Data[0].Trytes.Should().HaveCount(5).And.OnlyContain(t => t == "000000");
    }

    [Fact]
    public void ConsecutiveData_AdvancesTheTryteCounter()
    {
        var program = Asm.AssembleProgram("""
            nop.t
            .data
            a: .word 1
            b: .zero 3
            c: .word 2
            """);
        var addresses = program.Data.Select(d => d.Address).ToList();
        addresses.Should().Equal(program.DataBaseAddress, program.DataBaseAddress + 4, program.DataBaseAddress + 7);
    }

    // =========================================================================
    // 4. Label resolution across the Harvard split
    // =========================================================================

    [Fact]
    public void LiT_DataLabel_ResolvesToAbsoluteTryteAddress()
    {
        var program = Asm.AssembleProgram(SmallProgram);
        var liMc = program.Instructions[0].MachineCode;
        var imm24 = liMc[0..12] + liMc[18..30];
        imm24.Should().Be("000000000000000000000++0", because: "buf sits at tryte address 12 = ++0 balanced");
    }

    [Fact]
    public void LiT_CodeLabel_ResolvesToAbsoluteInstructionIndex_NotPcRelative()
    {
        var program = Asm.AssembleProgram("""
            nop.t
            nop.t
            target:
            li.t X1, target
            """);
        var liMc = program.Instructions[2].MachineCode;
        var imm24 = liMc[0..12] + liMc[18..30];
        imm24.Should().Be("0000000000000000000000+-",
            because: "target is instruction index 2 (absolute), not 0 (PC-relative)");
    }

    [Fact]
    public void WordDirective_HoldingDataLabel_StoresItsAddress()
    {
        var program = Asm.AssembleProgram("""
            nop.t
            .data
            w: .word 730
            p: .word w
            """);
        // 1 instruction = 6 trytes → base = 8; w = 8 = +0- balanced (9 - 1).
        program.DataBaseAddress.Should().Be(8);
        var pointer = program.Data[1];
        // The pointer word holds w's tryte address 8 (+0- balanced), least significant tryte first.
        pointer.Trytes.Should().Equal("000+0-", "000000", "000000", "000000");
    }

    [Fact]
    public void WordDirective_HoldingCodeLabel_StoresItsInstructionIndex()
    {
        var program = Asm.AssembleProgram("""
            nop.t
            func:
            nop.t
            .data
            table: .word func
            """);
        // func is instruction index 1 — a code address counts instructions, not trytes.
        program.Data[0].Trytes.Should().Equal("00000+", "000000", "000000", "000000");
    }

    [Fact]
    public void JalT_ToDataSymbol_IsRejected()
    {
        var act = () => Asm.AssembleProgram("""
            jal.t X1, buf
            .data
            buf: .word 1
            """);
        act.Should().Throw<InvalidOperationException>().WithMessage("*PC-relative*data symbol*");
    }

    [Fact]
    public void LargeImmediate_FullWordRange_Encodes()
    {
        // (3^24 − 1)/2 = 141,214,768,240 — the balanced word maximum, past int.MaxValue.
        var mc = Asm.Translate("LI.T X1, 141214768240");
        var imm24 = mc[0..12] + mc[18..30];
        imm24.Should().Be(new string('+', 24));
    }

    // =========================================================================
    // 5. Section rules
    // =========================================================================

    [Fact]
    public void WordDirective_InText_IsRejected()
    {
        var act = () => Asm.AssembleProgram(".text\n.word 5");
        act.Should().Throw<InvalidOperationException>().WithMessage("*instructions only*");
    }

    [Fact]
    public void Instruction_InData_IsRejected()
    {
        var act = () => Asm.AssembleProgram(".data\nnop.t");
        act.Should().Throw<InvalidOperationException>().WithMessage("*belong in .text*");
    }

    [Fact]
    public void UnknownDirective_IsRejected_WithSupportedList()
    {
        var act = () => Asm.AssembleProgram(".globl main\nnop.t");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported directive*.globl*");
    }

    [Fact]
    public void ZeroDirective_RequiresPositiveCount()
    {
        var act = () => Asm.AssembleProgram(".data\n.zero -3");
        act.Should().Throw<InvalidOperationException>().WithMessage("*positive tryte count*");
    }

    [Fact]
    public void AssembleInstructions_AcceptsDirectives_ReturnsInstructionStreamOnly()
    {
        var instructions = Asm.AssembleInstructions(SmallProgram);
        instructions.Should().HaveCount(2);
        instructions[1].MachineCode.Should().Be(new string('0', 32));
    }

    [Fact]
    public void TextIsDefaultSection_AndSwitchingBackAppendsInstructions()
    {
        var program = Asm.AssembleProgram("""
            nop.t
            .data
            w: .word 1
            .text
            nop.t
            """);
        program.Instructions.Should().HaveCount(2);
        program.Data.Should().HaveCount(1);
    }
}
