using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Diagram;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Diagram;

// DESIGN.md 7.2 and 7.4 (web surfaces W16c; ED-NEW-web-w16c-1): the svg line of a live answer is sanitised and drawn natively
// while its body streams in; the box it fills is known from the opening tag so nothing moves; hostile markup never reaches the
// picture; a picture that cannot be read says so quietly; a saved answer shown whole draws at once.
[Collection("Headless")]
public sealed class NthUiSvgViewTests(HeadlessSession headless)
{
    private const string Streaming = """
        text "Before the picture."
        svg alt="Two shapes" width=160
          <svg viewBox="0 0 160 80">
            <rect width="60" height="60" fill="#ff0000"/>
            <circle cx="120" cy="40" r="30" fill="#0000ff"/>
          </svg>
        text "After the picture."
        """;

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_picture_draws_part_by_part_as_the_body_streams_and_nothing_before_it_moves() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var before = (Rect?)null;
        void Watch()
        {
            var first = rig.View.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Before the picture.");
            if (first is null) return;
            var rect = rig.Layout(first);
            before ??= rect;
            Assert.Equal(before, rect);
        }
        var shapes = new List<int>();
        rig.Stream(Streaming, 5, () =>
        {
            Watch();
            if (rig.View.Document.Nodes.Any(n => n.Kind == "svg") && rig.View.ViewFor(rig.Node("svg").Key) is NthUiSvgView svg && svg.Document is { } doc)
                shapes.Add(doc.Root.Children.Count);
        });
        rig.View.Complete();
        rig.Settle(each: Watch);
        var view = rig.ViewOf<NthUiSvgView>();
        Assert.NotNull(view.Document);
        Assert.Equal(2, view.Document!.Root.Children.Count);
        Assert.Contains(1, shapes); // the rect was drawn before the circle arrived
        Assert.True(shapes.SequenceEqual(shapes.Order()), "the picture only grows while it streams");
        Assert.Equal(new Size(160, 80), view.Canvas.Bounds.Size);
        Assert.Equal(1, view.Canvas.Opacity);
        Assert.False(view.Failed);
        Assert.Equal("Two shapes", AutomationProperties.GetName(view.Canvas));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_box_is_the_same_from_the_first_drawn_frame_to_the_last() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var sizes = new HashSet<Size>();
        rig.Stream(Streaming, 4, () =>
        {
            if (rig.View.Document.Nodes.Any(n => n.Kind == "svg") && rig.View.ViewFor(rig.Node("svg").Key) is NthUiSvgView { Document: not null } view)
                sizes.Add(view.Canvas.DesiredSize);
        });
        rig.View.Complete();
        rig.Settle();
        Assert.Single(sizes);
        Assert.Equal(new Size(160, 80), sizes.Single());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_line_of_the_language_doc_draws_with_its_alt_as_the_name_and_a_whole_show_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Svg, culture: NthUiRig.English));
        rig.Settle();
        var view = rig.ViewOf<NthUiSvgView>();
        Assert.NotNull(view.Document);
        Assert.Equal(new Size(120, 40), view.Canvas.Bounds.Size);
        Assert.Equal("A rising line", AutomationProperties.GetName(view.Canvas));
        Assert.Equal(1, view.Canvas.Opacity);
        Assert.Equal(0, rig.Frames.Pending);
        Assert.Contains("Line art draws in the text colour.", rig.Texts());
        Assert.DoesNotContain("A rising line", rig.Texts()); // the note stood in only until the picture could be drawn
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Hostile_markup_in_the_body_never_reaches_the_drawing() => headless.OnUiThread(() =>
    {
        var body = string.Join("\n", SvgSamples.Hostile.Split('\n').Select(line => "  " + line));
        using var rig = new NthUiRig(NthUiView.Show("svg alt=\"Hostile\"\n" + body, culture: NthUiRig.English));
        rig.Settle();
        var view = rig.ViewOf<NthUiSvgView>();
        Assert.NotNull(view.Document);
        var markup = NthSvgWriter.ToMarkup(view.Document!);
        foreach (var forbidden in SvgSamples.Forbidden) Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<script> removed", view.Document!.Removed);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_picture_that_cannot_be_read_ends_as_a_quiet_note_with_the_alt_kept_as_its_name() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show("svg alt=\"A broken picture\"\n  this is not markup at all\ntext \"After.\"", culture: NthUiRig.English));
        rig.Settle();
        var view = rig.ViewOf<NthUiSvgView>();
        Assert.True(view.Failed);
        Assert.Null(view.Document);
        Assert.False(view.Canvas.IsVisible);
        Assert.Contains("This picture could not be drawn", rig.Texts());
        Assert.Equal("A broken picture", AutomationProperties.GetName(view.Control.GetVisualDescendants().OfType<Border>().First()));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_picture_whose_closing_tag_never_came_still_draws_at_the_end() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show("svg alt=\"Unclosed\"\n  <svg viewBox=\"0 0 50 50\"><rect width=\"20\" height=\"20\" fill=\"#00ff00\"/>", culture: NthUiRig.English));
        rig.Settle();
        var view = rig.ViewOf<NthUiSvgView>();
        Assert.NotNull(view.Document);
        Assert.False(view.Failed);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_picture_fades_in_over_0_18_seconds_and_with_Reduce_Motion_over_0_15(bool calm) => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(reduceMotion: calm);
        rig.View.Append(Streaming);
        rig.Pump();
        var view = rig.ViewOf<NthUiSvgView>(); // the picture is read as the line is placed, and starts invisible
        Assert.NotNull(view.Document);
        Assert.Equal(0, view.Canvas.Opacity);
        var seen = new List<double>();
        for (var i = 0; i < 14; i++)
        {
            rig.Tick();
            seen.Add(view.Canvas.Opacity);
        }
        Assert.True(seen.SequenceEqual(seen.Order()), "opacity only rises");
        Assert.InRange(seen[4], .05, .99);
        if (calm) Assert.Equal(1, seen[9]);
        else Assert.True(seen[9] < 1, $"still fading at 0.17 s: {seen[9]}");
        Assert.Equal(1, seen[12]);
        Assert.Equal(1, view.Canvas.Opacity);
    });
}
