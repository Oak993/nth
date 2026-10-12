using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: writing an export to a file the owner chose (DESIGN.md 12.5; ED-NEW-dextra-as21-5). The file appears whole or
// not at all: a failed or cancelled export leaves neither a half file nor the file it was to replace changed, and no
// temporary file behind. The exports cover every line of the log, not only the lines a snapshot lists.
[Trait("Decision", "ED-NEW-dextra-as21-5")]
[Trait("Feature", "AS-21")]
public sealed class ActivityExportFileTests : IDisposable
{
    private static readonly ActivityExportContext Context = new(new ActivityFilter(), null, new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero));
    private readonly ActivityLogRig data = ActivityDataset.Make();
    private readonly string folder;

    public ActivityExportFileTests()
    {
        folder = Path.Combine(data.Temp.Folder, "out");
        Directory.CreateDirectory(folder);
    }

    public void Dispose() => data.Dispose();

    private string[] Files() => Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<ActivityRow> Failing(IEnumerable<ActivityRow> rows)
    {
        foreach (var row in rows.Take(2)) yield return row;
        throw new InvalidOperationException("the log could not be read any further");
    }

    [Theory]
    [InlineData(ActivityExportFormat.Json, "nth-activity-2026-10-13.json")]
    [InlineData(ActivityExportFormat.Csv, "nth-activity-2026-10-13.csv")]
    public void The_save_dialog_is_offered_a_dated_name(ActivityExportFormat format, string expected) =>
        Assert.Equal(expected, ActivityExporter.SuggestedFileName(format, new DateOnly(2026, 10, 13)));

    [Fact]
    public void A_file_holds_the_same_bytes_as_the_stream_and_reports_what_it_wrote()
    {
        var rows = ActivityLogReader.Stream(data.Directory, null).ToList();
        using var memory = new MemoryStream();
        ActivityExporter.Write(memory, ActivityExportFormat.Json, rows, Context);
        var path = Path.Combine(folder, "all.json");

        var result = ActivityExporter.WriteFile(path, ActivityExportFormat.Json, rows, Context);

        Assert.Equal(path, result.Path);
        Assert.Equal(6, result.Rows);
        Assert.Equal(memory.Length, result.Bytes);
        Assert.Equal(memory.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(["all.json"], Files());
    }

    [Fact]
    public void A_file_replaces_an_earlier_one_in_one_step()
    {
        var path = Path.Combine(folder, "log.csv");
        File.WriteAllText(path, "old contents");

        var result = ActivityExporter.WriteFile(path, ActivityExportFormat.Csv, ActivityLogReader.Stream(data.Directory, null), Context);

        Assert.Equal(6, result.Rows);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
        Assert.StartsWith("seq,time,", File.ReadAllText(path), StringComparison.Ordinal); // the reader strips the mark
        Assert.Equal(["log.csv"], Files());
    }

    [Fact]
    public void An_export_that_fails_part_way_leaves_the_earlier_file_and_no_temporary_file()
    {
        var path = Path.Combine(folder, "keep.json");
        File.WriteAllText(path, "{\"kept\":true}");

        Assert.Throws<InvalidOperationException>(() =>
            ActivityExporter.WriteFile(path, ActivityExportFormat.Json, Failing(ActivityLogReader.Stream(data.Directory, null)), Context));

        Assert.Equal("{\"kept\":true}", File.ReadAllText(path));
        Assert.Equal(["keep.json"], Files());
    }

    [Fact]
    public void A_cancelled_export_creates_nothing()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var path = Path.Combine(folder, "never.csv");

        Assert.Throws<OperationCanceledException>(() =>
            ActivityExporter.WriteFile(path, ActivityExportFormat.Csv, ActivityLogReader.Stream(data.Directory, null), Context, cancel.Token));

        Assert.Empty(Files());
    }

    [Fact]
    public void A_folder_that_does_not_exist_fails_and_leaves_nothing_behind()
    {
        var path = Path.Combine(folder, "missing", "x.json");

        Assert.Throws<DirectoryNotFoundException>(() => ActivityExporter.WriteFile(path, ActivityExportFormat.Json, [], Context));

        Assert.Empty(Files());
        Assert.Throws<ArgumentException>(() => ActivityExporter.WriteFile(" ", ActivityExportFormat.Json, [], Context));
    }

    [Fact]
    public void A_filtered_export_of_a_streamed_log_holds_exactly_the_rows_that_pass_and_every_line_older_than_a_listing_would_hold()
    {
        // The snapshot lists the newest 3 lines; the export streams the files, so it still reaches line 1.
        var snapshot = ActivityLogReader.Load(data.Source, maxRows: 3);
        Assert.Equal(3, snapshot.NotListed);
        var filter = new ActivityFilter { Assistant = "dextra" };
        var path = Path.Combine(folder, "dextra.json");

        var result = ActivityExporter.WriteFile(path, ActivityExportFormat.Json,
            ActivityLogReader.Stream(data.Directory, snapshot.Verification).Where(filter.Matches), new ActivityExportContext(filter, snapshot.Verification, Context.ExportedAt));

        Assert.Equal(4, result.Rows);
        var json = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!;
        Assert.Equal([1L, 2, 5, 6], json["lines"]!.AsArray().Select(l => l!["seq"]!.GetValue<long>()));
        Assert.Equal("dextra", json["filter"]!["assistant"]!.GetValue<string>());
        Assert.Equal(4, json["count"]!.GetValue<int>());
    }
}
