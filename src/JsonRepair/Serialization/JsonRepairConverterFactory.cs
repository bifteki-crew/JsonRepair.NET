using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsonRepair.Serialization;

/// <summary>
/// Converter factory that creates <see cref="JsonRepairConverter{T}"/> instances to automatically repair malformed JSON during deserialization.
/// </summary>
public sealed class JsonRepairConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        return true; // Can attempt repair for any target DTO type
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type converterType = typeof(JsonRepairConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// A System.Text.Json converter that repairs malformed JSON before deserializing into target type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">Target type to deserialize into.</typeparam>
public sealed class JsonRepairConverter<T> : JsonConverter<T>
{
    private readonly JsonRepairOptions _repairOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="JsonRepairConverter{T}"/>.
    /// </summary>
    public JsonRepairConverter() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="JsonRepairConverter{T}"/> with custom repair options.
    /// </summary>
    public JsonRepairConverter(JsonRepairOptions? repairOptions)
    {
        _repairOptions = repairOptions ?? JsonRepairOptions.Default;
    }

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Try reading directly first if valid
        Utf8JsonReader copy = reader;
        try {
            if (copy.TokenType != JsonTokenType.None && copy.TokenType != JsonTokenType.PropertyName) {
                using var validDoc = JsonDocument.ParseValue(ref copy);
                string validRawText = validDoc.RootElement.GetRawText();
                reader.Skip();
                return JsonSerializer.Deserialize<T>(validRawText, GetCleanOptions(options));
            }
        }
        catch {
            // Fallback to Repair
        }

        // If reader has a string or object payload, repair it
        string rawPayload = ExtractRawString(ref reader);
        string repaired = JsonRepairEngine.Repair(rawPayload, _repairOptions);
        return JsonSerializer.Deserialize<T>(repaired, GetCleanOptions(options));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, GetCleanOptions(options));
    }

    private static string ExtractRawString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is string s) {
            return s;
        }

        // Read value span directly if available
        if (!reader.HasValueSequence && reader.ValueSpan.Length > 0) {
            return System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
        }

        return reader.GetString() ?? "{}";
    }

    private static JsonSerializerOptions GetCleanOptions(JsonSerializerOptions options)
    {
        var cleanOptions = new JsonSerializerOptions(options);
        for (int i = cleanOptions.Converters.Count - 1; i >= 0; i--) {
            if (cleanOptions.Converters[i] is JsonRepairConverterFactory or JsonRepairConverter<T>) {
                cleanOptions.Converters.RemoveAt(i);
            }
        }
        return cleanOptions;
    }
}
