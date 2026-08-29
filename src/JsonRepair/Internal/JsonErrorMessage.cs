using System;
using System.Text.Json;

namespace JsonRepair.Internal;

/// <summary>
/// Normalizes <see cref="JsonException"/> messages for the valid-or-throw repair contract.
/// </summary>
internal static class JsonErrorMessage
{
    private const string PositionMarker = " LineNumber: ";

    /// <summary>
    /// Returns the reason from <paramref name="ex"/> without its trailing position suffix.
    /// The offsets System.Text.Json reports describe the <em>repaired output</em>, not the caller's
    /// input, so quoting them in a message about "the input" points at the wrong place. Callers that
    /// need the raw offsets can read them from the inner exception.
    /// </summary>
    public static string WithoutPosition(Exception ex)
    {
        string message = ex.Message;
        int marker = message.IndexOf(PositionMarker, StringComparison.Ordinal);
        return marker < 0 ? message : message[..marker].TrimEnd();
    }
}
