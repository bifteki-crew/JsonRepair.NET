using System;
using System.Buffers;
using System.Text;

namespace JsonRepair.Internal;

internal ref struct Utf8JsonRepairStateMachine
{
    private readonly ReadOnlySpan<byte> _input;
    private readonly JsonRepairOptions _options;
    private readonly IBufferWriter<byte> _writer;
    private ByteStack _structureStack;
    private byte _lastWrittenByte;

    public Utf8JsonRepairStateMachine(ReadOnlySpan<byte> input, JsonRepairOptions options, IBufferWriter<byte> writer, Span<byte> stackBuffer)
    {
        _input = input;
        _options = options;
        _writer = writer;
        _structureStack = new ByteStack(stackBuffer);
        _lastWrittenByte = 0;
    }

    public void Repair()
    {
        int index = 0;
        bool inString = false;
        byte currentQuote = 0;
        bool expectedValue = false;

        // Skip leading noise until first '{' (0x7B) or '[' (0x5B)
        int start = FindFirstJsonToken(_input);
        if (start > 0 && start < _input.Length) {
            index = start;
        }

        while (index < _input.Length) {
            byte b = _input[index];

            if (inString) {
                if (b == currentQuote && !IsEscaped(_input, index)) {
                    WriteByte((byte)'"');
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
                        default: WriteString($"\\u{(int)b:x4}"); break;
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

            if (b is (byte)'"' or (byte)'\'') {
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
                inString = true;
                currentQuote = b;
                WriteByte((byte)'"');
                index++;
                continue;
            }

            if (b is (byte)'{' or (byte)'[') {
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
                _structureStack.Push(b);
                WriteByte(b);
                expectedValue = false;
                index++;
                continue;
            }

            if (b is (byte)'}' or (byte)']') {
                if (_structureStack.Count > 0) {
                    _structureStack.Pop();
                }

                WriteByte(b);
                expectedValue = false;
                index++;

                if (_structureStack.Count == 0) {
                    break;
                }
                continue;
            }

            if (b == (byte)':') {
                WriteByte((byte)':');
                expectedValue = true;
                index++;
                continue;
            }

            if (b == (byte)',') {
                WriteByte((byte)',');
                expectedValue = false;
                index++;
                continue;
            }

            // Check for non-standard literals (None, True, False, undefined, NaN)
            if (_options.ConvertNonStandardLiterals && TryMatchLiteral(_input, index, out ReadOnlySpan<byte> repairedLiteral, out int literalLen)) {
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
                int len = GetNumberLength(_input, index);
                WriteBytes(_input.Slice(index, len));
                expectedValue = false;
                index += len;
                continue;
            }

            // Check for unquoted keys/identifiers
            if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (byte)'_') {
                if (!expectedValue && _structureStack.Count > 0 && _options.InsertMissingCommas) {
                    EnsureCommaIfMissing();
                }
                int len = GetIdentifierLength(_input, index);
                ReadOnlySpan<byte> identifier = _input.Slice(index, len);

                if (_structureStack.Count > 0 && _structureStack.Peek() == (byte)'{' && !expectedValue && _options.QuoteUnquotedKeys) {
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

            WriteByte(b);
            index++;
        }

        // Auto-close unclosed strings
        if (inString && _options.AutoCloseStructures) {
            WriteByte((byte)'"');
        }

        // Auto-close unclosed objects/arrays
        if (_options.AutoCloseStructures) {
            while (_structureStack.Count > 0) {
                byte open = _structureStack.Pop();
                WriteByte(open == (byte)'{' ? (byte)'}' : (byte)']');
            }
        }
    }

    private static int FindFirstJsonToken(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++) {
            if (span[i] is (byte)'{' or (byte)'[') {
                return i;
            }
        }
        return 0;
    }

    private void EnsureCommaIfMissing()
    {
        if (_lastWrittenByte is not 0 and not (byte)'{' and not (byte)'[' and not (byte)':' and not (byte)',') {
            WriteByte((byte)',');
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

        if (slice.StartsWith("None"u8)) {
            replacement = "null"u8;
            length = 4;
            return true;
        }
        if (slice.StartsWith("True"u8)) {
            replacement = "true"u8;
            length = 4;
            return true;
        }
        if (slice.StartsWith("False"u8)) {
            replacement = "false"u8;
            length = 5;
            return true;
        }
        if (slice.StartsWith("undefined"u8)) {
            replacement = "null"u8;
            length = 9;
            return true;
        }
        if (slice.StartsWith("NaN"u8)) {
            replacement = "null"u8;
            length = 3;
            return true;
        }

        return false;
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
            if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_' or (byte)'-') {
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

    private ref struct ByteStack
    {
        private readonly Span<byte> _buffer;
        private int _count;

        public ByteStack(Span<byte> initialBuffer)
        {
            _buffer = initialBuffer;
            _count = 0;
        }

        public readonly int Count => _count;

        public void Push(byte item)
        {
            if (_count < _buffer.Length) {
                _buffer[_count++] = item;
            }
        }

        public byte Pop()
        {
            return _count > 0 ? _buffer[--_count] : (byte)0;
        }

        public readonly byte Peek()
        {
            return _count > 0 ? _buffer[_count - 1] : (byte)0;
        }
    }
}
