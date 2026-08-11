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
///   <item>122 instruction patterns: 50 ternary-native base + 27 binary (RV32I-compatible) + 37 extension (M, A, F, P, Ztl, Ztb, Zicsr — see docs/rebel6-extensions.md) + 8 pseudo patterns (NOP.T, MV.T, TDOT.T, NTI.T, PTI.T, MTI.T, CYU.T, CYD.T) + 13 operand-rewrite pseudos (BGT.T, BLE.T, SWAP.T, CYCLEUP.T, SL{N,Z,P}.T, SLI{N,Z,P}.T, SRI{N,Z,P}.T). 8 instruction formats: R, I, B, D, X (4-trit opcode), G, Y (2-trit opcode), L (RV32I pass-through).</item>
///   <item><b>ROT.T</b> is the cyclic shift (renamed from SC.T; docs/rebel6-isa.md errata E-3) — <b>SC.T</b> is the A extension's store-conditional. The A extension assembles the bare (relaxed) forms; aq/rl suffixed forms are reserved.</item>
///   <item><b>Zicsr</b> CSR numbers accept standard names (mstatus, mepc, …) or plain numerics; the zimm of the *I forms is written as a number and disassembles as the register name with the same encoding.</item>
///   <item><b>Register names</b>: architectural (<c>X0</c>, <c>X-11</c>, …), ABI (docs/rebel6-abi.md identity map — <c>zero</c>, <c>ra</c>, <c>sp</c>, <c>tp</c>, <c>t0</c>–<c>t6</c>, <c>s0</c>–<c>s11</c>/<c>fp</c>, <c>a0</c>–<c>a7</c>, extended pool <c>e0</c>–<c>e332</c>; <c>gp</c> is retired and rejected) and platform system/streaming names (docs/rebel6-platform.md standard layout — <c>mtvec</c> … <c>sip</c> at <c>X-1</c> … <c>X-22</c>, including <c>mcycle</c>, <c>minstret</c>, <c>stream0</c>–<c>stream2</c>).</item>
///   <item><b>Sections and data</b> (<see cref="AssembleProgram"/>): <c>.text</c>/<c>.data</c>/<c>.word</c>/<c>.zero</c>. Code labels resolve to instruction indices, data labels to tryte addresses (flat pre-linker layout after the 4-tryte-aligned instruction image, matching the R2R reference toolchain). <c>LI.T</c>/<c>LWA.T</c>/<c>SWA.T</c> take a symbol's absolute value; <c>JAL.T</c>/<c>AIPC.T</c> are PC-relative and reject data symbols (Harvard split).</item>
///   <item><b>12-trit immediates</b> (±265720) throughout I-type and B-type, giving the RV32I-parity binary instructions their full 12-bit immediate range. I-type splits it around the destination register (rs2 slot = imm[11:6], rd2 slot = imm[5:0]); B-type has no destination, so its 12 trits are contiguous across rd1+rd2.</item>
///   <item><b>Three-way branches</b> (B-type field read as two 6-trit displacements, ±364 each): <c>BCGS.T rs1, rs2, off1, off2</c> — greater → PC+off1, smaller → PC+off2, equal → PC+1; and <c>BCEG.T rs1, rs2, off1, off2</c> — equal → PC+off1, greater → PC+off2, smaller → PC+1. Two-way branches BEQ.T, BNE.T, BLT.T, BGE.T keep the full 12 trits; BGT.T and BLE.T are operand-swap pseudo-instructions. See docs/rebel6-isa.md errata E-1.</item>
///   <item><b>Indexed vs absolute word access</b>: <c>LW.T rd1, rs1, imm12</c> / <c>SW.T rs1, rs2, imm12</c> address rs1+imm12 (I/B-type), while <c>LWA.T rd1, imm24</c> / <c>SWA.T rs1, imm24</c> address imm24 directly with no base register (G/Y-type). The paper names both pairs lw.t/sw.t; the absolute forms are renamed. See docs/rebel6-isa.md errata E-2.</item>
///   <item><b>Signed shift amounts</b> (docs/rebel6-isa.md errata E-4): one shift family with direction from the sign of the balanced amount — <c>SH&lt;f&gt;.T</c> (register amount, low 4 trits of rs2) and <c>SHI&lt;f&gt;.T</c> (immediate); k &gt; 0 shifts toward the MST, k &lt; 0 toward the LST, |k| ≥ 24 is all-fill. Vacated trits are filled with <c>−</c>, <c>0</c> or <c>+</c>, selected by a single fill trit rather than by separate funcs — the rs2 slot (imm[11:6]) for immediate shifts, the rd2 slot for register shifts; mnemonic suffix N/Z/P names the fill, and func alone does not identify a shift. <c>ROT.T</c>/<c>ROTR.T</c> rotate by a signed immediate/register amount mod 24. The old SL*/SLI*/SRI* mnemonics assemble as pseudos over the signed forms (immediate right shifts negate the amount); the register right shifts SR{N,Z,P}.T are retired. 7 architectural shift instructions occupy 4 func slots.</item>
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
    /// Assemble a full REBEL-6 program: a <c>.text</c> instruction stream plus an optional
    /// <c>.data</c> image built from <c>.word</c> and <c>.zero</c> directives. Labels resolve
    /// across sections — code labels to instruction indices (I-space), data labels to tryte
    /// addresses (D-space, flat pre-linker layout after the instruction image, matching the
    /// R2R reference toolchain). Register operands accept the architectural <c>X</c> names,
    /// the ABI names (<c>sp</c>, <c>a0</c>, <c>t0</c>, …, extended pool <c>e0</c>…) and the
    /// platform system-register names (<c>mtvec</c> … <c>sip</c>, <c>mcycle</c>,
    /// <c>minstret</c>, <c>stream0</c>–<c>stream2</c>).
    /// </summary>
    public static Rebel6Program AssembleProgram(string assembly) =>
        ProgramAssembler6.Assemble(assembly);

    /// <summary>
    /// Assemble a block of REBEL-6 assembly into a list of <see cref="AssembledInstruction"/> records.
    /// Labels are resolved within the block. Directives are accepted (see
    /// <see cref="AssembleProgram"/>); only the instruction stream is returned.
    /// </summary>
    public static IReadOnlyList<AssembledInstruction> AssembleInstructions(string assembly) =>
        ProgramAssembler6.Assemble(assembly).Instructions;

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
