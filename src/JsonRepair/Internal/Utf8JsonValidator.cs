using System;
using System.Text.Json;

namespace JsonRepair.Internal;

/// <summary>
/// Lightweight JSON validation over UTF-8 bytes without building a DOM.
/// Enforces well-formedness and a single root value.
/// </summary>
internal static class Utf8JsonValidator
{
    /// <summary>
    /// Validates <paramref name="json"/>, reporting why it is invalid in <paramref name="error"/>
    /// so the UTF-8 engine can explain a failure as precisely as the string engine does.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<byte> json, out string? error)
    {
        if (json.IsEmpty) {
            error = "the repaired output is empty.";
            return false;
        }

        // MaxDepth int.MaxValue, not 0: JsonReaderOptions treats 0 as the 64-level default. The repair
        // engine supports arbitrary depth (growable stack), so the validator must not be stricter than it.
        var reader = new Utf8JsonReader(json, isFinalBlock: true, new JsonReaderState(new JsonReaderOptions { MaxDepth = int.MaxValue }));
        try {
            int depth = 0;
            bool rootCompleted = false;
            while (reader.Read()) {
                if (rootCompleted) {
                    error = "trailing content after the root value.";
                    return false;
                }
                switch (reader.TokenType) {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        depth++;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        depth--;
                        if (depth == 0) {
                            rootCompleted = true;
                        }
                        break;
                    default:
                        if (depth == 0) {
                            rootCompleted = true; // primitive root value
                        }
                        break;
                }
            }

            if (depth != 0 || !rootCompleted) {
                error = "the root value is incomplete.";
                return false;
            }

            error = null;
            return true;
        }
        catch (JsonException ex) {
            error = JsonErrorMessage.WithoutPosition(ex);
            return false;
        }
    }
}
