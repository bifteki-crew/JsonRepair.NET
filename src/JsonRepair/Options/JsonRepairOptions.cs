namespace JsonRepair;

/// <summary>
/// Options for customizing the JSON repair process.
/// </summary>
public sealed record JsonRepairOptions
{
    /// <summary>
    /// Gets the default configuration for JSON repair operations.
    /// </summary>
    public static JsonRepairOptions Default { get; } = new();

    /// <summary>
    /// Whether to automatically strip markdown code block fences (e.g., ```json and ```). Default is true.
    /// </summary>
    public bool StripMarkdownFences { get; init; } = true;

    /// <summary>
    /// Whether to convert unquoted object keys into double-quoted keys. Default is true.
    /// </summary>
    public bool QuoteUnquotedKeys { get; init; } = true;

    /// <summary>
    /// Whether to convert Python/JavaScript non-standard literals (None -> null, True -> true, False -> false, undefined -> null, NaN -> null). Default is true.
    /// </summary>
    public bool ConvertNonStandardLiterals { get; init; } = true;

    /// <summary>
    /// Whether to strip trailing commas in objects and arrays. Default is true.
    /// </summary>
    public bool StripTrailingCommas { get; init; } = true;

    /// <summary>
    /// Whether to insert missing commas between properties or array elements. Default is true.
    /// </summary>
    public bool InsertMissingCommas { get; init; } = true;

    /// <summary>
    /// Whether to automatically close unclosed objects and arrays in truncated JSON. Default is true.
    /// </summary>
    public bool AutoCloseStructures { get; init; } = true;

    /// <summary>
    /// Whether to strip single-line and multi-line comments. Default is true.
    /// </summary>
    public bool StripComments { get; init; } = true;
}
