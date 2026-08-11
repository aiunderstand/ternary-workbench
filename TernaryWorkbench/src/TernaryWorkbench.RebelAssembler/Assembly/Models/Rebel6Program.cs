namespace TernaryWorkbench.RebelAssembler.Assembly.Models;

/// <summary>
/// One assembled data item (a <c>.word</c> or <c>.zero</c> directive): its absolute D-space
/// tryte address, source text, and the emitted trytes in address order (little-endian —
/// the lowest address holds the least significant tryte).
/// </summary>
public sealed record AssembledDatum(int Address, string Assembly, IReadOnlyList<string> Trytes);

/// <summary>
/// A fully assembled REBEL-6 program: the <c>.text</c> instruction stream and the <c>.data</c>
/// image. <see cref="DataBaseAddress"/> is the D-space tryte address of the first data item,
/// following the flat pre-linker layout the R2R reference toolchain uses: the data image sits
/// after the instruction image, whose tryte size is rounded up to the next 4-tryte boundary
/// (strictly greater when already aligned). Revisited in Phase B when REBEL-ld introduces the
/// I/D address-space split.
/// </summary>
public sealed record Rebel6Program(
    IReadOnlyList<AssembledInstruction> Instructions,
    IReadOnlyList<AssembledDatum> Data,
    int DataBaseAddress);
