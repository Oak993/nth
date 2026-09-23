using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/StdioFramer.swift (B2 1fbf81e), :1-81.

/// <summary>
/// :3 <c>struct StdioFramer</c>: incremental byte framing for a child's stdout, split on 0x0A (PR-PROVACP-149) or
/// read as Content-Length blocks (PR-PROVACP-151, PR-PROVACP-152).
/// ENGINEERING DECISION: a sealed class, not an immutable record (R-VAL-1 covers model values). The Swift struct is
/// a mutable container whose <c>mutating</c> methods change it in place, and its one construction site
/// (StdioProcess.swift:36) stores it in <c>State.framer</c> (:24) and only ever mutates it there under the lock
/// (:142, :154). With no copy anywhere, a class instance behaves identically.
/// </summary>
public sealed class StdioFramer
{
    // :4-7
    public enum Framing
    {
        Lines,
        ContentLength,
    }

    // :9
    private readonly Framing framing;

    // :10 `private var buffer = Data()`. The bytes held are buffer[0..<count]; Append grows the array geometrically
    // (Data.append's amortized growth, :20) and every consuming append replaces it with a fresh array holding
    // exactly the unconsumed remainder, or [] when none remains (:59, ED-147).
    // Platform limit: a .NET byte[] holds at most Array.MaxLength bytes, so an unterminated line or header block of
    // 2 GiB or more throws from Append (OverflowException or OutOfMemoryException), where Swift Data keeps growing.
    // ED-147 fixes this single-array storage; the limit is reported, not worked around.
    private byte[] buffer = [];
    private int count;

    // :11
    private int scanned;

    // :12 `Int?`, 64-bit (R-NUM-4).
    private long? bodyLength;

    // :13 `Data("\r\n\r\n".utf8)`
    private static ReadOnlySpan<byte> Separator => "\r\n\r\n"u8;

    // :15-17
    public StdioFramer(Framing framing)
    {
        this.framing = framing;
    }

    // :19-61
    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> data)
    {
        // :20 buffer.append(data)
        var required = checked(count + data.Length);
        if (required > buffer.Length)
        {
            var capacity = Math.Max(required, (int)Math.Min((long)buffer.Length * 2, Array.MaxLength));
            Array.Resize(ref buffer, capacity);
        }
        data.CopyTo(buffer.AsSpan(count));
        count = required;
        // :21-23
        var messages = new List<byte[]>();
        var start = 0;
        var search = start + scanned;
        // :24-57
        while (true)
        {
            switch (framing)
            {
                case Framing.Lines:
                {
                    // :26-34
                    var relative = buffer.AsSpan(search, count - search).IndexOf((byte)0x0A);
                    if (relative < 0)
                    {
                        search = count;
                        break;
                    }
                    var newline = search + relative;
                    messages.Add(buffer.AsSpan(start, newline - start).ToArray());
                    start = newline + 1;
                    search = start;
                    continue;
                }
                case Framing.ContentLength:
                {
                    // :35-46
                    if (bodyLength is null)
                    {
                        var relative = buffer.AsSpan(search, count - search).IndexOf(Separator);
                        if (relative < 0)
                        {
                            search = Math.Max(start, count - 3);
                            break;
                        }
                        var headerEnd = search + relative;
                        var header = SwiftText.DecodeUtf8Repairing(buffer.AsSpan(start, headerEnd - start));
                        start = headerEnd + Separator.Length;
                        search = start;
                        if (ContentLength(header) is not { } parsed) continue;
                        bodyLength = parsed;
                    }
                    // :47-54. The buffered byte count bounds the narrowing; a valid Int64 length need not fit Int32.
                    if (bodyLength is { } length && count - start >= length)
                    {
                        var end = start + checked((int)length);
                        messages.Add(buffer.AsSpan(start, end - start).ToArray());
                        bodyLength = null;
                        start = end;
                        search = start;
                        continue;
                    }
                    break;
                }
            }
            // :56
            break;
        }
        // :58
        scanned = search - start;
        // :59 `if start != buffer.startIndex { buffer = Data(buffer[start...]) }` (ED-147)
        if (start != 0)
        {
            count -= start;
            buffer = count == 0 ? [] : buffer.AsSpan(start, count).ToArray();
        }
        // :60
        return messages;
    }

    // :63-69
    public byte[]? Finish()
    {
        var remainder = framing == Framing.Lines && count != 0 ? buffer.AsSpan(0, count).ToArray() : null;
        buffer = [];
        count = 0;
        scanned = 0;
        bodyLength = null;
        return remainder;
    }

    // :71-80
    private static long? ContentLength(string header)
    {
        // :72 `header.split(whereSeparator: \.isNewline)`. Character.isNewline reads the first scalar: LF, VT, FF,
        // CR, NEL, U+2028, U+2029. Each is a grapheme boundary on both sides except CR LF, which is one Character, so
        // splitting on these UTF-16 units and dropping empty pieces (Swift omits empty subsequences) gives the same
        // lines.
        foreach (var line in header.Split(
                     [
                         '\n', '\r', '\v', '\f',
                         '\U00000085', // U+0085 NEXT LINE (NEL)
                         '\U00002028', // U+2028 LINE SEPARATOR
                         '\U00002029', // U+2029 PARAGRAPH SEPARATOR
                     ],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            // :73 `line.split(separator: ":", maxSplits: 1)`
            var parts = SwiftString.Split(line, ":", maxSplits: 1);
            // :74-75
            if (parts.Count != 2 || !SwiftString.Equal(SwiftString.Lowercased(FoundationText.Trim(parts[0])), "content-length"))
                continue;
            // :76 `Int(...)`, `length >= 0`
            if (SwiftNumber.ParseInt(FoundationText.Trim(parts[1])) is not { } length || length < 0) return null;
            // :77
            return length;
        }
        // :79
        return null;
    }
}
