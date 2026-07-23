using System;
using System.Text.Json.Serialization;

namespace JsonRepair.Serialization;

/// <summary>
/// Specifies that a class, struct, or property should use <see cref="JsonRepairConverterFactory"/> to repair malformed JSON during deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class JsonRepairAttribute : JsonConverterAttribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="JsonRepairAttribute"/>.
    /// </summary>
    public JsonRepairAttribute() : base(typeof(JsonRepairConverterFactory))
    {
    }
}
