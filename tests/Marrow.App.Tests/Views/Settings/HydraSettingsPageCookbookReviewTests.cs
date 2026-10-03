using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// REVIEW (final review of unit/effort-title-fidelity, round 2, 2026-10-03): HydraSettingsPage.swift:208-218 presents the
// cookbook as a popover. The port builds the popover's content in its Opened handler, the popup having no child until then
// (HydraSettingsPage.cs:318). Through the real presenter (the one the app uses; the recording presenter never opens a popup,
// so the site's own tests raise Opened by hand), Avalonia's Popup.Open gives its host the null child, the site then sets the
// panel while the popup is open, and PopoverHost.Follow, which knows the host only from the child the host already shows,
// never gives the panel to it: the first time the cookbook is opened, nothing shows. ED-238's final-round-1 amendment (3)
// claims every popup PopoverHost.Prepare readies shows content set while it is open.
[Collection("Headless")]
public sealed class HydraSettingsPageCookbookReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-238")] // HydraSettingsPage.swift:208-218 the cookbook popover, with OD-0095's card
    public void REVIEW_The_cookbook_popover_shows_its_panel_the_first_time_it_opens() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var seam = Presentation.Presenter.Override(presenter);
        // As HydraSettingsPageRig: every provider's catalog already attempted, so nothing reaches a provider CLI or the network.
        var attempted = (HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model.Providers)!;
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases) attempted.Add(provider);
        var view = new HydraCookbookButton();
        var host = new Border { Child = view };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Width = 900, Height = 900, Content = host };
        SettingsProviderPopupLayers.Install(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            HydraSettingsPageRig.Click(HydraSettingsPageRig.Of<Button>(view).First());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(presenter.Popups);
            Assert.True(popup.IsOpen);
            Assert.IsType<HydraCookbookPanel>(HydraSettingsPageRig.Of<HydraCookbookPanel>(PopoverHost.Content(popup)!).First());
            Assert.True(window.GetVisualDescendants().OfType<HydraCookbookPanel>().Any(), "the cookbook popover opened with nothing on screen");
        }
        finally
        {
            foreach (var shown in presenter.Popups) shown.IsOpen = false;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
