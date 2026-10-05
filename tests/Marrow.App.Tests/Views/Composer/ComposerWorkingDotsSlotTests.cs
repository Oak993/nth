using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Settings;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// customization-SPEC CZ-10.4 (CZ-T55): every composer's ComposerWorkingDotsSlot hands its dots the colour the workingDots setting chooses for the composer's thread (accent, the thread's
// model colour, a custom swatch) and follows a change of the setting, of the thread's model and of the theme, as every composer does at once. Built as ComposerWorkingDotsFieldTests builds the slot.
[Collection("Headless")]
public sealed class ComposerWorkingDotsSlotTests(HeadlessSession headless)
{
    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Pump(ThreadRuntimePart12Harness h, AvaloniaWindow window)
    {
        for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));
    }

    private static ComposerWorkingDots DotsOf(Control slot) => LogicalQuery.SelfAndDescendants<ComposerWorkingDots>(slot).Single();

    // the login environment is read before the harness starts (as ComposerLightSlotTests does), or its bootstrap waits for it
    private static ThreadRuntimePart12Harness Harness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }

    // a running turn in a thread that the model holds, and in one that it does not (a composer with no provider: the Purple brand)
    private static (ComposerWorkingDotsSlot Held, ComposerWorkingDotsSlot Unheld, AvaloniaWindow Window) TwoComposers(ThreadRuntimePart12Harness h, bool light = false)
    {
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var unheld = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), h.App));
        h.Main.Loop.Run(() => unheld.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => { h.Runtime.RehearseTurn("working"); unheld.RehearseTurn("working"); });
        var held = h.Main.OnMain(() => new ComposerWorkingDotsSlot(h.Runtime));
        var other = h.Main.OnMain(() => new ComposerWorkingDotsSlot(unheld));
        var host = new StackPanel { Width = 400, VerticalAlignment = VerticalAlignment.Bottom, Children = { new Border { Child = held, Height = 100 }, new Border { Child = other, Height = 100 } } };
        AppEnvironment.SetAppModel(host, h.App);
        var window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        Pump(h, window);
        return (held, other, window);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T55, CZ-10.4: with the default setting both composers wear the accent, as the source draws the dots
    public void Both_composers_wear_the_accent_by_default() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = Harness();
        var (held, unheld, window) = TwoComposers(h);
        h.Main.OnMain(() =>
        {
            Assert.Same(Marrow.UI.Chrome.Chrome.Accent, DotsOf(held).Tint);
            Assert.Same(Marrow.UI.Chrome.Chrome.Accent, DotsOf(unheld).Tint);
            window.Close();
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T55, CZ-10.4: a change of the setting reaches both composers, each in its own thread's colour (Codex is OpenAI, no thread is the Purple brand), then a custom swatch for both
    public void Every_composer_follows_a_change_of_the_setting() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = Harness();
        var (held, unheld, window) = TwoComposers(h);
        h.Main.OnMain(() => h.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model });
        Pump(h, window);
        h.Main.OnMain(() =>
        {
            Assert.Same(WorkingDotsColor.Of(h.App.Settings.WorkingDots, "OpenAI"), DotsOf(held).Tint);
            Assert.Same(WorkingDotsColor.Of(h.App.Settings.WorkingDots, "Purple"), DotsOf(unheld).Tint);
            Assert.NotSame(DotsOf(held).Tint, DotsOf(unheld).Tint);
            h.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom, Custom = "teal" };
        });
        Pump(h, window);
        h.Main.OnMain(() =>
        {
            Assert.Same(WorkingDotsColor.Of(h.App.Settings.WorkingDots, null), DotsOf(held).Tint);
            Assert.Same(DotsOf(held).Tint, DotsOf(unheld).Tint);
            h.App.Settings.WorkingDots = WorkingDotsSettings.Default;
        });
        Pump(h, window);
        h.Main.OnMain(() =>
        {
            Assert.Same(Marrow.UI.Chrome.Chrome.Accent, DotsOf(held).Tint);
            Assert.Same(Marrow.UI.Chrome.Chrome.Accent, DotsOf(unheld).Tint);
            window.Close();
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T55, CZ-10.4: in the model colour a composer follows its thread's model when it changes
    public void A_composer_in_the_model_colour_follows_its_threads_model() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = Harness();
        h.Main.OnMain(() => h.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model });
        var (held, _, window) = TwoComposers(h);
        h.Main.OnMain(() => Assert.Same(WorkingDotsColor.Of(h.App.Settings.WorkingDots, "OpenAI"), DotsOf(held).Tint));
        h.Main.OnMain(() => h.App.UpdateThread(h.Thread.Id, thread => thread with { Provider = ProviderKind.Claude }));
        Pump(h, window);
        h.Main.OnMain(() =>
        {
            Assert.Same(WorkingDotsColor.Of(h.App.Settings.WorkingDots, "Claude"), DotsOf(held).Tint);
            window.Close();
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T55, CZ-10.4: dots that arrive when a turn starts, after the setting changed, wear the colour the setting chooses now
    public void Dots_that_arrive_after_a_change_wear_the_colour_chosen_now() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var slot = h.Main.OnMain(() => new ComposerWorkingDotsSlot(h.Runtime));
        var host = new Border { Child = slot, Width = 400, Height = 100 };
        AppEnvironment.SetAppModel(host, h.App);
        var window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        Pump(h, window);
        h.Main.OnMain(() =>
        {
            Assert.Empty(LogicalQuery.SelfAndDescendants<ComposerWorkingDots>(slot));
            h.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom, Custom = "orchid" };
        });
        Pump(h, window);
        h.Main.OnMain(() => h.Runtime.RehearseTurn("working"));
        Pump(h, window);
        h.Main.OnMain(() =>
        {
            Assert.Same(WorkingDotsColor.Of(h.App.Settings.WorkingDots, null), DotsOf(slot).Tint);
            window.Close();
        });
    });

    [Fact]
    [Trait("Decision", "ED-253")] // CZ-T55, CZ-T54: the running dots of a composer blend to a new colour of the setting over 0.32 s and wear it exactly afterwards; in a light window the light tone
    public void A_running_composer_blends_to_the_new_colour_and_wears_it_in_the_window_theme() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var h = Harness();
        var (held, _, window) = TwoComposers(h, light: true);
        var field = (DotFieldControl)DotsOf(held).Child!;
        Color Frame()
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var pixels = window.CaptureRenderedFrame();
            var frame = field.GetType().GetProperty("LastRenderedFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(field)!;
            return Assert.IsAssignableFrom<ISolidColorBrush>(frame.GetType().GetProperty("Brush")!.GetValue(frame)).Color;
        }
        h.Main.OnMain(() => h.App.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model });
        Pump(h, window);
        h.Main.Advance(TimeSpan.FromSeconds(0.5));
        manual.Advance(TimeSpan.FromSeconds(0.5)); // the dots' own arrival (0.4 s) and the blend (0.32 s) are over
        h.Main.OnMain(() =>
        {
            Assert.Equal(WorkingDotsColor.Resolve(h.App.Settings.WorkingDots, "OpenAI", ThemeVariant.Light), Frame());
            window.Close();
        });
    });
}
