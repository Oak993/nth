using Marrow.Core.Port;
using System.Globalization;
using System.Text;

namespace Marrow.Core.Support;

/// <summary>Incremental byte framing from Core/Support/StdioFramer.swift.</summary>
public sealed class StdioFramer(StdioFramer.Framing framing)
{
    public enum Framing { Lines, ContentLength }

    private byte[] buffer = [];
    private int count;
    private int scanned;
    private long? bodyLength;

    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> data)
    {
        var required = checked(count + data.Length);
        if (required > buffer.Length)
        {
            // Geometric capacity replaces Data's amortized append storage, not a wire limit.
            var capacity = Math.Max(required, (int)Math.Min((long)buffer.Length * 2, Array.MaxLength));
            Array.Resize(ref buffer, capacity);
        }
        data.CopyTo(buffer.AsSpan(count));
        count = required;
        List<byte[]>? messages = null;
        var start = 0;
        var search = scanned;
        while (true)
        {
            if (framing == Framing.Lines)
            {
                var relative = buffer.AsSpan(search, count - search).IndexOf((byte)0x0A);
                if (relative < 0)
                {
                    search = count;
                    break;
                }
                var newline = search + relative;
                (messages ??= []).Add(buffer.AsSpan(start, newline - start).ToArray());
                start = newline + 1;
                search = start;
                continue;
            }

            if (bodyLength is null)
            {
                var relative = buffer.AsSpan(search, count - search).IndexOf("\r\n\r\n"u8);
                if (relative < 0)
                {
                    search = Math.Max(start, count - 3);
                    break;
                }
                var headerEnd = search + relative;
                var header = Encoding.UTF8.GetString(buffer.AsSpan(start, headerEnd - start));
                start = headerEnd + "\r\n\r\n"u8.Length;
                search = start;
                bodyLength = ContentLength(header);
                if (bodyLength is null) continue;
            }
            if (bodyLength is { } length && count - start >= length)
            {
                // The buffered byte count bounds the cast; a valid Int64 header need not fit Int32.
                var end = start + (int)length;
                (messages ??= []).Add(buffer.AsSpan(start, (int)length).ToArray());
                bodyLength = null;
                start = end;
                search = start;
                continue;
            }
            break;
        }

        scanned = search - start;
        if (start != 0)
        {
            count -= start;
            if (count == 0) buffer = [];
            else buffer.AsSpan(start, count).CopyTo(buffer);
        }
        return messages is null ? Array.Empty<byte[]>() : messages;
    }

    public byte[]? Finish()
    {
        var remainder = framing == Framing.Lines && count != 0
            ? buffer.AsSpan(0, count).ToArray() : null;
        buffer = [];
        count = 0;
        scanned = 0;
        bodyLength = null;
        return remainder;
    }

    private static long? ContentLength(string header)
    {
        // Character.isNewline includes these scalar values (CRLF is one Character).
        foreach (var line in header.Split(['\n', '\r', '\v', '\f', '\u0085', '\u2028', '\u2029'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            // Swift split(maxSplits: 1) omits empty leading subsequences. A colon
            // combined with a mark is not the separator Character ":".
            var elements = StringInfo.GetTextElementEnumerator(line);
            var partStart = 0;
            var colon = -1;
            while (elements.MoveNext())
            {
                if (elements.GetTextElement() != ":") continue;
                var index = elements.ElementIndex;
                if (index == partStart) { partStart = index + 1; continue; }
                colon = index;
                break;
            }
            if (colon < 0 || colon == line.Length - 1) continue;
            var name = FoundationText.Trim(line.AsSpan(partStart, colon - partStart));
            if (name.ToString().ToLowerInvariant() != "content-length") continue;
            var value = FoundationText.Trim(line.AsSpan(colon + 1));
            if (value.IsEmpty) return null;
            var digitStart = value[0] is '+' or '-' ? 1 : 0;
            if (digitStart == value.Length) return null;
            for (var i = digitStart; i < value.Length; i++)
                if (value[i] is < '0' or > '9') return null;
            return long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var length)
                && length >= 0 ? length : null;
        }
        return null;
    }

}
