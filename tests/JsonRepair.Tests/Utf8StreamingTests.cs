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
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = nextSegment;
            return nextSegment;
        }
    }
}
