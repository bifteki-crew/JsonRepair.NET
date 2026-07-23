using System;
using System.Diagnostics;
using BenchmarkDotNet.Running;
using JsonRepair.Benchmarks;

Console.WriteLine("=================================================");
Console.WriteLine(" JsonRepair.NET Benchmark & Performance Harness ");
Console.WriteLine(" Framework: .NET 10 (net10.0)");
Console.WriteLine("=================================================\n");

if (args.Length > 0 && args[0].Equals("--run", StringComparison.OrdinalIgnoreCase)) {
    BenchmarkRunner.Run<RepairBenchmarks>();
}
else {
    Console.WriteLine("Running Quick Performance Verification Pass...");
    var bench = new RepairBenchmarks();
    bench.Setup();

    var watch = Stopwatch.StartNew();
    int iterations = 100_000;
    for (int i = 0; i < iterations; i++) {
        _ = bench.SmallPayload_Repair();
    }
    watch.Stop();

    double opsPerSec = iterations / watch.Elapsed.TotalSeconds;
    Console.WriteLine($"[RESULT] Small Payload: {iterations:N0} ops in {watch.ElapsedMilliseconds} ms ({opsPerSec:N0} ops/sec)");
    Console.WriteLine($"[RESULT] Avg Time per Op: {(watch.Elapsed.TotalMicroseconds / iterations):N2} µs");
    Console.WriteLine("\nTo run full BenchmarkDotNet suite with MemoryDiagnoser: dotnet run -c Release -- --run");
}
