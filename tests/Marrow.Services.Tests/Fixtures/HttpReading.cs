using System.IO.Compression;
using System.Text;
using Marrow.Services.Port;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test helpers for the HTTP unit's tests (HttpTests, HttpSecondTests): reading a byte or line sequence
// under the hang guard, compressing a body the way a server would, and splitting a raw request head. Nothing here
// computes an expected value; the tests transcribe those from the Swift and the records.
public static class HttpReading
{
    private static TimeSpan Guard => LoopbackHttpServer.Guard;

    public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Reads exactly <paramref name="count"/> bytes, failing if the stream ends first.</summary>
    public static async Task<byte[]> ReadExactly(IAsyncEnumerator<byte> reader, int count)
    {
        var read = new List<byte>(count);
        while (read.Count < count)
        {
            Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Guard), "the stream ended early");
            read.Add(reader.Current);
        }
        return [.. read];
    }

    public static async Task<byte[]> ReadToEnd(IAsyncEnumerator<byte> reader)
    {
        var read = new List<byte>();
        while (await reader.MoveNextAsync().AsTask().WaitAsync(Guard)) read.Add(reader.Current);
        return [.. read];
    }

    /// <summary>Every line `bytes.lines` yields, in order, until the sequence ends.</summary>
    public static async Task<List<string>> ReadLines(URLSession.AsyncBytes bytes)
    {
        var lines = new List<string>();
        await using var reader = bytes.Lines.GetAsyncEnumerator();
        while (await reader.MoveNextAsync().AsTask().WaitAsync(Guard)) lines.Add(reader.Current);
        return lines;
    }

    /// <summary>The body encoded as a server sends it with that Content-Encoding ("deflate" is zlib, RFC 9110 8.4.1.2).</summary>
    public static byte[] Compress(byte[] plain, string coding)
    {
        using var output = new MemoryStream();
        using (Stream compressor = coding switch
        {
            "gzip" => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "deflate" => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "br" => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(coding)),
        })
        {
            compressor.Write(plain);
        }
        return output.ToArray();
    }

    /// <summary>The header lines of a raw request head exactly as sent, without the request line and the final empty line.</summary>
    public static IReadOnlyList<string> RawHeaderLines(byte[] rawHead)
    {
        var lines = Encoding.Latin1.GetString(rawHead).Split("\r\n");
        // lines[0] is the request line; the head ends with "\r\n\r\n", which leaves two empty strings at the end.
        return lines[1..^2];
    }
}
