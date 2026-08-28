using System.Buffers;
using System.Globalization;
using System.Text;

namespace JsonRepair.Tests.Fuzzing;

/// <summary>
/// Deterministic generator of valid JSON documents and of the corruptions LLMs typically emit.
/// Every choice is driven by a seeded <see cref="Random"/>, so a failing case replays from its seed.
/// </summary>
internal static class JsonFuzzGenerator
{
    private static readonly SearchValues<char> Closers = SearchValues.Create("}]");

    /// <summary>Characters spliced in at a random offset to break structure.</summary>
    private const string InsertableChars = "{}[]\",:'`\\ \n";

    /// <summary>Atoms spliced into generated strings, biased towards characters that stress the engines.</summary>
    private static readonly string[] StringAtoms = {
        "a", "b", "name", "value", "Bifteki", "crew", " ", "-", "_", "0", "12",
        "\"", "\\", "/", "\n", "\t", "\r", "\b", "\f",
        "\u0001", "\u001f",
        "ü", "münchen", "日本語", "\U0001F969", "\U0001F525",
        "'", "`", ":", ",", "{", "}", "[", "]",
        "true", "null", "None",
    };

    /// <summary>Returns a valid JSON document. Always parseable — <c>Fuzz_GeneratorShouldProduceValidJson</c> guards this.</summary>
    public static string NextDocument(Random rng)
    {
        var sb = new StringBuilder();
        WriteValue(rng, sb, depth: 0, maxDepth: 4);
        return sb.ToString();
    }

    /// <summary>Applies one corruption of the kind seen in real LLM and legacy-API output.</summary>
    public static string Corrupt(Random rng, string json)
    {
        switch (rng.Next(14)) {
            case 0: return "```json\n" + json + "\n```";
            case 1: return "Here is your JSON:\n" + json;
            case 2:
                return json
                    .Replace("true", "True", StringComparison.Ordinal)
                    .Replace("false", "False", StringComparison.Ordinal)
                    .Replace("null", "None", StringComparison.Ordinal);
            case 3: return json[..rng.Next(0, json.Length)];                       // truncation
            case 4: return RemoveAt(json, rng.Next(0, json.Length));
            case 5: return InsertAt(json, rng.Next(0, json.Length + 1), InsertableChars[rng.Next(InsertableChars.Length)]);
            case 6: return json.Replace('"', '\'');                                // single quotes throughout
            case 7: return InsertBeforeLastCloser(json, ',');                      // trailing comma
            case 8: return RemoveFirst(json, ',');
            case 9: return RemoveFirst(json, ':');
            case 10: return FlipACloser(rng, json);
            case 11: return UnquoteKeys(json);
            case 12: return json + json;                                           // two root values
            default: return "/* note */" + json + "// trailing";
        }
    }

    private static void WriteValue(Random rng, StringBuilder sb, int depth, int maxDepth)
    {
        switch (depth >= maxDepth ? rng.Next(0, 5) : rng.Next(0, 7)) {
            case 0: sb.Append("null"); break;
            case 1: sb.Append(rng.Next(2) == 0 ? "true" : "false"); break;
            case 2: WriteNumber(rng, sb); break;
            case 3:
            case 4: WriteString(rng, sb); break;
            case 5: WriteArray(rng, sb, depth, maxDepth); break;
            default: WriteObject(rng, sb, depth, maxDepth); break;
        }
    }

    private static void WriteNumber(Random rng, StringBuilder sb)
    {
        if (rng.Next(4) == 0) {
            sb.Append('-');
        }
        sb.Append(rng.Next(0, 1000).ToString(CultureInfo.InvariantCulture)); // never leading-zero
        if (rng.Next(3) == 0) {
            sb.Append('.').Append(rng.Next(0, 1000).ToString(CultureInfo.InvariantCulture));
        }
        if (rng.Next(6) == 0) {
            sb.Append(rng.Next(2) == 0 ? 'e' : 'E')
              .Append(rng.Next(2) == 0 ? '+' : '-')
              .Append(rng.Next(0, 10).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void WriteString(Random rng, StringBuilder sb)
    {
        sb.Append('"');
        int parts = rng.Next(0, 4);
        for (int i = 0; i < parts; i++) {
            AppendEscaped(sb, StringAtoms[rng.Next(StringAtoms.Length)]);
        }
        sb.Append('"');
    }

    /// <summary>Keys carry a trailing index so an object never contains duplicate names.</summary>
    private static void WriteKey(Random rng, StringBuilder sb, int index)
    {
        sb.Append('"');
        int parts = rng.Next(0, 3);
        for (int i = 0; i < parts; i++) {
            AppendEscaped(sb, StringAtoms[rng.Next(StringAtoms.Length)]);
        }
        sb.Append('k').Append(index.ToString(CultureInfo.InvariantCulture)).Append('"');
    }

    private static void WriteArray(Random rng, StringBuilder sb, int depth, int maxDepth)
    {
        int count = rng.Next(0, 4);
        sb.Append('[');
        for (int i = 0; i < count; i++) {
            if (i > 0) {
                sb.Append(',');
            }
            WriteValue(rng, sb, depth + 1, maxDepth);
        }
        sb.Append(']');
    }

    private static void WriteObject(Random rng, StringBuilder sb, int depth, int maxDepth)
    {
        int count = rng.Next(0, 4);
        sb.Append('{');
        for (int i = 0; i < count; i++) {
            if (i > 0) {
                sb.Append(',');
            }
            WriteKey(rng, sb, i);
            sb.Append(':');
            WriteValue(rng, sb, depth + 1, maxDepth);
        }
        sb.Append('}');
    }

    private static void AppendEscaped(StringBuilder sb, string raw)
    {
        foreach (char c in raw) {
            switch (c) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
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
    }

    private static string RemoveAt(string s, int index)
    {
        return s.Length == 0 ? s : s.Remove(index, 1);
    }

    private static string InsertAt(string s, int index, char c)
    {
        return s.Insert(Math.Min(index, s.Length), c.ToString());
    }

    private static string RemoveFirst(string s, char c)
    {
        int i = s.IndexOf(c, StringComparison.Ordinal);
        return i < 0 ? s : s.Remove(i, 1);
    }

    private static string InsertBeforeLastCloser(string s, char c)
    {
        int i = s.AsSpan().LastIndexOfAny(Closers);
        return i < 0 ? s : s.Insert(i, c.ToString());
    }

    private static string FlipACloser(Random rng, string s)
    {
        var closers = new List<int>();
        for (int i = 0; i < s.Length; i++) {
            if (s[i] is '}' or ']') {
                closers.Add(i);
            }
        }
        if (closers.Count == 0) {
            return s;
        }
        int at = closers[rng.Next(closers.Count)];
        char[] chars = s.ToCharArray();
        chars[at] = chars[at] == '}' ? ']' : '}';
        return new string(chars);
    }

    /// <summary>Strips the quotes from simple object keys, producing <c>{key: value}</c>.</summary>
    private static string UnquoteKeys(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) {
            if (s[i] == '"' && i > 0 && (s[i - 1] == '{' || s[i - 1] == ',')) {
                int end = s.IndexOf('"', i + 1);
                if (end > i && end + 1 < s.Length && s[end + 1] == ':') {
                    string key = s[(i + 1)..end];
                    if (key.Length > 0 && key.All(char.IsLetterOrDigit)) {
                        sb.Append(key);
                        i = end;
                        continue;
                    }
                }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
