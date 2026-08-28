using System.Buffers;
using System.Text;
using FluentAssertions;
using JsonRepair.Tests.TestCases;
using Xunit;

namespace JsonRepair.Tests.Fuzzing;

/// <summary>
/// Records where the string and UTF-8 engines agree and where they still differ, for cases the fuzz
/// harness surfaced. <see cref="FuzzTests.Fuzz_BothEnginesShouldAgree"/> enforces the same rules across
/// thousands of generated inputs; these pin the concrete examples.
/// </summary>
public class EngineAgreementTests
{
    /// <summary>
    /// Inputs where dropped whitespace used to fuse two tokens into a value that was never in the input:
    /// the UTF-8 engine turned <c>"8 67"</c> into <c>867</c> and <c>"n ull"</c> into <c>null</c>. Both
    /// engines must reject these.
    /// </summary>
    public static TheoryData<string> FusionCandidates => new()
    {
        "8 67",           // two numbers
        "-2 7.241",       // across a negative sign
        "-8\n93",         // across a newline
        "1 89.402",       // integer then decimal
        "n ull",          // a literal split by a space
        "fal\nse",        // a literal split by a newline
        "{\"a\": n ull}", // and the same in value position inside an object
    };

    /// <summary>
    /// Regression guard for the token fusion the fuzz harness found. Neither engine may invent a number
    /// or literal by running two tokens together. Upstream agrees: josdejong throws on these and
    /// mangiucugna reports failure — neither ever fuses.
    /// </summary>
    [Theory]
    [MemberData(nameof(FusionCandidates))]
    public void NeitherEngine_ShouldFuseWhitespaceSplitTokens(string input)
    {
        Action viaString = () => JsonRepairEngine.Repair(input);
        viaString.Should().Throw<JsonRepairException>(because: "the string engine must not fuse tokens");

        Action viaUtf8 = () => {
            var writer = new ArrayBufferWriter<byte>();
            JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);
        };
        viaUtf8.Should().Throw<JsonRepairException>(because: "the utf8 engine must not fuse tokens either");
    }

    /// <summary>
    /// One of two remaining divergences. After a root-level primitive the UTF-8 engine drops a trailing
    /// comma, which is what josdejong does (<c>"536,"</c> repairs to <c>536</c>); the string engine
    /// rejects the input instead. So this is the string engine lagging upstream, not the UTF-8 engine
    /// overreaching, and it closes when root-level trailing commas land in Tier 3.
    /// <para>
    /// The other runs the opposite way: the string engine repairs non-ASCII whitespace (NBSP, U+3000)
    /// that the UTF-8 engine rejects, covering 6 corpus cases. Both are recorded in docs/UPSTREAM.md.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("536,", "536")]
    [InlineData("267.269,", "267.269")]
    [InlineData("\"abc\",", "\"abc\"")]
    [InlineData("true,", "true")]
    public void Utf8Engine_ShouldDropTrailingCommaAfterRootPrimitive(string input, string expected)
    {
        var writer = new ArrayBufferWriter<byte>();
        JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);
        Encoding.UTF8.GetString(writer.WrittenSpan).Should().Be(expected);

        Action viaString = () => JsonRepairEngine.Repair(input);
        viaString.Should().Throw<JsonRepairException>(because: "the string engine does not yet repair this");
    }

    /// <summary>
    /// The standing invariant: the engines may only differ by the UTF-8 side being the more lenient one.
    /// The UTF-8 engine rejecting what the string engine repairs, or the two repairing to different JSON,
    /// is a defect.
    /// </summary>
    [Theory]
    [InlineData("[8 67]")]
    [InlineData("[1 2]")]
    [InlineData("[\"a\" \"b\"]")]
    [InlineData("{\"a\": 8 67}")]
    [InlineData("{foo: 'bar', tags: ['a', 'b',]}")]
    [InlineData("```json\n{\"a\": 1}\n```")]
    [InlineData("{crew: 'Bifteki', grilled: True, secret: None,}")]
    public void BothEngines_ShouldDivergeOnlyInTheKnownDirection(string input)
    {
        string? viaString = Repair(() => JsonRepairEngine.Repair(input));
        string? viaUtf8 = Repair(() => {
            var writer = new ArrayBufferWriter<byte>();
            JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);
            return Encoding.UTF8.GetString(writer.WrittenSpan);
        });

        if (viaString is null) {
            return; // permitted direction: the string engine is the stricter of the two
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
