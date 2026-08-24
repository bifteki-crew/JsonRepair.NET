using System;
using System.Buffers;
using System.Text;

namespace JsonRepair.Internal;

internal ref struct Utf8JsonRepairStateMachine
{
    private ReadOnlySpan<byte> _input;
    private readonly JsonRepairOptions _options;
    private readonly IBufferWriter<byte> _writer;
    private ByteStack _structureStack;
    private byte _lastWrittenByte;
    private bool _pendingComma;

    public Utf8JsonRepairStateMachine(ReadOnlySpan<byte> input, JsonRepairOptions options, IBufferWriter<byte> writer, Span<byte> stackBuffer)
    {
        _options = options;
        _writer = writer;
        _structureStack = new ByteStack(stackBuffer);
        _lastWrittenByte = 0;
        _pendingComma = false;

        if (options.StripMarkdownFences) {
            input = StripMarkdownFences(input);
        }
        _input = input;
    }

    public void Repair()
    {
        try {
            int index = 0;
            bool inString = false;
            byte currentQuote = 0;
            bool expectedValue = false;

            // Skip leading noise until first JSON token ({, [, ", ', number, or literal)
            int start = FindFirstJsonToken(_input);
            if (start > 0 && start < _input.Length) {
                index = start;
            }

            while (index < _input.Length) {
                byte b = _input[index];

                if (inString) {
                    FlushPendingComma();
                    if (b == currentQuote && !IsEscaped(_input, index)) {
                        byte outQuote = currentQuote == (byte)'\'' ? (byte)'"' : currentQuote;
                        WriteByte(outQuote);
                        inString = false;
                        currentQuote = 0;
                        expectedValue = false;
                    }
                    else if (b < 32) {
                        switch (b) {
                            case (byte)'\n': WriteString("\\n"); break;
                            case (byte)'\r': WriteString("\\r"); break;
                            case (byte)'\t': WriteString("\\t"); break;
                            case (byte)'\b': WriteString("\\b"); break;
                            case (byte)'\f': WriteString("\\f"); break;
                            default: WriteHexEscape(b); break;
                        }
                    }
                    else if (b == (byte)'"' && currentQuote == (byte)'\'') {
                        WriteString("\\\"");
                    }
                    else {
                        WriteByte(b);
                    }
                    index++;
                    continue;
                }

                if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r') {
                    index++;
                    continue;
                }

                // Strip single-line (//) and multi-line (/* */) comments
                if (b == (byte)'/' && _options.StripComments) {
                    byte next = index + 1 < _input.Length ? _input[index + 1] : (byte)0;
                    if (next == (byte)'/') {
                        index += 2;
                        while (index < _input.Length && _input[index] is not (byte)'\n' and not (byte)'\r') {
                            index++;
                        }
                        continue;
                    }
                    if (next == (byte)'*') {
                        index += 2;
                        while (index + 1 < _input.Length && !(_input[index] == (byte)'*' && _input[index + 1] == (byte)'/')) {
                            index++;
                        }
                        if (index + 1 < _input.Length && _input[index] == (byte)'*' && _input[index + 1] == (byte)'/') {
                            index += 2; // Skip closing */
                        }
                        else {
                            index = _input.Length; // Unterminated block comment at end of input
                        }
                        continue;
                    }
                }

                if (b is (byte)'"' or (byte)'\'') {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    FlushPendingComma();
                    inString = true;
                    currentQuote = b;
                    byte outQuote = b == (byte)'\'' ? (byte)'"' : b;
                    WriteByte(outQuote);
                    index++;
                    continue;
                }

                if (b is (byte)'{' or (byte)'[') {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    FlushPendingComma();
                    _structureStack.Push(b);
                    WriteByte(b);
                    expectedValue = false;
                    index++;
                    continue;
                }

                if (b is (byte)'}' or (byte)']') {
                    // If trailing commas should be stripped, discard pending comma
                    if (_options.StripTrailingCommas) {
                        _pendingComma = false;
                    }
                    else {
                        FlushPendingComma();
                    }

                    byte closeByte = b;
                    if (_structureStack.Count > 0) {
                        byte open = _structureStack.Pop();
                        byte matching = open == (byte)'{' ? (byte)'}' : (byte)']';
                        if (matching != b) {
                            // Repair mismatched closing bracket by emitting the one matching the open bracket
                            closeByte = matching;
                        }
                    }

                    WriteByte(closeByte);
                    expectedValue = false;
                    index++;

                    if (_structureStack.Count == 0) {
                        break;
                    }
                    continue;
                }

                if (b == (byte)':') {
                    FlushPendingComma();
                    WriteByte((byte)':');
                    expectedValue = true;
                    index++;
                    continue;
                }

                if (b == (byte)',') {
                    _pendingComma = true;
                    expectedValue = false;
                    index++;
                    continue;
                }

                // Object key position: inside '{' and not after ':' — literals there are keys and must be quoted, not converted
                bool inKeyPosition = _structureStack.Count > 0 && _structureStack.Peek() == (byte)'{' && !expectedValue;

                // Check for non-standard literals (None, True, False, undefined, NaN)
                if (_options.ConvertNonStandardLiterals && !inKeyPosition && TryMatchLiteral(_input, index, out ReadOnlySpan<byte> repairedLiteral, out int literalLen)) {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    FlushPendingComma();
                    WriteBytes(repairedLiteral);
                    index += literalLen;
                    expectedValue = false;
                    continue;
                }

                // Check for numbers
                if (b is >= (byte)'0' and <= (byte)'9' or (byte)'-') {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    FlushPendingComma();
                    int len = GetNumberLength(_input, index);
                    WriteBytes(_input.Slice(index, len));
                    expectedValue = false;
                    index += len;
                    continue;
                }

                // Check for unquoted keys/identifiers (including non-ASCII UTF-8 bytes >= 0x80)
                if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (byte)'_' or >= 0x80) {
                    if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                        EnsureCommaIfMissing();
                    }
                    FlushPendingComma();
                    int len = GetIdentifierLength(_input, index);
                    ReadOnlySpan<byte> identifier = _input.Slice(index, len);

                    if (inKeyPosition && _options.QuoteUnquotedKeys) {
                        WriteByte((byte)'"');
                        WriteBytes(identifier);
                        WriteByte((byte)'"');
                    }
                    else {
                        WriteBytes(identifier);
                    }

                    index += len;
                    continue;
                }

                FlushPendingComma();
                WriteByte(b);
                index++;
            }

            // Auto-close unclosed strings
            if (inString && _options.AutoCloseStructures) {
                byte outQuote = currentQuote == (byte)'\'' ? (byte)'"' : currentQuote;
                WriteByte(outQuote);
            }

            // Auto-close unclosed objects/arrays
            if (_options.AutoCloseStructures) {
                while (_structureStack.Count > 0) {
                    byte open = _structureStack.Pop();
                    if (_options.StripTrailingCommas) {
                        _pendingComma = false;
                    }
                    else {
                        FlushPendingComma();
                    }
                    WriteByte(open == (byte)'{' ? (byte)'}' : (byte)']');
                }
            }
        }
        finally {
            _structureStack.Release();
        }
    }

    private static ReadOnlySpan<byte> StripMarkdownFences(ReadOnlySpan<byte> input)
    {
        input = TrimWhitespace(input);
        if (input.StartsWith("```json"u8)) {
            input = input[7..];
        }
        else if (input.StartsWith("```"u8)) {
            input = input[3..];
        }

        if (input.EndsWith("```"u8)) {
            input = input[..^3];
        }

        return TrimWhitespace(input);
    }

    private static ReadOnlySpan<byte> TrimWhitespace(ReadOnlySpan<byte> input)
    {
        int start = 0;
        while (start < input.Length && input[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') {
            start++;
        }
        int end = input.Length - 1;
        while (end >= start && input[end] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') {
            end--;
        }
        return start <= end ? input.Slice(start, end - start + 1) : ReadOnlySpan<byte>.Empty;
    }

    private static int FindFirstJsonToken(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++) {
            byte b = span[i];

            // Skip comments: prose before JSON may contain them
            if (b == (byte)'/' && i + 1 < span.Length) {
                if (span[i + 1] == (byte)'/') {
                    i += 2;
                    while (i < span.Length && span[i] is not (byte)'\n' and not (byte)'\r') {
                        i++;
                    }
                    continue;
                }
                if (span[i + 1] == (byte)'*') {
                    i += 2;
                    while (i + 1 < span.Length && !(span[i] == (byte)'*' && span[i + 1] == (byte)'/')) {
                        i++;
                    }
                    i = Math.Min(i + 1, span.Length - 1); // land on the closing '/' or at the end
                    continue;
                }
            }

            // Skip over quoted sections: brackets inside a string (e.g. input "[") are content, not tokens
            if (b is (byte)'"' or (byte)'\'') {
                byte quote = b;
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

            if (b is (byte)'{' or (byte)'[') {
                return i;
            }

            // Number start: only when not glued to a prose word (e.g. the "123" in "callback_123")
            if (b is (>= (byte)'0' and <= (byte)'9') or (byte)'-') {
                if (i == 0 || !IsIdentifierChar(span[i - 1])) {
                    return i;
                }
            }

            // Literal candidates: only a full literal with word boundary counts; otherwise skip the prose word
            if (b is (byte)'t' or (byte)'f' or (byte)'n' or (byte)'T' or (byte)'F' or (byte)'N' or (byte)'u') {
                if (TryMatchLiteral(span, i, out _, out _) || StartsWithStandardLiteral(span, i)) {
                    return i;
                }
                while (i < span.Length && IsIdentifierChar(span[i])) {
                    i++;
                }
                i--;
            }
        }
        return 0;
    }

    private static bool StartsWithStandardLiteral(ReadOnlySpan<byte> span, int index)
    {
        ReadOnlySpan<byte> slice = span[index..];
        return MatchLiteralWord(slice, "true"u8) || MatchLiteralWord(slice, "false"u8) || MatchLiteralWord(slice, "null"u8);
    }

    private static bool IsWhitespaceOnly(ReadOnlySpan<byte> span)
    {
        foreach (byte b in span) {
            if (b is not (byte)' ' and not (byte)'\t' and not (byte)'\n' and not (byte)'\r') {
                return false;
            }
        }
        return true;
    }

    private void EnsureCommaIfMissing()
    {
        if (!_pendingComma && _lastWrittenByte is not 0 and not (byte)'{' and not (byte)'[' and not (byte)':' and not (byte)',') {
            _pendingComma = true;
        }
    }

    private void FlushPendingComma()
    {
        if (_pendingComma) {
            WriteByte((byte)',');
            _pendingComma = false;
        }
    }

    private static bool IsEscaped(ReadOnlySpan<byte> span, int index)
    {
        int count = 0;
        for (int i = index - 1; i >= 0 && span[i] == (byte)'\\'; i--) {
            count++;
        }
        return (count % 2) != 0;
    }

    private static bool TryMatchLiteral(ReadOnlySpan<byte> span, int index, out ReadOnlySpan<byte> replacement, out int length)
    {
        replacement = default;
        length = 0;
        ReadOnlySpan<byte> slice = span[index..];

        if (MatchLiteralWord(slice, "None"u8)) {
            replacement = "null"u8;
            length = 4;
            return true;
        }
        if (MatchLiteralWord(slice, "True"u8)) {
            replacement = "true"u8;
            length = 4;
            return true;
        }
        if (MatchLiteralWord(slice, "False"u8)) {
            replacement = "false"u8;
            length = 5;
            return true;
        }
        if (MatchLiteralWord(slice, "undefined"u8)) {
            replacement = "null"u8;
            length = 9;
            return true;
        }
        if (MatchLiteralWord(slice, "NaN"u8)) {
            replacement = "null"u8;
            length = 3;
            return true;
        }

        return false;
    }

    // Requires a word boundary after the literal so longer identifiers like "TrueStuff" are not corrupted
    private static bool MatchLiteralWord(ReadOnlySpan<byte> slice, ReadOnlySpan<byte> word)
    {
        return slice.StartsWith(word)
            && (slice.Length == word.Length || !IsIdentifierChar(slice[word.Length]));
    }

    private static bool IsIdentifierChar(byte b)
    {
        return b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_' or (byte)'-' or >= 0x80;
    }

    private static int GetNumberLength(ReadOnlySpan<byte> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            byte b = span[start + len];
            if (b is (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'-' or (byte)'+' or (byte)'e' or (byte)'E') {
                len++;
            }
            else {
                break;
            }
        }
        return len;
    }

    private static int GetIdentifierLength(ReadOnlySpan<byte> span, int start)
    {
        int len = 0;
        while (start + len < span.Length) {
            byte b = span[start + len];
            if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_' or (byte)'-' or >= 0x80) {
                len++;
            }
            else {
                break;
            }
        }
        return len;
    }

    private void WriteByte(byte b)
    {
        Span<byte> span = _writer.GetSpan(1);
        span[0] = b;
        _writer.Advance(1);
        if (b is not (byte)' ' and not (byte)'\t' and not (byte)'\n' and not (byte)'\r') {
            _lastWrittenByte = b;
        }
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> span = _writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _writer.Advance(bytes.Length);

        for (int i = bytes.Length - 1; i >= 0; i--) {
            byte b = bytes[i];
            if (b is not (byte)' ' and not (byte)'\t' and not (byte)'\n' and not (byte)'\r') {
                _lastWrittenByte = b;
                break;
            }
        }
    }

    private void WriteString(string s)
    {
        int byteCount = Encoding.UTF8.GetByteCount(s);
        Span<byte> span = _writer.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(s, span);
        _writer.Advance(byteCount);

        if (byteCount > 0) {
            _lastWrittenByte = span[byteCount - 1];
        }
    }

    private void WriteHexEscape(byte b)
    {
        Span<byte> span = _writer.GetSpan(6);
        span[0] = (byte)'\\';
        span[1] = (byte)'u';
        span[2] = (byte)'0';
        span[3] = (byte)'0';
        span[4] = GetHexDigit(b >> 4);
        span[5] = GetHexDigit(b & 0x0F);
        _writer.Advance(6);
        _lastWrittenByte = span[5];
    }

    private static byte GetHexDigit(int val)
    {
        return (byte)(val < 10 ? '0' + val : 'a' + (val - 10));
    }

    private ref struct ByteStack
    {
        private Span<byte> _buffer;
        private byte[]? _rented;
        private int _count;

        public ByteStack(Span<byte> initialBuffer)
        {
            _buffer = initialBuffer;
            _rented = null;
            _count = 0;
        }

        public readonly int Count => _count;

        public void Push(byte item)
        {
            if (_count >= _buffer.Length) {
                Grow();
            }
            _buffer[_count++] = item;
        }

        private void Grow()
        {
            int newCapacity = _buffer.Length * 2;
            byte[] rented = ArrayPool<byte>.Shared.Rent(newCapacity);
            _buffer[.._count].CopyTo(rented);
            if (_rented is not null) {
                ArrayPool<byte>.Shared.Return(_rented);
            }
            _rented = rented;
            _buffer = rented;
        }

        public byte Pop()
        {
            return _count > 0 ? _buffer[--_count] : (byte)0;
        }

        public readonly byte Peek()
        {
            return _count > 0 ? _buffer[_count - 1] : (byte)0;
        }

        public void Release()
        {
            if (_rented is not null) {
                ArrayPool<byte>.Shared.Return(_rented);
                _rented = null;
            }
        }
    }
}
