using System.Buffers.Binary;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class ThumbnailCacheTests
{
    [Fact]
    [Trait("Req", "PR-STORE-123")] // DroppyCode/Core/Support/ThumbnailCache.swift:16, :22-26
    public async Task Point_size_uses_fixed_two_times_ceiling_and_missing_files_retry()
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "small.bmp");
        WriteBmp(path, 8, 2);
        var cache = new ThumbnailCache();
        var first = await cache.ThumbnailFor(path, 1.1);
        Assert.NotNull(first);
        Assert.Equal(3, Math.Max(first.Image.Width, first.Image.Height));
        Assert.Same(first, await cache.ThumbnailFor(path, 1.1));
        Assert.Equal(1, await cache.Decodes());
        Assert.Null(await cache.ThumbnailFor(Path.Combine(root, "absent.bmp"), 1.1));
        Assert.Null(await cache.ThumbnailFor(Path.Combine(root, "absent.bmp"), 1.1));
        Assert.Equal(3, await cache.Decodes());
    }

    [Fact]
    [Trait("Req", "PR-STORE-124")] // DroppyCode/Core/Support/ThumbnailCache.swift:27-42
    public async Task Concurrent_identical_requests_share_a_single_decode()
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "large.bmp");
        WriteBmp(path, 2400, 1600);
        var cache = new ThumbnailCache();
        var calls = Enumerable.Range(0, 24).Select(_ => cache.ThumbnailFor(path, 300)).ToArray();
        var images = await Task.WhenAll(calls);
        Assert.All(images, image => Assert.NotNull(image));
        Assert.Equal(1, await cache.Decodes());
        Assert.All(images, image => Assert.Same(images[0], image));
    }

    [Fact]
    [Trait("Req", "PR-STORE-123")] // DroppyCode/Core/Support/ThumbnailCache.swift:22-23
    [Trait("Req", "UI-STORE-005")] // DroppyCode/Core/Support/ThumbnailCache.swift:22-23
    public async Task Already_cancelled_caller_returns_before_starting_a_decode()
    {
        var cache = new ThumbnailCache();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = TaskHandle.Detached(async () =>
        {
            await gate.Task;
            return await cache.ThumbnailFor("missing.bmp", 20);
        });
        caller.Cancel();
        gate.SetResult();
        Assert.Null(await caller.Value);
        Assert.Equal(0, await cache.Decodes());
    }

    [Fact]
    [Trait("Req", "PR-STORE-125")] // DroppyCode/Core/Support/ThumbnailCache.swift:44-61
    [Trait("Req", "UI-STORE-005")] // DroppyCode/Core/Support/ThumbnailCache.swift:22-42, :44-62
    public async Task Full_image_is_decoded_eagerly_and_never_upscaled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "source.bmp");
        WriteBmp(path, 8, 2);
        var cache = new ThumbnailCache();
        var image = (await cache.ThumbnailFor(path, 100))!.Image;
        File.Delete(path);
        Assert.Equal(8, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(image.BytesPerRow * image.Height, image.Pixels.Length);
        Assert.NotEqual(0, image.Pixels[0]);
    }

    [Fact]
    [Trait("Req", "PR-STORE-125")] // DroppyCode/Core/Support/ThumbnailCache.swift:54-60
    [Trait("Req", "UI-STORE-005")] // DroppyCode/Core/Support/ThumbnailCache.swift:44-62
    public async Task Exif_orientation_rotates_the_decoded_pixels()
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "oriented.tif");
        WriteOrientedTiff(path);
        var image = (await new ThumbnailCache().ThumbnailFor(path, 20))!.Image;
        Assert.Equal(1, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 0, 0, 255 }, image.Pixels.AsSpan(0, 3).ToArray());
        Assert.Equal(new byte[] { 0, 255, 0 }, image.Pixels.AsSpan(image.BytesPerRow, 3).ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-160")]
    [Trait("Oracle", "O-54")]
    public async Task An_svg_file_yields_no_thumbnail_and_is_decoded_again_next_time()
    {
        // Rests on open item O-54: the macOS value for SVG is unsettled; parked nil.
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "red.svg");
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"red\"/></svg>");
        var cache = new ThumbnailCache();
        Assert.Null(await cache.ThumbnailFor(path, 16));
        Assert.Null(await cache.ThumbnailFor(path, 16));
        Assert.Equal(2, await cache.Decodes());
    }

    [Fact]
    [Trait("Req", "PR-STORE-126")] // DroppyCode/Core/Support/ThumbnailCache.swift:46-53
    public async Task Square_fill_multiplies_by_aspect_ratio_and_caps_the_long_edge_at_2048()
    {
        var root = Directory.CreateDirectory(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "wide.bmp");
        WriteBmp(path, 3000, 10);
        var cache = new ThumbnailCache();
        var plain = (await cache.ThumbnailFor(path, 10))!.Image;
        var square = (await cache.ThumbnailFor(path, 10, fillingSquare: true))!.Image;
        Assert.Equal(20, plain.Width);
        Assert.Equal(2048, square.Width);
        Assert.NotSame(plain, square);
        Assert.Equal(2, await cache.Decodes());
    }

    [Fact]
    [Trait("Req", "PR-STORE-127")] // DroppyCode/Core/Support/ThumbnailCache.swift:65-83
    [Trait("Req", "UI-STORE-005")] // DroppyCode/Core/Support/ThumbnailCache.swift:65-83
    public void Main_actor_memory_key_omits_the_square_flag_and_later_store_wins()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var a = new DecodedBitmap(1, 1, 4, new byte[] { 1, 2, 3, 4 });
            var b = new DecodedBitmap(1, 1, 4, new byte[] { 5, 6, 7, 8 });
            var path = Guid.NewGuid().ToString("N");
            ThumbnailMemory.Store(a, path, 1.1);
            Assert.Same(a, ThumbnailMemory.ImageFor(path, 1.1));
            Assert.Null(ThumbnailMemory.ImageFor(path, 1.6));
            ThumbnailMemory.Store(b, path, 1.1);
            Assert.Same(b, ThumbnailMemory.ImageFor(path, 1.1));
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-127")] // DroppyCode/Core/Support/ThumbnailCache.swift:70-81
    public void Main_actor_memory_refuses_a_bitmap_over_16_megabytes()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var bitmap = new DecodedBitmap(4096, 1025, 4096 * 4, new byte[4096 * 4 * 1025]);
            var path = Guid.NewGuid().ToString("N");
            ThumbnailMemory.Store(bitmap, path, 1);
            Assert.Null(ThumbnailMemory.ImageFor(path, 1));
        });
    }

    private static void WriteBmp(string path, int width, int height)
    {
        var stride = checked((width * 3 + 3) & ~3);
        var pixels = checked(stride * height);
        var bytes = new byte[checked(54 + pixels)];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = 54 + y * stride + x * 3;
                bytes[i] = 17; bytes[i + 1] = 47; bytes[i + 2] = 91;
            }
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteOrientedTiff(string path)
    {
        // TIFF 2x1 RGB with Orientation 6 (rotate 90 degrees clockwise), red then green.
        var bytes = new byte[158];
        bytes[0] = (byte)'I'; bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 11);
        var offset = 10;
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), value);
            offset += 12;
        }
        Entry(256, 4, 1, 2); Entry(257, 4, 1, 1); Entry(258, 3, 3, 146);
        Entry(259, 3, 1, 1); Entry(262, 3, 1, 2); Entry(273, 4, 1, 152);
        Entry(274, 3, 1, 6); Entry(277, 3, 1, 3); Entry(278, 4, 1, 1);
        Entry(279, 4, 1, 6); Entry(284, 3, 1, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(146), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(150), 8);
        bytes[152] = 255; bytes[156] = 255;
        File.WriteAllBytes(path, bytes);
    }
}
