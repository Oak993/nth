// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.2 "Home view", 2.5 Armilla (40 px in the header, 160 px in the empty thread),
// 12.6 and 12.6b (the header's Pause all / Resume all toggle and Stop all, which never asks), 16.2 (the home renders as a full view
// while the pane workspace is absent); the canvas's .hm, .hhead, .hgrid, renderLine and the live region, work/design/assistant/
// index.html lines 735-800, 2272-2290; ED-NEW-dextra-as8-7). No Swift twin.
// The Assistant home in the main area: the header (Armilla 40, the name, one state line, the voice button when the voice unit
// gives one, the Pause all / Resume all toggle, Stop all with its square and label, and a menu), the stop strip and the Result
// list under it, then the conversation (Armilla 160 and a caption while the home thread is empty, the chips, the home chat) beside
// the Board pane, stacked under it below 900 px. A polite live region announces each task's change ("Paused: ⟨task⟩") and every
// control's answer. The window's motion budget gives the Board first claim, then the Result list, then the header.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Safety;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>The home's menu: Reserves, Activity log and Settings always; Duties and Memory when their units give them.</summary>
public sealed record HomeMenu(Action? Reserves = null, Action? ActivityLog = null, Action? Settings = null, Action? Duties = null, Action? Memory = null);

/// <summary>Everything the home reads and calls; the app binds the controller's, a test its fakes.</summary>
public sealed record AssistantHomeParts(IMissionJobs Jobs, IDecisionSource Decisions, IGlobalControls Controls)
{
    public string Name { get; init; } = AssistantNames.Assistant;

    public DecisionContext? DecisionContext { get; init; }

    public Func<Action<IReadOnlyList<FeedEvent>>, IDisposable>? Feed { get; init; }

    public Func<FeedEvent, string>? FeedSource { get; init; }

    public Action<FeedEvent>? OpenFeed { get; init; }

    public Func<IReadOnlyList<AssistantProfile>>? Profiles { get; init; }

    /// <summary>The home chat (the hosted ChatView of the home thread); null shows only the hero.</summary>
    public Func<Control>? Conversation { get; init; }

    /// <summary>Whether the home thread holds no message yet (the hero shows while it does not).</summary>
    public Func<bool>? ConversationEmpty { get; init; }

    /// <summary>A chip's ask, sent to the home thread.</summary>
    public Action<string>? Say { get; init; }

    public TaskActions? TaskActions { get; init; }

    public HomeMenu Menu { get; init; } = new();

    /// <summary>The voice button (the voice unit's); null hides it.</summary>
    public Action? Talk { get; init; }

    /// <summary>The voice conversation (AS-18): its sheet opens over the conversation column. Null: no sheet.</summary>
    public VoiceConversation? Voice { get; init; }

    /// <summary>The talk chord as the owner has it, for the sheet's hint.</summary>
    public Func<string>? TalkChord { get; init; }

    /// <summary>The first-run card while first run is not done (null once it is).</summary>
    public Func<Control?>? FirstRun { get; init; }

    /// <summary>The morning digest's card (AS-11, 6), pinned at the top of the conversation as the day's first card; null: no digest service. The card hides itself until a digest exists.</summary>
    public Func<Control?>? Digest { get; init; }

    /// <summary>The suggestions the digest's lane holds open today (AS-11, 5.2), shown as chips beside the common asks; null: none.</summary>
    public Func<IReadOnlyList<HomeSuggestion>>? Suggestions { get; init; }

    /// <summary>Tells the home when the lane changed, on any thread; the subscription is dropped when the home leaves the screen.</summary>
    public Func<Action, IDisposable>? SuggestionsChanged { get; init; }
}

/// <summary>One open suggestion as the home shows it: a short chip, and the whole line for the screen reader. Its tap brings the lane's Accept into view; it never accepts by itself.</summary>
public sealed record HomeSuggestion(string Key, string Chip, string Line)
{
    /// <summary>The longest a chip runs before it is cut.</summary>
    public const int ChipChars = 40;

    /// <summary>The chip for a suggestion: what Accept would do, in a few words.</summary>
    public static HomeSuggestion Of(Suggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        var title = suggestion.Plan.Title;
        var words = suggestion.Kind switch
        {
            SuggestionKind.DoneByHand => "Make “" + title + "” a duty",
            SuggestionKind.IdleDuty => "Slow down “" + title + "”",
            _ => title,
        };
        if (words.Length > ChipChars) words = words[..(ChipChars - 1)].TrimEnd() + "…";
        return new HomeSuggestion(suggestion.Key, words, suggestion.Line);
    }
}

/// <summary>The words of the header's state line, the hero's caption and the sidebar row's status.</summary>
public static class HomeWords
{
    public static string Status(IReadOnlyList<MissionJob> jobs, bool stopped = false)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var live = jobs.Count(j => j.Group != BoardGroup.DoneToday);
        var needs = jobs.Count(j => j.Group == BoardGroup.NeedsYou);
        if (stopped) return live == 0 ? "Stopped" : $"Stopped · {ControlWords.Plural(live, "mission")} kept";
        if (live == 0) return "Ready";
        return needs == 0 ? ControlWords.Plural(live, "mission") : $"{ControlWords.Plural(live, "mission")} · {needs} need you";
    }

    public static string Caption(IReadOnlyList<MissionJob> jobs, int asturs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var needs = jobs.Any(j => j.Group == BoardGroup.NeedsYou);
        var working = jobs.Count(j => j.Runs);
        var parts = new List<string>();
        if (needs) parts.Add("Needs you");
        if (working > 0) parts.Add($"{ControlWords.Plural(working, "mission")} at work");
        else if (asturs > 0) parts.Add(ControlWords.Plural(asturs, "Astur"));
        return parts.Count == 0 ? "Ask for anything: work, life or the world" : string.Join(" · ", parts);
    }

    /// <summary>The mark of the whole assistant: what waits on the owner first, then what thinks, then what works.</summary>
    public static ArmillaState Mark(IReadOnlyList<MissionJob> jobs, bool stopped = false)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        if (jobs.Any(j => j.Group == BoardGroup.NeedsYou)) return ArmillaState.NeedsYou;
        if (stopped) return ArmillaState.Paused;
        if (jobs.Any(j => j.State is MissionState.Planning or MissionState.Reviewing)) return ArmillaState.Thinking;
        if (jobs.Any(j => j.Runs)) return ArmillaState.Working;
        return ArmillaState.Idle;
    }

    public static IReadOnlyList<string> Chips { get; } = ["What needs me?", "What are you working on?", "Plan my day", "Summarise what finished today"];
}

public sealed class AssistantHomeView : Decorator
{
    /// <summary>Below this width the Board stacks under the conversation (2.2 "Narrow windows").</summary>
    public const double StackWidth = 900;

    /// <summary>The least the digest's frame is allowed to grow to; it then takes up to half of the conversation's height.</summary>
    public const double DigestMinHeight = 160;

    /// <summary>What the digest's frame is allowed before the column has been measured.</summary>
    public const double DigestStartHeight = 320;

    private readonly AssistantHomeParts parts;
    private readonly TextBlock name;
    private readonly TextBlock stateLine;
    private readonly Grid columns = new();
    private readonly Border boardHost = new();
    private readonly Border conversation = new();
    private readonly StackPanel hero = new() { Orientation = Orientation.Vertical, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBlock caption = TaskLook.Text("", 12, SwiftFont.Weight.Regular, TaskLook.Ink2);
    private readonly WrapPanel chips = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(16, 4, 16, 8) };
    private readonly Border firstRun = new();
    private readonly Decorator voiceLayer = new() { IsVisible = false };
    private Control? conversationBody;
    private IInputElement? voiceReturn;
    private bool voiceWasOpen;
    private readonly Control? digest;
    private readonly ScrollViewer? digestFrame;
    private readonly List<Button> suggestionChips = [];
    private IDisposable? suggestionsWatch;
    private IReadOnlyList<HomeSuggestion> suggestions = [];
    private MotionScope.Entry? budget;
    private bool stacked;

    public AssistantHomeView(AssistantHomeParts parts)
    {
        this.parts = parts ?? throw new ArgumentNullException(nameof(parts));

        // ---- the header ----
        HeaderMark = new ArmillaMark { MarkSize = 40, Kind = ArmillaKind.Live, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(HeaderMark, AccessibilityView.Raw);
        name = TaskLook.Text(parts.Name, 16, SwiftFont.Weight.Semibold, TaskLook.Ink);
        AutomationProperties.SetHeadingLevel(name, 1);
        stateLine = TaskLook.Text("", 12, SwiftFont.Weight.Regular, TaskLook.Ink2);
        var who = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Children = { name, stateLine } };
        Toggle = new SlotButton(slotA: true) { VerticalAlignment = VerticalAlignment.Center };
        Toggle.Coarse = TaskTouch.IsCoarse;
        Toggle.Clicked += (_, kind) => Announce(kind == SlotKind.Resume ? parts.Controls.ResumeAll() : parts.Controls.PauseAll("Pause all button"));
        StopAllButton = StopAll();
        MenuButton = More();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), ColumnSpacing = 12 };
        var col = 0;
        foreach (var c in new Control?[] { HeaderMark, who, parts.Talk is null ? null : Talk(parts.Talk), Toggle, StopAllButton, MenuButton })
        {
            if (c is not null) { Grid.SetColumn(c, col); header.Children.Add(c); }
            col++;
        }
        var headerFace = new Border { Padding = new Thickness(16, 12), Child = header };
        SwiftGlass.SetEffect(headerFace, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        AutomationProperties.SetName(headerFace, parts.Name);
        AutomationProperties.SetControlTypeOverride(headerFace, AutomationControlType.Group);

        // ---- the stop strip and the Result list ----
        Strip = new StopStrip(parts.Controls);
        Strip.Said += Announce;
        Result = new ResultList(parts.Jobs, parts.Controls);
        Result.Said += Announce;

        // ---- the conversation ----
        HeroMark = new ArmillaMark { MarkSize = 160, Kind = ArmillaKind.Live, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAccessibilityView(HeroMark, AccessibilityView.Raw);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.TextAlignment = TextAlignment.Center;
        caption.MinHeight = 18;
        hero.Children.Add(HeroMark);
        hero.Children.Add(caption);
        foreach (var ask in HomeWords.Chips)
        {
            var text = ask;
            var (chip, shown) = TaskLook.Small(ask, () => parts.Say?.Invoke(text));
            chip.IsEnabled = parts.Say is not null;
            chips.Children.Add(shown);
        }
        AutomationProperties.SetName(chips, "Suggestions");
        var chat = parts.Conversation?.Invoke();
        var convo = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*") };
        Grid.SetRow(firstRun, 1);
        Grid.SetRow(chips, 3);
        convo.Children.Add(hero);
        convo.Children.Add(firstRun);
        // The morning digest is the day's first card (2.2): above the chips and the chat, in a frame that never takes more than half the column, so the composer is always in reach.
        digest = parts.Digest?.Invoke();
        if (digest is not null)
        {
            // The card hides itself until a digest exists (it takes no room, margin included) but stays in the tree, so it hears the digest the moment it is composed.
            digest.Margin = new Thickness(12, 8, 12, 4);
            digestFrame = new ScrollViewer
            {
                Content = digest,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                MaxHeight = DigestStartHeight,
            };
            Grid.SetRow(digestFrame, 2);
            convo.Children.Add(digestFrame);
            digest.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) RefreshConversation();
            };
            conversation.SizeChanged += (_, e) => digestFrame.MaxHeight = Math.Max(DigestMinHeight, e.NewSize.Height * 0.5);
        }
        convo.Children.Add(chips);
        if (chat is not null) { Grid.SetRow(chat, 4); convo.Children.Add(chat); }
        // ---- the voice sheet (AS-18, 2.4): a glass sheet over the conversation column; the thread steps back under it ----
        conversationBody = convo;
        if (parts.Voice is { } voice)
        {
            VoiceSheet = new VoiceSheet(voice, parts.TalkChord);
            VoiceSheet.Closed += GiveFocusBack;
            voiceLayer.Child = VoiceSheet;
            conversation.Child = new Panel { Children = { convo, voiceLayer } };
        }
        else conversation.Child = convo;
        AutomationProperties.SetName(conversation, "Conversation");
        AutomationProperties.SetControlTypeOverride(conversation, AutomationControlType.Group);

        // ---- the Board pane ----
        var context = parts.DecisionContext ?? new DecisionContext(parts.Decisions.Answer);
        Board = new BoardPane(
            new MissionBoard(parts.Jobs, parts.TaskActions),
            new DecisionList(parts.Decisions, context),
            new FeedTab(parts.Feed, parts.FeedSource, parts.OpenFeed),
            new TeamTab(parts.Profiles));
        Board.Missions.Said += Announce;
        Board.Missions.StateChanged += (job, was) =>
        {
            if (was is not null && TaskWords.Announce(job.State, job.Title) is { } line) Announce(line);
        };
        var boardFace = new Border { Padding = new Thickness(12), Child = new ScrollViewer { Content = Board, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        SwiftGlass.SetEffect(boardFace, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        AutomationProperties.SetName(boardFace, "Board");
        AutomationProperties.SetControlTypeOverride(boardFace, AutomationControlType.Group);
        boardHost.Child = boardFace;

        columns.Children.Add(conversation);
        columns.Children.Add(boardHost);
        Lay(false);

        Live = new LiveStatusText { Width = 1, Height = 1, ClipToBounds = true, Opacity = 0, IsHitTestVisible = false };
        var top = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 12, 12, 0), Children = { headerFace, new Border { Height = 10 }, Strip, Result } };
        var page = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(new Border { Margin = new Thickness(12, 0, 12, 12), Child = columns });
        Child = new Panel { Children = { page, Live } };
        AutomationProperties.SetName(this, parts.Name + " home");
        SizeChanged += (_, e) => Lay(e.NewSize.Width < StackWidth);
    }

    public ArmillaMark HeaderMark { get; }

    /// <summary>The voice sheet over the conversation, or null when the home was made without a voice.</summary>
    public VoiceSheet? VoiceSheet { get; }

    public ArmillaMark HeroMark { get; }

    public SlotButton Toggle { get; }

    public Button StopAllButton { get; }

    public Button MenuButton { get; }

    public StopStrip Strip { get; }

    public ResultList Result { get; }

    public BoardPane Board { get; }

    /// <summary>The polite live region.</summary>
    public LiveStatusText Live { get; }

    public string StateLine => stateLine.Text ?? "";

    public string Caption => caption.Text ?? "";

    public bool HeroShown => hero.IsVisible;

    public bool Stacked => stacked;

    internal Control ConversationColumn => conversation;

    internal Control BoardColumn => boardHost;

    internal WrapPanel Chips => chips;

    /// <summary>Says <paramref name="line"/> in the polite live region.</summary>
    public void Announce(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        // the same words twice still announce: the region is cleared first
        if (Live.Text == line) Live.Text = "";
        Live.Text = line;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        parts.Jobs.Changed += Refresh;
        parts.Controls.Changed += Refresh;
        if (parts.Voice is { } voice)
        {
            voice.Changed -= OnVoice;
            voice.Changed += OnVoice;
            OnVoice();
        }
        suggestionsWatch?.Dispose();
        suggestionsWatch = parts.SuggestionsChanged?.Invoke(() => RefreshSuggestions());
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            budget = MotionScope.For(top).Join(ArmillaListRank.Header, () => (HeaderMark.Moves ? 1 : 0) + (HeroShown && HeroMark.Moves ? 1 : 0), () => IsEffectivelyVisible, calm =>
            {
                HeaderMark.Calm = calm;
                HeroMark.Calm = calm;
            });
            HeaderMark.MovesChanged += _ => budget?.Changed();
            HeroMark.MovesChanged += _ => budget?.Changed();
        }
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        parts.Jobs.Changed -= Refresh;
        parts.Controls.Changed -= Refresh;
        if (parts.Voice is { } voice) voice.Changed -= OnVoice;
        suggestionsWatch?.Dispose();
        suggestionsWatch = null;
        budget?.Leave();
        budget = null;
    }

    // The sheet opens or closes with the conversation: the thread's copy of Armilla steps back, the conversation under the glass is skipped by Tab, and the keyboard
    // goes to the sheet, then back to where it was when the sheet closes (Escape closes the sheet and returns the focus to the opener).
    private void OnVoice()
    {
        var open = parts.Voice?.IsOpen == true;
        if (open == voiceWasOpen) return;
        voiceWasOpen = open;
        voiceLayer.IsVisible = open;
        if (conversationBody is not null)
        {
            conversationBody.IsHitTestVisible = !open;
            KeyboardNavigation.SetTabNavigation(conversationBody, open ? KeyboardNavigationMode.None : KeyboardNavigationMode.Continue);
        }
        if (open)
        {
            voiceReturn = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => VoiceSheet?.TakeFocus());
        }
        else GiveFocusBack();
        RefreshConversation();
    }

    private void GiveFocusBack()
    {
        var back = voiceReturn;
        voiceReturn = null;
        if (back is Control control && control.IsAttachedToVisualTree()) control.Focus(NavigationMethod.Tab);
    }

    /// <summary>Reads the missions, the toggle and the stop again.</summary>
    public void Refresh()
    {
        var jobs = parts.Jobs.Snapshot(Marrow.Core.Port.SwiftClock.Provider.Current.GetUtcNow());
        var stopped = parts.Controls.Stopped is not null;
        var asturs = (parts.Profiles?.Invoke() ?? []).Where(p => p.ParentId is not null).ToList();
        var nodes = asturs.Select(a => new ArmillaNode(a.Shape switch
        {
            AsturShape.Hexagon => ArmillaShape.Hexagon,
            AsturShape.Diamond => ArmillaShape.Diamond,
            AsturShape.RoundedTriangle => ArmillaShape.Triangle,
            AsturShape.Clover => ArmillaShape.Clover,
            AsturShape.Star => ArmillaShape.Star,
            _ => ArmillaShape.Squircle,
        })).ToList();
        var mark = HomeWords.Mark(jobs, stopped);
        HeaderMark.State = mark;
        HeroMark.State = mark;
        if (!HeaderMark.Nodes.SequenceEqual(nodes)) { HeaderMark.Nodes = nodes; HeroMark.Nodes = nodes; }
        stateLine.Text = HomeWords.Status(jobs, stopped);
        HeaderMark.Activity = parts.Name + ": " + stateLine.Text;
        caption.Text = HomeWords.Caption(jobs, asturs.Count);
        RefreshConversation();

        // the toggle flips to Resume all as soon as every live task is pausing, paused or stopped (r4 B11, B12)
        var toggle = parts.Controls.Toggle;
        var resume = toggle.Kind == GlobalToggleKind.ResumeAll;
        Toggle.Set(resume ? SlotKind.Resume : SlotKind.Pause, resume ? toggle.CanResumeAll : toggle.CanPauseAll, "all", TaskLook.Still(this));
        ToolTip.SetTip(Toggle.Button, resume ? "Resume all: every paused task goes on" : "Pause all: every task pauses at its next turn boundary");

        Strip.Show();
        if (stopped && !Result.IsOpen) Result.Open(true);
        budget?.Changed();
    }

    /// <summary>The hero shows while the home thread is empty; the first-run card while first run is not done.</summary>
    public void RefreshConversation()
    {
        // A digest is the thread's first card (the canvas appends it to the messages, so the empty thread's mark gives way to it).
        // The voice sheet shows the presence while it is open (2.2).
        hero.IsVisible = (parts.ConversationEmpty?.Invoke() ?? true) && digest?.IsVisible != true && !voiceWasOpen;
        var card = parts.FirstRun?.Invoke();
        if (!ReferenceEquals(firstRun.Child, card)) firstRun.Child = card;
        firstRun.IsVisible = card is not null;
        RefreshSuggestions();
        budget?.Changed();
    }

    /// <summary>The digest's card, or null when the home was made without a digest service.</summary>
    public Control? DigestCard => digest;

    /// <summary>The frame the digest's card scrolls in, or null when the home was made without a digest service.</summary>
    public ScrollViewer? DigestFrame => digestFrame;

    /// <summary>The chips of the suggestions open today, in the order of the lane.</summary>
    public IReadOnlyList<Button> SuggestionChips => suggestionChips;

    /// <summary>Reads the lane's open suggestions again and shows them as chips after the common asks. A tap on one brings its Accept into view.</summary>
    public void RefreshSuggestions()
    {
        var now = parts.Suggestions?.Invoke() ?? [];
        if (now.SequenceEqual(suggestions)) return;
        suggestions = now;
        foreach (var stale in suggestionChips) chips.Children.Remove(stale);
        suggestionChips.Clear();
        foreach (var suggestion in now)
        {
            var key = suggestion.Key;
            var (chip, _) = TaskLook.Small(suggestion.Chip, () => (digest as DigestCard)?.Reveal(key), name: "Suggestion: " + suggestion.Line);
            ToolTip.SetTip(chip, suggestion.Line);
            chips.Children.Add(chip);
            suggestionChips.Add(chip);
        }
    }

    private void Lay(bool narrow)
    {
        stacked = narrow;
        columns.ColumnDefinitions.Clear();
        columns.RowDefinitions.Clear();
        if (narrow)
        {
            // stacked: the conversation, then the Board under it
            columns.RowDefinitions = new RowDefinitions("*,Auto");
            columns.ColumnDefinitions = new ColumnDefinitions("*");
            columns.RowSpacing = 12;
            columns.ColumnSpacing = 0;
            Grid.SetColumn(boardHost, 0);
            Grid.SetRow(boardHost, 1);
            conversation.MinHeight = 420;
        }
        else
        {
            columns.ColumnDefinitions = new ColumnDefinitions("*,400");
            columns.RowDefinitions = new RowDefinitions("*");
            columns.ColumnSpacing = 12;
            columns.RowSpacing = 0;
            Grid.SetColumn(boardHost, 1);
            Grid.SetRow(boardHost, 0);
            conversation.MinHeight = 0;
        }
        Grid.SetColumn(conversation, 0);
        Grid.SetRow(conversation, 0);
    }

    private Button StopAll()
    {
        // Stop all: a square and its label; it never asks (12.6)
        var square = new Rectangle { Width = 11, Height = 11, RadiusX = 2.5, RadiusY = 2.5, VerticalAlignment = VerticalAlignment.Center };
        square.Fill = Chrome.PrimaryText.Brush(square);
        var label = TaskLook.Text("Stop all", 12.5, SwiftFont.Weight.Semibold, TaskLook.Ink);
        label.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border { Height = 32, Padding = new Thickness(12, 0), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { square, label } } };
        face.Capsule(RoundedCornerStyle.Continuous);
        var rest = Chrome.Overlay(.07).Brush(face);
        var hot = Chrome.Overlay(.12).Brush(face);
        face.Background = rest;
        var button = PlainControls.Button(face, () =>
        {
            var report = parts.Controls.StopAll(StopSource.Header);
            Announce(report.Strip);
            Refresh();
        });
        button.KeyboardFocus(16);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = rest;
        AutomationProperties.SetName(button, "Stop all");
        ToolTip.SetTip(button, "Stop all: every mission, head and duty stops now; state is kept");
        return button;
    }

    private static Button Talk(Action talk)
    {
        var mic = new Path { Data = Geometry.Parse("M9 3.5h6v11H9z M5.5 11.5a6.5 6.5 0 0 0 13 0 M12 18v2.5"), Width = 16, Height = 16, Stretch = Stretch.Uniform, StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round };
        mic.Stroke = Chrome.PrimaryText.Brush(mic);
        var face = new Border { Width = 32, Height = 32, Child = mic, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        var button = PlainControls.Button(face, talk);
        button.KeyboardFocus(16);
        button.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(button, "Talk");
        ToolTip.SetTip(button, "Talk");
        return button;
    }

    private Button More()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse { Width = 3.5, Height = 3.5 };
            dot.Fill = Chrome.PrimaryText.Brush(dot);
            dots.Children.Add(dot);
        }
        var face = new Border { Width = 32, Height = 32, Child = dots, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        Button? button = null;
        button = PlainControls.Button(face, () => button!.RaiseEvent(new ContextRequestedEventArgs()));
        button.KeyboardFocus(16);
        button.VerticalAlignment = VerticalAlignment.Center;
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var item in MenuItems()) menu.Items.Add(item);
        };
        button.ContextMenu = menu;
        var names = MenuItems().Select(m => (string)m.Header!).ToList();
        AutomationProperties.SetName(button, "More: " + string.Join(", ", names));
        ToolTip.SetTip(button, string.Join(" · ", names));
        return button;
    }

    /// <summary>The menu's rows: Duties and Memory only when their units give them; Reserves, Activity log, Settings.</summary>
    public IReadOnlyList<MenuItem> MenuItems()
    {
        var m = parts.Menu;
        var items = new List<MenuItem>();
        void Add(string title, Action? act, bool always)
        {
            if (act is null && !always) return;
            var item = new MenuItem { Header = title, IsEnabled = act is not null };
            if (act is not null) item.Click += (_, _) => act();
            items.Add(item);
        }
        Add("Duties", m.Duties, false);
        Add("Memory", m.Memory, false);
        Add("Reserves", m.Reserves, true);
        Add("Activity log", m.ActivityLog, true);
        Add("Settings", m.Settings, true);
        return items;
    }
}
