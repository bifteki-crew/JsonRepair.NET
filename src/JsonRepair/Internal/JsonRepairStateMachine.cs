using System;
using System.Buffers;
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
        try {
            int index = 0;
            bool inString = false;
            char currentQuote = '\0';
            bool expectedValue = false;

            // Skip leading noise until first JSON token ({, [, ", ', number, or literal)
            int start = FindFirstJsonToken(_input);
            if (start > 0 && start < _input.Length) {
                index = start;
            }

            while (index < _input.Length) {
                char c = _input[index];

                if (inString) {
                    if (c == currentQuote && !IsEscaped(_input, index)) {
                        _sb.Append(currentQuote == '\'' ? '"' : currentQuote);
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
                    // Collapse consecutive whitespace outside strings to a single space unless after '{', '[', ':', or ','
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
                    _sb.Append(c == '\'' ? '"' : c);
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

                    char closeChar = c;
                    if (_structureStack.Count > 0) {
                        char open = _structureStack.Pop();
                        char matching = open == '{' ? '}' : ']';
                        if (matching != c) {
                            // Repair mismatched closing bracket by emitting the one matching the open bracket
                            closeChar = matching;
                        }
                    }

                    _sb.Append(closeChar);
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

                // Object key position: inside '{' and not after ':' — literals there are keys and must be quoted, not converted
                bool inKeyPosition = _structureStack.Count > 0 && _structureStack.Peek() == '{' && !expectedValue;

                // Check for non-standard literals (None, True, False, undefined, NaN)
                if (_options.ConvertNonStandardLiterals && !inKeyPosition && TryMatchLiteral(_input, index, out string? repairedLiteral, out int literalLen)) {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
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
                    if (inKeyPosition && _options.QuoteUnquotedKeys) {
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
                _sb.Append(currentQuote == '\'' ? '"' : currentQuote);
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
        finally {
            _structureStack.Release();
        }
    }

    private static int FindFirstJsonToken(ReadOnlySpan<char> span)
    {
        // Skip over quoted sections: a '{' or '[' inside a string (e.g. input "[") is content, not a token
        for (int i = 0; i < span.Length; i++) {
            char c = span[i];
            if (c is '"' or '\'') {
                char quote = c;
                int quoteStart = i;
                i++;
                while (i < span.Length && (span[i] != quote || IsEscaped(span, i))) {
                    i++;
                }
                if (i >= span.Length) {
                    i = quoteStart; // unterminated quote: treat it as prose and keep scanning
                }
            }
            else if (c is '{' or '[') {
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

        if (MatchLiteralWord(slice, "None")) {
            replacement = "null";
            length = 4;
            return true;
        }
        if (MatchLiteralWord(slice, "True")) {
            replacement = "true";
            length = 4;
            return true;
        }
        if (MatchLiteralWord(slice, "False")) {
            replacement = "false";
            length = 5;
            return true;
        }
        if (MatchLiteralWord(slice, "undefined")) {
            replacement = "null";
            length = 9;
            return true;
        }
        if (MatchLiteralWord(slice, "NaN")) {
            replacement = "null";
            length = 3;
            return true;
        }

        return false;
    }

    // Requires a word boundary after the literal so longer identifiers like "TrueStuff" are not corrupted
    private static bool MatchLiteralWord(ReadOnlySpan<char> slice, string word)
    {
        return slice.StartsWith(word, StringComparison.Ordinal)
            && (slice.Length == word.Length || !IsIdentifierChar(slice[word.Length]));
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c is '_' or '-';
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
        private Span<char> _buffer;
        private char[]? _rented;
        private int _count;

        public CharStack(Span<char> initialBuffer)
        {
            _buffer = initialBuffer;
            _rented = null;
            _count = 0;
        }

        public readonly int Count => _count;

        public void Push(char item)
        {
            if (_count >= _buffer.Length) {
                Grow();
            }
            _buffer[_count++] = item;
        }

        private void Grow()
        {
            int newCapacity = _buffer.Length * 2;
            char[] rented = ArrayPool<char>.Shared.Rent(newCapacity);
            _buffer[.._count].CopyTo(rented);
            if (_rented is not null) {
                ArrayPool<char>.Shared.Return(_rented);
            }
            _rented = rented;
            _buffer = rented;
        }

        public char Pop()
        {
            return _count > 0 ? _buffer[--_count] : '\0';
        }

        public readonly char Peek()
        {
            return _count > 0 ? _buffer[_count - 1] : '\0';
        }

        public void Release()
        {
            if (_rented is not null) {
                ArrayPool<char>.Shared.Return(_rented);
                _rented = null;
            }
        }
    }
}
