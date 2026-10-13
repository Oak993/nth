using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Window details", "Backdrop", "Custom caption" and "Close"; unit U1
// (ED-NEW-inst-u1-1). 600 × 468 DIP, growing with its content (Windows' text size) up to the work area; not resizable,
// no maximise; Mica, Acrylic, else the solid tint (the headless platform has no Mica, so it shows the solid tint, as the
// reference renders do); a contrast theme drops the glass for Windows' colours and a 2 px edge; theme, contrast,
// Animation effects and text size are followed live; a paused Close refuses every close and is drawn disabled.
[Collection("SetupHeadless")]
public sealed class SetupWindowTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void It_opens_600_by_468_not_resizable_without_maximise_and_titled_nth_Setup()
    {
        headless.OnUiThread(() =>
        {
            // ED-NEW-inst-fix-7: the window opens at the tallest screen it can reach; with a typical folder every one fits the
            // canvas's 468 (the rig's long name, whose path takes two lines in Options, is ScreenProbeTests')
            var rig = new WindowRig { InstallRoot = @"C:\Users\Ana\AppData\Local\Programs\nth" };
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();

            Assert.Equal(new Size(600, 468), rig.Window.ClientSize);
            Assert.False(rig.Window.CanResize);
            Assert.False(rig.Window.CanMaximize);
            Assert.True(rig.Window.CanMinimize);
            Assert.Equal("nth Setup", rig.Window.Title);
            Assert.Equal("nth Setup", rig.Window.Caption.Title);
            session.Go(SetupScreen.Options);
            rig.Run(1);
            Assert.Equal(new Size(600, 468), rig.Window.ClientSize);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void It_grows_to_fit_a_taller_screen_and_never_jumps_smaller_while_open()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            rig.Window.Body.Show(new Border { Height = 300 }, immediately: true);
            rig.Window.Show();
            rig.Settle();
            Assert.Equal(468, rig.Window.ClientSize.Height);

            rig.Window.Body.Show(new Border { Height = 640 }, immediately: true);
            rig.Settle();
            Assert.Equal(CaptionBar.BandHeight + 640, rig.Window.ClientSize.Height);

            rig.Window.Body.Show(new Border { Height = 200 }, immediately: true);
            rig.Settle();
            Assert.Equal(CaptionBar.BandHeight + 640, rig.Window.ClientSize.Height);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Past_the_work_area_the_window_stops_growing_and_the_body_scrolls()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            rig.Window.Body.Show(new Border { Height = 300 }, immediately: true);
            rig.Window.Show();
            rig.Settle();

            rig.Window.Body.Show(new Border { Height = 1500 }, immediately: true);
            rig.Settle();

            // capped by the work area (1280 headless) and the platform's auto-size limit (1080 headless), whichever is less
            var height = rig.Window.ClientSize.Height;
            Assert.InRange(height, 468, 1280);
            Assert.True(height < 1500 + CaptionBar.BandHeight);
            var scroller = rig.Window.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Equal(1500, scroller.Extent.Height);
            Assert.Equal(height - CaptionBar.BandHeight, scroller.Viewport.Height);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Text_size_scales_the_type_live_and_lets_the_window_fit_again()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            var title = rig.Window.Body.Current!.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == "Welcome to nth");
            Assert.Equal(28, title.FontSize);

            rig.Change(SystemLook.Default with { TextScale = 2.25 });
            rig.Settle();

            Assert.Equal(63, title.FontSize);
            Assert.Equal(12 * 2.25, rig.Window.Caption.TitleBlock.FontSize);
            // the canvas's Welcome needs more than the base height at 225 %: the window grows to fit it exactly and nothing scrolls
            var scroller = rig.Window.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(rig.Window.ClientSize.Height > 468);
            Assert.Equal(CaptionBar.BandHeight + scroller.Extent.Height, rig.Window.ClientSize.Height, 1);
            Assert.Equal(scroller.Extent.Height, scroller.Viewport.Height, 1);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Without_Mica_the_backdrop_is_the_solid_tint_and_Windows_11_rounds_the_corners()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            rig.Window.Show();

            Assert.Equal(new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None }, rig.Window.TransparencyLevelHint);
            Assert.Equal(SetupPalette.Dark.Solid, Assert.IsType<SolidColorBrush>(rig.Window.Fill).Color);
            Assert.Equal(new CornerRadius(8), rig.Window.Rim.CornerRadius);
            Assert.Equal(new Thickness(1), rig.Window.Rim.BorderThickness);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_backdrop_chooses_Mica_then_Acrylic_then_the_tint()
    {
        var windows11 = SystemLook.Default;
        var windows10 = SystemLook.Default with { WindowsBuild = 19045 };
        var contrast = SystemLook.Default with { HighContrast = true };

        Assert.Equal(new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None }, SetupBackdrop.Levels(windows11));
        Assert.Equal(new[] { WindowTransparencyLevel.None }, SetupBackdrop.Levels(windows10));
        Assert.Equal(new[] { WindowTransparencyLevel.None }, SetupBackdrop.Levels(contrast));
        Assert.Equal(8, SetupBackdrop.CornerRadius(windows11));
        Assert.Equal(0, SetupBackdrop.CornerRadius(windows10));

        var glass = Assert.IsType<LinearGradientBrush>(SetupBackdrop.Fill(SetupPalette.Dark, WindowTransparencyLevel.Mica));
        Assert.Equal(new[] { SetupPalette.Dark.TintTop, SetupPalette.Dark.TintBottom }, glass.GradientStops.Select(s => s.Color));
        Assert.IsType<LinearGradientBrush>(SetupBackdrop.Fill(SetupPalette.Light, WindowTransparencyLevel.AcrylicBlur));
        Assert.Equal(SetupPalette.Light.Solid, Assert.IsType<SolidColorBrush>(SetupBackdrop.Fill(SetupPalette.Light, WindowTransparencyLevel.None)).Color);
        var hc = SetupPalette.HighContrast(SystemColorSet.HighContrastBlack);
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(SetupBackdrop.Fill(hc, WindowTransparencyLevel.Mica)).Color);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_contrast_theme_turned_on_while_open_drops_the_glass_for_Windows_colours_and_a_2_px_edge()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();

            rig.Change(WindowRig.Contrast);
            rig.Window.UpdateLayout();

            Assert.Equal("contrast", rig.Window.Palette.Name);
            Assert.Equal(new[] { WindowTransparencyLevel.None }, rig.Window.TransparencyLevelHint);
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(rig.Window.Fill).Color);
            Assert.Equal(new Thickness(2), rig.Window.Rim.BorderThickness);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(rig.Window.Rim.BorderBrush).Color);
            var title = rig.Window.Body.Current!.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == "Welcome to nth");
            Assert.Equal(Colors.White, Assert.IsType<ImmutableSolidColorBrush>(title.Foreground).Color);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Light_and_dark_follow_Windows_live_without_rebuilding_a_screen()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            var screen = rig.Window.Body.Current;
            var title = screen!.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == "Welcome to nth");
            Assert.Equal(SetupPalette.Dark.Ink1, Assert.IsType<ImmutableSolidColorBrush>(title.Foreground).Color);

            rig.Change(WindowRig.Light);

            Assert.Same(screen, rig.Window.Body.Current);
            Assert.Equal(SetupPalette.Light.Ink1, Assert.IsType<ImmutableSolidColorBrush>(title.Foreground).Color);
            Assert.Equal(SetupPalette.Light.Solid, Assert.IsType<SolidColorBrush>(rig.Window.Fill).Color);
            Assert.Equal(Avalonia.Styling.ThemeVariant.Light, rig.Window.RequestedThemeVariant);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Animation_effects_turned_off_while_open_make_the_next_change_calm()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();

            rig.Change(WindowRig.Calm);
            session.Go(SetupScreen.Installing);

            var arriving = rig.Window.Body.Current!;
            Assert.Equal(0, rig.Window.Body.Pose(arriving).Offset);
            rig.Frames.Run(0.3);
            Assert.Equal((1, 0), rig.Window.Body.Pose(arriving));
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_paused_Close_refuses_the_window_close_and_is_drawn_disabled_until_resumed()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Installing);
            session.Start();
            rig.Window.Show();
            var closed = false;
            rig.Window.Closed += (_, _) => closed = true;

            session.PauseClose();
            rig.Window.Caption.Close.IsHot = true;
            rig.Frames.Run(0.3);
            rig.Window.Close();

            Assert.False(closed);
            Assert.Equal(1, rig.Window.Guard.Refusals);
            Assert.Equal("Finishing installing nth", rig.Blocker.Reason);
            Assert.True(rig.Window.Caption.Close.IsPaused);
            Assert.Equal(0, rig.Window.Caption.Close.Lit);
            Assert.Equal(SetupPalette.Dark.CaptionDisabledOpacity, rig.Window.Caption.Close.GlyphOpacity);

            session.ResumeClose();
            Assert.False(rig.Window.Caption.Close.IsPaused);
            Assert.Null(rig.Blocker.Reason);
            rig.Window.Close();
            Assert.True(closed);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_finished_flow_closes_the_window_even_while_paused()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Installing);
            session.Start();
            rig.Window.Show();
            var closed = false;
            rig.Window.Closed += (_, _) => closed = true;
            session.PauseClose();

            session.Finish(0);

            Assert.True(closed);
            Assert.Equal(0, session.ExitCode);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_caption_buttons_light_in_0_12_s_and_close_lights_red()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            rig.Window.Show();
            var close = rig.Window.Caption.Close;

            rig.Window.Caption.SetHover(20);
            rig.Frames.Run(0.05);
            Assert.InRange(close.Lit, 0.05, 0.95);
            rig.Frames.Run(0.1);
            Assert.Equal(1, close.Lit);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(close.GlyphBrush).Color);
            Assert.Equal(0, rig.Window.Caption.Minimise.Lit);

            rig.Window.Caption.SetHover(0);
            rig.Frames.Run(0.2);
            Assert.Equal(0, close.Lit);
            Assert.Equal(SetupPalette.Dark.Ink1, Assert.IsType<SolidColorBrush>(close.GlyphBrush).Color);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_window_fits_the_work_area()
    {
        Assert.Equal(new WindowFit(600, 1040), WindowFit.Limits(new Size(1920, 1040)));
        Assert.Equal(new WindowFit(560, 700), WindowFit.Limits(new Size(560, 700)));
        Assert.Equal((468.0, false), WindowFit.Height(300, 1040));
        Assert.Equal((720.0, false), WindowFit.Height(720, 1040));
        Assert.Equal((700.0, true), WindowFit.Height(900, 700));
    }
}
