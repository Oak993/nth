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

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-ue-6")]
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

    private static UpdateChecker Inject(TestRuntime runtime, AvailableUpdate update, string running)
    {
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(update));
        return runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running(running), [], ""));
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
    [Trait("Spec", "UE-T72")]
    public void The_Updates_page_and_sidebar_offer_show_the_exact_versions_cards_without_downloading(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var checker = Inject(runtime, Release(), "1.7.0");
        using var shared = UpdateChecker.OverrideShared(checker);
        var item = new FakeUpdateItem("marrow", "Marrow", UpdateItemKind.Marrow, "1.7.0", "1.8.0");
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
