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

    [Fact]
    [Trait("Req", "PR-SETTINGS-142")] // WallpaperCard.swift:95-99 dropDestination(for: URL.self): a non-URL payload never targets the well
    public void A_drag_carrying_no_file_does_not_activate_the_drop_target() => headless.OnUiThread(() => {
        using var scope = new WallpaperCardScope();
        var button = CardQuery.All<Button>(scope.Card).First(); var content = (Panel)button.Content!;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, "selected text"));
        var enter = new DragEventArgs(DragDrop.DragEnterEvent, data, button, new Point(10, 10), KeyModifiers.None);
        button.RaiseEvent(enter); scope.Pump();
        var over = new DragEventArgs(DragDrop.DragOverEvent, data, button, new Point(10, 10), KeyModifiers.None);
        button.RaiseEvent(over); scope.Pump();
        Assert.DoesNotContain(content.Children.OfType<Border>(), b => b.Background is Avalonia.Media.SolidColorBrush brush && brush.Color == Chrome.Accent.Opacity(.18).Resolve(b.ActualThemeVariant));
        Assert.NotEqual(DragDropEffects.Copy, enter.DragEffects);
        Assert.NotEqual(DragDropEffects.Copy, over.DragEffects);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-145")] // WallpaperCard.swift:133-139 the slider binding getter returns the current store value
    public void A_very_small_external_softness_change_is_reflected_by_the_slider() => headless.OnUiThread(() => {
        using var scope = new WallpaperCardScope();
        scope.InstallAndSettle(PictureFiles.Png(PictureFiles.Folder(), "a.png"));
        var slider = CardQuery.All<Slider>(scope.Card).Single();
        scope.Store.Softness = 0.4; scope.Pump();
        scope.Store.Softness = 0.40005; scope.Pump();
        Assert.Equal(0.40005, slider.Value, 8);
    });
}
