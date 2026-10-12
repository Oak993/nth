using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.PcControl.Workspace;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the separate workspace's window (unit P18, ED-NEW-pcws-7; DESIGN.md 2.13). On a fake Remote Desktop
// control and fake cloaking: the window is cloaked before it is ever shown, connects view-only and stays off the taskbar;
// "Watch workspace" uncloaks it on top; Take over hands the workspace the user's mouse and keyboard until Give back; Esc and
// the close button only hide it again, with focus back where the user was; a refused or silent connection says why; the
// controls carry their names and help, and the status line is announced.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcws-7")]
public sealed class WorkspaceViewTests(HeadlessSession headless)
{
    private const string Line = "\"C:\\nth\\nth.exe\" --tactus-workspace-agent nth-workspace-0123456789abcdef0123456789abcdef 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    // Connect runs on the UI thread; the body holds it, so the dispatcher runs until the answer is there.
    private static string? Connect(WorkspaceView view)
    {
        var connecting = view.Connect(Line, CancellationToken.None);
        HeadlessSession.RunDispatcherUntil(connecting, Bound);
        Assert.True(connecting.IsCompletedSuccessfully);
        return connecting.Result;
    }

    private static void End(WorkspaceView view)
    {
        view.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    private static KeyEventArgs Escape() => new() { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, KeyModifiers = KeyModifiers.None };

    [Fact]
    public void It_is_cloaked_before_it_is_ever_shown_connects_view_only_and_names_its_controls() => headless.OnUiThread(() =>
    {
        var rdp = new FakeRdpControl();
        var windowing = new FakeWorkspaceWindowing();
        var view = new WorkspaceView(rdp, windowing, (_, _) => Task.CompletedTask);
        try
        {
            Assert.Null(Connect(view));
            Assert.Equal(new[] { (true, false) }, windowing.Cloaks.Select(cloak => (cloak.On, cloak.WasVisible)));
            Assert.True(view.IsVisible);
            Assert.False(view.Watching);
            Assert.False(view.ShowInTaskbar);
            Assert.Equal(new[] { Line }, rdp.Programs);
            Assert.Equal(new[] { false }, rdp.Input);
            Assert.Equal(WorkspaceView.ViewOnly, view.Status);
            Assert.Equal(WorkspaceView.ViewTitle, AutomationProperties.GetName(view));
            Assert.Equal((WorkspaceView.TakeOverLabel, WorkspaceView.TakeOverHelp), (AutomationProperties.GetName(view.TakeOverButton), AutomationProperties.GetHelpText(view.TakeOverButton)));
            Assert.Equal((WorkspaceView.CloseLabel, WorkspaceView.CloseHelp), (AutomationProperties.GetName(view.CloseButton), AutomationProperties.GetHelpText(view.CloseButton)));
            Assert.Equal(WorkspaceView.GiveBackLabel, AutomationProperties.GetName(view.GiveBackButton));
            var status = view.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == WorkspaceView.ViewOnly);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        }
        finally
        {
            End(view);
        }
        Assert.True(rdp.Disposed);
        Assert.Equal(1, rdp.Disconnects);
    });

    [Fact]
    public void Watch_shows_it_take_over_hands_over_the_input_and_Esc_hides_it_with_focus_back_where_the_user_was() => headless.OnUiThread(() =>
    {
        var rdp = new FakeRdpControl();
        var windowing = new FakeWorkspaceWindowing { Front = 0x4242 };
        var view = new WorkspaceView(rdp, windowing, (_, _) => Task.CompletedTask);
        var taken = new List<bool>();
        view.TakenOver += taken.Add;
        try
        {
            Assert.Null(Connect(view));
            view.TakeOver();
            Assert.Empty(taken);
            view.Watch();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.Watching);
            Assert.True(view.Topmost);
            Assert.Equal((false, true), (windowing.Cloaks[^1].On, windowing.Cloaks[^1].WasVisible));

            view.TakeOverButton.Activate();
            Assert.Equal(new[] { true }, taken);
            Assert.Equal(WorkspaceView.YoursNow, view.Status);
            Assert.Equal(new[] { false, false, true }, rdp.Input);
            Assert.Equal((false, true), (view.TakeOverButton.IsVisible, view.GiveBackButton.IsVisible));
            view.RaiseEvent(Escape());
            Assert.True(view.Watching, "Esc belongs to the workspace while the user has it");

            view.GiveBackButton.Activate();
            Assert.Equal(new[] { true, false }, taken);
            Assert.Equal(WorkspaceView.ViewOnly, view.Status);
            Assert.Equal(new[] { false, false, true, false }, rdp.Input);
            Assert.True(view.TakeOverButton.IsFocused);

            view.RaiseEvent(Escape());
            Assert.False(view.Watching);
            Assert.False(view.Topmost);
            Assert.Equal((true, true), (windowing.Cloaks[^1].On, windowing.Cloaks[^1].WasVisible));
            Assert.Equal(new[] { (nint)0x4242 }, windowing.FocusGiven);
        }
        finally
        {
            End(view);
        }
    });

    [Fact]
    public void Closing_the_window_only_hides_it_and_a_refused_or_silent_connection_says_why() => headless.OnUiThread(() =>
    {
        var rdp = new FakeRdpControl();
        var windowing = new FakeWorkspaceWindowing();
        var view = new WorkspaceView(rdp, windowing, (_, _) => Task.CompletedTask);
        try
        {
            Assert.Null(Connect(view));
            view.Watch();
            Dispatcher.UIThread.RunJobs();
            view.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsVisible);
            Assert.False(view.Watching);
            Assert.False(rdp.Disposed);
        }
        finally
        {
            End(view);
        }
        Assert.True(rdp.Disposed);
        Assert.False(view.IsVisible);

        var refused = new FakeRdpControl { Refusal = "the Remote Desktop control refused the connection (0x80004005)" };
        var first = new WorkspaceView(refused, new FakeWorkspaceWindowing(), (_, _) => Task.CompletedTask);
        try
        {
            Assert.Equal(refused.Refusal, Connect(first));
        }
        finally
        {
            End(first);
        }
        Assert.Equal(0, refused.Disconnects);

        var dropped = new FakeRdpControl();
        dropped.States.Clear();
        dropped.States.AddRange([2, 0]);
        var second = new WorkspaceView(dropped, new FakeWorkspaceWindowing(), (_, _) => Task.CompletedTask);
        try
        {
            Assert.Equal("the Remote Desktop control did not connect to a child session", Connect(second));
        }
        finally
        {
            End(second);
        }

        var silent = new FakeRdpControl();
        silent.States.Clear();
        silent.States.Add(2);
        var polls = 0;
        var third = new WorkspaceView(silent, new FakeWorkspaceWindowing(), (_, _) =>
        {
            polls++;
            return Task.CompletedTask;
        });
        try
        {
            Assert.Equal("the child session did not connect within 30 s", Connect(third));
        }
        finally
        {
            End(third);
        }
        Assert.Equal(WorkspaceView.ConnectTimeoutMs / WorkspaceView.PollMs, polls);
        Assert.Equal(1, silent.Disconnects);
    });
}
