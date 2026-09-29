using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/RequestCards.swift (B2 1fbf81e), :1-66.
// Form (ED-095, R-VIEW-1): a SwiftUI View is a Decorator whose constructor builds `body`.

namespace Marrow.App.Views.Composer;

/// <summary>:3-66 <c>struct ApprovalCard: View</c>: a warning-tinted glass card asking the user to approve, or
/// decline, one pending provider request.</summary>
public sealed class ApprovalCard : Decorator
{
    private const string PlanSentence = "Approve the plan above to let the agent start building."; // :26

    /// <param name="request">:4 <c>let request: ApprovalRequest</c>.</param>
    /// <param name="runtime">:5 <c>let runtime: ThreadRuntime</c>.</param>
    // :7-65 body
    public ApprovalCard(ApprovalRequest request, ThreadRuntime runtime)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 }; // :8 VStack(alignment: .leading, spacing: 10)
        stack.Children.Add(Header(request)); // :9-15
        stack.Children.Add(Body(request)); // :16-33
        if (request.Detail is { Length: > 0 } detail) stack.Children.Add(Detail(detail)); // :34-39
        if (request.Reason is { Length: > 0 } reason) stack.Children.Add(Reason(reason)); // :40-44
        stack.Children.Add(OptionsRow(this, request, runtime)); // :45-60

        var card = new Border { Child = stack, Padding = new Avalonia.Thickness(16) }; // :62 .padding(16)
        card.RoundedRectangle(20, RoundedCornerStyle.Continuous); // :64 in: .rect(cornerRadius: 20, style: .continuous)
        // The same hue as the card's symbol, so a theme recolours the whole card together (:63 comment, :64).
        SwiftGlass.SetEffect(card, GlassEffect.Regular(GlassShape.RoundedRectangle(20, RoundedCornerStyle.Continuous)).Tint(Chrome.Warning, 0.1));
        card.Background = Chrome.Warning.Opacity(0.1).Brush(card); // OD-0005: the app-drawn material's own tint and opacity
        Child = card;
    }

    // :9-15 HStack(spacing: 8) { Image(systemName: request.symbol).foregroundStyle(Chrome.warning); Text(request.headline)
    // .font(.headline); Spacer() }. ENGINEERING DECISION, standalone (no precedent elsewhere in this codebase pins it):
    // a bare trailing Spacer() with nothing after it is a Grid whose last column is "*" and holds no child, so it
    // absorbs the remaining width and pushes the leading content (icon, headline) to the row's leading edge.
    // (App/Views/Tour/TourView.cs:251, App/Views/RootView.cs:346 and UI/Markdown/MarkdownView.cs:1775 are a
    // different idiom: a *middle* "*" column separates leading content from *trailing* content in the Auto column
    // after it -- not this bare-trailing-spacer case.)
    private static Control Header(ApprovalRequest request)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8"), // the 8s are the HStack's own spacing before the headline and before the trailing Spacer; the headline takes the star width so it wraps
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var icon = SwiftImage.SystemName(request.Symbol).ForegroundStyle(Chrome.Warning); // :10-11
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var headline = new TextBlock { Text = request.Headline, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; // :12
        SwiftFont.Headline.Apply(headline); // :13 .font(.headline); ED-186: plain .headline is bold, not chat-semibold
        Grid.SetColumn(headline, 2);
        row.Children.Add(headline);

        return row;
    }

    // :16-33 switch request.kind
    private static Control Body(ApprovalRequest request) => request.RequestKind switch
    {
        ApprovalRequest.Kind.Command => CommandBlock(request.Title), // :17-24
        ApprovalRequest.Kind.Plan => PlanBody(), // :25-28
        _ => DefaultTitle(request.Title), // :29-33: .fileChange, .tool, .permissions
    };

    // :17-24 Text(request.title).font(.system(.callout, design: .monospaced)).textSelection(.enabled).lineLimit(8)
    // .padding(10).frame(maxWidth: .infinity, alignment: .leading).background(.quaternary.opacity(0.5), in: .rect(cornerRadius: 10, style: .continuous))
    private static Control CommandBlock(string title)
    {
        var text = new SelectableTextBlock { Text = title, TextWrapping = TextWrapping.Wrap, MaxLines = 8 }; // :18-21
        SwiftFont.System(SwiftFont.TextStyle.Callout, SwiftFont.Design.Monospaced).Apply(text); // :19
        var box = new Border { Child = text, Padding = new Avalonia.Thickness(10), HorizontalAlignment = HorizontalAlignment.Stretch }; // :22-23
        box.RoundedRectangle(10, RoundedCornerStyle.Continuous); // :24
        // UNVERIFIED OD-0001: SwiftColor.Quaternary has no assigned ED yet (see App/Views/Chat/TimelineRows.cs :463 note).
        box.Background = SwiftColor.Quaternary.Opacity(0.5).Brush(box); // :24 .quaternary.opacity(0.5)
        return box;
    }

    // :25-28 Text("Approve the plan above to let the agent start building.").font(.callout).foregroundStyle(Chrome.secondaryText)
    private static Control PlanBody()
    {
        var text = new TextBlock { Text = PlanSentence, TextWrapping = TextWrapping.Wrap }; // :26
        SwiftFont.Callout.Apply(text); // :27
        text.Foreground = Chrome.SecondaryText.Brush(text); // :28
        return text;
    }

    // :29-32 Text(request.title).font(.callout).textSelection(.enabled)
    private static Control DefaultTitle(string title)
    {
        var text = new SelectableTextBlock { Text = title, TextWrapping = TextWrapping.Wrap }; // :30
        SwiftFont.Callout.Apply(text); // :31
        return text;
    }

    // :34-39 if let detail = request.detail, !detail.isEmpty { Text(detail).font(.caption).foregroundStyle(Chrome.secondaryText).lineLimit(3) }
    private static Control Detail(string detail)
    {
        var text = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis }; // :35, :38 lineLimit(3) truncates with an ellipsis
        SwiftFont.Caption.Apply(text); // :36
        text.Foreground = Chrome.SecondaryText.Brush(text); // :37
        return text;
    }

    // :40-44 if let reason = request.reason, !reason.isEmpty { Text(reason).font(.callout).foregroundStyle(Chrome.secondaryText) }
    private static Control Reason(string reason)
    {
        var text = new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap }; // :41
        SwiftFont.Callout.Apply(text); // :42
        text.Foreground = Chrome.SecondaryText.Brush(text); // :43
        return text;
    }

    // :45-59 HStack(spacing: 8) { Spacer(); ForEach(request.options.reversed()) { option in ... } }. ENGINEERING
    // DECISION: a leading Spacer() with trailing content is App/Views/Chat/HydraIntroPopover.cs's idiom (:61-71):
    // a StackPanel whose own HorizontalAlignment is Right, no separate spacer control.
    private static Control OptionsRow(Control host, ApprovalRequest request, ThreadRuntime runtime)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var approveButtons = new List<Button>();
        foreach (var option in request.Options.Reverse()) // :50 request.options.reversed()
        {
            if (option.OptionRole == ApprovalRequest.Option.Role.Approve) // :51
            {
                var button = GlassProminentButton(option.Title, () => runtime.Resolve(request, option)); // :52-53
                approveButtons.Add(button);
                row.Children.Add(button);
            }
            else
            {
                row.Children.Add(GlassButton(option.Title, () => runtime.Resolve(request, option))); // :55-57
            }
        }

        // :47-49, :54 Only the card at the top of the stack answers to the chord: two cards both claiming it left
        // neither of them reliably approving. ED-099 (3) .command -> Ctrl, (6) a KeyboardShortcut is a KeyGesture;
        // App/Window/WindowManagerTests.cs's ApproveButton is the settled precedent for this exact site.
        if (approveButtons.Count > 0)
        {
            host.Track(
                () => request.Id == runtime.Approvals.FirstOrDefault()?.Id, // :49
                ownsShortcut =>
                {
                    var hotKey = ownsShortcut ? new KeyGesture(Key.Enter, KeyModifiers.Control) : null; // :54
                    foreach (var button in approveButtons) button.HotKey = hotKey;
                });
        }

        return row;
    }

    // ENGINEERING DECISION: `.buttonStyle(.glassProminent)` (:52) and `.buttonStyle(.glass)` (:56) carry no explicit
    // frame or padding in the Swift source, so both follow the one glass-button recipe this codebase already
    // established for that same shape (App/Views/RootView.cs GlassProminentButton, App/Views/Composer/FollowUpEditor.cs
    // GlassButton, App/Views/Tour/TourView.cs primaryActionButton): a continuous capsule sized to its label plus
    // Chrome.CapsuleHorizontalPadding, at Chrome.CapsuleContentHeight, with the regular interactive glass effect;
    // .glassProminent adds the accent tint, plain .glass has none. OD-0005 governs the material itself.
    private static Button GlassProminentButton(string title, Action action)
    {
        var framed = ButtonFrame(title);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 0.3).Interactive());
        framed.Background = Chrome.Accent.Opacity(0.3).Brush(framed);
        return PlainControls.Button(framed, action);
    }

    private static Button GlassButton(string title, Action action)
    {
        var framed = ButtonFrame(title);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        return PlainControls.Button(framed, action);
    }

    private static Border ButtonFrame(string title)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var framed = new Border
        {
            Padding = new Avalonia.Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = Chrome.CapsuleContentHeight,
            Child = label,
        };
        framed.Capsule(RoundedCornerStyle.Continuous);
        return framed;
    }
}
