using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-3.4 (the pinned block), CZ-4.1 (the stage frame), CZ-4.2 (the conversation text), CZ-4.4 (the foot). Numbers: stage.json, measured from the locked design by
// tests/fixtures/customization/stage.generate.cjs. The block is not sticky yet (CZ-6 pins it).
[Collection("Headless")]
public sealed class CustomizationStageTests(HeadlessSession headless)
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/stage.json")));
    }

    private static (CustomizationStage Stage, PreviewSession Session, PreviewSettingsModel Settings, Border Host) Make()
    {
        var settings = new PreviewSettingsModel();
        var session = new PreviewSession(ProviderKind.Claude);
        var stage = new CustomizationStage(() => settings.Light, () => settings.Backdrop, session);
        // the page's column: 520 wide (CZ-3.1)
        return (stage, session, settings, new Border { Width = 520, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = stage });
    }

    private static Rect Frame(Visual control, Visual relativeTo) => new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T38, CZ-3.4: the pinned block at rest is 372 high (the design's 371 with the port's 60 top) and fills the 520 column
    [Trait("Spec", "CZ-T38")]
    public void The_pinned_block_at_rest_is_372_high_and_520_wide() => headless.OnUiThread(() =>
    {
        using var fixture = Fixture();
        var g = fixture.RootElement.GetProperty("geometry");
        using var runtime = new TestRuntime();
        var (stage, _, _, host) = runtime.OnMain(Make);
        PreviewHost.Show(host, (window, _) =>
        {
            window.UpdateLayout();
            Assert.Equal(g.GetProperty("restDesign").GetDouble() + (60 - g.GetProperty("pin").GetProperty("padTop").GetDouble()), stage.Bounds.Height);
            Assert.Equal(372, stage.Bounds.Height);
            Assert.Equal(372, CustomizationStage.RestHeight);
            Assert.Equal(520, stage.Bounds.Width);
            Assert.Equal(CustomizationStage.GapBelow, g.GetProperty("pin").GetProperty("marginBottom").GetDouble());
            Assert.Equal(CustomizationStage.BottomPadding, g.GetProperty("pin").GetProperty("padBottom").GetDouble());
        }, runtime: runtime);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T37, CZ-4.1, CZ-3.4: the heading, the card's 12 padding, the 496 x 212 stage with radius 10, the 46 high foot
    [Trait("Spec", "CZ-T37")]
    public void The_stage_is_496_by_212_with_radius_10_in_the_card_under_the_heading_above_the_foot() => headless.OnUiThread(() =>
    {
        using var fixture = Fixture();
        var g = fixture.RootElement.GetProperty("geometry");
        using var runtime = new TestRuntime();
        var (stage, _, _, host) = runtime.OnMain(Make);
        PreviewHost.Show(host, (window, _) =>
        {
            window.UpdateLayout();
            Assert.Equal("Composer light", stage.Heading.Text);
            var heading = Frame(stage.HeadingBox, stage);
            Assert.Equal(60, heading.Y);
            Assert.Equal(g.GetProperty("heading").GetProperty("lineHeight").GetDouble(), heading.Height);
            var pad = g.GetProperty("card").GetProperty("padLeft").GetDouble();
            var surface = Frame(stage.Surface, stage);
            Assert.Equal(g.GetProperty("stage").GetProperty("width").GetDouble(), surface.Width);
            Assert.Equal(g.GetProperty("stage").GetProperty("height").GetDouble(), surface.Height);
            Assert.Equal(g.GetProperty("stage").GetProperty("radius").GetDouble(), stage.Surface.CornerRadius.TopLeft);
            Assert.True(stage.Surface.ClipToBounds);
            Assert.Equal(pad, surface.X);                                                                         // 12 into the card, which fills the column
            Assert.Equal(heading.Bottom + g.GetProperty("heading").GetProperty("marginBottom").GetDouble() + pad, surface.Y); // the card 10 under the heading, the stage 12 into it
            var foot = Frame(stage.Foot, stage);
            Assert.Equal(surface.Bottom, foot.Y);
            Assert.Equal(g.GetProperty("footMin").GetDouble(), foot.Height);
            Assert.Equal(stage.Bounds.Height - g.GetProperty("pin").GetProperty("padBottom").GetDouble(), foot.Bottom);
            // the composer in it: 472 wide, 12 from the stage's left and bottom
            var pill = Frame(stage.Preview.Pill, stage.Surface);
            Assert.Equal((12, 472), (pill.X, pill.Width));
            Assert.Equal(212 - 12, pill.Bottom);
        }, runtime: runtime);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-4.2, CZ-3.5.2: the conversation text above the composer, left and right 24, top 18
    public void The_conversation_text_is_the_designs_and_sits_24_in_and_18_down() => headless.OnUiThread(() =>
    {
        using var fixture = Fixture();
        var html = fixture.RootElement.GetProperty("chat").GetProperty("html").GetString()!;
        Assert.Contains("<h4>Dark mode toggle</h4>", html, StringComparison.Ordinal);
        Assert.Contains("<p>I added a <b>Theme</b> switch to the settings page. It reuses the existing <code>Switch</code> row, so it picks up the same spacing and focus ring as the other options.</p>", html, StringComparison.Ordinal);
        using var runtime = new TestRuntime();
        var (stage, _, _, host) = runtime.OnMain(Make);
        PreviewHost.Show(host, (window, _) =>
        {
            window.UpdateLayout();
            Assert.Equal("Dark mode toggle", stage.ChatHeading.Text);
            Assert.Equal("I added a Theme switch to the settings page. It reuses the existing Switch row, so it picks up the same spacing and focus ring as the other options.",
                string.Concat(stage.ChatBody.Inlines!.OfType<Run>().Select(r => r.Text)));
            var runs = stage.ChatBody.Inlines!.OfType<Run>().ToArray();
            Assert.True(runs.Single(r => r.Text == "Theme").FontWeight >= Avalonia.Media.FontWeight.SemiBold);
            Assert.Equal(12, runs.Single(r => r.Text == "Switch").FontSize);
            Assert.Equal(13, stage.ChatBody.FontSize);
            Assert.Equal(14, stage.ChatHeading.FontSize);
            var chat = Frame(stage.Chat, stage.Surface);
            Assert.Equal(24, chat.X);
            Assert.Equal(18, chat.Y);
            Assert.Equal(496 - 48, chat.Width);
            Assert.False(stage.Chat.IsHitTestVisible);
        }, runtime: runtime);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T28, CZ-4.4, CZ-13.1: the foot's caption follows the app's settings and the preview's maker, crossfading; the switch sits at its left
    public void The_foot_shows_the_switch_at_the_left_and_a_caption_that_follows_the_settings_and_the_maker() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        var (stage, session, settings, host) = runtime.OnMain(Make);
        PreviewHost.Show(host, (window, _) =>
        {
            window.UpdateLayout();
            Assert.Equal("Border beam · Large · Colourful", stage.Caption.Text);
            var foot = Frame(stage.Foot, stage);
            var sw = Frame(stage.Switch, stage);
            Assert.Equal(12, sw.X);
            Assert.Equal(foot.Y + foot.Height / 2, sw.Y + sw.Height / 2, 1);
            var caption = Frame(stage.Caption, stage);
            Assert.Equal(520 - 12, caption.Right, 1);
            Assert.Equal(12, stage.Caption.FontSize);
            Assert.True(stage.Caption.Secondary);
            // a change of the app's settings reaches the preview's light and the caption (live)
            runtime.OnMain(() => settings.Light = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura, Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Maker, Speed = 3.69 } });
            Assert.Equal(runtime.OnMain(() => settings.Light), stage.Preview.Light.Settings);
            Assert.Equal("Uiverse light · Claude colours · 3.7 s a turn", stage.Caption.Text);
            Assert.Equal(0, stage.Caption.IncomingOpacity);   // crossfading in
            runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.FromMilliseconds(50));
            Assert.InRange(stage.Caption.OutgoingOpacity, .001, .999);
            runtime.AdvanceMilliseconds(400); frames.Advance(TimeSpan.FromMilliseconds(400));
            Assert.Equal(1, stage.Caption.IncomingOpacity);
            // the preview's maker is part of the caption under Maker colours
            session.Pick("OpenCodeGo");
            Assert.Equal("Uiverse light · OpenCode Go colours · 3.7 s a turn", stage.Caption.Text);
            runtime.OnMain(() => settings.Light = ComposerLightSettings.Default with { Style = ComposerLightStyle.Off });
            Assert.Equal("No light", stage.Caption.Text);
        }, runtime: runtime, frames: frames);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T35, CZ-4.6: the preview's phase is the session's; the chip's pick of a maker reaches the foot's switch
    [Trait("Spec", "CZ-T35")]
    public void The_session_drives_the_preview_and_the_switch_and_the_composer_chip_share_one_maker() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var (stage, session, _, host) = runtime.OnMain(Make);
        PreviewHost.Show(host, (window, _) =>
        {
            window.UpdateLayout();
            Assert.Equal(ComposerLightPhase.Rest, stage.Preview.Light.Phase);
            session.Pick(PreviewState.Running);
            Assert.Equal(ComposerLightPhase.Running, stage.Preview.Light.Phase);
            stage.Switch.Next.Activate();
            Assert.Equal("OpenAI", session.Maker);
            Assert.Equal("OpenAI", stage.Preview.Chip.NameLabel.Text);
            stage.Preview.Chip.Activate();
            Assert.Equal("Gemini", session.Maker);
            Assert.Equal(PreviewMakers.ChipName("Gemini"), Avalonia.Automation.AutomationProperties.GetName(stage.Switch.Chip));
        }, runtime: runtime);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T36, CZ-4.8: working the whole stage writes nothing to the settings store, opens no popup but the model menu, and holds no thread
    [Trait("Spec", "CZ-T36")]
    public void Working_the_stage_writes_nothing_and_opens_only_the_model_menu() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var writes = runtime.Defaults.Writes.Count;
        var (stage, session, _, host) = runtime.OnMain(Make);
        PreviewHost.Show(host, (window, overlay) =>
        {
            window.UpdateLayout();
            foreach (var state in new[] { PreviewState.Typing, PreviewState.Running, PreviewState.Rest }) session.Pick(state);
            stage.Switch.Next.Activate();
            stage.Switch.Previous.Activate();
            stage.Preview.Chip.Activate();
            Assert.Empty(overlay.Popups);
            stage.Switch.OpenMenu();
            Assert.Single(overlay.Popups);
            stage.Switch.CloseMenu();
            Assert.Equal(writes, runtime.Defaults.Writes.Count);
            Assert.DoesNotContain(stage.GetVisualDescendants(), v => v is Marrow.App.Views.Composer.ComposerView or Marrow.App.Views.Composer.ComposerArea);
        }, runtime: runtime);
    });
}
