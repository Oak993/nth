using System.IO.Compression;
using System.Security.Cryptography;

namespace Marrow.Testing.Downloads;

// PORT-ONLY test fixture, no Swift twin (SK-0 = DL-0 of skills-ONDEMAND-PLAN.md section 7; shared by SK-3, VT-7 and UE-2): the
// bytes a FakeDownloadServer serves, with the length and SHA-256 a downloader would pin. Fixture artifacts are small Marrow-made
// bytes (a pattern, a text, a zip); no vendor file is ever a fixture (ONDEMAND-PLAN section 7, "Rules for every unit").
public sealed class FakeArtifact
{
    public FakeArtifact(byte[] bytes)
    {
        Bytes = bytes;
        Sha256 = SHA256.HashData(bytes);
    }

    public byte[] Bytes { get; }

    public long Length => Bytes.Length;

    public byte[] Sha256 { get; }

    /// <summary>The SHA-256 as 64 lower-case hex digits, the form a catalogue pins.</summary>
    public string Sha256Hex => Convert.ToHexStringLower(Sha256);

    public static FakeArtifact FromText(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// <paramref name="length"/> deterministic bytes (an xorshift stream seeded by <paramref name="seed"/>): the same call gives the
    /// same bytes on every run and platform, and no two seeds share a prefix, so a resumed download that re-fetched or skipped a
    /// byte cannot hash to the right value by luck.
    /// </summary>
    public static FakeArtifact Pattern(int length, int seed = 1)
    {
        var bytes = new byte[length];
        // The project builds with overflow checking on; the stream is meant to wrap.
        unchecked
        {
            var state = 0x9E3779B97F4A7C15UL ^ ((ulong)(uint)seed * 0xBF58476D1CE4E5B9UL);
            for (var i = 0; i < bytes.Length; i++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                bytes[i] = (byte)(state >> 32);
            }
        }
        return new FakeArtifact(bytes);
    }

    /// <summary>
    /// A zip of the given entries, entry times fixed so the same call gives the same bytes. Entry paths are written as given
    /// (a test of zip-slip refusal can name "../x"; the writer does not clean them).
    /// </summary>
    public static FakeArtifact Zip(params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var body = entry.Open();
                body.Write(content);
            }
        }
        return new FakeArtifact(stream.ToArray());
    }
}
