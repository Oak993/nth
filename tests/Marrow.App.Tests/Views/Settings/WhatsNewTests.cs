using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Port.Updates;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
[Trait("Decision", "ED-360")]
[Trait("Feature", "UE-6")]
public sealed class WhatsNewTests(HeadlessSession headless)
{
    private static AvailableUpdate Release(string version = "1.8.0", string feature = "Added update notes") => new()
    {
        Version = version, Tag = "v" + version,
        Notes = $"## [{version}]\n### New features\n- {feature}\n### Bug fixes\n- Fixed the notes gate\n### Refinements\n- Kept notes after restart\n### Thanks\n- Contributor is not a fourth card\n",
        DownloadURL = SwiftURL.Create("https://example.invalid/package")!,
        PageURL = SwiftURL.Create("https://example.invalid/release")!,
    };

    private static UpdateChecker Inject(TestRuntime runtime, AvailableUpdate update, string running, FakeSelfUpdateEngine? engine = null)
    {
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(update));
        return runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running(running), [], engine is null ? "" : MarrowReleaseFeed.Url, engine));
    }

    private static Avalonia.Controls.Window Show(Control content)
    {
        EnvironmentValues.SetAccessibilityReduceMotion(content, true);
        var window = new Avalonia.Controls.Window { Width = 650, Height = 900, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static void ReadCards(Control host, string version, string feature)
    {
        var cards = Assert.Single(host.GetVisualDescendants().OfType<UpdateReleaseSectionCards>());
        Assert.True(cards.IsEffectivelyVisible);
        Assert.True(cards.Bounds.Width > 0 && cards.Bounds.Height > 0);
        var buttons = cards.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(new[] { "New features", "Bug fixes", "Refinements" }, buttons.Select(AutomationProperties.GetName));
        Assert.All(buttons, button => Assert.True(button.IsEnabled));
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        var expected = new[] { feature, "Fixed the notes gate", "Kept notes after restart" };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var call = presenter.Calls.Last();
            Assert.True(call.Presented);
            // The headless presentation seam records the real popover; its Opened event builds the lazy content.
            var opened = (Delegate)typeof(Avalonia.Controls.Primitives.Popup).GetField("Opened", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(call.Popup)!;
            opened.DynamicInvoke(call.Popup, EventArgs.Empty);
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(call.Popup.Child!).Select(text => text.Text).ToArray();
            Assert.Contains(version, texts);
            Assert.Contains(expected[i], texts);
            Assert.DoesNotContain("Contributor is not a fourth card", texts);
            buttons[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(presenter.Calls.Last().Presented);
        }
    }

    [Fact]
    [Trait("Spec", "UE-T70")]
    public void The_existing_About_offer_shows_that_versions_cards_before_any_install() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var checker = Inject(runtime, Release(), "1.7.0");
        using var shared = UpdateChecker.OverrideShared(checker);
        var installProgress = UpdateInstallProgressFixtures.NewProgress();
        using var progress = UpdateInstallProgress.Running.Override(installProgress);
        using var info = Marrow.Core.Support.AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var page = new AboutSettingsPage();
        // SettingsView.cs:324 adds this accessory to the About page's chrome, outside AboutSettingsPage itself.
        var host = new StackPanel { Children = { new AboutUpdateChromeAccessory(), page } };
        var window = Show(host);
        try
        {
            Assert.True(checker.UpdateAvailable);
            ReadCards(page, "1.8.0", "Added update notes");
            Assert.Contains(host.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Update & restart");
            Assert.Equal(UpdateInstallProgress.Phase.Idle, installProgress.UpdateInstallProgressPhase);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Spec", "UE-T71")]
    public void About_keeps_the_installed_versions_cards_after_restart_and_a_successful_no_update_check() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var checker = Inject(runtime, Release(), "1.8.0");
        var engine = new FakeSelfUpdateEngine();
        checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.8.0"), [], MarrowReleaseFeed.Url, engine));
        runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()); // installed version is latest; this clears the offered release
        Assert.False(checker.UpdateAvailable);
        // Reopening the app must still recover the installed notes after that clear, with no network.
        checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.8.0"), [], ""));
        using var shared = UpdateChecker.OverrideShared(checker);
        using var info = Marrow.Core.Support.AppInfo.Running.Override(ReleaseFeed.Running("1.8.0"));
        var installProgress = UpdateInstallProgressFixtures.NewProgress();
        using var progress = UpdateInstallProgress.Running.Override(installProgress);
        var page = new AboutSettingsPage();
        var window = Show(page);
        try
        {
            ReadCards(page, "1.8.0", "Added update notes");
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Update & restart");
            Assert.Equal(new[] { "IsInstalled", "Check" }, engine.Calls);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "UE-T77")]
    public void Each_offer_uses_its_own_Marrow_item_even_when_the_shared_checker_offers_a_different_release(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var sharedChecker = Inject(runtime, Release(), "1.7.0");
        using var shared = UpdateChecker.OverrideShared(sharedChecker);
        var release = Release("1.9.0", "The actual offered feature");
        var engine = new FakeSelfUpdateEngine { Offer = new SelfUpdateOffer(release.Version, release.Notes, 100) };
        var offeredChecker = Inject(runtime, release, "1.7.0", engine);
        var item = new MarrowUpdateItem(offeredChecker, engine, (_, _) => throw new InvalidOperationException("Restart forbidden"));
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => throw new InvalidOperationException("Restart forbidden")));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var window = Show(offer);
        try
        {
            ReadCards(offer, "1.9.0", "The actual offered feature");
            Assert.Equal("1.9.0", Assert.Single(service.Rows).Latest);
            Assert.Equal("1.8.0", sharedChecker.Update!.Version);
            Assert.DoesNotContain(engine.Calls, call => call.StartsWith("Download", StringComparison.Ordinal));
            // Reconfiguring the live runner removes the offer and its notes, on both surfaces.
            runtime.OnMain(() => service.Configure([], () => throw new InvalidOperationException("Restart forbidden")));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(offer.GetVisualDescendants().OfType<UpdateReleaseSectionCards>());
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "UE-T78")]
    public void An_actual_offer_with_empty_notes_explains_the_missing_notes_before_install(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var engine = new FakeSelfUpdateEngine { Offer = new SelfUpdateOffer("1.8.0", "", 100) };
        var checker = Inject(runtime, Release() with { Notes = "" }, "1.7.0", engine);
        var service = runtime.OnMain(() => new UpdateEverythingService([new MarrowUpdateItem(checker, engine, (_, _) => throw new InvalidOperationException("Restart forbidden"))], () => { }));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var window = Show(offer);
        try
        {
            Assert.Contains(offer.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Release notes are unavailable for nth 1.8.0.");
            Assert.DoesNotContain(engine.Calls, call => call.StartsWith("Download", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "UE-T80")]
    public void Plain_notes_from_the_actual_item_match_About_on_both_offer_surfaces(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        const string notes = "Plain prose summary of the actual offered release.";
        var engine = new FakeSelfUpdateEngine { Offer = new SelfUpdateOffer("1.8.0", notes, 100) };
        var checker = Inject(runtime, Release() with { Notes = notes }, "1.7.0", engine);
        using var shared = UpdateChecker.OverrideShared(checker);
        var service = runtime.OnMain(() => new UpdateEverythingService([new MarrowUpdateItem(checker, engine, (_, _) => throw new InvalidOperationException("Restart forbidden"))], () => { }));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var about = new AboutSoftwareUpdateSection();
        var window = Show(new StackPanel { Children = { offer, about } });
        try
        {
            Assert.Equal(notes, Assert.Single(offer.GetVisualDescendants().OfType<MarkdownView>()).Text);
            Assert.Equal(notes, Assert.Single(about.GetVisualDescendants().OfType<MarkdownView>()).Text);
            Assert.DoesNotContain(engine.Calls, call => call.StartsWith("Download", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "UE-T81")]
    public void A_later_checker_offer_cannot_change_the_displayed_install_or_downloaded_restart_target(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var release = Release();
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(release));
        var engine = new FakeSelfUpdateEngine { Offer = new SelfUpdateOffer("1.8.0", release.Notes, 100) };
        var checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], MarrowReleaseFeed.Url, engine));
        var applied = new List<string>();
        var item = new MarrowUpdateItem(checker, engine, (_, version) => applied.Add(version));
        var service = runtime.OnMain(() => new UpdateEverythingService([item], item.Restart));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var window = Show(offer);
        try
        {
            engine.Offer = new SelfUpdateOffer("1.9.0", Release("1.9.0", "Future feature").Notes, 100);
            runtime.OnMain(() => checker.Check().GetAwaiter().GetResult());
            Dispatcher.UIThread.RunJobs();
            ReadCards(offer, "1.8.0", "Added update notes");
            runtime.OnMain(() => ((IUpdatesRunner)service).UpdateOne("marrow").GetAwaiter().GetResult());
            Assert.Contains("Download(1.8.0)", engine.Calls);
            Assert.DoesNotContain("Download(1.9.0)", engine.Calls);
            runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
            Dispatcher.UIThread.RunJobs();
            ReadCards(offer, "1.8.0", "Added update notes");
            Assert.Equal("1.8.0", Assert.Single(service.Rows).Latest);
            Assert.Equal("1.9.0", checker.Update!.Version);
            Assert.Equal("1.8.0", service.MarrowOffer!.Version);
            item.Restart();
            Assert.Equal(new[] { "1.8.0" }, applied);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Spec", "UE-T82")]
    public void A_noninstalled_or_current_Marrow_item_never_shows_the_shared_checkers_offer(bool sidebar, bool installed) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var sharedChecker = Inject(runtime, Release(), "1.7.0");
        using var shared = UpdateChecker.OverrideShared(sharedChecker);
        var engine = new FakeSelfUpdateEngine { Installed = installed };
        var checker = Inject(runtime, Release(), installed ? "1.8.0" : "1.7.0", engine);
        var service = runtime.OnMain(() => new UpdateEverythingService([new MarrowUpdateItem(checker, engine, (_, _) => throw new InvalidOperationException("Restart forbidden"))], () => { }));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var window = Show(offer);
        try
        {
            Assert.Null(service.MarrowOffer);
            Assert.Empty(offer.GetVisualDescendants().OfType<UpdateReleaseSectionCards>());
            Assert.Empty(offer.GetVisualDescendants().OfType<MarkdownView>());
            Assert.Equal(installed ? UpdateRowState.UpToDate : UpdateRowState.NotInstalled, Assert.Single(service.Rows).State);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "UE-T72")]
    public void The_Updates_page_and_sidebar_offer_show_the_exact_versions_cards_without_downloading(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var checker = Inject(runtime, Release(), "1.7.0");
        using var shared = UpdateChecker.OverrideShared(checker);
        var item = new FakeUpdateItem("marrow", "nth", UpdateItemKind.Marrow, "1.7.0", "1.8.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => throw new InvalidOperationException("Restart forbidden")));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var window = Show(offer);
        try
        {
            ReadCards(offer, "1.8.0", "Added update notes");
            Assert.Contains(offer.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Update everything");
            Assert.Equal(0, item.Updates);
        }
        finally { window.Close(); }
    });

}
