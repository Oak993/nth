using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// DESIGN 3.11 "Stored with the thread in Library" (web surfaces W3c; ED-NEW-web-w3c-1): a thread's pane workspace is a field of
// the thread in library.json. A thread at the default layout writes nothing; a saved one comes back whole; an unreadable one
// leaves the default layout and its text on the thread (for the app to keep as workspace.bad.json) without touching any other
// thread or field; and a library saved before this field existed reads as it always did.
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class ChatThreadWorkspaceCodingTests
{
    private static ChatThread NewThread() => new(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string LibraryJson(Guid project, params (Guid Id, string Extra)[] threads) =>
        "{\"projects\":[],\"threads\":[" + string.Join(",", threads.Select(t =>
            "{\"id\":\"" + SwiftUUID.UuidString(t.Id) + "\",\"projectID\":\"" + SwiftUUID.UuidString(project) + "\"" + (t.Extra.Length > 0 ? "," + t.Extra : "") + "}")) + "]}";

    [Fact]
    public void A_saved_workspace_comes_back_with_the_thread()
    {
        using var runtime = new TestRuntime();
        var workspace = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(PaneKind.Browser).Open(PaneKind.BackgroundTasks).WithRatio("chat", 0.4).Expand(PaneKind.Browser) }
            .WithPaneData("browser", "{\"tabs\":[{\"url\":\"https://a.test/\",\"title\":\"A\",\"scroll\":120.5,\"zoom\":1.25}]}");
        var thread = NewThread() with { Workspace = workspace };
        var json = Text(Coding.Encode(thread));
        Assert.Contains("\"workspace\":{", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DamagedWorkspace", json, StringComparison.Ordinal);
        var back = Coding.Decode<ChatThread>(Encoding.UTF8.GetBytes(json))!;
        Assert.Equal(workspace, back.Workspace);
        Assert.Null(back.DamagedWorkspace);
        Assert.Equal(thread, back);
        // Saved again, the same bytes.
        Assert.Equal(json, Text(Coding.Encode(back)));
    }

    [Fact]
    public void A_thread_at_the_default_layout_writes_no_workspace_and_an_old_library_reads_as_it_did()
    {
        using var runtime = new TestRuntime();
        var plain = NewThread();
        Assert.DoesNotContain("workspace", Text(Coding.Encode(plain)), StringComparison.Ordinal);
        Assert.DoesNotContain("workspace", Text(Coding.Encode(plain with { Workspace = new StoredWorkspace() })), StringComparison.Ordinal);
        var project = Guid.NewGuid();
        var id = Guid.NewGuid();
        var library = DecodeLibrary(LibraryJson(project, (id, "")));
        Assert.Null(library.Threads.Single().Workspace);
        Assert.Null(library.Threads.Single().DamagedWorkspace);
        // JSON null is the same as absent.
        var nulled = DecodeLibrary(LibraryJson(project, (id, "\"workspace\":null")));
        Assert.Null(nulled.Threads.Single().Workspace);
        Assert.Null(nulled.Threads.Single().DamagedWorkspace);
    }

    private static Library DecodeLibrary(string json) => Parse(json).Deserialize<Library>(Coding.StorageOptions)!;

    [Theory]
    [InlineData("\"workspace\":\"garbage\"")]
    [InlineData("\"workspace\":7")]
    [InlineData("\"workspace\":[1,2]")]
    [InlineData("\"workspace\":{\"order\":5}")]
    [InlineData("\"workspace\":{\"order\":[[3]]}")]
    [InlineData("\"workspace\":{\"ratios\":{\"chat\":\"wide\"}}")]
    [InlineData("\"workspace\":{\"v\":\"x\"}")]
    public void An_unreadable_workspace_leaves_the_default_layout_and_its_text_and_spares_everything_else(string workspace)
    {
        using var runtime = new TestRuntime();
        var project = Guid.NewGuid();
        var (before, damaged, after) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var good = "\"title\":\"Kept\",\"workspace\":{\"order\":[[\"tasks\"]],\"ratios\":{\"chat\":0.4}}";
        var library = DecodeLibrary(LibraryJson(project, (before, good), (damaged, "\"title\":\"Odd\"," + workspace), (after, good)));
        Assert.Equal(3, library.Threads.Count);
        var odd = library.Threads.Single(t => t.Id == damaged);
        Assert.Equal("Odd", odd.Title);
        Assert.Null(odd.Workspace);
        Assert.Equal(Parse("{" + workspace + "}").GetProperty("workspace").GetRawText(), odd.DamagedWorkspace);
        foreach (var id in new[] { before, after })
        {
            var thread = library.Threads.Single(t => t.Id == id);
            Assert.Equal("Kept", thread.Title);
            Assert.Equal(new[] { PaneKind.BackgroundTasks }, thread.Workspace!.State.GridPanes);
            Assert.Null(thread.DamagedWorkspace);
        }
        // Written again, the unreadable text is gone from the library (the app has kept it as workspace.bad.json) and nothing leaks out.
        var written = Text(Coding.Encode(library));
        Assert.DoesNotContain("Damaged", written, StringComparison.Ordinal);
        Assert.Equal(2, written.Split("\"workspace\":", StringSplitOptions.None).Length - 1);
        var again = DecodeLibrary(written);
        Assert.Null(again.Threads.Single(t => t.Id == damaged).Workspace);
        Assert.Null(again.Threads.Single(t => t.Id == damaged).DamagedWorkspace);
    }

    [Fact]
    public void A_newer_workspace_keeps_its_schema_number_and_unknown_fields_through_the_library()
    {
        using var runtime = new TestRuntime();
        var project = Guid.NewGuid();
        var id = Guid.NewGuid();
        var json = LibraryJson(project, (id, "\"workspace\":{\"v\":5,\"order\":[[\"tasks\"],[\"notes\"]],\"mood\":{\"calm\":true},\"ratios\":{\"chat\":0.45}}"));
        var thread = DecodeLibrary(json).Threads.Single();
        Assert.Equal(5, thread.Workspace!.Version);
        Assert.Equal(new[] { PaneKind.BackgroundTasks }, thread.Workspace.State.GridPanes);
        var edited = thread with { Workspace = thread.Workspace.WithState(thread.Workspace.State.Open(PaneKind.Browser)) };
        var written = Text(Coding.Encode(new Library { Threads = new ValueList<ChatThread>([edited]) }));
        var stored = Parse(written).GetProperty("threads")[0].GetProperty("workspace");
        Assert.Equal(5, stored.GetProperty("v").GetInt32());
        Assert.Equal("{\"calm\":true}", stored.GetProperty("mood").GetRawText());
        Assert.Equal("[[\"tasks\"],[\"notes\"],[\"browser\"]]", stored.GetProperty("order").GetRawText());
    }

    [Fact]
    public void A_threads_workspace_is_part_of_what_makes_it_changed()
    {
        using var runtime = new TestRuntime();
        var thread = NewThread();
        var opened = thread with { Workspace = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(PaneKind.Browser) } };
        Assert.NotEqual(thread, opened);
        Assert.Equal(opened, thread with { Workspace = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(PaneKind.Browser) } });
    }
}
