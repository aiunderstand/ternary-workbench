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
    [InlineData("SHN.T X1, X2, X3")]
    [InlineData("SHZ.T X1, X2, X3")]
    [InlineData("SHP.T X1, X2, X3")]
    [InlineData("ROTR.T X1, X2, X3")]
    [InlineData("SLT.T X1, X2, X3")]
    [InlineData("OR.T X1, X2, X3")]
    [InlineData("XOR.T X1, X2, X3")]
    [InlineData("AND.T X1, X2, X3")]
    // Ternary compare/unary (opcode -000)
    [InlineData("CMP.T X1, X2, X3")]
    [InlineData("STI.T X1, X2")]
    [InlineData("MV2.T X1, X2, X3, X4")]
    [InlineData("MIN.T X1, X2, X3")]
    [InlineData("MAX.T X1, X2, X3")]
    [InlineData("SWAP.T X1, X2")]
    // Ternary System (opcode ++00, zero operands)
    [InlineData("FENCE.T")]
    [InlineData("WFI.T")]
    [InlineData("TRET.T")]
    [InlineData("EBREAK.T")]
    [InlineData("ECALL.T")]
    // Ternary I-type (opcode 0000, imm12 split around rd1)
    [InlineData("ADDI.T X1, X2, 3")]
    [InlineData("ADDI.T X1, X2, -265720")]
    [InlineData("SHIN.T X1, X2, 3")]
    [InlineData("SHIZ.T X1, X2, 3")]
    [InlineData("SHIP.T X1, X2, 3")]
    [InlineData("SHIN.T X1, X2, -3")]
    [InlineData("SHIZ.T X1, X2, -3")]
    [InlineData("SHIP.T X1, X2, -3")]
    [InlineData("ROT.T X1, X2, 3")]
    [InlineData("ROT.T X1, X2, -3")]
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
    // M extension (opcode --+0 R-shape; MAC.T in +-+0 D-shape)
    [InlineData("MUL.T X1, X2, X3")]
    [InlineData("MULH.T X1, X2, X3")]
    [InlineData("DIV.T X1, X2, X3")]
    [InlineData("REM.T X1, X2, X3")]
    [InlineData("MOD.T X1, X2, X3")]
    [InlineData("MAC.T X1, X2, X3, X4")]
    // F extension (arith shares --+0 with M; cmp/cvt in -0+0; FMA in +-+0)
    [InlineData("FADD.T X1, X2, X3")]
    [InlineData("FSUB.T X1, X2, X3")]
    [InlineData("FMUL.T X1, X2, X3")]
    [InlineData("FDIV.T X1, X2, X3")]
    [InlineData("FMA.T X1, X2, X3, X4")]
    [InlineData("FCMP.T X1, X2, X3")]
    [InlineData("FCVT.W.T X1, X2")]
    [InlineData("FCVT.T.W X1, X2")]
    // A extension (opcode -++0; bare/relaxed forms — aq/rl trits zero)
    [InlineData("LR.T X1, X2")]
    [InlineData("SC.T X1, X2, X3")]
    [InlineData("AMOSWAP.T X1, X2, X3")]
    [InlineData("AMOADD.T X1, X2, X3")]
    [InlineData("AMOAND.T X1, X2, X3")]
    [InlineData("AMOOR.T X1, X2, X3")]
    [InlineData("AMOXOR.T X1, X2, X3")]
    [InlineData("AMOMIN.T X1, X2, X3")]
    [InlineData("AMOMAX.T X1, X2, X3")]
    // P extension (TMAC/TDOT in +-+0 D-shape; scalar reductions in -0+0)
    [InlineData("TMAC.T X1, X2, X3, X4")]
    [InlineData("TDOT.T X1, X2, X3")]
    [InlineData("TSUM.T X1, X2")]
    [InlineData("QNT.T X1, X2, X3")]
    [InlineData("HMAX.T X1, X2")]
    // Ztl extension (TLUT in +-+0; TLUTI + canonical unary gates in 00+0)
    [InlineData("TLUT.T X1, X2, X3, X4")]
    [InlineData("TLUTI.T X1, X2, 5")]
    [InlineData("NTI.T X1, X2")]
    [InlineData("PTI.T X1, X2")]
    [InlineData("MTI.T X1, X2")]
    [InlineData("CYU.T X1, X2")]
    [InlineData("CYD.T X1, X2")]
    [InlineData("CYCLEUP.T X1, X2")]
    // Ztb extension (opcode -0+0)
    [InlineData("CLZT.T X1, X2")]
    [InlineData("TCNT.T X1, X2")]
    // Zicsr extension (opcode ++-0; CSR number in the imm12 field)
    [InlineData("CSRRW X5, 768, X6")]
    [InlineData("CSRRS X5, 772, X6")]
    [InlineData("CSRRC X5, 773, X6")]
    [InlineData("CSRRWI X5, 768, 5")]
    [InlineData("CSRRSI X5, 768, 3")]
    [InlineData("CSRRCI X5, 768, 1")]
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
        var ops = new[] { "ADD.T", "SUB.T", "SHZ.T", "ROTR.T", "SLT.T", "OR.T", "XOR.T", "AND.T" };
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
    [InlineData("SHIN.T X1, X2, 5", 6, 12, "00000-")]
    [InlineData("SHIZ.T X1, X2, 5", 6, 12, "000000")]
    [InlineData("SHIP.T X1, X2, 5", 6, 12, "00000+")]
    [InlineData("SHN.T X1, X2, X3", 18, 24, "00000-")]
    [InlineData("SHZ.T X1, X2, X3", 18, 24, "000000")]
    [InlineData("SHP.T X1, X2, X3", 18, 24, "00000+")]
    public void ShiftFillVariants_DifferOnlyInTheFillTrit(string assembly, int from, int to, string expectedFill)
    {
        var mc = Asm.Translate(assembly);
        mc[from..to].Should().Be(expectedFill, because: "one trit selects among the three fills");
        Asm.Disassemble(mc).Should().Be(assembly, because: "the fill trit names the instruction");
    }

    [Fact]
    public void ShiftFillVariants_ShareOneFunc()
    {
        // All three immediate fills carry func 00--; only the rs2 slot differs.
        var codes = new[] { "SHIN.T X1, X2, 5", "SHIZ.T X1, X2, 5", "SHIP.T X1, X2, 5" }
            .Select(Asm.Translate).ToList();

        codes.Select(c => c[24..28]).Distinct().Should().ContainSingle()
            .Which.Should().Be("00--", because: "the fill costs no func slot");
        codes.Should().OnlyHaveUniqueItems(because: "the fill trit still distinguishes the encodings");
    }

    [Fact]
    public void TernaryShiftAmount_IsRangeLimitedToFourTrits()
    {
        var tooLarge = () => Asm.Translate("SHIZ.T X1, X2, 41");
        tooLarge.Should().Throw<InvalidOperationException>().WithMessage("*41*");

        var tooNegative = () => Asm.Translate("SHIZ.T X1, X2, -41");
        tooNegative.Should().Throw<InvalidOperationException>().WithMessage("*-41*");
    }

    [Fact]
    public void SignedShiftAmount_DirectionComesFromTheSign()
    {
        // Errata E-4: one shift family, direction = sign of the balanced amount. The negative
        // amount encodes as the tritwise-negated shamt field; everything else is identical.
        var left  = Asm.Translate("SHIZ.T X1, X2, 5");
        var right = Asm.Translate("SHIZ.T X1, X2, -5");

        left[18..24].Should().Be("000+--",  because: "+5 in 6-trit balanced ternary");
        right[18..24].Should().Be("000-++", because: "-5 is its tritwise negation");
        left[24..28].Should().Be(right[24..28], because: "direction costs no func — it rides the amount's sign");

        Asm.Disassemble(right).Should().Be("SHIZ.T X1, X2, -5",
            because: "negative amounts are first-class and round-trip through disassembly");
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

    // =========================================================================
    // 13. Spec-upgrade instructions: base additions, System group, extensions
    //     (docs/rebel6-isa.md, docs/rebel6-extensions.md, docs/rebel6-platform.md)
    // =========================================================================

    [Theory]
    // Base additions
    [InlineData("MIN.T X1, X2, X3",          "0000+-0000+000000+000000000--000")] // -000 func 000-
    [InlineData("MAX.T X1, X2, X3",          "0000+-0000+000000+000000000+-000")] // -000 func 000+
    [InlineData("MV2.T X1, X2, X3, X4",      "0000+00000++00000+0000+-00-+-000")] // rs1=X3 rs2=X4 rd1=X1 rd2=X2
    [InlineData("ROT.T X1, X2, 3",           "0000+-00000000000+0000+000-+0000")] // cyclic shift, signed shamt, selector zero
    [InlineData("ROT.T X1, X2, -3",          "0000+-00000000000+0000-000-+0000")] // negative amount = tritwise-negated shamt
    // Signed shift family (errata E-4): direction from the amount's sign, one func per form
    [InlineData("SHZ.T X1, X2, X3",          "0000+-0000+000000+00000000-+--00")] // register amount, fill 0 in rd2 slot
    [InlineData("SHN.T X1, X2, X3",          "0000+-0000+000000+00000-00-+--00")] // register amount, fill − in rd2 slot
    [InlineData("SHIZ.T X1, X2, 3",          "0000+-00000000000+0000+000--0000")] // immediate amount, fill 0 in rs2 slot
    [InlineData("SHIZ.T X1, X2, -3",         "0000+-00000000000+0000-000--0000")] // negative = toward LST, same func
    [InlineData("SHIN.T X1, X2, 3",          "0000+-00000-00000+0000+000--0000")] // fill − in rs2 slot
    [InlineData("ROTR.T X1, X2, X3",         "0000+-0000+000000+000000000---00")] // register-amount rotate, freed 0- func, rd2 zero
    // System group: all four operand slots zero, funcs match R2v2's ++ group
    [InlineData("ECALL.T",                   "00000000000000000000000000++++00")]
    [InlineData("TRET.T",                    "00000000000000000000000000+-++00")]
    // M / F share opcode --+0
    [InlineData("MUL.T X1, X2, X3",          "0000+-0000+000000+00000000----+0")]
    [InlineData("FADD.T X1, X2, X3",         "0000+-0000+000000+00000000-+--+0")]
    // A extension (bare/relaxed: rd2 = aq/rl trits zero)
    [InlineData("LR.T X1, X2",               "0000+-00000000000+00000000---++0")]
    [InlineData("SC.T X1, X2, X3",           "0000+-0000+000000+00000000-0-++0")]
    [InlineData("AMOSWAP.T X1, X2, X3",      "0000+-0000+000000+00000000-+-++0")]
    // D-shape extension ops (rd2 slot = rs3 / truth table)
    [InlineData("TMAC.T X1, X2, X3, X4",     "0000+-0000+000000+0000++000-+-+0")]
    [InlineData("TLUT.T X1, X2, X3, X4",     "0000+-0000+000000+0000++00--+-+0")]
    // Ztl canonical unary gate: table pinned in rd2 slot (imm[5:0])
    [InlineData("NTI.T X1, X2",              "0000+-00000000000+000--+00--00+0")]
    // P / Ztb scalar unaries in -0+0
    [InlineData("TSUM.T X1, X2",             "0000+-00000000000+000000000+-0+0")]
    [InlineData("CLZT.T X1, X2",             "0000+-00000000000+000000000--0+0")]
    [InlineData("FCVT.W.T X1, X2",           "0000+-00000000000+00000000-0-0+0")]
    // Zicsr: CSR number 768 (mstatus) rides the imm12 field split around rd1
    [InlineData("CSRRW X5, 768, X6",         "000+-000000+000+--00+++000--++-0")]
    [InlineData("CSRRWI X5, 768, 5",         "000+--00000+000+--00+++0000-++-0")]
    public void SpecUpgrade_Translate_ProducesMachineCode(string assembly, string expected)
    {
        Asm.Translate(assembly).Should().Be(expected,
            because: $"'{assembly}' has a pinned golden encoding");
    }

    [Theory]
    [InlineData("SWAP.T X1, X2",     "MV2.T X1, X2, X2, X1")]
    [InlineData("CYCLEUP.T X1, X2",  "CYU.T X1, X2")]
    [InlineData("TDOT.T X1, X2, X3", "TMAC.T X1, X2, X3, X0")]
    // E-4: the old direction-split shift names are pseudos over the signed-amount family;
    // the immediate right shifts negate the amount, which the balanced shamt encodes for free.
    [InlineData("SLN.T X1, X2, X3",  "SHN.T X1, X2, X3")]
    [InlineData("SLZ.T X1, X2, X3",  "SHZ.T X1, X2, X3")]
    [InlineData("SLP.T X1, X2, X3",  "SHP.T X1, X2, X3")]
    [InlineData("SLIN.T X1, X2, 5",  "SHIN.T X1, X2, 5")]
    [InlineData("SLIZ.T X1, X2, 5",  "SHIZ.T X1, X2, 5")]
    [InlineData("SLIP.T X1, X2, 5",  "SHIP.T X1, X2, 5")]
    [InlineData("SRIN.T X1, X2, 5",  "SHIN.T X1, X2, -5")]
    [InlineData("SRIZ.T X1, X2, 5",  "SHIZ.T X1, X2, -5")]
    [InlineData("SRIP.T X1, X2, 5",  "SHIP.T X1, X2, -5")]
    [InlineData("SRIZ.T X1, X2, -7", "SHIZ.T X1, X2, 7")]
    public void SpecUpgrade_PseudoForms_EncodeAsTheirExpansion(string pseudo, string expansion)
    {
        Asm.Translate(pseudo).Should().Be(Asm.Translate(expansion),
            because: $"'{pseudo}' must encode exactly as '{expansion}'");
    }

    [Fact]
    public void OldShiftNames_DisassembleAsTheSignedCanonicalForms()
    {
        // The pseudo mnemonics have no encoding of their own; the signed family is canonical.
        Asm.Disassemble(Asm.Translate("SLIZ.T X1, X2, 3")).Should().Be("SHIZ.T X1, X2, 3");
        Asm.Disassemble(Asm.Translate("SRIZ.T X1, X2, 3")).Should().Be("SHIZ.T X1, X2, -3");
        Asm.Disassemble(Asm.Translate("SLN.T X1, X2, X3")).Should().Be("SHN.T X1, X2, X3");
    }

    [Fact]
    public void RetiredRegisterRightShifts_AreUnknownMnemonics()
    {
        // E-4 Option A: SR{N,Z,P}.T register forms are retired outright — the assembler cannot
        // negate a runtime amount. Materialize a negative amount (STI.T) and use SH<f>.T.
        foreach (var retired in new[] { "SRN.T", "SRZ.T", "SRP.T" })
        {
            var act = () => Asm.Translate($"{retired} X1, X2, X3");
            act.Should().Throw<InvalidOperationException>().WithMessage($"*{retired}*",
                because: $"'{retired}' is retired by errata E-4 and must not silently assemble");
        }
    }

    [Fact]
    public void RotrT_TakesTheFreedSrFuncSlot_AndPinsRd2Zero()
    {
        var mc = Asm.Translate("ROTR.T X1, X2, X3");
        mc[24..28].Should().Be("000-", because: "ROTR.T reuses the func slot freed by the retired SR* family");
        mc[28..32].Should().Be("--00", because: "ROTR.T is an R-type base-ternary instruction");
        mc[18..24].Should().Be("000000", because: "rotates have no fill — the rd2 selector slot is required zero");

        Asm.Disassemble(mc).Should().Be("ROTR.T X1, X2, X3");
    }

    [Fact]
    public void SwapT_DisassemblesAsMv2()
    {
        // SWAP.T is an operand-rewrite pseudo (like BGT.T): the architectural dual move
        // with crossed operands is the canonical disassembly.
        Asm.Disassemble(Asm.Translate("SWAP.T X1, X2"))
            .Should().Be("MV2.T X1, X2, X2, X1");
    }

    [Fact]
    public void TdotT_IsCanonicalWhenRs3IsZero()
    {
        // TDOT.T pins rd2 (rs3) to X0, so it outscores TMAC.T on such words —
        // the same mechanism by which NOP.T outscores ADDI.T.
        Asm.Disassemble(Asm.Translate("TMAC.T X1, X2, X3, X0"))
            .Should().Be("TDOT.T X1, X2, X3");
        Asm.Disassemble(Asm.Translate("TMAC.T X1, X2, X3, X4"))
            .Should().StartWith("TMAC.T", because: "a non-zero rs3 is a genuine TMAC.T");
    }

    [Fact]
    public void CanonicalGateTables_DisassembleAsTheirGateNames()
    {
        // A TLUTI.T carrying a canonical 3-trit table is printed as the gate name;
        // a non-canonical table stays TLUTI.T.
        Asm.Disassemble(Asm.Translate("TLUTI.T X1, X2, 5")).Should().StartWith("TLUTI.T");

        foreach (var gate in new[] { "NTI.T", "PTI.T", "MTI.T", "CYU.T", "CYD.T" })
            Asm.Disassemble(Asm.Translate($"{gate} X1, X2"))
                .Should().Be($"{gate} X1, X2", because: $"{gate}'s canonical table wins the pattern score");
    }

    [Fact]
    public void CsrNames_ResolveToTheirStandardNumbers()
    {
        Asm.Translate("CSRRW X5, mstatus, X6").Should().Be(Asm.Translate("CSRRW X5, 768, X6"));
        Asm.Translate("CSRRS X5, mepc, X6").Should().Be(Asm.Translate("CSRRS X5, 833, X6"));
        Asm.Translate("CSRRC X5, mhartid, X0").Should().Be(Asm.Translate("CSRRC X5, 3860, X0"));
    }

    [Fact]
    public void SystemInstructions_AllOperandSlotsZero()
    {
        foreach (var mnemonic in new[] { "FENCE.T", "WFI.T", "TRET.T", "EBREAK.T", "ECALL.T" })
        {
            var mc = Asm.Translate(mnemonic);
            mc[..24].Should().Be(new string('0', 24),
                because: $"{mnemonic} takes no operands, so rs1/rs2/rd1/rd2 are all zero");
            mc[28..32].Should().Be("++00", because: "System instructions live in opcode ++00");
        }
    }

    [Fact]
    public void SystemFuncs_MatchRebel2v2ControlGroup()
    {
        // Continuity: same lower-func assignments as REBEL-2 V2.2's ++ group,
        // with TRET.T taking its predecessor IRET.T's slot.
        Asm.Translate("FENCE.T")[24..28].Should().Be("00-0");
        Asm.Translate("WFI.T")[24..28].Should().Be("00-+");
        Asm.Translate("TRET.T")[24..28].Should().Be("00+-");
        Asm.Translate("EBREAK.T")[24..28].Should().Be("00+0");
        Asm.Translate("ECALL.T")[24..28].Should().Be("00++");
    }

    [Fact]
    public void AtomicInstructions_AqRlTritsAreZeroInBareForm()
    {
        foreach (var asm in new[]
        {
            "LR.T X1, X2", "SC.T X1, X2, X3", "AMOSWAP.T X1, X2, X3", "AMOADD.T X1, X2, X3",
            "AMOAND.T X1, X2, X3", "AMOOR.T X1, X2, X3", "AMOXOR.T X1, X2, X3",
            "AMOMIN.T X1, X2, X3", "AMOMAX.T X1, X2, X3",
        })
        {
            var mc = Asm.Translate(asm);
            mc[18..24].Should().Be("000000",
                because: $"'{asm}' assembles the bare (relaxed) form — aq/rl trits in the rd2 slot are zero");
            mc[28..32].Should().Be("-++0", because: "the A extension lives in opcode -++0");
        }
    }

    [Fact]
    public void ScT_IsStoreConditional_NotTheCyclicShift()
    {
        // Errata E-3: SC.T now names the A extension's store-conditional; the
        // cyclic shift is ROT.T. Same mnemonic, different world.
        var storeConditional = Asm.Translate("SC.T X1, X2, X3");
        var rotate           = Asm.Translate("ROT.T X1, X2, 3");

        storeConditional[28..32].Should().Be("-++0", because: "SC.T is an A-extension instruction");
        rotate[28..32].Should().Be("0000", because: "ROT.T keeps the old cyclic shift encoding");
        rotate[24..28].Should().Be("00-+", because: "ROT.T keeps func -+ (unchanged by the rename)");
    }

    // =========================================================================
    // 14. Pattern-table integrity (enumerates InstructionSet6 via InternalsVisibleTo)
    // =========================================================================

    [Fact]
    public void PatternTable_EncodingSignaturesAreUnique()
    {
        // Signature = opcode + func + every pinned non-operand slot value. Two patterns
        // with the same signature would be indistinguishable to the disassembler.
        // (Shift-fill families and pseudo patterns differ in pinned values, so even
        // they have distinct signatures.)
        var patterns = TernaryWorkbench.RebelAssembler.Assembly.InstructionSet6.Patterns;

        var signatures = patterns.Values
            .Select(p =>
            {
                var defaults = p.Defaults is null
                    ? ""
                    : string.Join("|", p.Defaults.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                                 .Select(kv => $"{kv.Key.ToLowerInvariant()}={kv.Value}"));
                return $"{p.Opcode}#{defaults}#{string.Join(",", p.AssemblyOperands)}";
            })
            .ToList();

        signatures.Should().OnlyHaveUniqueItems(
            because: "every pattern must be uniquely decodable from opcode + pinned fields + operand shape");
    }

    [Fact]
    public void PatternTable_OpcodesRespectTheGroupRules()
    {
        var patterns = TernaryWorkbench.RebelAssembler.Assembly.InstructionSet6.Patterns;

        foreach (var p in patterns.Values)
        {
            p.Opcode.Should().MatchRegex("^[-+0]{2}$|^[-+0]{4}$");
            if (p.Opcode.Length == 4)
                p.Opcode[^1].Should().Be('0', because: "4-trit opcodes end in trit 0");
            else
                p.Opcode[^1].Should().NotBe('0', because: "2-trit (G/Y) opcodes end in a non-zero trit");
        }

        patterns.Count.Should().Be(122,
            because: "50 base ternary + 27 binary + 37 extension + 8 pseudo patterns (NOP, MV, TDOT, NTI, PTI, MTI, CYU, CYD)");
    }

    // =========================================================================
    // 15. Page capacity and label/register collision (parser, REBEL-6 rules)
    // =========================================================================

    [Fact]
    public void AssembleInstructions_MoreThanNineInstructions_Succeeds()
    {
        // The parser previously applied REBEL-2's 9-instruction page cap to REBEL-6;
        // a REBEL-6 page holds 729 instructions.
        var source = string.Join('\n', Enumerable.Repeat("NOP.T", 12));

        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(12);
    }

    [Fact]
    public void Label_CollidingWithRebel6RegisterName_IsRejected()
    {
        // X100 is a real REBEL-6 register (it was not one in REBEL-2, whose dictionary
        // the parser used to consult).
        var act = () => Asm.AssembleInstructions("X100: NOP.T");

        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with a register name*");
    }

    // =========================================================================
    // 16. CSV validation parity: REBEL-6 rows are checked like REBEL-2 rows
    // =========================================================================

    [Fact]
    public void Csv_ValidRebel6Row_Accepted()
    {
        var records = new List<AssemblyRecord>
        {
            new("MUL.T X1, X2, X3", Asm.Translate("MUL.T X1, X2, X3"), Isa.Rebel6, AssemblyDirection.Assemble),
        };

        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(AssemblyCsvSerializer.Serialize(records));

        errors.Should().BeEmpty();
        validRows.Should().ContainSingle().Which.Isa.Should().Be(Isa.Rebel6);
    }

    [Fact]
    public void Csv_TenTritCodeMarkedRebel6_Rejected()
    {
        var csv = AssemblyCsvSerializer.Header + "\n" +
                  "ADD.T X1, X2, X3;--+-+00+00;REBEL-6;assemble";

        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);

        validRows.Should().BeEmpty();
        errors.Should().ContainSingle().Which.Message.Should().Contain("32-trit");
    }

}
