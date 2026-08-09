namespace TernaryWorkbench.RebelAssembler.Assembly.Models;

/// <summary>
/// An assembler-level pseudo-instruction that rewrites to an architectural instruction.
/// <para>
/// <paramref name="Template"/> lists the operands handed to <paramref name="Target"/>. An entry of
/// the form <c>$n</c> copies source operand <c>n</c> (so operands may be reordered or duplicated);
/// an entry of the form <c>$-n</c> copies source operand <c>n</c> <b>negated</b> — a numeric
/// operand flips sign, a trit-string operand is tritwise inverted (used by the immediate right
/// shifts, whose signed replacement takes the negated amount — errata E-4). Any other entry is a
/// literal operand. A literal <c>1</c> is the fall-through displacement, used to steer a
/// comparison outcome to PC+1.
/// </para>
/// <para>
/// Example: <c>BLE.T rs1, rs2, L</c> → <c>BCEG.T rs2, rs1, L, L</c> is
/// <c>Template = ["$1", "$0", "$2", "$2"]</c>.
/// </para>
/// </summary>
internal sealed record PseudoExpansion(
    string Target,
    int OperandCount,
    IReadOnlyList<string> Template);
