using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace JsonRepair.Tests;

public class Utf8StreamingTests
{
    [Fact]
    public void Repair_Utf8Span_ShouldProduceValidJson()
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("{foo: 'bar', age: 30}");
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be("{\"foo\":\"bar\",\"age\":30}");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("foo").GetString().Should().Be("bar");
    }

    [Fact]
    public void Repair_Utf8Span_ShouldQuoteUnquotedUtf8Keys()
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("{ münchen: 'Germany', 🥩: 'Bifteki' }");
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be("{\"münchen\":\"Germany\",\"🥩\":\"Bifteki\"}");
    }

    [Fact]
    public void Repair_Utf8Span_ShouldStripTrailingCommas()
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("{\"a\": 1, \"b\": 2,}");
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be("{\"a\":1,\"b\":2}");
    }

    [Fact]
    public void Repair_Utf8Span_ShouldStripSingleAndMultiLineComments()
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("{ \"a\": 1, // single line comment\n \"b\": /* block comment */ 2 }");
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be("{\"a\":1,\"b\":2}");
    }

    [Theory]
    [InlineData("```json\n42\n```", "42")]
    [InlineData("```json\n'hello world'\n```", "\"hello world\"")]
    [InlineData("```json\nTrue\n```", "true")]
    public void Repair_Utf8Span_ShouldRepairMarkdownFencedPrimitives(string inputStr, string expected)
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes(inputStr);
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be(expected);
    }

    [Fact]
    public void Repair_Utf8Span_ShouldHandleUnterminatedBlockCommentAtEof()
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("{\"a\": 1 /* unterminated comment");
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be("{\"a\":1}");
    }

    [Theory]
    [InlineData("{None: 1}", "{\"None\":1}")]
    [InlineData("{True: 'yes'}", "{\"True\":\"yes\"}")]
    [InlineData("{a: None}", "{\"a\":null}")]
    [InlineData("[1 None]", "[1,null]")]
    public void Repair_Utf8Span_ShouldOnlyConvertLiteralsInValuePosition(string inputStr, string expected)
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes(inputStr);
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be(expected);
    }

    [Fact]
    public void Repair_Utf8Span_ShouldNotConvertLiteralPrefixInsideLongerWord()
    {
        // Arrange: unquoted string VALUES are not yet supported (Tier 3 / 0.3.0),
        // so this input is unrepairable and must throw (valid-or-throw contract, 0.2.0).
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("{a: TrueStuff}");
        var writer = new ArrayBufferWriter<byte>();

        // Act
        Action act = () => JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        act.Should().Throw<JsonRepairException>();
        writer.WrittenCount.Should().Be(0, because: "nothing may be written when repair fails");
    }

    [Theory]
    [InlineData("{\"a\": 1]", "{\"a\":1}")]
    [InlineData("[1, 2}", "[1,2]")]
    [InlineData("{]", "{}")]
    [InlineData("{\"a\": [1, 2}", "{\"a\":[1,2]}")]
    public void Repair_Utf8Span_ShouldRepairMismatchedClosingBrackets(string inputStr, string expected)
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes(inputStr);
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(malformedUtf8.AsSpan(), writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be(expected);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.Should().NotBeNull();
    }

    [Fact]
    public void Repair_Utf8Span_ShouldHandleDeepNestingExceedingStackBuffer()
    {
        // Arrange: 100 levels of nested arrays
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++) sb.Append('[');
        sb.Append('1');
        byte[] input = Encoding.UTF8.GetBytes(sb.ToString());
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(input.AsSpan(), writer);

        // Assert
        string expected = new string('[', 100) + "1" + new string(']', 100);
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be(expected);
    }

    [Fact]
    public void Repair_NullWriter_ShouldThrowArgumentNullException()
    {
        byte[] input = "{}"u8.ToArray();
        Action act = () => JsonRepairEngine.Repair(input.AsSpan(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RepairAsync_NullStreams_ShouldThrowArgumentNullException()
    {
        using var ms = new MemoryStream();
        Func<Task> act1 = async () => await JsonRepairEngine.RepairAsync(null!, ms);
        Func<Task> act2 = async () => await JsonRepairEngine.RepairAsync(ms, null!);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Repair_Utf8Sequence_ShouldHandleMultiSegmentSequence()
    {
        // Arrange: Create a multi-segment ReadOnlySequence
        byte[] chunk1 = Encoding.UTF8.GetBytes("{name: ");
        byte[] chunk2 = Encoding.UTF8.GetBytes("'Alice', ");
        byte[] chunk3 = Encoding.UTF8.GetBytes("active: True}");

        var firstNode = new BufferSegment(chunk1);
        var secondNode = firstNode.Append(chunk2);
        var thirdNode = secondNode.Append(chunk3);

        var sequence = new ReadOnlySequence<byte>(firstNode, 0, thirdNode, chunk3.Length);
        var writer = new ArrayBufferWriter<byte>();

        // Act
        JsonRepairEngine.Repair(sequence, writer);

        // Assert
        string result = Encoding.UTF8.GetString(writer.WrittenSpan);
        result.Should().Be("{\"name\":\"Alice\",\"active\":true}");
    }

    [Fact]
    public async Task RepairAsync_Stream_ShouldPipeRepairedJson()
    {
        // Arrange
        byte[] malformedUtf8 = Encoding.UTF8.GetBytes("```json\n{'item': 'Bifteki', 'price': 14.99}\n```");
        using var inputStream = new MemoryStream(malformedUtf8);
        using var outputStream = new MemoryStream();

        // Act
        await JsonRepairEngine.RepairAsync(inputStream, outputStream);

        // Assert
        string result = Encoding.UTF8.GetString(outputStream.ToArray());
        result.Should().Be("{\"item\":\"Bifteki\",\"price\":14.99}");
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(Memory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(Memory<byte> memory)
        {
            var nextSegment = new BufferSegment(memory) {
                RunningIndex = this.RunningIndex + this.Memory.Length
            };
            this.Next = nextSegment;
            return nextSegment;
        }
    }
}
