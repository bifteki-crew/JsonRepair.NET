using System.Text.Json;

namespace JsonRepair;

/// <summary>
/// Thrown when <see cref="JsonRepairEngine"/> cannot repair the input into valid JSON.
/// Inherits from <see cref="JsonException"/> so existing catch clauses keep working.
/// </summary>
public class JsonRepairException : JsonException
{
    /// <summary>
    /// Initializes a new instance of <see cref="JsonRepairException"/> with a specified error message.
    /// </summary>
    public JsonRepairException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="JsonRepairException"/> with a specified error message and inner exception.
    /// </summary>
    public JsonRepairException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
