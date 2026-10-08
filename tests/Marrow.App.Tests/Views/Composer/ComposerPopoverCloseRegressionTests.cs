using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class ComposerPopoverCloseRegressionTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("effort")]
    [InlineData("downloads")]
    [InlineData("usage")]
    [Trait("Decision", "ED-107")]
    public void Toggle_close_retains_the_visible_content_until_the_shrink_finishes(string site) => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var timeScope = SwiftClock.Provider.Override(clock);
        using var downloads = RecentDownloads.DirectoryProvider.Override(() => null);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var presentation = Presentation.Presenter.Override(presenter);
        var recents = new ObservableBoolBox();
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 310, WindowTokens = 1000 })));
        Control view = h.Main.OnMain<Control>(() => site switch
        {
            "effort" => new ModelEffortButton(h.Thread, false),
            "downloads" => new ComposerAttachButton(ComposerAttachPlace.Inside, true, new Binding<bool>(() => recents.Value, value => recents.Value = value), _ => { }, () => { }),
            _ => new ContextMeter(h.Runtime, ProviderKind.Codex, null),
        });
        var host = new Border { Child = view }; AppEnvironment.SetAppModel(host, h.App);
        var window = new Avalonia.Controls.Window { Width = 1400, Height = 900, Content = host };
        SettingsProviderPopupLayers.Install(window);
        void Drain() { h.Main.RunUntilIdle(); h.Main.OnMain(() => { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }); h.Main.RunUntilIdle(); }
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); h.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(ms))); Drain(); }
        h.Main.OnMain(window.Show); Drain();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().Single();
            h.Main.OnMain(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); Drain(); Step(300);
            var popup = Assert.Single(presenter.Popups);
            var card = Assert.IsType<PopoverCard>(popup.Child);
            Assert.True(popup.IsOpen);
            h.Main.OnMain(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); Drain();
            Assert.True(popup.IsOpen);
            Assert.Same(card, popup.Child);
            Step(80);
            Assert.True(popup.IsOpen);
            Assert.InRange(card.Opacity, .1, .9);
            Step(100);
            Assert.False(popup.IsOpen);
            Assert.Null(popup.Child);
        }
        finally { h.Main.OnMain(window.Close); Drain(); }
    });
}
