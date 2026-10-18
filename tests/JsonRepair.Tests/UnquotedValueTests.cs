using System.Buffers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using JsonRepair.Tests.TestCases;
using Xunit;

namespace JsonRepair.Tests;

/// <summary>
/// Unquoted string values are quoted on both engines when they sit inside an object or array.
/// Root prose is not quoted. Keys stay identifiers, and true/false/null/None stay literals
/// only when a real token boundary follows them. <c>8 67</c> is two numbers and still throws.
/// </summary>
public class UnquotedValueTests
{
    [Theory]
    [InlineData("{a: hello}", "{\"a\":\"hello\"}")]
    [InlineData("{greeting: hello world}", "{\"greeting\":\"hello world\"}")]
    [InlineData("{a: TrueStuff}", "{\"a\":\"TrueStuff\"}")]
    [InlineData("{a: hello, b: 1}", "{\"a\":\"hello\",\"b\":1}")]
    [InlineData("[hello]", "[\"hello\"]")]
    [InlineData("[a, b]", "[\"a\",\"b\"]")]
    [InlineData("{a: {b: hello}}", "{\"a\":{\"b\":\"hello\"}}")]
    [InlineData("{a: hel", "{\"a\":\"hel\"}")]
    [InlineData("{a: {b: hel", "{\"a\":{\"b\":\"hel\"}}")]
    [InlineData("{a: hello   world}", "{\"a\":\"hello   world\"}")]
    [InlineData("[foo 2 bar]", "[\"foo 2 bar\"]")]
    [InlineData("{a: false$}", "{\"a\":\"false$\"}")]
    [InlineData("{a: trueble}", "{\"a\":\"trueble\"}")]
    [InlineData("{greeting: hello world!}", "{\"greeting\":\"hello world!\"}")]
    [InlineData("{a: true}", "{\"a\":true}")]
    [InlineData("{a: false}", "{\"a\":false}")]
    [InlineData("{a: null}", "{\"a\":null}")]
    [InlineData("{a: None}", "{\"a\":null}")]
    [InlineData("{a: True}", "{\"a\":true}")]
    [InlineData("{a: False}", "{\"a\":false}")]
    [InlineData("{a: undefined}", "{\"a\":null}")]
    [InlineData("{true: 1}", "{\"true\":1}")]
    [InlineData("{None: 1}", "{\"None\":1}")]
    [InlineData("[true, false, null]", "[true,false,null]")]
    [InlineData("[hello world]", "[\"hello world\"]")]
    [InlineData("{url:https://www.bible.com/}", "{\"url\":\"https://www.bible.com/\"}")]
    [InlineData("{url:https://example.com/a//b,id:1}", "{\"url\":\"https://example.com/a//b\",\"id\":1}")]
    [InlineData("{url:https://example.com/a%20b}", "{\"url\":\"https://example.com/a%20b\"}")]
    [InlineData("{url:https://example.com/search?q=a%20b&x=1}", "{\"url\":\"https://example.com/search?q=a%20b&x=1\"}")]
    [InlineData("{a: null@example.com}", "{\"a\":\"null@example.com\"}")]
    [InlineData("[true\u00A9]", "[\"true\u00A9\"]")]
    [InlineData("{a: b c: d}", "{\"a\":\"b c: d\"}")]
    [InlineData("{$ref: 1}", "{\"$ref\":1}")]
    [InlineData("{foo$: 1}", "{\"foo$\":1}")]
    [InlineData("{2:2}", "{\"2\":2}")]
    [InlineData("{\"date\":2024-10-18T18:35:22.229Z}", "{\"date\":\"2024-10-18T18:35:22.229Z\"}")]
    public void BothEngines_ShouldQuoteUnquotedValues(string input, string expected)
    {
        string viaString = JsonRepairEngine.Repair(input);
        viaString.Should().Be(expected);
        Parse(viaString);

        string viaUtf8 = RepairUtf8(input);
        viaUtf8.Should().Be(expected);
        Parse(viaUtf8);
    }

    [Theory]
    [InlineData("{a: hello   }", "{\"a\":\"hello\"}")]
    [InlineData("{a: hello\t}", "{\"a\":\"hello\"}")]
    [InlineData("{greeting: hello world\n}", "{\"greeting\":\"hello world\"}")]
    public void BothEngines_ShouldTrimTrailingWhitespaceFromUnquotedValues(string input, string expected)
    {
        // The string engine may keep one structural space; comparison is semantic.
        JsonSemanticEquality.Equals(expected, JsonRepairEngine.Repair(input), out string stringReason)
            .Should().BeTrue(because: stringReason);
        JsonSemanticEquality.Equals(expected, RepairUtf8(input), out string utf8Reason)
            .Should().BeTrue(because: utf8Reason);
    }

    [Theory]
    [InlineData("I cannot help with that.")]
    [InlineData("hello   world")]
    [InlineData("foo 2 bar")]
    [InlineData("false$")]
    [InlineData("trueble")]
    [InlineData("n ull")]
    [InlineData("src/app.cs")]
    [InlineData("a/b")]
    [InlineData("C++")]
    [InlineData("{file: src/app.cs}")]
    [InlineData("{path: a/b}")]
    [InlineData("{lang: C++}")]
    [InlineData("{a: \u201Chi\u201D}")]
    [InlineData("{a: \u2018hi\u2019}")]
    [InlineData("{a: hello\u201Cworld}")]
    public void BothEngines_ShouldRejectRootProseAndValuesEndedBySlashPlusOrCurlyQuotes(string input)
    {
        Action viaString = () => JsonRepairEngine.Repair(input);
        viaString.Should().Throw<JsonRepairException>();
        JsonRepairEngine.TryRepair(input, out string? repaired).Should().BeFalse();
        repaired.Should().BeNull();

        byte[] bytes = Encoding.UTF8.GetBytes(input);
        var writer = new ArrayBufferWriter<byte>();
        Action viaUtf8 = () => JsonRepairEngine.Repair(bytes, writer);
        viaUtf8.Should().Throw<JsonRepairException>();
        writer.WrittenCount.Should().Be(0);

        var tryWriter = new ArrayBufferWriter<byte>();
        JsonRepairEngine.TryRepair(bytes, tryWriter).Should().BeFalse();
        tryWriter.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void BothEngines_ShouldNotFuseValuesAcrossALineComment()
    {
        const string input = "[hello//comment\nworld]";
        const string expected = "[\"hello\",\"world\"]";

        JsonSemanticEquality.Equals(expected, JsonRepairEngine.Repair(input), out string stringReason)
            .Should().BeTrue(because: stringReason);
        JsonSemanticEquality.Equals(expected, RepairUtf8(input), out string utf8Reason)
            .Should().BeTrue(because: utf8Reason);
    }

    [Fact]
    public void BothEngines_ShouldTreatNoBreakSpaceAfterANumberAsWhitespace()
    {
        const string input = "[1\u00A0]";
        const string expected = "[1]";

        JsonSemanticEquality.Equals(expected, JsonRepairEngine.Repair(input), out string stringReason)
            .Should().BeTrue(because: stringReason);
        JsonSemanticEquality.Equals(expected, RepairUtf8(input), out string utf8Reason)
            .Should().BeTrue(because: utf8Reason);
    }

    [Theory]
    [InlineData("8 67")]
    [InlineData("{\"a\": 8 67}")]
    [InlineData("-2 7.241")]
    public void BothEngines_ShouldStillRejectWhitespaceSplitNumbers(string input)
    {
        Action viaString = () => JsonRepairEngine.Repair(input);
        viaString.Should().Throw<JsonRepairException>();

        Action viaUtf8 = () => RepairUtf8(input);
        viaUtf8.Should().Throw<JsonRepairException>();
    }

    [Fact]
    public void Repair_ShouldEscapeBackslashInsideUnquotedValue()
    {
        const string input = "{a: hello\\world}";
        const string expected = "{\"a\":\"hello\\\\world\"}";

        JsonRepairEngine.Repair(input).Should().Be(expected);
        RepairUtf8(input).Should().Be(expected);
    }

    private static string RepairUtf8(string input)
    {
        var writer = new ArrayBufferWriter<byte>();
        JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    private static void Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().NotBe(JsonValueKind.Undefined);
    }
}
