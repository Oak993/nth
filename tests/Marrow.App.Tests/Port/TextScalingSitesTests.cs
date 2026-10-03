using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

// Source: DroppyCode/App/Views/{Composer/EffortSlider,Settings/AboutUpdateViews,Chat/NewThreadPrompt}.swift (B3 52bc792).
[Collection("Headless")]
public sealed class TextScalingSitesTests(HeadlessSession headless)
{
    private static double Factor(Control view)
    {
        var primitive = Assert.IsAssignableFrom<Type>(typeof(SwiftFont).Assembly.GetType("Marrow.UI.Port.TextScaling"));
        return Assert.IsType<double>(primitive.GetMethod("GetMinimumScaleFactor", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [view]));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:614
    [Trait("Req", "UI-COMPOSER-032")] // App/Views/Composer/EffortSlider.swift:599-617
    [Trait("Decision", "ED-214")]
    public void The_effort_subtitle_uses_0_point_85_and_shrinks_under_a_finite_card_width() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var card = new EffortSliderCard("A model name with an extremely long descriptive suffix", null,
            new ValueList<string>(["low", "high"]), "low", false,
            new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => false, _ => { }), () => { });
        var window = new Avalonia.Controls.Window { Width = 260, Height = 220, Content = card };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var text = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text!.StartsWith("A model name", StringComparison.Ordinal));
        Assert.Equal(0.85, Factor(text));
        Assert.Equal(13 * 0.85, text.FontSize, 7);
        Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:648
    [Trait("Req", "UI-COMPOSER-037")] // App/Views/Composer/EffortSlider.swift:642-651
    [Trait("Decision", "ED-214")]
    public void The_pair_caption_uses_0_point_9() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        var pair = new EffortPairLook(new HydraPair(ProviderKind.Codex) { WorkerEffort = "high" }, model.Providers, null);
        var card = new EffortSliderCard("Lead", ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", false,
            new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => false, _ => { }), () => { }, pair);
        var window = new Avalonia.Controls.Window { Width = 160, Height = 220, Content = card };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var text = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "The lead's effort · heads run at high effort");
        Assert.Equal(0.9, Factor(text));
        Assert.Equal(11 * 0.9, text.FontSize, 7);
        Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // App/Views/Settings/AboutUpdateViews.swift:218-227
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:218-227
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:218-227
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:218-227
    [Trait("Decision", "ED-214")]
    public void Both_texts_in_each_release_card_inherit_0_point_8_and_scale_independently() => headless.OnUiThread(() =>
    {
        var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>([]) }, "1.7.0");
        var window = new Avalonia.Controls.Window { Width = 180, Height = 200, Content = cards };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var texts = cards.GetVisualDescendants().OfType<TextBlock>().ToArray();
        Assert.Equal(6, texts.Length);
        Assert.All(texts, text => { Assert.Equal(0.8, Factor(text)); Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming); });
        Assert.Equal(12.5, Assert.Single(texts, t => t.Text == "New").FontSize);
        Assert.Equal(12.5 * 0.8, Assert.Single(texts, t => t.Text == "Refinements").FontSize, 7);
        Assert.All(texts.Where(t => t.Text == "Nothing here"), text => Assert.Equal(11 * 0.8, text.FontSize, 7));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:22-46
    [Trait("Req", "UI-CHAT-045")] // App/Views/Chat/NewThreadPrompt.swift:43-46
    [Trait("Decision", "ED-214")]
    public void The_whole_new_thread_question_including_the_project_button_text_uses_0_point_6() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var prompt = new NewThreadPrompt(Guid.NewGuid(), "An exceptionally long project name with another long suffix");
            var host = new Border { Child = prompt }; AppEnvironment.SetAppModel(host, model);
            var window = new Avalonia.Controls.Window { Width = 340, Height = 260, Content = host };
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the prompt fades in on the frame clock as it attaches (NewThreadPrompt.swift:50-53)
            window.Show(); Dispatcher.UIThread.RunJobs();
            var texts = prompt.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal(3, texts.Length);
            Assert.All(texts, text => Assert.Equal(0.6, Factor(text)));
            var name = Assert.Single(texts, t => t.Text!.StartsWith("An exceptionally", StringComparison.Ordinal));
            Assert.Equal(28 * 0.6, name.FontSize, 7);
            Assert.Contains(name.TextLayout.TextLines, line => line.HasCollapsed);
            window.Close();
        });
    });
}
