// PORT-ONLY: Dextra AS-8 and AS-17 (DESIGN.md rev 5, 2.1 "Sidebar": "Mission chats are the lead chats the Assistant starts. They appear in a collapsed 'Missions' group under the assistant that
// owns them"; the canvas's note under the sidebar, work/design/assistant/index.html line 790; ED-NEW-dextra-as8-8, ED-NEW-dextra-as17-10). No Swift twin.
// The sidebar's "Missions" group of one assistant: a disclosure line with its count, then one line for each task's lead chat (live ones first, then today's finished ones, "watching its
// computer"). Dextra's row holds one for the tasks it owns itself; each Astur's row in the tree holds one for its own. The group draws itself from plain values (TeamChat) so a tick that
// changes nothing draws nothing; it is shown only while there is a chat to list, and it is collapsed until opened.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>One lead chat of a task, as a Missions group lists it: what its line says, how its mark stands and what its hover shows.</summary>
public sealed record TeamChat(Guid Id, Guid Thread, string Title, MissionState State, string Hover)
{
    public ArmillaState Mark => ArmillaPoses.For(State);

    public ArmillaGlyph Glyph => ArmillaPoses.GlyphFor(State);

    /// <summary>What a screen reader hears for the line.</summary>
    public string Reading => Title + ", " + MissionStates.Label(State);

    /// <summary>The chat of a task that has one (<see cref="MissionJob.LeadThread"/>).</summary>
    public static TeamChat Of(MissionJob job) =>
        new(job.Id, job.LeadThread!.Value, job.Title, job.State, $"{job.Owner}: {(job.State == MissionState.Done ? job.Detail : job.Activity ?? job.Detail)}");
}

public static class SidebarChats
{
    /// <summary>
    /// The chats a Missions group lists: the tasks that have a lead chat, the live ones first and then today's finished ones (a finished task of an earlier day is not listed), newest first
    /// in each kind. One rule for Dextra's group and every Astur's.
    /// </summary>
    public static IReadOnlyList<TeamChat> Of(IEnumerable<MissionJob> jobs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var today = TaskWords.Local(now).Date;
        return [.. jobs
            .Where(j => j.LeadThread is not null && (j.Group != BoardGroup.DoneToday || j.Ended is { } ended && TaskWords.Local(ended).Date == today))
            .OrderBy(j => j.Group == BoardGroup.DoneToday ? 1 : 0)
            .ThenByDescending(j => j.Created)
            .Select(TeamChat.Of)];
    }
}

public sealed class MissionChatsGroup
{
    private readonly Action<Guid>? openChat;
    private readonly Func<bool>? collapsed;
    private readonly Action<bool>? setCollapsed;
    private string ownerName = "";
    private readonly StackPanel lines = new() { Spacing = 1 };
    private readonly RevealBox reveal = new();
    private readonly Dictionary<Guid, (Button Button, ArmillaMark Mark, TextBlock Title)> byChat = [];
    private int count;
    private bool calm;

    /// <param name="openChat">What a line does: opens the task's lead chat.</param>
    /// <param name="collapsed">Whether the group is folded, read when it is made; null: folded until opened.</param>
    /// <param name="setCollapsed">Told when the owner opens or folds it (the choice is remembered by whoever gave it).</param>
    /// <param name="indent">How far the disclosure line is set in from the left edge of the group.</param>
    /// <param name="lineIndent">How far the list of lines is set in.</param>
    public MissionChatsGroup(Action<Guid>? openChat = null, Func<bool>? collapsed = null, Action<bool>? setCollapsed = null, double indent = -1, double lineIndent = 8)
    {
        this.openChat = openChat;
        this.collapsed = collapsed;
        this.setCollapsed = setCollapsed;
        Chevron = TaskLook.Chevron();
        Label = new TextBlock { Text = "Missions", VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12, SwiftFont.Weight.Medium).MonospacedDigit().Apply(Label);
        Label.Foreground = Chrome.SecondaryText.Brush(Label);
        var toggleFace = new Border
        {
            Height = 24,
            Padding = new Thickness(indent < 0 ? Chrome.RowHorizontalPadding + 4 : indent, 0, Chrome.RowHorizontalPadding, 0),
            Background = Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Chevron, Label } },
        };
        Toggle = new DisclosureButton { Content = toggleFace, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch };
        Toggle.KeyboardFocus(Chrome.RowCornerRadius);
        Toggle.Click += (_, _) =>
        {
            var open = !Toggle.IsExpanded;
            this.setCollapsed?.Invoke(!open);
            SetOpen(open, TaskLook.Still(Toggle));
        };
        reveal.Child = new StackPanel { Margin = new Thickness(lineIndent, 1, 0, 2), Children = { lines } };
        Root = new StackPanel { Spacing = 0, Children = { Toggle, reveal } };
        SetOpen(!(this.collapsed?.Invoke() ?? true), still: true);
        NameToggle();
    }

    /// <summary>The assistant the group belongs to when it is not Dextra's own, for the toggle's name ("Show Store's missions, 2"); empty for Dextra's.</summary>
    public string OwnerName
    {
        get => ownerName;
        set
        {
            if (ownerName == value) return;
            ownerName = value ?? "";
            NameToggle();
        }
    }

    /// <summary>The group: the disclosure line, then the lines.</summary>
    public StackPanel Root { get; }

    public DisclosureButton Toggle { get; }

    public TextBlock Label { get; }

    internal Avalonia.Controls.Shapes.Path Chevron { get; }

    public bool IsOpen => Toggle.IsExpanded;

    /// <summary>How many chats it lists now.</summary>
    public int Count => count;

    /// <summary>The lead-chat lines, in the order they are listed.</summary>
    public IReadOnlyList<Button> Buttons => [.. lines.Children.OfType<Button>()];

    /// <summary>How many of the lines' marks move now (zero while the group is folded): what it costs the window's motion budget.</summary>
    public int MovingMarks => IsOpen ? byChat.Values.Count(l => l.Mark.Moves) : 0;

    /// <summary>The motion budget stilled the list this group is in: its marks show their still poses.</summary>
    public bool Calm
    {
        get => calm;
        set
        {
            calm = value;
            foreach (var line in byChat.Values) line.Mark.Calm = value;
        }
    }

    /// <summary>The controls the Up and Down keys walk: the disclosure line, then the lines while it is open.</summary>
    public IEnumerable<Control> Stops() => Count == 0 ? [] : IsOpen ? [Toggle, .. Buttons] : [Toggle];

    /// <summary>Lists these chats; a line that stays is updated in place and a line that goes is let go.</summary>
    public void Show(IReadOnlyList<TeamChat> chats)
    {
        ArgumentNullException.ThrowIfNull(chats);
        count = chats.Count;
        Label.Text = chats.Count == 0 ? "Missions" : "Missions  " + chats.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        NameToggle();
        var keep = chats.Select(c => c.Id).ToHashSet();
        foreach (var gone in byChat.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            lines.Children.Remove(byChat[gone].Button);
            byChat.Remove(gone);
        }
        for (var i = 0; i < chats.Count; i++)
        {
            var chat = chats[i];
            if (!byChat.TryGetValue(chat.Id, out var line)) byChat[chat.Id] = line = Line(chat);
            line.Mark.State = chat.Mark;
            line.Mark.Glyph = chat.Glyph;
            line.Mark.Activity = chat.Hover;
            line.Title.Text = chat.Title;
            AutomationProperties.SetName(line.Button, chat.Reading);
            line.Mark.Calm = calm;
            if (i < lines.Children.Count && ReferenceEquals(lines.Children[i], line.Button)) continue;
            lines.Children.Remove(line.Button);
            lines.Children.Insert(i, line.Button);
        }
    }

    /// <summary>Opens or folds the group (the spring of the chevron and the reveal are none with Reduce motion or <paramref name="still"/>).</summary>
    public void SetOpen(bool open, bool still)
    {
        Toggle.IsExpanded = open;
        NameToggle();
        reveal.Open(open, still);
        TaskLook.Turn(Chevron, open, still);
    }

    // the toggle's name says what pressing it does
    private void NameToggle() => AutomationProperties.SetName(Toggle,
        (Toggle.IsExpanded ? "Hide " : "Show ") + (ownerName.Length == 0 ? "missions" : ownerName + "'s missions") + ", " + count.ToString(System.Globalization.CultureInfo.CurrentCulture));

    private (Button Button, ArmillaMark Mark, TextBlock Title) Line(TeamChat chat)
    {
        var mark = TaskMark.Make();
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        var title = new TextBlock { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5).Apply(title);
        title.Foreground = Chrome.PrimaryText.Opacity(.92).Brush(title);
        var face = new Border
        {
            Height = Chrome.RowHeight,
            Padding = new Thickness(Chrome.RowHorizontalPadding, 0),
            Background = Brushes.Transparent,
            // a grid, not a row of two: the title is given what is left beside the mark, so a long one ends in an ellipsis instead of running past the sidebar
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { mark, title } },
        };
        Grid.SetColumn(title, 1);
        face.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        var hot = Chrome.Overlay(.06).Brush(face);
        var thread = chat.Thread;
        var button = PlainControls.Button(face, () => openChat?.Invoke(thread));
        button.KeyboardFocus(Chrome.RowCornerRadius);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = Brushes.Transparent;
        return (button, mark, title);
    }
}
