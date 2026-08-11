namespace TernaryWorkbench.RebelAssembler.Assembly.Models;

/// <summary>
/// A resolved label. Code labels hold an instruction index (I-space); data labels hold a tryte
/// address (D-space). The two units are not interchangeable under the Harvard split, so
/// <see cref="IsData"/> travels with the value.
/// </summary>
internal sealed record LabelDefinition(int InstructionIndex, int LineNumber, bool IsData = false);
