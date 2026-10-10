using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :206-252,638-703.
// Round 2 (opencode-proofs-aboutupdate): real pointer hover through the headless window's input pipeline and
// ToolTipService, the popover placement, the click closure reading live state, and the ready-face easing curve.
[Collection("Headless")]
public sealed class AboutUpdateViewsProofInputTests(HeadlessSession headless)
{
    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static Avalonia.Controls.Window Show(Control view, double width = 600, double height = 400)
    {
        var window = new Avalonia.Controls.Window { Content = view, Width = width, Height = height, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Point Center(Control control, Avalonia.Controls.Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>
    /// Moves the real pointer from the window's corner to <paramref name="at"/> and runs the dispatcher until Avalonia's
    /// ToolTipService, after its own default show delay (a real DispatcherTimer), sets ToolTip.IsOpen and raises
    /// ToolTipOpening on the control that owns the tooltip under the pointer (itself or the nearest ancestor with a tip).
    /// Returns that control, or null when the service opened nothing within the bound. The service decides from real hit
    /// testing; the test only cancels the opening, because the headless platform has no IPopupImpl or overlay layer to
    /// draw the tooltip popup in (it throws "Unable to create IPopupImpl"), the same limit as STOP #5.
    /// </summary>
    internal static Control? HoverAndReadOpenedTip(Point at, Avalonia.Controls.Window window)
    {
        var opened = new TaskCompletionSource<Control>();
        using var watch = ToolTip.ToolTipOpeningEvent.AddClassHandler(typeof(Control), (sender, e) =>
        {
            var owner = (Control)sender!;
            if (ToolTip.GetIsOpen(owner)) opened.TrySetResult(owner);
            ((CancelRoutedEventArgs)e).Cancel = true;
        }, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        window.MouseMove(new Point(1, 1), RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        window.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        try { HeadlessSession.RunDispatcherUntil(opened.Task, TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { return null; }
        return opened.Task.Result;
    }

    private static int PressAndRelease(Point at, Avalonia.Controls.Window window, Button button)
    {
        var clicks = 0;
        void Count(object? sender, RoutedEventArgs e) => clicks++;
        button.AddHandler(Button.ClickEvent, Count, RoutingStrategies.Bubble, handledEventsToo: true);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsPressed); // no pressed state: the press never reaches the standing button
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        button.RemoveHandler(Button.ClickEvent, Count);
        return clicks;
    }

    private static AppUpdater FakeUpdater(FakeUpdateInstallBoundary boundary, out IDisposable scope)
    {
        var updater = new AppUpdater(boundary);
        var ambient = (Ambient<AppUpdater>)typeof(AppUpdater).GetField("running", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        scope = ambient.Override(updater);
        return updater;
    }

    // (5) Swift :674-680 applies .help AFTER .allowsHitTesting(isReady) and .chromeGlassCapsule(); :676-677 "The seal is
    // not a button, but it is still worth saying what it means rather than showing an empty tooltip." Hovering every
    // standing must therefore open its tooltip, while the press stays inert (:674 allowsHitTesting(false)).
    [Theory]
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:674-681,693-699 ("The user hovers")
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:674-681,693-699
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:674 (not hit-testable, clicking does nothing)
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:674-681 ("Hover for the state's tooltip")
    [Trait("Req", "UI-UPDATE-002")] // App/Views/Settings/AboutUpdateViews.swift:674-681,695 ("Hover shows the tooltip; clicking does nothing")
    [Trait("Req", "UI-UPDATE-003")] // App/Views/Settings/AboutUpdateViews.swift:674-681,696 ("Hover shows the tooltip")
    [Trait("Req", "UI-UPDATE-004")] // App/Views/Settings/AboutUpdateViews.swift:674-681,697 ("Hover shows the tooltip")
    [InlineData(true, null, "nth 1.7.0 is the latest version")] // :695, OD-0012 F1
    [InlineData(false, "GitHub answered with HTTP 503.", "GitHub answered with HTTP 503.")] // :696
    [InlineData(false, null, "nth has not asked GitHub for a release yet")] // :697, OD-0012 F1
    public void Hovering_a_standing_pill_opens_its_tooltip_while_a_press_does_nothing(bool checkedBefore, string? error, string help) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "9.0.0" }; var updater = FakeUpdater(boundary, out var updaterScope); using var _ = updaterScope;
        runtime.OnMain(() =>
        {
            var checker = new UpdateChecker(AppInfo.Current, [], "");
            if (checkedBefore) Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
            Set(checker, nameof(UpdateChecker.LastError), error);
            using var shared = UpdateChecker.OverrideShared(checker);
            var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
            var pill = new AboutVersionPill(); var window = Show(pill);
            var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            var at = Center(pill, window);
            var host = HoverAndReadOpenedTip(at, window);
            Assert.NotNull(host); // the real ToolTipService opened a tooltip under the pointer
            Assert.True(ReferenceEquals(host, pill) || pill.IsVisualAncestorOf(host!)); // ...on this pill
            Assert.Equal(help, ToolTip.GetTip(host!));
            Assert.Equal(0, PressAndRelease(at, window, button)); // :674, :638-640 nothing runs
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            Assert.Null(Field(updater, "installTask")); Assert.Empty(boundary.Calls);
            window.Close();
        });
    });

    // The ready pill: the same tooltip holder answers a hover over the hit-testable button (:678-679).
    [Fact]
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:678-679
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:678-679
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:678-679
    public void Hovering_the_ready_pill_opens_the_install_tooltip() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            var checker = new UpdateChecker(AppInfo.Current, [], ""); Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
            using var shared = UpdateChecker.OverrideShared(checker);
            using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
            var pill = new AboutVersionPill(); var window = Show(pill);
            var host = HoverAndReadOpenedTip(Center(pill, window), window);
            Assert.NotNull(host); Assert.True(ReferenceEquals(host, pill) || pill.IsVisualAncestorOf(host!));
            Assert.Equal("Installs nth 9.0.0 and relaunches", ToolTip.GetTip(host!)); // :679, OD-0012 F1, no full stop
            Assert.True(Assert.Single(pill.GetVisualDescendants().OfType<Button>()).IsPointerOver); // the button itself is under the pointer
            window.Close();
        });
    });

    // (6) Swift :237-239: the absent card is .disabled and still carries .help("Nothing here"); UI-UPDATE-012/013/014
    // state it as the absent state's tooltip. A present card's tooltip names its section (:239).
    [Theory]
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:237-239
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:237-239
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:237-239
    [Trait("Req", "UI-SETTINGS-057")] // App/Views/Settings/AboutUpdateViews.swift:237-239
    [InlineData(0)] [InlineData(1)]
    public void Hovering_each_release_card_opens_its_tooltip_even_while_the_card_is_disabled(int count) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        runtime.OnMain(() =>
        {
            var sections = ReleaseNoteCategoryExtensions.AllCases.Select(c => new UpdateReleaseNotesDigest.Section
                { Category = c, Bullets = new ValueList<string>(Enumerable.Repeat("Change", count)), RollupCount = 0 }).ToArray();
            var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(count == 0 ? [] : sections) }, "9.0.0");
            var window = Show(cards); var buttons = cards.GetVisualDescendants().OfType<Button>().ToArray(); Assert.Equal(3, buttons.Length);
            var expected = count == 0
                ? new[] { "Nothing here", "Nothing here", "Nothing here" }
                : new[] { "Every change in new features", "Every change in bug fixes", "Every change in refinements" };
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(count != 0, buttons[i].IsEnabled);
                var host = HoverAndReadOpenedTip(Center(buttons[i], window), window);
                Assert.NotNull(host); Assert.True(ReferenceEquals(host, buttons[i]) || buttons[i].IsVisualAncestorOf(host!));
                Assert.Equal(expected[i], ToolTip.GetTip(host!));
                window.MouseMove(new Point(1, window.Height - 1), RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); // leave before the next card
            }
            window.Close();
        });
    });

    // Swift :241-244 .popover(isPresented:, arrowEdge: .top): the arrow on the card's top edge, so the popover opens above
    // the card (UI-SETTINGS-057 "popover above the card"), the same mapping as MessagePopoverMenu.cs:35 and
    // ShortcutsSettingsPage.cs:375 (arrowEdge .top -> PlacementMode.Top).
    [Fact]
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:241-244 (arrowEdge .top)
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:241-244
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:241-244
    [Trait("Req", "UI-SETTINGS-057")] // App/Views/Settings/AboutUpdateViews.swift:241-244 ("above the card")
    [Trait("Req", "UI-UPDATE-015")] // App/Views/Settings/AboutUpdateViews.swift:241-244: arrowEdge .top, light dismissal, the same card again closes it
    public void Each_card_presents_its_own_popover_above_itself() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var recorder = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(recorder);
        runtime.OnMain(() =>
        {
            var sections = ReleaseNoteCategoryExtensions.AllCases.Select(c => new UpdateReleaseNotesDigest.Section
                { Category = c, Bullets = new ValueList<string>(["Change"]), RollupCount = 0 }).ToArray();
            var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(sections) }, "9.0.0");
            var window = Show(cards); var buttons = cards.GetVisualDescendants().OfType<Button>().ToArray();
            for (var i = 0; i < 3; i++)
            {
                var at = Center(buttons[i], window);
                window.MouseMove(at, RawInputModifiers.None); window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                var (popup, presented) = recorder.Calls.Last();
                Assert.True(presented); Assert.Same(buttons[i], popup.PlacementTarget);
                Assert.Equal(PlacementMode.Top, popup.Placement); // :244 arrowEdge: .top
                Assert.True(popup.IsLightDismissEnabled);
                window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs(); Assert.False(recorder.Calls.Last().Presented); // :211 the second tap closes it
            }
            window.Close();
        });
    });

    // (7) Swift :639 `guard let update = checker.update, isReady else { return }` reads the checker and the progress when
    // the click lands, not the values the face was built from. A routed click on a face built while ready must do
    // nothing once the release is gone or the progress UI is showing.
    [Theory]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:581-583,638-640
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:581-583,638-640
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:581-583,638-640 ("Click while ready to install")
    [InlineData("release-withdrawn")] [InlineData("install-showing")] [InlineData("release-not-newer")]
    public void A_click_reads_the_live_checker_and_progress_not_the_face_it_was_built_from(string change) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        runtime.OnMain(() => Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release(url: "http://127.0.0.1:1/never")));
        using var shared = UpdateChecker.OverrideShared(checker);
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "9.0.0" }; var updater = FakeUpdater(boundary, out var updaterScope); using var _ = updaterScope;
        runtime.OnMain(() =>
        {
            var pill = new AboutVersionPill(); var window = Show(pill);
            var built = Assert.Single(pill.GetVisualDescendants().OfType<Button>()); Assert.True(built.IsHitTestVisible); // built while ready
            if (change == "release-withdrawn") Set(checker, nameof(UpdateChecker.Update), null);
            else if (change == "release-not-newer") Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release(url: "http://127.0.0.1:1/never") with { Version = "1.7.0" }); // UpdateChecker.swift:93-96 updateAvailable false
            else progress.Begin();
            Dispatcher.UIThread.RunJobs();
            var phase = progress.UpdateInstallProgressPhase;
            built.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(Field(updater, "installTask")); // AppUpdater.install was not entered (:639 guard failed)
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(phase, progress.UpdateInstallProgressPhase); Assert.Empty(boundary.Calls);
            window.Close();
        });
    });

    // Swift :639-640 installs `checker.update` as it is at click time: a face built for 9.0.0 installs the 9.1.0 the
    // checker holds when the click lands.
    [Fact]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:638-640; App/AppUpdater.swift:261-283
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:638-640
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:638-640
    public void A_click_installs_the_release_the_checker_holds_when_the_click_lands() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => CannedResponse.Json("installer bytes"))).GetAwaiter().GetResult();
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        runtime.OnMain(() => Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release(url: server.Url("/old"))));
        using var shared = UpdateChecker.OverrideShared(checker);
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "9.1.0" }; var updater = FakeUpdater(boundary, out var updaterScope); using var _ = updaterScope;
        var finished = new TaskCompletionSource<bool>();
        using var observe = new Tracked<bool>(() => progress.UpdateInstallProgressPhase == UpdateInstallProgress.Phase.Handoff, done => { if (done) finished.TrySetResult(true); });
        Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            var pill = new AboutVersionPill(); window = Show(pill);
            var built = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release(url: server.Url("/new")) with { Version = "9.1.0" });
            Dispatcher.UIThread.RunJobs();
            built.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        HeadlessSession.RunDispatcherUntil(finished.Task, TimeSpan.FromSeconds(20));
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("/new", Assert.Single(server.Requests).Target);
            Assert.Single(boundary.Calls, call => call.StartsWith("Stage(", StringComparison.Ordinal) && call.EndsWith(", 9.1.0)", StringComparison.Ordinal));
            window.Close();
        });
    });

    // Swift :581-583 isReady needs checker.updateAvailable as well as a release object; UpdateChecker.swift:93-96 makes
    // updateAvailable false for a stored release that is not newer than the running version (after an install it is the
    // running version). The pill then shows its standing, not the install button.
    [Fact]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:581-583 ("only when a newer release is stored")
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:581-583 ("an update is available")
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:581-583
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:678-702
    public void A_stored_release_that_is_not_newer_leaves_the_pill_a_standing() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            var checker = new UpdateChecker(AppInfo.Current, [], ""); Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
            Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release() with { Version = "1.7.0" });
            Assert.False(checker.UpdateAvailable);
            using var shared = UpdateChecker.OverrideShared(checker);
            using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
            var pill = new AboutVersionPill(); var window = Show(pill);
            var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            Assert.Equal("Up to date", Assert.Single(pill.GetVisualDescendants().OfType<TextBlock>()).Text); // :602
            Assert.False(button.IsHitTestVisible); // :674
            Assert.Equal("nth 1.7.0, up to date", AutomationProperties.GetName(button)); // :702, OD-0012 F1
            Assert.Equal("nth 1.7.0 is the latest version", ToolTip.GetTip(Assert.Single(pill.GetVisualDescendants().OfType<Control>(), c => ToolTip.GetTip(c) is not null))); // :695
            window.Close();
        });
    });

    // Swift :222 the count line is countSummary(section.changeCount), and :76 changeCount = bullets.count + rollupCount:
    // a section with one bullet and an "... 3 extra ..." rollup stands for four changes.
    [Fact]
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:44-50,76,222 ("<n> additions")
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:47,76,222 ("<n> fixed")
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:48,76,222 ("<n> improved")
    [Trait("Req", "UI-SETTINGS-057")] // App/Views/Settings/AboutUpdateViews.swift:76,222
    public void The_count_line_counts_a_rollup_as_the_changes_it_stands_for() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        runtime.OnMain(() =>
        {
            var sections = ReleaseNoteCategoryExtensions.AllCases.Select(c => new UpdateReleaseNotesDigest.Section
                { Category = c, Bullets = new ValueList<string>(["Change"]), Rollup = "Fixed 3 extra issues", RollupCount = 3 }).ToArray();
            var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(sections) }, "9.0.0");
            var window = Show(cards);
            var counts = cards.GetVisualDescendants().OfType<Button>().Select(b => b.GetVisualDescendants().OfType<TextBlock>().ToArray()[1].Text).ToArray();
            Assert.Equal(new[] { "4 additions", "4 fixed", "4 improved" }, counts);
            window.Close();
        });
    });

    // Swift :728-733: the task consumes the celebration and starts the sweep, and only then awaits the check. With a feed
    // whose answer is still outstanding, the head is already mounted at 0.86 and sweeps to 100% at 550 ms while the check
    // is in flight; the check then answers normally.
    [Fact]
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:728-733,800-802
    [Trait("Req", "PR-UPDATE-060")] // App/Views/Settings/AboutUpdateViews.swift:728-733; App/AppUpdater.swift:158-166
    public void The_sweep_starts_before_the_open_time_check_answers() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
        var answer = new TaskCompletionSource<CannedResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = System.Threading.Tasks.Task.Run(() => new LoopbackFeedServer(_ => answer.Task)).GetAwaiter().GetResult();
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], server.Url("/releases"))); using var shared = UpdateChecker.OverrideShared(checker);
        var pill = new AboutVersionPill(); var page = new AboutSoftwareUpdateSection(); Avalonia.Controls.Window window = null!;
        runtime.OnMain(() => { progress.ArmCelebration(); window = Show(new StackPanel { Children = { pill, page } }); });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(checker.IsChecking); // the check has started and not answered
            Assert.False(progress.HasPendingCelebration); Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.86, Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()).Fraction);
        });
        runtime.AdvanceMilliseconds(550);
        runtime.OnMain(() => { Assert.True(checker.IsChecking); Assert.Equal(1, progress.Fraction); });
        answer.SetResult(CannedResponse.Json("[]"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (runtime.OnMain(() => checker.IsChecking) && stopwatch.Elapsed < TimeSpan.FromSeconds(20))
        {
            System.Threading.Thread.Sleep(10); runtime.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
        }
        runtime.OnMain(() => { Assert.False(checker.IsChecking); Assert.NotNull(checker.LastCheckedAt); });
        runtime.AdvanceMilliseconds(3000);
        runtime.OnMain(() => { Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); window.Close(); });
    });

    // Round 3: Swift :551-571 fixes only the identity capsule's height (.frame(height: Chrome.capsuleContentHeight) plus
    // .padding(.vertical, Chrome.capsuleVerticalPadding)); unlike AboutVersionPill (:635 fixedSize) there is no
    // .fixedSize modifier here at all, so a narrower parent compresses the width and the :562 lineLimit(1) version
    // text truncates its tail instead of the capsule holding its natural width.
    [Fact]
    [Trait("Req", "UI-SETTINGS-055")] // App/Views/Settings/AboutUpdateViews.swift:559-567 (content height 28, 2 above and below)
    [Trait("Req", "UI-UPDATE-001")] // App/Views/Settings/AboutUpdateViews.swift:559-567
    public void The_identity_capsule_fixes_only_its_height_and_truncates_its_version_when_compressed() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            var pill = new AboutIdentityPill();
            var host = new Border
            {
                Width = 70, Height = 200, Child = pill,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            var window = Show(host);
            Assert.Equal(32, pill.Bounds.Height); // :566-567 28 + 2 + 2, not the host's 200 -- height stays fixed
            Assert.Equal(70, pill.Bounds.Width); // compressed to the offered width, not the natural width
            var text = Assert.Single(pill.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal("v1.7.0", text.Text);
            Assert.True(Assert.Single(text.TextLayout.TextLines).HasCollapsed); // :562 one line, tail truncated
            window.Close();
        });
    });

    // ED-092: SwiftUI .easeInOut is the cubic Bezier (0.42, 0, 0.58, 1). Its progress at a quarter and three quarters of
    // the duration (0.129162 and 0.870838, solved from those control points) differs from linear (0.25/0.75), easeIn
    // (0.093465/0.621862) and easeOut (0.378138/0.906535), so these samples pin the curve, unlike the midpoint.
    private const double QuarterProgress = 0.129162;
    private const double ThreeQuarterProgress = 0.870838;

    private static void AssertMix(SwiftColor from, SwiftColor to, double progress, IBrush? brush, Avalonia.Styling.ThemeVariant theme)
    {
        var a = from.Resolve(theme); var b = to.Resolve(theme);
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        static void Channel(byte x, byte y, double p, byte got) => Assert.InRange(got, (int)Math.Round(x + (y - x) * p) - 1, (int)Math.Round(x + (y - x) * p) + 1);
        Channel(a.A, b.A, progress, actual.A); Channel(a.R, b.R, progress, actual.R); Channel(a.G, b.G, progress, actual.G); Channel(a.B, b.B, progress, actual.B);
    }

    // Swift :643-671: while ready the symbol and the title take Chrome.blue and the capsule a Chrome.blue.opacity(0.2) wash;
    // otherwise the standing tint, the primary title and no wash. :671 animates every one of these on an isReady change
    // with .easeInOut(duration: 0.18), in both directions.
    [Fact]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:643-671
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:643-671
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:643-671
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:671: the ready/standing change eases 0.18 s in both directions
    public void The_face_change_eases_in_and_out_over_0_18_seconds_in_both_directions_for_wash_symbol_and_title() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        using var shared = UpdateChecker.OverrideShared(checker);
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        AboutVersionPill pill = null!; Avalonia.Controls.Window window = null!;
        var blue = new SwiftColor(0.040, 0.478, 1.000); // Chrome.swift:86 fixed blue
        var standingTint = Chrome.PrimaryText.Opacity(0.7); // :612 unchecked -> Chrome.secondaryText (Chrome.swift:79)
        void Advance(int ms) { runtime.Time.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
        (Border Face, SwiftImage Icon, TextBlock Title) Parts()
        {
            // The face that is leaving crossfades out under the one on show (OD-0076, ED-261 T2); the live face is the last.
            var face = pill.GetVisualDescendants().OfType<Border>().Last(b => b.Child is StackPanel);
            var row = Assert.IsType<StackPanel>(face.Child);
            return (face, Assert.IsType<SwiftImage>(row.Children[0]), Assert.IsType<TextBlock>(row.Children[1]));
        }
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); window = Show(pill, 300, 100);
            Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var (face, icon, title) = Parts(); var theme = face.ActualThemeVariant;
            Assert.Equal("Update & restart", title.Text);
            Advance(45); // a quarter of 0.18 s
            AssertMix(blue.Opacity(0), blue.Opacity(0.2), QuarterProgress, face.Background, theme);
            AssertMix(standingTint, blue, QuarterProgress, icon.Foreground, theme);
            AssertMix(Chrome.PrimaryText, blue, QuarterProgress, title.Foreground, theme);
            Advance(90); // three quarters
            AssertMix(blue.Opacity(0), blue.Opacity(0.2), ThreeQuarterProgress, face.Background, theme);
            AssertMix(standingTint, blue, ThreeQuarterProgress, icon.Foreground, theme);
            AssertMix(Chrome.PrimaryText, blue, ThreeQuarterProgress, title.Foreground, theme);
            Advance(45); // 0.18 s: settled
            AssertMix(blue.Opacity(0.2), blue.Opacity(0.2), 1, face.Background, theme);
            AssertMix(blue, blue, 1, title.Foreground, theme);
            Set(checker, nameof(UpdateChecker.Update), null); // back to the unchecked standing
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var (face, icon, title) = Parts(); var theme = face.ActualThemeVariant;
            Assert.Equal("Not checked yet", title.Text);
            Advance(45);
            AssertMix(blue.Opacity(0.2), blue.Opacity(0), QuarterProgress, face.Background, theme);
            AssertMix(blue, standingTint, QuarterProgress, icon.Foreground, theme);
            AssertMix(blue, Chrome.PrimaryText, QuarterProgress, title.Foreground, theme);
            Advance(90);
            AssertMix(blue.Opacity(0.2), blue.Opacity(0), ThreeQuarterProgress, face.Background, theme);
            AssertMix(blue, standingTint, ThreeQuarterProgress, icon.Foreground, theme);
            AssertMix(blue, Chrome.PrimaryText, ThreeQuarterProgress, title.Foreground, theme);
            Advance(45);
            AssertMix(standingTint, standingTint, 1, icon.Foreground, theme);
            AssertMix(Chrome.PrimaryText, Chrome.PrimaryText, 1, title.Foreground, theme);
            window.Close();
        });
    });
}
