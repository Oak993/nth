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

    /// <summary>With `scroll`, the page's scroll viewer, as the Settings window holds it: the page's column in a viewer whose content has the window's 60 above the page (the page takes it back, CZ-4) and 24 below.</summary>
    internal ScrollViewer? Scroll { get; }
    public TestRuntime Main => harness.Main;
    public InMemorySettingsStore Defaults => harness.Defaults;

    /// <param name="seed">Runs on the main loop before the page is built, to put settings in place.</param>
    /// <param name="scroll">The page in a scroll viewer (CZ-6) of the window's height <paramref name="height"/>, as the Settings window holds it.</param>
    /// <param name="height">The window's height.</param>
    public CustomizationPageRig(bool light = false, bool reduceMotion = false, Action<AppSettings>? seed = null, bool quiet = false, bool scroll = false, double height = 900)
    {
        if (quiet) Quiet = new QuietFrameSource();
        scopes.Add(FrameClock.Source.Override(quiet ? Quiet! : Frames));
        Overlay = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        scopes.Add(Presentation.Presenter.Override(Overlay));
        App = harness.MakeModel();
        if (seed is not null) Main.OnMain(() => seed(App.Settings));
        (Page, Host, Window, Scroll) = Main.OnMain(() =>
        {
            var page = new CustomizationSettingsPage();
            var host = new Border { Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = page };
            var viewer = scroll ? new ScrollViewer { Content = host, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } : null;
            if (viewer is not null) host.Margin = new Avalonia.Thickness(0, Marrow.UI.Chrome.Chrome.ChromeTopPadding + Marrow.UI.Chrome.Chrome.CapsuleHeight + 16, 0, 24);
            AppEnvironment.SetAppModel(host, App);
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(host, true);
            var window = new HostWindow { Width = 700, Height = height, Content = (Control?)viewer ?? host, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
            SettingsProviderPopupLayers.Install(window);
            window.Show();
            return (page, host, window, viewer);
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
        var end = Main.Time.Elapsed + TimeSpan.FromMilliseconds(milliseconds);
        while (Main.Time.Elapsed < end)
        {
            var step = TimeSpan.FromTicks(Math.Min((end - Main.Time.Elapsed).Ticks, TimeSpan.FromSeconds(1.0 / 60).Ticks));
            Main.Advance(step);
            Main.OnMain(() => { if (Quiet is not null) Quiet.Advance(step); else Frames.Advance(step); });
        }
        if (settle) Settle();
    }

    /// <summary>Scrolls the page to <paramref name="y"/> as a wheel would (the viewer's offset), then settles layout.</summary>
    internal void ScrollTo(double y)
    {
        Main.OnMain(() => Scroll!.Offset = new Avalonia.Vector(0, y));
        Settle();
    }

    /// <summary>The viewer's offset now.</summary>
    internal double Offset => Main.OnMain(() => Scroll!.Offset.Y);

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
