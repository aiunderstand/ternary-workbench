using FluentAssertions;
using TernaryWorkbench.RebelAssembler;
using Xunit;
using Asm = TernaryWorkbench.RebelAssembler.Rebel2Assembler;

namespace TernaryWorkbench.Tests;

/// <summary>
/// Exhaustive tests for the REBEL-2 assembler and disassembler.
///
/// Machine-code format: opcode[0..1] Rs1[2..3] Rs2[4..5] Rd1[6..7] Rd2[8..9]
/// Register encoding:
///   X-4="--"  X-3="-0"  X-2="-+"  X-1="0-"  X0="00"
///   X1="0+"   X2="+-"   X3="+0"   X4="++"
///
/// Known-good values are cross-verified against the reference implementation
/// in MRCSStudio.REBEL2 (MRCS-Studio project).
/// </summary>
public class Rebel2AssemblerTests
{
    // =========================================================================
    // 1. Single-instruction assembly (Translate)
    //    One representative test per mnemonic family, plus register-variety combos.
    // =========================================================================

    [Theory]
    // ADD family
    [InlineData("ADD.T X1, X2, X3",       "--+-+00+00")]
    [InlineData("ADD.T X-4, X4, X-4",     "--++----00")]
    [InlineData("SUB.T X1, X2, X3",       "--+-+00+--")]
    // SUB.T rd1, rs1, rs2 with fixed Rd2="--"
    // X-4("--"), X4("+"), X-4("--") → "--" + "++" + "--" + "--" + "--" = "--++------"
    [InlineData("SUB.T X-4, X4, X-4",     "--++------")]
    [InlineData("STI.T X1, X3",           "--00+00+--")]
    // STI.T rd1, rs2 with fixed Rd2="--", Rs1=default="00"
    // X-4("--"), X4("+") → "--" + "00" + "++" + "--" + "--" = "--00++----"
    [InlineData("STI.T X-4, X4",          "--00++----")]
    // ADDi family
    [InlineData("ADDI.T X1, X2, ++",      "-0+-++0+00")]
    // ADDI.T rd1, rs1, imm → rd1=X-1("0-"), rs1=X1("0+"), imm="--" → "-0"+"0+"+"--"+"0-"+"00" = "-00+--0-00"
    [InlineData("ADDI.T X-1, X1, --",     "-00+--0-00")]
    [InlineData("NOP.T",                  "-000000000")]
    [InlineData("LI.T X1, ++",            "-000++0+00")]
    [InlineData("LI.T X-4, --",           "-000----00")]
    [InlineData("MV.T X1, X2",            "-0+-000+00")]
    [InlineData("MV.T X-4, X4",           "-0++00--00")]
    // ADDi2
    [InlineData("ADDI2.T X1, X2, ++, --", "-+++--0++-")]
    // MUDI
    [InlineData("MUL.T X1, X2, X3",       "0-+-+00+00")]
    [InlineData("MUL.T X-4, X4, X-4",     "0-++----00")]
    // MIMA
    [InlineData("MINW.T X1, X2, X3",      "00+-+00+--")]
    [InlineData("MINT.T X1, X2, X3",      "00+-+00+-0")]
    [InlineData("MAXW.T X1, X2, X3",      "00+-+00++-")]
    [InlineData("MAXT.T X1, X2, X3",      "00+-+00++0")]
    // SHI
    [InlineData("SLIN.T X1, X2, ++",      "0++-++0+--")]
    [InlineData("SLIZ.T X1, X2, ++",      "0++-++0+-0")]
    [InlineData("SLIP.T X1, X2, ++",      "0++-++0+-+")]
    [InlineData("SRIN.T X1, X2, ++",      "0++-++0++-")]
    [InlineData("SRIZ.T X1, X2, ++",      "0++-++0++0")]
    [InlineData("SRIP.T X1, X2, ++",      "0++-++0+++")]
    [InlineData("SC.T X1, X2, ++",        "0++-++0+00")]
    // COMP
    [InlineData("CMPW.T X1, X2, X3",      "+-+-+00+00")]
    [InlineData("CMPT.T X1, X2, X3",      "+-+-+00+--")]
    // BCEG
    [InlineData("BCEG.T X1, X2, X3, X4",  "+0+0++0++-")]
    // PCO
    [InlineData("JAL.T X1, ++",           "++00++0+0+")]
    [InlineData("JALR.T X1, X2, ++",      "+++-++0+00")]
    [InlineData("LPC.T X1, ++",           "++00++0+0-")]
    public void Translate_SingleInstruction_ProducesMachineCode(string assembly, string expected)
    {
        Asm.Translate(assembly).Should().Be(expected);
    }

    // Verify numeric immediates and trit-pair immediates produce the same result
    [Theory]
    [InlineData("ADDI.T X1, X2, 4",   "ADDI.T X1, X2, ++")]
    [InlineData("ADDI.T X1, X2, -4",  "ADDI.T X1, X2, --")]
    [InlineData("ADDI.T X1, X2, 0",   "ADDI.T X1, X2, 00")]
    [InlineData("ADDI.T X1, X2, 1",   "ADDI.T X1, X2, 0+")]
    [InlineData("ADDI.T X1, X2, -1",  "ADDI.T X1, X2, 0-")]
    public void Translate_NumericImmediate_SameAsTritPairImmediate(string withNumeric, string withTritPair)
    {
        Asm.Translate(withNumeric).Should().Be(Asm.Translate(withTritPair));
    }

    // Register X0 and X-0 are identical (both encode as "00")
    [Fact]
    public void Translate_X0AndX_0AreEquivalent()
    {
        Asm.Translate("ADD.T X0, X1, X2")
            .Should().Be(Asm.Translate("ADD.T X-0, X1, X2"));
    }

    // =========================================================================
    // 2. Disassembly: machine code → canonical mnemonic+operands
    //
    //    The disassembler always emits register names for all operand fields,
    //    including immediate slots (X-4..X4 for values -4..4).
    //    "00" fields are formatted as X-0 (first match in the register dictionary).
    // =========================================================================

    [Theory]
    [InlineData("--+-+00+00",  "ADD.T X1, X2, X3")]
    [InlineData("--+-+00+--",  "SUB.T X1, X2, X3")]
    [InlineData("--00+00+--",  "STI.T X1, X3")]
    [InlineData("-0+-++0+00",  "ADDI.T X1, X2, X4")]  // imm "++" → register X4
    [InlineData("-000000000",  "NOP.T")]
    [InlineData("-000++0+00",  "LI.T X1, X4")]         // imm "++" → X4
    [InlineData("-0+-000+00",  "MV.T X1, X2")]
    [InlineData("-+++--0++-",  "ADDI2.T X1, X2, X4, X-4")] // rs1="++" → X4, rs2="--" → X-4
    [InlineData("0-+-+00+00",  "MUL.T X1, X2, X3")]
    [InlineData("00+-+00+--",  "MINW.T X1, X2, X3")]
    [InlineData("00+-+00+-0",  "MINT.T X1, X2, X3")]
    [InlineData("00+-+00++-",  "MAXW.T X1, X2, X3")]
    [InlineData("00+-+00++0",  "MAXT.T X1, X2, X3")]
    [InlineData("0++-++0+--",  "SLIN.T X1, X2, X4")]   // imm → X4
    [InlineData("0++-++0+-0",  "SLIZ.T X1, X2, X4")]
    [InlineData("0++-++0+-+",  "SLIP.T X1, X2, X4")]
    [InlineData("0++-++0++-",  "SRIN.T X1, X2, X4")]
    [InlineData("0++-++0++0",  "SRIZ.T X1, X2, X4")]
    [InlineData("0++-++0+++",  "SRIP.T X1, X2, X4")]
    [InlineData("0++-++0+00",  "SC.T X1, X2, X4")]
    [InlineData("+-+-+00+00",  "CMPW.T X1, X2, X3")]
    [InlineData("+-+-+00+--",  "CMPT.T X1, X2, X3")]
    [InlineData("+0+0++0++-",  "BCEG.T X1, X2, X3, X4")]
    [InlineData("++00++0+0+",  "JAL.T X1, X4")]         // imm → X4
    [InlineData("+++-++0+00",  "JALR.T X1, X2, X4")]    // imm → X4
    [InlineData("++00++0+0-",  "LPC.T X1, X4")]         // imm → X4
    public void Disassemble_MachineCode_ReturnsCanonicalMnemonic(string machineCode, string expected)
    {
        Asm.Disassemble(machineCode).Should().Be(expected);
    }

    // The all-zero word "0000000000" has opcode "00" which matches MINW/MINT/MAXW/MAXT family —
    // it is NOT equivalent to NOP.T (which encodes as "-000000000").
    [Fact]
    public void Disassemble_NopT_MachineCode_IsNopT()
    {
        Asm.Disassemble("-000000000").Should().Be("NOP.T");
    }

    // =========================================================================
    // 3. Round-trip: assemble → disassemble → re-assemble → same machine code
    // =========================================================================

    [Theory]
    [InlineData("ADD.T X1, X2, X3")]
    [InlineData("SUB.T X1, X2, X3")]
    [InlineData("STI.T X1, X3")]
    [InlineData("ADDI.T X1, X2, ++")]
    [InlineData("NOP.T")]
    [InlineData("LI.T X1, ++")]
    [InlineData("MV.T X1, X2")]
    [InlineData("ADDI2.T X1, X2, ++, --")]
    [InlineData("MUL.T X1, X2, X3")]
    [InlineData("MINW.T X1, X2, X3")]
    [InlineData("MINT.T X1, X2, X3")]
    [InlineData("MAXW.T X1, X2, X3")]
    [InlineData("MAXT.T X1, X2, X3")]
    [InlineData("SLIN.T X1, X2, ++")]
    [InlineData("SLIZ.T X1, X2, ++")]
    [InlineData("SLIP.T X1, X2, ++")]
    [InlineData("SRIN.T X1, X2, ++")]
    [InlineData("SRIZ.T X1, X2, ++")]
    [InlineData("SRIP.T X1, X2, ++")]
    [InlineData("SC.T X1, X2, ++")]
    [InlineData("CMPW.T X1, X2, X3")]
    [InlineData("CMPT.T X1, X2, X3")]
    [InlineData("BCEG.T X1, X2, X3, X4")]
    [InlineData("JAL.T X1, ++")]
    [InlineData("JALR.T X1, X2, ++")]
    [InlineData("LPC.T X1, ++")]
    public void RoundTrip_Assemble_Disassemble_Reassemble_SameMachineCode(string assembly)
    {
        var machineCode   = Asm.Translate(assembly);
        var disassembled  = Asm.Disassemble(machineCode);
        var reassembled   = Asm.Translate(disassembled);

        reassembled.Should().Be(machineCode,
            because: $"re-assembling the disassembly of '{assembly}' should yield the same machine code");
    }

    // =========================================================================
    // 4. Multi-instruction page assembly (AssembleInstructions)
    // =========================================================================

    [Fact]
    public void AssembleInstructions_TwoInstructions_BothPresent()
    {
        const string source = """
            ADD.T X1, X2, X3
            NOP.T
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(2);
        result[0].MachineCode.Should().Be("--+-+00+00");
        result[0].Address.Should().Be("--");
        result[1].MachineCode.Should().Be("-000000000");
        result[1].Address.Should().Be("-0");
    }

    [Fact]
    public void AssembleInstructions_MaxNineInstructions_Succeeds()
    {
        var nineNops = string.Join("\n", Enumerable.Repeat("NOP.T", 9));
        var result = Asm.AssembleInstructions(nineNops);

        result.Should().HaveCount(9);
        result.All(r => r.MachineCode == "-000000000").Should().BeTrue();
        result[0].Address.Should().Be("--");
        result[8].Address.Should().Be("++");
    }

    [Fact]
    public void AssembleInstructions_AddressesMatchAddressSpace()
    {
        string[] expectedAddresses = ["--", "-0", "-+", "0-", "00", "0+", "+-", "+0", "++"];
        var nineNops = string.Join("\n", Enumerable.Repeat("NOP.T", 9));
        var result = Asm.AssembleInstructions(nineNops);

        for (int i = 0; i < 9; i++)
            result[i].Address.Should().Be(expectedAddresses[i]);
    }

    // =========================================================================
    // 5. Label resolution
    // =========================================================================

    [Fact]
    public void Labels_BackwardReference_ResolvesCorrectly()
    {
        const string source = """
            start:
            ADD.T X1, X2, X3
            JAL.T X0, start
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(2);
        result[0].MachineCode.Should().Be("--+-+00+00");

        // JAL.T X0, start → start=index 0, AddressSpace[0]="--"
        // "++" + "00" + "--" + "00" + "0+" = "++00--000+"
        result[1].MachineCode.Should().Be("++00--000+");
    }

    [Fact]
    public void Labels_ForwardReference_ResolvesCorrectly()
    {
        const string source = """
            JAL.T X0, end
            NOP.T
            end:
            ADD.T X1, X2, X3
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(3);

        // JAL.T X0, end → end=index 2, AddressSpace[2]="-+"
        // "++" + "00" + "-+" + "00" + "0+" = "++00-+000+"
        result[0].MachineCode.Should().Be("++00-+000+");
        result[1].MachineCode.Should().Be("-000000000");
        result[2].MachineCode.Should().Be("--+-+00+00");
    }

    [Fact]
    public void Labels_TwoLabelsOnSameInstruction_BothResolve()
    {
        const string source = """
            start: loop:
            ADD.T X1, X2, X3
            JAL.T X0, start
            JAL.T X1, loop
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(3);
        // Both start and loop resolve to index 0, AddressSpace[0]="--"
        // instruction 1: JAL.T X0, start → "++"+"00"+"--"+"00"+"0+" = "++00--000+"
        // instruction 2: JAL.T X1, loop  → "++"+"00"+"--"+"0+"+"0+" = "++00--0+0+"
        result[1].MachineCode.Should().Be("++00--000+");
        result[2].MachineCode.Should().Be("++00--0+0+");
    }

    [Fact]
    public void Labels_LabelOnSameLineAsInstruction_Resolves()
    {
        const string source = """
            loop: NOP.T
            JAL.T X0, loop
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(2);
        // loop → index 0, AddressSpace[0]="--"
        result[1].MachineCode.Should().Be("++00--000+");
    }

    [Fact]
    public void Labels_DanglingLabel_ThrowsInvalidOperationException()
    {
        const string source = """
            ADD.T X1, X2, X3
            dangling:
            """;
        var act = () => Asm.AssembleInstructions(source);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dangling*");
    }

    [Fact]
    public void Labels_UnknownLabel_ThrowsInvalidOperationException()
    {
        const string source = "JAL.T X0, nowhere";
        var act = () => Asm.Translate(source);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Labels_DuplicateLabel_ThrowsInvalidOperationException()
    {
        const string source = """
            label:
            NOP.T
            label:
            NOP.T
            """;
        var act = () => Asm.AssembleInstructions(source);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*label*");
    }

    [Fact]
    public void Labels_RegisterNameAsLabel_ThrowsInvalidOperationException()
    {
        const string source = """
            X1:
            NOP.T
            """;
        var act = () => Asm.AssembleInstructions(source);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*X1*");
    }

    // =========================================================================
    // 6. Error cases
    // =========================================================================

    [Fact]
    public void Error_UnknownMnemonic_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("BOGUS.T X1, X2");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BOGUS.T*");
    }

    [Fact]
    public void Error_WrongOperandCount_ThrowsInvalidOperationException()
    {
        // ADD.T expects 3 operands, give 2
        var act = () => Asm.Translate("ADD.T X1, X2");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_TooManyOperands_ThrowsInvalidOperationException()
    {
        // NOP.T expects 0 operands
        var act = () => Asm.Translate("NOP.T X1");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_ImmediateOutOfRange_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("ADDI.T X1, X2, 5");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*5*");
    }

    [Fact]
    public void Error_ImmediateOutOfRange_Negative_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Translate("ADDI.T X1, X2, -5");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*-5*");
    }

    [Fact]
    public void Error_MoreThanNineInstructions_ThrowsInvalidOperationException()
    {
        var tenNops = string.Join("\n", Enumerable.Repeat("NOP.T", 10));
        var act = () => Asm.AssembleInstructions(tenNops);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*9*");
    }

    [Fact]
    public void Error_Disassemble_WrongLength_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Disassemble("--+-+0");  // only 6 trits
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*10*");
    }

    [Fact]
    public void Error_Disassemble_InvalidCharacter_ThrowsInvalidOperationException()
    {
        var act = () => Asm.Disassemble("--+-+00+0X");  // 'X' is invalid
        act.Should().Throw<InvalidOperationException>();
    }

    // =========================================================================
    // 7. Comment and annotation stripping
    // =========================================================================

    [Theory]
    [InlineData("ADD.T X1, X2, X3 # hash comment")]
    [InlineData("ADD.T X1, X2, X3 ; semicolon comment")]
    [InlineData("ADD.T X1, X2, X3 $ dollar comment")]
    [InlineData("ADD.T X1, X2, X3 // double-slash comment")]
    public void Comments_LineComment_IsStripped(string assembly)
    {
        Asm.Translate(assembly).Should().Be("--+-+00+00");
    }

    [Fact]
    public void Comments_QuotedString_IsStripped()
    {
        // "NOP.T" with a quoted annotation after it
        Asm.Translate("NOP.T \"no-operation\"").Should().Be("-000000000");
    }

    [Fact]
    public void Comments_ParenthesisedAnnotation_IsStripped()
    {
        Asm.Translate("NOP.T (this is padding)").Should().Be("-000000000");
    }

    [Fact]
    public void Comments_MultipleCommentStyles_InPage_AllStripped()
    {
        const string source = """
            ADD.T X1, X2, X3  # first instruction
            NOP.T              ; second instruction (padding)
            """;
        var result = Asm.AssembleInstructions(source);

        result.Should().HaveCount(2);
        result[0].MachineCode.Should().Be("--+-+00+00");
        result[1].MachineCode.Should().Be("-000000000");
    }

    [Fact]
    public void Comments_BlankLinesAfterStripping_Ignored()
    {
        const string source = """

            # full-line comment

            ADD.T X1, X2, X3

            """;
        var result = Asm.AssembleInstructions(source);
        result.Should().HaveCount(1);
        result[0].MachineCode.Should().Be("--+-+00+00");
    }

    // =========================================================================
    // 8. DisassemblePage
    // =========================================================================

    [Fact]
    public void DisassemblePage_MultipleCodes_ReturnsOnePerCode()
    {
        string[] codes = ["--+-+00+00", "-000000000", "++00++0+0+"];
        var result = Asm.DisassemblePage(codes);

        result.Should().HaveCount(3);
        result[0].Should().Be("ADD.T X1, X2, X3");
        result[1].Should().Be("NOP.T");
        result[2].Should().Be("JAL.T X1, X4");
    }

    // =========================================================================
    // 9. CSV serialization / deserialization round-trip
    // =========================================================================

    [Fact]
    public void Csv_SerializeDeserialize_RoundTrip()
    {
        var records = new List<AssemblyRecord>
        {
            new("ADD.T X1, X2, X3",  "--+-+00+00", Isa.Rebel2, AssemblyDirection.Assemble),
            new("NOP.T",             "-000000000",  Isa.Rebel2, AssemblyDirection.Assemble),
            new("ADD.T X1, X2, X3",  "--+-+00+00", Isa.Rebel2, AssemblyDirection.Disassemble),
        };

        var csv = AssemblyCsvSerializer.Serialize(records);
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);

        errors.Should().BeEmpty();
        validRows.Should().HaveCount(3);
        validRows[0].Assembly.Should().Be("ADD.T X1, X2, X3");
        validRows[0].MachineCode.Should().Be("--+-+00+00");
        validRows[0].Isa.Should().Be(Isa.Rebel2);
        validRows[0].Direction.Should().Be(AssemblyDirection.Assemble);
        validRows[2].Direction.Should().Be(AssemblyDirection.Disassemble);
    }

    [Fact]
    public void Csv_EmptyInput_ReturnsEmpty()
    {
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(string.Empty);
        validRows.Should().BeEmpty();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Csv_HeaderOnly_ReturnsEmpty()
    {
        const string csv = "assembly;machine code;isa;direction";
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().BeEmpty();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Csv_WrongHeaderRow_ReturnsError()
    {
        const string csv = "wrong;header;format";
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().BeEmpty();
        errors.Should().HaveCount(1);
    }

    [Fact]
    public void Csv_InvalidMachineCode_RowSkippedWithError()
    {
        const string csv = """
            assembly;machine code;isa;direction
            NOP.T;BADCODEHERE;REBEL-2;assemble
            """;
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().BeEmpty();
        errors.Should().HaveCount(1);
        errors[0].RowNumber.Should().Be(2);
    }

    [Fact]
    public void Csv_InvalidIsaField_RowSkippedWithError()
    {
        const string csv = """
            assembly;machine code;isa;direction
            NOP.T;-000000000;REBEL-99;assemble
            """;
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().BeEmpty();
        errors.Should().HaveCount(1);
    }

    [Fact]
    public void Csv_InvalidDirectionField_RowSkippedWithError()
    {
        const string csv = """
            assembly;machine code;isa;direction
            NOP.T;-000000000;REBEL-2;sideways
            """;
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().BeEmpty();
        errors.Should().HaveCount(1);
    }

    [Fact]
    public void Csv_FieldsWithSemicolonsAreQuoted_RoundTrip()
    {
        // Assembly text containing a semicolon (comment char) — must survive CSV quoting
        var records = new List<AssemblyRecord>
        {
            new("ADD.T X1, X2, X3; a comment in assembly field",
                "--+-+00+00", Isa.Rebel2, AssemblyDirection.Assemble),
        };

        var csv = AssemblyCsvSerializer.Serialize(records);
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);

        errors.Should().BeEmpty();
        validRows[0].Assembly.Should().Be("ADD.T X1, X2, X3; a comment in assembly field");
    }

    [Fact]
    public void Csv_MixedValidAndInvalidRows_ReturnsPartialResults()
    {
        const string csv = """
            assembly;machine code;isa;direction
            ADD.T X1, X2, X3;--+-+00+00;REBEL-2;assemble
            NOP.T;BADCODE1234;REBEL-2;assemble
            NOP.T;-000000000;REBEL-2;assemble
            """;
        var (validRows, errors) = AssemblyCsvSerializer.Deserialize(csv);
        validRows.Should().HaveCount(2);
        errors.Should().HaveCount(1);
    }

    // =========================================================================
    // 10. ISA and direction constants
    // =========================================================================

    [Fact]
    public void Isa_Constants_HaveExpectedValues()
    {
        Isa.Rebel2.Should().Be("REBEL-2");
        Isa.Rebel6.Should().Be("REBEL-6");
    }

    [Fact]
    public void AssemblyDirection_Values_ExistAndDistinct()
    {
        AssemblyDirection.Assemble.Should().NotBe(AssemblyDirection.Disassemble);
    }
}
