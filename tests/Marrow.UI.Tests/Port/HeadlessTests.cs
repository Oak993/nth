using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// Rules proved on Avalonia 12.1.2 headless (IMPLEMENTING.md 3.2, 3.7). Source: draft C (proto-C HeadlessViewTests)
// and draft B (the binding probe).
[Collection("Headless")]
public sealed class HeadlessTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Rule", "R-ISO-1")]
    [Trait("Decision", "ED-065")]
    public void The_main_actor_bound_to_Avalonia_runs_posts_and_spawns_FIFO_on_the_UI_thread_never_inline() => headless.OnUiThread(() =>
    {
        var log = new List<string>();
        MainActor.Post(() => log.Add("post 1"));
        MainActor.Spawn(async () =>
        {
            log.Add($"spawn isolated={MainActor.IsIsolated}");
            await Task.Run(() => { });
            log.Add($"after await isolated={MainActor.IsIsolated}");
        });
        MainActor.Post(() => log.Add("post 2"));
        log.Add("creator returned");
        Dispatcher.UIThread.RunJobs();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (log.Count < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(["creator returned", "post 1", "spawn isolated=True", "post 2", "after await isolated=True"], log);
    });

    [Fact]
    [Trait("Rule", "R-OBS-4")]
    [Trait("Rule", "R-VIEW-4")]
    public void An_Avalonia_binding_follows_a_computed_property_across_a_cell() => headless.OnUiThread(() =>
    {
        var model = new SelectionSample();
        model.Put("a", "Alpha");
        model.Put("b", "Beta");
        model.Selected = "a";
        var title = new TextBlock();
        title.Bind(TextBlock.TextProperty, CompiledBinding.Create((SelectionSample m) => m.SelectedTitle, model));
        var window = Show(title);
        Assert.Equal("Alpha", title.Text);
        model.Put("a", "Alpha 2");
        Assert.Equal("Alpha 2", title.Text);
        model.Selected = "b";
        Assert.Equal("Beta", title.Text);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-6")]
    public void Environment_values_come_from_the_nearest_ancestor() => headless.OnUiThread(() =>
    {
        var leaf = new Border();
        var middle = new Border { Child = leaf };
        var root = new Border { Child = middle };
        EnvironmentValues.SetAccessibilityReduceMotion(root, true);
        var window = Show(root);
        Assert.True(EnvironmentValues.GetAccessibilityReduceMotion(leaf));
        EnvironmentValues.SetAccessibilityReduceMotion(middle, false);
        Assert.False(EnvironmentValues.GetAccessibilityReduceMotion(leaf));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-8")]
    public void ForEach_keeps_a_surviving_rows_control_and_state_and_drops_the_vanished_one() => headless.OnUiThread(() =>
    {
        var panel = new StackPanel();
        var disposed = new List<string>();
        var rows = new ForEach<(string Id, string Title), string>(panel, item => item.Id,
            item => new RowProbe(item.Id, disposed) { Text = item.Title },
            (row, item) => ((RowProbe)row).Text = item.Title);
        var window = Show(panel);
        rows.Apply([("a", "A"), ("b", "B"), ("c", "C")]);
        var a = (RowProbe)panel.Children[0];
        var c = (RowProbe)panel.Children[2];
        a.Expanded = true; // row @State
        rows.Apply([("c", "C2"), ("a", "A")]);
        Assert.Equal([c, a], panel.Children.Cast<RowProbe>()); // same instances, new order
        Assert.True(a.Expanded);                               // state survived the reorder
        Assert.Equal("C2", c.Text);                            // inputs updated in place
        Assert.Equal(["b"], disposed);
        rows.Apply([("a", "A"), ("b", "B")]);
        Assert.NotSame(c, panel.Children[1]);                  // a returning id is a NEW identity
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-3")]
    public void A_task_modifier_starts_on_attach_and_is_cancelled_on_detach() => headless.OnUiThread(() =>
    {
        var log = new List<string>();
        var control = new Border().Task(() => "id", async id =>
        {
            log.Add("start " + id);
            var slept = await SwiftTask.TrySleep(TimeSpan.FromHours(1));
            log.Add(slept ? "slept" : "cancelled");
        });
        var host = new Border();
        var window = Show(host);
        host.Child = control;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["start id"], log);
        host.Child = null;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["start id", "cancelled"], log);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void An_async_local_override_flows_into_work_posted_to_the_UI_thread() => headless.OnUiThread(() =>
    {
        var probe = new Ambient<string>("default");
        string? seen = null;
        using (probe.Override("override"))
            MainActor.Post(() => seen = probe.Current);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("override", seen);
    });
}
