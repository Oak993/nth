using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

// Source: DroppyCode/App/Views/Chat/HydraReportsPopover.swift (B2 1fbf81e), :1-655.

/// <summary>:3-197 <c>struct HydraReportDigest</c>: a heads' report batch read as a digest.</summary>
public sealed record HydraReportDigest
{
    public string? Intro { get; init; } // :7
    public required ValueList<Head> Heads { get; init; } // :8

    /// <summary>:10-18 <c>struct Head: Identifiable</c>.</summary>
    public sealed record Head
    {
        public required string Id { get; init; } // :11
        public required string Name { get; init; } // :12
        public required string Task { get; init; } // :13
        public required Outcome Outcome { get; init; } // :14
        public string? Effort { get; init; } // :15
        public required Landing Landing { get; init; } // :16
        public required string Body { get; init; } // :17
    }

    /// <summary>:20-22 <c>enum Outcome</c>.</summary>
    public enum Outcome { Done, Failed, Stopped } // :21

    /// <summary>:24-30 <c>enum Landing</c>, a record union (R-ENUM-2).</summary>
    public abstract record Landing
    {
        private Landing() { }
        public sealed record Landed(ValueList<LandedFile> Value) : Landing; // :25
        public sealed record NotLanded(ValueList<LandedFile> Value, string? Error) : Landing; // :26
        public sealed record Nothing : Landing; // :27
        public sealed record Unfinished(string? CopyPath) : Landing; // :28
        public sealed record None : Landing; // :29
    }

    /// <summary>:32-38 <c>struct LandedFile: Identifiable</c>.</summary>
    public sealed record LandedFile
    {
        public required string Path { get; init; } // :33
        public required long Additions { get; init; } // :34
        public required long Deletions { get; init; } // :35
        public required bool HasConflicts { get; init; } // :36
        public string Id => Path; // :37
    }

    // :44-76
    public static HydraReportDigest Parse(string text)
    {
        var introLines = new List<string>();
        var sections = new List<(string Header, List<string> Lines)>();
        foreach (var line in text.Split(new[] { "\n" }, StringSplitOptions.None))
        {
            var trimmed = FoundationText.Trim(line);
            if (SwiftString.HasPrefix(trimmed, "## ")
                && IsHeadHeader(SwiftString.DropFirst(trimmed, 3)))
                sections.Add((SwiftString.DropFirst(trimmed, 3), []));
            else if (sections.Count == 0)
                introLines.Add(line);
            else
                sections[^1].Lines.Add(line);
        }

        var intro = string.Join("\n", introLines).Trimmed();
        var heads = new List<Head>();
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var header = ParseHeader(section.Header);
            var rest = section.Lines;
            while (rest.Count > 0 && rest[0].Trimmed().Length == 0)
                rest.RemoveAt(0);

            Landing landing = new Landing.None();
            if (rest.Count > 0 && LandingPrefix(rest[0]))
            {
                landing = ParseLanding(rest[0].Trimmed());
                rest.RemoveAt(0);
            }

            var body = string.Join("\n", rest).Trimmed();
            if (index == sections.Count - 1)
                body = WithoutLeadInstructions(body);

            heads.Add(new Head
            {
                Id = header.Name,
                Name = header.Name,
                Task = header.Task,
                Outcome = header.Outcome,
                Effort = header.Effort,
                Landing = landing,
                Body = body,
            });
        }

        return new HydraReportDigest
        {
            Intro = intro.Length == 0 ? null : intro,
            Heads = new ValueList<Head>(heads),
        };
    }

    // :80-84
    private static bool IsHeadHeader(string header)
    {
        var colon = header.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 && HydraRoster.Index(FoundationText.Trim(header[..colon])) is not null;
    }

    // :88-119
    private static (string Name, string Task, Outcome Outcome, string? Effort) ParseHeader(string header)
    {
        var trimmed = FoundationText.Trim(header);
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var name = colon >= 0 ? FoundationText.Trim(trimmed[..colon]) : trimmed;
        var rest = colon >= 0 ? FoundationText.Trim(trimmed[(colon + 1)..]) : "";
        var outcome = Outcome.Done;
        var efforts = new List<string>();

        while (rest.EndsWith(')'))
        {
            var open = rest.LastIndexOf('(');
            if (open < 0)
                break;

            var group = rest[(open + 1)..^1];
            var lower = SwiftString.Lowercased(group);
            var isEffortWord = lower is "low" or "medium" or "high" or "xhigh" or "max" or "minimal" or "fast" or "default";
            var isMeta = lower == "failed"
                || SwiftString.HasPrefix(lower, "stopped")
                || Regex.IsMatch(group, @"\p{Nd}", RegexOptions.CultureInvariant)
                || isEffortWord;
            if (!isMeta)
                break;

            rest = FoundationText.Trim(rest[..open]);
            if (lower == "failed")
                outcome = Outcome.Failed;
            else if (SwiftString.HasPrefix(lower, "stopped"))
                outcome = Outcome.Stopped;
            else if (group.Length > 0)
                efforts.Insert(0, group.Replace(", ", " \U000000B7 ", StringComparison.Ordinal));
        }

        return (name, rest, outcome, efforts.Count == 0 ? null : string.Join(" \U000000B7 ", efforts));
    }

    // :121-127
    private static bool LandingPrefix(string line)
    {
        var trimmed = FoundationText.Trim(line);
        return SwiftString.HasPrefix(trimmed, "Landed in your checkout:")
            || SwiftString.HasPrefix(trimmed, "Did not land")
            || SwiftString.HasPrefix(trimmed, "Changed nothing.")
            || SwiftString.HasPrefix(trimmed, "Nothing landed");
    }

    // :131-155
    private static Landing ParseLanding(string line)
    {
        if (SwiftString.HasPrefix(line, "Changed nothing."))
            return new Landing.Nothing();

        if (SwiftString.HasPrefix(line, "Nothing landed"))
        {
            var marker = line.IndexOf("in its copy at ", StringComparison.Ordinal);
            if (marker < 0)
                return new Landing.Unfinished(null);
            var path = line[(marker + "in its copy at ".Length)..].Trimmed();
            if (path.EndsWith('.'))
                path = SwiftString.DropLast(path, 1);
            return new Landing.Unfinished(path.Length == 0 ? null : path);
        }

        if (SwiftString.HasPrefix(line, "Landed in your checkout:"))
            return new Landing.Landed(new ValueList<LandedFile>(
                ParseFiles(SwiftString.DropFirst(line, "Landed in your checkout:".Length))));

        if (SwiftString.HasPrefix(line, "Did not land"))
        {
            var rest = SwiftString.DropFirst(line, "Did not land".Length);
            string? error = null;
            if (SwiftString.HasPrefix(rest, " ("))
            {
                var close = rest.IndexOf(')', StringComparison.Ordinal);
                if (close >= 0)
                {
                    error = FoundationText.Trim(rest[2..close]);
                    rest = rest[(close + 1)..];
                }
            }
            if (SwiftString.HasPrefix(rest, ":"))
                rest = SwiftString.DropFirst(rest, 1);
            return new Landing.NotLanded(new ValueList<LandedFile>(ParseFiles(rest)),
                error is { Length: > 0 } ? error : null);
        }

        return new Landing.None();
    }

    // :157-169
    private static List<LandedFile> ParseFiles(string raw)
    {
        var text = raw.Trimmed();
        if (text.EndsWith('.'))
            text = SwiftString.DropLast(text, 1);

        foreach (var suffix in new[]
        {
            new Regex(@"; \d+ build-output files? (was|were) left out\z", RegexOptions.CultureInvariant),
            new Regex(@", and \d+ more\z", RegexOptions.CultureInvariant),
        })
        {
            var match = suffix.Match(text);
            if (match.Success)
                text = text[..match.Index];
        }

        var files = new List<LandedFile>();
        foreach (var item in text.Split(new[] { ", " }, StringSplitOptions.None))
            if (MatchFile(FoundationText.Trim(item)) is { } file)
                files.Add(file);
        return files;
    }

    // :171-181
    private static LandedFile? MatchFile(string item)
    {
        var match = Regex.Match(item, @"\A(\S+) \(\+(\d+) [\u2212\-](\d+)\)( with conflicts)?\z", RegexOptions.CultureInvariant);
        if (!match.Success
            || SwiftNumber.ParseInt(match.Groups[2].Value) is not { } additions
            || SwiftNumber.ParseInt(match.Groups[3].Value) is not { } deletions)
            return null;

        return new LandedFile
        {
            Path = match.Groups[1].Value,
            Additions = additions,
            Deletions = deletions,
            HasConflicts = match.Groups[4].Success,
        };
    }

    // :185-189
    private static readonly string[] LeadPrefixes =
    [
        "The changes listed above", "The heads worked in your checkout", "A file listed with conflicts",
        "Do not check any of this", "Do not wait for", "A head that failed", "A head the user stopped",
        "The user queued", "The user sent",
    ];

    // :191-196
    private static string WithoutLeadInstructions(string body)
    {
        var boundary = body.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (boundary < 0)
            return body;
        var tail = body[(boundary + 2)..].Trimmed();
        return LeadPrefixes.Any(prefix => SwiftString.HasPrefix(tail, prefix))
            ? body[..boundary].Trimmed()
            : body;
    }
}

/// <summary>:199-655 <c>struct HydraReportsPopover: View</c>: the digest in the popover its pill opens.</summary>
public sealed class HydraReportsPopover : Decorator
{
    private const double HydraReportsPopoverWidth = 460; // :218 Swift: width
    private readonly string title;
    private readonly string text;
    private readonly IReadOnlyList<HydraPersona> personas;
    private readonly HydraReportDigest digest;
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ValueList<MarkdownBlock>> blocks = new(StringComparer.Ordinal);
    private TaskHandle? warmingTask;

    private HydraReportDigest.Head? Single =>
        digest.Heads.Count == 1 ? digest.Heads[0] : null; // :212

    // :220-235
    public HydraReportsPopover(string title, string text, IReadOnlyList<HydraPersona> personas)
    {
        this.title = title;
        this.text = text;
        this.personas = personas;
        digest = HydraReportDigest.Parse(text);
        Width = HydraReportsPopoverWidth;

        if (digest.Heads.Count == 1)
        {
            var head = digest.Heads[0];
            expanded.Add(head.Name);
            blocks[head.Name] = MarkdownView.Blocks(head.Body);
        }

        Child = BuildBody();
        AttachedToVisualTree += (_, _) => StartWarming();
        DetachedFromVisualTree += (_, _) => StopWarming();
    }

    // :237-266
    private Control BuildBody()
    {
        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        root.Children.Add(BuildHeader());
        root.Children.Add(new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Chrome.SecondaryText.Brush(root),
        });

        var collapsible = digest.Heads.Count > 1;
        var cards = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        foreach (var head in digest.Heads)
            cards.Children.Add(BuildHeadCard(head, collapsible));

        var cardContent = new Decorator
        {
            Child = cards,
            Margin = collapsible ? new Thickness(10) : new Thickness(0),
        };
        cardContent.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(new PopoverScroll(() => cardContent, maxHeight: 520, width: HydraReportsPopoverWidth));
        root.Width = HydraReportsPopoverWidth;
        return root;
    }

    // :253-265
    private void StartWarming()
    {
        var missing = digest.Heads.Where(head => !blocks.ContainsKey(head.Name)).ToArray();
        if (missing.Length == 0)
            return;

        warmingTask?.Cancel();
        warmingTask = MainActor.Spawn(async () =>
        {
            try
            {
                await MarkdownView.Warm(missing.Select(head => head.Body));
            }
            catch (Exception exception) when (SwiftTry.IsThrownError(exception))
            {
            }

            if (SwiftTask.IsCancelled)
                return;

            foreach (var head in missing)
                blocks[head.Name] = MarkdownView.Blocks(head.Body);
            Child = BuildBody();
        });
    }

    // :253-265
    private void StopWarming()
    {
        warmingTask?.Cancel();
        warmingTask = null;
    }

    // :273-316
    private Control BuildHeader()
    {
        if (Single is { } head)
            return BuildSingleHeader(head);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var glyphs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -6 };
        foreach (var persona in personas.Take(9))
            glyphs.Children.Add(new HydraGlyph(persona, 22));

        if (personas.Count > 9)
        {
            var overflow = MakeText("+" + (personas.Count - 9).ToString(System.Globalization.CultureInfo.InvariantCulture),
                SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold), Chrome.SecondaryText);
            glyphs.Children.Add(new Border
            {
                Child = overflow,
                Padding = new Thickness(7, 3),
                Background = Chrome.Overlay(0.1).Brush(glyphs),
                CornerRadius = new CornerRadius(999),
                Margin = new Thickness(10, 0, 0, 0),
            });
        }
        row.Children.Add(glyphs);

        var textColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        textColumn.Children.Add(MakeText(CleanedTitle(title, personas.Select(persona => persona.Name).ToArray()),
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText, 2));
        textColumn.Children.Add(MakeText(TotalsLine(digest),
            SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit(), Chrome.SecondaryText));
        if (digest.Intro is { } intro && StillAtWorkSentence(intro) is { } sentence)
            textColumn.Children.Add(MakeText(sentence, SwiftFont.Subheadline, Chrome.SecondaryText, 2));

        row.Children.Add(textColumn);
        row.Children.Add(new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Stretch });
        row.Margin = new Thickness(12, 10);
        return row;
    }

    // :318-356
    private Control BuildSingleHeader(HydraReportDigest.Head head)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new HydraGlyph(PersonaFor(head), 24, status: Status(head.Outcome)));

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        var firstLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        firstLine.Children.Add(MakeText(head.Name,
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText));
        firstLine.Children.Add(MakeText(OutcomeCaption(head),
            SwiftFont.Subheadline.MonospacedDigit(), OutcomeColor(head.Outcome)));
        if (FilesLine(head) is { } fileLine)
            firstLine.Children.Add(MakeText("\U000000B7 " + fileLine,
                SwiftFont.Subheadline.MonospacedDigit(), Chrome.SecondaryText, 1));
        column.Children.Add(firstLine);

        var task = WithoutNamePrefix(CleanedTitle(head.Task, [head.Name]), head.Name);
        column.Children.Add(MakeText(task.Length == 0 ? CleanedTitle(title, [head.Name]) : task,
            SwiftFont.Callout, Chrome.PrimaryText, 3));
        if (digest.Intro is { } intro && StillAtWorkSentence(intro) is { } sentence)
            column.Children.Add(MakeText(sentence, SwiftFont.Subheadline, Chrome.SecondaryText, 2));

        row.Children.Add(column);
        row.Children.Add(new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Stretch });
        row.Margin = new Thickness(12, 10);
        return row;
    }

    // :358-367
    private static string OutcomeCaption(HydraReportDigest.Head head)
    {
        var caption = head.Outcome switch
        {
            HydraReportDigest.Outcome.Done => "Finished",
            HydraReportDigest.Outcome.Failed => "Failed",
            HydraReportDigest.Outcome.Stopped => "Stopped",
            _ => throw new UnreachableException(),
        };
        return head.Effort is { Length: > 0 } effort ? caption + " \U000000B7 " + effort : caption;
    }

    // :369-375
    private static SwiftColor OutcomeColor(HydraReportDigest.Outcome outcome) => outcome switch
    {
        HydraReportDigest.Outcome.Failed => Chrome.Danger,
        HydraReportDigest.Outcome.Stopped => Chrome.Warning,
        HydraReportDigest.Outcome.Done => Chrome.SecondaryText,
        _ => throw new UnreachableException(),
    };

    // :377-389
    private static string? FilesLine(HydraReportDigest.Head head)
    {
        var files = FilesFor(head.Landing);
        if (files.Count == 0)
            return null;
        long additions = 0, deletions = 0;
        foreach (var file in files)
        {
            additions = checked(additions + file.Additions);
            deletions = checked(deletions + file.Deletions);
        }
        return files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (files.Count == 1 ? " file" : " files")
            + " \U000000B7 +" + additions.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " \U00002212" + deletions.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // :393-405
    public static string CleanedTitle(string title, IReadOnlyList<string> names)
    {
        var cleaned = title;
        foreach (var name in names)
        {
            if (name.Length == 0)
                continue;
            var prefixes = new[] { name + ": ", name + " " };
            string? Prefix(string value) => prefixes.FirstOrDefault(prefix => SwiftString.HasPrefix(value, prefix));
            while (Prefix(cleaned) is { } prefix
                && Prefix(SwiftString.DropFirst(cleaned, SwiftString.Count(prefix))) is not null)
                cleaned = SwiftString.DropFirst(cleaned, SwiftString.Count(prefix));
        }
        return cleaned;
    }

    // :408-412
    private static string WithoutNamePrefix(string value, string name)
    {
        var prefix = name + ": ";
        return name.Length > 0 && SwiftString.HasPrefix(value, prefix)
            ? FoundationText.Trim(SwiftString.DropFirst(value, SwiftString.Count(prefix)))
            : value;
    }

    // :415-432
    private static string TotalsLine(HydraReportDigest digest)
    {
        var files = new List<HydraReportDigest.LandedFile>();
        foreach (var head in digest.Heads)
            files.AddRange(FilesFor(head.Landing));

        var count = digest.Heads.Count;
        var line = count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (count == 1 ? " head" : " heads");
        if (files.Count == 0)
            return line;

        long additions = 0, deletions = 0;
        foreach (var file in files)
        {
            additions = checked(additions + file.Additions);
            deletions = checked(deletions + file.Deletions);
        }
        return line + " \U000000B7 " + files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (files.Count == 1 ? " file" : " files")
            + " \U000000B7 +" + additions.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " \U00002212" + deletions.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // :435-445
    private static string? StillAtWorkSentence(string intro)
    {
        var match = intro.IndexOf("still at work", StringComparison.Ordinal);
        if (match < 0)
            return null;
        var lastPeriod = intro.LastIndexOf('.', match);
        var sentence = intro[(lastPeriod >= 0 ? lastPeriod + 1 : 0)..].Trimmed();
        return sentence.Length == 0 ? null : sentence;
    }

    // :449-453
    private HydraPersona PersonaFor(HydraReportDigest.Head head)
    {
        foreach (var persona in personas)
            if (SwiftString.Equal(persona.Name, head.Name))
                return persona;
        return HydraRoster.Persona(head.Name) ?? HydraRoster.Persona(0);
    }

    // :455-461
    private static HydraHeadInfo.Status Status(HydraReportDigest.Outcome outcome) => outcome switch
    {
        HydraReportDigest.Outcome.Done => HydraHeadInfo.Status.Completed,
        HydraReportDigest.Outcome.Failed => HydraHeadInfo.Status.Failed,
        HydraReportDigest.Outcome.Stopped => HydraHeadInfo.Status.Stopped,
        _ => throw new UnreachableException(),
    };

    // :465-516
    private Control BuildHeadCard(HydraReportDigest.Head head, bool collapsible)
    {
        var card = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        if (collapsible)
        {
            if (HasReport(head))
            {
                var button = new Button
                {
                    Content = BuildNameRow(head, true),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                button.Click += (_, _) =>
                {
                    if (!expanded.Add(head.Name))
                        expanded.Remove(head.Name);
                    Child = BuildBody();
                };
                card.Children.Add(button);
            }
            else
                card.Children.Add(BuildNameRow(head, false));

            var task = WithoutNamePrefix(CleanedTitle(head.Task, [head.Name]), head.Name);
            if (task.Length > 0)
                card.Children.Add(MakeText(task, SwiftFont.Callout, Chrome.PrimaryText, 2));
        }

        card.Children.Add(BuildLandingRows(head.Landing));
        if (!collapsible || expanded.Contains(head.Name))
            card.Children.Add(BuildReportBody(head));

        var panel = new Border { Child = card, Padding = new Thickness(12) };
        if (collapsible)
        {
            panel.CornerRadius = new CornerRadius(10);
            panel.Background = Chrome.Overlay(0.05).Brush(panel);
        }
        return panel;
    }

    // :519-546
    private Control BuildNameRow(HydraReportDigest.Head head, bool showChevron)
    {
        var persona = PersonaFor(head);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameAndOutcome = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameAndOutcome.Children.Add(new HydraGlyph(persona, 20, status: Status(head.Outcome)));
        nameAndOutcome.Children.Add(MakeText(head.Name,
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold),
            new SwiftColor(persona.Color.Red, persona.Color.Green, persona.Color.Blue, persona.Color.Alpha)));
        if (head.Outcome == HydraReportDigest.Outcome.Failed)
            nameAndOutcome.Children.Add(MakeText("Failed", SwiftFont.Subheadline, Chrome.Danger));
        else if (head.Outcome == HydraReportDigest.Outcome.Stopped)
            nameAndOutcome.Children.Add(MakeText("Stopped", SwiftFont.Subheadline, Chrome.Warning));
        Grid.SetColumn(nameAndOutcome, 0);
        row.Children.Add(nameAndOutcome);

        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (head.Effort is { Length: > 0 } effort)
            trailing.Children.Add(MakeText(effort, SwiftFont.Subheadline.MonospacedDigit(), Chrome.SecondaryText, 1));
        if (showChevron)
        {
            var chevron = SwiftImage.SystemName("chevron.right");
            chevron.Width = 10;
            chevron.Height = 10;
            SwiftImage.SetFont(chevron, SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold));
            chevron.Foreground = Chrome.SecondaryText.Brush(chevron);
            chevron.RenderTransform = expanded.Contains(head.Name) ? new RotateTransform(90) : new RotateTransform(0);
            trailing.Children.Add(chevron);
        }
        Grid.SetColumn(trailing, 2);
        row.Children.Add(trailing);
        return row;
    }

    // :548-550
    private static bool HasReport(HydraReportDigest.Head head) =>
        head.Body.Length != 0 && head.Body != "No report.";

    // :553-586
    private Control BuildLandingRows(HydraReportDigest.Landing landing)
    {
        switch (landing)
        {
            case HydraReportDigest.Landing.Landed landed:
                return BuildFileRows(landed.Value);
            case HydraReportDigest.Landing.NotLanded notLanded:
            {
                var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
                column.Children.Add(MakeText(notLanded.Error is { } error ? "Did not land: " + error : "Did not land",
                    SwiftFont.Subheadline, Chrome.Warning));
                column.Children.Add(BuildFileRows(notLanded.Value));
                return column;
            }
            case HydraReportDigest.Landing.Nothing:
                return MakeText("Changed nothing.", SwiftFont.Subheadline, Chrome.SecondaryText);
            case HydraReportDigest.Landing.Unfinished unfinished:
            {
                var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
                column.Children.Add(MakeText("Nothing landed", SwiftFont.Subheadline, Chrome.Warning));
                if (unfinished.CopyPath is { } path)
                {
                    var font = SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced);
                    column.Children.Add(new MiddleTruncatingText
                    {
                        FullText = path,
                        FontFamily = font.Resolve().FontFamily,
                        FontSize = font.PointSize,
                        Foreground = Chrome.SecondaryText.Brush(column),
                    });
                }
                return column;
            }
            case HydraReportDigest.Landing.None:
                return new Border();
            default:
                throw new UnreachableException();
        }
    }

    // :591-616
    private Control BuildFileRows(IReadOnlyList<HydraReportDigest.LandedFile> files)
    {
        if (files.Count == 0)
            return new Border { IsVisible = false };

        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        foreach (var file in files)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,8,Auto"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var path = (TextBlock)BuildFilePathText(file);
            path.Margin = new Thickness(0);
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.MaxLines = 1;
            var pathRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { path, new Border { Width = 8 } },
            };
            Grid.SetColumn(pathRow, 0);
            row.Children.Add(pathRow);

            var diff = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            diff.Children.Add(MakeText("+" + file.Additions.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SwiftFont.Subheadline.MonospacedDigit(), Chrome.Success));
            diff.Children.Add(MakeText("\U00002212" + file.Deletions.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SwiftFont.Subheadline.MonospacedDigit(), Chrome.Danger));
            Grid.SetColumn(diff, 2);
            row.Children.Add(diff);

            ToolTip.SetTip(row, file.HasConflicts ? file.Path + " (with conflicts)" : file.Path);
            ToolTip.SetTip(pathRow, file.HasConflicts ? file.Path + " (with conflicts)" : file.Path);
            rows.Children.Add(row);
        }

        return new Border
        {
            Child = rows,
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(6),
            Background = Chrome.Overlay(0.05).Brush(rows),
        };
    }

    // :618-626
    private Control BuildFilePathText(HydraReportDigest.LandedFile file)
    {
        var slash = file.Path.LastIndexOf('/');
        var directory = slash >= 0 ? file.Path[..(slash + 1)] : "";
        var filename = slash >= 0 ? file.Path[(slash + 1)..] : file.Path;
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        new StyledText(directory).ForegroundColor(Chrome.SecondaryText).AppendTo(text.Inlines!, text);
        new StyledText(filename).ForegroundColor(file.HasConflicts ? Chrome.Warning : Chrome.PrimaryText)
            .AppendTo(text.Inlines!, text);
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced).Apply(text);
        return text;
    }

    // :629-654
    private Control BuildReportBody(HydraReportDigest.Head head)
    {
        if (!HasReport(head))
            return MakeText("No report.", SwiftFont.Subheadline, Chrome.SecondaryText);

        if (!blocks.TryGetValue(head.Name, out var headBlocks))
            return new ProgressBar
            {
                IsIndeterminate = true,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 8),
            };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
        MarkdownView.SetMarkdownPointSize(stack, 12);
        MarkdownView.SetMarkdownDimmed(stack, false);
        MarkdownView.SetMarkdownBlockSelection(stack, true);
        foreach (var block in headBlocks)
        {
            var view = new MarkdownBlockView(block);
            MarkdownView.SetMarkdownPointSize(view, 12);
            MarkdownView.SetMarkdownDimmed(view, false);
            MarkdownView.SetMarkdownBlockSelection(view, true);
            stack.Children.Add(view);
        }
        return stack;
    }

    // :277-389, :465-626
    private static TextBlock MakeText(string value, SwiftFont font, SwiftColor color, int? maxLines = null)
    {
        var textBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = maxLines ?? 0,
        };
        font.Apply(textBlock);
        textBlock.Foreground = color.Brush(textBlock);
        return textBlock;
    }

    // :377-389, :415-432
    private static IReadOnlyList<HydraReportDigest.LandedFile> FilesFor(HydraReportDigest.Landing landing) =>
        landing switch
        {
            HydraReportDigest.Landing.Landed landed => landed.Value,
            HydraReportDigest.Landing.NotLanded notLanded => notLanded.Value,
            HydraReportDigest.Landing.Nothing => Array.Empty<HydraReportDigest.LandedFile>(),
            HydraReportDigest.Landing.Unfinished => Array.Empty<HydraReportDigest.LandedFile>(),
            HydraReportDigest.Landing.None => Array.Empty<HydraReportDigest.LandedFile>(),
            _ => throw new UnreachableException(),
        };
}
