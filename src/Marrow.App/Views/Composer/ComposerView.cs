using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :1-1307.
// Skeleton (c100 group 2): every type the file declares, private and nested ones included, the views' inputs, the
// records' shapes and the members the other cycle files use. Every view body and every member with behaviour is left to
// the implementing unit (MARROW_STUB), as are ComposerView's private members (:228-533), the private views' private
// members, and FileIndex's private cache (:1118-1127) and private statics build, search, contains, list and enumerate
// (:1161-1287).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; `@Binding var x` is a Binding<T> (R-VIEW-2); a file `URL` handed to an attach is
// its path string, as AttachmentSource.File and DownloadsPopover's pick take it. ENGINEERING DECISION: NSRange
// (SuggestionState.range, :1044) is the (Location, Length) pair of UTF-16 offsets ComposerController.ReplaceCharacters
// takes; the port has no NSRange type. Swift `private` top-level types are `internal` here: C# has no private top-level
// type, and `file` cannot appear in another type's member signatures (CS9051).
//
// Unit App.Views.Composer.ComposerView, part 1 of 5 (B3 52bc792; this file is unchanged from B2 1fbf81e): ComposerArea
// (:4-58), TabSlot (:60-99), ChangeStatsRefresh (:101-112), ComposerView's body layout (:139-189), placeholder and the
// Hydra helpers (:275-309), keys (:311-368), send (:370-382) and recall (:400-422), ComposerTextColumn (:536-607),
// SuggestionState.isVisible (:1048) and ComposerSurface (:1290-1307). blur (:228-237), cursorMoved (:426-437) and
// refreshSuggestions (:439-473) are written too, because the text view calls the first two on every loss of focus and
// every change of its text (ComposerTextView.swift:280-282, :317-327). DraftChips (:876-879, :939), DraftAttachments
// (:980-987) and ComposerWorkingDotsSlot (:664-665) carry only their conditions and gaps here; what they draw is parts 2
// to 5's, as are ComposerView's other body modifiers (:190-226) and private members, marked MARROW_STUB where they
// stand. Not written: the height animation at :604 (STOP #1 in ComposerViewTests.cs, WindowLiveResize missing).

/// <summary>:4-58 <c>struct ComposerArea: View</c>.</summary>
public sealed class ComposerArea : Decorator
{
    private readonly ThreadRuntime runtime;

    // :18-20 @State private var diffPopover = DiffPopoverCoordinator(): the changes popover presented from the tab.
    // Driven by runtime.isDiffVisible, so every opener (tab, Review, the changes chord) shares it.
    private readonly DiffPopoverCoordinator diffPopover = new();

    // :25-27 the approval cards, keyed by request id (R-VIEW-8), with the request each card was drawn from.
    private readonly ForEach<ApprovalRequest, string> approvals;
    private readonly Dictionary<string, ApprovalRequest> drawnRequests = new(SwiftStringComparer.Instance);
    private string[]? approvalIDs;

    // :41-47 the .onChange observers, alive while the group is on screen.
    private readonly List<IDisposable> observers = [];
    private bool onScreen;

    // :6-16 the view's inputs; :22-57 body
    public ComposerArea(
        ThreadRuntime runtime,
        string? workingDirectory,
        bool compactModelChip = false,
        bool takesFocusOnAppear = true,
        bool showsChanges = true)
    {
        this.runtime = runtime;

        // :28-31 the tab slot and the pill, the tab drawn under the pill's top 14 points.
        var group = new StackPanel { Orientation = Orientation.Vertical, Spacing = -ThreadChangesTab.Overlap };
        group.Children.Add(new TabSlot(runtime, diffPopover, showsChanges)); // :29
        group.Children.Add(new ComposerView(runtime, workingDirectory, compactModelChip, takesFocusOnAppear)); // :30

        // :32-38 two backgrounds; the later one sits behind the earlier.
        var layers = new Grid();
        if (showsChanges)
        {
            // :35-38 A box without the tab never runs git for the stats it would show.
            layers.Children.Add(new ChangeStatsRefresh(runtime)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }
        layers.Children.Add(new AttachmentAnchorCapture(view => diffPopover.SetFallbackAnchor(view))); // :32-34
        layers.Children.Add(group);
        layers.AttachedToVisualTree += (_, _) => Appear();
        layers.DetachedFromVisualTree += (_, _) => Disappear();
        // :49 .onChange(of: isColumnShown) { _, shown in if !shown { diffPopover.close() } }
        layers.PropertyChanged += (_, e) =>
        {
            if (!onScreen || e.Property != Views.RootView.ChatColumnIsShownProperty) return;
            if (!Views.RootView.GetChatColumnIsShown(layers)) diffPopover.Close();
        };

        // :24 VStack(spacing: 12): the approval cards, then the group.
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12 };
        stack.Children.Add(layers);
        approvals = new ForEach<ApprovalRequest, string>(stack, request => request.Id, MakeCard, UpdateCard);

        // :23 GlassEffectContainer(spacing: 8). MARROW_DEFERRED ED-180: the container's glass merging at spacing 8 has
        // no drawn material yet; its children record their own glass (SwiftGlass).
        // :52 .frame(maxWidth: 820): at most 820 wide, centred in the room the padding leaves.
        var container = new Decorator { Child = stack, MaxWidth = 820 };
        // :53-55 .padding(.horizontal, 20).padding(.bottom, 14).frame(maxWidth: .infinity)
        Child = new Border { Child = container, Padding = new Thickness(20, 0, 20, 14) };

        // :25 ForEach(runtime.approvals) and :56 .animation(Chrome.panelSlide, value: runtime.approvals.map(\.id))
        this.Track(() => runtime.Approvals, ShowApprovals);
    }

    private void ShowApprovals(ValueList<ApprovalRequest> requests)
    {
        var ids = requests.Select(request => request.Id).ToArray();
        var changed = approvalIDs is not null && !approvalIDs.SequenceEqual(ids, SwiftStringComparer.Instance);
        approvalIDs = ids;
        // R-VIEW-4: the id list alone keys the animation; a request whose other fields change re-lays out without it.
        if (changed) Transaction.WithAnimation(Chrome.PanelSlide, () => approvals.Apply(requests));
        else approvals.Apply(requests);
    }

    // :26 ApprovalCard(request: request, runtime: runtime)
    private Control MakeCard(ApprovalRequest request)
    {
        drawnRequests[request.Id] = request;
        return new Decorator { Child = new ApprovalCard(request, runtime) };
    }

    // ApprovalCard keeps no state (RequestCards.swift:3-66), so a changed request is drawn again in the same row.
    private void UpdateCard(Control row, ApprovalRequest request)
    {
        if (drawnRequests.TryGetValue(request.Id, out var drawn) && drawn == request) return;
        drawnRequests[request.Id] = request;
        ((Decorator)row).Child = new ApprovalCard(request, runtime);
    }

    private void Appear()
    {
        onScreen = true;
        // :39 .onAppear
        diffPopover.Sync(runtime.IsDiffVisible, runtime);
        // :40-41 The box moved to another thread; the coordinator closes the popover it had and never opens one on
        // arrival. The runtime is this control's input, fixed for its life, so this observer does not fire here.
        observers.Add(new OnChange<Guid>(() => runtime.ThreadID, (_, _) => diffPopover.Sync(runtime.IsDiffVisible, runtime)));
        // :42-44
        observers.Add(new OnChange<bool>(() => runtime.IsDiffVisible, (_, visible) => diffPopover.Sync(visible, runtime)));
        // :45-47
        observers.Add(new OnChange<long>(() => runtime.DiffOpenRequest, (_, _) => diffPopover.Reopen(runtime)));
    }

    private void Disappear()
    {
        onScreen = false;
        foreach (var observer in observers) observer.Dispose();
        observers.Clear();
        diffPopover.Close(); // :48
    }
}

/// <summary>:60-99 <c>private struct TabSlot: View</c>.</summary>
internal sealed class TabSlot : Decorator
{
    // The .softAppear of a tab that arrived with its slot (Chrome.swift:949-955): the ZStack's own insertion carried
    // it in, so only the removal half, the 0.12 s ease-out fade, is left for when it leaves. SwiftUI runs a transition
    // on the view an update inserts or removes, not on the views inside it: documented SwiftUI behaviour, UNVERIFIED
    // under OD-0001 as ED-092 records for the other documented defaults.
    private static SwiftTransition ArrivedWithSlot =>
        SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Identity,
            removal: SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.12)));

    private readonly ThreadRuntime runtime;
    private readonly DiffPopoverCoordinator diffPopover;
    private readonly bool showsChanges;

    // Where the ZStack stands while the slot exists; the slot takes room only while it holds one (:73-75).
    private readonly Panel place = new();
    private Grid? zstack; // :76 ZStack(alignment: .bottom)
    private FollowUpQueueTab? queueTab; // :80
    private Decorator? changesTab; // :83-94, the ThreadChangesTab drawn from the latest stats
    private FileChangeSummary? drawnStats;

    // :65-67 the view's inputs; :69-98 body
    public TabSlot(ThreadRuntime runtime, DiffPopoverCoordinator diffPopover, bool showsChanges)
    {
        this.runtime = runtime;
        this.diffPopover = diffPopover;
        this.showsChanges = showsChanges;
        Child = place;
        // :73-75 The slot exists only while a tab does, or the stack's overlap would pull an empty slot's box up.
        IsVisible = false;
        place.Children.CollectionChanged += (_, _) => IsVisible = place.Children.Count != 0;
        this.Track(Occupant, Apply);
    }

    // :75, :77, :82: whether follow-ups are queued, else the stats the changes tab shows (nil when it shows none).
    private (bool Queued, FileChangeSummary? Stats) Occupant()
    {
        var queued = runtime.FollowUps.Count != 0;
        if (queued || !showsChanges) return (queued, null);
        return (false, runtime.ChangeStats);
    }

    private void Apply((bool Queued, FileChangeSummary? Stats) occupant)
    {
        var (queued, stats) = occupant;
        // :75 neither tab: the ZStack leaves (an unmarked view: .opacity, under the change's own transaction)
        if (!queued && stats is null)
        {
            if (zstack is { } leaving) SwiftTransition.Remove(place, leaving);
            zstack = null;
            queueTab = null;
            changesTab = null;
            drawnStats = null;
            return;
        }

        if (zstack is null)
        {
            // :75-76 the slot arrives with its tab inside
            var arriving = new Grid();
            zstack = arriving;
            arriving.Children.Add(queued ? MakeQueueTab(ArrivedWithSlot) : MakeChangesTab(stats!, ArrivedWithSlot));
            SwiftTransition.Insert(place, place.Children.Count, arriving);
            return;
        }

        // :70-72 the tab leaving fades where it stood while the one arriving fades in over it.
        if (queued)
        {
            if (queueTab is not null) return;
            if (changesTab is { } gone) SwiftTransition.Remove(zstack, gone);
            changesTab = null;
            drawnStats = null;
            SwiftTransition.Insert(zstack, zstack.Children.Count, MakeQueueTab(SwiftTransition.SoftAppear)); // :81
            return;
        }

        if (changesTab is { } shown)
        {
            // The same tab with new numbers: ThreadChangesTab keeps no state (ThreadChangesTab.swift:6-47).
            if (stats == drawnStats) return;
            drawnStats = stats;
            shown.Child = ChangesTab(stats!);
            return;
        }
        if (queueTab is { } done) SwiftTransition.Remove(zstack, done);
        queueTab = null;
        SwiftTransition.Insert(zstack, zstack.Children.Count, MakeChangesTab(stats!, SwiftTransition.SoftAppear)); // :94
    }

    // :77-81 The queued steering prompts take the tab slot while any are queued: the changes tab hides behind them and
    // reappears once the queue empties.
    private FollowUpQueueTab MakeQueueTab(SwiftTransition transition)
    {
        var tab = new FollowUpQueueTab(runtime) { VerticalAlignment = VerticalAlignment.Bottom }.Transition(transition);
        queueTab = tab;
        return tab;
    }

    // :82-94
    private Decorator MakeChangesTab(FileChangeSummary stats, SwiftTransition transition)
    {
        var holder = new Decorator
        {
            Child = ChangesTab(stats),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        }.Transition(transition);
        changesTab = holder;
        drawnStats = stats;
        return holder;
    }

    // :83-93
    private ThreadChangesTab ChangesTab(FileChangeSummary stats) =>
        new(stats, anchor: view => diffPopover.SetAnchor(view), action: ShowChanges);

    // :86-93
    private void ShowChanges()
    {
        runtime.ClearDiffFocus();
        runtime.DiffAnchor = null;
        if (runtime.DiffSelection is not null) runtime.DiffSelection = null;
        // Set last so a redundant write never restarts the diff load or steals the presentation.
        if (!runtime.IsDiffVisible) runtime.IsDiffVisible = true;
    }
}

/// <summary>:101-112 <c>private struct ChangeStatsRefresh: View</c>.</summary>
internal sealed class ChangeStatsRefresh : Decorator
{
    // :105 let runtime; :107-111 body
    public ChangeStatsRefresh(ThreadRuntime runtime)
    {
        // :108-109 Color.clear.frame(width: 0, height: 0)
        Width = 0;
        Height = 0;
        // :110 .task(id: runtime.diffRevision) { await runtime.refreshChangeStats() }
        this.Task(() => runtime.DiffRevision, async _ => await runtime.RefreshChangeStats());
    }
}

/// <summary>:114-534 <c>struct ComposerView: View</c>: the chat box's pill.</summary>
public sealed class ComposerView : Decorator
{
    // The @State the body reads (R-VIEW-2).
    private sealed class ViewState : MainActorModel
    {
        private SuggestionState suggestions = new();
        private bool showingRecents;
        private string? attachmentNotice;

        // :127 @State private var suggestions = SuggestionState()
        public SuggestionState Suggestions { get => Read(suggestions); set => Set(ref suggestions, value); }

        // :131 @State private var showingRecents = false
        public bool ShowingRecents { get => Read(showingRecents); set => Set(ref showingRecents, value); }

        // :132-134 @State private var attachmentNotice: String?
        public string? AttachmentNotice { get => Read(attachmentNotice); set => Set(ref attachmentNotice, value); }
    }

    // :137 static let maxAttachments = Storage.attachmentLimit
    public static long MaxAttachments => Storage.AttachmentLimit;

    private readonly ThreadRuntime runtime;
    private readonly bool compactModelChip;
    private readonly ComposerController controller = new(); // :126
    private readonly ViewState state = new();
    private readonly ComposerTextColumn textColumn;
    private readonly Grid row;
    private bool dismissedSuggestions; // :128-129 Escape closed the list; the next caret move may open it again.
    private int? historyIndex; // :130
    private readonly FileIndex fileIndex = new(); // :132
    private ComposerControls? controls;
    private (ChatThread Thread, bool RecentDownloadsPicker, bool GoesToHead, bool QueueGoesToHead, ProviderKind? HeadsProvider)? drawnControls;

    // :115 @Environment(AppModel.self) private var model
    private AppModel Model => AppEnvironment.GetAppModel(this);

    // :119-123 the view's inputs; :139-226 body
    public ComposerView(
        ThreadRuntime runtime,
        string? workingDirectory,
        bool compactModelChip = false,
        bool takesFocusOnAppear = true)
    {
        this.runtime = runtime;
        this.compactModelChip = compactModelChip;
        _ = workingDirectory; // :120, read by the file index and the command key (parts 2 and 3 of this unit)

        // :146-158 The resolved thread comes from the environment, which a control reaches once it is in the tree, so
        // the column starts with no placeholder and takes the thread's own as it appears (the Track below).
        textColumn = new ComposerTextColumn(
            runtime,
            controller,
            placeholder: "",
            takesFocusOnAppear: takesFocusOnAppear,
            attachmentNotice: state.AttachmentNotice,
            onKey: HandleKey,
            onAttach: Attach,
            onCursorChange: CursorMoved,
            onBlur: Blur)
        {
            // :157-158 A single line of text is as tall as the send button, so an empty pill is 44 high.
            Margin = new Thickness(0, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        // :143-145 One pill: the text on the left, the controls tucked into its trailing end, on its bottom line, so they
        // stay put while the text grows upward. HStack(alignment: .bottom, spacing: 8): the 8 is the controls' leading
        // margin, so it exists only with them.
        row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(textColumn);

        // :176-178
        var padded = new Border { Child = row, Padding = new Thickness(14, 8, 8, 8) };
        // :179-185 While a turn runs, accent dots flow along the pill's bottom two-fifths, behind the text.
        var layers = new Grid();
        layers.Children.Add(new ComposerWorkingDotsSlot(runtime));
        layers.Children.Add(padded);
        // :186 .modifier(ComposerSurface())
        Child = new ComposerSurface().Body(layers);
        // :187-189 .geometryGroup(): the pill's width glides as the panels take or give room, and the text view and the
        // controls move as one with the glass. The port animates no frame of its own on a width change, so the row's
        // children are laid out with the pill in the same pass.

        // :142, :149, :151 the resolved thread's placeholder and the attach notice, handed to the column as values.
        this.Track(
            () => (Placeholder: Placeholder(Model.Thread(runtime.ThreadID)), Notice: state.AttachmentNotice),
            inputs => textColumn.Update(inputs.Placeholder, inputs.Notice));
        // :160-174
        this.Track(ControlInputs, ShowControls);

        // MARROW_STUB :190-226 the body's remaining modifiers (the suggestion list, the command and file index tasks,
        // the notice's timeout, the column-shown focus): parts 2 and 3 of this unit.
    }

    // :142, :160-173 what the controls are drawn from, nil without a resolved thread.
    private (ChatThread Thread, bool RecentDownloadsPicker, bool GoesToHead, bool QueueGoesToHead, ProviderKind? HeadsProvider)? ControlInputs()
    {
        if (Model.Thread(runtime.ThreadID) is not { } thread) return null; // :142, :160
        return (thread, Model.Settings.RecentDownloadsPicker, GoesToHead(thread), QueueGoesToHead(thread), HeadsProvider(thread));
    }

    private void ShowControls((ChatThread Thread, bool RecentDownloadsPicker, bool GoesToHead, bool QueueGoesToHead, ProviderKind? HeadsProvider)? inputs)
    {
        if (inputs == drawnControls) return;
        drawnControls = inputs;
        if (controls is { } previous) row.Children.Remove(previous);
        controls = null;
        if (inputs is not { } value) return; // :160 no resolved thread: no controls at all
        // :161-173. ComposerControls takes its inputs once (part 3 of this unit), so new inputs draw it again.
        var made = new ComposerControls(
            runtime,
            value.Thread,
            compact: compactModelChip,
            recentDownloadsPicker: value.RecentDownloadsPicker,
            goesToHead: value.GoesToHead,
            queueGoesToHead: value.QueueGoesToHead,
            headsProvider: value.HeadsProvider,
            showingRecents: new Binding<bool>(() => state.ShowingRecents, shown => state.ShowingRecents = shown),
            onAttach: paths => Attach(paths.Select(path => (AttachmentSource)new AttachmentSource.File(path)).ToList()),
            onChooseFiles: ChooseFiles,
            onSend: Send)
        {
            Margin = new Thickness(8, 0, 0, 0), // :145 spacing: 8
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetColumn(made, 1);
        row.Children.Add(made);
        controls = made;
    }

    // :228-237 Written with part 1 because the text view reports every loss of focus here (ComposerTextView.swift:280-282).
    private void Blur()
    {
        // :229-230 Clicking a row focuses the popover; only dismiss when focus truly left both the text and the
        // suggestions.
        if (!controller.IsClickInsideSuggestions())
        {
            // :232-234 A search still under way for the last keystroke would put the popover back over a field that no
            // longer has focus.
            controller.SuggestionRefresh?.Cancel();
            state.Suggestions = new SuggestionState();
        }
    }

    // :275-288. Internal for this unit's tests (InternalsVisibleTo Marrow.App.Tests); private in Swift.
    internal string Placeholder(ChatThread? thread)
    {
        if (thread is null) return ""; // :276
        if (thread.InteractionMode == InteractionMode.Plan) return "Describe what you want to plan"; // :277
        if (Model.HydraIsOn(thread)) // :278
        {
            // :279-284 With every message sent to a head, the lead never works the message itself: it goes straight
            // out and comes back as a report, which is worth saying here because nothing else in the box shows it.
            if (Model.Settings.HydraAlwaysHeads)
                return "Goes to a head; " + thread.Provider.DisplayName + " leads and reports back";
            return "Ask " + thread.Provider.DisplayName + " for anything; big jobs go out to a team of heads"; // :285
        }
        return "Ask " + thread.Provider.DisplayName + " to build, fix or explain. @ for files, / for commands"; // :287
    }

    // :290-293 Whether the next message is handed to a head rather than worked by the lead.
    private bool GoesToHead(ChatThread thread) =>
        Model.Settings.HydraAlwaysHeads && Model.HydraIsOn(thread);

    // :295-299 Whether a message queued behind the running turn is handed to a head of its own rather than waiting for
    // the lead.
    private bool QueueGoesToHead(ChatThread thread) =>
        (Model.Settings.HydraQueueHeads || Model.Settings.HydraAlwaysHeads) && Model.HydraIsOn(thread);

    // :301-307 The pair's heads provider when it differs from the lead's, so the usage popover can show both plans'
    // limits at once. Nil keeps it to the chat's own provider.
    private ProviderKind? HeadsProvider(ChatThread thread)
    {
        if (!Model.HydraIsOn(thread) || Model.HydraPair(thread) is not { } pair) return null; // :304
        var heads = Model.HydraHeadsProvider(pair); // :305
        return heads == thread.Provider ? null : heads; // :306
    }

    // :311-368
    private bool HandleKey(ComposerKey key)
    {
        if (state.Suggestions.IsVisible) // :312
        {
            switch (key)
            {
                case ComposerKey.Up: // :314-316
                    state.Suggestions = state.Suggestions with { Selected = Math.Max(0, state.Suggestions.Selected - 1) };
                    return true;
                case ComposerKey.Down: // :317-319
                    state.Suggestions = state.Suggestions with
                    {
                        Selected = Math.Min(state.Suggestions.Items.Count - 1, state.Suggestions.Selected + 1),
                    };
                    return true;
                case ComposerKey.Tab or ComposerKey.Submit or ComposerKey.Steer: // :320-322
                    Pick(state.Suggestions.Items[checked((int)state.Suggestions.Selected)]);
                    return true;
                case ComposerKey.Escape: // :323-326
                    dismissedSuggestions = true;
                    state.Suggestions = new SuggestionState();
                    return true;
                case ComposerKey.DeleteAtStart: // :327-328
                    return false;
            }
        }
        switch (key)
        {
            case ComposerKey.Submit: // :332-334
                Send();
                return true;
            case ComposerKey.Steer: // :335-337
                Steer();
                return true;
            case ComposerKey.Up: // :338-339
                return Recall(older: true);
            case ComposerKey.Down: // :340-341
                return Recall(older: false);
            case ComposerKey.Escape: // :342-354
                // A running turn is stopped and taken back, its words returning to the box; with nothing running, the
                // panels go the way the terminal and changes chords take them.
                if (runtime.IsRunning) runtime.TakeBackRunningTurn();
                else if (runtime.IsTerminalVisible) runtime.IsTerminalVisible = false;
                else if (runtime.IsDiffVisible) runtime.ToggleDiff();
                else return false;
                return true;
            case ComposerKey.Tab: // :355-356
                return false;
            case ComposerKey.DeleteAtStart: // :357-366
                if (runtime.Draft.Command is not null)
                {
                    Transaction.WithAnimation(Chrome.PanelSlide, () => runtime.Draft = runtime.Draft with { Command = null });
                    return true;
                }
                if (runtime.Draft.Quotes.Count != 0)
                {
                    Transaction.WithAnimation(Chrome.PanelSlide, () => runtime.Draft = runtime.Draft with
                    {
                        Quotes = runtime.Draft.Quotes.Mutating(quotes => quotes.RemoveAt(quotes.Count - 1)),
                    });
                    return true;
                }
                return false;
        }
        // Swift's switch over ComposerKey is exhaustive and every case above returns.
        throw new System.Diagnostics.UnreachableException();
    }

    // :370-382
    private void Send()
    {
        if (runtime.Draft.IsEmpty) return; // :371
        historyIndex = null; // :372
        state.Suggestions = new SuggestionState(); // :373
        // :375-378 Return while the turn runs stops it and sends right away. With heads at work it queues instead (a
        // head takes it, or it waits for the lead): the heads run inside the lead's session, and a stop would kill them.
        if (runtime.IsRunning) runtime.InterruptAndSend();
        else runtime.Send(); // :380
    }

    // :384-398 Command-Return. MARROW_STUB: part 2 of this unit (PR-COMPOSER-031).
    private void Steer() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:388");

    // :400-422
    private bool Recall(bool older)
    {
        var prompts = runtime.SentPrompts.Where(prompt => prompt.Length != 0).ToList(); // :401
        if (prompts.Count == 0 || runtime.Draft.Attachments.Count != 0) return false; // :402
        var text = runtime.Draft.Text; // :403
        // :404
        int? recalledIndex = historyIndex is { } index && index >= 0 && index < prompts.Count && SwiftString.Equal(prompts[index], text)
            ? index
            : null;
        if (!(text.Length == 0 || recalledIndex is not null)) return false; // :405
        if (older)
        {
            var next = (recalledIndex ?? prompts.Count) - 1; // :407
            if (next < 0) return true; // :408
            historyIndex = next; // :409
            runtime.Draft = runtime.Draft with { Text = prompts[next] }; // :410
        }
        else
        {
            if (recalledIndex is not { } recalled) return false; // :412
            if (recalled + 1 < prompts.Count) // :413
            {
                historyIndex = recalled + 1; // :414
                runtime.Draft = runtime.Draft with { Text = prompts[recalled + 1] }; // :415
            }
            else
            {
                historyIndex = null; // :417
                runtime.Draft = runtime.Draft with { Text = "" }; // :418
            }
        }
        return true; // :421
    }

    // :426-437 One refresh per keystroke. The text view reports a keystroke twice, as the change and as the caret's
    // move, and each report scored the whole file index; the earlier refresh is dropped and the one that runs reads the
    // caret where it is then. Written with part 1 because the text view reports every change of its text here
    // (ComposerTextView.swift:317-327), its first one included.
    private void CursorMoved(int _)
    {
        dismissedSuggestions = false; // :430
        if (runtime.Draft.Text.Length != 0) runtime.WarmSession(); // :431
        controller.SuggestionRefresh?.Cancel(); // :432
        controller.SuggestionRefresh = MainActor.Spawn(async () => // :433
        {
            if (dismissedSuggestions) return; // :434
            await RefreshSuggestions(controller.CursorLocation); // :435
        });
    }

    // :439-473 The word before the caret, back to a space, newline or tab, offers files after @ and commands after a /
    // at the very start. Written with part 1 for the same reason as cursorMoved; what it calls for the two lists
    // (FileIndex.search, commandSuggestions) is part 2's.
    private async Task RefreshSuggestions(int cursor)
    {
        var text = runtime.Draft.Text; // :440 as NSString: UTF-16 offsets (R-STR-1)
        if (!(cursor > 0 && cursor <= text.Length)) // :441
        {
            if (state.Suggestions.IsVisible) state.Suggestions = new SuggestionState(); // :442
            return;
        }
        var start = cursor; // :445
        while (start > 0) // :446
        {
            var character = text[start - 1]; // :447
            if (character == 0x20 || character == 0x0A || character == 0x09) break; // :448
            start -= 1; // :449
        }
        var range = (Location: start, Length: cursor - start); // :451
        var word = text.Substring(range.Location, range.Length); // :452
        var kind = SuggestionState.Kind.File; // :453
        var items = ValueList<Suggestion>.Empty; // :454
        if (SwiftString.HasPrefix(word, "@")) // :455
        {
            var paths = await fileIndex.Search(SwiftString.DropFirst(word)); // :456
            // :457-459 The caret moved on while the index was searched: the newer keystroke's refresh is under way,
            // and this answer is for a word that is gone.
            if (SwiftTask.IsCancelled) return;
            items = new ValueList<Suggestion>(paths.Select(path => new Suggestion // :460-462
            {
                Value = "@" + path + " ",
                Title = SwiftPath.LastPathComponent(path),
                Detail = path,
                Symbol = "doc",
            }));
        }
        else if (SwiftString.HasPrefix(word, "/") && start == 0) // :463
        {
            kind = SuggestionState.Kind.Command; // :464
            items = CommandSuggestions(SwiftString.DropFirst(word)); // :465
        }
        if (items.Count == 0) // :467
        {
            if (state.Suggestions.IsVisible) state.Suggestions = new SuggestionState(); // :468
            return;
        }
        var selected = state.Suggestions.Range.Location == range.Location // :471
            ? Math.Min(state.Suggestions.Selected, items.Count - 1)
            : 0;
        state.Suggestions = new SuggestionState { SuggestionKind = kind, Range = range, Items = items, Selected = selected }; // :472
    }

    // :475-485 private func commandSuggestions(matching:). MARROW_STUB: part 2 of this unit (PR-COMPOSER-040 to
    // PR-COMPOSER-043).
    private ValueList<Suggestion> CommandSuggestions(string matching) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:475");

    // :487-505 private func pick(_:). MARROW_STUB: part 2 of this unit (PR-COMPOSER-049).
    private void Pick(Suggestion suggestion) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:487");

    // :507-520 private func attach(_:). MARROW_STUB: part 2 of this unit (PR-COMPOSER-059).
    private void Attach(IReadOnlyList<AttachmentSource> sources) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:507");

    // :526-533 private func chooseFiles(). MARROW_STUB: part 3 of this unit (PR-COMPOSER-063).
    private void ChooseFiles() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:526");
}

/// <summary>:536-607 <c>private struct ComposerTextColumn: View</c>.</summary>
internal sealed class ComposerTextColumn : Decorator
{
    // The @State the body reads (R-VIEW-2).
    private sealed class ViewState : MainActorModel
    {
        private double textHeight = 20;

        // :552 @State private var textHeight: CGFloat = 20
        public double TextHeight { get => Read(textHeight); set => Set(ref textHeight, value); }
    }

    // The .softAppear of a notice that was there when the column was made (Chrome.swift:949-955): only the removal
    // half, the 0.12 s ease-out fade, is left for when it goes (a view present from the first render does not
    // transition in: documented SwiftUI behaviour, UNVERIFIED under OD-0001 as ED-092 records).
    private static SwiftTransition ArrivedWithColumn =>
        SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Identity,
            removal: SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.12)));

    private const double MinComposerHeight = 20; // :554
    private const long MaxComposerLines = 5; // :555

    private readonly ViewState state = new();
    private readonly StackPanel stack;
    private readonly ComposerTextView textView;
    private readonly Border clip;
    private readonly Func<ComposerKey, bool> onKey;
    private readonly Action<IReadOnlyList<AttachmentSource>> onAttach;
    private string placeholder;
    private string? attachmentNotice;
    private TextBlock? notice;

    // :556-561. DEVIATION OD-0009: the port's text face is Inter, so the line is Inter's at 14 points; its wrap points can
    // differ from the macOS face's.
    private static double MaxComposerHeight
    {
        get
        {
            var font = SwiftFont.SystemFont(14).Resolve(); // :557 NSFont.systemFont(ofSize: 14)
            FontManager.Current.TryGetGlyphTypeface(new Typeface(font.FontFamily, font.FontStyle, font.FontWeight), out var glyph);
            var metrics = SwiftTraps.Unwrap(glyph).Metrics;
            // NSFont's ascender, descender and leading are the face's design metrics at the point size: Avalonia keeps
            // the ascent negative (above the baseline) and the descent positive (below it).
            var scale = font.FontSize / metrics.DesignEmHeight;
            var ascender = -metrics.Ascent * scale;
            var descender = -metrics.Descent * scale;
            var leading = metrics.LineGap * scale;
            var line = Math.Ceiling(ascender - descender + leading); // :558
            // Matches ComposerTextView's textContainerInset.height * 2.
            return line * MaxComposerLines + 4; // :559-560
        }
    }

    // :563-565 min(max(textHeight, minComposerHeight), maxComposerHeight), with Swift's max and min.
    private double ComposerHeight
    {
        get
        {
            var textHeight = state.TextHeight;
            var floored = MinComposerHeight >= textHeight ? MinComposerHeight : textHeight;
            var ceiling = MaxComposerHeight;
            return ceiling < floored ? ceiling : floored;
        }
    }

    // :542-550 the view's inputs; :567-606 body
    public ComposerTextColumn(
        ThreadRuntime runtime,
        ComposerController controller,
        string placeholder,
        bool takesFocusOnAppear,
        string? attachmentNotice,
        Func<ComposerKey, bool> onKey,
        Action<IReadOnlyList<AttachmentSource>> onAttach,
        Action<int> onCursorChange,
        Action onBlur)
    {
        this.placeholder = placeholder;
        this.attachmentNotice = attachmentNotice;
        this.onKey = onKey;
        this.onAttach = onAttach;

        // :568-573 Spacing 0 and the strip carries its own gap: the strip reads the draft itself, so it stays a
        // zero-height subview while there is nothing attached and the composer never has to look at the draft to decide
        // whether to stack it.
        stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new DraftChips(runtime)); // :574
        stack.Children.Add(new DraftAttachments(runtime)); // :575
        if (attachmentNotice is { } text) stack.Children.Add(Notice(text).Transition(ArrivedWithColumn)); // :576-583

        // :584-594
        textView = new ComposerTextView(
            text: new Binding<string>(() => runtime.Draft.Text, value => runtime.Draft = runtime.Draft with { Text = value }),
            height: new Binding<double>(() => state.TextHeight, value => state.TextHeight = value),
            placeholder: placeholder,
            controller: controller,
            onKey: onKey,
            onAttach: onAttach,
            onCursorChange: onCursorChange,
            onBlur: onBlur,
            takesFocusOnAppear: takesFocusOnAppear)
        {
            VerticalAlignment = VerticalAlignment.Top, // :601 alignment: .top
        };
        // :595-602 The text view itself takes its new height at once (.transaction { $0.animation = nil }), so it lays out
        // every line with nothing to scroll; only the clip around it grows and shrinks.
        clip = new Border { Child = textView, ClipToBounds = true };
        stack.Children.Add(clip);
        Child = stack;
        ApplyHeight();
        this.Track(() => ComposerHeight, _ => ApplyHeight());
        // ComposerTextView.swift:293-306 updateNSView reads the draft through the text binding, so a draft written
        // elsewhere (recall, a send, a take-back) reaches the text view.
        this.Track(() => runtime.Draft.Text, _ => textView.Update(this.placeholder, this.onKey, this.onAttach));
    }

    // The parent's body run again with new values for :544 placeholder and :546 attachmentNotice (R-VIEW-1).
    internal void Update(string placeholder, string? attachmentNotice)
    {
        this.placeholder = placeholder;
        textView.Update(placeholder, onKey, onAttach); // :587
        if (attachmentNotice == this.attachmentNotice) return;
        var previous = this.attachmentNotice;
        this.attachmentNotice = attachmentNotice;
        if (previous is null && attachmentNotice is { } arriving)
        {
            // :576-582 .transition(.softAppear)
            SwiftTransition.Insert(stack, 2, Notice(arriving).Transition(SwiftTransition.SoftAppear));
        }
        else if (attachmentNotice is null)
        {
            if (notice is { } leaving) SwiftTransition.Remove(stack, leaving);
            notice = null;
        }
        else if (notice is { } shown)
        {
            shown.Text = attachmentNotice; // the same Text with new words
        }
    }

    // :577-581
    private TextBlock Notice(string text)
    {
        var block = new TextBlock
        {
            Text = text, // :577 Text(verbatim: attachmentNotice)
            MaxLines = 1, // :580 .lineLimit(1), truncating at the tail
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left, // VStack(alignment: .leading)
            Margin = new Thickness(0, 0, 0, 6), // :581
        };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(block); // :578 .font(.system(.subheadline))
        block.Foreground = Chrome.Warning.Brush(block); // :579
        notice = block;
        return block;
    }

    private void ApplyHeight()
    {
        var height = ComposerHeight;
        textView.Height = height; // :599 .frame(height: composerHeight), :600 with no animation
        clip.Height = height; // :601-602 .frame(height: composerHeight, alignment: .top).clipped()
        // MARROW_STUB :603-604 .animation(liveResize.isReshaping ? nil : .smooth(duration: 0.28), value: composerHeight)
        // is not written: WindowLiveResize is not reachable from a view yet (STOP App.Views.Composer.ComposerView #1 in
        // ComposerViewTests.cs), so the clip takes the height at once.
    }
}

/// <summary>:609-657 <c>private struct ComposerControls: View</c>.</summary>
internal sealed class ComposerControls : Decorator
{
    // :613-623 the view's inputs; :625-656 body
    public ComposerControls(
        ThreadRuntime runtime,
        ChatThread thread,
        bool compact,
        bool recentDownloadsPicker,
        bool goesToHead,
        bool queueGoesToHead,
        ProviderKind? headsProvider,
        Binding<bool> showingRecents,
        Action<IReadOnlyList<string>> onAttach,
        Action onChooseFiles,
        Action onSend) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:625");
}

/// <summary>:659-675 <c>private struct ComposerWorkingDotsSlot: View</c>.</summary>
internal sealed class ComposerWorkingDotsSlot : Decorator
{
    // :662 let runtime; :664-674 body. Reading isRunning here keeps the pill's body off the run flag, so starting and
    // stopping a turn repaints the dots alone.
    public ComposerWorkingDotsSlot(ThreadRuntime runtime) =>
        this.Track(() => runtime.IsRunning, running => Child = running ? Dots() : null); // :665

    // :666-672 the dots, their frame, clip and transition. MARROW_STUB: part 3 of this unit (PR-COMPOSER-101).
    private static Control Dots() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:666");
}

/// <summary>:679-690 <c>private struct ModelEffortSlot: View</c>.</summary>
internal sealed class ModelEffortSlot : Decorator
{
    // :683-685 the view's inputs; :687-689 body
    public ModelEffortSlot(ThreadRuntime runtime, ChatThread thread, bool compact) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:687");
}

/// <summary>:692-773 <c>private struct ContextMeter: View</c>.</summary>
internal sealed class ContextMeter : Decorator
{
    // :698-700 the view's inputs; :704-762 body
    public ContextMeter(ThreadRuntime runtime, ProviderKind provider, ProviderKind? headsProvider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:704");
}

/// <summary>:775-781 <c>@MainActor private enum SendShortcutText</c>, a caseless enum used as a namespace.</summary>
internal static class SendShortcutText
{
    // :779 static let queue = ShortcutStore.label(for: .queueChat)
    public static string? Queue =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:779");

    // :780 static let stop = ShortcutStore.hint(for: .stopTurn)
    public static string Stop =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:780");
}

/// <summary>:783-796 <c>private struct SendButton: View</c>.</summary>
internal sealed class SendButton : Decorator
{
    // :784-791 the view's inputs; :793-795 body
    public SendButton(ThreadRuntime runtime, bool goesToHead, bool queueGoesToHead, Action send) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:793");
}

/// <summary>:798-813 <c>private struct SendRunState: View</c>.</summary>
internal sealed class SendRunState : Decorator
{
    // :801-804 the view's inputs; :806-812 body
    public SendRunState(ThreadRuntime runtime, bool goesToHead, bool queueGoesToHead, Action send) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:806");
}

/// <summary>:815-866 <c>private struct SendDraftState: View</c>.</summary>
internal sealed class SendDraftState : Decorator
{
    // :818-823 the view's inputs; :825-844 body
    public SendDraftState(
        ThreadRuntime runtime,
        bool isRunning,
        bool headsWorking,
        bool goesToHead,
        bool queueGoesToHead,
        Action send) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:825");
}

/// <summary>:868-944 <c>private struct DraftChips: View</c>.</summary>
internal sealed class DraftChips : Decorator
{
    /// <summary>:871-874 <c>private struct ChipKey: Equatable</c>.</summary>
    private sealed record ChipKey
    {
        public required ValueList<Guid> QuoteIDs { get; init; } // :872
        public string? CommandName { get; init; } // :873
    }

    // :869 let runtime; :876-943 body
    public DraftChips(ThreadRuntime runtime) =>
        this.Track(() => (runtime.Draft.Quotes, runtime.Draft.Command), draft => Show(draft.Quotes, draft.Command)); // :877-878

    private void Show(ValueList<ReplyQuote> quotes, DraftCommand? command)
    {
        // :879 nothing to show: no row, so the column's zero spacing leaves no room for it
        if (quotes.Count == 0 && command is null)
        {
            Child = null;
            return;
        }
        var chipRow = Row(quotes, command);
        chipRow.Margin = new Thickness(0, 0, 0, 8); // :939 .padding(.bottom, 8)
        Child = chipRow;
    }

    // :880-938 the chips' scroller, and :940-941 its transition and animation. MARROW_STUB: part 3 of this unit
    // (PR-COMPOSER-086 to PR-COMPOSER-089).
    private static Control Row(ValueList<ReplyQuote> quotes, DraftCommand? command) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:880");
}

/// <summary>:946-959 <c>private struct ChipRowCollapse: ViewModifier</c>.</summary>
internal sealed record ChipRowCollapse(bool Collapsed)
{
    // :953-958 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:953");
}

/// <summary>:961-970 <c>struct ChipChrome: ViewModifier</c>: the capsule behind a composer chip.</summary>
public sealed record ChipChrome
{
    // :963-969 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:963");
}

/// <summary>:972-1033 <c>private struct DraftAttachments: View</c>.</summary>
internal sealed class DraftAttachments : Decorator
{
    // :973 let runtime; :980-988 body
    public DraftAttachments(ThreadRuntime runtime) =>
        this.Track(() => runtime.Draft.Attachments, Show); // :981

    private void Show(ValueList<Attachment> attachments)
    {
        // :982 an empty strip takes no room at all
        if (attachments.Count == 0)
        {
            Child = null;
            return;
        }
        var shown = Strip(attachments); // :983
        // :984-986 The strip's own gap to the text below it: the composer stacks with no spacing.
        shown.Margin = new Thickness(0, 0, 0, 8);
        Child = shown;
    }

    // :990-1032 private func strip(_:), with :975-978 its @State. MARROW_STUB: part 3 of this unit (PR-COMPOSER-067,
    // PR-COMPOSER-068).
    private static Control Strip(ValueList<Attachment> attachments) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:990");
}

/// <summary>:1037-1049 <c>struct SuggestionState: Equatable</c>.</summary>
public sealed record SuggestionState
{
    /// <summary>:1038-1041 <c>enum Kind</c>.</summary>
    public enum Kind
    {
        File, // :1039
        Command, // :1040
    }

    public Kind SuggestionKind { get; init; } = Kind.File; // :1043 Swift: kind (R-NAME-3)
    public (int Location, int Length) Range { get; init; } = (0, 0); // :1044 NSRange(location: 0, length: 0)
    public ValueList<Suggestion> Items { get; init; } = ValueList<Suggestion>.Empty; // :1045
    public long Selected { get; init; } = 0; // :1046

    // :1048 var isVisible: Bool { !items.isEmpty }
    public bool IsVisible => Items.Count != 0;
}

/// <summary>:1051-1059 <c>struct Suggestion: Identifiable, Equatable</c>.</summary>
public sealed record Suggestion
{
    public required string Value { get; init; } // :1052
    public required string Title { get; init; } // :1053
    public required string Detail { get; init; } // :1054
    public required string Symbol { get; init; } // :1055
    public SlashCommand? Command { get; init; } = null; // :1056

    // :1058 var id: String { value }
    public string Id =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:1058");
}

/// <summary>:1061-1101 <c>private struct SuggestionPopoverRow: View</c>.</summary>
internal sealed class SuggestionPopoverRow : Decorator
{
    // :1062-1065 the view's inputs; :1069-1100 body
    public SuggestionPopoverRow(Suggestion item, bool isSelected, Action select, Action pick) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:1069");
}

/// <summary>:1103-1288 <c>@MainActor @Observable final class FileIndex</c> (R-OBS-6): paths for <c>@</c> mentions.
/// Its stored properties are all <c>@ObservationIgnored</c> (R-OBS-5).</summary>
public sealed class FileIndex : MainActorModel
{
    /// <summary>:1107-1116 <c>nonisolated private struct Entry: Sendable</c>.</summary>
    private sealed record Entry
    {
        public required string Path { get; init; } // :1108
        public required string Lowercased { get; init; } // :1109
        public required string Name { get; init; } // :1110
        public required long LengthPenalty { get; init; } // :1113
        public required bool IsASCII { get; init; } // :1115
    }

    private const double Freshness = 60; // :1127

    // :1129-1159 func prepare(_:) async
    public Task Prepare(string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:1129");

    // :1179-1183 func search(_:limit:) async -> [String]
    public Task<ValueList<string>> Search(string query, long limit = 8) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:1179");

    // :1235-1254 nonisolated static func isSubsequence(_:of:ascii:) -> Bool
    public static bool IsSubsequence(string needle, string of, bool ascii) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/ComposerView.swift:1235");
}

/// <summary>:1290-1307 <c>private struct ComposerSurface: ViewModifier</c>.</summary>
internal sealed record ComposerSurface
{
    // :1297-1306 func body(content:)
    public Control Body(Control content)
    {
        // :1298 let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        var shape = new Border { Child = content }.RoundedRectangle(22, RoundedCornerStyle.Continuous);

        void Draw()
        {
            // :1294 @Environment(\.isOnGlassPanel)
            if (RenderBudget.GetIsOnGlassPanel(shape))
            {
                // :1299-1302 inside a floating glass panel (a helper's, a head's), the panel's flat control fill for the
                // appearance (:1295 @Environment(\.colorScheme)), clipped to the shape: glass on the panel's glass sampled
                // the same pixels twice on every frame the transcript moved under it.
                SwiftGlass.SetEffect(shape, null);
                shape.Background = SwiftColor.Dynamic(
                    light: Chrome.PanelControlFill(isDark: false),
                    dark: Chrome.PanelControlFill(isDark: true)).Brush(shape);
                shape.ClipToBounds = true;
            }
            else
            {
                // :1304 content.glassEffect(.regular, in: shape). The material is recorded as data on the layer (ED-180);
                // over the desktop the window's Acrylic lies under it (OD-0005, OD-0014).
                // MARROW_DEFERRED ED-180: the app-drawn glass material.
                shape.Background = null;
                shape.ClipToBounds = false;
                SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
            }
        }

        shape.PropertyChanged += (_, e) =>
        {
            if (e.Property == RenderBudget.IsOnGlassPanelProperty) Draw();
        };
        Draw();
        return shape;
    }
}
