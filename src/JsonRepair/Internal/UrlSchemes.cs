using System;

namespace JsonRepair.Internal;

/// <summary>
/// The only allowlist of schemes whose <c>://</c> belongs to an unquoted URL value.
/// A later <c>//</c> in that value is part of the URL, not a comment.
/// </summary>
internal static class UrlSchemes
{
    internal static readonly string[] Known =
    [
        "http",
        "https",
        "ftp",
        "mailto",
        "file",
        "data",
        "irc",
    ];

    internal static bool IsKnown(ReadOnlySpan<char> scheme)
    {
        foreach (string known in Known) {
            if (scheme.Equals(known, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    internal static bool IsKnown(ReadOnlySpan<byte> scheme)
    {
        foreach (string known in Known) {
            if (scheme.Length != known.Length) {
                continue;
            }

            bool match = true;
            for (int i = 0; i < known.Length; i++) {
                if (scheme[i] != (byte)known[i]) {
                    match = false;
                    break;
                }
            }

            if (match) {
                return true;
            }
        }

        return false;
    }
}
