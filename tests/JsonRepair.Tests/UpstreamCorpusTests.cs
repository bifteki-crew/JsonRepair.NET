using System.Buffers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using JsonRepair.Tests.TestCases;
using Xunit;
using Xunit.Abstractions;

namespace JsonRepair.Tests;

/// <summary>
/// Runs the ported josdejong/jsonrepair corpus (428+ cases) against both engines.
/// Cases with a semantic mismatch are tracked in josdejong-baseline.json (Tier 3/4 features);
/// a guard test enforces that the baseline only ever shrinks.
/// </summary>
public class UpstreamCorpusTests
{
    private static readonly IReadOnlyList<UpstreamCorpusCase> Cases = UpstreamCorpus.LoadCases();
    private static readonly IReadOnlyDictionary<string, string> Baseline = UpstreamCorpus.LoadBaseline();

    private readonly ITestOutputHelper _output;

    public UpstreamCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> ExpectedCases => Cases
        .Where(c => c.Expected is not null && !Baseline.ContainsKey(c.Id))
        .Select(c => new object[] { c.Id, c.Input, c.Expected! });

    public static IEnumerable<object[]> ThrowCases => Cases
        .Where(c => c.Expected is null)
        .Select(c => new object[] { c.Id, c.Input });

    [Theory]
    [MemberData(nameof(ExpectedCases))]
    public void StringEngine_CorpusCase_ShouldMatchUpstream(string id, string input, string expected)
    {
        string actual = JsonRepairEngine.Repair(input);

        JsonSemanticEquality.Equals(expected, actual, out string reason)
            .Should().BeTrue(because: $"corpus case {id} failed: {reason}\ninput:    {Show(input)}\nexpected: {Show(expected)}\nactual:   {Show(actual)}");
    }

    [Theory]
    [MemberData(nameof(ExpectedCases))]
    public void Utf8Engine_CorpusCase_ShouldMatchUpstream(string id, string input, string expected)
    {
        if (HasLoneSurrogates(input)) {
            return; // lone surrogates cannot exist in UTF-8 input bytes; case is string-engine only
        }

        string actual = RepairUtf8(input);

        JsonSemanticEquality.Equals(expected, actual, out string reason)
            .Should().BeTrue(because: $"corpus case {id} failed (UTF-8): {reason}\ninput:    {Show(input)}\nexpected: {Show(expected)}\nactual:   {Show(actual)}");
    }

    [Theory]
    [MemberData(nameof(ThrowCases))]
    public void UpstreamThrowCase_ShouldThrowOrProduceValidJson(string id, string input)
    {
        string actual;
        try {
            actual = JsonRepairEngine.Repair(input);
        }
        catch (JsonException) {
            return; // acceptable: the repair contract rejects the input
        }

        // acceptable as well: our engine is more lenient, but then the output must be valid
        using var doc = JsonDocument.Parse(actual);
        doc.RootElement.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: $"corpus case {id} must produce valid JSON");
    }

    [Fact]
    public void Baseline_ShouldOnlyContainCurrentlyFailingCases()
    {
        // A baselined case may only be removed once it passes on BOTH engines
        var nowPassing = new List<string>();

        foreach (var c in Cases.Where(c => c.Expected is not null && Baseline.ContainsKey(c.Id))) {
            if (PassesStringEngine(c) && (HasLoneSurrogates(c.Input) || PassesUtf8Engine(c))) {
                nowPassing.Add(c.Id);
            }
        }

        nowPassing.Should().BeEmpty(
            because: "these corpus cases now pass on both engines and must be removed from josdejong-baseline.json: " + string.Join(", ", nowPassing));
    }

    private static bool PassesStringEngine(UpstreamCorpusCase c)
    {
        try {
            return JsonSemanticEquality.Equals(c.Expected!, JsonRepairEngine.Repair(c.Input), out _);
        }
        catch (JsonException) {
            return false;
        }
    }

    private static bool PassesUtf8Engine(UpstreamCorpusCase c)
    {
        try {
            return JsonSemanticEquality.Equals(c.Expected!, RepairUtf8(c.Input), out _);
        }
        catch (JsonException) {
            return false;
        }
    }

    [Fact]
    public void ParityReport()
    {
        int total = 0, passing = 0, baselineCount = 0, throwing = 0;
        var newFailures = new List<string>();

        foreach (var c in Cases) {
            if (c.Expected is null) {
                continue;
            }
            total++;

            string actual;
            try {
                actual = JsonRepairEngine.Repair(c.Input);
            }
            catch (JsonException) {
                throwing++;
                if (!Baseline.ContainsKey(c.Id)) {
                    newFailures.Add(c.Id);
                }
                else {
                    baselineCount++;
                }
                continue;
            }

            if (JsonSemanticEquality.Equals(c.Expected, actual, out _)) {
                passing++;
            }
            else if (Baseline.ContainsKey(c.Id)) {
                baselineCount++;
            }
            else {
                newFailures.Add(c.Id);
            }
        }

        var categories = Baseline.Values.GroupBy(v => v).OrderByDescending(g => g.Count())
            .Select(g => $"    {g.Key}: {g.Count()}");

        _output.WriteLine($"Upstream corpus parity (string engine): {passing}/{total} passing, {baselineCount} baselined (known Tier 3/4 gaps), {throwing} contract throws.");
        _output.WriteLine("Baseline by category:");
        foreach (string cat in categories) {
            _output.WriteLine(cat);
        }
        if (newFailures.Count > 0) {
            _output.WriteLine($"UNTRACKED failures (must pass or be baselined): {string.Join(", ", newFailures)}");
        }

        newFailures.Should().BeEmpty(because: "all corpus failures must be tracked in the baseline");
    }

    private static string RepairUtf8(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        var writer = new ArrayBufferWriter<byte>();
        JsonRepairEngine.Repair(bytes.AsSpan(), writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
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

    private static string Show(string s)
    {
        return s.Length <= 120 ? s : s[..120] + "…";
    }
}
