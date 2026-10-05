using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :822-845, with App/Window/WindowManager.swift:162,170-191
// (the hosted window's .buttonBorderShape(.capsule) and .tint(theme accent)). The reference is the recorded Mac runtime,
// E:\Droppy\work\reference-video\1-settings-tour.mov (SOURCE FACT for what is on screen; OD-0042 "as the reference videos show
// them"), the Check now button at S 211.7-213.9 on the Monokai theme (accent F92672), 2560x1440 at 1 px = 1 pt, measured on
// the extracted frames: capsule 78 x 20 at rest, fill (247,46,120) over a (52,59,60) backdrop; hover (254,68,141); press
// (254,79,189) with a violet glow under the pointer; disabled "Checking..." 82 x 20, grey fill (75,84,85) over (53,60,59), label
// (123,133,136). The port draws the label in Inter (ED-059, OD-0009), so the widths are compared with a tolerance. The colours
// the material renders (rest, hover, press, disabled) are compared in Marrow.UI.Tests SmallAccentGlassButtonTests, where the headless
// platform composites the material; this class pins the control inside the row: size, label, place, and which effect it carries.
[Collection("Headless")]
public sealed class AboutUpdateViewsCheckNowLookTests(HeadlessSession headless)
{
    private static readonly Color Backdrop = Color.FromRgb(52, 59, 60);

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static void Drain()
    {
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private static (Avalonia.Controls.Window Window, AboutUpdateCheckRow Row) Mount()
    {
        var row = new AboutUpdateCheckRow();
        var window = new Avalonia.Controls.Window
        {
            Width = 600, Height = 160, UseLayoutRounding = false, RequestedThemeVariant = ThemeVariant.Dark,
            Content = new Border { Background = new SolidColorBrush(Backdrop), Child = row },
        };
        window.Show(); Drain();
        return (window, row);
    }

    private static Button CheckButton(Control row) => Assert.Single(row.GetVisualDescendants().OfType<Button>());

    private static Rect BoundsIn(Control control, Avalonia.Controls.Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    [Fact]
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:830-831 .buttonStyle(.glass).controlSize(.small): the reference's 20 pt accent capsule with a white 11 pt label, in the row's trailing slot
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:826-833: small accent glass, as the reference video shows it
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:826-833: the control is a small glass button (the colours are compared in SmallAccentGlassButtonTests)
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:822-845: the row's trailing control in the card
    [Trait("Decision", "ED-059")] // Inter label widths against the reference's SF, compared with a tolerance
    public void Check_now_is_the_reference_runtime_accent_capsule_in_the_rows_trailing_slot() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
        using var shared = UpdateChecker.OverrideShared(checker);
        var (window, row) = Mount();
        var button = CheckButton(row); var capsule = BoundsIn(button, window);
        Assert.Equal(20, capsule.Height); // video: 20 pt tall (controlSize .small)
        Assert.InRange(capsule.Width, 78 - 8, 78 + 8); // video: about 78 pt wide; Inter against SF
        var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal("Check now", label.Text); Assert.Equal(11, label.FontSize); Assert.Equal(FontWeight.Medium, label.FontWeight);
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // white label on the accent
        Assert.Equal("Check now", AutomationProperties.GetName(button));
        // The row: the capsule sits centred in its row, 10 pt from the card's trailing edge (the ChromeRow metrics, video: 11 px).
        var chromeRow = BoundsIn(Assert.Single(row.GetVisualDescendants().OfType<ChromeRow>()), window);
        Assert.InRange(Math.Abs(capsule.Center.Y - chromeRow.Center.Y), 0, 1);
        Assert.Equal(10, chromeRow.Right - capsule.Right);
        // The glass is the accent, drawn at full strength, and interactive: the material's own hover and press light.
        var effect = Assert.Single(row.GetVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
        Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.True(effect.IsInteractive); Assert.Equal(1.0, effect.TintOpacity);
        Assert.Equal(Chrome.Accent.Resolve(label.ActualThemeVariant), effect.TintColor!.Resolve(label.ActualThemeVariant));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:827,832: "Checking…" disabled: the reference's grey translucent capsule with a dim label
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:827,832
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:827,832
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:827,832
    [Trait("Decision", "ED-059")] // Inter label widths against the reference's SF, compared with a tolerance
    public void Disabled_Checking_is_the_reference_runtime_grey_translucent_capsule_with_a_dim_label() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); Set(checker, nameof(UpdateChecker.IsChecking), true);
        using var shared = UpdateChecker.OverrideShared(checker);
        var (window, row) = Mount();
        var button = CheckButton(row); var capsule = BoundsIn(button, window);
        Assert.False(button.IsEnabled); Assert.Equal(20, capsule.Height);
        Assert.InRange(capsule.Width, 82 - 8, 82 + 8); // video: about 82 pt wide ("Checking…" is wider than "Check now")
        var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal("Checking…", label.Text);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.3).Resolve(label.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // video label (123,133,136) over (75,84,85): about 0.3 of white
        var effect = Assert.Single(row.GetVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
        Assert.False(effect.IsInteractive); Assert.Equal(0.0, effect.TintOpacity); // ED-356: the video's grey fill (white at about 11 % over the old dark veil) is the material's own film now; the pixels are SmallAccentGlassButtonTests'
        Assert.Equal(Chrome.PrimaryText.Resolve(label.ActualThemeVariant), effect.TintColor!.Resolve(label.ActualThemeVariant));
        Assert.Equal("Checking…", AutomationProperties.GetName(button));
        window.Close();
    });
}
