using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet;

namespace TernaryWorkbench.RebelAssembler.Assembly;

/// <summary>
/// V2.2 instruction patterns for the REBEL-2v2 assembler.
/// <para>
/// Encoding layout (2 trits each = 10 total):
/// <c>opcode[0..1] | Rs1[2..3] | Rs2[4..5] | Rd1[6..7] | Rd2[8..9]</c>
/// </para>
/// <para>
/// The <c>Rd2</c> slot doubles as a function discriminator for all instruction
/// groups except D-format (<see cref="LI2.T"/>, <see cref="BCEG.T"/>) and
/// E-format (<see cref="MAJV.T"/>) where it carries an explicit assembly operand.
/// </para>
/// <para>
/// Pseudo-instructions (<c>NOP.T</c>, <c>LI.T</c>, <c>MV.T</c>) reuse the
/// <c>ADDI.T</c> encoding (<c>opcode=00</c>, <c>Rd2="00"</c>); the disassembler
/// selects among them using the fixed-field scoring algorithm.
/// </para>
/// </summary>
internal static class InstructionSet2v2
{
    public const string DefaultPaddingInstruction = "NOP.T";

    /// <summary>
    /// Complete V2.2 pattern table: 46 hardware encodings + 3 pseudo-instructions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, InstructionPattern> Patterns =
        new Dictionary<string, InstructionPattern>(StringComparer.OrdinalIgnoreCase)
        {
            // ---------------------------------------------------------------
            // Group 1 — Core ALU (opcode --)  R format
            //   Field layout: Rd2 = func discriminator
            // ---------------------------------------------------------------
            { "ADD.T",    new InstructionPattern("ADD.T",    "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "00" } }) },
            { "SUB.T",    new InstructionPattern("SUB.T",    "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "--" } }) },
            { "MUL.T",    new InstructionPattern("MUL.T",    "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "-0" } }) },
            { "MULH.T",   new InstructionPattern("MULH.T",   "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "-+" } }) },
            { "DIV.T",    new InstructionPattern("DIV.T",    "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "0-" } }) },
            { "REM.T",    new InstructionPattern("REM.T",    "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "0+" } }) },
            { "MOD.T",    new InstructionPattern("MOD.T",    "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "+-" } }) },

            // ---------------------------------------------------------------
            // Group 2 — Data Move, Load/Store, Unary (opcode 00)
            //   ADDI.T (I format) + 3 pseudos + 2 load/store + 6 unary (U format)
            //   Discriminator: Rd2 = func; unary additionally fixes Rs2 = "00"
            // ---------------------------------------------------------------

            // I format: rd1, rs1, imm  (Rd2 = func = "00")
            { "ADDI.T",   new InstructionPattern("ADDI.T",   "00", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "00" } }) },

            // Pseudos — all reuse opcode "00" / Rd2 "00"; scored above ADDI.T by fixed-field count
            { "NOP.T",    new InstructionPattern("NOP.T",    "00", []) },
            { "LI.T",     new InstructionPattern("LI.T",     "00", [Rd1, Imm],      new Dictionary<string, string> { { Rs1, "00" }, { Rd2, "00" } }) },
            { "MV.T",     new InstructionPattern("MV.T",     "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "00" } }) },

            // R format: rd1, rsAddr  (load trit-pair from rsAddr; Rd2 = "-0")
            { "LD2.T",    new InstructionPattern("LD2.T",    "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rd2, "-0" } }) },

            // R format: rdAddr, rsVal  (store; both are sources; Rd2 = "-+")
            { "ST2.T",    new InstructionPattern("ST2.T",    "00", [Rs1, Rs2],      new Dictionary<string, string> { { Rd1, "00" }, { Rd2, "-+" } }) },

            // U format: rd1, rs1  (Rs2 = "00" hard-wired; Rd2 = func)
            { "STI.T",    new InstructionPattern("STI.T",    "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "--" } }) },
            { "NTI.T",    new InstructionPattern("NTI.T",    "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "0-" } }) },
            { "PTI.T",    new InstructionPattern("PTI.T",    "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "0+" } }) },
            { "MTI.T",    new InstructionPattern("MTI.T",    "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "+-" } }) },
            { "CYCLEUP.T",new InstructionPattern("CYCLEUP.T","00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "+0" } }) },
            { "SWAP.T",   new InstructionPattern("SWAP.T",   "00", [Rd1, Rs1],      new Dictionary<string, string> { { Rs2, "00" }, { Rd2, "++" } }) },

            // ---------------------------------------------------------------
            // Group 3 — Load Immediate Pair (opcode -+)  D format
            //   4 explicit registers; no func discriminator
            // ---------------------------------------------------------------
            { "LI2.T",    new InstructionPattern("LI2.T",    "-+", [Rd1, Rd2, Rs1, Rs2]) },

            // ---------------------------------------------------------------
            // Group 4 — Majority Vote (opcode 0-)  E format
            //   3 sources + 1 dest; rs3 encoded in Rd2 slot
            // ---------------------------------------------------------------
            { "MAJV.T",   new InstructionPattern("MAJV.T",   "0-", [Rd1, Rs1, Rs2, Rd2]) },

            // ---------------------------------------------------------------
            // Group 5 — Min/Max (opcode -0)  R/I format
            // ---------------------------------------------------------------
            { "MINW.T",   new InstructionPattern("MINW.T",   "-0", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "--" } }) },
            { "MINT.T",   new InstructionPattern("MINT.T",   "-0", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "-0" } }) },
            { "MINI.T",   new InstructionPattern("MINI.T",   "-0", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-+" } }) },
            { "MAXI.T",   new InstructionPattern("MAXI.T",   "-0", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "0-" } }) },
            { "MAXW.T",   new InstructionPattern("MAXW.T",   "-0", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "+-" } }) },
            { "MAXT.T",   new InstructionPattern("MAXT.T",   "-0", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "+0" } }) },

            // ---------------------------------------------------------------
            // Group 6 — Shift (opcode 0+)  I format  — unchanged from v1
            // ---------------------------------------------------------------
            { "SLIN.T",   new InstructionPattern("SLIN.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "--" } }) },
            { "SLIZ.T",   new InstructionPattern("SLIZ.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-0" } }) },
            { "SLIP.T",   new InstructionPattern("SLIP.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-+" } }) },
            { "SC.T",     new InstructionPattern("SC.T",     "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "00" } }) },
            { "SRIN.T",   new InstructionPattern("SRIN.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "+-" } }) },
            { "SRIZ.T",   new InstructionPattern("SRIZ.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "+0" } }) },
            { "SRIP.T",   new InstructionPattern("SRIP.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "++" } }) },

            // ---------------------------------------------------------------
            // Group 7 — Compare, Branch, Logic (opcode +-)  R/I/B format
            // ---------------------------------------------------------------
            { "CMPT.T",   new InstructionPattern("CMPT.T",   "+-", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "--" } }) },
            { "CMPWI.T",  new InstructionPattern("CMPWI.T",  "+-", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-0" } }) },
            { "CMPTI.T",  new InstructionPattern("CMPTI.T",  "+-", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-+" } }) },
            { "CMPW.T",   new InstructionPattern("CMPW.T",   "+-", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "00" } }) },
            // B format: Rd1 = branch target register; Rs1/Rs2 = comparison sources
            { "BNE.T",    new InstructionPattern("BNE.T",    "+-", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "0-" } }) },
            { "KIMP.T",   new InstructionPattern("KIMP.T",   "+-", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "0+" } }) },

            // ---------------------------------------------------------------
            // Group 8 — Conditional Branch/Execute (opcode +0)  D format
            //   4 explicit registers; no func discriminator
            // ---------------------------------------------------------------
            { "BCEG.T",   new InstructionPattern("BCEG.T",   "+0", [Rd1, Rd2, Rs1, Rs2]) },

            // ---------------------------------------------------------------
            // Group 9 — Control Flow and System (opcode ++)  J/I/SYS format
            // ---------------------------------------------------------------

            // I format: rd1, rs1, imm  (Rd2 = func = "00")
            { "JALR.T",   new InstructionPattern("JALR.T",   "++", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "00" } }) },

            // J format: rd1, imm  (Rs1 = "00" fixed; Rd2 = func)
            { "LPC.T",    new InstructionPattern("LPC.T",    "++", [Rd1, Imm],      new Dictionary<string, string> { { Rs1, "00" }, { Rd2, "0-" } }) },
            { "JAL.T",    new InstructionPattern("JAL.T",    "++", [Rd1, Imm],      new Dictionary<string, string> { { Rs1, "00" }, { Rd2, "0+" } }) },

            // SYS format: no register operands  (all data fields "00"; Rd2 = func)
            { "FENCE.T",  new InstructionPattern("FENCE.T",  "++", [], new Dictionary<string, string> { { Rs1, "00" }, { Rs2, "00" }, { Rd1, "00" }, { Rd2, "-0" } }) },
            { "WFI.T",    new InstructionPattern("WFI.T",    "++", [], new Dictionary<string, string> { { Rs1, "00" }, { Rs2, "00" }, { Rd1, "00" }, { Rd2, "-+" } }) },
            { "IRET.T",   new InstructionPattern("IRET.T",   "++", [], new Dictionary<string, string> { { Rs1, "00" }, { Rs2, "00" }, { Rd1, "00" }, { Rd2, "+-" } }) },
            { "EBREAK.T", new InstructionPattern("EBREAK.T", "++", [], new Dictionary<string, string> { { Rs1, "00" }, { Rs2, "00" }, { Rd1, "00" }, { Rd2, "+0" } }) },
            { "ECALL.T",  new InstructionPattern("ECALL.T",  "++", [], new Dictionary<string, string> { { Rs1, "00" }, { Rs2, "00" }, { Rd1, "00" }, { Rd2, "++" } }) },
        };
}
