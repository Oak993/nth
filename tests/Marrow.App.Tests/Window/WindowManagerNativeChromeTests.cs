using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Window;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Microsoft.Win32;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// What the factory's two real HWNDs carry for the system's own chrome: the immersive dark title bar (DWMWA_USE_IMMERSIVE_DARK_MODE,
// 20) that follows the theme (WindowManager.swift:151-159), and the system's window transitions (DWMWA_TRANSITIONS_FORCEDISABLED,
// 3) that stay on (:99). Both are read back from DWM on the windows the factory made, never set by the test.
public sealed partial class WindowManagerNativeTests
{
    private const uint DwmwaTransitionsForceDisabled = 3;
    private const uint DwmwaUseImmersiveDarkMode = 20;

    private static int Dwm(nint handle, uint attribute)
    {
        var status = DwmGetWindowAttribute(handle, attribute, out var value, sizeof(int));
        Assert.Equal(0, status);
        return value;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);

    private static bool SystemAppsAreDark() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;

    [Fact]
    [Trait("Req", "PR-THEME-039")] // WindowManager.swift:151-159 `NSApp.appearance`; windows_path: immersive dark mode on each window
    public void Immersive_dark_mode_follows_the_theme_on_both_factory_windows() => native.Run(subject =>
    {
        var original = native.OnUi(() => subject.Manager.Model.Settings.Theme);
        try
        {
            foreach (var (theme, dark) in new[]
            {
                (AppTheme.Dark, true), (AppTheme.Light, false), (AppTheme.Dracula, true), (AppTheme.System, SystemAppsAreDark()),
            })
            {
                native.OnUi(() => subject.Manager.Model.Settings.Theme = theme);
                subject.MainProbe.Fence();
                subject.SettingsProbe.Fence();
                var expected = dark ? 1 : 0;
                N.Until(() => Dwm(subject.MainProbe.Handle, DwmwaUseImmersiveDarkMode) == expected && Dwm(subject.SettingsProbe.Handle, DwmwaUseImmersiveDarkMode) == expected,
                    $"the immersive dark mode of the two windows did not become {expected} for {theme}");
            }
        }
        finally { native.OnUi(() => subject.Manager.Model.Settings.Theme = original); }
    });

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint parameter, ref NativeAnimationInfo value, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAnimationInfo { public uint Size; public int MinAnimate; }

    // A window shown over a plain backdrop and then hidden: the screen under its middle is sampled as fast as it can be
    // read. The time from the last sample of the window's colour to the first of the backdrop's, and the samples between them,
    // are the system's fade-out (DWM's transition): measured on this PC 130 to 150 ms and 12 to 31 samples for a window with
    // its transitions on, 5 to 8 ms and none for one with them forced off (one composed frame).
    private (int Between, double Milliseconds, int Samples) FadeSamples(AvaloniaWindow window, PixelPoint at)
    {
        using var sampler = new Sampler(at);
        Thread.Sleep(300);
        native.OnUi(window.Hide);
        Thread.Sleep(700);
        var samples = sampler.Stop();
        Assert.Contains(samples, sample => sample.Mean > 245); // the window was seen, white
        Assert.Contains(samples, sample => sample.Mean < 115); // and then the backdrop
        var lastWhite = samples.Last(sample => sample.Mean > 245);
        var firstBackdrop = samples.First(sample => sample.Milliseconds > lastWhite.Milliseconds && sample.Mean < 115);
        return (samples.Count(sample => sample.Mean is > 115 and < 245), firstBackdrop.Milliseconds - lastWhite.Milliseconds, samples.Length);
    }

    [Fact]
    [Trait("Req", "PR-WIN-008")] // WindowManager.swift:99 animationBehavior = .documentWindow; windows_path: the system's window animation
    public void The_factory_windows_are_hidden_with_the_systems_fade_not_cut_and_a_window_with_transitions_disabled_is_cut() => native.Run(subject =>
    {
        var info = new NativeAnimationInfo { Size = (uint)Marshal.SizeOf<NativeAnimationInfo>() };
        Assert.True(SystemParametersInfo(0x0048 /* SPI_GETANIMATION */, info.Size, ref info, 0));
        if (info.MinAnimate == 0) throw new InvalidOperationException("NATIVE_PRECONDITION: 'Animate windows when minimizing and maximizing' is off on this PC.");
        var screen = native.OnUi(() => subject.Main.Screens.Primary!);
        var work = screen.WorkingArea;
        var backdrop = native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            var plain = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(work.X + 100, work.Y + 60),
                Width = 1000 / screen.Scaling, Height = 700 / screen.Scaling, Content = new Border { Background = Brushes.DimGray },
            };
            plain.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(plain);
            plain.Show();
            return plain;
        });
        Thread.Sleep(600);
        var inside = new PixelPoint(work.X + 560, work.Y + 380);
        var bare = ScreenGrab.Of(inside.X, inside.Y, 60, 12).MeanLuma();
        Assert.InRange(bare, 95, 115); // DimGray

        AvaloniaWindow Place(AvaloniaWindow window, double width, double height)
        {
            native.OnUi(() =>
            {
                window.Topmost = true;
                window.Width = width;
                window.Height = height;
                window.Position = new PixelPoint(work.X + 300, work.Y + 200);
                window.Show();
            });
            Thread.Sleep(700);
            Assert.True(ScreenGrab.Of(inside.X, inside.Y, 60, 12).MeanLuma() > 245, "the window is not over the backdrop");
            return window;
        }

        // the control: the instrument sees a cut when the window's transitions are disabled
        var scratch = native.OnUi(() =>
        {
            var plain = new AvaloniaWindow { ShowInTaskbar = false, ShowActivated = false, Topmost = true, CanResize = false, WindowStartupLocation = WindowStartupLocation.Manual, Content = new Border { Background = Brushes.White } };
            plain.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(plain);
            return plain;
        });
        Place(scratch, 800 / screen.Scaling, 500 / screen.Scaling);
        var forced = 1;
        Assert.Equal(0, DwmSetWindowAttribute(native.OnUi(() => scratch.TryGetPlatformHandle()!.Handle), DwmwaTransitionsForceDisabled, ref forced, sizeof(int)));
        var cut = FadeSamples(scratch, inside);
        Assert.True(cut.Milliseconds <= 40 && cut.Between <= 2, $"the control window with transitions disabled still faded: {cut}");

        // the same hide on a window of the same kind with transitions left alone fades (the control, enabled)
        var control = native.OnUi(() =>
        {
            var plain = new AvaloniaWindow { ShowInTaskbar = false, ShowActivated = false, Topmost = true, CanResize = false, WindowStartupLocation = WindowStartupLocation.Manual, Content = new Border { Background = Brushes.White } };
            plain.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(plain);
            return plain;
        });
        Place(control, 800 / screen.Scaling, 500 / screen.Scaling);
        var faded = FadeSamples(control, inside);
        Assert.True(faded.Milliseconds >= 80 && faded.Between >= 4, $"a window with transitions enabled was cut: {faded}");
        // the factory's own windows, as the app makes them: Settings (its close hides it, :98) and main
        foreach (var (window, name) in new[] { (subject.Settings, "Settings"), (subject.Main, "main") })
        {
            Place(window, name == "Settings" ? 780 : 880, name == "Settings" ? 580 : 570);
            var factory = FadeSamples(window, inside);
            Assert.True(factory.Milliseconds >= 80 && factory.Between >= 4, $"the factory {name} window was cut, not faded: {factory}");
        }
        GC.KeepAlive(backdrop);
    });
}
