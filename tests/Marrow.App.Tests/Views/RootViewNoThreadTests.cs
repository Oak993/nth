using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :424-450 struct NoThreadView, shown by DetailView (:155-165)
// when a project exists and no thread is selected. Records UI-WIN-070 to UI-WIN-072.
[Collection("Headless")]
public sealed class RootViewNoThreadTests(HeadlessSession headless)
{
    private void OnPane(Action<Control, AppModel> check)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            harness.Main.OnMain(() =>
            {
                model.AddProject(new Project("Project", directory).Url);
                var host = RootViewRig.Show(model, new NoThreadView());
                try { check(host, model); }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    // :428 the VStack(spacing: 16): symbol, heading, button, then the chord hint.
    private static StackPanel Stack(Control host) =>
        Assert.IsType<StackPanel>(RootViewRig.Text(host, "Pick a thread or start a new one").Parent);

    private static Button NewThread(Control host) => Assert.Single(RootViewRig.All<Button>(host));

    [Fact]
    [Trait("Req", "UI-WIN-070")] // App/Views/RootView.swift:428-433, :448
    public void The_empty_thread_symbol_and_heading_are_a_centred_16_point_stack_filling_the_pane()
    {
        OnPane((host, _) =>
        {
            var stack = Stack(host);
            var symbol = Assert.IsType<SwiftImage>(stack.Children[0]);
            var heading = RootViewRig.Text(host, "Pick a thread or start a new one");
            Assert.Same(heading, stack.Children[1]);
            Assert.Equal("bubble.left.and.text.bubble.right", symbol.SymbolName); // :429
            Assert.Equal(SwiftFont.System(46, SwiftFont.Weight.Light), SwiftImage.GetFont(symbol)); // :430 .font(.system(size: 46, weight: .light))
            RootViewRig.AssertForeground(symbol, RootViewRig.Secondary); // :431 Chrome.secondaryText
            RootViewRig.AssertStyle(heading, 17, FontWeight.Medium); // :433 .font(.system(.title2, weight: .medium)), ChatZoom.swift:49
            Assert.False(heading.IsSet(TextBlock.ForegroundProperty)); // default colour
            Assert.Equal(16, heading.Bounds.Top - symbol.Bounds.Bottom, 0.5); // :428 VStack(spacing: 16)
            Assert.Same(NewThread(host), stack.Children[2]);
            Assert.Equal(16, NewThread(host).Bounds.Top - heading.Bounds.Bottom, 0.5);

            // :448 .frame(maxWidth: .infinity, maxHeight: .infinity) around a centred stack
            var stackBox = RootViewRig.In(stack, host);
            Assert.Equal(host.Bounds.Width / 2, stackBox.Center.X, 0.5);
            Assert.Equal(host.Bounds.Height / 2, stackBox.Center.Y, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-071")] // App/Views/RootView.swift:434-440
    public void New_thread_is_a_prominent_accent_tinted_glass_button_with_its_pencil_symbol_that_creates_a_thread()
    {
        OnPane((host, model) =>
        {
            var button = NewThread(host);
            Assert.Equal("New thread", RootViewRig.Text(button, "New thread").Text); // :437
            Assert.Equal("square.and.pencil", Assert.Single(RootViewRig.All<SwiftImage>(button)).SymbolName);
            Assert.True(button.IsEnabled); // no disabled state
            var glass = Assert.Single(RootViewRig.All<Border>(button), border => SwiftGlass.GetEffect(border) is not null);
            var effect = SwiftGlass.GetEffect(glass)!;
            Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); // :439 .buttonStyle(.glassProminent)
            Assert.NotNull(effect.TintColor);
            Assert.Equal(Chrome.Accent.Resolve(button.ActualThemeVariant), effect.TintColor.Resolve(button.ActualThemeVariant));

            Assert.Empty(model.Threads);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // :435 model.newThread()
            var thread = Assert.Single(model.Threads);
            Assert.Equal(thread.Id, model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-072")] // App/Views/RootView.swift:441-446; DroppyCode/Core/Models/Shortcuts.swift:32-34 and OD-0008 B7 (Windows notation)
    public void The_chord_hint_is_the_last_subheadline_secondary_line_16_points_under_the_button()
    {
        OnPane((host, _) =>
        {
            var stack = Stack(host);
            var hint = RootViewRig.Text(host, "Ctrl+N"); // the New thread default, ⌘N upstream
            Assert.Same(hint, stack.Children[^1]); // last element of the stack
            Assert.Equal(4, stack.Children.Count);
            RootViewRig.AssertStyle(hint, 11, FontWeight.Normal); // :444 .font(.system(.subheadline)), ChatZoom.swift:53
            RootViewRig.AssertForeground(hint, RootViewRig.Secondary); // :445
            Assert.Equal(16, hint.Bounds.Top - NewThread(host).Bounds.Bottom, 0.5); // :428 VStack(spacing: 16)
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-072")] // App/Views/RootView.swift:441-446 `if let chord = ShortcutStore.label(for: .newThread)`: absent when cleared, current after a remap
    public void The_chord_hint_follows_a_remap_and_is_absent_while_the_chord_is_cleared()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var store = ShortcutStore.Shared.Override(harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!));
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var model = harness.MakeModel();
            var directory = harness.NewFolder("project");
            Control host = null!;
            StackPanel stack = null!;
            harness.Main.OnMain(() =>
            {
                model.AddProject(new Project("Project", directory).Url);
                host = RootViewRig.Show(model, new NoThreadView());
                stack = Stack(host);
                Assert.Equal(["Ctrl+N"], HintLines(stack));
                ShortcutStore.Shared.Current.Set(new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Shift), AppShortcut.NewThread);
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                RootViewRig.Layout(host);
                Assert.Equal(["Ctrl+Shift+Y"], HintLines(stack)); // the remapped chord at once, the stale one gone
                ShortcutStore.Shared.Current.Set(null, AppShortcut.NewThread);
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                RootViewRig.Layout(host);
                Assert.Empty(HintLines(stack)); // absent entirely, not an empty line
                Assert.Equal(3, stack.Children.Count);
                ShortcutStore.Shared.Current.Set(new KeyChord(0x15, ChordModifiers.Control), AppShortcut.NewThread);
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                RootViewRig.Layout(host);
                Assert.Equal(["Ctrl+Y"], HintLines(stack));
                Assert.IsType<TextBlock>(stack.Children[^1]);
                RootViewRig.Close(host);
            });
        });
    }

    // Every line of the stack after the heading's own: the button's label lives inside the button, so only the hint is a direct TextBlock child past the heading.
    private static string[] HintLines(StackPanel stack) =>
        stack.Children.OfType<TextBlock>().Where(block => block.Text != "Pick a thread or start a new one").Select(block => block.Text ?? "").ToArray();
}
