using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Settings;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using Marrow.UI.Theme;
using Marrow.App.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// CZ-5 review fixes: nullable no-pick API, every activation (CZ-10.2), and every working-dot
// appearance change (CZ-10.4 / Q-CZ-7 / OD-0076), including the actual preview wrapper.
[Collection("Headless")]
public sealed class CustomizationDotsFixTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-251")]
    public void The_swatch_constructor_declares_that_a_line_may_hold_no_pick()
    {
        var selected = typeof(CzSwatchRow).GetConstructors().Single().GetParameters()[2];
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(selected).ReadState);
    }

    [Fact]
    [Trait("Decision", "ED-251")]
    public void The_row_names_helper_preserves_the_nullable_automation_name_contract()
    {
        var names = typeof(CustomizationDotsRowsTests).GetMethod("Names", BindingFlags.Static | BindingFlags.NonPublic)!;
        var element = new NullabilityInfoContext().Create(names.ReturnParameter).ElementType;
        Assert.NotNull(element);
        Assert.Equal(NullabilityState.Nullable, element.ReadState);
    }

    [Theory]
    [InlineData("accent", null)]
    [InlineData("model", null)]
    [InlineData("custom", null)]
    [InlineData("custom", "blue")]
    [InlineData("custom", "windows")]
    [InlineData("custom", "indigo")]
    [Trait("Decision", "ED-251")]
    public void Activating_the_current_pick_runs_the_preview_and_cancels_once(string mode, string? swatch) => headless.OnUiThread(() =>
    {
        var settings = new WorkingDotsSettings
        {
            Mode = mode switch { "model" => WorkingDotsMode.Model, "custom" => WorkingDotsMode.Custom, _ => WorkingDotsMode.Accent },
            Custom = swatch ?? "blue",
        };
        var original = settings;
        var session = new PreviewSession(ProviderKind.Claude);
        var cancels = 0;
        session.VoiceDemoCancelRequested += () => cancels++;
        var section = new WorkingDotsSections(() => settings, s => settings = s, () => "Claude", session.WorkingDotsTouched);
        var button = swatch is null
            ? section.DotColour.Control.Buttons.Single(b => b.Id == mode)
            : section.Swatches.Classic.Buttons.Concat(section.Swatches.Signature.Buttons).Single(b => b.Id == swatch);
        foreach (var state in new[] { PreviewState.Rest, PreviewState.Typing, PreviewState.Running })
        {
            session.Pick(state);
            var before = cancels;
            button.Activate(); // mouse and keyboard use this same activation
            Assert.Equal(PreviewState.Running, session.State);
            Assert.Equal(before + 1, cancels);
            Assert.Equal(original, settings);
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void Custom_fold_tracks_all_three_modes_without_a_layout_dependency() => headless.OnUiThread(() =>
    {
        var settings = WorkingDotsSettings.Default;
        var section = new WorkingDotsSections(() => settings, s => settings = s, () => "Claude", () => { });
        Assert.False(section.CustomSlot.Open);
        foreach (var mode in new[] { WorkingDotsMode.Custom, WorkingDotsMode.Model, WorkingDotsMode.Accent })
        {
            settings = settings with { Mode = mode };
            section.Apply();
            Assert.Equal(mode == WorkingDotsMode.Custom, section.CustomSlot.Open);
        }
    });

    private static void Render(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
    }

    private static Color Frame(DotFieldControl field) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<DotFieldCompositionFrame>(field.LastRenderedFrame).Brush).Color;

    private static Color Still(DotFieldControl field) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(Assert.IsType<Canvas>(field.Layer).Children[0]).Fill).Color;

    private static int Distance(Color a, Color b) => new[] { Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B), Math.Abs(a.A - b.A) }.Max();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [Trait("Decision", "ED-253")]
    public void Theme_and_system_accent_changes_follow_the_point_32_curve_or_land_at_once_under_reduce_motion(bool preview, bool systemAccent, bool reduceMotion) => headless.OnUiThread(() =>
    {
        var oldTheme = ThemeManager.Current;
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var system = Color.Parse("#0078D7");
        using var accent = SwiftColor.AccentSource.Override(() => system);
        ThemeManager.Current = AppTheme.System;
        var tint = WorkingDotsColor.Of(new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = systemAccent ? "windows" : "teal" }, null);
        Control content;
        if (preview)
        {
            var wrapper = new PreviewWorkingDots { Tint = tint };
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(wrapper, true);
            wrapper.Show(true);
            content = wrapper;
        }
        else
        {
            content = new ComposerWorkingDots { Tint = tint };
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(content, true);
        }
        var window = new Avalonia.Controls.Window { Width = 64, Height = 60, Content = content, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            Render(window);
            // Finish the preview's arrival before measuring colour frames; no UI frame is requested by the blend.
            runtime.Advance(TimeSpan.FromSeconds(.5));
            frames.Advance(TimeSpan.FromSeconds(.5));
            Render(window);
            var dots = LogicalQuery.SelfAndDescendants<ComposerWorkingDots>(content).Single();
            var field = Assert.IsType<DotFieldControl>(dots.Child);
            var before = reduceMotion ? Still(field) : Frame(field);
            if (systemAccent)
            {
                system = Color.Parse("#E81123");
                var platform = Avalonia.Application.Current!.PlatformSettings!;
                platform.GetType().GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(platform, [platform.GetColorValues()]);
            }
            else window.RequestedThemeVariant = ThemeVariant.Light;
            var target = tint.Resolve(systemAccent ? ThemeVariant.Dark : ThemeVariant.Light);
            Assert.NotEqual(before, target);
            if (reduceMotion) Assert.Equal(target, Still(field)); // before a render or clock advance
            Render(window);
            if (reduceMotion) Assert.Equal(target, Still(field));
            else
            {
                Assert.Equal(before, Frame(field));
                for (var frame = 1; frame <= 19; frame++)
                {
                    runtime.Advance(TimeSpan.FromSeconds(1.0 / 60));
                    Render(window);
                    var expected = SwiftColor.PremultipliedMix(before, target, CssCurve.At(CssCurve.Ease, frame * 1000.0 / 60, 320));
                    Assert.True(Distance(expected, Frame(field)) <= 2, $"{frame / 60.0:F3} s: {Frame(field)}, curve {expected}");
                    Assert.Equal(0, frames.Pending);
                }
                runtime.Advance(TimeSpan.FromSeconds(.32 - 19.0 / 60));
                Render(window);
                Assert.Equal(target, Frame(field));
            }
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            window.Content = null;
            window.Close();
            ThemeManager.Current = oldTheme;
            Dispatcher.UIThread.RunJobs();
        }
    });
}
