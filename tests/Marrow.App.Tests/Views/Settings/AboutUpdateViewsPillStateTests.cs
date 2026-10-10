using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :581-583 (isReady), :623-636 (the slider
// face and its chase), :638-704 (the button face), :687-690 (chaseResponse), :836-844 (the row's detail).
[Collection("Headless")]
public sealed class AboutUpdateViewsPillStateTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Content = content, Width = 600, Height = 300, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static double Field(object target, string name) => (double)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    // PR-UPDATE-019 keeps a stored release when a later check fails, so lastError is set while update and updateAvailable
    // stay. :581-583 isReady has no error term, so the pill stays the install button; :839 puts the error ahead of the
    // "ready to install" sentence in the row (:840 comes after it).
    [Fact]
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:581-583,617-621: the stored newer release wins over the failed standing
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:581-583: ready needs only updateAvailable, a release and no install progress
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:573-583,678-682: ready stays the button with its tooltip and label
    public void A_waiting_release_stays_the_install_button_after_a_later_check_failed_and_the_row_leads_with_the_error() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        Set(checker, nameof(UpdateChecker.LastError), "GitHub answered with HTTP 503.");
        Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now.AddHours(-2));
        Assert.True(checker.UpdateAvailable);
        using var shared = UpdateChecker.OverrideShared(checker);
        var pill = new AboutVersionPill(); var row = new AboutUpdateCheckRow(); var window = Show(new StackPanel { Children = { pill, row } });
        var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
        Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Update & restart"); // not "Check failed"
        Assert.DoesNotContain(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Check failed");
        Assert.Contains(pill.GetVisualDescendants().OfType<SwiftImage>(), i => i.SymbolName == "arrow.down.circle.fill");
        Assert.True(button.IsHitTestVisible); Assert.True(button.Focusable); // :674 allowsHitTesting(isReady)
        Assert.Equal("Update & restart", AutomationProperties.GetName(button)); // :702
        Assert.Equal("Installs nth 9.0.0 and relaunches", ToolTip.GetTip(Assert.Single(pill.GetVisualDescendants().OfType<Control>(), c => ToolTip.GetTip(c) is not null))); // :679, not the error
        var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType()); Assert.IsAssignableFrom<IInvokeProvider>(peer); // :682 isButton
        var texts = row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        var stamp = "Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + ".";
        Assert.Contains("GitHub answered with HTTP 503. " + stamp, texts); // :839 the error first
        Assert.DoesNotContain(texts, t => t!.Contains("ready to install", StringComparison.Ordinal)); // :840 never reached
        window.Close();
    });

    // The install face is the nth capsule for every phase (motion pack step 10): 168 x 26, in the pill's height, with the install's fraction as its value.
    [Theory]
    [Trait("Req", "PR-UPDATE-074")] // App/Views/Settings/AboutUpdateViews.swift:625-636: the slider shows for every phase
    [InlineData("preparing", false)] [InlineData("downloading", false)] [InlineData("verifying", false)]
    [InlineData("staging", false)] [InlineData("handoff", false)] [InlineData("celebrating", false)]
    [InlineData("downloading", true)] [InlineData("celebrating", true)]
    public void The_pill_hosts_the_install_capsule_for_every_phase_still_under_reduce_motion(string phase, bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var shared = UpdateChecker.OverrideShared(runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")));
        runtime.OnMain(() =>
        {
            switch (phase)
            {
                case "preparing": progress.Begin(); break;
                case "downloading": progress.Begin(); progress.NoteDownloadProgress(0.5); break;
                case "verifying": progress.Begin(); progress.BeginVerification(); break;
                case "staging": progress.Begin(); progress.BeginStaging(); break;
                case "handoff": progress.Begin(); progress.NoteHandoff(); break;
                default: progress.ArmCelebration(); Assert.True(progress.ConsumePendingCelebration()); break;
            }
            Assert.Equal(Enum.Parse<UpdateInstallProgress.Phase>(phase, ignoreCase: true), progress.UpdateInstallProgressPhase);
            var pill = new AboutVersionPill(); // built before it has a parent: the environment it reads is not known yet
            var window = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = pill };
            if (reduced) EnvironmentValues.SetAccessibilityReduceMotion(window, true); // the window's own root, as SystemReduceMotion sets it
            window.Show(); Dispatcher.UIThread.RunJobs();
            var slider = Assert.Single(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>());
            var capsule = Assert.Single(slider.GetVisualDescendants().OfType<Marrow.App.Port.Updates.UpdateInstallCapsule>());
            Assert.Equal((168.0, 26.0), (capsule.Bounds.Width, capsule.Bounds.Height));
            Assert.Equal(progress.Fraction, capsule.Value, 12);
            Assert.Equal(reduced, capsule.Calm);
            window.Close(); progress.CompleteCelebration();
        });
    });

    // Reduce Motion is read live: a setting that turns on (or off) while the capsule shows stills (or brings back) its light without a new capsule.
    [Fact]
    [Trait("Req", "UI-SETTINGS-058")] // App/Views/Settings/AboutUpdateViews.swift:387,408: "Reduce Motion", read live from the environment
    public void The_capsule_follows_Reduce_Motion_while_it_shows() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new UpdateProgressSlider(0.5) { Width = 168 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = slider };
        window.Show(); Dispatcher.UIThread.RunJobs();
        Assert.False(slider.Capsule.Calm);
        EnvironmentValues.SetAccessibilityReduceMotion(window, true); Dispatcher.UIThread.RunJobs();
        Assert.True(slider.Capsule.Calm); // the system setting turned on: the light is gone at once
        EnvironmentValues.SetAccessibilityReduceMotion(window, false); Dispatcher.UIThread.RunJobs();
        Assert.False(slider.Capsule.Calm); // and back when it turns off
        window.Close();
    });
}
