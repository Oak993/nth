using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.7: every ProcessFailed case has a designed outcome (card, model step result, log), three crashes of one page
// in 5 minutes stop automatic reloads, and "Restart Visus" recreates the environment and restores every tab by URL.
[Trait("Decision", "ED-NEW-web-1")]
[Trait("Feature", "W-7")]
public sealed class WebFailureTests
{
    [Fact]
    public void A_renderer_crash_shows_the_crash_card_over_the_dimmed_snapshot_and_fails_the_running_step()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var idle = rig.OpenLive(shown: true);
        var failed = new List<(WebSurfaceView View, string Reason)>();
        rig.Surface.StepFailed += (v, r) => failed.Add((v, r));
        using var step = view.BeginModelStep();

        WebRig.Fake(view).RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);
        WebRig.Fake(idle).RaiseProcessFailed(WebProcessFailureKind.FrameRenderProcessExited);

        var card = Assert.IsType<WebFailureCard>(view.Failure);
        Assert.Equal(WebFailureKind.RendererCrashed, card.Kind);
        Assert.Equal("This page stopped working.", card.Title);
        Assert.Equal("Reload opens the page again. Anything typed into a form on it is gone.", card.Body);
        Assert.Equal([WebFailureAction.Reload, WebFailureAction.CloseTab], card.Buttons.Select(b => b.Action));
        Assert.Equal(["Reload", "Close tab"], card.Buttons.Select(b => b.Title));
        Assert.True(card.Dimmed);
        Assert.True(card.ShowsCard);
        Assert.Equal(WebFailureKind.RendererCrashed, idle.Failure?.Kind);
        var (who, reason) = Assert.Single(failed); // only the view a step was running on
        Assert.Same(view, who);
        Assert.Equal("page crashed", reason);
    }

    [Fact]
    public void Reload_on_the_crash_card_clears_it_and_reloads_the_page()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);

        view.InvokeFailureAction(WebFailureAction.Reload);

        Assert.Null(view.Failure);
        Assert.Contains("Reload", fake.Log);
    }

    [Fact]
    public void Close_tab_on_the_crash_card_asks_the_pane_to_close_it()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var closed = new List<WebSurfaceView>();
        rig.Surface.CloseRequested += closed.Add;
        WebRig.Fake(view).RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);

        view.InvokeFailureAction(WebFailureAction.CloseTab);

        Assert.Same(view, Assert.Single(closed));
    }

    [Fact]
    public void Three_crashes_of_one_page_in_five_minutes_stop_automatic_reloads_and_say_so()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.RaiseNavigationCompleted("https://a.test/app#one");
        for (var i = 0; i < 2; i++)
        {
            fake.RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);
            Assert.DoesNotContain(WebFailureCard.ReloadsStoppedBody, view.Failure!.Body, StringComparison.Ordinal);
            view.InvokeFailureAction(WebFailureAction.Reload);
            rig.Time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.True(view.TryAutomaticReload());
        fake.RaiseNavigationCompleted("https://a.test/app#two"); // the same page: the fragment does not count
        fake.RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);

        Assert.EndsWith(WebFailureCard.ReloadsStoppedBody, view.Failure!.Body, StringComparison.Ordinal);
        Assert.Equal("It stopped working 3 times in 5 minutes, so nth no longer reloads it by itself.", WebFailureCard.ReloadsStoppedBody);
        Assert.False(view.TryAutomaticReload());

        rig.Time.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1)); // the first crash leaves the window
        Assert.True(view.TryAutomaticReload());
    }

    [Fact]
    public void An_unresponsive_renderer_shows_the_wait_card_and_wait_clears_it()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        WebRig.Fake(view).RaiseProcessFailed(WebProcessFailureKind.RenderProcessUnresponsive);

        var card = Assert.IsType<WebFailureCard>(view.Failure);
        Assert.Equal(WebFailureKind.RendererHung, card.Kind);
        Assert.Equal("This page is not responding.", card.Title);
        Assert.Equal([WebFailureAction.Wait, WebFailureAction.Reload], card.Buttons.Select(b => b.Action));

        view.InvokeFailureAction(WebFailureAction.Wait);
        Assert.Null(view.Failure);
    }

    [Fact]
    public void No_frame_reply_in_five_seconds_marks_a_visible_page_hung_and_a_reply_clears_it()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.AnswersFrames = false;

        rig.Surface.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1));
        Assert.Null(view.Failure);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(WebFailureKind.RendererHung, view.Failure?.Kind);

        fake.AnswerFrames();
        Assert.Null(view.Failure);
    }

    [Fact]
    public void A_shown_page_is_pinged_for_a_frame_at_most_once_a_second()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        int Pings() => fake.Log.Count(entry => entry == "Frames 1");

        rig.Surface.Tick();
        Assert.Equal(1, Pings());
        for (var i = 0; i < 3; i++)
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(250));
            rig.Surface.Tick();
        }

        Assert.Equal(1, Pings()); // the page answered at once, but a second has not passed
        rig.Time.Advance(TimeSpan.FromMilliseconds(250));
        rig.Surface.Tick();
        Assert.Equal(2, Pings());

        view.SetShown(false); // a hidden page is not probed at all
        rig.Time.Advance(TimeSpan.FromSeconds(3));
        rig.Surface.Tick();
        Assert.Equal(2, Pings());
    }

    [Fact]
    public void A_step_on_a_hung_page_fails_after_25_seconds()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var failed = new List<string>();
        rig.Surface.StepFailed += (_, reason) => failed.Add(reason);
        using var step = view.BeginModelStep();
        WebRig.Fake(view).RaiseProcessFailed(WebProcessFailureKind.RenderProcessUnresponsive);

        rig.Time.Advance(TimeSpan.FromSeconds(24));
        rig.Surface.Tick();
        Assert.Empty(failed);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Surface.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Surface.Tick();

        Assert.Equal(["page not responding"], failed);
    }

    [Fact]
    public void The_browser_process_going_away_shows_one_card_in_the_first_pane_and_fails_every_step()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(shown: true);
        var second = rig.OpenLive(window: "w2", shown: true);
        var failed = new List<(WebSurfaceView View, string Reason)>();
        rig.Surface.StepFailed += (v, r) => failed.Add((v, r));
        using var one = first.BeginModelStep();
        using var two = second.BeginModelStep();

        WebRig.Fake(second).RaiseProcessFailed(WebProcessFailureKind.BrowserProcessExited);
        WebRig.Fake(first).RaiseProcessFailed(WebProcessFailureKind.BrowserProcessExited); // every view reports it

        Assert.Equal(WebFailureKind.BrowserGone, first.Failure?.Kind);
        Assert.Equal(WebFailureKind.BrowserGone, second.Failure?.Kind);
        Assert.True(first.Failure!.ShowsCard);
        Assert.False(second.Failure!.ShowsCard);
        Assert.Equal("Visus stopped.", first.Failure.Title);
        Assert.Equal([WebFailureAction.RestartVisus], first.Failure.Buttons.Select(b => b.Action));
        Assert.Equal(2, failed.Count);
        Assert.All(failed, f => Assert.Equal("Visus stopped", f.Reason));
    }

    [Fact]
    public void Restart_Visus_makes_a_new_environment_and_restores_every_tab_by_address()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(url: "https://a.test/one", shown: true);
        var second = rig.OpenLive(url: "https://a.test/two", shown: true);
        WebRig.Fake(first).RaiseProcessFailed(WebProcessFailureKind.BrowserProcessExited);

        first.InvokeFailureAction(WebFailureAction.RestartVisus);

        Assert.Equal(1, rig.Engine.EnvironmentResets);
        Assert.Null(first.Failure);
        Assert.Null(second.Failure);
        Assert.Equal(WebLiveness.Pending, first.Liveness);
        Assert.Equal(4, rig.Engine.Views.Count);
        first.AttachNative(5, 6);
        Assert.Contains("Navigate https://a.test/one", WebRig.Fake(first).Log);
    }

    [Theory]
    [InlineData(WebProcessFailureKind.GpuProcessExited)]
    [InlineData(WebProcessFailureKind.UtilityProcessExited)]
    [InlineData(WebProcessFailureKind.UnknownProcessExited)]
    public void Other_process_failures_show_nothing(WebProcessFailureKind kind)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var failed = 0;
        rig.Surface.StepFailed += (_, _) => failed++;
        using var step = view.BeginModelStep();

        WebRig.Fake(view).RaiseProcessFailed(kind);

        Assert.Null(view.Failure);
        Assert.Equal(0, failed);
        Assert.Equal(WebLiveness.Live, view.Liveness);
    }

    [Fact]
    public void A_guarded_action_interrupted_by_a_crash_says_nothing_was_sent()
    {
        Assert.Equal("The page crashed before this ran. Nothing was sent.", WebSurface.GuardedActionCrashed);
    }
}
