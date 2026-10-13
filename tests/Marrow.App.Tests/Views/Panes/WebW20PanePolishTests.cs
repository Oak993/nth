using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Panes;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-3")]
public sealed class WebW20PanePolishTests(HeadlessSession headless)
{
    [Fact]
    public void Every_pane_is_a_named_landmark_and_all_controls_remain_reachable_with_long_RTL_titles() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: PaneWorkspaceState.Default with { TerminalPrefersGrid = true });
        rig.Window.FlowDirection = FlowDirection.RightToLeft;
        foreach (var kind in Enum.GetValues<PaneKind>().Where(kind => kind != PaneKind.Chat))
        {
            rig.Workspace.Open(kind);
            rig.Settle();
            var card = rig.Card(kind);
            Assert.Equal(PaneKinds.AccessibleName(kind), ControlAutomationPeer.CreatePeerForElement(card).GetName());
            Assert.Equal(AutomationLandmarkType.Region, AutomationProperties.GetLandmarkType(card));
            card.Content.Update(string.Concat(Enumerable.Repeat("عنوان طويل Visus ", 30)), null);
            rig.Layout();
            Assert.True(card.Header.CloseButton.IsEffectivelyVisible);
            Assert.True(card.Header.CloseButton.Focusable);
            Assert.True(card.Header.TitleBlock.Bounds.Width >= 80);
            rig.Workspace.Close(kind);
            rig.Settle();
        }
    });

    [Fact]
    public void Pane_and_rail_contrast_changes_restore_the_original_glass_without_moving_the_layout() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var card = rig.Card(PaneKind.Browser);
        var fill = card.Background;
        var shadow = card.BoxShadow;
        var bounds = card.Bounds;
        void Capture(string name)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "w20-evidence");
            Directory.CreateDirectory(folder);
            using var frame = rig.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        Capture("workspace-canvas");
        var glyph = card.GetVisualDescendants().OfType<Marrow.UI.Panes.PaneGlyph>().First();
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette);
            rig.Layout();
            Assert.Equal(palette.Window, ((ISolidColorBrush)card.Background!).Color);
            Assert.Equal(palette.WindowText, ((ISolidColorBrush)glyph.Foreground!).Color);
            Assert.Equal(default, card.BoxShadow);
            Assert.Equal(new Thickness(1), card.BorderThickness);
            Assert.Equal(bounds, card.Bounds);
            if (palette.IsDark)
            {
                var origin = card.TranslatePoint(default, rig.Window)!.Value;
                var x = (int)(origin.X + card.Bounds.Width / 2);
                var levels = FramePixels.Levels(rig.Window, (x, (int)origin.Y - 2), (x, (int)origin.Y + 20));
                Assert.True(levels[0] > .9 && levels[1] < .1,
                    $"the solid card must paint inside its unchanged bounds: origin={origin}, above={levels[0]}, inside={levels[1]}");
            }
            Capture(palette.IsDark ? "workspace-contrast-black" : "workspace-contrast-white");
            rig.Workspace.Collapse(PaneGutterKeys.Chat);
            rig.Settle();
            var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
            Assert.Equal(palette.Window, ((ISolidColorBrush)rail.Background!).Color);
            Assert.Equal(palette.WindowText, ((ISolidColorBrush)rail.BorderBrush!).Color);
            rig.Workspace.Restore(PaneGutterKeys.Chat);
            rig.Settle();
        }
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, null);
        rig.Layout();
        Assert.IsType<LinearGradientBrush>(card.Background);
        Assert.Equal(shadow, card.BoxShadow);
        Assert.Equal(((LinearGradientBrush)fill!).GradientStops.Select(stop => (stop.Offset, stop.Color)), ((LinearGradientBrush)card.Background!).GradientStops.Select(stop => (stop.Offset, stop.Color)));
        Assert.Equal(bounds, card.Bounds);
    });

    [Fact]
    public void User_collapse_and_restore_raise_a_polite_live_announcement_while_saved_layout_loads_stay_quiet() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.SideChat);
        rig.Settle();
        var announcer = rig.Workspace.GetVisualDescendants().OfType<TextBlock>().Single(control => AutomationProperties.GetLiveSetting(control) == AutomationLiveSetting.Polite);
        var names = new List<string>();
        var peer = ControlAutomationPeer.CreatePeerForElement(announcer);
        peer.PropertyChanged += (_, change) => { if (change.Property == AutomationElementIdentifiers.NameProperty) names.Add((string)change.NewValue!); };
        rig.Workspace.Collapse(PaneGutterKeys.Side);
        rig.Settle();
        rig.Workspace.Restore(PaneGutterKeys.Side);
        rig.Settle();
        rig.Workspace.Collapse(PaneGutterKeys.Chat);
        rig.Settle();
        rig.Workspace.Restore(PaneGutterKeys.Chat);
        rig.Settle();
        Assert.Equal(new[] { "Side chat collapsed", "Side chat restored", "Panes collapsed", "Panes restored" }, names);
        rig.Workspace.SetState(rig.Workspace.State.Collapse(PaneGutterKeys.Side, null), animate: false);
        Assert.Equal(4, names.Count);
    });

    [Fact]
    public void Reduce_Motion_keeps_every_pose_unscaled_through_open_expand_redock_collapse_and_narrow_switch() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reduceMotion: true);
        void Frames()
        {
            for (var frame = 0; frame < 20; frame++)
            {
                rig.Frame();
                foreach (var card in rig.Workspace.GetVisualDescendants().OfType<Marrow.App.Views.Panes.PaneCard>())
                {
                    Assert.Equal(1, card.Scale);
                    Assert.Equal(0, card.Slide);
                }
            }
        }
        rig.Workspace.Open(PaneKind.Browser); Frames();
        rig.Workspace.Open(PaneKind.Artifacts); Frames();
        rig.Workspace.ToggleExpanded(PaneKind.Browser); Frames();
        rig.Workspace.RestoreExpanded(); Frames();
        rig.Workspace.MoveEarlier(PaneKind.Artifacts); Frames();
        rig.Workspace.Collapse(PaneGutterKeys.Chat); Frames();
        rig.Workspace.Restore(PaneGutterKeys.Chat); Frames();
        rig.Window.Width = 680; rig.Layout(); Frames();
        rig.Workspace.Select(PaneKind.Artifacts); Frames();
    });
}
