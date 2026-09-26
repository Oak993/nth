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

        // :15 HStack(alignment: .top, spacing: 12)
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Top,
        };
        glyph.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(glyph);
        row.Children.Add(column);
        row.Children.Add(new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Stretch }); // :37 Spacer(minLength: 0)
        if (accessory is not null) // :38-40
            row.Children.Add(accessory);

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

    // :124-129. Swift: duration(from:to:).
    public static string Duration(DateTimeOffset from, DateTimeOffset to) =>
        RelativeTime.Duration(to - from); // :125

    // :74, :149 Divider(). ENGINEERING DECISION: a bare SwiftUI Divider() has no port counterpart of its own; this
    // matches HydraReportsPopover.cs's rendering of the same bare Divider() between a fixed header and a scrolling
    // body in this popover family (Chrome.secondaryText, full width, no inset).
    internal static Control Divider()
    {
        var bar = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        bar.Background = Chrome.SecondaryText.Brush(bar);
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
