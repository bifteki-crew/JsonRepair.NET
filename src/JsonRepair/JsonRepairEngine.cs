using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JsonRepair.Internal;

namespace JsonRepair;

/// <summary>
/// High-performance JSON repair engine for fixing malformed LLM and legacy JSON strings and UTF-8 streams into valid JSON.
/// </summary>
public static class JsonRepairEngine
{
    /// <summary>
    /// Default JsonSerializerOptions configured for web/LLM case-insensitive property matching.
    /// </summary>
    public static JsonSerializerOptions DefaultJsonSerializerOptions { get; } = new(JsonSerializerOptions.Web) {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Repairs a malformed JSON string into valid, standard JSON.
    /// </summary>
    /// <param name="json">The malformed JSON string.</param>
    /// <param name="options">Repair options. If null, <see cref="JsonRepairOptions.Default"/> is used.</param>
    /// <returns>A valid JSON string.</returns>
    public static string Repair(string json, JsonRepairOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json)) {
            return "{}";
        }

        options ??= JsonRepairOptions.Default;
        ReadOnlySpan<char> span = json.AsSpan();

        // Step 1: Strip Markdown Fences if enabled
        if (options.StripMarkdownFences) {
            span = StripMarkdownFences(span);
        }

        // Step 2: Strip comments if enabled
        string cleanInput;
        if (options.StripComments) {
            cleanInput = StripComments(span);
            span = cleanInput.AsSpan();
        }

        // Step 3: Core State Machine Repair Pass
        Span<char> stackBuffer = stackalloc char[64];
        var stateMachine = new JsonRepairStateMachine(span, options, stackBuffer);
        return stateMachine.Repair();
    }

    /// <summary>
    /// Repairs malformed UTF-8 JSON bytes from a <see cref="ReadOnlySpan{Byte}"/> and writes repaired valid JSON directly to an <see cref="IBufferWriter{Byte}"/>.
    /// </summary>
    public static void Repair(ReadOnlySpan<byte> utf8Input, IBufferWriter<byte> writer, JsonRepairOptions? options = null)
    {
        if (utf8Input.IsEmpty) {
            Span<byte> span = writer.GetSpan(2);
            "{}"u8.CopyTo(span);
            writer.Advance(2);
            return;
        }

        options ??= JsonRepairOptions.Default;
        Span<byte> stackBuffer = stackalloc byte[64];
        var stateMachine = new Utf8JsonRepairStateMachine(utf8Input, options, writer, stackBuffer);
        stateMachine.Repair();
    }

    /// <summary>
    /// Repairs malformed UTF-8 JSON bytes from a multi-segment <see cref="ReadOnlySequence{Byte}"/> and writes repaired valid JSON to an <see cref="IBufferWriter{Byte}"/>.
    /// </summary>
    public static void Repair(ReadOnlySequence<byte> utf8Input, IBufferWriter<byte> writer, JsonRepairOptions? options = null)
    {
        if (utf8Input.IsSingleSegment) {
            Repair(utf8Input.FirstSpan, writer, options);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent((int)utf8Input.Length);
        try {
            utf8Input.CopyTo(rented);
            Repair(rented.AsSpan(0, (int)utf8Input.Length), writer, options);
        }
        finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Asynchronously reads malformed UTF-8 JSON from <paramref name="inputStream"/>, repairs it, and writes valid JSON to <paramref name="outputStream"/>.
    /// </summary>
    public static async Task RepairAsync(Stream inputStream, Stream outputStream, JsonRepairOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await inputStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        byte[] inputBytes = ms.ToArray();

        var bufferWriter = new ArrayBufferWriter<byte>(inputBytes.Length + 32);
        Repair(inputBytes.AsSpan(), bufferWriter, options);

        await outputStream.WriteAsync(bufferWriter.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Repairs malformed JSON and deserializes directly into target type <typeparamref name="T"/>.
    /// </summary>
    public static T? Deserialize<T>(string malformedJson, JsonRepairOptions? options = null, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        string repaired = Repair(malformedJson, options);
        return JsonSerializer.Deserialize<T>(repaired, jsonSerializerOptions ?? DefaultJsonSerializerOptions);
    }

    /// <summary>
    /// Tries to repair malformed JSON and deserialize directly into target type <typeparamref name="T"/>.
    /// </summary>
    public static bool TryDeserialize<T>(string malformedJson, out T? result, JsonRepairOptions? options = null, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        result = default;
        try {
            result = Deserialize<T>(malformedJson, options, jsonSerializerOptions);
            return result is not null;
        }
        catch (JsonException) {
            return false;
        }
        catch (FormatException) {
            return false;
        }
    }

    /// <summary>
    /// Tries to repair malformed JSON and parse directly as a <see cref="JsonDocument"/>.
    /// </summary>
    public static bool TryParse(string malformedJson, out JsonDocument? document, JsonRepairOptions? options = null)
    {
        document = null;
        try {
            string repaired = Repair(malformedJson, options);
            document = JsonDocument.Parse(repaired);
            return true;
        }
        catch (JsonException) {
            return false;
        }
    }

    private static ReadOnlySpan<char> StripMarkdownFences(ReadOnlySpan<char> input)
    {
        input = input.Trim();
        if (input.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) {
            input = input[7..];
        }
        else if (input.StartsWith("```")) {
            input = input[3..];
        }

        if (input.EndsWith("```")) {
            input = input[..^3];
        }

        return input.Trim();
    }

    private static string StripComments(ReadOnlySpan<char> input)
    {
        var sb = new StringBuilder(input.Length);
        bool inString = false;
        char quoteChar = '\0';

        for (int i = 0; i < input.Length; i++) {
            char c = input[i];
            char next = i + 1 < input.Length ? input[i + 1] : '\0';

            if (inString) {
                sb.Append(c);
                if (c == quoteChar && !IsEscaped(input, i)) {
                    inString = false;
                }
                continue;
            }

            if (c is '"' or '\'') {
                inString = true;
                quoteChar = c;
                sb.Append(c);
                continue;
            }

            // Single line comment //
            if (c == '/' && next == '/') {
                i += 2;
                while (i < input.Length && input[i] is not '\n' and not '\r') {
                    i++;
                }
                continue;
            }

            // Multi-line comment /* */
            if (c == '/' && next == '*') {
                i += 2;
                while (i + 1 < input.Length && !(input[i] == '*' && input[i + 1] == '/')) {
                    i++;
                }
                i++; // Skip closing '/'
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsEscaped(ReadOnlySpan<char> span, int index)
    {
        int backslashCount = 0;
        for (int i = index - 1; i >= 0 && span[i] == '\\'; i--) {
            backslashCount++;
        }
        return (backslashCount % 2) != 0;
    }
}
