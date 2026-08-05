using FluentAssertions;
using TernaryWorkbench.RebelAssembler;
using Xunit;
using Asm = TernaryWorkbench.RebelAssembler.Rebel6Assembler;

namespace TernaryWorkbench.Tests;

/// <summary>
/// Tests for the REBEL-6 assembler and disassembler.
///
/// Machine-code layout (32 trits, left to right):
///   [31:26] rs1 (6T) | [25:20] rs2 (6T) | [19:14] rd1 (6T) |
///   [13:8]  rd2 (6T) | [7:4]   func (4T) | [3:0]   opcode (4T)
///
/// Opcode groups (by last 2 trits): xx00 = Base Ternary; xx-0 = Base Binary; xx+0 = Extensions.
/// Last trit == '0' → 4-trit opcode; last trit ≠ '0' → 2-trit long-immediate (G/Y type).
///
/// G-type (2-trit opcode): imm[23:12](12) | rd1(6) | imm[11:0](12) | opc(2)
/// Y-type (2-trit opcode): rs1(6) | imm[23:0](24) | opc(2)
///
/// NOP.T encodes as all-zero 32 trits (opcode 0000, func 0000 = ADDI.T X0, X0, 0).
///
/// Register encoding (selected):
///   X0 = "000000"  X1 = "00000+"  X2 = "0000+-"
///   X3 = "0000+0"  X4 = "0000++"
/// </summary>
public class Rebel6AssemblerTests
{
    // =========================================================================
    // 1. Single-instruction assembly — explicit machine code verification
    //    Covers one representative per format type.
    // =========================================================================

    [Theory]
    // R-type  opcode=--00  func discriminates the operation
    [InlineData("ADD.T X1, X2, X3",        "0000+-0000+000000+00000000----00")]
    [InlineData("SUB.T X1, X2, X3",        "0000+-0000+000000+00000000-0--00")]
    [InlineData("OR.T X1, X2, X3",         "0000+-0000+000000+000000000+--00")] // func=000+
    // I-type  opcode=0000  imm12 split around rd1: rs2 slot = imm[11:6], rd2 slot = imm[5:0]
    [InlineData("ADDI.T X1, X2, 3",        "0000+-00000000000+0000+000000000")]
    [InlineData("ADDI.T X1, X2, 2187",     "0000+-0000+000000+00000000000000")]  // 2187 = 3 * 3^6, imm[11:6]=3
    // NOP.T = all-zero 32 trits; MV.T (pseudo sharing opcode 0000 and func 0000)
    [InlineData("NOP.T",                   "00000000000000000000000000000000")]
    [InlineData("MV.T X1, X2",            "0000+-00000000000+00000000000000")]
    // B-type three-way branch  opcode=0-00  two 6-trit displacements: off1 in rd1, off2 in rd2
    [InlineData("BCGS.T X1, X2, 3, -3",   "00000+0000+-0000+00000-000--0-00")]  // func=00--
    [InlineData("BCEG.T X1, X2, 3, -3",   "00000+0000+-0000+00000-000-00-00")]  // func=00-0
    // B-type two-way branch  opcode=0-00  one contiguous 12-trit displacement across rd1+rd2
    [InlineData("BEQ.T X1, X2, 0",        "00000+0000+-00000000000000-+0-00")]  // func=00-+
    [InlineData("BEQ.T X1, X2, 400",      "00000+0000+-00000+--0-++00-+0-00")]  // beyond 6-trit reach
    // B-type store  opcode=0+00  indexed: base register + imm12 contiguous across rd1+rd2
    [InlineData("SW.T X1, X2, 3",         "00000+0000+-0000000000+000--0+00")]  // func=00--
    [InlineData("SH.T X1, X2, 3",         "00000+0000+-0000000000+000-00+00")]  // func=00-0
    // I-type indexed load  opcode=-+00  imm12 split around rd1
    [InlineData("LW.T X1, X2, 3",         "0000+-00000000000+0000+000---+00")]
    // D-type (3 sources + dest)  opcode=+-00
    [InlineData("MAJV.T X1, X2, X3, X4",  "0000+-0000+000000+0000++00--+-00")]
    // X-type (dual dest + dual imm)  opcode=+000
    [InlineData("LI2.T X1, X2, X3, X4",   "0000+00000++00000+0000+-00--+000")]
    // G-type (24-trit immediate split around rd1)  opcode=0+
    [InlineData("LI.T X1, 1",             "00000000000000000+00000000000+0+")]
    // G-type full-word load: absolute imm24, no base register  opcode=++
    [InlineData("LWA.T X1, 1",             "00000000000000000+00000000000+++")]
    // Y-type full-word store: rs1 first, absolute imm24  opcode=-+
    [InlineData("SWA.T X1, 1",             "00000+00000000000000000000000+-+")]
    // Binary R-type  opcode=---0
    [InlineData("ADD X1, X2, X3",         "0000+-0000+000000+00000000-----0")]
    // Binary I-type  opcode=00-0  imm12, RV32I parity
    [InlineData("ADDI X1, X2, 3",         "0000+-00000000000+0000+000--00-0")]
    public void Translate_SingleInstruction_ProducesMachineCode(string assembly, string expected)
    {
        Asm.Translate(assembly).Should().Be(expected);
    }

    // =========================================================================
    // 2. Long-immediate (G/Y-type) explicit encoding
    // =========================================================================

    [Fact]
    public void NopT_IsAllZeros()
    {
        // NOP.T = ADDI.T X0, X0, 0: opcode 0000, func 0000, all registers zero
        Asm.Translate("NOP.T").Should().Be(new string('0', 32),
            because: "NOP.T must encode as all-zero 32-trit machine code");
    }

    [Fact]
    public void LiT_X0_Zero_EncodesWithLiTOpcode()
    {
        // G-type: imm24=all-zero, rd1=X0=000000, opcode=0+
        // New G-type layout: imm[23:12](12) | rd1(6) | imm[11:0](12) | opc(2)
        var mc = Asm.Translate("LI.T X0, 0");
        mc.Should().HaveLength(32);
        mc[30..32].Should().Be("0+", because: "LI.T has 2-trit opcode '0+'");
        mc[12..18].Should().Be("000000", because: "rd1=X0 at positions [12..18] in G-type layout");
    }

    [Fact]
    public void JalT_X0_Zero_EncodesCorrectly()
    {
        // G-type, opcode="+-": imm[23:12] | rd1(X0) | imm[11:0] | "+-"
        var mc = Asm.Translate("JAL.T X0, 0");
        mc.Should().HaveLength(32);
        mc[30..32].Should().Be("+-", because: "JAL.T has 2-trit opcode '+-'");
        mc[12..18].Should().Be("000000", because: "rd1=X0 at positions [12..18] in new G-type layout");
    }

    [Fact]
    public void SwaT_X0_Zero_LastCharIsPlus()
    {
        // Y-type, opcode="-+": last char must be '+'
        Asm.Translate("SWA.T X0, 0")[^1].Should().Be('+', because: "SWA.T opcode is '-+', last trit is '+'");
    }

    [Fact]
    public void LiT_NumericImmediate_EncodesCorrectly()
    {
        // imm=1 in 24-trit BT = "00000000000000000000000+"
        // G-type layout: imm[23:12] at mc[0..12], imm[11:0] at mc[18..30]
        var mc = Asm.Translate("LI.T X1, 1");
        (mc[0..12] + mc[18..30]).Should().Be("00000000000000000000000+",
            because: "reconstructed imm24 from split G-type positions must equal 24-trit BT value of 1");
    }

    // =========================================================================
    // 3. Disassembly: machine code → canonical mnemonic+operands
    // =========================================================================

    [Theory]
    // R-type
    [InlineData("0000+-0000+000000+00000000----00",  "ADD.T X1, X2, X3")]
    [InlineData("0000+-0000+000000+00000000-0--00",  "SUB.T X1, X2, X3")]
    // I-type — imm12 is reassembled from the rs2 and rd2 slots and printed as a number
    [InlineData("0000+-00000000000+0000+000000000",  "ADDI.T X1, X2, 3")]
    // NOP and MV (pseudo)
    [InlineData("00000000000000000000000000000000",  "NOP.T")]
    [InlineData("0000+-00000000000+00000000000000",  "MV.T X1, X2")]
    // B-type three-way (two displacements) and two-way (one 12-trit displacement)
    [InlineData("00000+0000+-0000+00000-000--0-00",  "BCGS.T X1, X2, 3, -3")]
    [InlineData("00000+0000+-0000+00000-000-00-00",  "BCEG.T X1, X2, 3, -3")]
    [InlineData("00000+0000+-00000+--0-++00-+0-00",  "BEQ.T X1, X2, 400")]
    // D-type
    [InlineData("0000+-0000+000000+0000++00--+-00",  "MAJV.T X1, X2, X3, X4")]
    // X-type
    [InlineData("0000+00000++00000+0000+-00--+000",  "LI2.T X1, X2, X3, X4")]
    // G-type
    [InlineData("00000000000000000+00000000000+0+",  "LI.T X1, 1")]
    // Y-type
    [InlineData("00000+00000000000000000000000+-+",  "SWA.T X1, 1")]
    // Binary
    [InlineData("0000+-0000+000000+00000000-----0",  "ADD X1, X2, X3")]
    public void Disassemble_MachineCode_ReturnsCanonicalMnemonic(string machineCode, string expected)
    {
        Asm.Disassemble(machineCode).Should().Be(expected);
    }

    // =========================================================================
    // 4. Round-trip: assemble → disassemble → re-assemble → same machine code
    //    One representative per instruction in each format group.
    // =========================================================================

    [Theory]
    // Ternary R-type (opcode --00): func distinguishes operations
    [InlineData("ADD.T X1, X2, X3")]
    [InlineData("SUB.T X1, X2, X3")]
    [InlineData("SLN.T X1, X2, X3")]
    [InlineData("SLZ.T X1, X2, X3")]
    [InlineData("SLP.T X1, X2, X3")]
    [InlineData("SRN.T X1, X2, X3")]
    [InlineData("SRZ.T X1, X2, X3")]
    [InlineData("SRP.T X1, X2, X3")]
    [InlineData("SLT.T X1, X2, X3")]
    [InlineData("OR.T X1, X2, X3")]
    [InlineData("XOR.T X1, X2, X3")]
    [InlineData("AND.T X1, X2, X3")]
    // Ternary misc (opcode -000)
    [InlineData("CMP.T X1, X2, X3")]
    [InlineData("STI.T X1, X2")]
    // Ternary I-type (opcode 0000, imm12 split around rd1)
    [InlineData("ADDI.T X1, X2, 3")]
    [InlineData("ADDI.T X1, X2, -265720")]
    [InlineData("SLIN.T X1, X2, 3")]
    [InlineData("SLIZ.T X1, X2, 3")]
    [InlineData("SLIP.T X1, X2, 3")]
    [InlineData("SRIN.T X1, X2, 3")]
    [InlineData("SRIZ.T X1, X2, 3")]
    [InlineData("SRIP.T X1, X2, 3")]
    [InlineData("SC.T X1, X2, 3")]
    [InlineData("SLTI.T X1, X2, 3")]
    [InlineData("ORI.T X1, X2, 3")]
    [InlineData("XORI.T X1, X2, 3")]
    [InlineData("ANDI.T X1, X2, 3")]
    // Pseudo-instructions (opcode 0000, share func with ADDI.T)
    [InlineData("NOP.T")]
    [InlineData("MV.T X1, X2")]
    // Ternary I-type indexed load (opcode -+00, base register + imm12)
    [InlineData("LW.T X1, X2, 3")]
    [InlineData("LH.T X1, X2, 3")]
    [InlineData("LT.T X1, X2, 3")]
    [InlineData("JALR.T X1, X2, 3")]
    // Ternary three-way branch (opcode 0-00; two 6-trit displacements in rd1 and rd2 slots)
    [InlineData("BCGS.T X1, X2, 3, -3")]
    [InlineData("BCEG.T X1, X2, 3, -3")]
    // Ternary two-way branch (opcode 0-00; one 12-trit displacement across rd1+rd2)
    [InlineData("BEQ.T X1, X2, 4")]
    [InlineData("BNE.T X1, X2, 5")]
    [InlineData("BLT.T X1, X2, 6")]
    [InlineData("BGE.T X1, X2, 8")]
    [InlineData("BEQ.T X1, X2, -265720")]
    [InlineData("BNE.T X1, X2, 265720")]
    // Two-way pseudo-instructions (operand swap)
    [InlineData("BGT.T X1, X2, 7")]
    [InlineData("BLE.T X1, X2, 9")]
    // Ternary B-type indexed store (opcode 0+00, base register + imm12)
    [InlineData("SW.T X1, X2, -3")]
    [InlineData("SH.T X1, X2, 0")]
    [InlineData("ST.T X1, X2, 3")]
    [InlineData("ST.T X1, X2, 265720")]
    // D-type (opcode +-00): 4 operands
    [InlineData("MAJV.T X1, X2, X3, X4")]
    [InlineData("MINV.T X1, X2, X3, X4")]
    // X-type (opcode +000): dual dest + dual source
    [InlineData("LI2.T X1, X2, X3, X4")]
    // G-type (2-trit opcode): 24-trit immediate, split around rd1
    [InlineData("LI.T X1, 1")]
    [InlineData("LWA.T X1, 1")]
    [InlineData("JAL.T X1, 1")]
    [InlineData("AIPC.T X1, 1")]
    // Y-type (2-trit opcode): rs1 + 24-trit immediate
    [InlineData("SWA.T X1, 1")]
    // Binary R-type (opcode ---0)
    [InlineData("ADD X1, X2, X3")]
    [InlineData("SUB X1, X2, X3")]
    [InlineData("SLL X1, X2, X3")]
    [InlineData("SRL X1, X2, X3")]
    [InlineData("SRA X1, X2, X3")]
    [InlineData("SLTU X1, X2, X3")]
    [InlineData("OR X1, X2, X3")]
    [InlineData("XOR X1, X2, X3")]
    [InlineData("AND X1, X2, X3")]
    // Binary I-type (opcode 00-0, imm12 — RV32I parity)
    [InlineData("ADDI X1, X2, 3")]
    [InlineData("SLLI X1, X2, 3")]
    [InlineData("SRLI X1, X2, 3")]
    [InlineData("SRAI X1, X2, 3")]
    [InlineData("SLTIU X1, X2, 3")]
    [InlineData("ORI X1, X2, 3")]
    [InlineData("XORI X1, X2, 3")]
    [InlineData("ANDI X1, X2, 3")]
    // Binary load (opcode -+-0, imm12)
    [InlineData("LW X1, X2, 3")]
    [InlineData("LH X1, X2, 3")]
    [InlineData("LB X1, X2, 3")]
    [InlineData("LHU X1, X2, 3")]
    [InlineData("LBU X1, X2, 3")]
    // Binary branch (opcode 0--0, imm12 — unsigned only)
    [InlineData("BLTU X1, X2, 0")]
    [InlineData("BGEU X1, X2, 2000")]
    // Binary store (opcode 0+-0, imm12)
    [InlineData("SW X1, X2, 0")]
    [InlineData("SH X1, X2, 3")]
    [InlineData("SB X1, X2, -2000")]
    public void RoundTrip_Assemble_Disassemble_Reassemble_SameMachineCode(string assembly)
    {
        var machineCode  = Asm.Translate(assembly);
        var disassembled = Asm.Disassemble(machineCode);
        var reassembled  = Asm.Translate(disassembled);

        reassembled.Should().Be(machineCode,
            because: $"re-assembling the disassembly of '{assembly}' should yield the same 32-trit code");
    }

    // =========================================================================
    // 5. Pseudo-instruction disambiguation
    // =========================================================================

    [Fact]
    public void MvT_SameAsAddiTWithZeroImmediate()
    {
        // MV.T rd1, rs1 ≡ ADDI.T rd1, rs1, 0 (both use opcode 0000, func 0000, imm=0)
        Asm.Translate("MV.T X1, X2").Should().Be(Asm.Translate("ADDI.T X1, X2, 0"));
    }

    [Theory]
    // Swapping the sources exchanges the greater and smaller outcomes, so BGT.T and BLE.T need
    // no encoding of their own — as in RISC-V.
    [InlineData("BGT.T X1, X2, 5", "BLT.T X2, X1, 5")]
    [InlineData("BLE.T X1, X2, 5", "BGE.T X2, X1, 5")]
    public void OrderingPseudoBranches_AreOperandSwapsOfTheirArchitecturalForm(string pseudo, string architectural)
    {
        var mc = Asm.Translate(pseudo);

        mc.Should().Be(Asm.Translate(architectural),
            because: $"'{pseudo}' must encode exactly as '{architectural}'");
        Asm.Disassemble(mc).Should().Be(architectural,
            because: "pseudo-instructions have no encoding of their own, so the swapped form is canonical");
    }

    [Fact]
    public void TwoWayBranch_DisplacementSpansBothSlots()
    {
        // BEQ.T reads rd1+rd2 as one contiguous 12-trit displacement, so it reaches far past the
        // ±364 limit of a single 6-trit slot (errata E-2).
        var mc = Asm.Translate("BEQ.T X1, X2, 265720");
        mc[12..24].Should().Be("++++++++++++", because: "+265720 is the largest 12-trit displacement");
        mc[24..28].Should().Be("00-+", because: "BEQ.T uses func 00-+ in branch group 0-00");

        Asm.Disassemble(mc).Should().Be("BEQ.T X1, X2, 265720");
    }

    [Fact]
    public void TwoWayBranch_ReachesFurtherThanRv32i()
    {
        // RV32I's B-type reaches ±1024 instructions; a 6-trit slot reaches ±364.
        var act = () => Asm.Translate("BLT.T X1, X2, 2000");
        act.Should().NotThrow(because: "the 12-trit displacement covers ±265720 instructions");
    }

    [Fact]
    public void ThreeWayBranches_DisplacementsOccupyRd1AndRd2Slots()
    {
        var bcgs = Asm.Translate("BCGS.T X1, X2, 1, -1");
        bcgs[12..18].Should().Be("00000+", because: "off1 (greater target) sits in the rd1 slot");
        bcgs[18..24].Should().Be("00000-", because: "off2 (smaller target) sits in the rd2 slot");
        bcgs[24..28].Should().Be("00--",   because: "BCGS.T uses func 00-- in branch group 0-00");
        bcgs[28..32].Should().Be("0-00",   because: "three-way branches share the ternary branch opcode group");

        var bceg = Asm.Translate("BCEG.T X1, X2, 1, -1");
        bceg[12..18].Should().Be("00000+", because: "off1 (equal target) sits in the rd1 slot");
        bceg[18..24].Should().Be("00000-", because: "off2 (greater target) sits in the rd2 slot");
        bceg[24..28].Should().Be("00-0",   because: "BCEG.T uses func 00-0 in branch group 0-00");
    }

    [Fact]
    public void ThreeWayBranch_Labels_BothTargetsResolvePcRelative()
    {
        const string source = """
            back:
            NOP.T
            BCGS.T X1, X2, back, fwd
            NOP.T
            fwd:
            NOP.T
            """;
        var result = Asm.AssembleInstructions(source);

        // BCGS.T is at index 1; back is index 0 (offset -1); fwd is index 3 (offset +2)
        var mc = result[1].MachineCode;
        mc[12..18].Should().Be("00000-", because: "off1 = 0 - 1 = -1");
        mc[18..24].Should().Be("0000+-", because: "off2 = 3 - 1 = 2");
    }

    [Fact]
    public void IndexedAndAbsoluteWordAccess_AreDistinctInstructions()
    {
        // Indexed (LW.T/SW.T): base register + imm12, in the 4-trit-opcode load/store groups.
        // Absolute (LWA.T/SWA.T): a 24-trit address and no base register, in the G/Y long-immediate
        // groups. They occupy different opcode groups and never collide (errata E-2).
        Asm.Translate("LW.T X1, X2, 3")[^4..].Should().Be("-+00", because: "LW.T is I-type, opcode -+00");
        Asm.Translate("SW.T X1, X2, 3")[^4..].Should().Be("0+00", because: "SW.T is B-type, opcode 0+00");
        Asm.Translate("LWA.T X1, 1")[^2..].Should().Be("++",   because: "LWA.T is G-type, opcode ++");
        Asm.Translate("SWA.T X1, 1")[^2..].Should().Be("-+",   because: "SWA.T is Y-type, opcode -+");

        // The absolute forms reach the whole 24-trit address space in one instruction, which the
        // indexed forms cannot: imm12 tops out at ±265720.
        var farAbsolute = () => Asm.Translate("LWA.T X1, 5000000");
        farAbsolute.Should().NotThrow();
        var farIndexed = () => Asm.Translate("LW.T X1, X2, 5000000");
        farIndexed.Should().Throw<InvalidOperationException>();
    }

    // =========================================================================
    // 6. Instruction format structure verification
    // =========================================================================

    [Fact]
    public void StandardInstructions_LastTrit_IsZero()
    {
        // All 4-trit opcode instructions must have '0' as last trit (mc[31])
        var standardMnemonics = new[] { "ADD.T X1, X2, X3", "ADDI.T X1, X2, 3", "BEQ.T X1, X2, 0", "ADD X1, X2, X3" };
        foreach (var mnemonic in standardMnemonics)
        {
            var mc = Asm.Translate(mnemonic);
            mc.Should().HaveLength(32, because: "REBEL-6 instructions are 32 trits");
            mc[31].Should().Be('0', because: $"'{mnemonic}' is a standard (4-trit opcode) instruction; last trit must be '0'");
        }
    }

    [Fact]
    public void LongImmediateInstructions_LastTrit_IsNotZero()
    {
        // G/Y-type instructions: 2-trit opcode; last trit ≠ '0'
        var longImmMnemonics = new[] { "LI.T X1, 1", "LWA.T X1, 1", "SWA.T X1, 1", "JAL.T X1, 1" };
        foreach (var mnemonic in longImmMnemonics)
        {
            var mc = Asm.Translate(mnemonic);
            mc[^1].Should().NotBe('0', because: $"'{mnemonic}' is a long-immediate instruction; last trit must not be '0'");
        }
    }

    [Fact]
    public void RType_AllDifferentFuncs_ProduceDifferentEncodings()
    {
        var ops = new[] { "ADD.T", "SUB.T", "SLZ.T", "SRZ.T", "SLT.T", "OR.T", "XOR.T", "AND.T" };
        var codes = ops.Select(op => Asm.Translate($"{op} X1, X2, X3")).ToList();
        codes.Should().OnlyHaveUniqueItems("R-type ternary ops share opcode but have distinct func values");
    }

    [Fact]
    public void Stores_UseContiguous12TritImmediate()
    {
        // SH.T X1, X2, 2000: B-type stores carry imm12 across rd1+rd2, matching RV32I S-type
        var mc = Asm.Translate("SH.T X1, X2, 2000");
        mc[12..24].Should().Be("0000+0-+-0+-", because: "2000 as a 12-trit balanced-ternary value");
        Asm.Disassemble(mc).Should().Be("SH.T X1, X2, 2000");
    }

    [Theory]
    // The three fills share one func and differ only in the fill selector: the rs2 slot for
    // immediate shifts, the rd2 slot for register shifts. Func alone does not identify a shift.
    [InlineData("SLIN.T X1, X2, 5", 6, 12, "00000-")]
    [InlineData("SLIZ.T X1, X2, 5", 6, 12, "000000")]
    [InlineData("SLIP.T X1, X2, 5", 6, 12, "00000+")]
    [InlineData("SLN.T X1, X2, X3", 18, 24, "00000-")]
    [InlineData("SLZ.T X1, X2, X3", 18, 24, "000000")]
    [InlineData("SLP.T X1, X2, X3", 18, 24, "00000+")]
    public void ShiftFillVariants_DifferOnlyInTheFillTrit(string assembly, int from, int to, string expectedFill)
    {
        var mc = Asm.Translate(assembly);
        mc[from..to].Should().Be(expectedFill, because: "one trit selects among the three fills");
        Asm.Disassemble(mc).Should().Be(assembly, because: "the fill trit names the instruction");
    }

    [Fact]
    public void ShiftFillVariants_ShareOneFunc()
    {
        // All three left-immediate fills carry func 00--; only the rs2 slot differs.
        var codes = new[] { "SLIN.T X1, X2, 5", "SLIZ.T X1, X2, 5", "SLIP.T X1, X2, 5" }
            .Select(Asm.Translate).ToList();

        codes.Select(c => c[24..28]).Distinct().Should().ContainSingle()
            .Which.Should().Be("00--", because: "the fill costs no func slot");
        codes.Should().OnlyHaveUniqueItems(because: "the fill trit still distinguishes the encodings");
    }

    [Fact]
    public void TernaryShiftAmount_IsRangeLimitedToFourTrits()
    {
        var tooLarge = () => Asm.Translate("SLIZ.T X1, X2, 41");
        tooLarge.Should().Throw<InvalidOperationException>().WithMessage("*41*");
    }

    [Fact]
    public void BinaryShiftImmediates_AreRangeLimitedToShamt()
    {
        // RV32I's shamt is a 5-bit field (0..31), carried in 4 trits (±40). It occupies the same
        // slot pair as imm12 but is range-checked far more tightly.
        Asm.Translate("SLLI X1, X2, 31").Should().NotBeNull(because: "31 is the largest RV32I shift amount");

        var tooLarge = () => Asm.Translate("SLLI X1, X2, 41");
        tooLarge.Should().Throw<InvalidOperationException>().WithMessage("*41*");

        // A general immediate on the same opcode group is not restricted
        var wideImm = () => Asm.Translate("ADDI X1, X2, 2000");
        wideImm.Should().NotThrow(because: "ADDI takes imm12, not shamt");
    }

    [Fact]
    public void BinaryInstructions_HaveRv32iImmediateRange()
    {
        // Every RV32I-parity instruction takes imm12; RV32I's own I/S/B immediates are 12 bits.
        var cases = new[] { "ADDI X1, X2, 2000", "LW X1, X2, 2000", "SW X1, X2, 2000", "BLTU X1, X2, 2000" };
        foreach (var asmText in cases)
        {
            var act = () => Asm.Translate(asmText);
            act.Should().NotThrow(because: $"'{asmText}' must accept the full RV32I immediate range");
        }
    }

    [Fact]
    public void ITypeInstructions_ImmSplitAroundDestinationRegister()
    {
        // I-type needs rd1 for the destination, so its imm12 straddles it:
        // rs2 slot holds imm[11:6], rd2 slot holds imm[5:0].
        var mc = Asm.Translate("ADDI.T X1, X2, 1");
        mc[6..12].Should().Be("000000",  because: "imm[11:6] of 1 is zero");
        mc[12..18].Should().Be("00000+", because: "rd1=X1 sits between the immediate halves");
        mc[18..24].Should().Be("00000+", because: "imm[5:0] of 1 is '00000+'");

        // 3^6 = 729 has imm[5:0] = 0 and imm[11:6] = 1, exercising the upper half
        var upper = Asm.Translate("ADDI.T X1, X2, 729");
        upper[6..12].Should().Be("00000+", because: "imm[11:6] of 729 is 1");
        upper[18..24].Should().Be("000000", because: "imm[5:0] of 729 is zero");
    }

    [Fact]
    public void DType_AllFourOperandsEncoded()
    {
        // MAJV.T X1, X2, X3, X4: rd1=X1, rs1=X2, rs2=X3, rd2=X4
        var mc = Asm.Translate("MAJV.T X1, X2, X3, X4");
        mc[0..6].Should().Be("0000+-",   because: "rs1=X2");
        mc[6..12].Should().Be("0000+0",  because: "rs2=X3");
        mc[12..18].Should().Be("00000+", because: "rd1=X1");
        mc[18..24].Should().Be("0000++", because: "rd2=X4");
    }

    // =========================================================================
    // 7. Register encoding coverage
    // =========================================================================

    [Fact]
    public void LargeRegisterNumbers_EncodeCorrectly()
    {
        // X364 is the largest positive register; X-364 the most negative
        var mc = Asm.Translate("ADD.T X364, X-364, X0");
        mc[0..6].Should().Be("------",  because: "X-364 encodes as all-minus in 6-trit BT");
        mc[6..12].Should().Be("000000", because: "X0 encodes as all-zero");
        mc[12..18].Should().Be("++++++", because: "X364 encodes as all-plus in 6-trit BT");
    }

    [Fact]
    public void NegativeRegisterNames_SameEncodingAsPositive()
    {
        // X-1 and X1 are distinct registers; should produce different codes
        var withX1   = Asm.Translate("MV.T X0, X1");
        var withXm1  = Asm.Translate("MV.T X0, X-1");
        withX1.Should().NotBe(withXm1, because: "X1 and X-1 have different 6-trit encodings");
    }

    // =========================================================================
    // 8. Multi-instruction page assembly and address space
    // =========================================================================

    [Fact]
    public void AssembleInstructions_TwoInstructions_CorrectEncodingsAndAddresses()
    {
        const string source = """
            ADD.T X1, X2, X3
            NOP.T
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(2);
        result[0].MachineCode.Should().Be("0000+-0000+000000+00000000----00");
        result[0].Address.Should().Be("------", because: "REBEL-6 address space starts at -364 = '------'");
        result[1].MachineCode.Should().Be("00000000000000000000000000000000");
        result[1].Address.Should().Be("-----0", because: "second address is -363 = '-----0'");
    }

    [Fact]
    public void AddressSpace_StartsAtNegative364_EndsAtPositive364()
    {
        Rebel6Assembler.AddressSpace.Should().HaveCount(729);
        Rebel6Assembler.AddressSpace[0].Should().Be("------");
        Rebel6Assembler.AddressSpace[364].Should().Be("000000");
        Rebel6Assembler.AddressSpace[728].Should().Be("++++++");
    }

    [Fact]
    public void AssembleInstructions_DoesNotPadPage()
    {
        var result = Asm.AssembleInstructions("ADD.T X1, X2, X3");
        result.Should().HaveCount(1, because: "page is not padded when padPage=false");
    }

    // =========================================================================
    // 9. Label resolution (PC-relative branch offsets)
    // =========================================================================

    [Fact]
    public void Labels_BranchOffset_IsRelativeToBranchInstruction()
    {
        const string source = """
            start:
            NOP.T
            BEQ.T X0, X0, start
            """;
        var result = Asm.AssembleInstructions(source);

        // BEQ is instruction index 1; start is index 0; offset = 0-1 = -1.
        // The displacement spans rd1+rd2 as one contiguous 12-trit field.
        var beqMc = result[1].MachineCode;
        beqMc[12..24].Should().Be("00000000000-", because: "PC-relative offset -1 in 12 trits");
    }

    [Fact]
    public void Labels_ForwardBranch_EncodesPositiveOffset()
    {
        const string source = """
            BEQ.T X0, X0, target
            NOP.T
            target:
            NOP.T
            """;
        var result = Asm.AssembleInstructions(source);

        // BEQ is at index 0; target is at index 2; offset = 2-0 = 2
        var beqMc = result[0].MachineCode;
        beqMc[12..24].Should().Be("0000000000+-", because: "PC-relative offset 2 in 12 trits");
    }

    [Fact]
    public void Labels_JalT_UsesLongImmediateOffset()
    {
        const string source = """
            start:
            NOP.T
            JAL.T X0, start
            """;
        var result = Asm.AssembleInstructions(source);

        // JAL.T is at index 1; start is at index 0; offset = 0-1 = -1
        // G-type imm24 = -1 → "00000000000000000000000-"
        // New G-type layout: imm[23:12] at mc[0..12], imm[11:0] at mc[18..30]
        var jalMc = result[1].MachineCode;
        (jalMc[0..12] + jalMc[18..30]).Should().Be("00000000000000000000000-",
            because: "G-type offset -1 reconstructed from split imm positions");
        jalMc[30..32].Should().Be("+-", because: "JAL.T 2-trit opcode is '+-'");
    }

    // =========================================================================
    // 10. DisassemblePage
    // =========================================================================

    [Fact]
    public void DisassemblePage_MultipleCodes_ReturnsOnePerCode()
    {
        string[] codes =
        [
            "0000+-0000+000000+00000000----00",  // ADD.T X1, X2, X3
            "00000000000000000000000000000000",  // NOP.T
            "00000000000000000+00000000000+0+",  // LI.T X1, 1
        ];
        var result = Asm.DisassemblePage(codes);

        result.Should().HaveCount(3);
        result[0].Should().Be("ADD.T X1, X2, X3");
        result[1].Should().Be("NOP.T");
        result[2].Should().Be("LI.T X1, 1");
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
        var act = () => Asm.Translate("ADD.T X1, X2");  // ADD.T requires 3 operands
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_NopWithOperand_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("NOP.T X1");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_ThreeWayBranchOffsetOutOfRange_ThrowsInvalidOperationException()
    {
        // Each three-way displacement is a single 6-trit slot: [-364, +364]
        var act = () => Asm.Translate("BCGS.T X1, X2, 400, 0");
        act.Should().Throw<InvalidOperationException>().WithMessage("*400*");
    }

    [Fact]
    public void Error_TwoWayBranchDisplacementOutOfRange_ThrowsInvalidOperationException()
    {
        // The 12-trit displacement covers [-265720, +265720]
        var act = () => Asm.Translate("BEQ.T X1, X2, 265721");
        act.Should().Throw<InvalidOperationException>().WithMessage("*265721*");
    }

    [Fact]
    public void Error_PseudoBranch_WrongOperandCount_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("BGT.T X1, X2, 0, 0");  // BGT.T takes 3 operands
        act.Should().Throw<InvalidOperationException>().WithMessage("*BGT.T*");
    }

    [Fact]
    public void Error_Disassemble_WrongLength_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Disassemble("00000000000000000000");  // 20 trits, not 32
        act.Should().Throw<InvalidOperationException>().WithMessage("*32*");
    }

    [Fact]
    public void Error_Disassemble_InvalidCharacters_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Disassemble(new string('x', 32));
        act.Should().Throw<InvalidOperationException>();
    }

    // =========================================================================
    // 12. Binary vs ternary instruction disambiguation
    // =========================================================================

    [Fact]
    public void TernaryAndBinaryAdd_ProduceDifferentOpcodes()
    {
        var ternaryAdd = Asm.Translate("ADD.T X1, X2, X3");
        var binaryAdd  = Asm.Translate("ADD X1, X2, X3");

        ternaryAdd[28..32].Should().Be("--00", because: "ADD.T uses ternary-base opcode --00");
        binaryAdd[28..32].Should().Be("---0",  because: "ADD uses binary-base opcode ---0");
        ternaryAdd.Should().NotBe(binaryAdd);
    }

}
