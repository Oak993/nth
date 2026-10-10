// REVIEW (UE-6 independent review): the Updates page and sidebar offer must follow the About page's behaviour and the offer itself.
using Avalonia.Controls;
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
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
[Trait("Decision", "ED-360")]
[Trait("Feature", "UE-6")]
[Trait("Review", "UE-6")]
public sealed class REVIEW_WhatsNewOfferTests(HeadlessSession headless)
{
    private static AvailableUpdate Release(string notes) => new()
    {
        Version = "1.8.0", Tag = "v1.8.0", Notes = notes,
        DownloadURL = SwiftURL.Create("https://example.invalid/package")!,
        PageURL = SwiftURL.Create("https://example.invalid/release")!,
    };

    private static Avalonia.Controls.Window Show(Control content)
    {
        EnvironmentValues.SetAccessibilityReduceMotion(content, true);
        var window = new Avalonia.Controls.Window { Width = 650, Height = 900, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static UpdateChecker Inject(TestRuntime runtime, AvailableUpdate update)
    {
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(update));
        return runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], ""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Notes_without_a_recognised_section_are_still_shown_with_the_offer_as_About_shows_them(bool sidebar) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var checker = Inject(runtime, Release("Plain prose summary of the release, no headings."));
        using var shared = UpdateChecker.OverrideShared(checker);
        var item = new FakeUpdateItem("marrow", "Nth", UpdateItemKind.Marrow, "1.7.0", "1.8.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => throw new InvalidOperationException("Restart forbidden")));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        Control offer = sidebar ? new UpdatePopoverContent(service) : new UpdatesSettingsPage(service);
        var window = Show(offer);
        try
        {
            // AboutSoftwareUpdateSection falls back to a MarkdownView for such notes; UpdateWhatsNew drops them, so the user installs blind.
            Assert.NotEmpty(offer.GetVisualDescendants().OfType<MarkdownView>());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void An_offer_with_no_Marrow_row_to_install_shows_no_Marrow_whats_new() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var checker = Inject(runtime, Release("## [1.8.0]\n### New features\n- Added update notes\n"));
        using var shared = UpdateChecker.OverrideShared(checker);
        // The service lists no Marrow item (a copy that is not a Velopack install has none), so nothing here can install 1.8.0.
        var item = new FakeUpdateItem("claude", "Claude Code", UpdateItemKind.ProviderCli, "1.0.0", "1.1.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => throw new InvalidOperationException("Restart forbidden")));
        runtime.OnMain(() => service.CheckNow().GetAwaiter().GetResult());
        var page = new UpdatesSettingsPage(service);
        var window = Show(page);
        try
        {
            Assert.Empty(page.GetVisualDescendants().OfType<UpdateReleaseSectionCards>());
        }
        finally { window.Close(); }
    });
}
