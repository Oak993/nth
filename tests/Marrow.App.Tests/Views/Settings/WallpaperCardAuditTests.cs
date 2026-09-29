using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using System.Linq;

namespace Marrow.App.Tests.Views.Settings;

// Audit-fix tests for DroppyCode/App/Views/Settings/WallpaperCard.swift (B3 52bc7926).
[Collection("Headless")]
public sealed class WallpaperCardAuditTests(HeadlessSession headless)
{
    private static void Frame(WallpaperCardScope scope, double seconds)
    {
        var span = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        scope.Time.Advance(span); scope.Frames.Advance(span); scope.Pump();
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-141")] // WallpaperCard.swift:28-75 the outgoing picture keeps its image while it fades and scales out
    public void The_outgoing_picture_keeps_its_image_while_the_removal_transition_runs() => headless.OnUiThread(() => {
        using var scope = new WallpaperCardScope();
        var button = CardQuery.All<Button>(scope.Card).First(); var content = (Panel)button.Content!; var swap = (Panel)content.Children[0];
        scope.InstallAndSettle(PictureFiles.Png(PictureFiles.Folder(), "a.png"));
        var picture = swap.Children[0];
        var image = picture.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Single();
        Assert.NotNull(image.Source);
        scope.Store.Remove(); scope.Pump(); Frame(scope, .05);
        Assert.Contains(picture, swap.Children);
        Assert.NotNull(image.Source);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-145")] // WallpaperCard.swift:145-162 a reset present when the row first appears is not inserted with a transition
    public void A_reset_already_true_when_the_softness_row_appears_is_not_animated_in() => headless.OnUiThread(() => {
        using var scope = new WallpaperCardScope();
        scope.Store.Softness = 0.5; scope.Pump();
        scope.Store.Install(from: PictureFiles.Png(PictureFiles.Folder(), "a.png"));
        scope.WaitFor(() => !scope.Store.IsInstalling);
        var reset = Assert.Single(CardQuery.WithTip(scope.Card, "Back to sharp"));
        Assert.Equal(1, reset.Opacity);
    });

}
