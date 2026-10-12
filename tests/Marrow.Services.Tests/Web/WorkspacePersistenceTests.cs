using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// nth web surfaces, unit W3c (DESIGN 3.11 "Migration and bad data" and 13.2 "Persistence migration"): a real library.json with an old
// thread that saved no workspace, a layout saved on a larger monitor, a future schema version, unknown pane kinds and corrupted
// JSON. Each thread opens its own way, a damaged one opens with the default layout and its text kept as workspace.bad.json in the
// thread's folder (and said in the log), and saving the library again loses nothing a newer build wrote.
[Trait("Decision", "ED-NEW-web-w3c-1")]
[Trait("Feature", "W-3c")]
public sealed class WorkspacePersistenceTests
{
    private static readonly Guid Project = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static string ThreadJson(Guid id, string title, string? workspace)
    {
        var tail = workspace is null ? "" : ",\"workspace\":" + workspace;
        return "{\"id\":\"" + SwiftUUID.UuidString(id) + "\",\"projectID\":\"" + SwiftUUID.UuidString(Project) + "\",\"title\":\"" + title + "\"" + tail + "}";
    }

    private static void WriteLibrary(StorageScratchFolder storage, params string[] threads)
    {
        Directory.CreateDirectory(storage.Root);
        File.WriteAllText(Storage.LibraryURL, "{\"projects\":[],\"threads\":[" + string.Join(",", threads) + "]}", new UTF8Encoding(false));
    }

    private static Library Load()
    {
        var result = Assert.IsType<LibraryLoadResult.Loaded>(Storage.LoadLibrary());
        return result.Library;
    }

    private static ChatThread Thread(Library library, int n) => library.Threads.Single(thread => thread.Id == Id(n));

    [Fact]
    public void A_library_with_every_migration_case_opens_each_thread_its_own_way()
    {
        using var storage = new StorageScratchFolder();
        WriteLibrary(
            storage,
            ThreadJson(Id(1), "Old", null),
            ThreadJson(Id(2), "Bigger monitor", "{\"v\":1,\"order\":[[\"tasks\"],[\"artifact\"],[\"browser\"]],\"side\":true,\"ratios\":{\"chat\":0.2,\"side\":0.5,\"cols\":0.8}}"),
            ThreadJson(Id(3), "Future", "{\"v\":4,\"order\":[[\"browser\"]],\"lanes\":{\"a\":[1,2]},\"ratios\":{\"chat\":0.4}}"),
            ThreadJson(Id(4), "Broken", "{\"order\":[[7]]}"),
            ThreadJson(Id(5), "Unknown panes", "{\"v\":2,\"order\":[[\"notes\"],[\"tasks\",\"mixer\"]]}"));
        var library = Load();
        Assert.Equal(5, library.Threads.Count);

        // Old: no saved workspace, nothing damaged; the runtime maps the old flags in.
        Assert.Null(Thread(library, 1).Workspace);
        Assert.Null(Thread(library, 1).DamagedWorkspace);

        // Bigger monitor: kept whole, whatever the window it opens in.
        var big = Thread(library, 2).Workspace!;
        Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser }, big.State.GridPanes);
        Assert.Equal(0.8, big.State.Ratios["cols"]);
        Assert.Empty(PaneLayoutProblems(big.State, 620));
        Assert.Empty(PaneLayoutProblems(big.State, 1366));

        // Future: read as far as this build knows, the rest kept.
        var future = Thread(library, 3).Workspace!;
        Assert.Equal(4, future.Version);
        Assert.Equal(new[] { PaneKind.Browser }, future.State.GridPanes);

        // Broken: the default layout and the text.
        Assert.Null(Thread(library, 4).Workspace);
        Assert.Equal("{\"order\":[[7]]}", Thread(library, 4).DamagedWorkspace);

        // Unknown panes: out of view, still held.
        Assert.Equal(new[] { PaneKind.BackgroundTasks }, Thread(library, 5).Workspace!.State.GridPanes);
        Assert.Equal(2, Thread(library, 5).Workspace!.UnknownSlots.Count);
    }

    private static List<string> PaneLayoutProblems(PaneWorkspaceState state, double width)
    {
        var layout = PaneLayout.Solve(state, width, 760);
        var problems = new List<string>();
        if (layout.Mode == PaneLayoutMode.Tiles && layout.Chat.Width < PaneKinds.ChatMinWidth - 1e-6) problems.Add($"chat {layout.Chat.Width} at {width}");
        if (layout.Mode == PaneLayoutMode.Narrow && !layout.NarrowTabs.Contains(PaneKind.Browser)) problems.Add("a pane lost its tab");
        return problems;
    }

    [Fact]
    public void Saving_the_library_again_loses_nothing_a_newer_build_wrote_and_drops_only_the_damaged_text()
    {
        using var storage = new StorageScratchFolder();
        WriteLibrary(
            storage,
            ThreadJson(Id(3), "Future", "{\"v\":4,\"order\":[[\"browser\"]],\"lanes\":{\"a\":[1,2]}}"),
            ThreadJson(Id(4), "Broken", "{\"order\":[[7]]}"),
            ThreadJson(Id(5), "Unknown panes", "{\"v\":2,\"order\":[[\"notes\"],[\"tasks\",\"mixer\"]],\"recent\":[\"notes\",\"tasks\"]}"));
        var library = Load();
        // The user opens Visus in the thread of the future build, then the library is saved.
        var future = Thread(library, 3);
        var edited = library with
        {
            Threads = library.Threads.SetItem(library.Threads.ToList().IndexOf(future), future with { Workspace = future.Workspace!.WithState(future.Workspace.State.Open(PaneKind.Artifacts)) }),
        };
        DiskWriter.Shared.WriteSynchronously(edited, Storage.LibraryURL);
        var text = File.ReadAllText(Storage.LibraryURL);
        Assert.DoesNotContain("[[7]]", text, StringComparison.Ordinal);
        var again = Load();
        Assert.Equal(4, Thread(again, 3).Workspace!.Version);
        Assert.Equal(new[] { PaneKind.Artifacts, PaneKind.Browser }, Thread(again, 3).Workspace!.State.GridPanes);
        Assert.Equal("{\"a\":[1,2]}", Thread(again, 3).Workspace!.UnknownFields["lanes"]);
        Assert.Null(Thread(again, 4).Workspace);
        Assert.Null(Thread(again, 4).DamagedWorkspace);
        using var document = JsonDocument.Parse(text);
        var unknownPanes = document.RootElement.GetProperty("threads").EnumerateArray().Single(t => t.GetProperty("title").GetString() == "Unknown panes").GetProperty("workspace");
        Assert.Equal("[[\"notes\"],[\"tasks\",\"mixer\"]]", unknownPanes.GetProperty("order").GetRawText());
        Assert.Equal("[\"notes\",\"tasks\"]", unknownPanes.GetProperty("recent").GetRawText());
    }

    [Fact]
    public void Keeping_a_damaged_workspace_writes_workspace_bad_json_in_the_threads_folder_and_logs_it()
    {
        using var storage = new StorageScratchFolder();
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Add(channel + ": " + message));
        var id = Id(9);
        const string text = "{\"order\":[[7]],\"note\":\"bad\"}";
        var path = WorkspaceRecovery.Keep(id, text);
        Assert.Equal(Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(id), "workspace.bad.json"), path);
        Assert.Equal(text, File.ReadAllText(path!, Encoding.UTF8));
        var line = Assert.Single(lines);
        Assert.StartsWith(Logger.Subsystem + "/workspace: ", line, StringComparison.Ordinal);
        Assert.Contains(SwiftUUID.UuidString(id), line, StringComparison.Ordinal);
        Assert.Contains("workspace.bad.json", line, StringComparison.Ordinal);
        Assert.Contains("default layout", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_damage_found_again_is_kept_once_and_different_damage_takes_a_numbered_file()
    {
        using var storage = new StorageScratchFolder();
        using var sink = Logger.Sink.Override((_, _) => { });
        var id = Id(9);
        var first = WorkspaceRecovery.Keep(id, "{\"order\":[[7]]}")!;
        Assert.Equal(first, WorkspaceRecovery.Keep(id, "{\"order\":[[7]]}"));
        var second = WorkspaceRecovery.Keep(id, "{\"order\":[[8]]}")!;
        Assert.Equal("workspace.bad.2.json", Path.GetFileName(second));
        Assert.Equal(second, WorkspaceRecovery.Keep(id, "{\"order\":[[8]]}"));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(first)!, "workspace.bad*.json").Length);
        // After nine different texts the last file is replaced; the first stays.
        for (var n = 9; n < 9 + WorkspaceRecovery.MaxCopies + 3; n++) WorkspaceRecovery.Keep(id, "{\"order\":[[" + n + "]]}");
        Assert.Equal(WorkspaceRecovery.MaxCopies, Directory.GetFiles(Path.GetDirectoryName(first)!, "workspace.bad*.json").Length);
        Assert.Equal("{\"order\":[[7]]}", File.ReadAllText(first, Encoding.UTF8));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_is_logged_and_never_throws()
    {
        using var storage = new StorageScratchFolder();
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => lines.Add(message));
        var id = Id(9);
        // A file stands where the thread's folder should be.
        File.WriteAllText(Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(id)), "in the way");
        Assert.Null(WorkspaceRecovery.Keep(id, "{\"order\":[[7]]}"));
        Assert.Contains("could not keep", Assert.Single(lines), StringComparison.Ordinal);
    }
}
