using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using JsonRepair;

namespace JsonRepair.Benchmarks;

[MemoryDiagnoser]
public class RepairBenchmarks
{
    private string _smallJson = null!;
    private string _mediumJson = null!;
    private string _largeJson = null!;

    private byte[] _smallUtf8 = null!;
    private byte[] _mediumUtf8 = null!;
    private byte[] _largeUtf8 = null!;

    // Reused across iterations: the UTF-8 API is designed to write into a buffer the caller owns,
    // so a per-iteration allocation here would measure the harness rather than the engine.
    private ArrayBufferWriter<byte> _writer = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 1. Small Payload (83 B) - Typical LLM Tool Call
        _smallJson = """
        ```json
        { user: 'Alice', active: True, balance: None, tags: ['admin', 'dev',] }
        ```
        """;

        // 2. Medium Payload (2.9 KB) - Typical LLM Structured Response
        var sbMedium = new StringBuilder();
        sbMedium.AppendLine("```json");
        sbMedium.AppendLine("{ status: 'success', code: 200, items: [");
        for (int i = 0; i < 50; i++) {
            sbMedium.AppendLine($"  {{ id: {i}, name: 'Item_{i}', valid: True, extra: None, }},");
        }
        sbMedium.AppendLine("], metadata: { total: 50, page: 1, } }");
        sbMedium.AppendLine("```");
        _mediumJson = sbMedium.ToString();

        // 3. Large Payload (136 KB) - Heavy LLM Data Dump
        var sbLarge = new StringBuilder();
        sbLarge.AppendLine("{ data: [");
        for (int i = 0; i < 1000; i++) {
            sbLarge.AppendLine($"  {{ index: {i}, uuid: '550e8400-e29b-41d4-a716-446655440000', flag: False, score: 99.8, note: 'Sample line {i}\nwith unescaped newline', }},");
        }
        sbLarge.AppendLine("] }");
        _largeJson = sbLarge.ToString();

        _smallUtf8 = Encoding.UTF8.GetBytes(_smallJson);
        _mediumUtf8 = Encoding.UTF8.GetBytes(_mediumJson);
        _largeUtf8 = Encoding.UTF8.GetBytes(_largeJson);
        _writer = new ArrayBufferWriter<byte>(_largeUtf8.Length + 1024);
    }

    [Benchmark(Baseline = true)]
    public string SmallPayload_Repair()
    {
        return JsonRepairEngine.Repair(_smallJson);
    }

    [Benchmark]
    public bool SmallPayload_TryParse()
    {
        return JsonRepairEngine.TryParse(_smallJson, out _);
    }

    [Benchmark]
    public string MediumPayload_Repair()
    {
        return JsonRepairEngine.Repair(_mediumJson);
    }

    [Benchmark]
    public string LargePayload_Repair()
    {
        return JsonRepairEngine.Repair(_largeJson);
    }

    [Benchmark]
    public int SmallPayload_RepairUtf8()
    {
        _writer.ResetWrittenCount();
        JsonRepairEngine.Repair(_smallUtf8, _writer);
        return _writer.WrittenCount;
    }

    [Benchmark]
    public int MediumPayload_RepairUtf8()
    {
        _writer.ResetWrittenCount();
        JsonRepairEngine.Repair(_mediumUtf8, _writer);
        return _writer.WrittenCount;
    }

    [Benchmark]
    public int LargePayload_RepairUtf8()
    {
        _writer.ResetWrittenCount();
        JsonRepairEngine.Repair(_largeUtf8, _writer);
        return _writer.WrittenCount;
    }
}
