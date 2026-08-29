using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using JsonRepair.Tests.TestCases;
using Xunit;
using Xunit.Abstractions;

namespace JsonRepair.Tests.Fuzzing;

/// <summary>
/// Property-based tests over generated and corrupted JSON. Seeds are fixed so CI is deterministic
/// and a failure replays exactly; set JSONREPAIR_FUZZ_SEEDS to widen a local hunt.
/// </summary>
/// <remarks>
/// These assert with <see cref="Assert.True(bool, string)"/> rather than FluentAssertions so a run can
/// report every violation it found as a plain multi-line block, rather than one assertion's worth
/// wrapped in "Expected ... because ...". Braces in the JSON are safe either way — FluentAssertions 8
/// does not treat a "because" string as a format template.
/// </remarks>
public class FuzzTests
{
    private const int IterationsPerSeed = 500;

    private static readonly int SeedCount =
        int.TryParse(Environment.GetEnvironmentVariable("JSONREPAIR_FUZZ_SEEDS"), out int n) && n > 0 ? n : 8;

    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, SeedCount).Select(s => new object[] { s });

    private readonly ITestOutputHelper _output;

    public FuzzTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Guards the generator itself: an invalid generator would silently weaken every test below.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz_GeneratorShouldProduceValidJson(int seed)
    {
        var rng = new Random(seed);
        var failures = new List<string>();

        for (int i = 0; i < IterationsPerSeed; i++) {
            string json = JsonFuzzGenerator.NextDocument(rng);
            if (!Parses(json, out string error)) {
                failures.Add(Describe(seed, i, "generator", json, "generator emitted invalid JSON: " + error));
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>The core Tier 2 invariant: output parses, or JsonRepairException is thrown. Nothing else.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz_RepairShouldReturnParseableJsonOrThrowRepairException(int seed)
    {
        var rng = new Random(seed);
        var failures = new List<string>();

        for (int i = 0; i < IterationsPerSeed; i++) {
            string input = JsonFuzzGenerator.Corrupt(rng, JsonFuzzGenerator.NextDocument(rng));
            Add(failures, CheckContract(seed, i, input, "string", () => JsonRepairEngine.Repair(input)));
            Add(failures, CheckContract(seed, i, input, "utf8", () => RepairUtf8(input)));
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>The two engines are separate implementations; they must not disagree.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz_BothEnginesShouldAgree(int seed)
    {
        var rng = new Random(seed);
        var failures = new List<string>();
        int lenientUtf8 = 0;

        for (int i = 0; i < IterationsPerSeed; i++) {
            string input = JsonFuzzGenerator.Corrupt(rng, JsonFuzzGenerator.NextDocument(rng));
            if (HasLoneSurrogates(input)) {
                continue; // UTF-8 encoding would substitute U+FFFD, so the engines would see different input
            }

            string? viaString = Outcome(() => JsonRepairEngine.Repair(input));
            string? viaUtf8 = Outcome(() => RepairUtf8(input));

            if (viaString is null && viaUtf8 is not null) {
                // Allowed in this direction only: the UTF-8 engine repairs a little more than the
                // string engine does — a trailing comma after a root primitive ("536," -> 536), which
                // is what josdejong does too. Pinned in EngineAgreementTests. The reverse direction,
                // and any outright disagreement, is a defect and fails below.
                //
                // The reverse does occur once, on non-ASCII whitespace (NBSP, U+3000), where the string
                // engine is the lenient one. This generator does not emit those characters, so that
                // class shows up in the corpus rather than here; see docs/UPSTREAM.md.
                lenientUtf8++;
                continue;
            }

            if (viaUtf8 is null && viaString is not null) {
                failures.Add(Describe(seed, i, "parity", input,
                    $"utf8 engine threw but string engine repaired to {Literal(viaString)} — the opposite of the known divergence"));
            }
            else if (viaString is not null && viaUtf8 is not null
                     && !JsonSemanticEquality.Equals(viaString, viaUtf8, out string reason)) {
                failures.Add(Describe(seed, i, "parity", input,
                    $"both engines repaired but disagree ({reason}); string={Literal(viaString)} utf8={Literal(viaUtf8)}"));
            }
        }

        _output.WriteLine($"seed {seed}: {lenientUtf8} input(s) hit the known one-directional divergence (utf8 lenient, string strict).");
        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>Repairing something already valid must not change what it means.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz_ValidJsonShouldSurviveRepairUnchanged(int seed)
    {
        var rng = new Random(seed);
        var failures = new List<string>();

        for (int i = 0; i < IterationsPerSeed; i++) {
            string valid = JsonFuzzGenerator.NextDocument(rng);
            string? repaired = Outcome(() => JsonRepairEngine.Repair(valid));

            if (repaired is null) {
                failures.Add(Describe(seed, i, "roundtrip", valid, "valid JSON was rejected"));
            }
            else if (!JsonSemanticEquality.Equals(valid, repaired, out string reason)) {
                failures.Add(Describe(seed, i, "roundtrip", valid, $"valid JSON changed meaning ({reason}); output={Literal(repaired)}"));
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>Repaired output is valid JSON, so repairing it again must be a no-op.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz_RepairShouldBeIdempotent(int seed)
    {
        var rng = new Random(seed);
        var failures = new List<string>();

        for (int i = 0; i < IterationsPerSeed; i++) {
            string input = JsonFuzzGenerator.Corrupt(rng, JsonFuzzGenerator.NextDocument(rng));
            string? once = Outcome(() => JsonRepairEngine.Repair(input));
            if (once is null) {
                continue;
            }

            string? twice = Outcome(() => JsonRepairEngine.Repair(once));
            if (twice is null) {
                failures.Add(Describe(seed, i, "idempotence", input, $"repairing repaired output threw; first pass={Literal(once)}"));
            }
            else if (!JsonSemanticEquality.Equals(once, twice, out string reason)) {
                failures.Add(Describe(seed, i, "idempotence", input,
                    $"second pass changed the result ({reason}); first={Literal(once)} second={Literal(twice)}"));
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>TryRepair must report exactly what Repair does, without throwing.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Fuzz_TryRepairShouldAgreeWithRepair(int seed)
    {
        var rng = new Random(seed);
        var failures = new List<string>();

        for (int i = 0; i < IterationsPerSeed; i++) {
            string input = JsonFuzzGenerator.Corrupt(rng, JsonFuzzGenerator.NextDocument(rng));
            string? direct = Outcome(() => JsonRepairEngine.Repair(input));

            bool succeeded;
            string? viaTry;
            try {
                succeeded = JsonRepairEngine.TryRepair(input, out viaTry);
            }
            catch (Exception ex) {
                failures.Add(Describe(seed, i, "tryrepair", input, $"TryRepair threw {ex.GetType().Name}: {ex.Message}"));
                continue;
            }

            if (succeeded != (direct is not null)) {
                failures.Add(Describe(seed, i, "tryrepair", input,
                    $"TryRepair returned {succeeded} but Repair {(direct is null ? "threw" : "succeeded")}"));
            }
            else if (succeeded && !string.Equals(viaTry, direct, StringComparison.Ordinal)) {
                failures.Add(Describe(seed, i, "tryrepair", input,
                    $"TryRepair and Repair produced different output; try={Literal(viaTry!)} repair={Literal(direct!)}"));
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    private static string? CheckContract(int seed, int iteration, string input, string engine, Func<string> repair)
    {
        string result;
        try {
            result = repair();
        }
        catch (JsonRepairException) {
            return null; // the contract's other permitted outcome
        }
        catch (Exception ex) {
            return Describe(seed, iteration, engine, input, $"threw {ex.GetType().Name} instead of JsonRepairException: {ex.Message}");
        }

        return Parses(result, out string error)
            ? null
            : Describe(seed, iteration, engine, input, $"returned unparseable output {Literal(result)}: {error}");
    }

    private static bool Parses(string json, out string error)
    {
        try {
            using (JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = int.MaxValue })) { }
            error = "";
            return true;
        }
        catch (JsonException ex) {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Returns the repaired text, or null when the contract rejected the input.</summary>
    private static string? Outcome(Func<string> repair)
    {
        try {
            return repair();
        }
        catch (JsonRepairException) {
            return null;
        }
    }

    private static string RepairUtf8(string input)
    {
        var writer = new ArrayBufferWriter<byte>();
        JsonRepairEngine.Repair(Encoding.UTF8.GetBytes(input).AsSpan(), writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    private static void Add(List<string> failures, string? failure)
    {
        if (failure is not null) {
            failures.Add(failure);
        }
    }

    private static string Describe(int seed, int iteration, string kind, string input, string problem)
    {
        return $"seed {seed} iteration {iteration} [{kind}]: {problem}\n      input: {Literal(input)}";
    }

    private static string Report(List<string> failures)
    {
        if (failures.Count == 0) {
            return "";
        }

        var sb = new StringBuilder();
        sb.Append(failures.Count).AppendLine(" fuzz invariant violation(s). Each line replays from its seed; add it to the corpus before fixing:");
        foreach (string failure in failures.Take(5)) {
            sb.Append("  ").AppendLine(failure);
        }
        if (failures.Count > 5) {
            sb.Append("  ... and ").Append(failures.Count - 5).AppendLine(" more");
        }
        return sb.ToString();
    }

    /// <summary>Renders a value as a C# string literal so a failure pastes straight into a test.</summary>
    private static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s) {
            switch (c) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static bool HasLoneSurrogates(string s)
    {
        for (int i = 0; i < s.Length; i++) {
            if (char.IsHighSurrogate(s[i])) {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) {
                    return true;
                }
                i++;
            }
            else if (char.IsLowSurrogate(s[i])) {
                return true;
            }
        }
        return false;
    }
}
