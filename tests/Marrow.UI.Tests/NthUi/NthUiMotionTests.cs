using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4 and 9 (web surfaces W16a; ED-NEW-web-w16a-1): a new node fades in over 180 ms with a 6 DIP rise while its
// container's height springs open; Reduce Motion keeps a short fade with no rise; a show or hide reverses from where it
// is; a chosen pill glides; a swapped panel's height springs; nothing asks for frames once everything rests.
[Collection("Headless")]
public sealed class NthUiMotionTests(HeadlessSession headless)
{
    private static double Rise(NthUiSlot slot) => slot.RenderTransform is TranslateTransform t ? t.Y : 0;

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_new_node_fades_in_over_180_ms_rising_6_dip_while_its_container_springs_open() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("card \"A\"\n  text \"one\"\n");
        rig.Settle();
        var card = rig.View.SlotFor(rig.Node("card").Key)!;
        var before = card.Bounds.Height;
        rig.View.Append("  text \"two\"\n");
        rig.Pump();
        var slot = rig.View.SlotFor(rig.Node("text", 1).Key)!;
        Assert.Equal(0, slot.Opacity, 3);
        Assert.Equal(NthUiTokens.Rise, Rise(slot), 3);
        Assert.Equal(6, NthUiTokens.Rise);
        Assert.Equal(.18, NthUiTokens.FadeSeconds);
        rig.Run(5 * NthUiRig.Frame);
        Assert.InRange(slot.Opacity, .3, .99);
        Assert.InRange(Rise(slot), .01, 5.99);
        Assert.InRange(card.Bounds.Height, before + .5, before + slot.ContentHeight + NthUiTokens.CardGap - .5);
        rig.Run(6 * NthUiRig.Frame);
        Assert.Equal(1, slot.Opacity, 3);
        Assert.Equal(0, Rise(slot), 3);
        rig.Settle();
        Assert.Equal(before + slot.ContentHeight + NthUiTokens.CardGap, card.Bounds.Height, 1);
        Assert.Equal(1, slot.Openness, 6);
        Assert.False(rig.View.IsMoving);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Reduce_motion_keeps_a_short_fade_with_no_rise() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(reduceMotion: true);
        rig.View.Append("text \"calm\"\n");
        rig.Pump();
        var slot = rig.View.SlotFor(rig.Node("text").Key)!;
        Assert.Equal(0, slot.Opacity, 3);
        Assert.Equal(0, Rise(slot), 3);
        rig.Run(4 * NthUiRig.Frame);
        Assert.InRange(slot.Opacity, .2, .99);
        Assert.Equal(0, Rise(slot), 3);
        rig.Run(6 * NthUiRig.Frame);
        Assert.Equal(1, slot.Opacity, 3);
        Assert.Equal(1, slot.Openness, 6);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Showing_and_hiding_reverse_from_where_they_are() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Quiz, culture: NthUiRig.English));
        var callout = rig.View.SlotFor(rig.Node("callout").Key)!;
        Assert.False(callout.IsShown);
        var toggle = rig.ViewOf<NthUiToggleView>().Switch;
        rig.Click(toggle);
        Assert.True(callout.IsShown);
        rig.Run(4 * NthUiRig.Frame);
        var midway = callout.Opacity;
        var open = callout.Openness;
        Assert.InRange(midway, .05, .95);
        rig.Click(toggle);
        Assert.False(callout.IsShown);
        rig.Tick();
        Assert.InRange(callout.Opacity, .01, midway);
        Assert.InRange(callout.Openness, .01, 1);
        Assert.True(callout.Openness <= open + .2);
        rig.Settle();
        Assert.Equal(0, callout.Opacity, 3);
        Assert.Equal(0, callout.Openness, 3);
        Assert.False(callout.Child!.IsVisible);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_chosen_tab_pill_glides_and_the_panel_height_springs_to_the_new_tab() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Tabs, culture: NthUiRig.English));
        rig.Settle();
        var tabs = rig.ViewOf<NthUiTabsView>();
        var from = tabs.Track.Pill;
        var team = (ToggleButton)tabs.Track.Items[1];
        var to = team.Bounds;
        Assert.Equal(tabs.Track.Items[0].Bounds.X, from.X, 1);
        var host = rig.View.GetVisualDescendants().OfType<NthUiSlot>().First(s => s.Key == rig.Node("tabs").Key + "/panel");
        var startHeight = host.Bounds.Height;
        rig.Click(team);
        rig.Run(3 * NthUiRig.Frame);
        Assert.InRange(tabs.Track.Pill.X, from.X + .5, to.X - .5);
        var endHeight = host.ContentHeight;
        if (Math.Abs(endHeight - startHeight) > 2) Assert.InRange(host.Bounds.Height, Math.Min(startHeight, endHeight) + .5, Math.Max(startHeight, endHeight) - .5);
        Assert.InRange(host.Opacity, .01, .99);
        rig.Settle();
        Assert.Equal(to.X, tabs.Track.Pill.X, 1);
        Assert.Equal(to.Width, tabs.Track.Pill.Width, 1);
        Assert.Equal(host.ContentHeight, host.Bounds.Height, 1);
        Assert.Equal(1, host.Opacity, 3);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_switch_knob_slides_to_its_new_side() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Quiz, culture: NthUiRig.English));
        var toggle = rig.ViewOf<NthUiToggleView>().Switch;
        Assert.Equal(0, toggle.Travel);
        rig.Click(toggle);
        rig.Run(3 * NthUiRig.Frame);
        Assert.InRange(toggle.Travel, .05, .95);
        rig.Settle();
        Assert.Equal(1, toggle.Travel, 6);
        Assert.False(rig.View.IsMoving);
    });
}
