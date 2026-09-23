using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/DownloadsPopover.swift (B2 1fbf81e), :1-97 (RecentDownload, RecentDownloads).
// RecentDownloads.Directory is seamed behind RecentDownloads.DirectoryProvider (a test-only Ambient factory, since
// SHGetKnownFolderPath has no override in the Swift itself): every test here points it at a throwaway folder under
// MARROW_PORT_DATA_DIR, never the real Downloads directory (R-TEST-9).
public sealed class DownloadsPopoverTests
{
    private static IDisposable Directed(string? path) => RecentDownloads.DirectoryProvider.Override(() => path);

    [Fact]
    [Trait("Req", "PR-COMPOSER-078")] // DownloadsPopover.swift:39-96, :50-58
    public void Load_lists_files_newest_first_and_drops_hidden_files_and_directories()
    {
        using var folder = new ScratchDownloads();
        var older = folder.File("older.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = folder.File("newer.txt", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var hidden = folder.File(".hidden.txt", new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(folder.Path, "subfolder"));

        using var directed = Directed(folder.Path);
        var downloads = new RecentDownloads();
        using var runtime = new TestRuntime();
        runtime.Loop.Run(() => downloads.Load());

        Assert.True(downloads.IsLoaded);
        Assert.Equal(new[] { "newer.txt", "older.txt" }, downloads.Items.Select(item => item.Name));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-078")] // DownloadsPopover.swift:18-19 limit = 15, :56-57 sorted then prefix(limit)
    public void Load_keeps_only_the_15_most_recently_modified_files()
    {
        using var folder = new ScratchDownloads();
        var expectedNewest = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var name = $"file{i:D2}.txt";
            folder.File(name, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
            if (i >= 5) expectedNewest.Add(name); // the 15 newest: minutes 5..19
        }
        expectedNewest.Reverse(); // newest (minute 19) first

        using var directed = Directed(folder.Path);
        var downloads = new RecentDownloads();
        using var runtime = new TestRuntime();
        runtime.Loop.Run(() => downloads.Load());

        Assert.Equal(15, downloads.Items.Count);
        Assert.Equal(expectedNewest, downloads.Items.Select(item => item.Name));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-078")] // DownloadsPopover.swift:74 size > 0 ? formatted : nil
    public void Load_formats_a_positive_size_and_omits_it_for_a_zero_byte_file()
    {
        using var folder = new ScratchDownloads();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var sized = folder.File("sized.bin", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), bytes: 1024);
        var empty = folder.File("empty.bin", new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), bytes: 0);

        using var directed = Directed(folder.Path);
        var downloads = new RecentDownloads();
        using var runtime = new TestRuntime();
        runtime.Loop.Run(() => downloads.Load());

        var sizedItem = Assert.Single(downloads.Items, item => item.Name == "sized.bin");
        var emptyItem = Assert.Single(downloads.Items, item => item.Name == "empty.bin");
        Assert.Equal("1 KB", sizedItem.Size); // :34-37 ByteCountFormatter, countStyle .file
        Assert.Null(emptyItem.Size);
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-078")] // DownloadsPopover.swift:29-31 imageExtensions
    [InlineData("photo.PNG", true)] // extensions are matched case-insensitively (:70 .lowercased())
    [InlineData("photo.jpeg", true)]
    [InlineData("photo.webp", true)]
    [InlineData("document.pdf", false)]
    [InlineData("archive.zip", false)]
    public void Load_marks_only_the_ten_listed_extensions_as_images(string name, bool expectedIsImage)
    {
        using var folder = new ScratchDownloads();
        folder.File(name, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        using var directed = Directed(folder.Path);
        var downloads = new RecentDownloads();
        using var runtime = new TestRuntime();
        runtime.Loop.Run(() => downloads.Load());

        var item = Assert.Single(downloads.Items);
        Assert.Equal(expectedIsImage, item.IsImage);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-078")] // DownloadsPopover.swift:40 guard let directory else return
    public void Load_returns_early_and_stays_unloaded_when_there_is_no_downloads_directory()
    {
        using var directed = Directed(null);
        var downloads = new RecentDownloads();
        using var runtime = new TestRuntime();
        runtime.Loop.Run(() => downloads.Load());

        Assert.False(downloads.IsLoaded);
        Assert.Empty(downloads.Items);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-078")] // DownloadsPopover.swift:44-48 (try? ... ) ?? []
    public void Load_yields_an_empty_loaded_list_when_the_directory_read_throws()
    {
        using var directed = Directed(@"Z:\marrow-port-tests-nonexistent\does-not-exist");
        var downloads = new RecentDownloads();
        using var runtime = new TestRuntime();
        runtime.Loop.Run(() => downloads.Load());

        Assert.True(downloads.IsLoaded);
        Assert.Empty(downloads.Items);
    }

    [Fact]
    [Trait("Rule", "R-VAL-4")] // Identifiable.id -> Id (DownloadsPopover.swift:11 var id: String { url.path })
    public void RecentDownload_id_is_its_path()
    {
        var download = new RecentDownload(@"C:\Downloads\a.txt", "a.txt", null, false);
        Assert.Equal(@"C:\Downloads\a.txt", download.Id);
    }
}
