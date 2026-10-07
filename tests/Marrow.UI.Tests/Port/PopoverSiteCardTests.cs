using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class PopoverSiteCardTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-292")]
    [Trait("Feature", "CZ-7b")]
    public void The_presenter_preserves_a_sites_existing_card_and_appearance_motion() => headless.OnUiThread(() =>
    {
        var card = new PopoverCard();
        var content = new Grid { Children = { card }, RenderTransform = new TranslateTransform() };
        var popup = new Popup { Child = content, PlacementTarget = new Button(), Placement = PlacementMode.AnchorAndGravity, PlacementAnchor = PopupAnchor.TopLeft, PlacementGravity = PopupGravity.BottomRight };
        PopoverHost.SetSiteCard(popup, card);
        PopoverHost.Prepare(popup);
        Assert.Same(content, popup.Child);
        Assert.Same(content, PopoverHost.Content(popup));
        Assert.Equal(default, PopoverHost.Room(popup));
        Assert.Contains(PopoverHost.TippedClass, popup.Classes);
        var transform = content.RenderTransform;
        PopoverAppear.Play(content, popup.Placement);
        Assert.Same(transform, content.RenderTransform);
        Assert.Equal(1, content.Opacity);
        var closed = false;
        PopoverAppear.Dismiss(content, popup.Placement, () => closed = true);
        Assert.True(closed);
        Assert.Same(transform, content.RenderTransform);
    });
}
