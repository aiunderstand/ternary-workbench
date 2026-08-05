using TernaryWorkbench.RebelAssembler.Assembly.Models;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class InstructionSet
{
    public const string Rd1 = "rd1";
    public const string Rd2 = "rd2";
    public const string Rs1 = "rs1";
    public const string Rs2 = "rs2";
    public const string Imm = "imm";

    public const string DefaultField = "00";
    public const string DefaultPaddingInstruction = "NOP.T";
    public const string ZeroInstruction = "0000000000";
    public const int PageInstructionCount = 9;

    public static readonly char[] ArgumentSeparators = [' ', '\t', ','];
    public static readonly string[] AddressSpace = ["--", "-0", "-+", "0-", "00", "0+", "+-", "+0", "++"];
    public static readonly string[] NewLineSeparators = ["\r\n", "\n", "\r"];

    public static readonly Dictionary<string, int> FieldOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        { "opcode", 0 }, // length 2
        { Rs1, 2 },
        { Rs2, 4 },
        { Rd1, 6 },
        { Rd2, 8 }
    };

    public static readonly Dictionary<string, string> RegisterDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        { "X-4", "--" },
        { "X-3", "-0" },
        { "X-2", "-+" },
        { "X-1", "0-" },
        { "X-0", "00" },
        { "X0",  "00" },
        { "X1",  "0+" },
        { "X2",  "+-" },
        { "X3",  "+0" },
        { "X4",  "++" }
    };

    public static readonly Dictionary<string, InstructionPattern> Patterns = new(StringComparer.OrdinalIgnoreCase)
    {
        // 4.1 ADD family
        { "ADD.T",  new InstructionPattern("ADD.T",  "--", [Rd1, Rs1, Rs2]) },
        { "SUB.T",  new InstructionPattern("SUB.T",  "--", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "--" } }) },
        { "STI.T",  new InstructionPattern("STI.T",  "--", [Rd1, Rs2],      new Dictionary<string, string> { { Rd2, "--" } }) },

        // 4.2 ADDi family
        { "ADDI.T", new InstructionPattern("ADDI.T", "-0", [Rd1, Rs1, Imm]) },
        { "NOP.T",  new InstructionPattern("NOP.T",  "-0", []) },
        { "LI.T",   new InstructionPattern("LI.T",   "-0", [Rd1, Imm]) },
        { "MV.T",   new InstructionPattern("MV.T",   "-0", [Rd1, Rs1]) },

        // 4.3 ADDi2
        { "ADDI2.T", new InstructionPattern("ADDI2.T", "-+", [Rd1, Rd2, Rs1, Rs2]) },

        // 4.4 MUDI
        { "MUL.T",  new InstructionPattern("MUL.T",  "0-", [Rd1, Rs1, Rs2]) },

        // 4.5 MIMA
        { "MINW.T",  new InstructionPattern("MINW.T", "00", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "--" } }) },
        { "MINT.T",  new InstructionPattern("MINT.T", "00", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "-0" } }) },
        { "MAXW.T",  new InstructionPattern("MAXW.T", "00", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "+-" } }) },
        { "MAXT.T",  new InstructionPattern("MAXT.T", "00", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "+0" } }) },

        // 4.6 SHI
        { "SLIN.T", new InstructionPattern("SLIN.T", "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "--" } }) },
        { "SLIZ.T", new InstructionPattern("SLIZ.T", "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-0" } }) },
        { "SLIP.T", new InstructionPattern("SLIP.T", "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "-+" } }) },
        { "SRIN.T", new InstructionPattern("SRIN.T", "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "+-" } }) },
        { "SRIZ.T", new InstructionPattern("SRIZ.T", "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "+0" } }) },
        { "SRIP.T", new InstructionPattern("SRIP.T", "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "++" } }) },
        { "SC.T",   new InstructionPattern("SC.T",   "0+", [Rd1, Rs1, Imm], new Dictionary<string, string> { { Rd2, "00" } }) },

        // 4.7 COMP
        { "CMPW.T", new InstructionPattern("CMPW.T", "+-", [Rd1, Rs1, Rs2]) },
        { "CMPT.T", new InstructionPattern("CMPT.T", "+-", [Rd1, Rs1, Rs2], new Dictionary<string, string> { { Rd2, "--" } }) },

        // 4.8 BCEG
        { "BCEG.T", new InstructionPattern("BCEG.T", "+0", [Rd1, Rd2, Rs1, Rs2]) },

        // 4.9 PCO
        { "JAL.T",  new InstructionPattern("JAL.T",  "++", [Rd1, Imm], new Dictionary<string, string> { { Rd2, "0+" } }) },
        { "JALR.T", new InstructionPattern("JALR.T", "++", [Rd1, Rs1, Imm]) },
        { "LPC.T",  new InstructionPattern("LPC.T",  "++", [Rd1, Imm], new Dictionary<string, string> { { Rd2, "0-" } }) }
    };
}
