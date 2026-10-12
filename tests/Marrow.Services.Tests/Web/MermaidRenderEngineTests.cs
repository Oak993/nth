using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN.md 7.2 (web surfaces W16c; ED-NEW-web-w16c-1): the hidden Mermaid renderer is one sandboxed web view for the whole
// process on its own site, with no bridge and no network. It opens on first use, draws one diagram at a time in order, hands the
// page's SVG back unread, replaces a page that stops or does not answer, and closes after 30 seconds idle. These tests drive it
// over the fake web engine: the rig plays the window that hosts the view and the page that answers.
[Trait("Decision", "ED-NEW-web-w16c-1")]
public sealed class MermaidRenderEngineTests
{
    [Fact]
    public async Task The_first_drawing_opens_one_sandboxed_view_with_no_bridge_loads_the_site_and_asks_the_page()
    {
        using var rig = new DiagramRig();
        Assert.True(rig.Engine.IsAvailable);
        Assert.Null(rig.Engine.Hosted);
        var result = await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.Equal(DiagramRig.Drawing, result.Svg);

        var view = Assert.Single(rig.Web.Surface.Views);
        Assert.Same(view, rig.Engine.Hosted);
        Assert.Equal(WebSurfaceKind.InlineWidget, view.Kind);
        Assert.Equal(MermaidRenderEngine.WindowId, view.WindowId);
        Assert.Equal(WebProfileKind.Artifacts, view.Creation.Kind);
        Assert.Equal(WebProfiles.ArtifactsProfile, view.Creation.ProfileName);
        Assert.True(view.Creation.IsInPrivate);
        Assert.Equal(MermaidRenderSite.Origin, view.SandboxOrigin);
        var fake = WebRig.Fake(view);
        Assert.Equal(MermaidRenderSite.Address, fake.Url);
        Assert.Contains($"Navigate {MermaidRenderSite.Address}", fake.Log);
        // Hardening, and not the bridge: the page has no way to message nth.
        Assert.Equal([SandboxScripts.Hardening], fake.DocumentScripts);
        Assert.NotNull(fake.ResourceHandler);
        Assert.Same(rig.Engine.Site, fake.ResourceHandler);
        // The page was probed for Mermaid, then asked for the drawing.
        Assert.Equal(2, rig.Expressions.Count);
        Assert.StartsWith("window.nthDiagramReady", rig.Expressions[0], StringComparison.Ordinal);
        var drawing = Assert.Single(rig.Drawings);
        Assert.Equal("graph LR\n  A --> B", DiagramRig.SourceOf(drawing));
        var options = DiagramRig.Options(drawing);
        Assert.False(options["dark"]!.GetValue<bool>());
        Assert.Equal("Segoe UI", options["fontFamily"]!.GetValue<string>());
        Assert.Equal("#6f6f72", options["variables"]!["lineColor"]!.GetValue<string>());
        // Runtime.evaluate by value, promises awaited, with a limit.
        var call = fake.CdpCalls.Last(c => c.Method == "Runtime.evaluate");
        var parameters = JsonNode.Parse(call.Params)!;
        Assert.True(parameters["awaitPromise"]!.GetValue<bool>());
        Assert.True(parameters["returnByValue"]!.GetValue<bool>());
        Assert.Equal((int)MermaidRenderEngine.RenderWait.TotalMilliseconds, parameters["timeout"]!.GetValue<int>());
        Assert.False(view.IsModelStepRunning);
    }

    [Fact]
    public async Task The_next_drawings_reuse_the_view_and_the_page()
    {
        using var rig = new DiagramRig();
        await rig.Engine.RenderAsync(DiagramRig.Request("graph LR\n  A --> B"), CancellationToken.None);
        var view = rig.Engine.View;
        await rig.Engine.RenderAsync(DiagramRig.Request("graph TD\n  A --> B", dark: true), CancellationToken.None);
        await rig.Engine.RenderAsync(DiagramRig.Request("pie\n  \"a\": 1"), CancellationToken.None);
        Assert.Same(view, rig.Engine.View);
        Assert.Single(rig.Web.Surface.Views);
        Assert.Equal(1, rig.Hosted);
        Assert.Equal(3, rig.Drawings.Count);
        Assert.Equal(1, rig.Expressions.Count(e => e.StartsWith("window.nthDiagramReady", StringComparison.Ordinal)));
        Assert.Single(WebRig.Fake(view!).Log, line => line.StartsWith("Navigate ", StringComparison.Ordinal));
        Assert.True(DiagramRig.Options(rig.Drawings[1])["dark"]!.GetValue<bool>());
        Assert.Equal("pie\n  \"a\": 1", DiagramRig.SourceOf(rig.Drawings[2]));
    }

    [Fact]
    public async Task Drawings_run_one_at_a_time_in_the_order_they_were_asked()
    {
        using var rig = new DiagramRig();
        var gates = new List<TaskCompletionSource<string>>();
        rig.Page = _ =>
        {
            var gate = new TaskCompletionSource<string>();
            gates.Add(gate);
            return gate.Task;
        };
        var first = rig.Engine.RenderAsync(DiagramRig.Request("one"), CancellationToken.None);
        var second = rig.Engine.RenderAsync(DiagramRig.Request("two"), CancellationToken.None);
        var third = rig.Engine.RenderAsync(DiagramRig.Request("three"), CancellationToken.None);
        Assert.Single(gates); // the first is in the page; the others wait their turn
        Assert.Equal("one", DiagramRig.SourceOf(rig.Drawings[0]));
        gates[0].SetResult(DiagramRig.Good("<svg viewBox=\"0 0 1 1\"/>"));
        Assert.Equal("<svg viewBox=\"0 0 1 1\"/>", (await first).Svg);
        await UntilAsync(() => gates.Count == 2); // the semaphore hands the page on from the thread pool
        Assert.Equal("two", DiagramRig.SourceOf(rig.Drawings[1]));
        Assert.False(third.IsCompleted);
        gates[1].SetResult(DiagramRig.Good());
        await second;
        await UntilAsync(() => gates.Count == 3);
        gates[2].SetResult(DiagramRig.Good());
        await third;
        Assert.Equal(["one", "two", "three"], rig.Drawings.Select(DiagramRig.SourceOf));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++) await Task.Delay(1);
        Assert.True(condition());
    }

    [Fact]
    public async Task A_view_that_never_gets_hosted_leaves_the_diagram_unavailable()
    {
        using var rig = new DiagramRig { Host = false };
        var task = rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.False(task.IsCompleted);
        rig.Web.Time.Advance(MermaidRenderEngine.StartWait + TimeSpan.FromSeconds(1));
        var error = await Assert.ThrowsAsync<DiagramRenderException>(() => task);
        Assert.Equal(DiagramFailure.Unavailable, error.Failure);
        Assert.Null(rig.Engine.View); // the view that nobody hosts is closed again
        Assert.Empty(rig.Web.Surface.Views);
    }

    [Fact]
    public async Task A_page_that_does_not_load_or_has_no_mermaid_is_unavailable_and_is_closed()
    {
        using (var rig = new DiagramRig { Load = false })
        {
            var task = rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
            rig.Web.Time.Advance(MermaidRenderEngine.StartWait + TimeSpan.FromSeconds(1));
            var error = await Assert.ThrowsAsync<DiagramRenderException>(() => task);
            Assert.Equal(DiagramFailure.Unavailable, error.Failure);
            Assert.Empty(rig.Web.Surface.Views);
        }
        using (var rig = new DiagramRig { MermaidLoaded = false })
        {
            var error = await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None));
            Assert.Equal(DiagramFailure.Unavailable, error.Failure);
            Assert.Contains("Mermaid", error.Message, StringComparison.Ordinal);
            Assert.Empty(rig.Web.Surface.Views);
        }
    }

    [Fact]
    public async Task What_mermaid_says_about_a_source_comes_back_as_a_failure_with_its_line()
    {
        using var rig = new DiagramRig();
        rig.Page = _ => Task.FromResult(DiagramRig.Failed("syntax", "Parse error on line 3: expected 'SEMI'", 3));
        var error = await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None));
        Assert.Equal(DiagramFailure.Syntax, error.Failure);
        Assert.Equal(3, error.Line);
        Assert.Contains("Parse error", error.Message, StringComparison.Ordinal);
        Assert.NotNull(rig.Engine.View); // a bad source is not a bad page: the view stays for the next one
        rig.Page = _ => Task.FromResult(DiagramRig.Failed("toolarge", "too big"));
        Assert.Equal(DiagramFailure.TooLarge, (await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None))).Failure);
        rig.Page = null;
        Assert.Equal(DiagramRig.Drawing, (await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None)).Svg);
        Assert.Single(rig.Web.Surface.Views);
    }

    [Fact]
    public async Task A_page_that_raises_an_error_or_stops_is_replaced_for_the_next_drawing()
    {
        using var rig = new DiagramRig();
        rig.Page = _ => Task.FromResult("{\"exceptionDetails\":{\"exception\":{\"description\":\"TypeError: boom\"}},\"result\":{\"type\":\"object\"}}");
        var raised = await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None));
        Assert.Equal(DiagramFailure.Stopped, raised.Failure);
        Assert.Contains("boom", raised.Message, StringComparison.Ordinal);
        Assert.Null(rig.Engine.View);
        rig.Page = null;
        await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.Equal(2, rig.Hosted);
        Assert.Single(rig.Web.Surface.Views);
    }

    [Fact]
    public async Task A_page_that_does_not_answer_in_ten_seconds_times_out_and_the_view_is_closed()
    {
        using var rig = new DiagramRig();
        rig.Page = _ => new TaskCompletionSource<string>().Task;
        var task = rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.False(task.IsCompleted);
        rig.Web.Time.Advance(MermaidRenderEngine.RenderWait + TimeSpan.FromSeconds(3));
        var error = await Assert.ThrowsAsync<DiagramRenderException>(() => task);
        Assert.Equal(DiagramFailure.Timeout, error.Failure);
        Assert.Null(rig.Engine.View);
        Assert.Empty(rig.Web.Surface.Views);
    }

    [Fact]
    public async Task A_crashing_renderer_process_ends_the_drawing_at_once()
    {
        using var rig = new DiagramRig();
        rig.Page = _ => new TaskCompletionSource<string>().Task;
        var task = rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.False(task.IsCompleted);
        rig.Fake!.RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);
        var error = await Assert.ThrowsAsync<DiagramRenderException>(() => task);
        Assert.Equal(DiagramFailure.Stopped, error.Failure);
        Assert.Null(rig.Engine.View);
    }

    [Fact]
    public async Task Cancelling_a_drawing_ends_the_wait_and_lets_the_next_one_through()
    {
        using var rig = new DiagramRig();
        var gates = new List<TaskCompletionSource<string>>();
        rig.Page = _ =>
        {
            var gate = new TaskCompletionSource<string>();
            gates.Add(gate);
            return gate.Task;
        };
        using var cancel = new CancellationTokenSource();
        var first = rig.Engine.RenderAsync(DiagramRig.Request("one"), cancel.Token);
        var queued = rig.Engine.RenderAsync(DiagramRig.Request("two"), CancellationToken.None);
        var cancelled = rig.Engine.RenderAsync(DiagramRig.Request("three"), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await UntilAsync(() => gates.Count == 2);
        Assert.Equal("two", DiagramRig.SourceOf(rig.Drawings[1]));
        gates[1].SetResult(DiagramRig.Good());
        await queued;
    }

    [Fact]
    public async Task Without_a_runtime_or_without_mermaid_in_the_build_nothing_is_drawn_and_nothing_is_opened()
    {
        using (var rig = new DiagramRig(installed: null))
        {
            Assert.False(rig.Engine.IsAvailable);
            var error = await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None));
            Assert.Equal(DiagramFailure.Unavailable, error.Failure);
            Assert.Empty(rig.Web.Surface.Views);
            rig.Engine.Prepare();
            Assert.Empty(rig.Web.Surface.Views);
        }
        var empty = Path.Combine(Path.GetTempPath(), "nth-no-libs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            using var rig = new DiagramRig(libraries: new ArtifactLibraryFiles(empty));
            Assert.False(rig.Engine.IsAvailable);
            Assert.Null(rig.Engine.Site.MermaidPath);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public async Task A_source_that_is_empty_or_too_long_is_refused_before_a_view_is_opened()
    {
        using var rig = new DiagramRig();
        Assert.Equal(DiagramFailure.Syntax, (await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request("  \n "), CancellationToken.None))).Failure);
        var huge = new string('a', DiagramLimits.MaxSource + 1);
        Assert.Equal(DiagramFailure.TooLarge, (await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(huge), CancellationToken.None))).Failure);
        Assert.Empty(rig.Web.Surface.Views);
        await rig.Engine.RenderAsync(DiagramRig.Request(new string('a', DiagramLimits.MaxSource)), CancellationToken.None);
        Assert.Equal(DiagramLimits.MaxSource, DiagramRig.SourceOf(rig.Drawings[0]).Length);
    }

    [Fact]
    public async Task The_view_closes_after_thirty_seconds_idle_and_opens_again_on_demand()
    {
        using var rig = new DiagramRig();
        await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.NotNull(rig.Engine.Hosted);
        rig.Web.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.NotNull(rig.Engine.Hosted);
        await rig.Engine.RenderAsync(DiagramRig.Request("again"), CancellationToken.None); // a drawing restarts the clock
        rig.Web.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.NotNull(rig.Engine.Hosted);
        rig.Web.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(rig.Engine.Hosted);
        Assert.Empty(rig.Web.Surface.Views);
        Assert.Equal(1, rig.Unhosted);
        await rig.Engine.RenderAsync(DiagramRig.Request("after"), CancellationToken.None);
        Assert.Equal(2, rig.Hosted);
        Assert.Single(rig.Web.Surface.Views);
    }

    [Fact]
    public async Task Preparing_opens_the_view_ahead_of_the_first_drawing_which_then_finds_it_ready()
    {
        using var rig = new DiagramRig();
        rig.Engine.Prepare();
        Assert.NotNull(rig.Engine.Hosted);
        Assert.Empty(rig.Drawings);
        Assert.Single(rig.Expressions); // the readiness probe only
        await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        Assert.Equal(1, rig.Hosted);
        Assert.Single(rig.Drawings);
        // Nothing else follows it: the idle clock closes the view.
        rig.Web.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(rig.Engine.Hosted);
    }

    [Fact]
    public async Task A_view_whose_window_went_away_is_replaced_for_the_next_drawing()
    {
        using var rig = new DiagramRig();
        await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        var first = rig.Engine.View;
        rig.Fake!.Detach(); // the window that hosted it closed
        await rig.Engine.RenderAsync(DiagramRig.Request("later"), CancellationToken.None);
        Assert.NotSame(first, rig.Engine.View);
        Assert.Equal(2, rig.Hosted);
        Assert.Single(rig.Web.Surface.Views);
        Assert.Equal("later", DiagramRig.SourceOf(rig.Drawings[^1]));
    }

    [Fact]
    public async Task Disposing_closes_the_view_and_refuses_new_drawings()
    {
        var rig = new DiagramRig();
        await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None);
        var changes = 0;
        rig.Engine.AvailabilityChanged += () => changes++;
        rig.Engine.Dispose();
        Assert.Null(rig.Engine.Hosted);
        Assert.False(rig.Engine.IsAvailable);
        Assert.Equal(1, changes);
        await Assert.ThrowsAsync<DiagramRenderException>(() => rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None));
        rig.Web.Dispose();
    }

    [Fact]
    public async Task The_runtime_flow_can_tell_the_live_views_that_drawing_became_possible()
    {
        using var rig = new DiagramRig(installed: null);
        var changes = 0;
        rig.Engine.AvailabilityChanged += () => changes++;
        rig.Engine.RefreshAvailability();
        Assert.Equal(0, changes);
        rig.Web.Engine.InstalledVersion = "141.0.3537.71";
        rig.Web.Surface.RefreshRuntime();
        rig.Engine.RefreshAvailability();
        Assert.Equal(1, changes);
        Assert.True(rig.Engine.IsAvailable);
        rig.Engine.RefreshAvailability();
        Assert.Equal(1, changes);
        Assert.Equal(DiagramRig.Drawing, (await rig.Engine.RenderAsync(DiagramRig.Request(), CancellationToken.None)).Svg);
    }
}
