using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraIntroPopover.swift (B3 52bc792), :1-158.

/// <summary>:3-76 <c>struct HydraIntroPopover: View</c>: the first chat's Hydra intro, opened by the mark itself
/// until it has been read.</summary>
public sealed class HydraIntroPopover : Decorator
{
    // :8 let dismiss: () -> Void; :10-75 body
    public HydraIntroPopover(Action dismiss)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        content.Children.Add(BuildIntroHeader());

        // :26-45 VStack(alignment: .leading, spacing: 12) { three hydraIntroStep rows }
        var steps = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
        };
        steps.Children.Add(HydraIntroStep(
            IntroMark(),
            "You ask, the chat leads",
            "Ask as you always do. Small things it does itself; anything bigger it splits into briefs."));
        steps.Children.Add(HydraIntroStep(
            BuildOverlappingHeads(24),
            "Heads go out in parallel",
            "Each head gets one brief and a panel of its own beside the chat. Hank, Walter, Ada and the rest work at the same time."));
        steps.Children.Add(HydraIntroStep(
            Symbol("checkmark.circle", 20, Chrome.Success),
            "Reports come back, the lead finishes",
            "The lead checks their work, fixes what needs fixing and answers you. Their changes land as one merge."));
        content.Children.Add(steps);

        content.Children.Add(Divider());

        // :47-53 VStack(alignment: .leading, spacing: 2) { "Pairs"; its paragraph }
        var pairs = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
        };
        pairs.Children.Add(Label("Pairs", SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)));
        pairs.Children.Add(Label(
            "A pair says which model leads and which model, or which provider, runs the heads: Opus leading Sonnet, or Claude leading Gemini heads. Without one, the heads run on this chat's model at a working effort. Pairs live in the model picker and in Settings \U0000203A Hydra.", // U+203A SINGLE RIGHT-POINTING ANGLE QUOTATION MARK
            SwiftFont.Callout,
            Chrome.SecondaryText));
        content.Children.Add(pairs);

        // :54-60 HStack(alignment: .center, spacing: 6) { symbol; the line, lineLimit(1) } .font(.callout) .foregroundStyle(secondaryText)
        var hint = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hintSymbol = SwiftImage.SystemName("cursorarrow.click.2").ForegroundStyle(Chrome.SecondaryText);
        SwiftImage.SetFont(hintSymbol, SwiftFont.Callout);
        hint.Children.Add(hintSymbol);
        hint.Children.Add(Label("Right-click the mark to switch Hydra off or on per chat.", SwiftFont.Callout, Chrome.SecondaryText, noWrap: true));
        content.Children.Add(hint);

        // :61-71 HStack(spacing: 8) { Spacer(); settings button; Got it } trailing
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var settingsButton = GlassButton("Set up in Settings", prominent: false); // :67 .glass
        settingsButton.Click += (_, _) =>
        {
            dismiss();
            WindowManager.Shared.ShowSettings(SettingsPage.Hydra);
        };
        buttons.Children.Add(settingsButton);

        var gotItButton = GlassButton("Got it", prominent: true); // :69 .glassProminent
        gotItButton.IsDefault = true; // :70 .keyboardShortcut(.defaultAction)
        gotItButton.Click += (_, _) => dismiss();
        buttons.Children.Add(gotItButton);

        content.Children.Add(buttons);

        // :73-74 .padding(20) .frame(width: 420)
        Child = new Border
        {
            Width = 420,
            Padding = new Thickness(20),
            Child = content,
        };
    }

    // :67,69. Same regular-size capsule mapping as FollowUpEditor.GlassButton and WelcomeView.GlassProminentButton.
    // MARROW_DEFERRED ED-180: .glass/.glassProminent compositor and press response are foundation follow-up work.
    private static Button GlassButton(string title, bool prominent)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.Body.Apply(label);
        var surface = new Border { Child = label, Height = Chrome.CapsuleContentHeight, Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0) };
        surface.Capsule(RoundedCornerStyle.Continuous);
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive();
        if (prominent) { effect = effect.Tint(Chrome.Accent, .3); surface.Background = Chrome.Accent.Opacity(.3).Brush(surface); }
        SwiftGlass.SetEffect(surface, effect);
        return PlainControls.Button(surface, () => {});
    }

    // :78-89 private func hydraIntroStep(glyph:title:line:) -> some View
    internal static Control HydraIntroStep(Control glyph, string title, string line)
    {
        // :80 glyph.frame(width: 30, height: 30)
        var glyphFrame = new Border
        {
            Width = 30,
            Height = 30,
            Child = glyph,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // :81-87 VStack(alignment: .leading, spacing: 2) { title; line }
        var text = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
        };
        text.Children.Add(Label(title, SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)));
        text.Children.Add(Label(line, SwiftFont.Callout, Chrome.SecondaryText));

        // :79 HStack(alignment: .top, spacing: 12)
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Top,
        };
        row.Children.Add(glyphFrame);
        row.Children.Add(text);
        return row;
    }

    // :11-25 the header row: five glyphs at 30, then the title and the lead line
    private static Control BuildIntroHeader()
    {
        var glyphs = RosterGlyphs(30);
        var text = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
        };
        text.Children.Add(Label("Meet Hydra", SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold)));
        text.Children.Add(Label(
            "Hydra is on. Big jobs go out to a team of heads, so this chat leads instead of doing everything itself.",
            SwiftFont.Body,
            Chrome.SecondaryText));

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(glyphs);
        header.Children.Add(text);
        return header;
    }

    // :13-17 HStack(spacing: -6) { five HydraGlyph(persona:at:) at 30 }
    private static Control RosterGlyphs(double size)
    {
        var glyphs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = -6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < 5; i++)
            glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(i), size));
        return glyphs;
    }

    // :28, :114 HydraMarkImage().foregroundStyle(Chrome.primaryText).frame(width: 18, height: 18)
    private static Control IntroMark()
    {
        var mark = new HydraMarkImage
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mark.Child is SwiftImage image)
            image.ForegroundStyle(Chrome.PrimaryText);
        return mark;
    }

    // :33-36, :119-122 HStack(spacing: -8) { two HydraGlyph(persona:at:) }
    private static Control BuildOverlappingHeads(double size)
    {
        var glyphs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = -8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(0), size));
        glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(1), size));
        return glyphs;
    }

    // :133-134 HydraGlyph(persona: HydraRoster.persona(at: 0), size: 24)
    private static Control BuildInfoHeadGlyph()
    {
        return new HydraGlyph(HydraRoster.Persona(0), 24);
    }

    // :41, :127, :138, :143 Image(systemName:).font(.system(size:)).foregroundStyle(_:) in the step's 30-by-30 frame
    private static Control Symbol(string name, double size, SwiftColor color)
    {
        var image = SwiftImage.SystemName(name)
            .Font(SwiftFont.System(size))
            .ForegroundStyle(color);
        return new Border
        {
            Width = 30,
            Height = 30,
            Child = image,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    // :46, :150 Divider().opacity(0.5)
    private static Control Divider()
    {
        var divider = new Border
        {
            Height = 1,
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        divider.Background = Chrome.PrimaryText.Brush(divider);
        return divider;
    }

    // :19-23, :29-30, :42-43, :48-52, :104-108, :115-116, :123-125, :128-130, :134-145, :151-153
    private static TextBlock Label(string value, SwiftFont font, SwiftColor? color = null, bool noWrap = false)
    {
        var label = new TextBlock
        {
            Text = value,
            TextWrapping = noWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        font.Apply(label);
        if (color is not null) label.Foreground = color.Brush(label);
        return label;
    }
}

/// <summary>:91-158 <c>struct HydraInfoPopover: View</c>: the Hydra or Heads explainer.</summary>
public sealed class HydraInfoPopover : Decorator
{
    /// <summary>:92 <c>enum Topic { case hydra, heads }</c>.</summary>
    public enum Topic
    {
        Hydra, // :92
        Heads, // :92
    }

    // :93 let topic: Topic; :95-157 body
    public HydraInfoPopover(Topic topic)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // :97-110 HStack(spacing: 10) { five glyphs at 24; the topic title and subtitle }
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(BuildRosterGlyphs(24));

        var headerText = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
        };
        headerText.Children.Add(Label(
            topic == Topic.Hydra ? "Hydra" : "Heads",
            SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold)));
        headerText.Children.Add(Label(
            topic == Topic.Hydra ? "One chat leads a team of heads on big jobs." : "The helpers a chat sends out, one brief each.",
            SwiftFont.Callout,
            Chrome.SecondaryText));
        header.Children.Add(headerText);
        content.Children.Add(header);

        // :111-148 VStack(alignment: .leading, spacing: 12) with one topic's three steps
        var steps = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
        };

        if (topic == Topic.Hydra)
        {
            steps.Children.Add(HydraIntroPopover.HydraIntroStep(
                InfoMark(),
                "You ask, the chat leads",
                "Ask as you always do. Small things it does itself; anything bigger it splits into briefs."));
            steps.Children.Add(HydraIntroPopover.HydraIntroStep(
                BuildOverlappingHeads(24),
                "Heads go out in parallel",
                "Each head gets one brief and a panel of its own beside the chat, all working at the same time."));
            steps.Children.Add(HydraIntroPopover.HydraIntroStep(
                Symbol("checkmark.circle", 20, Chrome.Success),
                "Reports come back, the lead finishes",
                "The lead checks their work, fixes what needs fixing and answers you. Their changes land as one merge."));
        }
        else
        {
            steps.Children.Add(HydraIntroPopover.HydraIntroStep(
                BuildInfoHeadGlyph(),
                "One brief, one panel",
                "A head gets a self-contained brief and its own panel beside the chat, on the model its pair names or the chat's own."));
            steps.Children.Add(HydraIntroPopover.HydraIntroStep(
                Symbol("square.on.square", 18, Chrome.PrimaryText),
                "Its own copy of the checkout",
                "With the switch on, each head works in a copy of the project so heads never trip over each other; its changes land in the chat's checkout when it reports."));
            steps.Children.Add(HydraIntroPopover.HydraIntroStep(
                Symbol("arrow.uturn.backward.circle", 20, Chrome.Success),
                "Reports back to the lead",
                "A finished head hands its report to the lead, which checks the work and answers you. Cleared heads move from the panel to the sidebar."));
        }

        content.Children.Add(steps);

        // :149-154 if topic == .hydra { Divider().opacity(0.5); the closing hint }
        if (topic == Topic.Hydra)
        {
            content.Children.Add(BuildDivider());
            content.Children.Add(Label(
                "Right-click the Hydra mark in a chat to switch it off or on for that chat.",
                SwiftFont.Callout,
                Chrome.SecondaryText));
        }

        // :156 .padding(18)
        Child = new Border
        {
            Padding = new Thickness(18),
            Child = content,
        };
    }

    // :98-102 HStack(spacing: -6) { five HydraGlyph(persona:at:) at 24 }
    private static Control BuildRosterGlyphs(double size)
    {
        var glyphs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = -6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < 5; i++)
            glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(i), size));
        return glyphs;
    }

    // :133-134 includes the shared glyph's persona accessibility name (HydraGlyph.swift:40).
    private static Control BuildInfoHeadGlyph()
    {
        return new HydraGlyph(HydraRoster.Persona(0), 24);
    }

    // :114 HydraMarkImage().foregroundStyle(Chrome.primaryText).frame(width: 18, height: 18)
    private static Control InfoMark()
    {
        var mark = new HydraMarkImage
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mark.Child is SwiftImage image)
            image.ForegroundStyle(Chrome.PrimaryText);
        return mark;
    }

    // :119-122 HStack(spacing: -8) { two HydraGlyph(persona:at:) at 24 }
    private static Control BuildOverlappingHeads(double size)
    {
        var glyphs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = -8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(0), size));
        glyphs.Children.Add(new HydraGlyph(HydraRoster.Persona(1), size));
        return glyphs;
    }

    // :127, :138, :143 Image(systemName:).font(.system(size:)).foregroundStyle(_:) in the step's 30-by-30 frame
    private static Control Symbol(string name, double size, SwiftColor color)
    {
        var image = SwiftImage.SystemName(name)
            .Font(SwiftFont.System(size))
            .ForegroundStyle(color);
        return new Border
        {
            Width = 30,
            Height = 30,
            Child = image,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    // :150 Divider().opacity(0.5)
    private static Control BuildDivider()
    {
        var divider = new Border
        {
            Height = 1,
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        divider.Background = Chrome.PrimaryText.Brush(divider);
        return divider;
    }

    // :104-108, :115-153
    private static TextBlock Label(string value, SwiftFont font, SwiftColor? color = null)
    {
        var label = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        font.Apply(label);
        if (color is not null) label.Foreground = color.Brush(label);
        return label;
    }
}
