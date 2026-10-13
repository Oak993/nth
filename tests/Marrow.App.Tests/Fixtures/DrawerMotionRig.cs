using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Usage;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// ChatView.swift:96,156-241,275-290: the drawer, composer and usage panel on one injected frame clock.
// Used only inside ChatHeadlessFixtures.Run: every provider and terminal shell is an offline stand-in.
internal sealed class DrawerMotionRig : IDisposable
{
    private readonly AppModelHarness harness = new();
    private readonly IDisposable actor;
    private readonly IDisposable cursor;
    private readonly IDisposable frameScope;
    private readonly ManualFrameSource frames = new();
    internal AppModel Model { get; }
    internal ThreadRuntime Runtime { get; }
    internal ChatView View { get; }
    internal Avalonia.Controls.Window Window { get; }
    internal ComposerArea Composer { get; }
    internal UsageFloatingPanel Panel { get; }
    internal Grid Pane { get; }
    internal double Resting { get; }
    internal double Gap { get; }

    internal DrawerMotionRig(PanelDockCorner dock)
    {
        harness.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
        actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        cursor = Marrow.UI.Chrome.DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        frameScope = FrameClock.Source.Override(frames);
        harness.WriteLibrary([new Project("Project", harness.NewFolder("project"))], []);
        Model = harness.MakeModel();
        harness.PrepareBootstrap(Model);
        Runtime = harness.Main.OnMain(() =>
        {
            Model.Settings.HydraEnabled = false;
            Model.Settings.UsagePanelDock = dock;
            Model.Settings.ShowsUsagePanel = true;
            return Model.Runtime(Model.NewThread(Model.Projects[0])!.Id);
        });
        View = harness.Main.OnMain(() => new ChatView(Runtime));
        Window = new Avalonia.Controls.Window { Content = View, Width = 1400, Height = 900 };
        AppEnvironment.SetAppModel(Window, Model);
        AppEnvironment.SetWindowLiveResize(Window, new WindowLiveResize(Window));
        ChatHeadlessFixtures.Track(Window);
        Window.Show();
        Drain();
        Frame(1);
        Composer = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(View));
        Panel = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(View));
        Pane = (Grid)typeof(ChatView).GetField("pane", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(View)!;
        Resting = ComposerBottom;
        Gap = Resting - PanelBottom;
    }

    internal TerminalPanel Terminal => Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(View));
    internal double ComposerBottom => Composer.TranslatePoint(new Point(0, Composer.Bounds.Height), View)!.Value.Y;
    internal double PanelBottom => Panel.TranslatePoint(new Point(0, Panel.Bounds.Height), View)!.Value.Y;
    internal double TerminalTop => Terminal.TranslatePoint(default, View)!.Value.Y;
    internal double Margin => Pane.Margin.Bottom;
    internal double RawPanelBottom
    {
        get
        {
            var placed = LogicalQuery.SelfAndDescendants<PlacedPanel>(View)
                .Single(p => LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(p).Contains(Panel));
            var placement = (PanelPlacementView)placed.Child!;
            return placement.RenderTransform!.Value.M32 + placement.Height;
        }
    }

    internal void Toggle(bool visible)
    {
        harness.Main.OnMain(() => Runtime.IsTerminalVisible = visible);
        Drain();
    }

    internal void Frame(double seconds)
    {
        harness.Main.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.Zero);
        Drain();
    }

    internal void AdvanceWithoutFrame(double seconds) => harness.Main.Advance(TimeSpan.FromSeconds(seconds));

    internal void Drain()
    {
        harness.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        harness.Main.RunUntilIdle();
    }

    public void Dispose()
    {
        harness.Main.OnMain(() => { Window.Close(); Model.Terminals.TerminateAll(); });
        Drain();
        frameScope.Dispose();
        cursor.Dispose();
        actor.Dispose();
        harness.Dispose();
    }
}
