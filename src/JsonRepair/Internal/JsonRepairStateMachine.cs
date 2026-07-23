using System;
using System.Collections.Generic;
using System.Text;

namespace JsonRepair.Internal;

internal ref struct JsonRepairStateMachine
{
    private readonly ReadOnlySpan<char> _input;
    private readonly JsonRepairOptions _options;
    private readonly StringBuilder _sb;
    private readonly Stack<char> _structureStack;

    public JsonRepairStateMachine(ReadOnlySpan<char> input, JsonRepairOptions options)
    {
        _input = input;
        _options = options;
        _sb = new StringBuilder(input.Length + 32);
        _structureStack = new Stack<char>();
    }

    public string Repair()
    {
        int index = 0;
        bool inString = false;
        char currentQuote = '\0';
        bool expectedValue = false;

        // Skip leading noise until first '{' or '['
        int start = FindFirstJsonToken(_input);
        if (start > 0 && start < _input.Length) {
            index = start;
        }

        while (index < _input.Length) {
            char c = _input[index];

            if (inString) {
                if (c == currentQuote && (index == 0 || _input[index - 1] != '\\')) {
                    _sb.Append('"');
                    inString = false;
                    currentQuote = '\0';
                    expectedValue = false;
                }
                else if (c == '\n') {
                    _sb.Append("\\n");
                }
                else if (c == '\r') {
                    _sb.Append("\\r");
                }
                else if (c == '\t') {
                    _sb.Append("\\t");
                }
                else if (c == '"' && currentQuote == '\'') {
                    _sb.Append("\\\"");
                }
                else {
                    _sb.Append(c);
                }
                index++;
                continue;
            }

            if (char.IsWhiteSpace(c)) {
                // Collapse consecutive whitespace outside strings to a single space unless after '{', '[', ':', ',' or before '}', ']'
                if (_sb.Length > 0 && !char.IsWhiteSpace(_sb[^1]) && _sb[^1] is not '{' and not '[' and not ':' and not ',') {
                    _sb.Append(' ');
                }
                index++;
                continue;
            }

            if (c is '"' or '\'') {
                inString = true;
                currentQuote = c;
                _sb.Append('"');
                index++;
                continue;
            }

            if (c == '{' || c == '[') {
                _structureStack.Push(c);
                _sb.Append(c);
                expectedValue = false;
                index++;
                continue;
            }

            if (c == '}' || c == ']') {
                // Strip trailing comma if present
                if (_options.StripTrailingCommas) {
                    TrimTrailingComma(_sb);
                }

                if (_structureStack.Count > 0) {
                    _structureStack.Pop();
                }

                _sb.Append(c);
                expectedValue = false;
                index++;

                // Check if we reached the root closing brace
                if (_structureStack.Count == 0) {
                    break;
                }
                continue;
            }

            if (c == ':') {
                _sb.Append(':');
                expectedValue = true;
                index++;
                continue;
            }

            if (c == ',') {
                _sb.Append(',');
                expectedValue = false;
                index++;
                continue;
            }

            // Check for non-standard literals (None, True, False, undefined, NaN)
            if (_options.ConvertNonStandardLiterals && TryMatchLiteral(_input, index, out string? repairedLiteral, out int literalLen)) {
                _sb.Append(repairedLiteral);
                index += literalLen;
                expectedValue = false;
                continue;
            }

            // Check for unquoted keys or unquoted values
            if (char.IsLetter(c) || c == '_') {
                int len = GetIdentifierLength(_input, index);
                ReadOnlySpan<char> identifier = _input.Slice(index, len);

                // If inside an object and expecting a key (not after ':')
                if (_structureStack.Count > 0 && _structureStack.Peek() == '{' && !expectedValue && _options.QuoteUnquotedKeys) {
                    _sb.Append('"');
                    _sb.Append(identifier);
                    _sb.Append('"');
                }
                else {
                    _sb.Append(identifier);
                }

                index += len;
                continue;
            }

            _sb.Append(c);
            index++;
        }

        // Auto-close unclosed strings
        if (inString && _options.AutoCloseStructures) {
            _sb.Append('"');
        }

        // Auto-close unclosed objects/arrays
        if (_options.AutoCloseStructures) {
            while (_structureStack.Count > 0) {
                char open = _structureStack.Pop();
                TrimTrailingComma(_sb);
                _sb.Append(open == '{' ? '}' : ']');
            }
        }

        return _sb.ToString();
    }

    private static int FindFirstJsonToken(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++) {
            if (span[i] is '{' or '[') {
                return i;
            }
        }
        return 0;
    }

    private static void TrimTrailingComma(StringBuilder sb)
    {
        int i = sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(sb[i])) {
            i--;
        }

        if (i >= 0 && sb[i] == ',') {
            sb.Remove(i, sb.Length - i);
            i--;
            while (i >= 0 && char.IsWhiteSpace(sb[i])) {
                i--;
            }
        }

        if (i < sb.Length - 1) {
            sb.Remove(i + 1, sb.Length - (i + 1));
        }
    }

    private static bool TryMatchLiteral(ReadOnlySpan<char> span, int index, out string? replacement, out int length)
    {
        replacement = null;
        length = 0;

        ReadOnlySpan<char> slice = span[index..];

        if (slice.StartsWith("None", StringComparison.Ordinal)) {
            replacement = "null";
            length = 4;
            return true;
        }
        if (slice.StartsWith("True", StringComparison.Ordinal)) {
            replacement = "true";
            length = 4;
            return true;
        }
        if (slice.StartsWith("False", StringComparison.Ordinal)) {
            replacement = "false";
            length = 5;
            return true;
        }
        if (slice.StartsWith("undefined", StringComparison.Ordinal)) {
            replacement = "null";
            length = 9;
            return true;
        }
        if (slice.StartsWith("NaN", StringComparison.Ordinal)) {
            replacement = "null";
            length = 3;
            return true;
        }

        return false;
    }

    private static int GetIdentifierLength(ReadOnlySpan<char> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            char ch = span[start + len];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') {
                len++;
            }
            else {
                break;
            }
        }
        return len;
    }
}
