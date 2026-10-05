using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The Customization page (CZ-4) in a real AppModel's environment, shown in a headless window on a manual frame clock: the runtime the test advances
/// (<see cref="Advance"/> moves the clock and the frames together), the app's in-memory settings store (<see cref="Defaults"/> records every write),
/// and the real popover presenter kept in the window's overlay. Everything the page does runs on the harness's main loop.
/// </summary>
public sealed class CustomizationPageRig : IDisposable
{
    private readonly AppModelHarness harness = new();
    private readonly List<IDisposable> scopes = [];

    public AppModel App { get; }
    public ManualFrameSource Frames { get; } = new();

    /// <summary>With `quiet`, the page runs on this source instead of <see cref="Frames"/> (which then stays idle): it allocates nothing of its own, for the allocation probe.</summary>
    public QuietFrameSource? Quiet { get; }
    public ReviewOverlayPresenter Overlay { get; }
    internal CustomizationSettingsPage Page { get; }
    public HostWindow Window { get; }
    public Border Host { get; }
    public TestRuntime Main => harness.Main;
    public InMemorySettingsStore Defaults => harness.Defaults;

    /// <param name="seed">Runs on the main loop before the page is built, to put settings in place.</param>
    public CustomizationPageRig(bool light = false, bool reduceMotion = false, Action<AppSettings>? seed = null, bool quiet = false)
    {
        if (quiet) Quiet = new QuietFrameSource();
        scopes.Add(FrameClock.Source.Override(quiet ? Quiet! : Frames));
        Overlay = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        scopes.Add(Presentation.Presenter.Override(Overlay));
        App = harness.MakeModel();
        if (seed is not null) Main.OnMain(() => seed(App.Settings));
        (Page, Host, Window) = Main.OnMain(() =>
        {
            var page = new CustomizationSettingsPage();
            var host = new Border { Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = page };
            AppEnvironment.SetAppModel(host, App);
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(host, true);
            var window = new HostWindow { Width = 700, Height = 900, Content = host, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
            SettingsProviderPopupLayers.Install(window);
            window.Show();
            return (page, host, window);
        });
        Settle();
    }

    /// <summary>Lays out and runs what the page posted, until nothing is left (the manual main loop and the UI thread's jobs).</summary>
    public void Settle() => Main.OnMain(() =>
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Runs <paramref name="body"/> on the main loop, then settles.</summary>
    public void Do(Action body)
    {
        Main.OnMain(body);
        Settle();
    }

    public T Get<T>(Func<T> read) => Main.OnMain(read);

    /// <summary>The frame clock and the runtime's clock move together by <paramref name="milliseconds"/>, in steps of one frame (1/60 s) at most.</summary>
    public void Advance(double milliseconds, bool settle = true)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            var step = Math.Min(remaining, 1000.0 / 60);
            Main.AdvanceMilliseconds(step);
            Main.OnMain(() => { if (Quiet is not null) Quiet.Advance(TimeSpan.FromMilliseconds(step)); else Frames.Advance(TimeSpan.FromMilliseconds(step)); });
            remaining -= step;
        }
        if (settle) Settle();
    }

    /// <summary>The app's settings now.</summary>
    public ComposerLightSettings Light => Main.OnMain(() => App.Settings.ComposerLight);

    /// <summary>Puts the composer light into the app's settings, as the page would after an edit.</summary>
    public void SetLight(ComposerLightSettings value) => Do(() => App.Settings.ComposerLight = value);

    public void Dispose()
    {
        Main.OnMain(Window.Close);
        foreach (var scope in scopes) scope.Dispose();
        harness.Dispose();
    }
}
