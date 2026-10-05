using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;
using Marrow.UI.Port.Settings;
using System.Reflection;
using Marrow.App.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class LibraryPageReviewTests(HeadlessSession headless)
{
    [Theory, InlineData(false), InlineData(true), Trait("Decision", "ED-261")]
    public void The_optional_still_sheet_uses_fades_and_the_default_keeps_scale(bool inWindow) =>
        LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
        {
            if (!inWindow) { SettingsProviderPopupLayers.Install(harness.Window); harness.Settle(); }
            using var presenter = Presentation.Presenter.Override(new ReviewOverlayPresenter(Presentation.Presenter.Current));
            Control? content = null;
            Action? dismiss = null;
            ChatSheetPresentation.Show(harness.Page, close => { dismiss = close; return content = new Border { Width = 200, Height = 100 }; }, inWindow: inWindow);
            Assert.IsType<ScaleTransform>(content!.RenderTransform);
            harness.Step(400); dismiss!(); harness.Step(400);
            ChatSheetPresentation.Show(harness.Page, close => { dismiss = close; return content = new Border { Width = 200, Height = 100 }; }, inWindow: inWindow, still: true);
            Assert.Null(content!.RenderTransform);
            Assert.Equal(0, content.Opacity);
            harness.Step(30); Assert.InRange(content.Opacity, 0.01, 0.99);
            harness.Step(300); dismiss!();
            Assert.Null(content.RenderTransform);
            harness.Step(80); Assert.InRange(content.Opacity, 0.01, 0.99);
            harness.Step(300);
        }, motion: true);

    [Fact, Trait("Decision", "ED-326")]
    public void An_installed_skill_set_says_Ready_in_new_chats() =>
        LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
        {
            harness.Backend.Install("S1", LibraryFixtures.Pin);
            harness.Controller.Select("S2"); harness.Controller.Select("S1"); harness.Settle();
            Assert.Contains("Ready in new chats", harness.Words(harness.Page.Details["S1"]));
        });

    [Fact, Trait("Decision", "ED-261")]
    public void Changed_included_ids_and_Needs_crossfade_instead_of_clearing_visible_rows() =>
        LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
        {
            harness.Press("download-all"); harness.Step(500);
            var dialog = harness.Page.Sheet!.Dialog;
            var before = dialog.Lines["S1"];
            dialog.Apply(dialog.View with { Lines = [new RunLine("replacement", "Replacement", RunLineState.Waiting, 0, "Waiting")] });
            var incoming = dialog.Lines["replacement"];
            Assert.NotNull(before.GetVisualParent());
            Assert.Equal(0, ((Control)incoming.Parent!).Opacity);
            harness.Step(300);
            Assert.Null(TopLevel.GetTopLevel(before));
            Assert.Equal(1, ((Control)incoming.Parent!).Opacity);
            var detail = harness.Page.Details["S1"];
            var needs = Assert.IsType<Marrow.App.Views.Settings.CrossfadeSlot>(typeof(DetailView).GetField("needs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(detail));
            var old = needs.Live!;
            detail.Apply(harness.Controller.Rows[0] with { Needs = [new NeedLine("changed", "Changed need", null)] }, false);
            Assert.NotSame(old, needs.Live);
            Assert.Equal(1, old.Opacity); Assert.Equal(0, needs.Live!.Opacity);
            harness.Step(300);
            Assert.Null(old.GetVisualParent()); Assert.Equal(1, needs.Live!.Opacity);
        }, motion: true, catalogue: LibraryFixtures.WithPythonNeed(LibraryFixtures.Catalogue()));

    [Fact, Trait("Decision", "ED-261")]
    public void Changed_dialog_facts_and_progress_keep_outgoing_text_and_fade_in_new_text() =>
        LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
        {
            harness.Press("download-all"); harness.Step(500);
            var dialog = harness.Page.Sheet!.Dialog;
            var view = dialog.View;
            dialog.Apply(view with { DownloadFact = "changed download", DiskFact = "changed disk", FreeFact = "changed free", HostsFact = "changed host" });
            foreach (var name in new[] { "download", "onDisk", "free", "from" })
            {
                var text = Assert.IsType<CzCrossfadeText>(typeof(DownloadAllDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog));
                Assert.Equal(0, text.IncomingOpacity); Assert.Equal(1, text.OutgoingOpacity);
            }
            harness.Step(400);
            dialog.Apply(view with { Phase = RunPhase.Downloading, ProgressText = "new progress" });
            var progress = Assert.IsType<CzCrossfadeText>(typeof(DownloadAllDialog).GetField("progress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog));
            Assert.Equal(0, progress.IncomingOpacity);
            harness.Step(400); Assert.Equal(1, progress.IncomingOpacity);
        }, motion: true);

    [Fact, Trait("Decision", "ED-261")]
    public void An_unbound_backend_disables_download_all_Download_Retry_and_Continue_and_starts_nothing() =>
        LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
        {
            Assert.False(harness.Page.DownloadAllButton.IsEnabled);
            Assert.False(harness.Find<Button>("action-download").IsEnabled);
            Assert.Contains(LibraryWords.DownloadsUnavailable, harness.Words());
            harness.Wait(harness.Controller.Download("S1"));
            Assert.Equal(ItemState.Available, harness.Controller.Rows[0].State);
            harness.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.Failed, null, LibraryFixtures.Pin, "fixture failure")], [], [], []);
            harness.Controller.Select("S4"); harness.Controller.Select("S1"); harness.Settle();
            Assert.False(harness.Find<Button>("action-retry").IsEnabled);
            harness.Controller.OpenDownloadAll(); harness.Settle();
            Assert.False(harness.Find<Button>("dialog-continue").IsEnabled);
            harness.Wait(harness.Controller.Continue());
            Assert.Equal(RunPhase.Confirm, harness.Controller.Dialog.Phase);
            var dialog = harness.Page.Sheet!.Dialog;
            dialog.Apply(dialog.View with { Phase = RunPhase.Partial, Failed = 1 }); harness.Settle();
            Assert.False(harness.Find<Button>("dialog-retry").IsEnabled);
            harness.Wait(harness.Controller.RetryFailed());
            Assert.Equal(RunPhase.Confirm, harness.Controller.Dialog.Phase);
            Assert.Contains(LibraryWords.SizeEstimateNote, harness.Words());
        }, install: new FailClosedFirstInstall());

    [Fact, Trait("Decision", "ED-261")]
    public void Reduce_Motion_fades_the_sheet_without_any_scale_on_open_or_close() =>
        LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
        {
            harness.Press("download-all");
            var sheet = harness.Page.Sheet!;
            Assert.Null(sheet.RenderTransform);
            Assert.Equal(0, sheet.Opacity);
            harness.Step(30);
            Assert.InRange(sheet.Opacity, 0.01, 0.99);
            Assert.Null(sheet.RenderTransform);
            harness.Step(300);
            Assert.Equal(1, sheet.Opacity);
            harness.Press("dialog-cancel");
            Assert.Null(sheet.RenderTransform);
            harness.Step(80);
            Assert.InRange(sheet.Opacity, 0.01, 0.99);
            Assert.Null(sheet.RenderTransform);
        }, motion: true, reduceMotion: true);
}
