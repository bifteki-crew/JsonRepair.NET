using System.Buffers;
using System.Text;
using FluentAssertions;
using JsonRepair.Tests.TestCases;
using Xunit;

namespace JsonRepair.Tests.Fuzzing;

/// <summary>
/// Pins a divergence between the string and UTF-8 engines that <see cref="FuzzTests"/> surfaced, so the
/// current behaviour is visible and any change to it is deliberate rather than accidental.
/// </summary>
/// <remarks>
/// <para>
/// The UTF-8 engine reassembles literals and numbers that whitespace has split, where the string engine
/// rejects the input. For numbers that means <b>fusing two tokens into a third</b>: <c>"8 67"</c> repairs
/// to <c>867</c> and <c>"-2 7.241"</c> to <c>-27.241</c>. The output is valid JSON, so the valid-or-throw
/// contract cannot catch it, but the number it reports was never in the input.
/// </para>
/// <para>
/// The divergence is not limited to root-level values — it reaches inside objects and arrays too, which is
/// why <see cref="BothEngines_ShouldDivergeOnlyInTheKnownDirection"/> checks the direction rather than the
/// shape of the input. These tests assert today's behaviour, not the desired behaviour: choosing between
/// throwing, taking the first value, and fusing is a repair-semantics decision that wants an upstream
/// comparison first.
/// </para>
/// </remarks>
public class KnownEngineDivergenceTests
{
    public static TheoryData<string, string> Utf8LenientCases => new()
    {
        { "8 67", "867" },                    // two numbers fused into a third
        { "-2 7.241", "-27.241" },            // fusion across a negative sign
        { "-8\n93", "-893" },                 // fusion across a newline
        { "536,", "536" },                    // trailing comma dropped
        { "n ull", "null" },                  // literal reassembled across a space
        { "fal\nse", "false" },               // literal reassembled across a newline
        { "{\"a\": n ull}", "{\"a\":null}" }, // and the same inside an object
    };

    [Theory]
    [MemberData(nameof(Utf8LenientCases))]
    public void Utf8Engine_ShouldReassembleWhitespaceSplitTokens(string input, string expected)
    {
        var writer = new ArrayBufferWriter<byte>();
        JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);

        Encoding.UTF8.GetString(writer.WrittenSpan).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Utf8LenientCases))]
    public void StringEngine_ShouldRejectWhitespaceSplitTokens(string input, string unusedExpected)
    {
        _ = unusedExpected;
        Action act = () => JsonRepairEngine.Repair(input);

        act.Should().Throw<JsonRepairException>();
    }

    /// <summary>
    /// The invariant that still holds and that <see cref="FuzzTests.Fuzz_BothEnginesShouldAgree"/> enforces
    /// across thousands of generated inputs: the engines may only diverge by the UTF-8 side being more
    /// lenient. The UTF-8 engine rejecting what the string engine repairs, or the two repairing to
    /// different JSON, is a real defect.
    /// </summary>
    [Theory]
    [InlineData("{\"a\": 8 67}")]
    [InlineData("[8 67]")]
    [InlineData("[1 2]")]
    [InlineData("[\"a\" \"b\"]")]
    [InlineData("{foo: 'bar', tags: ['a', 'b',]}")]
    [InlineData("```json\n{\"a\": 1}\n```")]
    public void BothEngines_ShouldDivergeOnlyInTheKnownDirection(string input)
    {
        string? viaString = Repair(() => JsonRepairEngine.Repair(input));
        string? viaUtf8 = Repair(() => {
            var writer = new ArrayBufferWriter<byte>();
            JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);
            return Encoding.UTF8.GetString(writer.WrittenSpan);
        });

        if (viaString is null) {
            return; // the known direction: the string engine is the stricter of the two
        }

        viaUtf8.Should().NotBeNull(because: "the utf8 engine must not reject what the string engine repairs");

        // Compared semantically: insignificant whitespace differs between engines ([8 ,67] vs [8,67])
        JsonSemanticEquality.Equals(viaString, viaUtf8!, out string reason)
            .Should().BeTrue(because: $"both engines repaired this, so they must agree ({reason})");
    }

    private static string? Repair(Func<string> repair)
    {
        try {
            return repair();
        }
        catch (JsonRepairException) {
            return null;
        }
    }
}
