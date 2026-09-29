using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewPart1ReviewTests(HeadlessSession headless)
{
    // STOP App.Views.Chat.ChatView #3
    // Kind: missing dependency
    // Swift: DroppyCode/App/Views/Chat/ChatView.swift:1123 (1.7.0).
    // Records: PR-CHAT-171, UI-CHAT-073.
    // ED-180 explicitly defers the shared GlassEffectContainer compositor pass. Its layout can be
    // checked here; one glass sampling pass cannot be claimed from the current container.
    // Needed: ED-180 compositor implementation; visual parity remains UNVERIFIED under OD-0001.

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:751-768
    public void The_reserve_glides_on_frames_without_rewrapping_and_publishes_its_live_offset()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var content = new Border();
        var slide = new ReserveSlide(new PanelReserve(), Slides: true);
        slide.Body(content);
        slide = slide with { Reserve = new PanelReserve { Leading = 100 } };
        slide.Body(content);
        Assert.Equal(-50, slide.Offset); // :760 ((100-0)-(0-0))/2, :766 offset -= shift.
        Assert.Equal(-50, ChatView.GetReserveSlideOffset(content));
        runtime.AdvanceMilliseconds(50);
        frames.Advance(TimeSpan.Zero);
        Assert.InRange(slide.Offset, -49.999, -0.001); // :768 a continuous glide, not a delayed jump.
        Assert.Equal(slide.Offset, ChatView.GetReserveSlideOffset(content));
        Assert.Equal(new Thickness(100, 0, 0, 0), content.Margin); // :751 one final-width layout.
        runtime.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration));
        frames.Advance(TimeSpan.Zero);
        Assert.Equal(0, slide.Offset);
        Assert.Equal(0, ChatView.GetReserveSlideOffset(content));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:753-768
    public void The_composer_width_glides_while_the_timeline_padding_snaps()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var content = new Border();
        var slide = new ReserveSlide(new PanelReserve(), Slides: true, AnimatesWidth: true);
        slide.Body(content);
        slide = slide with { Reserve = new PanelReserve { Leading = 100, Trailing = 40 } };
        slide.Body(content);
        Assert.Equal(0, slide.Offset); // :761 animatesWidth skips the transform glide.
        Assert.Equal(default, content.Margin); // :754 width starts at its previous room.
        runtime.AdvanceMilliseconds(50);
        frames.Advance(TimeSpan.Zero);
        Assert.InRange(content.Margin.Left, 0.001, 99.999);
        runtime.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration));
        frames.Advance(TimeSpan.Zero);
        Assert.Equal(new Thickness(100, 0, 40, 0), content.Margin);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1128-1131
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1135-1142
    public void Changing_the_sidebar_and_Hydra_settings_updates_the_conditional_slots_live()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            // The two soft-appear buttons leave with a fade (ChatView.swift:1129-1131,1139-1140): time and frames are driven by hand.
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            ChatChromeRow row = null!;
            Avalonia.Controls.Window window = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = false;
                row = new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", project, project.Path, new GitStatusModel());
                window = new Avalonia.Controls.Window { Content = row };
                window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Single(LogicalQuery.SelfAndDescendants<ThreadsButton>(row));
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraButton>(row));
                model.Settings.SidebarFloats = true;
                model.Settings.SidebarOnlyFloats = true;
                model.Settings.HydraEnabled = true;
            });
            harness.Main.RunUntilIdle();
            for (var i = 0; i < 40; i++) { time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); harness.Main.RunUntilIdle(); }
            Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadsButton>(row));
            Assert.Single(LogicalQuery.SelfAndDescendants<HydraButton>(row));
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-175")] // App/Views/Chat/ChatView.swift:1132-1134
    [Trait("Req", "PR-CHAT-183")] // App/Views/Chat/ChatView.swift:1165-1171
    public void Changing_a_shortcut_updates_the_chrome_tooltip_on_the_mounted_row()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            using var store = ShortcutStore.Shared.Override(harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!));
            var model = harness.MakeModel();
            ChatChromeRow row = null!;
            Avalonia.Controls.Window window = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                row = new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", project, project.Path, new GitStatusModel());
                window = new Avalonia.Controls.Window { Content = row };
                window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                ShortcutStore.Shared.Current.Set(null, AppShortcut.NewThread);
                ShortcutStore.Shared.Current.Set(null, AppShortcut.ToggleTerminal);
            });
            harness.Main.RunUntilIdle();
            var tips = LogicalQuery.SelfAndDescendants<Button>(row).Select(ToolTip.GetTip).ToArray();
            Assert.Contains("New thread", tips);
            Assert.Contains("Terminal", tips);
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    }
}
