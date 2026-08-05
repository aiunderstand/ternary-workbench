using TernaryWorkbench.RebelAssembler.Assembly;
using TernaryWorkbench.RebelAssembler.Assembly.Models;

namespace TernaryWorkbench.RebelAssembler;

/// <summary>
/// Public facade for assembling and disassembling REBEL-6 programs.
/// </summary>
/// <remarks>
/// REBEL-6 is the successor to REBEL-2 for real-world applications. Key differences:
/// <list type="bullet">
///   <item>Instructions are 32 trits wide (vs 10 trits in REBEL-2).</item>
///   <item>729 registers (6-trit address, vs 9 registers in REBEL-2); X0 hardwired zero.</item>
///   <item>69 instruction patterns: 34 ternary-native + 27 binary (RV32I-compatible) + 8 pseudo (NOP.T, MV.T and the six two-way comparison branches). 8 instruction formats: R, I, B, D, X (4-trit opcode), G, Y (2-trit opcode), L (RV32I pass-through).</item>
///   <item><b>Three-way branches</b> (B-type, both displacement slots used): <c>BCGS.T rs1, rs2, off1, off2</c> — greater → PC+off1, smaller → PC+off2, equal → PC+1; and <c>BCEG.T rs1, rs2, off1, off2</c> — equal → PC+off1, greater → PC+off2, smaller → PC+1. These are the only architectural ternary branches. BEQ.T, BNE.T, BLT.T, BGT.T, BGE.T and BLE.T are pseudo-instructions over them. See docs/rebel6-isa.md errata E-1.</item>
///   <item>Dedicated 4-trit func field (not encoded in Rd2 as in REBEL-2).</item>
///   <item>Register width 24 trits (vs 2 trits in REBEL-2).</item>
///   <item><b>NOP.T</b> encodes as all-zero 32 trits (opcode <c>0000</c>, func <c>0000</c>).</item>
///   <item><b>Opcode groups</b> by last 2 trits: <c>xx00</c> = Base Ternary; <c>xx-0</c> = Base Binary (RV32I); <c>xx+0</c> = Extensions. Last trit ≠ 0 = 2-trit long-immediate opcode.</item>
///   <item><b>Reserved</b>: 2-trit opcode <c>--</c> is reserved (not assigned to any instruction).</item>
///   <item><b>Full RV32I binary compatibility</b> (L-type): a hardware flag enables direct execution of native RV32I 32-bit instructions without recompilation. Binary is a subset of ternary — 32 bits fit exactly in 32 trits by using only the extremes (+/−). A hardware binary-ternary ALU and instruction translator handle the mapping transparently.</item>
/// </list>
/// </remarks>
public static class Rebel6Assembler
{
    // -------------------------------------------------------------------------
    // ISA metadata
    // -------------------------------------------------------------------------

    /// <summary>The 729 valid instruction addresses in 6-trit balanced ternary, ordered by integer value.</summary>
    public static string[] AddressSpace => InstructionSet6.AddressSpace;

    // -------------------------------------------------------------------------
    // Assembly
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assemble a block of REBEL-6 assembly into a list of <see cref="AssembledInstruction"/> records.
    /// Labels are resolved within the block.
    /// </summary>
    public static IReadOnlyList<AssembledInstruction> AssembleInstructions(string assembly) =>
        PageAssembler.AssemblePage(
            assembly,
            padPage: false,
            InstructionSet6.DefaultPaddingInstruction,
            InstructionSet6.Patterns,
            (inst, labels, pats, currentIdx) => InstructionEncoder6.Translate(inst, labels, pats, currentIdx),
            InstructionSet6.AddressSpace);

    /// <summary>
    /// Translate a single REBEL-6 assembly instruction string into a 32-trit machine code string.
    /// </summary>
    public static string Translate(string instruction) =>
        InstructionEncoder6.Translate(instruction, InstructionSet6.Patterns);

    // -------------------------------------------------------------------------
    // Disassembly
    // -------------------------------------------------------------------------

    /// <summary>
    /// Disassemble a single 32-trit machine code string into a mnemonic+operands string.
    /// </summary>
    public static string Disassemble(string machineCode) =>
        InstructionDisassembler6.Disassemble(machineCode, InstructionSet6.Patterns);

    /// <summary>
    /// Disassemble a sequence of 32-trit machine code strings into mnemonic+operands strings.
    /// </summary>
    public static IReadOnlyList<string> DisassemblePage(IEnumerable<string> machineCodes) =>
        [.. machineCodes.Select(mc => InstructionDisassembler6.Disassemble(mc, InstructionSet6.Patterns))];
}
