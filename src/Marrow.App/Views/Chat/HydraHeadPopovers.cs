using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/HydraHeadPopovers.swift (B2 1fbf81e), :1-173.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; AnyView is Control; Color is
// SwiftColor; Date is a UTC DateTimeOffset.

namespace Marrow.App.Views.Chat;

/// <summary>HydraPopoverHeader (:3-48).</summary>
public sealed class HydraPopoverHeader : Decorator
{
    // :5-12. `captionColor` defaults to Chrome.secondaryText (:8), not a compile-time constant: null here stands for
    // that default. :14-47 body.
    public HydraPopoverHeader(
        HydraPersona persona,
        HydraHeadInfo.Status? status = null,
        string? caption = null,
        SwiftColor? captionColor = null,
        string? title = null,
        Control? accessory = null)
    {
        var resolvedCaptionColor = captionColor ?? Chrome.SecondaryText; // :8

        // :16-17 HydraGlyph(persona:, size: 28, status:).padding(.top, 1)
        var glyph = new HydraGlyph(persona, 28, status: status)
        {
            Margin = new Thickness(0, 1, 0, 0),
        };

        // :19-28 HStack(alignment: .firstTextBaseline, spacing: 8). No baseline guide exists yet (PR-THEME-151
        // part 2); centering the differently sized name and caption on one line is the closest approximation
        // (ENGINEERING DECISION, same precedent as TokenActivitySection.cs:461).
        var firstLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        firstLine.Children.Add(new HydraNameText(persona, 14, SwiftFont.Weight.Semibold, Chrome.PrimaryText)); // :20
        if (caption is { Length: > 0 }) // :21
        {
            var captionText = new TextBlock { Text = caption, TextWrapping = TextWrapping.NoWrap }; // :22, :26 lineLimit(1)
            SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium) // :23
                .MonospacedDigit() // :25
                .Apply(captionText);
            captionText.Foreground = resolvedCaptionColor.Brush(captionText); // :24
            firstLine.Children.Add(captionText);
        }

        // :18-36 VStack(alignment: .leading, spacing: 3)
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        column.Children.Add(firstLine);
        if (title is { Length: > 0 }) // :29
        {
            var titleText = new TextBlock // :30-34
            {
                Text = title,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3, // :33 lineLimit(3)
            };
            SwiftFont.Callout.Apply(titleText); // :31
            titleText.Foreground = Chrome.PrimaryText.Opacity(0.85).Brush(titleText); // :32
            column.Children.Add(titleText);
        }

        // :15 HStack(alignment: .top, spacing: 12), then :36 Spacer(minLength: 0) pushing :38-40's optional accessory
        // to the trailing edge once :44 stretches the HStack to the header's full width. A StackPanel has no
        // flexible-spacer primitive (Avalonia), so this is a two-column Grid ("*,Auto"), following the convention
        // already established for Spacer(minLength:) at ChromeRow (Chrome.cs:1648-1653) and
        // TokenActivitySection.BuildHeader (TokenActivitySection.cs:452-474): column 0 stretches to hold the
        // glyph+text group, column 1 (Auto) holds the accessory, so it lands at the row's trailing edge regardless
        // of column 0's width.
        var leadingGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        glyph.VerticalAlignment = VerticalAlignment.Top;
        leadingGroup.Children.Add(glyph);
        leadingGroup.Children.Add(column);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(leadingGroup, 0);
        row.Children.Add(leadingGroup);
        if (accessory is not null) // :38-40
        {
            accessory.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(accessory, 1);
            row.Children.Add(accessory);
        }

        // :42-46
        Child = new Border
        {
            Child = row,
            Padding = new Thickness(16, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Chrome.Overlay(0.035).Brush(row),
        };
    }
}

/// <summary>HydraHeadReportPopover (:50-130).</summary>
public sealed class HydraHeadReportPopover : Decorator
{
    // :55. Swift: width; prefixed with the owner's name because Layoutable.Width is a member of every control.
    private const double HydraHeadReportPopoverWidth = 460;

    public HydraHeadInfo Head { get; } // :52
    private readonly ValueList<MarkdownBlock> blocks; // :53

    // :57-62 init, and the body :64-103
    public HydraHeadReportPopover(HydraHeadInfo head)
    {
        Head = head;
        var summary = (head.Summary ?? "").Trimmed(); // :60
        blocks = summary.Length == 0 ? ValueList<MarkdownBlock>.Empty : MarkdownView.Blocks(summary); // :61
        Width = HydraHeadReportPopoverWidth; // :102, on the view itself

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :65
        root.Children.Add(new HydraPopoverHeader( // :66-73
            head.Persona,
            head.HydraHeadInfoStatus,
            Caption(head),
            CaptionColor(head.HydraHeadInfoStatus),
            head.Task,
            blocks.Count == 0 ? null : new CopyButton(head.Summary ?? "")));
        root.Children.Add(Divider()); // :74

        Control body;
        if (blocks.Count == 0) // :80-83
        {
            var noReport = new TextBlock { Text = "No report yet.", TextWrapping = TextWrapping.Wrap };
            SwiftFont.Callout.Apply(noReport); // :82
            noReport.Foreground = Chrome.SecondaryText.Brush(noReport); // :83
            body = noReport;
        }
        else // :84-94
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 }; // :85
            MarkdownView.SetMarkdownPointSize(stack, 12); // :92
            MarkdownView.SetMarkdownDimmed(stack, false); // :93
            MarkdownView.SetMarkdownBlockSelection(stack, true); // :94 textSelection(.enabled)
            foreach (var block in blocks) // :86-89
            {
                var view = new MarkdownBlockView(block);
                MarkdownView.SetMarkdownPointSize(view, 12);
                MarkdownView.SetMarkdownDimmed(view, false);
                MarkdownView.SetMarkdownBlockSelection(view, true);
                stack.Children.Add(view);
            }
            body = stack;
        }

        // :97-99
        var scrollContent = new Border
        {
            Child = body,
            Padding = new Thickness(16, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(new PopoverScroll(() => scrollContent, maxHeight: 460, width: HydraHeadReportPopoverWidth)); // :78, :96-100
        root.Width = HydraHeadReportPopoverWidth; // :102
        Child = root;
    }

    // :105-114. Swift: caption(for:).
    public static string Caption(HydraHeadInfo head)
    {
        var baseText = head.HydraHeadInfoStatus switch // :106-111
        {
            HydraHeadInfo.Status.Completed => "Finished", // :107
            HydraHeadInfo.Status.Failed => "Failed", // :108
            HydraHeadInfo.Status.Stopped => "Stopped", // :109
            HydraHeadInfo.Status.Running => "Working", // :110
            _ => throw new UnreachableException(),
        };
        if (head.HydraHeadInfoStatus == HydraHeadInfo.Status.Running || head.FinishedAt is not { } finishedAt) // :112
            return baseText;
        return baseText + " \U000000B7 " + Duration(head.StartedAt, finishedAt); // U+00B7 MIDDLE DOT, :113
    }

    // :116-122. Swift: captionColor(for:).
    public static SwiftColor CaptionColor(HydraHeadInfo.Status status) => status switch // :117-121
    {
        HydraHeadInfo.Status.Failed => Chrome.Danger, // :118
        HydraHeadInfo.Status.Stopped => Chrome.Warning, // :119
        HydraHeadInfo.Status.Completed => Chrome.SecondaryText, // :120 default
        HydraHeadInfo.Status.Running => Chrome.SecondaryText, // :120 default
        _ => throw new UnreachableException(),
    };

    // :124-129. Swift: duration(from:to:), which reads UI/Common/Components.swift:37-40 RelativeTime.duration.
    // ED-197 (F.ICUFormatters/SwiftFormatters.DurationUnits) identifies that shared helper's register -
    // `Duration.seconds(max(0, Int(interval.rounded()))).formatted(.units(allowed: [.seconds, .minutes, .hours],
    // width: .narrow, maximumUnitCount: 2, fractionalPart: .hide(rounded: .down)))` - as one of B3's three ICU
    // duration sites; `RelativeTime.Duration` in Components.cs has not yet been migrated onto it (it is still the
    // pre-B3 hand-built "Ns" / "Mm Ss" register), so this site calls `SwiftFormatters.DurationUnits` directly,
    // matching the pattern already migrated for the sibling ED-197 sites at Hydra.cs:1732-1740 (PR-HYDRA-254) and
    // HydraPanel.cs:810-820. When Components.cs's RelativeTime.Duration is migrated, this can call it instead.
    private static readonly SwiftFormatters.DurationUnit[] DurationUnitsAllowed =
        [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes, SwiftFormatters.DurationUnit.Hours];

    public static string Duration(DateTimeOffset from, DateTimeOffset to)
    {
        var totalSeconds = Math.Max(0, SwiftNumber.TruncatingToInt(SwiftNumber.Rounded((to - from).TotalSeconds))); // :38 max(0, Int(interval.rounded()))
        return SwiftFormatters.DurationUnits(totalSeconds, DurationUnitsAllowed, SwiftFormatters.CurrentLocale.Current); // :39
    }

    // :74, :149 Divider(). ENGINEERING DECISION: a bare SwiftUI Divider() draws the system separator colour, which
    // has no port counterpart. This reuses Chrome.PrimaryText.Opacity(0.22), the colour the port's UI layer already
    // establishes for exactly this SwiftUI construct - ChromeDivider (Chrome.cs:545-555, from Chrome.swift:324-331)
    // and PopoverDivider (Popovers.cs:200-212, from Popovers.swift:152-158) both resolve a bare Divider() to it -
    // full width and with no inset, since unlike PopoverDivider this Divider() carries no .padding() of its own.
    // (HydraReportsPopover.cs:325-330 renders the same bare Divider() at HydraReportsPopover.swift:240 with
    // Chrome.SecondaryText instead, with no ENGINEERING DECISION recorded there; that is a separate, unresolved
    // inconsistency, out of this unit's scope to fix.)
    internal static Control Divider()
    {
        var bar = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        bar.Background = Chrome.PrimaryText.Opacity(0.22).Brush(bar);
        return bar;
    }
}

/// <summary>HydraBriefPopover (:132-173).</summary>
public sealed class HydraBriefPopover : Decorator
{
    // :139. Swift: width; prefixed with the owner's name because Layoutable.Width is a member of every control.
    private const double HydraBriefPopoverWidth = 520;

    public HydraPersona Persona { get; } // :134
    public string? Task { get; }         // :135
    public string Text { get; }          // :136
    private readonly ValueList<MarkdownBlock> blocks; // :137

    // :141-147 init, and the body :149-172
    public HydraBriefPopover(HydraPersona persona, string? task, string text)
    {
        Persona = persona;
        Task = task;
        Text = text;
        blocks = MarkdownView.Blocks(text); // :143
        Width = HydraBriefPopoverWidth; // :168, on the view itself

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :147
        root.Children.Add(new HydraPopoverHeader(persona, caption: "Brief", title: task, accessory: new CopyButton(text))); // :148
        root.Children.Add(HydraHeadReportPopover.Divider()); // :149

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 }; // :152
        MarkdownView.SetMarkdownPointSize(stack, 12); // :159
        MarkdownView.SetMarkdownDimmed(stack, false); // :160
        MarkdownView.SetMarkdownBlockSelection(stack, true); // :161 textSelection(.enabled)
        foreach (var block in blocks) // :153-156
        {
            var view = new MarkdownBlockView(block);
            MarkdownView.SetMarkdownPointSize(view, 12);
            MarkdownView.SetMarkdownDimmed(view, false);
            MarkdownView.SetMarkdownBlockSelection(view, true);
            stack.Children.Add(view);
        }

        // :162-164
        var scrollContent = new Border
        {
            Child = stack,
            Padding = new Thickness(16, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(new PopoverScroll(() => scrollContent, maxHeight: 460, width: HydraBriefPopoverWidth)); // :150, :165
        root.Width = HydraBriefPopoverWidth; // :168
        Child = root;
    }
}
