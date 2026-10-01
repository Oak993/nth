using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraMergePopover.swift (B2 1fbf81e), :1-286.

/// <summary>:4-76 <c>struct HydraMergeNote</c>: a merge note's body read into fields.</summary>
public sealed record HydraMergeNote
{
    public required MergeRequestLink Link { get; init; } // :8
    public required long Files { get; init; } // :9
    public required long Additions { get; init; } // :10
    public required long Deletions { get; init; } // :11
    public required string Target { get; init; } // :12
    public required string Branch { get; init; } // :13
    public required ValueList<string> Notes { get; init; } // :14
    public required ValueList<Head> Heads { get; init; } // :15

    /// <summary>:17-23 <c>struct Head: Hashable, Identifiable</c>.</summary>
    public sealed record Head
    {
        public required string Name { get; init; } // :18
        public required long Index { get; init; } // :19
        public required string Task { get; init; } // :20
        public required ValueList<string> Files { get; init; } // :21

        // :22 var id: String
        public string Id => $"{Index}-{Name}";
    }

    // :25-75 static func parse(_:link:) -> HydraMergeNote?
    private static readonly Regex HeadPattern = new(
        @"\A- (.+?) \((\d+)\): (.*?) \u2014 (?:no files|(\d+) files?: (.*))\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex FilesPattern = new(
        @"\A(\d+) files? \(\+(\d+) \u2212(\d+)\) landed on `([^`]+)` from `([^`]+)`\.\z",
        RegexOptions.CultureInvariant);

    public static HydraMergeNote? Parse(string body, MergeRequestLink link)
    {
        (long Files, long Additions, long Deletions, string Target, string Branch)? match = null;
        var notes = new List<string>();
        var heads = new List<Head>();
        var inHeads = false;

        // :30-31 components(separatedBy: "\n"), followed by trimmed.
        foreach (var rawLine in body.Split("\n"))
        {
            var line = rawLine.Trimmed();
            if (line.Length == 0) // :32-35
            {
                inHeads = false;
                continue;
            }

            if (line == "Heads") // :36-39
            {
                inHeads = true;
                continue;
            }

            if (inHeads)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal)) // :40-55
                {
                    var foundHead = HeadPattern.Match(line);
                    if (foundHead.Success)
                    {
                        var filesText = foundHead.Groups[5].Success ? foundHead.Groups[5].Value : "";
                        heads.Add(new Head
                        {
                            Name = foundHead.Groups[1].Value,
                            Index = SwiftNumber.ParseInt(foundHead.Groups[2].Value) ?? 0,
                            Task = foundHead.Groups[3].Value,
                            Files = foundHead.Groups[4].Success && filesText.Length != 0
                                ? new ValueList<string>(filesText.Split(", "))
                                : ValueList<string>.Empty,
                        });
                    }

                    continue;
                }

                inHeads = false; // :56-57
            }

            // :58-65. The trimmed line must match whole; only U+2212 is accepted.
            var foundFiles = FilesPattern.Match(line);
            if (foundFiles.Success && match is null)
            {
                var files = SwiftNumber.ParseInt(foundFiles.Groups[1].Value);
                var additions = SwiftNumber.ParseInt(foundFiles.Groups[2].Value);
                var deletions = SwiftNumber.ParseInt(foundFiles.Groups[3].Value);
                if (files is null || additions is null || deletions is null)
                    continue;

                match = (
                    files.Value,
                    additions.Value,
                    deletions.Value,
                    foundFiles.Groups[4].Value,
                    foundFiles.Groups[5].Value);
                continue;
            }

            // :66-69
            if (line.EndsWith("is up to date.", StringComparison.Ordinal))
                continue;

            notes.Add(line.Replace("`", "", StringComparison.Ordinal));
        }

        // :70-74
        if (match is not { } parsed)
            return null;

        return new HydraMergeNote
        {
            Link = link,
            Files = parsed.Files,
            Additions = parsed.Additions,
            Deletions = parsed.Deletions,
            Target = parsed.Target,
            Branch = parsed.Branch,
            Notes = new ValueList<string>(notes),
            Heads = new ValueList<Head>(heads),
        };
    }
}

/// <summary>:78-286 <c>struct HydraMergePopover: View</c>: a merge note as a small card.</summary>
public sealed class HydraMergePopover : Decorator
{
    private readonly HydraMergeNote note;
    private HydraMergeNote.Head? hoveredHead;
    private Popup? hoveredPopup;
    private bool didCopyLink;
    private readonly TextBlock copyLabel;
    private readonly StackPanel copyLabelPanel;
    private readonly SwiftImage copyCheckmark;
    private Border mark = null!;
    private TaskHandle? faviconTask;

    // :90-96 private var openTitle
    private string OpenTitle => note.Link.LinkForge switch
    {
        MergeRequestLink.Forge.Gitlab => "Open merge request",
        MergeRequestLink.Forge.Github or MergeRequestLink.Forge.Gitea => "Open pull request",
        _ => throw new ArgumentOutOfRangeException(),
    };

    // :83-87 @State favicon, didCopyLink, hoveredHead; :98-227 body
    public HydraMergePopover(HydraMergeNote note)
    {
        this.note = note;

        copyLabel = new TextBlock
        {
            Text = "Copy link",
            VerticalAlignment = VerticalAlignment.Center,
        };
        copyCheckmark = SwiftImage.SystemName("checkmark")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold))
            .ForegroundStyle(Chrome.Success)
            .Transition(SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity));
        copyLabelPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
        };
        copyLabelPanel.Children.Add(copyLabel);

        var host = note.Link.Url.HostComponent() ?? "";
        var rootStack = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var header = Header(host);
        rootStack.Children.Add(header);
        rootStack.Children.Add(Stats());

        if (note.Heads.Count != 0)
            rootStack.Children.Add(Heads());

        rootStack.Children.Add(BranchRow());

        foreach (var entry in note.Notes)
            rootStack.Children.Add(NoteRow(entry));

        rootStack.Children.Add(Actions());

        Child = new Border
        {
            Width = 380,
            Padding = new Thickness(18),
            Child = rootStack,
        };

        // :220-226. The cache is checked when the card appears; uncached marks load asynchronously.
        AttachedToVisualTree += (_, _) =>
        {
            if (FaviconCache.Cached(host) is { } cached)
            {
                ReplaceMark(cached);
                return;
            }

            faviconTask?.Cancel();
            faviconTask = MainActor.Spawn(async () =>
            {
                if (await FaviconCache.Image(host) is { } fetched && !SwiftTask.IsCancelled)
                    ReplaceMark(fetched);
            });
        };
        DetachedFromVisualTree += (_, _) =>
        {
            faviconTask?.Cancel();
            faviconTask = null;
        };
    }

    // :99-127 header row, favicon, title, subtitle and success seal
    private Control Header(string host)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };

        mark = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = MergeMark(),
        };
        Grid.SetColumn(mark, 0);
        row.Children.Add(mark);

        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(Text(
            "Merged",
            SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold),
            Chrome.PrimaryText));
        titleStack.Children.Add(Text(
            $"{note.Link.Label} on {host}",
            SwiftFont.System(SwiftFont.TextStyle.Subheadline),
            Chrome.SecondaryText));
        Grid.SetColumn(titleStack, 1);
        row.Children.Add(titleStack);

        var seal = SwiftImage.SystemName("checkmark.seal.fill")
            .Font(SwiftFont.System(20))
            .ForegroundStyle(Chrome.Success);
        Grid.SetColumn(seal, 2);
        row.Children.Add(seal);
        return row;
    }

    // :102-114 fallback merge mark
    private static Control MergeMark() =>
        SwiftImage.SystemName("arrow.triangle.merge")
            .Font(SwiftFont.System(18))
            .ForegroundStyle(Chrome.Accent);

    // :103-114 favicon image, aspect fit in its 22 by 22 clipped frame
    private void ReplaceMark(IImage image)
    {
        mark.Child = new Avalonia.Controls.Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
        };
    }

    // :128-132 the three stat tiles
    private Control Stats()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8,
        };
        grid.Children.Add(Stat(
            note.Files.ToString(CultureInfo.InvariantCulture),
            note.Files == 1 ? "file" : "files",
            null));
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(Stat(
            "+" + note.Additions.ToString(CultureInfo.InvariantCulture),
            "added",
            Chrome.Success));
        Grid.SetColumn(grid.Children[^1], 1);
        grid.Children.Add(Stat(
            "\U00002212" + note.Deletions.ToString(CultureInfo.InvariantCulture), // U+2212 MINUS SIGN
            "removed",
            Chrome.Danger));
        Grid.SetColumn(grid.Children[^1], 2);
        return grid;
    }

    // :258-271 shared stat tile
    private static Control Stat(string value, string label, SwiftColor? tint)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(Text(
            value,
            SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold).MonospacedDigit(),
            tint ?? Chrome.PrimaryText));
        stack.Children.Add(Text(
            label,
            SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Medium),
            Chrome.SecondaryText));

        return new Border
        {
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(10),
            Background = Chrome.Overlay(0.08).Brush(stack),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = stack,
        };
    }

    // :135-161 the optional heads row, first eight faces and the count of all heads
    private Control Heads()
    {
        var faces = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (var i = 0; i < Math.Min(8, note.Heads.Count); i++)
            faces.Children.Add(HeadFace(note.Heads[i]));

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(faces, 0);
        row.Children.Add(faces);
        var count = Text(
            note.Heads.Count == 1 ? "1 head landed" : $"{note.Heads.Count} heads landed",
            SwiftFont.System(SwiftFont.TextStyle.Subheadline),
            Chrome.SecondaryText);
        Grid.SetColumn(count, 1);
        row.Children.Add(count);
        return row;
    }

    // :140-154 one glyph, its tooltip and its own bottom-anchored head card
    private Control HeadFace(HydraMergeNote.Head head)
    {
        var glyph = new HydraGlyph(HydraRoster.Persona(head.Index), size: 26);
        ToolTip.SetTip(glyph, $"{head.Name}: {head.Task}");

        var popup = new Popup
        {
            PlacementTarget = glyph,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = HeadCard(head),
        };

        glyph.PointerEntered += (_, _) =>
        {
            if (hoveredPopup is { } previousPopup && previousPopup != popup)
                SetPopoverPresented(previousPopup, false);

            hoveredHead = head;
            hoveredPopup = popup;
            SetPopoverPresented(popup, true);
        };
        glyph.PointerExited += (_, _) =>
        {
            if (hoveredHead == head)
            {
                hoveredHead = null;
                hoveredPopup = null;
                SetPopoverPresented(popup, false);
            }
        };
        popup.Closed += (_, _) =>
        {
            if (hoveredHead == head)
            {
                hoveredHead = null;
                hoveredPopup = null;
            }
        };

        var wrapper = new StackPanel();
        wrapper.Children.Add(glyph);
        wrapper.Children.Add(popup);
        return wrapper;
    }

    // :149-154 route popover presentation through the shared presenter seam
    private static void SetPopoverPresented(Popup popup, bool presented) =>
        Presentation.Presenter.Current.SetPresented(popup, presented);

    // :230-256 the head's own card
    private static Control HeadCard(HydraMergeNote.Head head)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        stack.Children.Add(Text(
            head.Name,
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)));
        var task = Text(
            head.Task,
            SwiftFont.System(SwiftFont.TextStyle.Callout),
            Chrome.SecondaryText);
        task.MaxLines = 3;
        task.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(task);
        stack.Children.Add(new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Gray,
        });

        if (head.Files.Count == 0)
        {
            stack.Children.Add(Text(
                "No files of its own in this merge",
                SwiftFont.System(SwiftFont.TextStyle.Subheadline),
                Chrome.SecondaryText));
        }
        else
        {
            foreach (var path in head.Files)
            {
                var font = SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced);
                var file = Text(path, font, Chrome.SecondaryText);
                file.MaxLines = 1;
                InstallMiddleTruncation(file, path, font);
                stack.Children.Add(file);
            }
        }

        return new Border
        {
            Width = 320,
            Padding = new Thickness(12),
            Child = stack,
        };
    }

    // :162-170 branch-to-target row
    private Control BranchRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var branch = Chip(note.Branch, fills: true);
        ToolTip.SetTip(branch, note.Branch);
        branch.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(branch, 0);
        row.Children.Add(branch);

        var arrow = SwiftImage.SystemName("arrow.right")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Semibold))
            .ForegroundStyle(Chrome.SecondaryText);
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);

        var target = (Border)Chip(note.Target);
        target.HorizontalAlignment = HorizontalAlignment.Left;
        if (target.Child is TextBlock targetText)
            targetText.TextTrimming = TextTrimming.None;
        Grid.SetColumn(target, 2);
        row.Children.Add(target);
        return row;
    }

    // :275-285 monospaced branch/target chip
    private static Control Chip(string name, bool fills = false)
    {
        var font = SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced);
        var text = Text(name, font, Chrome.PrimaryText.Opacity(0.9));
        text.MaxLines = 1;
        if (fills)
            InstallMiddleTruncation(text, name, font);
        else
            text.TextTrimming = TextTrimming.CharacterEllipsis;

        return new Border
        {
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(12),
            Background = Chrome.Overlay(0.1).Brush(text),
            Child = text,
        };
    }

    // :249-250, :279-280. A native middle-truncation equivalent for a single-line label:
    // retain both ends and only shorten after the full text no longer fits.
    private static void InstallMiddleTruncation(TextBlock text, string value, SwiftFont font)
    {
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.SizeChanged += (_, _) =>
        {
            var width = text.Bounds.Width;
            if (width <= 0)
            {
                text.Text = value;
                return;
            }

            var full = new TextBlock { Text = value, MaxLines = 1 };
            font.Apply(full);
            full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (full.DesiredSize.Width <= width)
            {
                text.Text = value;
                return;
            }

            var characterCount = SwiftString.Count(value);
            var low = 0;
            var high = Math.Max(0, characterCount - 1);
            var best = "\u2026"; // U+2026 HORIZONTAL ELLIPSIS
            while (low <= high)
            {
                var kept = low + (high - low) / 2;
                var leadingCount = (kept + 1) / 2;
                var trailingCount = kept / 2;
                var candidate = SwiftString.Prefix(value, leadingCount) + "\u2026" + SwiftString.Suffix(value, trailingCount);
                var measured = new TextBlock { Text = candidate, MaxLines = 1 };
                font.Apply(measured);
                measured.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (measured.DesiredSize.Width <= width)
                {
                    best = candidate;
                    low = kept + 1;
                }
                else
                {
                    high = kept - 1;
                }
            }

            text.Text = best;
        };
    }

    // :171-181 remark row
    private static Control NoteRow(string entry)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var icon = SwiftImage.SystemName("info.circle")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Callout))
            .ForegroundStyle(Chrome.SecondaryText);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var text = Text(entry, SwiftFont.System(SwiftFont.TextStyle.Callout), Chrome.SecondaryText);
        text.TextWrapping = TextWrapping.Wrap;
        text.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    // :182-215 copy and open actions
    private Control Actions()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };

        var copy = new Button
        {
            Content = copyLabelPanel,
        };
        copy.Click += async (_, _) =>
        {
            if (didCopyLink)
                return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.ClearAsync();
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(note.Link.Url.AbsoluteString));
                await clipboard.SetDataAsync(data);
            }

            Transaction.WithAnimation(Animation.Snappy(duration: 0.22), () =>
            {
                didCopyLink = true;
                UpdateCopyLabel();
            });
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(SwiftTask.Seconds(1.4));
                Transaction.WithAnimation(Animation.Snappy(duration: 0.22), () =>
                {
                    didCopyLink = false;
                    UpdateCopyLabel();
                });
            });
        };
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);

        var open = new Button
        {
            Content = OpenActionLabel(),
            IsDefault = true,
        };
        open.Click += (_, _) =>
        {
            // :209 NSWorkspace.shared.open(note.link.url): the default browser, through the port's one seam for it
            // (Marrow.Services.Port.Workspace.Open, as TimelineRows' links), so tests never start a real browser.
            Marrow.Services.Port.Workspace.Open.Current(note.Link.Url);
        };
        Grid.SetColumn(open, 2);
        row.Children.Add(open);
        return row;
    }

    // :195-207 copy label with the transient checkmark
    private void UpdateCopyLabel()
    {
        copyLabelPanel.Children.Clear();
        if (didCopyLink)
            copyLabelPanel.Children.Add(copyCheckmark);
        copyLabel.Text = didCopyLink ? "Copied" : "Copy link";
        copyLabelPanel.Children.Add(copyLabel);
    }

    // :208-214 prominent open action label and symbol
    private Control OpenActionLabel()
    {
        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Children.Add(SwiftImage.SystemName("arrow.up.right")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Body))
            .ForegroundStyle(Chrome.PrimaryText));
        label.Children.Add(new TextBlock
        {
            Text = OpenTitle,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return label;
    }

    // :115-126, :232-243 shared text construction
    private static TextBlock Text(string value, SwiftFont font, SwiftColor? color = null)
    {
        var text = new TextBlock
        {
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
        };
        font.Apply(text);
        if (color is not null)
            text.Foreground = color.Brush(text);
        return text;
    }
}
