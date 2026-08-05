using TernaryWorkbench.RebelAssembler.Assembly.Models;

namespace TernaryWorkbench.RebelAssembler.Assembly;

/// <summary>
/// REBEL-6 ISA constants and instruction patterns.
/// <para>
/// Standard encoding (4-trit opcode), 32 trits total, left to right:
/// <c>rs1[6] | rs2[6] | rd1[6] | rd2[6] | func[4] | opcode[4]</c>
/// </para>
/// <para>
/// Opcode groups (by last 2 trits of 4-trit opcode):
/// <c>xx00</c> = Base Ternary (R/I/B/D/X formats);
/// <c>xx-0</c> = Base Binary (RV32I compatible);
/// <c>xx+0</c> = Extensions (reserved for future RIBGXDY instructions).
/// The upper 2 trits (t3 t2) encode the instruction category; same t3t2 means the
/// same category in both ternary and binary groups:
/// <c>00</c>=I-type ALU, <c>0-</c>=Branch, <c>0+</c>=Store,
/// <c>--</c>=R-type ALU, <c>-+</c>=I-type Load,
/// <c>+-</c>=D/Control, <c>+0</c>=X/Upper-imm, <c>++</c>=reserved/System.
/// </para>
/// <para>
/// Func field convention: func[3:2] (upper 2 trits) are always <c>00</c>;
/// func[1:0] (lower 2 trits, LST) discriminate instructions within an opcode group.
/// The UI and documentation show only the 2 discriminating LST trits.
/// </para>
/// <para>
/// 2-trit opcode (last trit ≠ 0) — long-immediate formats:
/// <c>++</c> LWA.T (G-type); <c>0+</c> LI.T (G-type); <c>-+</c> SWA.T (Y-type);
/// <c>+-</c> JAL.T (G-type); <c>0-</c> AIPC.T (G-type); <c>--</c> Reserved.
/// </para>
/// <para>
/// G-type (2-trit opcode, long immediate + destination):
/// <c>imm[23:12][12] | rd1[6] | imm[11:0][12] | opc[2]</c>
/// </para>
/// <para>
/// Y-type (2-trit opcode, source + long immediate):
/// <c>rs1[6] | imm[23:0][24] | opc[2]</c>
/// </para>
/// <para>
/// B-type: <c>rs1[6] | rs2[6] | rd1[6] | rd2[6] | func[4] | opcode[4]</c>, where the rd1+rd2
/// slots hold displacements and the func selects how they are read —
/// one contiguous 12-trit displacement (two-way ternary branches, ±265720);
/// two independent 6-trit displacements <c>off1</c>/<c>off2</c> (three-way branches, ±364 each);
/// or the low 6 trits alone with rd1 zero (stores, binary two-way branches, ±364).
/// </para>
/// <para>
/// NOP.T encodes as all-zero 32 trits (opcode <c>0000</c>, func <c>0000</c>,
/// all register fields zero = ADDI.T X0, X0, 0).
/// </para>
/// </summary>
internal static class InstructionSet6
{
    // -------------------------------------------------------------------------
    // Field name constants (shared with existing encoder conventions where possible)
    // -------------------------------------------------------------------------

    public const string Rs1  = "rs1";
    public const string Rs2  = "rs2";
    public const string Rd1  = "rd1";
    public const string Rd2  = "rd2";
    public const string Func = "func";
    public const string Imm   = "imm";    // I-type imm12, split around rd1: rs2 slot = imm[11:6], rd2 slot = imm[5:0]
    public const string Shamt = "shamt";  // shift amount: rd2 slot alone, 4 trits (rs2 slot holds the fill)
    public const string Disp  = "disp";   // B-type imm12, contiguous across the rd1 and rd2 slots
    public const string Off1 = "off1";  // rd1 slot alone: first 6-trit displacement of a three-way branch
    public const string Off2 = "off2";  // rd2 slot alone: second 6-trit displacement of a three-way branch

    /// <summary>Largest magnitude a 6-trit balanced-ternary displacement can hold.</summary>
    public const int Displacement6Max = 364;

    /// <summary>Largest magnitude a 12-trit balanced-ternary displacement can hold.</summary>
    public const int Displacement12Max = 265720;

    /// <summary>
    /// Largest magnitude a 4-trit shift amount can hold. The binary shift-immediate instructions
    /// carry RV32I's 5-bit shamt (0..31) in 4 trits, so the field is range-checked to ±40 even
    /// though it occupies the full 12-trit immediate slot pair.
    /// </summary>
    public const int Shamt4Max = 40;

    public const string DefaultField = "000000"; // 6 trits, zero register / unused field
    public const string DefaultFunc  = "0000";   // 4-trit func field, all zero
    public const string DefaultPaddingInstruction = "NOP.T"; // encodes as all-zero 32 trits

    // REBEL-6 page: 3^6 = 729 instruction slots
    public const int PageInstructionCount = 729;
    public const int InstructionWidth     = 32;

    // -------------------------------------------------------------------------
    // Address space: 729 6-trit balanced-ternary strings, -364 … +364
    // -------------------------------------------------------------------------

    public static readonly string[] AddressSpace = BuildAddressSpace();

    private static string[] BuildAddressSpace()
    {
        var space = new string[729];
        for (int i = 0; i < 729; i++)
            space[i] = ToBalancedTernaryN(i - 364, 6);
        return space;
    }

    // -------------------------------------------------------------------------
    // Register dictionary: X-364 … X364 (X-0 omitted, only X0)
    // -------------------------------------------------------------------------

    public static readonly Dictionary<string, string> RegisterDictionary = BuildRegisterDictionary();

    private static Dictionary<string, string> BuildRegisterDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int n = -364; n <= 364; n++)
        {
            string trits = ToBalancedTernaryN(n, 6);
            if (n == 0)
                dict["X0"] = trits;
            else if (n > 0)
                dict[$"X{n}"] = trits;
            else
                dict[$"X{n}"] = trits; // e.g. "X-1", "X-364"
        }
        return dict;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts an integer to a balanced-ternary string of exactly <paramref name="width"/> trits
    /// (most-significant trit first).
    /// </summary>
    public static string ToBalancedTernaryN(int value, int width)
    {
        var digits = new char[width];
        for (int i = width - 1; i >= 0; i--)
        {
            int rem = ((value % 3) + 3) % 3; // ensure non-negative: 0, 1, or 2
            if (rem == 2)      { digits[i] = '-'; value = (value + 1) / 3; }
            else if (rem == 1) { digits[i] = '+'; value = (value - 1) / 3; }
            else               { digits[i] = '0'; value /= 3; }
        }
        if (value != 0)
            throw new OverflowException($"Value is out of the {width}-trit balanced-ternary range.");
        return new string(digits);
    }

    // -------------------------------------------------------------------------
    // Instruction patterns
    // -------------------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, InstructionPattern> Patterns =
        new Dictionary<string, InstructionPattern>(StringComparer.OrdinalIgnoreCase)
        {
            // =================================================================
            // TERNARY BASE (opcode suffix 00)
            // Detection: last trit == '0' → 4-trit opcode; NOP = all-zero (0000…0)
            // =================================================================

            // ----------------------------------------------------------------
            // R-type ALU  opcode=--00  func discriminator (stored as 4 trits)
            // ----------------------------------------------------------------
            { "ADD.T",  new InstructionPattern("ADD.T",  "--00", [Rd1, Rs1, Rs2], Func4("00--")) },
            { "SUB.T",  new InstructionPattern("SUB.T",  "--00", [Rd1, Rs1, Rs2], Func4("00-0")) },

            // Register-amount shifts: the shift amount is rs2, the fill trit is the least
            // significant trit of the otherwise unused rd2 slot. All three fills share one func,
            // so func alone does not identify the instruction — the fill trit must be read too.
            { "SLN.T",  new InstructionPattern("SLN.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("00-+"), Fixed(Rd2, "00000-"))) },
            { "SLZ.T",  new InstructionPattern("SLZ.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("00-+"), Fixed(Rd2, "000000"))) },
            { "SLP.T",  new InstructionPattern("SLP.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("00-+"), Fixed(Rd2, "00000+"))) },
            { "SRN.T",  new InstructionPattern("SRN.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("000-"), Fixed(Rd2, "00000-"))) },
            { "SRZ.T",  new InstructionPattern("SRZ.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("000-"), Fixed(Rd2, "000000"))) },
            { "SRP.T",  new InstructionPattern("SRP.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("000-"), Fixed(Rd2, "00000+"))) },

            { "SLT.T",  new InstructionPattern("SLT.T",  "--00", [Rd1, Rs1, Rs2], Func4("0000")) },
            { "OR.T",   new InstructionPattern("OR.T",   "--00", [Rd1, Rs1, Rs2], Func4("000+")) },
            { "XOR.T",  new InstructionPattern("XOR.T",  "--00", [Rd1, Rs1, Rs2], Func4("00+-")) },
            { "AND.T",  new InstructionPattern("AND.T",  "--00", [Rd1, Rs1, Rs2], Func4("00+0")) },

            // ----------------------------------------------------------------
            // R-type misc  opcode=-000
            // ----------------------------------------------------------------
            { "CMP.T",  new InstructionPattern("CMP.T",  "-000", [Rd1, Rs1, Rs2], Func4("00--")) },
            { "STI.T",  new InstructionPattern("STI.T",  "-000", [Rd1, Rs1],
                Merge(Func4("00-0"), Fixed(Rs2, DefaultField))) },

            // ----------------------------------------------------------------
            // I-type ALU  opcode=0000   (Imm → rs2 slot; NOP = all-zero)
            // Func 0000 = ADDI.T, others follow
            // ----------------------------------------------------------------
            { "ADDI.T",  new InstructionPattern("ADDI.T",  "0000", [Rd1, Rs1, Imm], Func4("0000")) },

            // Immediate shifts read imm12 as its two natural 6-trit halves: the rs2 slot
            // (imm[11:6]) carries the fill selector, the rd2 slot (imm[5:0]) carries shamt.
            // SC.T is cyclic, so it has no fill and requires the selector half to be zero.
            { "SLIN.T",  new InstructionPattern("SLIN.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00--"), Fixed(Rs2, "00000-"))) },
            { "SLIZ.T",  new InstructionPattern("SLIZ.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00--"), Fixed(Rs2, "000000"))) },
            { "SLIP.T",  new InstructionPattern("SLIP.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00--"), Fixed(Rs2, "00000+"))) },
            { "SRIN.T",  new InstructionPattern("SRIN.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00-0"), Fixed(Rs2, "00000-"))) },
            { "SRIZ.T",  new InstructionPattern("SRIZ.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00-0"), Fixed(Rs2, "000000"))) },
            { "SRIP.T",  new InstructionPattern("SRIP.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00-0"), Fixed(Rs2, "00000+"))) },
            { "SC.T",    new InstructionPattern("SC.T",    "0000", [Rd1, Rs1, Shamt], Merge(Func4("00-+"), Fixed(Rs2, DefaultField))) },

            { "SLTI.T",  new InstructionPattern("SLTI.T",  "0000", [Rd1, Rs1, Imm], Func4("000-")) },
            { "ORI.T",   new InstructionPattern("ORI.T",   "0000", [Rd1, Rs1, Imm], Func4("000+")) },
            { "XORI.T",  new InstructionPattern("XORI.T",  "0000", [Rd1, Rs1, Imm], Func4("00+-")) },
            { "ANDI.T",  new InstructionPattern("ANDI.T",  "0000", [Rd1, Rs1, Imm], Func4("00+0")) },

            // Pseudo-instructions (reuse ADDI.T encoding with func 0000)
            { "NOP.T",   new InstructionPattern("NOP.T",   "0000", [],
                Merge(Func4("0000"), Fixed(Rs1, DefaultField), Fixed(Rs2, DefaultField), Fixed(Rd1, DefaultField), Fixed(Rd2, DefaultField))) },
            { "MV.T",    new InstructionPattern("MV.T",    "0000", [Rd1, Rs1],
                Merge(Func4("0000"), Fixed(Rs2, DefaultField), Fixed(Rd2, DefaultField))) },

            // ----------------------------------------------------------------
            // I-type load  opcode=-+00   (indexed: base register + imm12 split around rd1)
            // The absolute counterparts are the G/Y-type LWA.T / SWA.T.
            // ----------------------------------------------------------------
            { "LW.T",    new InstructionPattern("LW.T",    "-+00", [Rd1, Rs1, Imm], Func4("00--")) },
            { "LH.T",    new InstructionPattern("LH.T",    "-+00", [Rd1, Rs1, Imm], Func4("00-0")) },
            { "LT.T",    new InstructionPattern("LT.T",    "-+00", [Rd1, Rs1, Imm], Func4("00-+")) },
            { "JALR.T",  new InstructionPattern("JALR.T",  "-+00", [Rd1, Rs1, Imm], Func4("000-")) },

            // ----------------------------------------------------------------
            // B-type three-way branch  opcode=0-00   (rd1+rd2 read as two 6-trit
            // displacements, ±364 each)
            // BCGS.T: rs1 > rs2 → PC+off1; rs1 < rs2 → PC+off2; rs1 == rs2 → PC+1
            // BCEG.T: rs1 == rs2 → PC+off1; rs1 > rs2 → PC+off2; rs1 < rs2 → PC+1
            // ----------------------------------------------------------------
            { "BCGS.T",  new InstructionPattern("BCGS.T",  "0-00", [Rs1, Rs2, Off1, Off2], Func4("00--")) },
            { "BCEG.T",  new InstructionPattern("BCEG.T",  "0-00", [Rs1, Rs2, Off1, Off2], Func4("00-0")) },

            // ----------------------------------------------------------------
            // B-type two-way branch  opcode=0-00   (rd1+rd2 read as one contiguous
            // 12-trit displacement, ±265720 — 259x the RV32I branch range)
            // BGT.T and BLE.T are pseudo-instructions over BLT.T / BGE.T with the
            // source operands swapped (see PseudoExpansions).
            // ----------------------------------------------------------------
            { "BEQ.T",   new InstructionPattern("BEQ.T",   "0-00", [Rs1, Rs2, Disp], Func4("00-+")) },
            { "BNE.T",   new InstructionPattern("BNE.T",   "0-00", [Rs1, Rs2, Disp], Func4("000-")) },
            { "BLT.T",   new InstructionPattern("BLT.T",   "0-00", [Rs1, Rs2, Disp], Func4("0000")) },
            { "BGE.T",   new InstructionPattern("BGE.T",   "0-00", [Rs1, Rs2, Disp], Func4("000+")) },

            // ----------------------------------------------------------------
            // B-type store  opcode=0+00   (indexed: base register + imm12 contiguous)
            // ----------------------------------------------------------------
            { "SW.T",    new InstructionPattern("SW.T",    "0+00", [Rs1, Rs2, Disp], Func4("00--")) },
            { "SH.T",    new InstructionPattern("SH.T",    "0+00", [Rs1, Rs2, Disp], Func4("00-0")) },
            { "ST.T",    new InstructionPattern("ST.T",    "0+00", [Rs1, Rs2, Disp], Func4("00-+")) },

            // ----------------------------------------------------------------
            // D-type (3 sources)  opcode=+-00   (Rd2 slot encodes Rs3)
            // ----------------------------------------------------------------
            { "MAJV.T",  new InstructionPattern("MAJV.T",  "+-00", [Rd1, Rs1, Rs2, Rd2], Func4("00--")) },
            { "MINV.T",  new InstructionPattern("MINV.T",  "+-00", [Rd1, Rs1, Rs2, Rd2], Func4("00-0")) },

            // ----------------------------------------------------------------
            // X-type (dual immediate)  opcode=+000
            // [Rd1, Rd2, Rs1, Rs2] — Rs1/Rs2 slots carry the two immediates
            // ----------------------------------------------------------------
            { "LI2.T",   new InstructionPattern("LI2.T",   "+000", [Rd1, Rd2, Rs1, Rs2], Func4("00--")) },

            // =================================================================
            // LONG-IMMEDIATE FORMS (2-trit opcode, last trit ≠ 0)
            // G-type: imm[23:12](12) | rd1(6) | imm[11:0](12) | opc(2)
            // Y-type: rs1(6) | imm[23:0](24) | opc(2)
            // =================================================================
            // LWA.T / SWA.T are the absolute-addressed word load and store: a 24-trit
            // address and no base register. Indexed word access is LW.T / SW.T above.
            { "LWA.T",   new InstructionPattern("LWA.T",   "++",  [Rd1, Imm]) },
            { "LI.T",    new InstructionPattern("LI.T",    "0+",  [Rd1, Imm]) },
            { "SWA.T",   new InstructionPattern("SWA.T",   "-+",  [Rs1, Imm]) },
            { "JAL.T",   new InstructionPattern("JAL.T",   "+-",  [Rd1, Imm]) },
            { "AIPC.T",  new InstructionPattern("AIPC.T",  "0-",  [Rd1, Imm]) },

            // =================================================================
            // BINARY BASE (opcode suffix -0)
            // =================================================================

            // ----------------------------------------------------------------
            // R-type binary ALU  opcode=---0
            // ----------------------------------------------------------------
            { "ADD",     new InstructionPattern("ADD",  "---0", [Rd1, Rs1, Rs2], Func4("00--")) },
            { "SUB",     new InstructionPattern("SUB",  "---0", [Rd1, Rs1, Rs2], Func4("00-0")) },
            { "SLL",     new InstructionPattern("SLL",  "---0", [Rd1, Rs1, Rs2], Func4("00-+")) },
            { "SRL",     new InstructionPattern("SRL",  "---0", [Rd1, Rs1, Rs2], Func4("000-")) },
            { "SRA",     new InstructionPattern("SRA",  "---0", [Rd1, Rs1, Rs2], Func4("0000")) },
            { "SLTU",    new InstructionPattern("SLTU", "---0", [Rd1, Rs1, Rs2], Func4("000+")) },
            { "OR",      new InstructionPattern("OR",   "---0", [Rd1, Rs1, Rs2], Func4("00+-")) },
            { "XOR",     new InstructionPattern("XOR",  "---0", [Rd1, Rs1, Rs2], Func4("00+0")) },
            { "AND",     new InstructionPattern("AND",  "---0", [Rd1, Rs1, Rs2], Func4("00++")) },

            // ----------------------------------------------------------------
            // I-type binary ALU  opcode=00-0  (Imm → rs2 slot; parallel to ternary 00)
            // ----------------------------------------------------------------
            { "ADDI",    new InstructionPattern("ADDI",  "00-0", [Rd1, Rs1, Imm],   Func4("00--")) },
            { "SLLI",    new InstructionPattern("SLLI",  "00-0", [Rd1, Rs1, Shamt], Merge(Func4("00-0"), Fixed(Rs2, DefaultField))) },
            { "SRLI",    new InstructionPattern("SRLI",  "00-0", [Rd1, Rs1, Shamt], Merge(Func4("00-+"), Fixed(Rs2, DefaultField))) },
            { "SRAI",    new InstructionPattern("SRAI",  "00-0", [Rd1, Rs1, Shamt], Merge(Func4("000-"), Fixed(Rs2, DefaultField))) },
            { "SLTIU",   new InstructionPattern("SLTIU", "00-0", [Rd1, Rs1, Imm], Func4("0000")) },
            { "ORI",     new InstructionPattern("ORI",   "00-0", [Rd1, Rs1, Imm], Func4("000+")) },
            { "XORI",    new InstructionPattern("XORI",  "00-0", [Rd1, Rs1, Imm], Func4("00+-")) },
            { "ANDI",    new InstructionPattern("ANDI",  "00-0", [Rd1, Rs1, Imm], Func4("00+0")) },

            // ----------------------------------------------------------------
            // I-type binary load  opcode=-+-0  (Imm → rs2 slot)
            // ----------------------------------------------------------------
            { "LW",      new InstructionPattern("LW",  "-+-0", [Rd1, Rs1, Imm], Func4("00--")) },
            { "LH",      new InstructionPattern("LH",  "-+-0", [Rd1, Rs1, Imm], Func4("00-0")) },
            { "LB",      new InstructionPattern("LB",  "-+-0", [Rd1, Rs1, Imm], Func4("00-+")) },
            { "LHU",     new InstructionPattern("LHU", "-+-0", [Rd1, Rs1, Imm], Func4("000-")) },
            { "LBU",     new InstructionPattern("LBU", "-+-0", [Rd1, Rs1, Imm], Func4("0000")) },

            // ----------------------------------------------------------------
            // B-type binary branch  opcode=0--0  (unsigned only; parallel to ternary 0-xx00 branch group)
            // BLTU=000+ BGEU=00+-
            // ----------------------------------------------------------------
            { "BLTU",    new InstructionPattern("BLTU", "0--0", [Rs1, Rs2, Disp], Func4("00--")) },
            { "BGEU",    new InstructionPattern("BGEU", "0--0", [Rs1, Rs2, Disp], Func4("00-0")) },

            // ----------------------------------------------------------------
            // B-type binary store  opcode=0+-0  (Offset → rd2 slot; parallel to ternary 0+xx00 store)
            // ----------------------------------------------------------------
            { "SW",      new InstructionPattern("SW",  "0+-0", [Rs1, Rs2, Disp], Func4("00--")) },
            { "SH",      new InstructionPattern("SH",  "0+-0", [Rs1, Rs2, Disp], Func4("00-0")) },
            { "SB",      new InstructionPattern("SB",  "0+-0", [Rs1, Rs2, Disp], Func4("00-+")) },

        };

    // -------------------------------------------------------------------------
    // Pseudo-instructions resolved by operand duplication
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pseudo-instructions the encoder rewrites before pattern lookup. These are not in
    /// <see cref="Patterns"/>, so the disassembler always emits the architectural form.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, PseudoExpansion> PseudoExpansions =
        new Dictionary<string, PseudoExpansion>(StringComparer.OrdinalIgnoreCase)
        {
            // Swapping the sources exchanges the greater and smaller outcomes, so the two
            // remaining orderings need no encoding of their own (as in RISC-V).
            { "BGT.T", new PseudoExpansion("BLT.T", 3, ["$1", "$0", "$2"]) },
            { "BLE.T", new PseudoExpansion("BGE.T", 3, ["$1", "$0", "$2"]) },
        };

    // -------------------------------------------------------------------------
    // Field mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// The two 6-trit slots a 12-trit field occupies, most-significant half first, or
    /// <c>null</c> when the field fits in a single slot.
    /// <para>
    /// I-type splits its immediate around the destination register (rs2 slot holds imm[11:6],
    /// rd2 slot holds imm[5:0]); B-type has no destination, so its 12 trits are contiguous
    /// across the rd1 and rd2 slots.
    /// </para>
    /// </summary>
    public static (string Hi, string Lo)? WideFieldSlots(string fieldName) =>
        string.Equals(fieldName, Imm,  StringComparison.OrdinalIgnoreCase) ? (Rs2, Rd2)
      : string.Equals(fieldName, Disp, StringComparison.OrdinalIgnoreCase) ? (Rd1, Rd2)
      : null;

    /// <summary>
    /// Maps an assembly-level field name to the single physical encoding slot it occupies.
    /// Not valid for 12-trit fields — use <see cref="MapFieldToSlots"/>.
    /// </summary>
    public static string MapFieldToSlot(string fieldName) =>
        string.Equals(fieldName, Off1,  StringComparison.OrdinalIgnoreCase) ? Rd1
      : string.Equals(fieldName, Off2,  StringComparison.OrdinalIgnoreCase) ? Rd2
      : string.Equals(fieldName, Shamt, StringComparison.OrdinalIgnoreCase) ? Rd2
      : fieldName;

    /// <summary>
    /// True when the field is a shift amount: it occupies the rd2 slot alone, leaving the rs2 slot
    /// free to carry the fill selector, and is range-checked to 4 trits.
    /// </summary>
    public static bool IsShamtField(string fieldName) =>
        string.Equals(fieldName, Shamt, StringComparison.OrdinalIgnoreCase);

    /// <summary>Maps an assembly-level field name to every encoding slot it occupies.</summary>
    public static IEnumerable<string> MapFieldToSlots(string fieldName) =>
        WideFieldSlots(fieldName) is { } slots ? [slots.Hi, slots.Lo] : [MapFieldToSlot(fieldName)];

    /// <summary>True when the field carries a PC-relative displacement rather than a register.</summary>
    public static bool IsOffsetField(string fieldName) =>
        string.Equals(fieldName, Off1, StringComparison.OrdinalIgnoreCase)
     || string.Equals(fieldName, Off2, StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Pattern-building helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns a Defaults dict with a single Func entry.</summary>
    private static Dictionary<string, string> Func4(string func4) =>
        new(StringComparer.OrdinalIgnoreCase) { { Func, func4 } };

    /// <summary>Returns a single-entry fixed-field dict.</summary>
    private static Dictionary<string, string> Fixed(string field, string value) =>
        new(StringComparer.OrdinalIgnoreCase) { { field, value } };

    /// <summary>Merges multiple single-entry dicts into one Defaults dict.</summary>
    private static Dictionary<string, string> Merge(params Dictionary<string, string>[] parts)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
            foreach (var kv in part)
                result[kv.Key] = kv.Value;
        return result;
    }
}
