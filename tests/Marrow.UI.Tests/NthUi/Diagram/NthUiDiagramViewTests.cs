using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;
using Marrow.Core.Web;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Diagram;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Diagram;

// DESIGN.md 7.2 and 7.4 (web surfaces W16c; ED-NEW-web-w16c-1): a Mermaid diagram of a live answer is asked of the renderer once
// per completed statement (never faster than every 0.2 s, never while a drawing is in the making), the SVG that comes back is
// sanitised and drawn natively in the thread's palette, a source Mermaid cannot read yet keeps the last good drawing, and
// without a renderer the source shows as it did before. Nothing here starts a web view: the renderer is a fake the test holds.
[Collection("Headless")]
public sealed class NthUiDiagramViewTests(HeadlessSession headless)
{
    private const string Streaming = """
        text "Before the diagram."
        diagram "Request flow" alt="Client to server to database"
          graph LR
            A[Client] --> B[Server]
            B --> C[(Database)]
            C --> D[Backup]
            D --> E[Archive]
        text "After the diagram."
        """;

    private static void Until(NthUiRig rig, Func<bool> condition, string what, double seconds = 5)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < end)
        {
            rig.Tick();
            Thread.Sleep(1);
        }
        Assert.True(condition(), what);
    }

    private static IDisposable Use(FakeDiagramEngine engine)
    {
        NthUiDiagrams.ClearCache();
        return DiagramEngines.Shared.Override(engine);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void With_no_renderer_the_source_shows_as_before_and_nothing_is_asked() => headless.OnUiThread(() =>
    {
        NthUiDiagrams.ClearCache();
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        rig.Settle();
        var view = rig.ViewOf<NthUiDiagramView>();
        Assert.True(view.IsSourceShown);
        Assert.Null(view.Document);
        Assert.False(view.SourceToggle.IsEnabled);
        Assert.Contains("Diagram · Request flow", rig.Texts());
        Assert.Contains(rig.TextsOf("diagram"), t => t.Contains("A[Client] --> B[Server]", StringComparison.Ordinal));
        Assert.Equal("Client to server to database", AutomationProperties.GetName(view.Control));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_whole_diagram_is_asked_once_and_drawn_natively_in_the_threads_palette() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        var view = rig.ViewOf<NthUiDiagramView>();
        Until(rig, () => view.Document is not null, "the diagram was drawn");
        rig.Settle();
        var request = Assert.Single(engine.Requests);
        Assert.Equal("graph LR\n  A[Client] --> B[Server]\n  B --> C[(Database)]", request.Source);
        Assert.False(request.Theme.Dark);
        Assert.Equal(NthUiDiagramTheme.FontFamily, request.Theme.FontFamily);
        Assert.False(view.IsSourceShown);
        Assert.True(view.Canvas.IsVisible);
        Assert.False(view.Status.IsVisible);
        Assert.True(view.SourceToggle.IsEnabled);
        Assert.False(view.SourceToggle.IsChecked);
        Assert.True(view.Canvas.Drawn >= 2);
        Assert.Equal(1, view.Canvas.Opacity);
        Assert.Equal("Client to server to database", AutomationProperties.GetName(view.Canvas));
        // The theme it was drawn with is the thread's own palette: opaque colours resolved from nth's tokens.
        Assert.All(request.Theme.Variables.Where(v => v.Value.StartsWith('#')), v => Assert.Equal(7, v.Value.Length));
        Assert.Contains("primaryColor", request.Theme.Variables.Keys);
        Assert.Contains("lineColor", request.Theme.Variables.Keys);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_streaming_diagram_is_asked_for_by_completed_statement_never_faster_than_every_0_2_seconds_and_never_twice_at_once() => headless.OnUiThread(() =>
    {
        var held = new List<TaskCompletionSource<DiagramRenderResult>>();
        var engine = new FakeDiagramEngine();
        engine.Answer = (n, request) =>
        {
            if (n > 1) return Task.FromResult(engine.Drawing(request));
            var gate = new TaskCompletionSource<DiagramRenderResult>();
            held.Add(gate);
            return gate.Task;
        };
        using var _ = Use(engine);
        using var rig = new NthUiRig();
        rig.Stream(Streaming, 6);
        // The first statements went to the renderer; its answer is still being made, so the later lines wait.
        Assert.Single(engine.Requests);
        Assert.True(engine.Requests[0].Source.Length < Streaming.Length);
        var view = rig.ViewOf<NthUiDiagramView>();
        Assert.Null(view.Document);
        Assert.True(view.Status.IsVisible);
        Assert.Equal("Drawing the diagram", view.Status.Text);
        rig.View.Complete();
        rig.Run(1);
        Assert.Single(engine.Requests); // the end of the stream does not start a second drawing in the making
        held[0].SetResult(engine.Drawing(engine.Requests[0]));
        Until(rig, () => engine.Requests.Count == 2 && view.Document is not null, "the newest source went out when the first answer came back");
        Until(rig, () => view.Renders == 2, "two drawings asked for");
        Assert.Equal("graph LR\n  A[Client] --> B[Server]\n  B --> C[(Database)]\n  C --> D[Backup]\n  D --> E[Archive]", engine.Requests[1].Source);
        rig.Settle();
        Assert.Equal(2, engine.Requests.Count);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Requests_are_spaced_by_the_redraw_interval_while_lines_keep_arriving() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using var rig = new NthUiRig();
        var times = new List<double>();
        var seen = 0;
        rig.Stream(Streaming, 3, () =>
        {
            if (engine.Requests.Count > seen)
            {
                seen = engine.Requests.Count;
                times.Add(rig.Time.GetUtcNow().Ticks / (double)TimeSpan.TicksPerSecond);
            }
            rig.Pump();
        });
        rig.View.Complete();
        Until(rig, () => engine.Requests.Count >= 1 && rig.ViewOf<NthUiDiagramView>().Document is not null, "drawn");
        for (var i = 1; i < times.Count; i++)
            Assert.True(times[i] - times[i - 1] >= NthUiDiagrams.RedrawSeconds - .02, $"requests {i - 1} and {i} were {times[i] - times[i - 1]:0.000} s apart");
        Assert.True(engine.Requests.Count <= 7, $"a 4-statement diagram asked {engine.Requests.Count} times");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_source_that_cannot_be_read_yet_keeps_the_last_good_drawing_and_the_end_of_the_stream_says_so() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        engine.Answer = (n, request) => n switch
        {
            1 => Task.FromResult(engine.Drawing(request)),
            2 => Task.FromException<DiagramRenderResult>(new DiagramRenderException(DiagramFailure.Syntax, "Parse error on line 4", 4)),
            _ => Task.FromException<DiagramRenderResult>(new DiagramRenderException(DiagramFailure.Syntax, "Parse error on line 3", 3)),
        };
        using var _ = Use(engine);
        using var rig = new NthUiRig();
        rig.View.Append("diagram \"Flow\"\n  graph LR\n    A --> B\n");
        rig.Pump();
        rig.Run(.3);
        var view = rig.ViewOf<NthUiDiagramView>();
        Until(rig, () => view.Document is not null, "first drawing");
        var first = view.Document;
        rig.View.Append("    B --> \n");
        rig.Run(.5);
        Until(rig, () => engine.Requests.Count >= 2, "second request");
        rig.Pump();
        Assert.Same(first, view.Document); // the broken statement did not blank the drawing
        Assert.False(view.Failed);
        Assert.False(view.IsSourceShown);
        rig.View.Complete();
        Until(rig, () => view.Failed, "the end of the stream could not be drawn");
        Assert.True(view.IsSourceShown);
        Assert.True(view.Status.IsVisible);
        Assert.Equal("This diagram could not be drawn (line 3)", view.Status.Text);
        Assert.False(view.SourceToggle.IsEnabled);
        Assert.Contains(rig.TextsOf("diagram"), t => t.Contains("B --> ", StringComparison.Ordinal));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_renderer_that_stops_is_treated_as_a_failed_end_not_a_crash() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine { Answer = (_, _) => Task.FromException<DiagramRenderResult>(new InvalidOperationException("the renderer page crashed")) };
        using (Use(engine))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Failed, "failed");
            Assert.True(view.IsSourceShown);
            Assert.Equal("This diagram could not be drawn", view.Status.Text);
        }
        var unavailable = new FakeDiagramEngine { Answer = (_, _) => Task.FromException<DiagramRenderResult>(new DiagramRenderException(DiagramFailure.Unavailable, "no runtime")) };
        using (Use(unavailable))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Failed, "failed when unavailable");
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void What_the_page_returns_is_sanitised_before_anything_is_drawn() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine { Answer = (_, _) => Task.FromResult(new DiagramRenderResult(SvgSamples.Hostile)) };
        using (Use(engine))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Document is not null, "drawn");
            var markup = NthSvgWriter.ToMarkup(view.Document!);
            foreach (var forbidden in SvgSamples.Forbidden) Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
        }
        // A page that returns something that is not an svg at all is a failed drawing, not a crash.
        var broken = new FakeDiagramEngine { Answer = (_, _) => Task.FromResult(new DiagramRenderResult("<html><script>alert(1)</script></html>")) };
        using (Use(broken))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Failed, "a non-svg answer failed");
            Assert.Null(view.Document);
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_theme_change_draws_the_diagram_again_in_the_other_palette_and_a_drawn_theme_is_not_asked_for_twice() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        var view = rig.ViewOf<NthUiDiagramView>();
        Until(rig, () => view.Document is not null, "drawn in light");
        var light = view.Document;
        Assert.Single(engine.Requests);
        rig.Window.RequestedThemeVariant = ThemeVariant.Dark;
        rig.Pump();
        rig.Run(.3);
        Until(rig, () => engine.Requests.Count == 2 && !ReferenceEquals(view.Document, light), "drawn again in dark");
        Assert.True(engine.Requests[1].Theme.Dark);
        Assert.NotEqual(engine.Requests[0].Theme.Variables["primaryColor"], engine.Requests[1].Theme.Variables["primaryColor"]);
        var dark = view.Document;
        rig.Window.RequestedThemeVariant = ThemeVariant.Light;
        rig.Pump();
        rig.Run(.3);
        Until(rig, () => ReferenceEquals(view.Document, light), "light again from the cache");
        Assert.Equal(2, engine.Requests.Count);
        Assert.NotSame(dark, view.Document);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_reopened_answer_comes_from_the_cache_without_asking_the_renderer_again() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using (var first = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English)))
        {
            var view = first.ViewOf<NthUiDiagramView>();
            Until(first, () => view.Document is not null, "drawn");
        }
        using var second = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        var reopened = second.ViewOf<NthUiDiagramView>();
        Assert.NotNull(reopened.Document); // at once, before any pumping of the dispatcher
        Assert.Single(engine.Requests);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_reader_can_flip_between_the_drawing_and_its_source_and_copy_the_source() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        var copied = new List<NthUiEffect>();
        rig.View.EffectRequested += (_, e) =>
        {
            copied.Add(e.Effect);
            e.Handled = true;
        };
        var view = rig.ViewOf<NthUiDiagramView>();
        Until(rig, () => view.Document is not null, "drawn");
        Assert.Equal("Show source", AutomationProperties.GetName(view.SourceToggle));
        rig.Click(view.SourceToggle);
        Assert.True(view.IsSourceShown);
        Assert.False(view.Scroller.IsVisible);
        Assert.Equal("Show diagram", AutomationProperties.GetName(view.SourceToggle));
        Assert.Contains(rig.TextsOf("diagram"), t => t.Contains("B --> C[(Database)]", StringComparison.Ordinal));
        rig.Click(view.SourceToggle);
        Assert.False(view.IsSourceShown);
        Assert.True(view.Scroller.IsVisible);
        rig.Click(NthUiRig.Named<Button>(view.Control, "Copy source"));
        var effect = Assert.Single(copied);
        Assert.Equal(NthUiEffectKind.Copy, effect.Kind);
        Assert.Equal("graph LR\n  A[Client] --> B[Server]\n  B --> C[(Database)]", effect.Text);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_wide_diagram_scales_down_to_a_floor_and_scrolls_beyond_and_a_small_one_is_never_enlarged() => headless.OnUiThread(() =>
    {
        var wide = new FakeDiagramEngine { Width = 1200 };
        using (Use(wide))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Document is not null, "drawn");
            rig.Settle();
            var viewport = view.Scroller.Viewport.Width;
            Assert.InRange(view.Canvas.Bounds.Width, 1200 * NthUiDiagrams.MinScale - .5, 1200 * NthUiDiagrams.MinScale + .5);
            Assert.True(view.Scroller.Extent.Width > viewport, "it scrolls sideways");
            Assert.True(view.Canvas.Scale < 1 && view.Canvas.Scale >= NthUiDiagrams.MinScale - .001);
        }
        var small = new FakeDiagramEngine { Width = 200 };
        using (Use(small))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Document is not null, "drawn");
            rig.Settle();
            Assert.Equal(200, view.Canvas.Bounds.Width, 1);
            Assert.Equal(1, view.Canvas.Scale, 3);
        }
        var fits = new FakeDiagramEngine { Width = 400 };
        using (Use(fits))
        {
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English), width: 520);
            var view = rig.ViewOf<NthUiDiagramView>();
            Until(rig, () => view.Document is not null, "drawn");
            rig.Settle();
            Assert.True(view.Canvas.Bounds.Width >= 399 && view.Canvas.Scale > .99, $"a diagram that fits is drawn whole ({view.Canvas.Bounds.Width})");
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Nothing_before_the_diagram_moves_while_it_streams_in_and_is_drawn() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using var rig = new NthUiRig();
        Rect? first = null;
        void Watch()
        {
            var text = rig.View.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Before the diagram.");
            if (text is null) return;
            var rect = rig.Layout(text);
            first ??= rect;
            Assert.Equal(first, rect);
        }
        rig.Stream(Streaming, 5, Watch);
        rig.View.Complete();
        var view = rig.ViewOf<NthUiDiagramView>();
        for (var i = 0; i < 400 && (view.Document is null || rig.View.IsMoving); i++)
        {
            rig.Tick();
            Thread.Sleep(1);
            Watch();
        }
        rig.Settle(each: Watch);
        Assert.NotNull(view.Document);
        Assert.NotNull(first);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_renderer_that_arrives_late_is_picked_up_and_one_that_leaves_before_a_drawing_shows_the_source() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine { Available = false };
        using var _ = Use(engine);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        var view = rig.ViewOf<NthUiDiagramView>();
        Assert.True(view.IsSourceShown);
        Assert.Empty(engine.Requests);
        engine.Available = true;
        engine.RaiseAvailability();
        Until(rig, () => view.Document is not null, "drawn once the renderer was there");
        Assert.False(view.IsSourceShown);
        Assert.Single(engine.Requests);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_drawing_comes_in_with_a_fade_and_a_redraw_while_streaming_dips_only_to_sixty_percent() => headless.OnUiThread(() =>
    {
        var engine = new FakeDiagramEngine();
        using var _ = Use(engine);
        using var rig = new NthUiRig();
        rig.View.Append("diagram \"Flow\"\n  graph LR\n    A --> B\n");
        rig.Pump();
        rig.Run(.3);
        var view = rig.ViewOf<NthUiDiagramView>();
        Until(rig, () => view.Document is not null, "first drawing");
        var seen = new List<double>();
        for (var i = 0; i < 14; i++)
        {
            rig.Tick();
            seen.Add(view.Canvas.Opacity);
        }
        Assert.Equal(1, view.Canvas.Opacity);
        rig.View.Append("    B --> C\n");
        var before = view.Document;
        for (var i = 0; i < 600 && ReferenceEquals(view.Document, before); i++)
        {
            rig.Tick();
            Thread.Sleep(1);
        }
        Assert.NotSame(before, view.Document);
        Assert.InRange(view.Canvas.Opacity, .59, .8); // the new drawing comes in over the old from sixty percent
        var low = 1.0;
        for (var i = 0; i < 24; i++)
        {
            rig.Tick();
            low = Math.Min(low, view.Canvas.Opacity);
        }
        Assert.True(low >= .59, $"a redraw never dips below 60 %: {low}");
        Assert.Equal(1, view.Canvas.Opacity);
    });
}
