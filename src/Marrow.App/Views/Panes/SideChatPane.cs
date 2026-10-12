using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W6, the Side chat pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.9, 3.1; the approved canvas's "Side chat"
// pane: the eye note, the conversation, the composer pill; ED-NEW-web-w6-1). No Swift counterpart. The pane shows the conversation of the
// main thread's side chat (AppModel.SideChat): the app's own thread timeline for the answers (streaming, tool rows, approvals), a
// light composer, and in the header the Can act toggle and Clear. "Send to main as a draft" under the latest answer puts it in the main
// thread's composer; it never sends. Nothing here writes to the main thread but that draft.

namespace Marrow.App.Views.Panes;

/// <summary>The words of the Side chat pane (the canvas's, and DESIGN 3.9's).</summary>
public static class SideChatStrings
{
    public const string Note = "Sees this thread. Never interrupts it.";
    public const string NoteCanAct = "Can act with this thread's tools. Its edits show in Changes.";
    public const string Empty = "Ask about this thread. The main run keeps going.";
    public const string Placeholder = "Ask about this thread…";
    public const string CanAct = "Can act";
    public const string Clear = "Clear";
    public const string SendToMain = "Send to main as a draft";
    public const string SentToMain = "Added to the main draft";
    public const string Send = "Send";
    public const string Stop = "Stop";
}

internal static class SideChatTokens
{
    private static SwiftColor Ink(double opacity) => new(red: 0, green: 0, blue: 0, opacity: opacity);

    private static SwiftColor Paper(double opacity) => new(red: 1, green: 1, blue: 1, opacity: opacity);

    /// <summary>The round send button (canvas .scomp .sendb: white .12).</summary>
    public static SwiftColor SendFill { get; } = SwiftColor.Dynamic(light: Ink(.08), dark: Paper(.12));

    /// <summary>The chip under an answer (canvas .fchips button: white .03 with a 1 DIP ring of --line2).</summary>
    public static SwiftColor ChipFill { get; } = SwiftColor.Dynamic(light: Ink(.03), dark: Paper(.03));

    public static SwiftColor ChipRim { get; } = SwiftColor.Dynamic(light: Ink(.16), dark: Paper(.14));
}

/// <summary>The body of the Side chat pane, bound to the side chat of one thread.</summary>
public sealed class SideChatPane : Grid
{
    private readonly AppModel model;
    private readonly Guid parentID;
    private readonly TextBlock note;
    private readonly StackPanel emptyState;
    private readonly Border timelineHost = new();
    private readonly StackPanel approvals = new() { Spacing = 8 };
    private readonly Dictionary<string, (ApprovalRequest Request, Control Card)> approvalCards = new(StringComparer.Ordinal);
    private readonly Border chipRow;
    private readonly Decorator jumpSlot = new() { VerticalAlignment = VerticalAlignment.Top };
    private bool showingTimeline;
    private readonly Button sendToMain;
    private readonly TextBlock sendToMainLabel;
    private readonly PaneGlyph sendToMainGlyph = new(PaneGlyphKind.Check, 12);
    private readonly List<IDisposable> mainTrackers = [];
    private readonly List<IDisposable> sideTrackers = [];
    private PaneContent? content;
    private ThreadRuntime? side;
    private Guid? sideId;
    private bool bound;
    private ThreadTimeline? timeline;
    private TimelineScrollState scrollState = new();
    private ChromeScrollModel scrollChrome = new();
    private string? sentAnswerId;
    private (bool CanAct, bool HasTurns)? drawnHeader;

    private SideChatPane(ThreadRuntime main, AppModel model)
    {
        this.model = model;
        parentID = main.ThreadID;
        RowDefinitions = new RowDefinitions("Auto,*,Auto");

        // The note: what this pane is, in one line (canvas .note2).
        note = new TextBlock { Text = SideChatStrings.Note, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        note.Foreground = PaneTokens.Text3.Brush(note);
        var eye = new PaneGlyph(PaneGlyphKind.Eye, 12) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        eye.Foreground = PaneTokens.Text3.Brush(eye);
        // A grid, not a row of two: the note wraps at the pane's width instead of running past it.
        var noteRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(14, 10, 14, 0) };
        Grid.SetColumn(note, 1);
        noteRow.Children.Add(eye);
        noteRow.Children.Add(note);
        Grid.SetRow(noteRow, 0);
        Children.Add(noteRow);

        // The conversation: the empty state until the first question, then the thread timeline (the two cross-fade).
        var empty = new TextBlock { Text = SideChatStrings.Empty, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 220 };
        empty.Foreground = PaneTokens.Text2.Brush(empty);
        var emptyGlyph = new PaneGlyph(PaneGlyphKind.SideChat, 22) { HorizontalAlignment = HorizontalAlignment.Center };
        emptyGlyph.Foreground = PaneTokens.Text3.Brush(emptyGlyph);
        emptyState = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0), Children = { emptyGlyph, empty } };
        AutomationProperties.SetName(timelineHost, "Side chat conversation");
        var transcript = new Grid { ClipToBounds = true, Children = { emptyState, timelineHost } };
        timelineHost.Opacity = 0;
        timelineHost.IsHitTestVisible = false;
        Grid.SetRow(transcript, 1);
        Children.Add(transcript);

        // The foot: approvals the side chat is waiting on, the chip under the latest answer, the composer.
        sendToMainLabel = new TextBlock { Text = SideChatStrings.SendToMain, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        sendToMainLabel.Foreground = PaneTokens.Text1.Brush(sendToMainLabel);
        sendToMainGlyph.IsVisible = false;
        sendToMainGlyph.Foreground = PaneTokens.Text2.Brush(sendToMainGlyph);
        var chipFace = new Border
        {
            Height = 28,
            Padding = new Thickness(12, 0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { sendToMainGlyph, sendToMainLabel } },
        };
        chipFace.Background = SideChatTokens.ChipFill.Brush(chipFace);
        chipFace.BorderBrush = SideChatTokens.ChipRim.Brush(chipFace);
        sendToMain = PlainControls.Button(chipFace, SendLatestToMain).KeyboardFocus(14);
        sendToMain.OnHover(on => chipFace.Background = (on ? PaneTokens.HoverFill : SideChatTokens.ChipFill).Brush(chipFace));
        AutomationProperties.SetName(sendToMain, SideChatStrings.SendToMain);
        chipRow = new Border { Margin = new Thickness(12, 0, 12, 8), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, Child = sendToMain };

        Composer = new SideChatComposer { Margin = new Thickness(10, 0, 10, 10) };
        Composer.Submitted += text =>
        {
            if (model.SendSideChat(parentID, text)) Composer.Text = "";
        };
        Composer.StopRequested += () => model.StopSideChat(parentID);
        Composer.TextEdited += text =>
        {
            if (side is { } runtime) runtime.Draft = runtime.Draft with { Text = text };
        };

        var foot = new StackPanel { Orientation = Orientation.Vertical, Children = { approvals, chipRow, Composer } };
        approvals.Margin = new Thickness(10, 0, 10, 8);
        var footHost = new Grid { Children = { foot, jumpSlot } };
        Grid.SetRow(footHost, 2);
        Children.Add(footHost);
    }

    /// <summary>The composer (tests and the workspace's focus on open reach it here).</summary>
    public SideChatComposer Composer { get; }

    /// <summary>The conversation's timeline, once the side chat has one.</summary>
    internal ThreadTimeline? Timeline => timeline;

    /// <summary>The note line.</summary>
    public string NoteText => note.Text ?? "";

    /// <summary>The note's text block (it wraps at the pane's width).</summary>
    public TextBlock NoteBlock => note;

    /// <summary>Whether the empty state shows (no question has been asked).</summary>
    public bool ShowsEmptyState => !showingTimeline;

    /// <summary>The "Send to main as a draft" chip, visible under a finished answer.</summary>
    public Button SendToMainButton => sendToMain;

    public bool SendToMainIsVisible => chipRow.IsVisible;

    public string SendToMainText => sendToMainLabel.Text ?? "";

    /// <summary>The thread the pane is bound to, if it has one.</summary>
    public Guid? SideChatId => sideId;

    /// <summary>The approval cards the side chat is waiting on.</summary>
    public IReadOnlyCollection<Control> ApprovalCards => approvalCards.Values.Select(entry => entry.Card).ToList();

    /// <summary>Adds the side chat's pane to the workspace's contents.</summary>
    public static void Register() => PaneContents.Register(PaneKind.SideChat, Create);

    /// <summary>The Side chat pane of a thread's workspace; null when the runtime has no app (a runtime that was never opened).</summary>
    public static PaneContent? Create(PaneKind kind, ThreadRuntime runtime)
    {
        if (kind != PaneKind.SideChat || runtime.App is not { } app) return null;
        var pane = new SideChatPane(runtime, app);
        var content = new PaneContent(pane) { FocusTarget = pane.Composer.Field };
        pane.content = content;
        pane.UpdateHeader();
        return content;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        mainTrackers.Add(new Tracked<Guid?>(() => model.SideChat(parentID)?.Id, Bind));
        mainTrackers.Add(new Tracked<bool>(() => model.SideChatCanAct(parentID), _ => CanActChanged()));
        if (sideId is null)
        {
            // The side chat of this thread is made when the pane first shows, after this layout pass (it adds a thread to the library).
            MainActor.Post(() =>
            {
                if (VisualRoot is not null && model.SideChat(parentID) is null) model.EnsureSideChat(parentID);
            });
        }
        SizeChanged += OnSizeChanged;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        SizeChanged -= OnSizeChanged;
        foreach (var tracker in mainTrackers) tracker.Dispose();
        mainTrackers.Clear();
        StopSideTrackers();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateTimelineInputs();

    private void StopSideTrackers()
    {
        foreach (var tracker in sideTrackers) tracker.Dispose();
        sideTrackers.Clear();
    }

    // The side chat the pane shows changed (made, cleared, or this attach): a new conversation gets a new timeline.
    private void Bind(Guid? id)
    {
        if (bound && id == sideId)
        {
            StartSideTrackers();
            return;
        }
        StopSideTrackers();
        bound = true;
        sideId = id;
        side = id is { } value ? model.Runtime(value) : null;
        _ = side?.EnsureLoaded(); // a saved conversation is read from disk (the timeline and the empty state follow when it lands)
        timeline = null;
        timelineHost.Child = null;
        scrollState = new TimelineScrollState();
        scrollChrome = new ChromeScrollModel();
        jumpSlot.Child = new JumpToLatestButton(scrollState) { VerticalAlignment = VerticalAlignment.Top };
        sentAnswerId = null;
        drawnHeader = null;
        Composer.ChipHost.Child = side is not null && model.Thread(sideId!.Value) is { } thread ? new ModelEffortSlot(side, thread, compact: true) : null;
        Composer.Text = side?.Draft.Text ?? "";
        Composer.IsRunning = side?.IsRunning ?? false;
        ClearApprovals();
        StartSideTrackers();
        UpdateTranscript();
        UpdateChip();
        UpdateHeader();
    }

    private void StartSideTrackers()
    {
        StopSideTrackers();
        if (side is not { } runtime) return;
        sideTrackers.Add(new Tracked<(int, bool, bool)>(() => (runtime.Entries.Count, runtime.IsRunning, runtime.IsLoadingHistory), _ =>
        {
            UpdateTranscript();
            UpdateHeader();
        }));
        sideTrackers.Add(new Tracked<bool>(() => runtime.IsRunning, running => Composer.IsRunning = running));
        sideTrackers.Add(new Tracked<ValueList<ApprovalRequest>>(() => runtime.Approvals, ShowApprovals));
        sideTrackers.Add(new Tracked<(int, int, RuntimePhase)>(() => (runtime.Entries.Count, runtime.Turns.Count, runtime.Phase), _ => UpdateChip()));
    }

    private void CanActChanged()
    {
        note.Text = model.SideChatCanAct(parentID) ? SideChatStrings.NoteCanAct : SideChatStrings.Note;
        UpdateHeader();
    }

    // ---- the header: Can act and Clear

    private void UpdateHeader()
    {
        var canAct = model.SideChatCanAct(parentID);
        var hasTurns = side is { } runtime && runtime.Entries.Count != 0;
        if (drawnHeader == (canAct, hasTurns)) return;
        drawnHeader = (canAct, hasTurns);
        var actions = new List<PaneHeaderAction>
        {
            new(SideChatStrings.CanAct, () => model.SetSideChatCanAct(parentID, !model.SideChatCanAct(parentID)), Text: SideChatStrings.CanAct, Pressed: canAct),
        };
        if (hasTurns) actions.Add(new PaneHeaderAction(SideChatStrings.Clear, () => model.ClearSideChat(parentID), Text: SideChatStrings.Clear));
        content?.Update(null, null, actions);
    }

    // ---- the conversation

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void UpdateTranscript()
    {
        // Nothing shows while a saved conversation is being read, so "Ask about this thread" never flashes over one.
        var show = side is { } runtime && (runtime.Entries.Count != 0 || runtime.IsRunning);
        var loading = side is { IsLoadingHistory: true } && !show;
        showingTimeline = show;
        if (show) EnsureTimeline();
        SetShown(timelineHost, show);
        SetShown(emptyState, !show && !loading);
    }

    private void SetShown(Control control, bool shown)
    {
        control.IsHitTestVisible = shown;
        control.Transitions = ReduceMotion ? null : [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];
        control.Opacity = shown ? 1 : 0;
    }

    private void EnsureTimeline()
    {
        if (timeline is not null || side is not { } runtime || sideId is not { } id || model.Thread(id) is not { } thread) return;
        var project = model.Project(thread.ProjectID);
        timeline = new ThreadTimeline(runtime, scrollChrome, scrollState, project?.Name, thread.WorktreePath ?? project?.Path,
            supportsRewind: false, columnHeight: Math.Max(0, Bounds.Height), showsMinimap: false, contentMargin: new Thickness(14, 8, 14, 12));
        timelineHost.Child = timeline;
    }

    private void UpdateTimelineInputs()
    {
        if (timeline is null || sideId is not { } id || model.Thread(id) is not { } thread) return;
        var project = model.Project(thread.ProjectID);
        timeline.UpdateInputs(project?.Name, thread.WorktreePath ?? project?.Path, false, Math.Max(0, Bounds.Height), false);
    }

    // ---- approvals

    private void ShowApprovals(ValueList<ApprovalRequest> requests)
    {
        if (side is not { } runtime) return;
        var keep = requests.Select(request => request.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in approvalCards.Keys.Where(id => !keep.Contains(id)).ToArray())
        {
            approvals.Children.Remove(approvalCards[id].Card);
            approvalCards.Remove(id);
        }
        foreach (var request in requests)
        {
            if (approvalCards.TryGetValue(request.Id, out var shown) && shown.Request == request) continue;
            if (shown.Card is not null) approvals.Children.Remove(shown.Card);
            var card = new Decorator { Child = new ApprovalCard(request, runtime) };
            approvalCards[request.Id] = (request, card);
            approvals.Children.Add(card);
        }
    }

    private void ClearApprovals()
    {
        approvals.Children.Clear();
        approvalCards.Clear();
    }

    // ---- Send to main

    // The latest answer: the last assistant text of the side chat, once it has finished writing.
    private (string Id, string Text)? LatestAnswer()
    {
        if (side is not { } runtime || runtime.IsRunning) return null;
        for (var index = runtime.Entries.Count - 1; index >= 0; index--)
        {
            if (runtime.Entries[index].Item is { TimelineItemContent: TimelineItem.Content.Assistant { Value: { IsStreaming: false } answer } } item && answer.Text.Trim().Length != 0)
                return (item.Id, answer.Text);
        }
        return null;
    }

    private void SendLatestToMain()
    {
        if (LatestAnswer() is not { } answer || !model.SendSideChatToMain(parentID, answer.Text)) return;
        sentAnswerId = answer.Id;
        UpdateChip();
    }

    private void UpdateChip()
    {
        var answer = LatestAnswer();
        chipRow.IsVisible = answer is not null;
        var sent = answer is { } latest && latest.Id == sentAnswerId;
        sendToMainLabel.Text = sent ? SideChatStrings.SentToMain : SideChatStrings.SendToMain;
        sendToMainGlyph.IsVisible = sent;
        sendToMain.IsHitTestVisible = !sent;
        sendToMain.Focusable = !sent;
        chipRow.Opacity = sent ? 0.75 : 1;
        AutomationProperties.SetName(sendToMain, sendToMainLabel.Text);
    }
}
