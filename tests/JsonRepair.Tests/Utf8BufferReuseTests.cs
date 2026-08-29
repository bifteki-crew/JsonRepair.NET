using System.Buffers;
using System.Text;
using FluentAssertions;
using Xunit;

namespace JsonRepair.Tests;

/// <summary>
/// Guards the per-thread staging buffer behind the UTF-8 repair path. Reusing it is what keeps that
/// path free of per-call allocations; the reuse must not be observable in the results.
/// </summary>
public class Utf8BufferReuseTests
{
    private static readonly byte[] Payload =
        Encoding.UTF8.GetBytes("```json\n{ user: 'Alice', active: True, balance: None, tags: ['admin','dev',] }\n```");

    private const string Expected = "{\"user\":\"Alice\",\"active\":true,\"balance\":null,\"tags\":[\"admin\",\"dev\"]}";

    [Fact]
    public void Repair_Utf8_ShouldNotAllocatePerCall()
    {
        var writer = new ArrayBufferWriter<byte>(4096);

        for (int i = 0; i < 2_000; i++) {
            writer.ResetWrittenCount();
            JsonRepairEngine.Repair(Payload, writer); // warm up JIT and the pool
        }

        const int iterations = 20_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) {
            writer.ResetWrittenCount();
            JsonRepairEngine.Repair(Payload, writer);
        }
        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

        // Measured at 0 B/op. Asserted against a small tolerance rather than exactly zero so the test
        // reports a real regression (the staging object is 32 B) without tripping on runtime noise.
        perCall.Should().BeLessThan(16,
            because: "the UTF-8 path reuses its staging buffer, so repairs must not allocate per call");
    }

    [Fact]
    public void Repair_Utf8_ShouldStayCorrect_WhenTheCallersWriterReentersTheEngine()
    {
        // The caller's IBufferWriter is user code running while the staging buffer is live. If a
        // re-entrant repair were handed the same buffer, it would corrupt the outer result.
        var writer = new ReentrantWriter();

        JsonRepairEngine.Repair(Payload, writer);

        writer.OuterResult.Should().Be(Expected, because: "the nested repair must not disturb the outer one");
        writer.NestedResult.Should().Be(Expected, because: "the nested repair must get a buffer of its own");
    }

    private sealed class ReentrantWriter : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _inner = new();

        public string OuterResult => Encoding.UTF8.GetString(_inner.WrittenSpan);

        public string NestedResult { get; private set; } = "";

        public void Advance(int count)
        {
            _inner.Advance(count);
            if (NestedResult.Length == 0) {
                var nested = new ArrayBufferWriter<byte>();
                JsonRepairEngine.Repair(Payload, nested);
                NestedResult = Encoding.UTF8.GetString(nested.WrittenSpan);
            }
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);
    }
}
