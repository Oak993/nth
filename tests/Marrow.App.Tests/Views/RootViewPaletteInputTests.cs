using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views;

// B3 52bc792: RootView.swift:69-76 places the palette above both animated layers and constructs it only
// while presented. AppCommands.swift:40-41 and Shortcuts.swift:228 supply the real opener (ED-050/ED-099:
// Ctrl+K on Windows). CommandPalette.swift:35-41,59-61,100 supplies scrim/Escape dismissal and initial focus.
[Collection("Headless")]
public sealed class RootViewPaletteInputTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Req", "PR-WIN-067")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_production_shortcut_opens_above_a_live_glide_and_input_dismissal_reopens_fresh(bool escape) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var shortcuts = ShortcutStore.Shared.Override((ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var glides = RowGlideAnimator.Shared.Override(animator);
        using var departure = new RenderTargetBitmap(new PixelSize(200, 24));
        using var arrival = new RenderTargetBitmap(new PixelSize(200, 24));
        RootView root = null!;
        launch.Manager.MainContent = () => root = new RootView();
        Avalonia.Controls.Window window = null!;
        try
        {
            launch.Harness.Main.OnMain(() =>
            {
                launch.Manager.ShowMain(); // installs the production command dispatcher, not a test key handler
                window = launch.Manager.MainWindow!;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.False(launch.Manager.Model.IsCommandPalettePresented);
                animator.Launch(Guid.NewGuid(), RowGlideAnimator.Glide.Direction.Settle,
                    new Rect(20, 60, 200, 24), new Rect(20, 260, 200, 24), new Rect(root.Bounds.Size),
                    departure, arrival, 1, 1);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            });
            launch.Harness.Main.AdvanceMilliseconds(100);
            launch.Harness.Main.OnMain(() =>
            {
                frames.Advance(TimeSpan.FromMilliseconds(100));
                var ghost = Assert.Single(LogicalQuery.SelfAndDescendants<RowGlideView>(root));
                var flight = Assert.Single(animator.Glides);
                Assert.False(flight.HasArrived);
                Press(window!, Key.K, PhysicalKey.K, RawInputModifiers.Control);
                Assert.True(launch.Manager.Model.IsCommandPalettePresented);
                var first = Assert.Single(LogicalQuery.SelfAndDescendants<CommandPalette>(root));
                var layers = Assert.IsType<Panel>(root.Child);
                Assert.Equal(4, layers.Children.Count);
                Assert.IsType<GenieLayer>(layers.Children[1]);
                var glideLayer = Assert.IsType<RowGlideLayer>(layers.Children[2]);
                var paletteLayer = Assert.IsType<Panel>(layers.Children[3]);
                Assert.Same(first, Assert.Single(LogicalQuery.SelfAndDescendants<CommandPalette>(paletteLayer)));
                Assert.Same(ghost, Assert.Single(LogicalQuery.SelfAndDescendants<RowGlideView>(glideLayer)));
                Assert.True(paletteLayer.ZIndex >= glideLayer.ZIndex);
                Assert.True(paletteLayer.ZIndex >= layers.Children[1].ZIndex);
                foreach (var layer in new Control[] { layers.Children[1], glideLayer, paletteLayer })
                {
                    Assert.Equal(root.Bounds.Size, layer.Bounds.Size);
                    Assert.Equal(new Point(0, 0), layer.TranslatePoint(default, window!));
                }
                Assert.True(first.QueryField.IsFocused);
                window!.KeyTextInput("old query");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("old query", first.QueryField.Text);
                if (escape) Press(window, Key.Escape, PhysicalKey.Escape);
                else
                {
                    // Left of the centred palette card: the window-wide scrim covers the sidebar too.
                    var point = new Point(24, window.ClientSize.Height * 0.85);
                    var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
                    Assert.Contains(first, hit.GetVisualAncestors());
                    window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                    window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.False(launch.Manager.Model.IsCommandPalettePresented);
                Assert.Empty(LogicalQuery.SelfAndDescendants<CommandPalette>(root));
                Assert.Null(first.GetVisualParent());
                Assert.Same(flight, Assert.Single(animator.Glides));
                Assert.False(flight.HasArrived);
                Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control);
                var reopened = Assert.Single(LogicalQuery.SelfAndDescendants<CommandPalette>(root));
                Assert.NotSame(first, reopened);
                Assert.True(reopened.QueryField.IsFocused);
                Assert.True(string.IsNullOrEmpty(reopened.QueryField.Text));
                Assert.Equal(0, reopened.Selection);
                Assert.Same(ghost, Assert.Single(LogicalQuery.SelfAndDescendants<RowGlideView>(root)));
                Press(window, Key.Escape, PhysicalKey.Escape);
                Assert.Empty(LogicalQuery.SelfAndDescendants<CommandPalette>(root));
            });
        }
        finally
        {
            launch.Harness.Main.OnMain(() =>
            {
                window?.Hide();
                if (window is not null) window.Content = null;
                foreach (var flight in animator.Glides.ToArray()) animator.Finish(flight.Id);
                Dispatcher.UIThread.RunJobs();
            });
        }
    });

    private static void Press(Avalonia.Controls.Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
