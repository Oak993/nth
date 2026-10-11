using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Threading;
using Marrow.App.Window;
using Marrow.App.Views.Settings;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraIntroPopoverTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-HYDRA-063")] // App/Views/Chat/HydraIntroPopover.swift:133-134; UI/Common/HydraGlyph.swift:40
    public void Heads_step_one_keeps_the_persona_accessibility_name()
    {
        headless.OnUiThread(() => {
            var glyphs = LogicalQuery.SelfAndDescendants<HydraGlyph>(new HydraInfoPopover(HydraInfoPopover.Topic.Heads)).ToArray();
            Assert.Equal(6, glyphs.Length);
            Assert.Equal(AutomationProperties.GetName(glyphs[0]), AutomationProperties.GetName(glyphs[5]));
            Assert.NotEmpty(AutomationProperties.GetName(glyphs[5])!);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-059")] // App/Views/Chat/HydraIntroPopover.swift:63-67
    [Trait("Req", "UI-HYDRA-060")] // App/Views/Chat/HydraIntroPopover.swift:68-70
    public void Action_buttons_have_regular_and_prominent_interactive_glass()
    {
        headless.OnUiThread(() => {
            var buttons = LogicalQuery.SelfAndDescendants<Button>(new HydraIntroPopover(() => {})).ToArray();
            var regular = Assert.Single(LogicalQuery.SelfAndDescendants<Control>(buttons[0]).Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
            var prominent = Assert.Single(LogicalQuery.SelfAndDescendants<Control>(buttons[1]).Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
            Assert.Equal(GlassShapeKind.Capsule, regular.Shape.Kind); Assert.True(regular.IsInteractive); Assert.Null(regular.TintColor);
            Assert.Equal(GlassShapeKind.Capsule, prominent.Shape.Kind); Assert.True(prominent.IsInteractive); Assert.NotNull(prominent.TintColor);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-059")] // App/Views/Chat/HydraIntroPopover.swift:63-66
    [Trait("Decision", "OD-0140")] // Settings opens as the panel inside the main window
    public void Setup_dismisses_before_opening_settings_on_the_hydra_page()
    {
        headless.OnUiThread(() => {
            using var harness = new AppModelHarness();
            harness.Main.OnMain(() => {
                var manager = new WindowManager { MainContent = () => new Border() }; var content = new Border(); var events = new List<string>();
                manager.SettingsContent = () => { events.Add("open"); return content; };
                using var shared = WindowManager.OverrideShared(manager);
                var prior = SettingsNavigation.Shared.RequestedPage;
                try {
                    var intro = new HydraIntroPopover(() => events.Add("dismiss"));
                    LogicalQuery.SelfAndDescendants<Button>(intro).Single(b => ButtonText(b) == "Set up in Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(new[] {"dismiss", "open"}, events); Assert.Equal(SettingsPage.Hydra, SettingsNavigation.Shared.RequestedPage);
                    Assert.Same(manager.MainWindow, TopLevel.GetTopLevel(content)); // OD-0140: the Settings panel is in the main window
                    Assert.True(manager.IsSettingsOpen);
                    Assert.True(((Avalonia.Controls.Window)TopLevel.GetTopLevel(content)!).IsVisible);
                } finally { manager.CloseSettings(); ((Avalonia.Controls.Window?)TopLevel.GetTopLevel(content))?.Close(); SettingsNavigation.Shared.RequestedPage = prior; Dispatcher.UIThread.RunJobs(); }
            });
        });
    }
    [Fact]
    [Trait("Req", "PR-HYDRA-217")] // App/Views/Chat/HydraIntroPopover.swift:11-75
    [Trait("Req", "UI-HYDRA-052")] // App/Views/Chat/HydraIntroPopover.swift:11-25
    [Trait("Req", "UI-HYDRA-053")] // App/Views/Chat/HydraIntroPopover.swift:21-23
    [Trait("Req", "UI-HYDRA-054")] // App/Views/Chat/HydraIntroPopover.swift:27-31
    [Trait("Req", "UI-HYDRA-055")] // App/Views/Chat/HydraIntroPopover.swift:32-39
    [Trait("Req", "UI-HYDRA-056")] // App/Views/Chat/HydraIntroPopover.swift:40-44
    [Trait("Req", "UI-HYDRA-057")] // App/Views/Chat/HydraIntroPopover.swift:46-53
    [Trait("Req", "UI-HYDRA-058")] // App/Views/Chat/HydraIntroPopover.swift:54-60
    [Trait("Req", "UI-HYDRA-059")] // App/Views/Chat/HydraIntroPopover.swift:61-67
    [Trait("Req", "UI-HYDRA-060")] // App/Views/Chat/HydraIntroPopover.swift:68-70
    public void Intro_shows_its_exact_text_in_source_order()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraIntroPopover(() => { });

            Assert.Equal(
                new[]
                {
                    "Meet Hydra",
                    "Hydra is on. Big jobs go out to a team of heads, so this chat leads instead of doing everything itself.",
                    "You ask, the chat leads",
                    "Ask as you always do. Small things it does itself; anything bigger it splits into briefs.",
                    "Heads go out in parallel",
                    "Each head gets one brief and a panel of its own beside the chat. Hank, Walter, Ada and the rest work at the same time.",
                    "Reports come back, the lead finishes",
                    "The lead checks their work, fixes what needs fixing and answers you. Their changes land as one merge.",
                    "Pairs",
                    "A pair says which model leads and which model, or which provider, runs the heads: Opus leading Sonnet, or Claude leading Gemini heads. Without one, the heads run on this chat's model at a working effort. Pairs live in the model picker and in Settings \U0000203A Hydra.", // U+203A SINGLE RIGHT-POINTING ANGLE QUOTATION MARK
                    "Right-click the mark to switch Hydra off or on per chat.",
                    "Set up in Settings",
                    "Got it",
                },
                VisibleText(popover));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-217")] // App/Views/Chat/HydraIntroPopover.swift:11-75
    [Trait("Req", "UI-HYDRA-052")] // App/Views/Chat/HydraIntroPopover.swift:11-25
    [Trait("Req", "UI-HYDRA-054")] // App/Views/Chat/HydraIntroPopover.swift:27-31
    [Trait("Req", "UI-HYDRA-055")] // App/Views/Chat/HydraIntroPopover.swift:32-39
    [Trait("Req", "UI-HYDRA-056")] // App/Views/Chat/HydraIntroPopover.swift:40-44
    public void Intro_uses_its_roster_glyph_sizes_and_step_symbols()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraIntroPopover(() => { });
            var glyphs = LogicalQuery.SelfAndDescendants<HydraGlyph>(popover).ToArray();

            Assert.Equal(new[] { 30d, 30d, 30d, 30d, 30d, 24d, 24d }, glyphs.Select(glyph => glyph.Size));
            Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(popover));

            var symbols = LogicalQuery.SelfAndDescendants<SwiftImage>(popover)
                .Where(image => image.SymbolName is not null)
                .Select(image => image.SymbolName)
                .ToArray();
            Assert.Equal(new[] { "checkmark.circle", "cursorarrow.click.2" }, symbols);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-217")] // App/Views/Chat/HydraIntroPopover.swift:11-75
    [Trait("Req", "UI-HYDRA-060")] // App/Views/Chat/HydraIntroPopover.swift:68-70
    public void Got_it_is_the_default_action_and_dismisses_the_intro()
    {
        headless.OnUiThread(() =>
        {
            var dismissals = 0;
            var popover = new HydraIntroPopover(() => dismissals++);
            var button = LogicalQuery.SelfAndDescendants<Button>(popover)
                .Single(candidate => ButtonText(candidate) == "Got it");

            Assert.True(button.IsDefault);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, dismissals);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-218")] // App/Views/Chat/HydraIntroPopover.swift:91-158
    [Trait("Req", "PR-SETTINGS-148")] // App/Views/Chat/HydraIntroPopover.swift:104-155
    [Trait("Req", "UI-HYDRA-061")] // App/Views/Chat/HydraIntroPopover.swift:96-110
    [Trait("Req", "UI-HYDRA-062")] // App/Views/Chat/HydraIntroPopover.swift:149-154
    public void Hydra_info_topic_shows_its_exact_text_and_closing_hint()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraInfoPopover(HydraInfoPopover.Topic.Hydra);

            Assert.Equal(
                new[]
                {
                    "Hydra",
                    "One chat leads a team of heads on big jobs.",
                    "You ask, the chat leads",
                    "Ask as you always do. Small things it does itself; anything bigger it splits into briefs.",
                    "Heads go out in parallel",
                    "Each head gets one brief and a panel of its own beside the chat, all working at the same time.",
                    "Reports come back, the lead finishes",
                    "The lead checks their work, fixes what needs fixing and answers you. Their changes land as one merge.",
                    "Right-click the Hydra mark in a chat to switch it off or on for that chat.",
                },
                VisibleText(popover));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-218")] // App/Views/Chat/HydraIntroPopover.swift:91-158
    [Trait("Req", "PR-SETTINGS-148")] // App/Views/Chat/HydraIntroPopover.swift:104-155
    [Trait("Req", "UI-HYDRA-061")] // App/Views/Chat/HydraIntroPopover.swift:96-110
    [Trait("Req", "UI-HYDRA-063")] // App/Views/Chat/HydraIntroPopover.swift:131-136
    [Trait("Req", "UI-HYDRA-064")] // App/Views/Chat/HydraIntroPopover.swift:137-141
    [Trait("Req", "UI-HYDRA-065")] // App/Views/Chat/HydraIntroPopover.swift:142-146
    public void Heads_info_topic_shows_its_exact_text_without_a_closing_hint()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraInfoPopover(HydraInfoPopover.Topic.Heads);

            Assert.Equal(
                new[]
                {
                    "Heads",
                    "The helpers a chat sends out, one brief each.",
                    "One brief, one panel",
                    "A head gets a self-contained brief and its own panel beside the chat, on the model its pair names or the chat's own.",
                    "Its own copy of the checkout",
                    "With the switch on, each head works in a copy of the project so heads never trip over each other; its changes land in the chat's checkout when it reports.",
                    "Reports back to the lead",
                    "A finished head hands its report to the lead, which checks the work and answers you. Cleared heads move from the panel to the sidebar.",
                },
                VisibleText(popover));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-218")] // App/Views/Chat/HydraIntroPopover.swift:91-158
    [Trait("Req", "PR-SETTINGS-148")] // App/Views/Chat/HydraIntroPopover.swift:112-147
    [Trait("Req", "UI-HYDRA-063")] // App/Views/Chat/HydraIntroPopover.swift:131-136
    [Trait("Req", "UI-HYDRA-064")] // App/Views/Chat/HydraIntroPopover.swift:137-141
    [Trait("Req", "UI-HYDRA-065")] // App/Views/Chat/HydraIntroPopover.swift:142-146
    public void Heads_info_topic_uses_its_five_roster_glyphs_and_head_symbols()
    {
        headless.OnUiThread(() =>
        {
            var popover = new HydraInfoPopover(HydraInfoPopover.Topic.Heads);

            Assert.Equal(
                new[] { 24d, 24d, 24d, 24d, 24d, 24d }, // five header glyphs plus Swift :133 step-one glyph.
                LogicalQuery.SelfAndDescendants<HydraGlyph>(popover).Select(glyph => glyph.Size));

            var symbols = LogicalQuery.SelfAndDescendants<SwiftImage>(popover)
                .Where(image => image.SymbolName is not null)
                .Select(image => image.SymbolName)
                .ToArray();
            Assert.Equal(
                new[] { "square.on.square", "arrow.uturn.backward.circle" },
                symbols);
        });
    }

    private static IReadOnlyList<string> VisibleText(Control root)
    {
        var result = new List<string>();
        foreach (var control in LogicalQuery.SelfAndDescendants<Control>(root))
        {
            if (control is TextBlock { Text: { } text })
                result.Add(text);
            else if (control is ContentControl { Content: string content })
                result.Add(content);
        }

        return result;
    }

    private static string? ButtonText(Button button) =>
        button.Content switch
        {
            string text => text,
            TextBlock textBlock => textBlock.Text,
            _ => LogicalQuery.SelfAndDescendants<TextBlock>(button).FirstOrDefault()?.Text,
        };
}
