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
            // A skipped comment is a token boundary. Keeping this set stops "n/*x*/ull" fusing into null.
            bool separatorPending = false;

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

                // Strip single-line (//) and multi-line (/* */) comments in the scan, matching the
                // UTF-8 engine. The newline after // stays in the input. A later // inside a URL is
                // consumed by the value scan, not by this strip. The comment itself is a boundary,
                // so the next raw token cannot fuse with the previous one.
                if (c == '/' && _options.StripComments) {
                    char next = index + 1 < _input.Length ? _input[index + 1] : '\0';
                    if (next == '/') {
                        index += 2;
                        while (index < _input.Length && _input[index] is not '\n' and not '\r') {
                            index++;
                        }
                        separatorPending = true;
                        continue;
                    }
                    if (next == '*') {
                        index += 2;
                        while (index + 1 < _input.Length && !(_input[index] == '*' && _input[index + 1] == '/')) {
                            index++;
                        }
                        if (index + 1 < _input.Length && _input[index] == '*' && _input[index + 1] == '/') {
                            index += 2;
                        }
                        else {
                            index = _input.Length;
                        }
                        separatorPending = true;
                        continue;
                    }
                }

                bool separated = separatorPending;
                separatorPending = false;

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

                // Object key position: inside '{' and not after ':' — literals there are keys and must be quoted, not converted.
                // Unquoted text is quoted only inside an object or array. Root prose stays prose and fails validation.
                bool inKeyPosition = _structureStack.Count > 0 && _structureStack.Peek() == '{' && !expectedValue;
                bool quoteAsValue = !inKeyPosition && _structureStack.Count > 0;

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

                // true/false/null stay literals. The word boundary keeps "trueble" a string.
                if (!inKeyPosition && TryMatchStandardLiteral(_input, index, out string? standardLiteral, out int standardLen)) {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    _sb.Append(standardLiteral);
                    index += standardLen;
                    expectedValue = false;
                    continue;
                }

                // A key may mix ASCII digits with Nd digits ({1١:1}). Values stay ASCII-only numbers.
                if (inKeyPosition && (IsAsciiDigit(c) || c == '-')) {
                    int keyLen = GetNumericKeyLength(_input, index);
                    if (keyLen > 0) {
                        if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                            EnsureCommaIfMissing();
                        }
                        ReadOnlySpan<char> keySpan = _input.Slice(index, keyLen);
                        if (_options.QuoteUnquotedKeys) {
                            _sb.Append('"');
                            _sb.Append(keySpan);
                            _sb.Append('"');
                        }
                        else {
                            _sb.Append(keySpan);
                        }
                        index += keyLen;
                        continue;
                    }
                }

                // JSON number values are ASCII digits only. U+0661 and other Nd digits are text.
                if (!inKeyPosition && (IsAsciiDigit(c) || c == '-')) {
                    int len = GetNumberLength(_input, index);
                    // A digit run glued to more value text (a date, a UUID) is an unquoted string.
                    // A pure number token, even an invalid one such as "2." or "0789", is left as a
                    // number: repairing those is number normalization, a separate repair.
                    if (len > 0 && !(quoteAsValue && ContinuesUnquotedValue(_input, index + len))) {
                        if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                            EnsureCommaIfMissing();
                        }
                        // A comment between root numbers must not glue them ("1/*x*/2" is not 12).
                        // Inside a container the comma above already separates them.
                        if (separated && _sb.Length > 0 && IsNumberFusionChar(_sb[^1])) {
                            _sb.Append(' ');
                        }
                        _sb.Append(_input.Slice(index, len));
                        expectedValue = false;
                        index += len;
                        continue;
                    }
                }

                // Unquoted object keys stay identifiers (letters, ASCII or Nd digits, '_', '-', '$').
                if (inKeyPosition && (char.IsLetter(c) || IsUnicodeDigit(c) || c is '_' or '$')) {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    int len = GetIdentifierLength(_input, index);
                    ReadOnlySpan<char> identifier = _input.Slice(index, len);

                    if (_options.QuoteUnquotedKeys) {
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

                // Unquoted string values inside an object or array: {a: hello world}, [a, b], https://...
                if (quoteAsValue && (IsUnquotedValueStart(c) || IsAsciiDigit(c) || c == '-')) {
                    int len = ScanUnquotedValueLength(_input, index);
                    if (len > 0) {
                        if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                            EnsureCommaIfMissing();
                        }
                        AppendQuoted(_input.Slice(index, len));
                        expectedValue = false;
                        index += len;
                        continue;
                    }
                }

                if (separated && _sb.Length > 0 && IsAsciiLetterOrDigit(_sb[^1]) && IsAsciiLetterOrDigit(c)) {
                    _sb.Append(' ');
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

    /// <summary>
    /// Locates where the JSON actually starts, skipping any prose, comments or markdown the model
    /// wrapped around it. Mirrors <c>Utf8JsonRepairStateMachine.FindFirstJsonToken</c> — the two
    /// engines are the same public contract and must not disagree about where a document begins.
    /// </summary>
    private static int FindFirstJsonToken(ReadOnlySpan<char> span)
    {
        bool leadingUnquoted = false;
        for (int i = 0; i < span.Length; i++) {
            char c = span[i];

            // Skip comments: prose before JSON may contain them
            if (c == '/' && i + 1 < span.Length) {
                if (span[i + 1] == '/') {
                    i += 2;
                    while (i < span.Length && span[i] is not '\n' and not '\r') {
                        i++;
                    }
                    continue;
                }
                if (span[i + 1] == '*') {
                    i += 2;
                    while (i + 1 < span.Length && !(span[i] == '*' && span[i + 1] == '/')) {
                        i++;
                    }
                    i = Math.Min(i + 1, span.Length - 1); // land on the closing '/' or at the end
                    continue;
                }
            }

            // Skip over quoted sections: brackets inside a string (e.g. input "[") are content, not tokens
            if (c is '"' or '\'') {
                char quote = c;
                int quoteStart = i;
                i++;
                while (i < span.Length && (span[i] != quote || IsEscaped(span, i))) {
                    i++;
                }
                if (i >= span.Length) {
                    // Unterminated quote: when only whitespace precedes it, the JSON starts at this quote
                    if (IsWhitespaceOnly(span[..quoteStart])) {
                        return quoteStart;
                    }
                    i = quoteStart; // otherwise it's prose (e.g. an apostrophe): keep scanning
                }
                continue;
            }

            if (c is '{' or '[') {
                return i;
            }

            // Literal candidates: only a full literal with word boundary counts; otherwise skip the prose word.
            // A literal after other unquoted text ("foo true") belongs to that text, not a new document.
            if (c is 't' or 'f' or 'n' or 'T' or 'F' or 'N' or 'u') {
                if (!leadingUnquoted && (TryMatchLiteral(span, i, out _, out _) || StartsWithStandardLiteral(span, i))) {
                    return i;
                }
                while (i < span.Length && IsIdentifierChar(span[i])) {
                    i++;
                }
                i--;
                leadingUnquoted = true;
            }
            else if (IsUnquotedValueStart(c)) {
                leadingUnquoted = true;
            }

            // Number start: only when not glued to a prose word (e.g. the "123" in "callback_123").
            // A number inside leading unquoted text ("foo 2 bar") is part of that string; a later '{' or '['
            // still wins above, so prose wrapped around an object is unchanged.
            if (!leadingUnquoted && c is (>= '0' and <= '9') or '-') {
                if (i == 0 || !IsIdentifierChar(span[i - 1])) {
                    return i;
                }
            }
        }
        return 0;
    }

    private static bool StartsWithStandardLiteral(ReadOnlySpan<char> span, int index)
    {
        ReadOnlySpan<char> slice = span[index..];
        return MatchLiteralWord(slice, "true") || MatchLiteralWord(slice, "false") || MatchLiteralWord(slice, "null");
    }

    private static bool IsWhitespaceOnly(ReadOnlySpan<char> span)
    {
        foreach (char c in span) {
            if (c is not ' ' and not '\t' and not '\n' and not '\r') {
                return false;
            }
        }
        return true;
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

    private static bool TryMatchStandardLiteral(ReadOnlySpan<char> span, int index, out string? replacement, out int length)
    {
        replacement = null;
        length = 0;
        ReadOnlySpan<char> slice = span[index..];

        if (MatchLiteralWord(slice, "true")) {
            replacement = "true";
            length = 4;
            return true;
        }
        if (MatchLiteralWord(slice, "false")) {
            replacement = "false";
            length = 5;
            return true;
        }
        if (MatchLiteralWord(slice, "null")) {
            replacement = "null";
            length = 4;
            return true;
        }

        return false;
    }

    // A literal ends at whitespace or a value stop. "trueble" and "null@example.com" are one token;
    // "@" and "©" are not boundaries, so the word is not taken as the literal and then split.
    private static bool MatchLiteralWord(ReadOnlySpan<char> slice, string word)
    {
        return slice.StartsWith(word, StringComparison.Ordinal)
            && (slice.Length == word.Length || IsValueTokenBoundary(slice, word.Length));
    }

    private static bool IsValueTokenBoundary(ReadOnlySpan<char> slice, int index)
    {
        if ((uint)index >= (uint)slice.Length) {
            return true;
        }
        char c = slice[index];
        // Same set the UTF-8 engine treats as a boundary: ASCII whitespace, NBSP, and value stops.
        // // and /* end a literal. A single '/' does not, so "true/x" stays one unquoted value.
        if (c is ' ' or '\t' or '\n' or '\r' or '\u00A0' || IsUnquotedValueStop(c)) {
            return true;
        }
        return c == '/' && index + 1 < slice.Length && slice[index + 1] is '/' or '*';
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c is '_' or '-' or '$';
    }

    private static int GetNumberLength(ReadOnlySpan<char> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            char ch = span[start + len];
            if (IsAsciiDigit(ch) || ch is '.' or '-' or '+' or 'e' or 'E') {
                len++;
            }
            else {
                break;
            }
        }
        return len;
    }

    // Object keys keep Nd digits that follow an ASCII number ("1١"), which are not JSON number values.
    private static int GetNumericKeyLength(ReadOnlySpan<char> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            char ch = span[start + len];
            if (IsAsciiDigit(ch) || IsUnicodeDigit(ch) || ch is '.' or '-' or '+' or 'e' or 'E') {
                len++;
            }
            else {
                break;
            }
        }
        return len;
    }

    private static bool IsNumberFusionChar(char c)
    {
        return c is (>= '0' and <= '9') or '.' or '-' or '+' or 'e' or 'E';
    }

    private static int GetIdentifierLength(ReadOnlySpan<char> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            char ch = span[start + len];
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '$') {
                len++;
            }
            else {
                break;
            }
        }
        return len;
    }

    // ',' '[' ']' '{' '}' CR LF quotes and curly quotes end an unquoted value.
    // '/' and '+' continue a value once it has started (paths, C++). ':' does not end one either:
    // colons belong inside values ("hello: world", URLs, dates) and only terminate keys.
    // Curly quotes (U+201C/U+201D/U+2018/U+2019) end a value; converting them is a later repair.
    // CR ends a value the same way LF does, so a bare CR cannot glue two values together.
    private static bool IsUnquotedValueStop(char c)
    {
        return c is ',' or '[' or ']' or '{' or '}' or '\n' or '\r' or '"' or '\''
            or '\u201C' or '\u201D' or '\u2018' or '\u2019';
    }

    private static bool IsAsciiDigit(char c)
    {
        return c is >= '0' and <= '9';
    }

    private static bool IsUnicodeDigit(char c)
    {
        return char.IsDigit(c) && !IsAsciiDigit(c);
    }

    private static bool IsAsciiLetterOrDigit(char c)
    {
        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
    }

    private static bool IsUnquotedValueStart(char c)
    {
        if (c is '\u201C' or '\u201D' or '\u2018' or '\u2019') {
            return false;
        }
        return c is '_' or '$' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or >= (char)128;
    }

    private static bool ContinuesUnquotedValue(ReadOnlySpan<char> input, int index)
    {
        if ((uint)index >= (uint)input.Length) {
            return false;
        }
        char c = input[index];
        // U+00A0 is whitespace here, matching the main loop, so [1\u00A0] stays a number.
        if (c is ' ' or '\t' or '\n' or '\r' or '\u00A0') {
            return false;
        }
        // // and /* end the number. A single '/' continues it ("1/2" is text, not a comment).
        if (c == '/' && index + 1 < input.Length && input[index + 1] is '/' or '*') {
            return false;
        }
        return !IsUnquotedValueStop(c);
    }

    private static int ScanUnquotedValueLength(ReadOnlySpan<char> input, int start)
    {
        int i = start;
        bool inUrl = false;
        while (i < input.Length) {
            char c = input[i];
            if (!inUrl) {
                if (IsUnquotedValueStop(c)) {
                    break;
                }

                // // and /* start a comment only at a token boundary. scheme:// enters the URL.
                if (c == '/' && i + 1 < input.Length && input[i + 1] is '/' or '*') {
                    if (input[i + 1] == '/' && IsUrlSchemeAt(input, start, i)) {
                        inUrl = true;
                        i += 2;
                        continue;
                    }
                    break;
                }
            }
            else if (!IsUrlChar(c)) {
                // Apostrophes are URL characters, so they must not use the general value stop.
                break;
            }

            i++;
        }

        while (i > start && input[i - 1] is ' ' or '\t' or '\r' or '\n') {
            i--;
        }

        return i - start;
    }

    private static bool IsUrlSchemeAt(ReadOnlySpan<char> text, int valueStart, int slashIndex)
    {
        if (slashIndex <= valueStart || text[slashIndex - 1] != ':') {
            return false;
        }
        if ((uint)slashIndex + 1 >= (uint)text.Length || text[slashIndex + 1] != '/') {
            return false;
        }

        ReadOnlySpan<char> scheme = text.Slice(valueStart, slashIndex - 1 - valueStart);
        return UrlSchemes.IsKnown(scheme);
    }

    private static bool IsUrlChar(char c)
    {
        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or '-' or '.' or '_' or '~' or ':' or '/' or '?' or '#' or '@' or '!'
            or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ';' or '=' or '%';
    }

    private void AppendQuoted(ReadOnlySpan<char> value)
    {
        _sb.Append('"');
        foreach (char c in value) {
            switch (c) {
                case '"': _sb.Append("\\\""); break;
                case '\\': _sb.Append("\\\\"); break;
                case '\b': _sb.Append("\\b"); break;
                case '\f': _sb.Append("\\f"); break;
                case '\n': _sb.Append("\\n"); break;
                case '\r': _sb.Append("\\r"); break;
                case '\t': _sb.Append("\\t"); break;
                default:
                    if (c < 32) {
                        _sb.Append($"\\u{(int)c:x4}");
                    }
                    else {
                        _sb.Append(c);
                    }
                    break;
            }
        }
        _sb.Append('"');
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
