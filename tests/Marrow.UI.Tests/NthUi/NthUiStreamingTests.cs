using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4 and 13.3 (web surfaces W16a; ED-NEW-web-w16a-1): NthUiView renders progressively while the stream arrives;
// a new node fades in and its container springs open; earlier nodes never move except by growth below them, read as layout
// bounds compared on every frame; a line still arriving shows after a quarter second and keeps its slot when it completes.
[Collection("Headless")]
public sealed class NthUiStreamingTests(HeadlessSession headless)
{
    public static TheoryData<string> Samples => new(NthUiSamples.All.Select(s => s.Name));

    private static string Sample(string name) => NthUiSamples.All.Single(s => s.Name == name).Text;

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    [MemberData(nameof(Samples))]
    public void Every_sample_streamed_in_small_pieces_ends_exactly_as_the_whole_text_shown_at_once(string name) => headless.OnUiThread(() =>
    {
        var text = Sample(name);
        IReadOnlyList<string> streamedTexts;
        IReadOnlyList<(string Key, Rect Bounds)> streamedSlots;
        using (var rig = new NthUiRig())
        {
            rig.Stream(text, 7);
            rig.View.Complete();
            rig.Settle();
            Assert.False(rig.View.IsMoving);
            streamedTexts = rig.Texts();
            streamedSlots = rig.Slots().Select(s => (s.Key, rig.Layout(s))).ToList();
            Assert.All(rig.Slots(), s => Assert.True(s.IsHeld || !s.IsShown || s.Opacity is 1, $"{s.Key} opacity {s.Opacity}"));
        }
        using var whole = new NthUiRig(NthUiView.Show(text, culture: NthUiRig.English));
        Assert.NotEmpty(streamedTexts);
        Assert.Equal(whole.Texts(), streamedTexts);
        var wholeSlots = whole.Slots().Select(s => (s.Key, whole.Layout(s))).ToList();
        Assert.Equal(wholeSlots.Select(s => s.Key), streamedSlots.Select(s => s.Key));
        for (var i = 0; i < wholeSlots.Count; i++)
        {
            var (key, expected) = wholeSlots[i];
            var actual = streamedSlots[i].Bounds;
            Assert.True(Math.Abs(expected.Y - actual.Y) < .5 && Math.Abs(expected.Height - actual.Height) < .5 && Math.Abs(expected.X - actual.X) < .5,
                $"{name} {key}: whole {expected}, streamed {actual}");
        }
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    [MemberData(nameof(Samples))]
    public void Earlier_nodes_never_move_on_any_frame_while_later_lines_and_half_lines_arrive(string name) => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var lines = Sample(name).Split('\n');
        var pieces = lines.SelectMany((line, i) =>
        {
            var end = i < lines.Length - 1 ? "\n" : "";
            return line.Length > 16 ? new[] { line[..(line.Length / 2)], line[(line.Length / 2)..] + end } : [line + end];
        }).Where(p => p.Length > 0).ToList();
        var checks = 0;
        foreach (var piece in pieces)
        {
            // A slot with no height yet (an empty container) shows nothing, so only drawn nodes are held to their place.
            var before = rig.Slots().Where(s => !s.IsHeld && s.Bounds.Height >= .5).ToDictionary(s => s, s => rig.Layout(s));
            void Check()
            {
                foreach (var (slot, was) in before)
                {
                    if (!slot.IsEffectivelyVisible || slot.IsHeld || !slot.IsShown) continue;
                    var now = rig.Layout(slot);
                    checks++;
                    Assert.True(Math.Abs(now.Y - was.Y) < .5 && Math.Abs(now.X - was.X) < .5,
                        $"{name}: {slot.Key} moved from {was.Position} to {now.Position} after \"{piece.Trim()}\"");
                }
            }
            rig.View.Append(piece);
            rig.Pump();
            Check();
            rig.Run(.3, Check);
            rig.Settle(3, Check);
        }
        rig.View.Complete();
        rig.Pump();
        rig.Settle(3);
        Assert.True(checks > 0);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Text_still_arriving_shows_after_a_quarter_second_and_its_line_keeps_the_same_slot() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("card \"Notes\"\n  text \"Hello wor");
        rig.Pump();
        Assert.DoesNotContain("Hello wor", rig.Texts());
        rig.Run(.2);
        Assert.DoesNotContain("Hello wor", rig.Texts());
        rig.Run(.1);
        rig.Settle();
        Assert.Contains("Hello wor", rig.Texts());
        var slot = rig.Slots().Single(s => s.Key != rig.Node("card").Key && s.Child is { } child && Contains(child, "Hello wor"));
        var at = rig.Layout(slot);
        rig.View.Append("ld, again\"\n");
        rig.Pump();
        Assert.Same(slot, rig.View.SlotFor(rig.Node("text").Key));
        Assert.Equal(1, slot.Opacity);
        Assert.Equal(at.Position, rig.Layout(slot).Position);
        Assert.Contains("Hello world, again", rig.Texts());
        Assert.DoesNotContain("Hello wor", rig.Texts());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_tail_expression_still_arriving_never_shows_its_source() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("state n = 2\ntext = fmt(n * 3");
        rig.Run(.5);
        Assert.Empty(rig.Texts());
        rig.View.Append(", \"0\")\n");
        rig.Settle();
        Assert.Equal(["6"], rig.Texts());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_centred_row_holds_its_children_until_it_closes_then_they_fade_in_where_they_sit() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("row align=between\n  badge \"Pro\"\n  text \"Seats\"\n");
        rig.Settle();
        var held = rig.Slots().Where(s => s.IsHeld).ToList();
        Assert.Equal(2, held.Count);
        Assert.All(held, s => Assert.Equal(0, s.Opacity));
        var spots = held.Select(rig.Layout).ToList();
        rig.View.Append("text \"after\"\n");
        rig.Pump();
        rig.Tick();
        Assert.All(held, s => Assert.False(s.IsHeld));
        Assert.All(held, s => Assert.InRange(s.Opacity, 0, .99));
        rig.Settle();
        Assert.All(held, s => Assert.Equal(1, s.Opacity));
        Assert.Equal(spots, held.Select(rig.Layout).ToList());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_form_gets_its_submit_button_when_it_closes_so_it_never_slides_down_as_fields_arrive() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.Stream(NthUiSamples.Form, 9);
        rig.Settle();
        Assert.DoesNotContain(rig.View.GetVisualDescendants().OfType<Button>(), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Request booking");
        rig.View.Complete();
        rig.Settle();
        var submit = NthUiRig.Named<Button>(rig.View, "Request booking");
        var form = rig.Node("form");
        var last = rig.Slots().Where(s => s.Key != form.Key && s.Key.StartsWith(form.Key, StringComparison.Ordinal)).MaxBy(s => rig.Layout(s).Bottom)!;
        Assert.Equal(form.Key + "/submit", last.Key);
        Assert.True(submit.IsEffectivelyVisible);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_view_shown_whole_places_every_node_in_its_end_state_without_asking_for_frames() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        Assert.Equal(0, rig.Frames.Pending);
        Assert.False(rig.View.IsMoving);
        Assert.All(rig.Slots().Where(s => s.IsShown), s => Assert.Equal(1, s.Opacity));
        Assert.All(rig.Slots().Where(s => s.IsShown), s => Assert.Equal(1, s.Openness));
        Assert.Contains("$134", rig.TextsOf("table"));
    });

    private static bool Contains(Visual root, string text) =>
        root is TextBlock { Text: { } own } && own == text || root.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);
}
