using System.Text.Json;

namespace JsonRepair.Tests.TestCases;

/// <summary>A single case from the josdejong/jsonrepair test corpus. <see cref="Expected"/> is null when upstream expects a throw.</summary>
public sealed record UpstreamCorpusCase(string Id, string Input, string? Expected);

/// <summary>
/// Loads the josdejong/jsonrepair corpus (tests/JsonRepair.Tests/TestCases/UpstreamCorpus/josdejong-corpus.json)
/// and the known-failure baseline (josdejong-baseline.json, format: { "JS001": "category" }).
/// </summary>
internal static class UpstreamCorpus
{
    private static readonly string CorpusPath = Path.Combine(AppContext.BaseDirectory, "TestCases", "UpstreamCorpus", "josdejong-corpus.json");
    private static readonly string BaselinePath = Path.Combine(AppContext.BaseDirectory, "TestCases", "UpstreamCorpus", "josdejong-baseline.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<UpstreamCorpusCase> LoadCases()
    {
        var cases = JsonSerializer.Deserialize<List<UpstreamCorpusCase>>(File.ReadAllText(CorpusPath), SerializerOptions);
        return cases ?? throw new InvalidOperationException($"Failed to load corpus from {CorpusPath}");
    }

    public static IReadOnlyDictionary<string, string> LoadBaseline()
    {
        if (!File.Exists(BaselinePath)) {
            return new Dictionary<string, string>();
        }
        var baseline = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BaselinePath), SerializerOptions);
        return baseline ?? new Dictionary<string, string>();
    }
}

/// <summary>
/// Tolerant JSON equality for corpus comparison: object order-insensitive, numbers compared by value
/// (1 == 1.0 == 1e0), strings ordinal. If the expected text isn't valid JSON (rare upstream trap
/// cases), falls back to exact string comparison.
/// </summary>
internal static class JsonSemanticEquality
{
    public static bool Equals(string expectedJson, string actualJson, out string reason)
    {
        reason = "";

        JsonDocument expectedDoc;
        try {
            expectedDoc = JsonDocument.Parse(expectedJson);
        }
        catch (JsonException) {
            bool same = string.Equals(expectedJson, actualJson, StringComparison.Ordinal);
            if (!same) {
                reason = "exact string mismatch (expected output is not parseable JSON)";
            }
            return same;
        }

        try {
            using (expectedDoc)
            using (var actualDoc = JsonDocument.Parse(actualJson)) {
                bool equal = ElementEquals(expectedDoc.RootElement, actualDoc.RootElement);
                if (!equal) {
                    reason = "semantic mismatch";
                }
                return equal;
            }
        }
        catch (JsonException ex) {
            reason = $"actual output is not valid JSON: {ex.Message.Split('\n')[0]}";
            return false;
        }
    }

    private static bool ElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) {
            return false;
        }

        switch (a.ValueKind) {
            case JsonValueKind.Number:
                return NumbersEqual(a, b);
            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Array: {
                    if (a.GetArrayLength() != b.GetArrayLength()) {
                        return false;
                    }
                    using var ae = a.EnumerateArray();
                    using var be = b.EnumerateArray();
                    while (ae.MoveNext() && be.MoveNext()) {
                        if (!ElementEquals(ae.Current, be.Current)) {
                            return false;
                        }
                    }
                    return true;
                }
            case JsonValueKind.Object: {
                    var bProps = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var p in b.EnumerateObject()) {
                        bProps[p.Name] = p.Value;
                    }
                    int aCount = 0;
                    foreach (var p in a.EnumerateObject()) {
                        aCount++;
                        if (!bProps.TryGetValue(p.Name, out var bv) || !ElementEquals(p.Value, bv)) {
                            return false;
                        }
                    }
                    return aCount == bProps.Count;
                }
            default: // True, False, Null, Undefined
                return true;
        }
    }

    private static bool NumbersEqual(JsonElement a, JsonElement b)
    {
        if (a.TryGetDecimal(out decimal da) && b.TryGetDecimal(out decimal db)) {
            return da == db;
        }
        if (a.TryGetDouble(out double xa) && b.TryGetDouble(out double xb)) {
            return xa.Equals(xb);
        }
        return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
    }
}
