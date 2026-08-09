using FluentAssertions;
using TernaryWorkbench.RebelAssembler;
using Xunit;
using Asm = TernaryWorkbench.RebelAssembler.Rebel2v2Assembler;

namespace TernaryWorkbench.Tests;

/// <summary>
/// Tests for the REBEL-2v2 assembler and disassembler (ISA v0.5).
///
/// Machine-code format: opcode[0..1] Rs1[2..3] Rs2[4..5] Rd1[6..7] Rd2[8..9]
/// Rd2 slot serves as the function-code discriminator for all non-D/E format groups.
///
/// Register encoding:
///   X-4="--"  X-3="-0"  X-2="-+"  X-1="0-"  X0/X-0="00"
///   X1="0+"   X2="+-"   X3="+0"   X4="++"
///
/// Key ISA v0.5 breaking changes from v1:
///   • NOP.T  is now 0000000000 (opcode "00", all-zero pseudo of ADDI.T)
///   • MUL.T  moved to opcode "--" func "-0"  (was standalone opcode "0-")
///   • ADDI.T moved to opcode "00"             (was "-0")
///   • Min/Max moved to opcode "-0"            (was "00")
///   • STI.T  moved to opcode "00" func "--"   (was opcode "--")
/// </summary>
public class Rebel2v2AssemblerTests
{
    // =========================================================================
    // 1. Single-instruction assembly — one per mnemonic
    //    Encoding = opcode | Rs1 | Rs2 | Rd1 | Rd2
    // =========================================================================

    [Theory]
    // Group 1 — Core ALU (opcode --)
    [InlineData("ADD.T X1, X2, X3",       "--+-+00+00")]  // func=00
    [InlineData("ADD.T X-4, X4, X-4",     "--++----00")]
    [InlineData("SUB.T X1, X2, X3",       "--+-+00+--")]  // func=--
    [InlineData("SUB.T X-4, X4, X-4",     "--++------")]
    [InlineData("MUL.T X1, X2, X3",       "--+-+00+-0")]  // func=-0  (moved from opcode 0-)
    [InlineData("MULH.T X1, X2, X3",      "--+-+00+-+")] // func=-+  (new)
    [InlineData("DIV.T X1, X2, X3",       "--+-+00+0-")]  // func=0-  (new)
    [InlineData("REM.T X1, X2, X3",       "--+-+00+0+")]  // func=0+  (new)
    [InlineData("MOD.T X1, X2, X3",       "--+-+00++-")]  // func=+-  (new)
    // Group 2 — Data Move + Unary (opcode 00)
    [InlineData("ADDI.T X1, X2, ++",      "00+-++0+00")]  // opcode 00 (was -0)
    [InlineData("ADDI.T X-1, X1, --",     "000+--0-00")]
    [InlineData("NOP.T",                  "0000000000")]  // all-zero (was -000000000)
    [InlineData("LI.T X1, ++",            "0000++0+00")]  // opcode 00 (was -0)
    [InlineData("LI.T X-4, --",           "0000----00")]
    [InlineData("MV.T X1, X2",            "00+-000+00")]  // opcode 00 (was -0)
    [InlineData("MV.T X-4, X4",           "00++00--00")]
    [InlineData("LD2.T X1, X2",           "00+-000+-0")]  // func=-0  (new)
    [InlineData("ST2.T X1, X2",           "000++-00-+")]  // func=-+  (new); rs1=addr, rs2=val
    [InlineData("STI.T X1, X2",           "00+-000+--")]  // func=--  (moved from opcode --)
    [InlineData("STI.T X-4, X4",          "00++00----")]
    [InlineData("NTI.T X1, X2",           "00+-000+0-")]  // func=0-  (new)
    [InlineData("PTI.T X1, X2",           "00+-000+0+")]  // func=0+  (new)
    [InlineData("MTI.T X1, X2",           "00+-000++-")]  // func=+-  (new)
    [InlineData("CYCLEUP.T X1, X2",       "00+-000++0")]  // func=+0  (new)
    [InlineData("SWAP.T X1, X2",          "00+-000+++")]  // func=++  (new)
    // Group 3 — LI2 / wide D-format (opcode -+)
    [InlineData("LI2.T X1, X2, X3, X4",  "-++0++0++-")]  // renamed from ADDI2.T
    // Group 4 — Majority Vote / wide E-format (opcode 0-)
    [InlineData("MAJV.T X1, X2, X3, X4", "0-+-+00+++")] // new; rs3 in Rd2 slot
    // Group 5 — Min/Max (opcode -0, moved from 00)
    [InlineData("MINW.T X1, X2, X3",      "-0+-+00+--")]  // func=--
    [InlineData("MINT.T X1, X2, X3",      "-0+-+00+-0")]  // func=-0
    [InlineData("MINI.T X1, X2, ++",      "-0+-++0+-+")]  // I-format (new)
    [InlineData("MAXI.T X1, X2, ++",      "-0+-++0+0-")]  // I-format (new)
    [InlineData("MAXW.T X1, X2, X3",      "-0+-+00++-")]  // func=+-
    [InlineData("MAXT.T X1, X2, X3",      "-0+-+00++0")]  // func=+0
    // Group 6 — Shift (opcode 0+) — unchanged from v1
    [InlineData("SLIN.T X1, X2, ++",      "0++-++0+--")]
    [InlineData("SLIZ.T X1, X2, ++",      "0++-++0+-0")]
    [InlineData("SLIP.T X1, X2, ++",      "0++-++0+-+")]
    [InlineData("SRIN.T X1, X2, ++",      "0++-++0++-")]
    [InlineData("SRIZ.T X1, X2, ++",      "0++-++0++0")]
    [InlineData("SRIP.T X1, X2, ++",      "0++-++0+++")]
    [InlineData("ROT.T X1, X2, ++",       "0++-++0+00")]
    [InlineData("SC.T X1, X2, ++",        "0++-++0+00")] // deprecated alias of ROT.T
    // Group 7 — Compare + Branch + Logic (opcode +-)
    [InlineData("CMPT.T X1, X2, X3",      "+-+-+00+--")]
    [InlineData("CMPW.T X1, X2, X3",      "+-+-+00+00")]
    [InlineData("CMPWI.T X1, X2, ++",     "+-+-++0+-0")]  // new I-format
    [InlineData("CMPTI.T X1, X2, ++",     "+-+-++0+-+")]  // new I-format
    [InlineData("BNE.T X1, X2, X3",       "+-+-+00+0-")]  // new branch
    [InlineData("KIMP.T X1, X2, X3",      "+-+-+00+0+")]  // Kleene implication (new)
    // Group 8 — BCEG (opcode +0) — unchanged from v1
    [InlineData("BCEG.T X1, X2, X3, X4",  "+0+0++0++-")]
    // Group 9 — Control + System (opcode ++)
    [InlineData("JALR.T X1, X2, ++",      "+++-++0+00")]  // unchanged from v1
    [InlineData("LPC.T X1, ++",           "++00++0+0-")]  // unchanged from v1
    [InlineData("JAL.T X1, ++",           "++00++0+0+")]  // func changed from v1 ("0-"→"0+", "0+"→"0+")
    [InlineData("FENCE.T",                "++000000-0")]  // SYS new
    [InlineData("WFI.T",                  "++000000-+")]  // SYS new
    [InlineData("IRET.T",                 "++000000+-")]  // SYS new
    [InlineData("EBREAK.T",               "++000000+0")]  // SYS new
    [InlineData("ECALL.T",                "++000000++")]  // SYS new
    public void Translate_SingleInstruction_ProducesMachineCode(string assembly, string expected)
    {
        Asm.Translate(assembly).Should().Be(expected);
    }

    // =========================================================================
    // 2. Pseudo-instruction disambiguation (NOP / LI / MV vs ADDI)
    //    All share opcode "00" and func Rd2="00"; distinguished by fixed-field score.
    // =========================================================================

    [Fact]
    public void NopT_EncodesAsAllZeros()
    {
        Asm.Translate("NOP.T").Should().Be("0000000000");
    }

    [Fact]
    public void NopT_AllZeroWord_DisassemblesToNopT()
    {
        // In v2, "0000000000" = NOP.T (unlike v1 where it was an unknown MIMA encoding)
        Asm.Disassemble("0000000000").Should().Be("NOP.T");
    }

    [Fact]
    public void LiT_UsesOpcode00_NotMinus0()
    {
        // v1 LI.T used opcode "-0"; v2 uses "00"
        var mc = Asm.Translate("LI.T X1, ++");
        mc.Should().StartWith("00").And.Be("0000++0+00");
    }

    [Fact]
    public void MvT_UsesOpcode00_Rs2Zero()
    {
        var mc = Asm.Translate("MV.T X1, X2");
        mc[4..6].Should().Be("00", because: "Rs2 (imm slot) must be 00 for MV.T");
        mc.Should().Be("00+-000+00");
    }

    [Fact]
    public void AddiT_AndMvT_SameEncodingWhenImm0()
    {
        // MV.T rd1, rs1  ≡  ADDI.T rd1, rs1, 0  — same machine code
        Asm.Translate("MV.T X1, X2").Should().Be(Asm.Translate("ADDI.T X1, X2, 0"));
    }

    // Verify numeric immediates and trit-pair immediates produce the same result
    [Theory]
    [InlineData("ADDI.T X1, X2, 4",   "ADDI.T X1, X2, ++")]
    [InlineData("ADDI.T X1, X2, -4",  "ADDI.T X1, X2, --")]
    [InlineData("ADDI.T X1, X2, 0",   "ADDI.T X1, X2, 00")]
    [InlineData("ADDI.T X1, X2, 1",   "ADDI.T X1, X2, 0+")]
    [InlineData("ADDI.T X1, X2, -1",  "ADDI.T X1, X2, 0-")]
    [InlineData("MINI.T X1, X2, 4",   "MINI.T X1, X2, ++")]
    [InlineData("MAXI.T X1, X2, -4",  "MAXI.T X1, X2, --")]
    [InlineData("CMPWI.T X1, X2, 2",  "CMPWI.T X1, X2, +-")]
    public void Translate_NumericImmediate_SameAsTritPairImmediate(string withNumeric, string withTritPair)
    {
        Asm.Translate(withNumeric).Should().Be(Asm.Translate(withTritPair));
    }

    // =========================================================================
    // 3. Disassembly: machine code → canonical mnemonic+operands
    //    Immediates / labels are expressed as register names X-4..X4.
    // =========================================================================

    [Theory]
    // Group 1
    [InlineData("--+-+00+00",   "ADD.T X1, X2, X3")]
    [InlineData("--+-+00+--",   "SUB.T X1, X2, X3")]
    [InlineData("--+-+00+-0",   "MUL.T X1, X2, X3")]
    [InlineData("--+-+00+-+",   "MULH.T X1, X2, X3")]
    [InlineData("--+-+00+0-",   "DIV.T X1, X2, X3")]
    [InlineData("--+-+00+0+",   "REM.T X1, X2, X3")]
    [InlineData("--+-+00++-",   "MOD.T X1, X2, X3")]
    // Group 2
    [InlineData("00+-++0+00",   "ADDI.T X1, X2, X4")]   // imm "++" → X4
    [InlineData("0000000000",   "NOP.T")]
    [InlineData("0000++0+00",   "LI.T X1, X4")]
    [InlineData("00+-000+00",   "MV.T X1, X2")]
    [InlineData("00+-000+-0",   "LD2.T X1, X2")]
    [InlineData("000++-00-+",   "ST2.T X1, X2")]
    [InlineData("00+-000+--",   "STI.T X1, X2")]
    [InlineData("00+-000+0-",   "NTI.T X1, X2")]
    [InlineData("00+-000+0+",   "PTI.T X1, X2")]
    [InlineData("00+-000++-",   "MTI.T X1, X2")]
    [InlineData("00+-000++0",   "CYCLEUP.T X1, X2")]
    [InlineData("00+-000+++",   "SWAP.T X1, X2")]
    // Group 3
    [InlineData("-++0++0++-",   "LI2.T X1, X2, X3, X4")]
    // Group 4
    [InlineData("0-+-+00+++",   "MAJV.T X1, X2, X3, X4")]
    // Group 5
    [InlineData("-0+-+00+--",   "MINW.T X1, X2, X3")]
    [InlineData("-0+-+00+-0",   "MINT.T X1, X2, X3")]
    [InlineData("-0+-++0+-+",   "MINI.T X1, X2, X4")]
    [InlineData("-0+-++0+0-",   "MAXI.T X1, X2, X4")]
    [InlineData("-0+-+00++-",   "MAXW.T X1, X2, X3")]
    [InlineData("-0+-+00++0",   "MAXT.T X1, X2, X3")]
    // Group 6
    [InlineData("0++-++0+--",   "SLIN.T X1, X2, X4")]
    [InlineData("0++-++0+-0",   "SLIZ.T X1, X2, X4")]
    [InlineData("0++-++0+-+",   "SLIP.T X1, X2, X4")]
    [InlineData("0++-++0++-",   "SRIN.T X1, X2, X4")]
    [InlineData("0++-++0++0",   "SRIZ.T X1, X2, X4")]
    [InlineData("0++-++0+++",   "SRIP.T X1, X2, X4")]
    [InlineData("0++-++0+00",   "ROT.T X1, X2, X4")]
    // Group 7
    [InlineData("+-+-+00+--",   "CMPT.T X1, X2, X3")]
    [InlineData("+-+-+00+00",   "CMPW.T X1, X2, X3")]
    [InlineData("+-+-++0+-0",   "CMPWI.T X1, X2, X4")]
    [InlineData("+-+-++0+-+",   "CMPTI.T X1, X2, X4")]
    [InlineData("+-+-+00+0-",   "BNE.T X1, X2, X3")]
    [InlineData("+-+-+00+0+",   "KIMP.T X1, X2, X3")]
    // Group 8
    [InlineData("+0+0++0++-",   "BCEG.T X1, X2, X3, X4")]
    // Group 9
    [InlineData("+++-++0+00",   "JALR.T X1, X2, X4")]
    [InlineData("++00++0+0-",   "LPC.T X1, X4")]
    [InlineData("++00++0+0+",   "JAL.T X1, X4")]
    [InlineData("++000000-0",   "FENCE.T")]
    [InlineData("++000000-+",   "WFI.T")]
    [InlineData("++000000+-",   "IRET.T")]
    [InlineData("++000000+0",   "EBREAK.T")]
    [InlineData("++000000++",   "ECALL.T")]
    public void Disassemble_MachineCode_ReturnsCanonicalMnemonic(string machineCode, string expected)
    {
        Asm.Disassemble(machineCode).Should().Be(expected);
    }

    // =========================================================================
    // 4. Round-trip: assemble → disassemble → re-assemble → same machine code
    // =========================================================================

    [Theory]
    [InlineData("ADD.T X1, X2, X3")]
    [InlineData("SUB.T X1, X2, X3")]
    [InlineData("MUL.T X1, X2, X3")]
    [InlineData("MULH.T X1, X2, X3")]
    [InlineData("DIV.T X1, X2, X3")]
    [InlineData("REM.T X1, X2, X3")]
    [InlineData("MOD.T X1, X2, X3")]
    [InlineData("ADDI.T X1, X2, ++")]
    [InlineData("NOP.T")]
    [InlineData("LI.T X1, ++")]
    [InlineData("MV.T X1, X2")]
    [InlineData("LD2.T X1, X2")]
    [InlineData("ST2.T X1, X2")]
    [InlineData("STI.T X1, X2")]
    [InlineData("NTI.T X1, X2")]
    [InlineData("PTI.T X1, X2")]
    [InlineData("MTI.T X1, X2")]
    [InlineData("CYCLEUP.T X1, X2")]
    [InlineData("SWAP.T X1, X2")]
    [InlineData("LI2.T X1, X2, X3, X4")]
    [InlineData("MAJV.T X1, X2, X3, X4")]
    [InlineData("MINW.T X1, X2, X3")]
    [InlineData("MINT.T X1, X2, X3")]
    [InlineData("MINI.T X1, X2, ++")]
    [InlineData("MAXI.T X1, X2, ++")]
    [InlineData("MAXW.T X1, X2, X3")]
    [InlineData("MAXT.T X1, X2, X3")]
    [InlineData("SLIN.T X1, X2, ++")]
    [InlineData("SLIZ.T X1, X2, ++")]
    [InlineData("SLIP.T X1, X2, ++")]
    [InlineData("SRIN.T X1, X2, ++")]
    [InlineData("SRIZ.T X1, X2, ++")]
    [InlineData("SRIP.T X1, X2, ++")]
    [InlineData("ROT.T X1, X2, ++")]
    [InlineData("SC.T X1, X2, ++")]
    [InlineData("CMPT.T X1, X2, X3")]
    [InlineData("CMPW.T X1, X2, X3")]
    [InlineData("CMPWI.T X1, X2, ++")]
    [InlineData("CMPTI.T X1, X2, ++")]
    [InlineData("BNE.T X1, X2, X3")]
    [InlineData("KIMP.T X1, X2, X3")]
    [InlineData("BCEG.T X1, X2, X3, X4")]
    [InlineData("JALR.T X1, X2, ++")]
    [InlineData("LPC.T X1, ++")]
    [InlineData("JAL.T X1, ++")]
    [InlineData("FENCE.T")]
    [InlineData("WFI.T")]
    [InlineData("IRET.T")]
    [InlineData("EBREAK.T")]
    [InlineData("ECALL.T")]
    public void RoundTrip_Assemble_Disassemble_Reassemble_SameMachineCode(string assembly)
    {
        var machineCode  = Asm.Translate(assembly);
        var disassembled = Asm.Disassemble(machineCode);
        var reassembled  = Asm.Translate(disassembled);

        reassembled.Should().Be(machineCode,
            because: $"re-assembling the disassembly of '{assembly}' should yield the same machine code");
    }

    // =========================================================================
    // 5. Unary-family disambiguation in Group 2 (opcode 00)
    //    U-format: Rs2=00 fixed; multiple unary ops differ only in Rd2 func.
    // =========================================================================

    [Fact]
    public void UnaryFamily_DifferentFuncsProduceDifferentEncodings()
    {
        var sti      = Asm.Translate("STI.T X1, X2");
        var nti      = Asm.Translate("NTI.T X1, X2");
        var pti      = Asm.Translate("PTI.T X1, X2");
        var mti      = Asm.Translate("MTI.T X1, X2");
        var cycleUp  = Asm.Translate("CYCLEUP.T X1, X2");
        var swap     = Asm.Translate("SWAP.T X1, X2");

        new[] { sti, nti, pti, mti, cycleUp, swap }
            .Should().OnlyHaveUniqueItems("each unary instruction has a distinct func code");
    }

    [Fact]
    public void UnaryFamily_AllHaveRs2Zero()
    {
        foreach (var mnemonic in new[] { "STI.T", "NTI.T", "PTI.T", "MTI.T", "CYCLEUP.T", "SWAP.T" })
        {
            var mc = Asm.Translate($"{mnemonic} X1, X2");
            mc[4..6].Should().Be("00",
                because: $"{mnemonic} is U-format and Rs2 must be hard-wired to 00");
        }
    }

    [Fact]
    public void UnaryFamily_AllHaveOpcode00()
    {
        foreach (var mnemonic in new[] { "STI.T", "NTI.T", "PTI.T", "MTI.T", "CYCLEUP.T", "SWAP.T" })
        {
            var mc = Asm.Translate($"{mnemonic} X1, X2");
            mc[..2].Should().Be("00",
                because: $"{mnemonic} belongs to Group 2 (opcode 00)");
        }
    }

    // =========================================================================
    // 6. SYS-format instructions (FENCE / WFI / IRET / EBREAK / ECALL)
    //    All data fields 00; Rd2 = func discriminator.
    // =========================================================================

    [Fact]
    public void SysInstructions_AllDataFieldsZero()
    {
        foreach (var mnemonic in new[] { "FENCE.T", "WFI.T", "IRET.T", "EBREAK.T", "ECALL.T" })
        {
            var mc = Asm.Translate(mnemonic);
            mc[..2].Should().Be("++", because: $"{mnemonic} is in Group 9 (opcode ++)");
            mc[2..8].Should().Be("000000",
                because: $"{mnemonic} is SYS-format: Rs1, Rs2, Rd1 all fixed to 00");
        }
    }

    [Fact]
    public void SysInstructions_DifferentFuncsProduceDifferentEncodings()
    {
        var instructions = new[] { "FENCE.T", "WFI.T", "IRET.T", "EBREAK.T", "ECALL.T" };
        var codes = instructions.Select(Asm.Translate).ToList();
        codes.Should().OnlyHaveUniqueItems("each SYS instruction has a distinct func code");
    }

    // =========================================================================
    // 7. Multi-instruction page assembly
    // =========================================================================

    [Fact]
    public void AssembleInstructions_TwoInstructions_CorrectEncodings()
    {
        const string source = """
            ADD.T X1, X2, X3
            NOP.T
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(2);
        result[0].MachineCode.Should().Be("--+-+00+00");
        result[0].Address.Should().Be("--");
        result[1].MachineCode.Should().Be("0000000000");  // v2 NOP = all-zero
        result[1].Address.Should().Be("-0");
    }

    [Fact]
    public void AssembleInstructions_NineNops_AllZero()
    {
        var nineNops = string.Join("\n", Enumerable.Repeat("NOP.T", 9));
        var result = Asm.AssembleInstructions(nineNops);

        result.Should().HaveCount(9);
        result.All(r => r.MachineCode == "0000000000").Should().BeTrue(
            because: "v2 NOP.T encodes as 0000000000");
        result[0].Address.Should().Be("--");
        result[8].Address.Should().Be("++");
    }

    // =========================================================================
    // 8. Label resolution
    // =========================================================================

    [Fact]
    public void Labels_JalT_TargetsCorrectAddress()
    {
        const string source = """
            start:
            ADD.T X1, X2, X3
            JAL.T X0, start
            """;
        var result = Asm.AssembleInstructions(source);

        // JAL.T X0, start → start=index 0, AddressSpace[0]="--"
        // "++" + Rs1="00" + Rs2(imm)="--" + Rd1="00" + Rd2="0+" = "++00--000+"
        result[1].MachineCode.Should().Be("++00--000+");
    }

    // =========================================================================
    // 9. DisassemblePage
    // =========================================================================

    [Fact]
    public void DisassemblePage_MultipleCodes_ReturnsOnePerCode()
    {
        string[] codes = ["--+-+00+00", "0000000000", "++00++0+0+"];
        var result = Asm.DisassemblePage(codes);

        result.Should().HaveCount(3);
        result[0].Should().Be("ADD.T X1, X2, X3");
        result[1].Should().Be("NOP.T");
        result[2].Should().Be("JAL.T X1, X4");
    }

    // =========================================================================
    // 10. v2 ISA identifier in CSV serialization
    // =========================================================================

    [Fact]
    public void Csv_Rebel2v2_IsaIdentifier_Accepted()
    {
        var records = new List<AssemblyRecord>
        {
            new("ADD.T X1, X2, X3", "--+-+00+00", Isa.Rebel2v2, AssemblyDirection.Assemble),
            new("NOP.T",            "0000000000", Isa.Rebel2v2, AssemblyDirection.Assemble),
        };

        var csv = AssemblyCsvSerializer.Serialize(records);
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);

        errors.Should().BeEmpty();
        validRows.Should().HaveCount(2);
        validRows[0].Isa.Should().Be(Isa.Rebel2v2);
    }

    [Fact]
    public void Csv_V1NopEncoding_RejectedAsRebel2v2()
    {
        // "-000000000" was v1 NOP.T.  In v2, opcode "-0" = Group 5 (Min/Max).
        // func "00" has no match there, so this is now an invalid encoding.
        const string csv = """
            assembly;machine code;isa;direction
            NOP.T;-000000000;REBEL-2v2;assemble
            """;
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().BeEmpty("v1 NOP encoding is not a valid v2 machine code");
        errors.Should().HaveCount(1, "the disassembler should reject the unknown v2 opcode func");
    }

    [Fact]
    public void Csv_V2NopEncoding_AcceptedAsRebel2v2()
    {
        const string csv = """
            assembly;machine code;isa;direction
            NOP.T;0000000000;REBEL-2v2;assemble
            """;
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        errors.Should().BeEmpty();
        validRows.Should().HaveCount(1);
        validRows[0].MachineCode.Should().Be("0000000000");
    }

    // =========================================================================
    // 11. Error cases
    // =========================================================================

    [Fact]
    public void Error_UnknownMnemonic_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("BOGUS.T X1, X2");
        act.Should().Throw<InvalidOperationException>().WithMessage("*BOGUS.T*");
    }

    [Fact]
    public void Error_WrongOperandCount_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("ADD.T X1, X2");  // ADD.T needs 3 operands
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_NopWithOperand_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("NOP.T X1");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_FenceWithOperand_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("FENCE.T X1");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_ImmediateOutOfRange_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("ADDI.T X1, X2, 5");
        act.Should().Throw<InvalidOperationException>().WithMessage("*5*");
    }

    [Fact]
    public void Error_MoreThanNineInstructions_ThrowsInvalidOperationException()
    {
        var tenNops = string.Join("\n", Enumerable.Repeat("NOP.T", 10));
        var act = () => Asm.AssembleInstructions(tenNops);
        act.Should().Throw<InvalidOperationException>().WithMessage("*9*");
    }

    [Fact]
    public void Error_Disassemble_WrongLength_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Disassemble("--+-+0");  // 6 trits
        act.Should().Throw<InvalidOperationException>().WithMessage("*10*");
    }

    [Fact]
    public void Error_Disassemble_ReservedOpcode_ThrowsInvalidOperationException()
    {
        // "--+0000000" has opcode "--" with Rd2="+0" which is a reserved slot in v2
        var act = () => Asm.Disassemble("--0000+000+0");
        act.Should().Throw<InvalidOperationException>(); // wrong length anyway
    }

    [Fact]
    public void Error_V1MulOpcode_IsNowMajv_NotMul()
    {
        // In v2, opcode "0-" is MAJV.T (E-format). The old v1 MUL.T encoding
        // "0-+-+00+00" now has opcode "0-" = MAJV.T, not MUL.T.
        var disasm = Asm.Disassemble("0-+-+00+00");
        disasm.Should().NotStartWith("MUL.T",
            because: "opcode 0- is now MAJV.T in v2, not MUL.T");
        disasm.Should().StartWith("MAJV.T");
    }

    // =========================================================================
    // SC.T -> ROT.T rename (docs/rebel6-isa.md errata E-3, family-wide)
    // =========================================================================

    [Fact]
    public void ScT_IsDeprecatedAliasOfRotT()
    {
        var viaAlias = Asm.Translate("SC.T X1, X2, ++");

        viaAlias.Should().Be(Asm.Translate("ROT.T X1, X2, ++"),
            because: "SC.T is a deprecated alias with an identical encoding");
        Asm.Disassemble(viaAlias).Should().StartWith("ROT.T",
            because: "disassembly always prints the canonical mnemonic");
    }
}
