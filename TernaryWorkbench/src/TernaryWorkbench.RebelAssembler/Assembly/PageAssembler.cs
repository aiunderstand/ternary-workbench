using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class PageAssembler
{
    /// <summary>Assemble a block of REBEL-2 assembly using the default encoder and address space.</summary>
    public static IReadOnlyList<AssembledInstruction> AssemblePage(string assembly, bool padPage, string padInstruction, IReadOnlyDictionary<string, InstructionPattern>? patterns = null)
    {
        var instructions = new List<AssembledInstruction>(PageInstructionCount);
        var parsed = InstructionParser.ParsePage(assembly, PageInstructionCount, RegisterDictionary);

        foreach (var parsedInstruction in parsed.Instructions)
        {
            var machineCode = InstructionEncoder.Translate(parsedInstruction, parsed.Labels, patterns);
            instructions.Add(new AssembledInstruction(
                instructions.Count,
                AddressSpace[instructions.Count],
                parsedInstruction.Text,
                machineCode));
        }

        if (!padPage || instructions.Count >= PageInstructionCount)
            return instructions;

        var padMachineCode = InstructionEncoder.Translate(padInstruction, patterns);
        while (instructions.Count < PageInstructionCount)
        {
            var address = AddressSpace[instructions.Count];
            instructions.Add(new AssembledInstruction(
                instructions.Count,
                address,
                padInstruction,
                padMachineCode));
        }

        return instructions;
    }

    /// <summary>
    /// Assemble a block of assembly using a custom encoder delegate and address space.
    /// The encoder delegate receives (instruction, labels, patterns, currentInstructionIndex).
    /// The page capacity is the address space length; the register dictionary guards labels.
    /// </summary>
    public static IReadOnlyList<AssembledInstruction> AssemblePage(
        string assembly,
        bool padPage,
        string padInstruction,
        IReadOnlyDictionary<string, InstructionPattern> patterns,
        Func<ParsedInstruction, IReadOnlyDictionary<string, LabelDefinition>?, IReadOnlyDictionary<string, InstructionPattern>?, int, string> encoder,
        string[] addressSpace,
        IReadOnlyDictionary<string, string> registerDictionary)
    {
        var instructions = new List<AssembledInstruction>(addressSpace.Length);
        var parsed       = InstructionParser.ParsePage(assembly, addressSpace.Length, registerDictionary);

        foreach (var parsedInstruction in parsed.Instructions)
        {
            var currentIndex = instructions.Count;
            var machineCode  = encoder(parsedInstruction, parsed.Labels, patterns, currentIndex);
            instructions.Add(new AssembledInstruction(
                currentIndex,
                addressSpace[currentIndex],
                parsedInstruction.Text,
                machineCode));
        }

        if (!padPage || instructions.Count >= addressSpace.Length)
            return instructions;

        var padMachineCode = encoder(InstructionParser.ParsePage(padInstruction, addressSpace.Length, registerDictionary).Instructions[0], null, patterns, 0);
        while (instructions.Count < addressSpace.Length)
        {
            var currentIndex = instructions.Count;
            instructions.Add(new AssembledInstruction(
                currentIndex,
                addressSpace[currentIndex],
                padInstruction,
                padMachineCode));
        }

        return instructions;
    }
}
