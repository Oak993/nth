using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Web;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-1")]
public sealed class VisusFindCardTests(HeadlessSession headless)
{
    [Fact]
    public void An_untipped_card_keeps_equal_shadow_room_when_the_host_changes_placement() => headless.OnUiThread(() =>
    {
        var card = new PopoverCard { ShowTip = false, ShadowRoom = true, Child = new Border { Width = 328, Height = 36 } };
        var room = card.Margin;
        Assert.Equal(room.Left, room.Top);
        foreach (var edge in Enum.GetValues<PopoverTipEdge>()) { card.TipEdge = edge; Assert.Equal(room, card.Margin); }
    });

    [Fact]
    public void The_find_card_has_no_tip_and_keeps_the_canvas_rectangle() => headless.OnUiThread(() =>
    {
        var card = new PopoverCard { BodyCornerRadius = 14, Child = new Border { Width = 328, Height = 36 } };
        var property = typeof(PopoverCard).GetProperty("ShowTip");
        Assert.NotNull(property);
        property.SetValue(card, false);
        card.Measure(Size.Infinity);
        card.Arrange(new Rect(card.DesiredSize));
        Assert.Equal(330, card.Bounds.Width);
        Assert.Equal(38, card.Bounds.Height);
        Assert.Equal(new Rect(1, 1, 328, 36), card.Child.Bounds);
        Assert.True(card.HitTest(new Point(165, 19)));
        Assert.False(card.HitTest(new Point(0, 0)));
        var glass = card.GlassShape(card);
        Assert.NotNull(glass);
        Assert.Equal(new Rect(0, 0, 330, 38), glass.Value.Body);
        Assert.Equal(14, glass.Value.Radius);
        Assert.Equal(0, glass.Value.TipSide);
        Assert.Equal(0, glass.Value.TipRadius);
    });
}
