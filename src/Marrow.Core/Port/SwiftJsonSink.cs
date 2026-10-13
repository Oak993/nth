using System.Buffers;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY (ED-NEW-perf-memory-5). Where <see cref="SwiftJson"/> writes its UTF-8 bytes: chunks rented from the shared pool (16 KB, doubling to 1 MB), joined
/// into one exact-size array at the end. The writer used to build a StringBuilder, then a string (twice the file size), then the byte array;
/// a long thread was saved with about 6 to 8 times its size in transient large-object-heap memory. Now only the finished array is allocated.
/// </summary>
internal sealed class SwiftJsonSink : IDisposable
{
    // The first chunk is small, as most documents are; each later one doubles up to 1 MB. A long thread is a few dozen chunks, and the shared pool keeps
    // that many arrays of one size (it drops the rest of a hundred 64 KB ones on return), so the next save finds them again instead of allocating.
    private const int FirstChunkLength = 1 << 14;
    private const int LargestChunkLength = 1 << 20;

    private byte[]? chunk = ArrayPool<byte>.Shared.Rent(FirstChunkLength);
    private int used;
    private List<(byte[] Bytes, int Length)>? full;
    private long finished;

    public void Write(byte value)
    {
        if (used == chunk!.Length) Spill();
        chunk[used++] = value;
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (used == chunk!.Length) Spill();
            var count = Math.Min(chunk.Length - used, bytes.Length);
            bytes[..count].CopyTo(chunk.AsSpan(used));
            used += count;
            bytes = bytes[count..];
        }
    }

    /// <summary>Text made of ASCII characters only (keywords and number text).</summary>
    public void WriteAscii(string text)
    {
        foreach (var c in text) Write((byte)c);
    }

    public void WriteSpaces(int count)
    {
        for (; count > 0; count--) Write((byte)' ');
    }

    private void Spill()
    {
        (full ??= []).Add((chunk!, used));
        finished += used;
        chunk = ArrayPool<byte>.Shared.Rent(Math.Min(LargestChunkLength, chunk!.Length * 2));
        used = 0;
    }

    public byte[] ToArray()
    {
        var result = GC.AllocateUninitializedArray<byte>(checked((int)(finished + used)));
        var offset = 0;
        if (full is not null)
        {
            foreach (var (bytes, length) in full)
            {
                bytes.AsSpan(0, length).CopyTo(result.AsSpan(offset));
                offset += length;
            }
        }
        chunk.AsSpan(0, used).CopyTo(result.AsSpan(offset));
        return result;
    }

    public void Dispose()
    {
        if (full is not null)
        {
            foreach (var (bytes, _) in full) ArrayPool<byte>.Shared.Return(bytes);
            full = null;
        }
        if (chunk is not null)
        {
            ArrayPool<byte>.Shared.Return(chunk);
            chunk = null;
        }
    }
}
