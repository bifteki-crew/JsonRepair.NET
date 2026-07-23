using System;
using System.Text;

namespace JsonRepair.Internal;

internal ref struct JsonRepairStateMachine
{
    private readonly ReadOnlySpan<char> _input;
    private readonly JsonRepairOptions _options;
    private readonly StringBuilder _sb;
    private CharStack _structureStack;

    public JsonRepairStateMachine(ReadOnlySpan<char> input, JsonRepairOptions options, Span<char> stackBuffer)
    {
        _input = input;
        _options = options;
        _sb = new StringBuilder(input.Length + 32);
        _structureStack = new CharStack(stackBuffer);
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
                if (c == currentQuote && !IsEscaped(_input, index)) {
                    _sb.Append('"');
                    inString = false;
                    currentQuote = '\0';
                    expectedValue = false;
                }
                else if (c < 32) {
                    switch (c) {
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        case '\b': _sb.Append("\\b"); break;
                        case '\f': _sb.Append("\\f"); break;
                        default: _sb.Append($"\\u{(int)c:x4}"); break;
                    }
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
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
                inString = true;
                currentQuote = c;
                _sb.Append('"');
                index++;
                continue;
            }

            if (c == '{' || c == '[') {
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
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

            // Check for numbers (digits or leading minus)
            if (char.IsDigit(c) || c == '-') {
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
                int len = GetNumberLength(_input, index);
                ReadOnlySpan<char> numSpan = _input.Slice(index, len);
                _sb.Append(numSpan);
                expectedValue = false;
                index += len;
                continue;
            }

            // Check for unquoted keys or unquoted values
            if (char.IsLetter(c) || c == '_') {
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
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

    private void EnsureCommaIfMissing()
    {
        int i = _sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(_sb[i])) {
            i--;
        }

        if (i >= 0) {
            char last = _sb[i];
            if (last is not '{' and not '[' and not ':' and not ',') {
                _sb.Append(',');
            }
        }
    }

    private static void TrimTrailingComma(StringBuilder sb)
    {
        int i = sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(sb[i])) {
            i--;
        }

        if (i >= 0 && sb[i] == ',') {
            i--;
            while (i >= 0 && char.IsWhiteSpace(sb[i])) {
                i--;
            }
            sb.Length = i + 1;
        }
    }

    private static bool IsEscaped(ReadOnlySpan<char> span, int index)
    {
        int backslashCount = 0;
        for (int i = index - 1; i >= 0 && span[i] == '\\'; i--) {
            backslashCount++;
        }
        return (backslashCount % 2) != 0;
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

    private static int GetNumberLength(ReadOnlySpan<char> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            char ch = span[start + len];
            if (char.IsDigit(ch) || ch is '.' or '-' or '+' or 'e' or 'E') {
                len++;
            }
            else {
                break;
            }
        }
        return len;
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

    private ref struct CharStack
    {
        private readonly Span<char> _buffer;
        private int _count;

        public CharStack(Span<char> initialBuffer)
        {
            _buffer = initialBuffer;
            _count = 0;
        }

        public readonly int Count => _count;

        public void Push(char item)
        {
            if (_count < _buffer.Length) {
                _buffer[_count++] = item;
            }
        }

        public char Pop()
        {
            return _count > 0 ? _buffer[--_count] : '\0';
        }

        public readonly char Peek()
        {
            return _count > 0 ? _buffer[_count - 1] : '\0';
        }
    }
}
