using System.Buffers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using JsonRepair.Tests.TestCases;
using Xunit;

namespace JsonRepair.Tests;

/// <summary>
/// Unquoted string values are quoted on both engines. Keys stay identifiers, and
/// true/false/null/None stay literals. <c>8 67</c> is two numbers and still throws.
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
    [InlineData("hello   world", "\"hello   world\"")]
    [InlineData("foo 2 bar", "\"foo 2 bar\"")]
    [InlineData("false$", "\"false$\"")]
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
    [InlineData("trueble", "\"trueble\"")]
    [InlineData("[true, false, null]", "[true,false,null]")]
    [InlineData("[hello world]", "[\"hello world\"]")]
    [InlineData("{url:https://www.bible.com/}", "{\"url\":\"https://www.bible.com/\"}")]
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
