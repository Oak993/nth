using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Runtime;

// Opening a thread with no prefetched document (App/Runtime/ThreadRuntime.swift:540-554, B3): the history loads off the main
// thread, the runtime shows an empty history meanwhile, and what is read becomes the history. Saving is gated by the loading
// state (:3698-3699, :3711-3712).
public sealed class ThreadRuntimeHistoryLoadTests
{
    private static (AppModelHarness Harness, AppModel App, ChatThread Thread) Open()
    {
        var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Disk runtime", harness.NewFolder("history-load"));
        var thread = AppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        return (harness, harness.MakeModel(), thread);
    }

    private static ThreadDocument OnDisk(Guid id) => new(id)
    {
        Items = new ValueList<TimelineItem>([new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "on disk" }))]),
    };

    [Fact]
    [Trait("Req", "PR-STORE-052")] // :544-553 no prefetched document: isLoadingHistory, an empty history, then the decoded file
    public void A_thread_with_no_prefetched_document_shows_an_empty_history_while_its_file_loads()
    {
        var (h, app, thread) = Open();
        using var _ = h;
        DiskWriter.Shared.WriteSynchronously(OnDisk(thread.Id), Storage.ThreadURL(thread.Id));

        var runtime = h.Main.OnMain(() =>
        {
            var created = app.Runtime(thread.Id);
            Assert.True(created.IsLoadingHistory);
            Assert.Empty(created.Entries); // the view paints at once, with no history
            return created;
        });
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        Assert.False(runtime.IsLoadingHistory);
        var loaded = Assert.IsType<TimelineItem.Content.User>(Assert.Single(runtime.Entries).Item.TimelineItemContent);
        Assert.Equal("on disk", loaded.Value.Text);
    }

    [Fact]
    [Trait("Req", "PR-STORE-052")] // :541 a missing file becomes a fresh empty ThreadDocument for that thread id
    public void A_missing_history_file_becomes_an_empty_history()
    {
        var (h, app, thread) = Open();
        using var _ = h;
        Assert.False(File.Exists(ThreadFile(thread.Id)));

        var runtime = h.Main.OnMain(() => app.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        Assert.False(runtime.IsLoadingHistory);
        Assert.Empty(runtime.Entries);
        Assert.Empty(runtime.Turns);
    }

    [Fact]
    [Trait("Req", "PR-STORE-052")] // :541 an undecodable file becomes a fresh empty ThreadDocument too
    public void An_undecodable_history_file_becomes_an_empty_history()
    {
        var (h, app, thread) = Open();
        using var _ = h;
        Directory.CreateDirectory(Path.GetDirectoryName(ThreadFile(thread.Id))!);
        File.WriteAllBytes(ThreadFile(thread.Id), Encoding.UTF8.GetBytes("{ this is not a thread document"));

        var runtime = h.Main.OnMain(() => app.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        Assert.False(runtime.IsLoadingHistory);
        Assert.Empty(runtime.Entries);
    }

    [Fact]
    [Trait("Req", "PR-STORE-052")] // :3698-3699 a save before the history is installed would write an empty thread over the file: it writes nothing
    public void A_save_while_the_history_is_loading_leaves_the_file_alone()
    {
        var (h, app, thread) = Open();
        using var _ = h;
        DiskWriter.Shared.WriteSynchronously(OnDisk(thread.Id), Storage.ThreadURL(thread.Id));
        var before = File.ReadAllBytes(ThreadFile(thread.Id));

        h.Main.OnMain(() =>
        {
            var runtime = app.Runtime(thread.Id);
            Assert.True(runtime.IsLoadingHistory);
            ThreadRuntimeReflection.Set(runtime, "saveRevision", 5L); // something to write, were the history installed
            runtime.SaveNow();
            Assert.Null(runtime.UnsavedSnapshot());
        });

        Assert.Equal(before, File.ReadAllBytes(ThreadFile(thread.Id)));
    }

    private static string ThreadFile(Guid id) => Storage.ThreadURL(id);
}
