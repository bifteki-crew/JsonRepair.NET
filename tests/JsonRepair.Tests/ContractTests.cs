using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace JsonRepair.Tests;

/// <summary>
/// Tests for the 0.2.0 valid-or-throw contract: Repair either returns valid JSON
/// or throws JsonRepairException; TryRepair reports success without throwing.
/// </summary>
public class ContractTests
{
    [Theory]
    [InlineData("{a: hello}")] // unquoted string value (Tier 3)
    [InlineData("[\"y\"\\, \"z\"]")] // stray backslash after string
    [InlineData("{\"a\": 1-2}")] // invalid number pass-through
    [InlineData("}")] // lone closer
    public void Repair_ShouldThrowJsonRepairException_WhenUnrepairable(string input)
    {
        Action act = () => JsonRepairEngine.Repair(input);

        act.Should().Throw<JsonRepairException>()
            .Which.Should().BeAssignableTo<JsonException>(because: "JsonRepairException inherits JsonException so existing catch clauses keep working");
    }

    [Theory]
    [InlineData("{a: hello}")]
    [InlineData("[\"y\"\\, \"z\"]")]
    public void Repair_Utf8_ShouldThrowJsonRepairException_AndWriteNothing_WhenUnrepairable(string inputStr)
    {
        byte[] input = Encoding.UTF8.GetBytes(inputStr);
        var writer = new ArrayBufferWriter<byte>();

        Action act = () => JsonRepairEngine.Repair(input.AsSpan(), writer);

        act.Should().Throw<JsonRepairException>();
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void TryRepair_ShouldReturnTrue_WithValidOutput()
    {
        bool success = JsonRepairEngine.TryRepair("{foo: 'bar', age: 30}", out string? repaired);

        success.Should().BeTrue();
        using var doc = JsonDocument.Parse(repaired!);
        doc.RootElement.GetProperty("foo").GetString().Should().Be("bar");
    }

    [Theory]
    [InlineData("{a: hello}")]
    [InlineData("[\"y\"\\, \"z\"]")]
    public void TryRepair_ShouldReturnFalse_WhenUnrepairable(string input)
    {
        bool success = JsonRepairEngine.TryRepair(input, out string? repaired);

        success.Should().BeFalse();
        repaired.Should().BeNull();
    }

    [Fact]
    public void TryRepair_Utf8_ShouldReturnFalse_AndWriteNothing_WhenUnrepairable()
    {
        byte[] input = Encoding.UTF8.GetBytes("{a: hello}");
        var writer = new ArrayBufferWriter<byte>();

        bool success = JsonRepairEngine.TryRepair(input.AsSpan(), writer);

        success.Should().BeFalse();
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void TryRepair_Utf8_ShouldReturnTrue_WithValidOutput()
    {
        byte[] input = Encoding.UTF8.GetBytes("{foo: 'bar', tags: ['a', 'b',]}");
        var writer = new ArrayBufferWriter<byte>();

        bool success = JsonRepairEngine.TryRepair(input.AsSpan(), writer);

        success.Should().BeTrue();
        Encoding.UTF8.GetString(writer.WrittenSpan).Should().Be("{\"foo\":\"bar\",\"tags\":[\"a\",\"b\"]}");
    }

    [Fact]
    public void TryParse_ShouldReturnFalse_WhenUnrepairable()
    {
        bool success = JsonRepairEngine.TryParse("{a: hello}", out var doc);

        success.Should().BeFalse();
        doc.Should().BeNull();
    }

    [Fact]
    public void Repair_ShouldNeverSilentlyReturnInvalidJson()
    {
        // The contract on a broad sample: every returned value must parse
        string[] inputs = {
            "{None: 1}", "{a: TrueStuff}", "{]", "{\"a\": 1]", "{a: hello}",
            "{a 1}", "{name: 'it's'}", "{\"a\": 1-2}", "{\"a\": -Infinity}",
            "Result: 42!", "{\"a\": 0123}", "{\"a\": \"abc\\", "[1, 2}",
        };

        foreach (string input in inputs) {
            string? repaired = null;
            Exception? caught = null;
            try {
                repaired = JsonRepairEngine.Repair(input);
            }
            catch (JsonRepairException ex) {
                caught = ex;
            }

            if (caught is null) {
                using var doc = JsonDocument.Parse(repaired!); // throws if the contract is violated
            }
        }
    }
}
