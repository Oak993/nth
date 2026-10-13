using System.Collections.Concurrent;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Views.Artifacts;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-27")]
public sealed class WebReviewTabulaThreadTests(HeadlessSession headless)
{
    [Fact]
    public void Pane_writes_use_the_storage_queue_that_shutdown_drains() => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var file = Path.Combine(store.NewFolder("review-tabula-drain"), TabulaState.FileName);
        var queue = (SerialQueue)typeof(Marrow.Services.Store.DiskWriter)
            .GetField("queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(Marrow.Services.Store.DiskWriter.Shared)!;
        ConcurrentBag<bool> owners = [];
        using var writes = TabulaState.WriteBytes.Override((path, bytes) =>
        {
            owners.Add(queue.IsCurrent);
            FileOps.WriteAtomic(path, bytes);
        });
        var state = new TabulaState(Guid.NewGuid(), file);
        state.Select(new TabulaRef(state.Thread, "new"));
        HeadlessSession.RunDispatcherUntil(state.Persistence, TimeSpan.FromSeconds(10));
        Assert.Single(owners);
        Assert.All(owners, Assert.True);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void A_choice_made_during_load_wins_and_keeps_the_stored_format_contract(int format) => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var file = Path.Combine(store.NewFolder("review-tabula-order"), TabulaState.FileName);
        var original = System.Text.Encoding.UTF8.GetBytes("{\"format\":" + format + ",\"artifact\":\"old\",\"future\":{\"mode\":\"other\"}}");
        File.WriteAllBytes(file, original);
        using var release = new ManualResetEventSlim();
        using var reads = TabulaState.ReadBytes.Override(path =>
        {
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The test did not release the held disk read.");
            return File.ReadAllBytes(path);
        });
        var state = TabulaState.Load(Guid.NewGuid(), file);
        state.Select(new TabulaRef(state.Thread, "first"));
        state.Select(new TabulaRef(state.Thread, "second"));
        state.Select(new TabulaRef(state.Thread, "new"));
        release.Set();
        HeadlessSession.RunDispatcherUntil(state.Persistence, TimeSpan.FromSeconds(10));
        Assert.Equal("new", state.Selected!.Id);
        if (format > 1) Assert.Equal(original, File.ReadAllBytes(file));
        else
        {
            var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(file))!;
            Assert.Equal("new", saved["artifact"]!.GetValue<string>());
            Assert.Equal("other", saved["future"]!["mode"]!.GetValue<string>());
        }
        Assert.True(state.Persistence.IsCompletedSuccessfully);
    });

    [Fact]
    public void Loading_the_panes_choice_reads_disk_off_the_UI_thread() => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var file = Path.Combine(store.NewFolder("review-tabula-read"), TabulaState.FileName);
        File.WriteAllText(file, "{\"format\":1,\"artifact\":\"old\"}");
        ConcurrentBag<bool> threads = [];
        using var reads = TabulaState.ReadBytes.Override(path =>
        {
            threads.Add(Dispatcher.UIThread.CheckAccess());
            return File.ReadAllBytes(path);
        });
        var state = TabulaState.Load(Guid.NewGuid(), file);
        HeadlessSession.RunDispatcherUntil(state.Persistence, TimeSpan.FromSeconds(10));
        Assert.Equal("old", state.Selected!.Id);
        Assert.Single(threads);
        Assert.DoesNotContain(true, threads);
    });

    [Fact]
    public void Changing_the_panes_choice_writes_disk_off_the_UI_thread() => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var file = Path.Combine(store.NewFolder("review-tabula-write"), TabulaState.FileName);
        ConcurrentBag<bool> threads = [];
        using var writes = TabulaState.WriteBytes.Override((path, bytes) =>
        {
            threads.Add(Dispatcher.UIThread.CheckAccess());
            FileOps.WriteAtomic(path, bytes);
        });
        var state = new TabulaState(Guid.NewGuid(), file);
        state.Select(new TabulaRef(state.Thread, "new"));
        HeadlessSession.RunDispatcherUntil(state.Persistence, TimeSpan.FromSeconds(10));
        Assert.Single(threads);
        Assert.DoesNotContain(true, threads);
        Assert.Contains("new", File.ReadAllText(file), StringComparison.Ordinal);
    });
}
