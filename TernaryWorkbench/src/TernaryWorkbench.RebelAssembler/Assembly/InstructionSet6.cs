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

            // Register-amount shifts (signed amount — errata E-4): the direction comes from the
            // sign of the amount, read from the low 4 trits of rs2 (balanced, ±40). k > 0 shifts
            // toward the MST (the old left shift), k < 0 toward the LST, |k| >= 24 gives all-fill.
            // The fill trit is the least significant trit of the otherwise unused rd2 slot. All
            // three fills share one func, so func alone does not identify the instruction — the
            // fill trit must be read too. The old SL{N,Z,P}.T names live on as pseudos (see
            // PseudoExpansions); the register right shifts SR{N,Z,P}.T are retired outright.
            { "SHN.T",  new InstructionPattern("SHN.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("00-+"), Fixed(Rd2, "00000-"))) },
            { "SHZ.T",  new InstructionPattern("SHZ.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("00-+"), Fixed(Rd2, "000000"))) },
            { "SHP.T",  new InstructionPattern("SHP.T",  "--00", [Rd1, Rs1, Rs2], Merge(Func4("00-+"), Fixed(Rd2, "00000+"))) },

            // Register-amount rotate (errata E-4) in the func slot freed by the retired SR* family:
            // amount = signed low 4 trits of rs2, applied mod 24 (+ = toward MST). Cyclic, so it
            // has no fill — the rd2 slot is required zero, the same selector rule as ROT.T.
            { "ROTR.T", new InstructionPattern("ROTR.T", "--00", [Rd1, Rs1, Rs2], Merge(Func4("000-"), Fixed(Rd2, DefaultField))) },

            { "SLT.T",  new InstructionPattern("SLT.T",  "--00", [Rd1, Rs1, Rs2], Func4("0000")) },
            { "OR.T",   new InstructionPattern("OR.T",   "--00", [Rd1, Rs1, Rs2], Func4("000+")) },
            { "XOR.T",  new InstructionPattern("XOR.T",  "--00", [Rd1, Rs1, Rs2], Func4("00+-")) },
            { "AND.T",  new InstructionPattern("AND.T",  "--00", [Rd1, Rs1, Rs2], Func4("00+0")) },

            // ----------------------------------------------------------------
            // R-type compare/unary  opcode=-000
            // MIN.T / MAX.T are wordwise (arithmetic select); their funcs are negatives
            // of each other, mirroring min(a,b) = -max(-a,-b). MV2.T is the dual move
            // (both reads complete before either write); SWAP.T is its operand-crossed
            // pseudo (see PseudoExpansions).
            // ----------------------------------------------------------------
            { "CMP.T",  new InstructionPattern("CMP.T",  "-000", [Rd1, Rs1, Rs2], Func4("00--")) },
            { "STI.T",  new InstructionPattern("STI.T",  "-000", [Rd1, Rs1],
                Merge(Func4("00-0"), Fixed(Rs2, DefaultField))) },
            { "MV2.T",  new InstructionPattern("MV2.T",  "-000", [Rd1, Rd2, Rs1, Rs2], Func4("00-+")) },
            { "MIN.T",  new InstructionPattern("MIN.T",  "-000", [Rd1, Rs1, Rs2], Func4("000-")) },
            { "MAX.T",  new InstructionPattern("MAX.T",  "-000", [Rd1, Rs1, Rs2], Func4("000+")) },

            // ----------------------------------------------------------------
            // I-type ALU  opcode=0000   (Imm → rs2 slot; NOP = all-zero)
            // Func 0000 = ADDI.T, others follow
            // ----------------------------------------------------------------
            { "ADDI.T",  new InstructionPattern("ADDI.T",  "0000", [Rd1, Rs1, Imm], Func4("0000")) },

            // Immediate shifts (signed amount — errata E-4) read imm12 as its two natural 6-trit
            // halves: the rs2 slot (imm[11:6]) carries the fill selector, the rd2 slot (imm[5:0])
            // carries the signed shamt (direction = sign; k > 0 toward MST, k < 0 toward LST).
            // The old SLI*/SRI* mnemonics are pseudos over SHI<f>.T (see PseudoExpansions); the
            // I-type func -0 slot the SRI* family occupied is reserved.
            // ROT.T is cyclic and signed both ways (mod 24), so it has no fill and requires the
            // selector half to be zero. (ROT.T was named SC.T before the A extension reclaimed
            // that mnemonic for store-conditional; see docs/rebel6-isa.md errata E-3. No alias
            // here: with the A extension in the table, SC.T unambiguously means store-conditional.)
            { "SHIN.T",  new InstructionPattern("SHIN.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00--"), Fixed(Rs2, "00000-"))) },
            { "SHIZ.T",  new InstructionPattern("SHIZ.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00--"), Fixed(Rs2, "000000"))) },
            { "SHIP.T",  new InstructionPattern("SHIP.T",  "0000", [Rd1, Rs1, Shamt], Merge(Func4("00--"), Fixed(Rs2, "00000+"))) },
            { "ROT.T",   new InstructionPattern("ROT.T",   "0000", [Rd1, Rs1, Shamt], Merge(Func4("00-+"), Fixed(Rs2, DefaultField))) },

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

            // ----------------------------------------------------------------
            // System  opcode=++00   (zero operands; all four operand slots zero)
            // Funcs deliberately match REBEL-2 V2.2's ++ control group; TRET.T takes
            // its predecessor IRET.T's slot. Semantics: docs/rebel6-platform.md.
            // ----------------------------------------------------------------
            { "FENCE.T",  new InstructionPattern("FENCE.T",  "++00", [],
                Merge(Func4("00-0"), Fixed(Rs1, DefaultField), Fixed(Rs2, DefaultField), Fixed(Rd1, DefaultField), Fixed(Rd2, DefaultField))) },
            { "WFI.T",    new InstructionPattern("WFI.T",    "++00", [],
                Merge(Func4("00-+"), Fixed(Rs1, DefaultField), Fixed(Rs2, DefaultField), Fixed(Rd1, DefaultField), Fixed(Rd2, DefaultField))) },
            { "TRET.T",   new InstructionPattern("TRET.T",   "++00", [],
                Merge(Func4("00+-"), Fixed(Rs1, DefaultField), Fixed(Rs2, DefaultField), Fixed(Rd1, DefaultField), Fixed(Rd2, DefaultField))) },
            { "EBREAK.T", new InstructionPattern("EBREAK.T", "++00", [],
                Merge(Func4("00+0"), Fixed(Rs1, DefaultField), Fixed(Rs2, DefaultField), Fixed(Rd1, DefaultField), Fixed(Rd2, DefaultField))) },
            { "ECALL.T",  new InstructionPattern("ECALL.T",  "++00", [],
                Merge(Func4("00++"), Fixed(Rs1, DefaultField), Fixed(Rs2, DefaultField), Fixed(Rd1, DefaultField), Fixed(Rd2, DefaultField))) },

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

            // =================================================================
            // EXTENSIONS (opcode suffix +0) — ternary-only designs; the sole
            // exception is Zicsr (binary System opcode ++-0), which requires the
            // Base Binary layer. Encodings: docs/rebel6-extensions.md.
            // =================================================================

            // ----------------------------------------------------------------
            // M — integer multiply / divide  opcode=--+0 (R-shape, shared with F arith)
            // DIV.T truncates toward zero and pairs with REM.T; MOD.T is floored.
            // ----------------------------------------------------------------
            { "MUL.T",   new InstructionPattern("MUL.T",   "--+0", [Rd1, Rs1, Rs2], Func4("00--")) },
            { "MULH.T",  new InstructionPattern("MULH.T",  "--+0", [Rd1, Rs1, Rs2], Func4("00-0")) },
            { "DIV.T",   new InstructionPattern("DIV.T",   "--+0", [Rd1, Rs1, Rs2], Func4("000-")) },
            { "REM.T",   new InstructionPattern("REM.T",   "--+0", [Rd1, Rs1, Rs2], Func4("000+")) },
            { "MOD.T",   new InstructionPattern("MOD.T",   "--+0", [Rd1, Rs1, Rs2], Func4("00+-")) },

            // ----------------------------------------------------------------
            // F — trifloat24 scalar float (docs/rebel6-trifloat24.md)
            // Arithmetic shares opcode --+0 with M; compare/convert live in -0+0.
            // ----------------------------------------------------------------
            { "FADD.T",   new InstructionPattern("FADD.T",   "--+0", [Rd1, Rs1, Rs2], Func4("00-+")) },
            { "FSUB.T",   new InstructionPattern("FSUB.T",   "--+0", [Rd1, Rs1, Rs2], Func4("0000")) },
            { "FMUL.T",   new InstructionPattern("FMUL.T",   "--+0", [Rd1, Rs1, Rs2], Func4("00+0")) },
            { "FDIV.T",   new InstructionPattern("FDIV.T",   "--+0", [Rd1, Rs1, Rs2], Func4("00++")) },
            { "FCMP.T",   new InstructionPattern("FCMP.T",   "-0+0", [Rd1, Rs1, Rs2], Func4("00--")) },
            { "FCVT.W.T", new InstructionPattern("FCVT.W.T", "-0+0", [Rd1, Rs1],
                Merge(Func4("00-0"), Fixed(Rs2, DefaultField))) },
            { "FCVT.T.W", new InstructionPattern("FCVT.T.W", "-0+0", [Rd1, Rs1],
                Merge(Func4("00-+"), Fixed(Rs2, DefaultField))) },

            // ----------------------------------------------------------------
            // A — atomics  opcode=-++0 (load-category extension slot; word-sized)
            // Assembles the bare (relaxed) forms only: the aq/rl ordering trits in the
            // rd2 slot encode as zero. Suffixed .AQ/.RL/.AQRL forms are reserved.
            // SC.T here is store-conditional — the cyclic shift is ROT.T (errata E-3).
            // ----------------------------------------------------------------
            { "LR.T",      new InstructionPattern("LR.T",      "-++0", [Rd1, Rs1],
                Merge(Func4("00--"), Fixed(Rs2, DefaultField))) },
            { "SC.T",      new InstructionPattern("SC.T",      "-++0", [Rd1, Rs1, Rs2], Func4("00-0")) },
            { "AMOSWAP.T", new InstructionPattern("AMOSWAP.T", "-++0", [Rd1, Rs1, Rs2], Func4("00-+")) },
            { "AMOADD.T",  new InstructionPattern("AMOADD.T",  "-++0", [Rd1, Rs1, Rs2], Func4("000-")) },
            { "AMOAND.T",  new InstructionPattern("AMOAND.T",  "-++0", [Rd1, Rs1, Rs2], Func4("0000")) },
            { "AMOOR.T",   new InstructionPattern("AMOOR.T",   "-++0", [Rd1, Rs1, Rs2], Func4("000+")) },
            { "AMOXOR.T",  new InstructionPattern("AMOXOR.T",  "-++0", [Rd1, Rs1, Rs2], Func4("00+-")) },
            { "AMOMIN.T",  new InstructionPattern("AMOMIN.T",  "-++0", [Rd1, Rs1, Rs2], Func4("00+0")) },
            { "AMOMAX.T",  new InstructionPattern("AMOMAX.T",  "-++0", [Rd1, Rs1, Rs2], Func4("00++")) },

            // ----------------------------------------------------------------
            // D-shape extension ops  opcode=+-+0   (Rd2 slot = rs3 / truth table)
            // TDOT.T is TMAC.T with rs3 pinned to X0; its extra pinned slot outscores
            // TMAC.T during disassembly exactly as NOP.T outscores ADDI.T.
            // ----------------------------------------------------------------
            { "TLUT.T",  new InstructionPattern("TLUT.T",  "+-+0", [Rd1, Rs1, Rs2, Rd2], Func4("00--")) },
            { "MAC.T",   new InstructionPattern("MAC.T",   "+-+0", [Rd1, Rs1, Rs2, Rd2], Func4("00-0")) },
            { "FMA.T",   new InstructionPattern("FMA.T",   "+-+0", [Rd1, Rs1, Rs2, Rd2], Func4("00-+")) },
            { "TMAC.T",  new InstructionPattern("TMAC.T",  "+-+0", [Rd1, Rs1, Rs2, Rd2], Func4("000-")) },
            { "TDOT.T",  new InstructionPattern("TDOT.T",  "+-+0", [Rd1, Rs1, Rs2],
                Merge(Func4("000-"), Fixed(Rd2, DefaultField))) },

            // ----------------------------------------------------------------
            // P — packed ternary scalar reductions/quantize  opcode=-0+0 (with F cmp/cvt, Ztb)
            // ----------------------------------------------------------------
            { "TSUM.T",  new InstructionPattern("TSUM.T",  "-0+0", [Rd1, Rs1],
                Merge(Func4("000+"), Fixed(Rs2, DefaultField))) },
            { "QNT.T",   new InstructionPattern("QNT.T",   "-0+0", [Rd1, Rs1, Rs2], Func4("00+-")) },
            { "HMAX.T",  new InstructionPattern("HMAX.T",  "-0+0", [Rd1, Rs1],
                Merge(Func4("00+0"), Fixed(Rs2, DefaultField))) },

            // ----------------------------------------------------------------
            // Ztl — programmable tritwise gates  TLUTI.T opcode=00+0 (I-shape)
            // The unary canonical gates are patterns over TLUTI.T with the 3-trit
            // table pinned in the rd2 slot (imm[5:0]); table entry for input a sits
            // at trit position a+1. Their extra pinned slots win disassembly.
            // Binary gates (KIMP/CMPT/CONS) need the 9-trit table in rs3 — they are
            // documented idioms over TLUT.T, not single-instruction expansions.
            // ----------------------------------------------------------------
            { "TLUTI.T", new InstructionPattern("TLUTI.T", "00+0", [Rd1, Rs1, Imm], Func4("00--")) },
            { "NTI.T",   new InstructionPattern("NTI.T",   "00+0", [Rd1, Rs1],
                Merge(Func4("00--"), Fixed(Rs2, DefaultField), Fixed(Rd2, "000--+"))) },
            { "PTI.T",   new InstructionPattern("PTI.T",   "00+0", [Rd1, Rs1],
                Merge(Func4("00--"), Fixed(Rs2, DefaultField), Fixed(Rd2, "000-++"))) },
            { "MTI.T",   new InstructionPattern("MTI.T",   "00+0", [Rd1, Rs1],
                Merge(Func4("00--"), Fixed(Rs2, DefaultField), Fixed(Rd2, "000+0+"))) },
            { "CYU.T",   new InstructionPattern("CYU.T",   "00+0", [Rd1, Rs1],
                Merge(Func4("00--"), Fixed(Rs2, DefaultField), Fixed(Rd2, "000-+0"))) },
            { "CYD.T",   new InstructionPattern("CYD.T",   "00+0", [Rd1, Rs1],
                Merge(Func4("00--"), Fixed(Rs2, DefaultField), Fixed(Rd2, "0000-+"))) },

            // ----------------------------------------------------------------
            // Ztb — trit manipulation  opcode=-0+0
            // ----------------------------------------------------------------
            { "CLZT.T",  new InstructionPattern("CLZT.T",  "-0+0", [Rd1, Rs1],
                Merge(Func4("000-"), Fixed(Rs2, DefaultField))) },
            { "TCNT.T",  new InstructionPattern("TCNT.T",  "-0+0", [Rd1, Rs1],
                Merge(Func4("0000"), Fixed(Rs2, DefaultField))) },

            // ----------------------------------------------------------------
            // Zicsr — CSR access  opcode=++-0 (binary System; requires Base Binary)
            // I-shape: CSR number rides the imm12 field (split around rd1); rs1 slot
            // carries the source register, or the 5-bit zimm as a value for the *I forms.
            // CSR numbers accept names from CsrNames (e.g. mstatus) or plain numerics.
            // ----------------------------------------------------------------
            { "CSRRW",   new InstructionPattern("CSRRW",  "++-0", [Rd1, Imm, Rs1], Func4("00--")) },
            { "CSRRS",   new InstructionPattern("CSRRS",  "++-0", [Rd1, Imm, Rs1], Func4("00-0")) },
            { "CSRRC",   new InstructionPattern("CSRRC",  "++-0", [Rd1, Imm, Rs1], Func4("00-+")) },
            { "CSRRWI",  new InstructionPattern("CSRRWI", "++-0", [Rd1, Imm, Rs1], Func4("000-")) },
            { "CSRRSI",  new InstructionPattern("CSRRSI", "++-0", [Rd1, Imm, Rs1], Func4("0000")) },
            { "CSRRCI",  new InstructionPattern("CSRRCI", "++-0", [Rd1, Imm, Rs1], Func4("000+")) },

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

            // SWAP.T a, b exchanges two registers via the dual move: both reads complete
            // before either write, so the crossed operands express the swap exactly.
            { "SWAP.T", new PseudoExpansion("MV2.T", 2, ["$0", "$1", "$1", "$0"]) },

            // REBEL-2 V2.2's cycle-up gate; canonical REBEL-6 name is CYU.T (Ztl).
            { "CYCLEUP.T", new PseudoExpansion("CYU.T", 2, ["$0", "$1"]) },

            // Signed shift amounts (errata E-4): the old direction-split shift mnemonics live on
            // as pseudos over the signed-amount family. The left forms copy their operands 1:1;
            // the immediate right forms negate the amount ($-2), which the balanced encoding
            // expresses for free. The register right shifts SR{N,Z,P}.T are retired outright —
            // an assembler cannot negate a runtime value (materialize a negative amount with
            // STI.T and use SH<f>.T).
            { "SLN.T",  new PseudoExpansion("SHN.T",  3, ["$0", "$1", "$2"]) },
            { "SLZ.T",  new PseudoExpansion("SHZ.T",  3, ["$0", "$1", "$2"]) },
            { "SLP.T",  new PseudoExpansion("SHP.T",  3, ["$0", "$1", "$2"]) },
            { "SLIN.T", new PseudoExpansion("SHIN.T", 3, ["$0", "$1", "$2"]) },
            { "SLIZ.T", new PseudoExpansion("SHIZ.T", 3, ["$0", "$1", "$2"]) },
            { "SLIP.T", new PseudoExpansion("SHIP.T", 3, ["$0", "$1", "$2"]) },
            { "SRIN.T", new PseudoExpansion("SHIN.T", 3, ["$0", "$1", "$-2"]) },
            { "SRIZ.T", new PseudoExpansion("SHIZ.T", 3, ["$0", "$1", "$-2"]) },
            { "SRIP.T", new PseudoExpansion("SHIP.T", 3, ["$0", "$1", "$-2"]) },
        };

    // -------------------------------------------------------------------------
    // CSR name table (Zicsr) — standard RISC-V numbers, resolved by the encoder
    // before label lookup. The shim maps them onto the negative-range trap
    // registers; see docs/rebel6-platform.md.
    // -------------------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, int> CsrNames =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "sstatus",  0x100 }, { "sie",      0x104 }, { "stvec",    0x105 },
            { "sscratch", 0x140 }, { "sepc",     0x141 }, { "scause",   0x142 },
            { "sip",      0x144 },
            { "mstatus",  0x300 }, { "medeleg",  0x302 }, { "mideleg",  0x303 },
            { "mie",      0x304 }, { "mtvec",    0x305 },
            { "mscratch", 0x340 }, { "mepc",     0x341 }, { "mcause",   0x342 },
            { "mip",      0x344 },
            { "cycle",    0xC00 }, { "time",     0xC01 }, { "instret",  0xC02 },
            { "cycleh",   0xC80 }, { "instreth", 0xC82 },
            { "mhartid",  0xF14 },
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
