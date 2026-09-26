using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Diff;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
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
//
// Part 2 of 5 (B3 52bc792, unchanged from B2): ComposerView's body modifiers :190-218 (the list's show and hide, the
// hide on disappear, thread change and hidden column, the column-shown focus, the command and file index tasks and the
// command observers), suggestionMenu (:239-262), commandKey and loadCommands (:264-273), steer (:384-398),
// commandSuggestions (:475-485), pick (:487-501), attach and note (:505-524), Suggestion.id (:1058),
// SuggestionPopoverRow (:1061-1101) and FileIndex (:1103-1288); the pick arm of handleKey now follows upstream defect
// 50's windows_path. SlashCommand.suggestions (Provider.swift:306-323), which commandSuggestions ranks with, is ported
// in Provider.cs. Still MARROW_STUB for parts 3 to 5: the notice's timeout (:219-225), chooseFiles (:526-533),
// ComposerControls and the views under it, the chip row, the attachment strip, ChipRowCollapse and ChipChrome.

/// <summary>:4-58 <c>struct ComposerArea: View</c>.</summary>
public sealed class ComposerArea : Decorator
{
    private readonly ThreadRuntime runtime;

    // :18-20 @State private var diffPopover = DiffPopoverCoordinator(): the changes popover presented from the tab.
    // Driven by runtime.isDiffVisible, so every opener (tab, Review, the changes chord) shares it.
    private readonly DiffPopoverCoordinator diffPopover = new();

    // :25-27 the approval cards, keyed by request id (R-VIEW-8), with the request each card was drawn from.
    private readonly StackPanel approvalStack;
    private readonly Dictionary<string, Control> approvalRows = new(SwiftStringComparer.Instance);
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
        approvalStack = stack;

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
        void Apply()
        {
            var keep = new System.Collections.Generic.HashSet<string>(ids, SwiftStringComparer.Instance);
            foreach (var id in approvalRows.Keys.Where(id => !keep.Contains(id)).ToArray())
            {
                SwiftTransition.Remove(approvalStack, approvalRows[id]);
                approvalRows.Remove(id);
                drawnRequests.Remove(id);
            }
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                if (approvalRows.TryGetValue(request.Id, out var row))
                {
                    UpdateCard(row, request);
                    var at = approvalStack.Children.IndexOf(row);
                    if (at != index) approvalStack.Children.Move(at, index);
                }
                else
                {
                    approvalRows[request.Id] = row = MakeCard(request);
                    SwiftTransition.Insert(approvalStack, index, row);
                }
            }
        }
        if (changed) Transaction.WithAnimation(Chrome.PanelSlide, Apply);
        else Apply();
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
    private readonly string? workingDirectory; // :120
    private readonly bool compactModelChip;
    private readonly bool takesFocusOnAppear; // :123
    private readonly ComposerController controller = new(); // :126
    private readonly ViewState state = new();
    private readonly ComposerTextColumn textColumn;
    private readonly Grid row;
    private bool dismissedSuggestions; // :128-129 Escape closed the list; the next caret move may open it again.
    private int? historyIndex; // :130
    private readonly FileIndex fileIndex = new(); // :132
    private ComposerControls? controls;
    private (ChatThread Thread, bool RecentDownloadsPicker, bool GoesToHead, bool QueueGoesToHead, ProviderKind? HeadsProvider)? drawnControls;

    // :190-215 the .onChange observers, alive while the pill is on screen.
    private readonly List<IDisposable> observers = [];
    private bool onScreen;

    // :239-262 the hosted list. ENGINEERING DECISION: it keeps its identity from one show to the next, as the
    // NSHostingController whose rootView each show replaces keeps SwiftUI's (ComposerTextView.swift:72-78): the scroller,
    // its offset and the rows with their hover state survive, and each show hands them the current values.
    private PopoverMenu? menu;
    private StackPanel? menuHead;
    private StackPanel? menuRowsPanel;
    private ForEach<(int Index, Suggestion Item), int>? menuRows;
    private PopoverSectionHeader? menuHeader;
    private string? menuHeaderTitle;
    private (PopoverNote Note, PopoverItem Retry, string Error)? menuFailure;
    private long? menuSelected;

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
        this.workingDirectory = workingDirectory;
        this.compactModelChip = compactModelChip;
        this.takesFocusOnAppear = takesFocusOnAppear;

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

        // :190-203, :205-215 the list's show and hide, and the observers of the commands, their errors and the list's
        // visibility, alive while the pill is on screen.
        AttachedToVisualTree += (_, _) => Appear();
        DetachedFromVisualTree += (_, _) => Disappear();
        // :201-203 The column shown again takes the keyboard back, as it did when it first appeared; one going behind
        // another drops its list.
        PropertyChanged += (_, e) =>
        {
            if (!onScreen || e.Property != Views.RootView.ChatColumnIsShownProperty) return;
            if (Views.RootView.GetChatColumnIsShown(this))
            {
                if (this.takesFocusOnAppear) controller.Focus();
            }
            else
            {
                controller.HideSuggestions();
            }
        };
        // :204 .task(id: commandKey) { await loadCommands() }
        this.Task(() => CommandKey, async _ => await LoadCommands());
        // :216-218 .task(id: workingDirectory)
        this.Task(() => this.workingDirectory, async directory =>
        {
            if (directory is { } prepared) await fileIndex.Prepare(prepared);
        });
        // :219-225 The string is the task key: equal notices do not restart the wait.
        this.Task(() => state.AttachmentNotice, async notice =>
        {
            if (notice is null) return;
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(5));
            if (SwiftTask.IsCancelled) return;
            Transaction.WithAnimation(Chrome.PanelSlide, () => state.AttachmentNotice = null);
        });
    }

    private void Appear()
    {
        onScreen = true;
        // :190-196
        observers.Add(new OnChange<SuggestionState>(() => state.Suggestions, (_, now) =>
        {
            if (now.IsVisible) controller.ShowSuggestions(SuggestionMenu, now.Items.Count);
            else controller.HideSuggestions();
        }));
        // :198 The runtime is this control's input, fixed for its life, so this observer does not fire here.
        observers.Add(new OnChange<Guid>(() => runtime.ThreadID, (_, _) => controller.HideSuggestions()));
        // :205-207
        observers.Add(new OnChange<ValueList<SlashCommand>?>(LoadedCommands, (_, _) =>
        {
            if (controller.HasFocus) CursorMoved(controller.CursorLocation);
        }));
        // :208-212
        observers.Add(new OnChange<string?>(CommandError, (_, _) =>
        {
            if (controller.HasFocus && state.Suggestions.IsVisible)
                controller.ShowSuggestions(SuggestionMenu, state.Suggestions.Items.Count);
        }));
        // :213-215
        observers.Add(new OnChange<bool>(() => state.Suggestions.IsVisible, (_, visible) =>
        {
            if (visible && state.Suggestions.SuggestionKind == SuggestionState.Kind.Command)
                MainActor.Spawn(async () => await LoadCommands());
        }));
    }

    private void Disappear()
    {
        onScreen = false;
        foreach (var observer in observers) observer.Dispose();
        observers.Clear();
        controller.HideSuggestions(); // :197
    }

    // :205 model.providers.commands[commandKey]
    private ValueList<SlashCommand>? LoadedCommands() =>
        Model.Providers.Commands.TryGetValue(CommandKey, out var commands) ? commands : null;

    // :208 model.providers.commandErrors[commandKey]
    private string? CommandError() =>
        Model.Providers.CommandErrors.TryGetValue(CommandKey, out var error) ? error : null;

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

    // :239-262 The slash/@ list hosted in the caret-anchored popover (see ComposerController). Internal for this unit's
    // tests (InternalsVisibleTo Marrow.App.Tests); private in Swift.
    internal Control SuggestionMenu()
    {
        if (menu is null)
        {
            // :243 PopoverMenu { ... }: its stack holds the header and the failure rows, then one row per item.
            var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            menuHead = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            menuRowsPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            content.Children.Add(menuHead);
            content.Children.Add(menuRowsPanel);
            menu = new PopoverMenu(() => content);
            // :249 ForEach(suggestions.items.indices, id: \.self), each row :257 .id(index)
            menuRows = new ForEach<(int Index, Suggestion Item), int>(menuRowsPanel, row => row.Index, MakeRow, UpdateRow);
        }
        else if (menu.Parent is Decorator host)
        {
            host.Child = null; // the frame of the previous show gives the hosted list up to this one's
        }
        var suggestions = state.Suggestions;

        // :244 PopoverSectionHeader: the command list's header, or "Files"
        var title = suggestions.SuggestionKind == SuggestionState.Kind.Command
            ? "Commands \U000000B7 Recently used first" // U+00B7 MIDDLE DOT
            : "Files";
        if (menuHeader is null || menuHeaderTitle != title)
        {
            if (menuHeader is not null) menuHead!.Children.Remove(menuHeader);
            menuHeader = new PopoverSectionHeader(title);
            menuHeaderTitle = title;
            menuHead!.Children.Insert(0, menuHeader);
        }

        // :245-248 A command list whose load failed says why, with a way to try again.
        if (suggestions.SuggestionKind == SuggestionState.Kind.Command && CommandError() is { } error)
        {
            if (menuFailure is not { } failure)
            {
                var note = new PopoverNote(error); // :246
                // :247 PopoverItem("Retry loading commands", symbol: "arrow.clockwise") { Task { await loadCommands(force: true) } }
                var retry = new PopoverItem(
                    "Retry loading commands",
                    () => MainActor.Spawn(async () => await LoadCommands(force: true)),
                    symbol: "arrow.clockwise");
                menuHead!.Children.Add(note);
                menuHead.Children.Add(retry);
                menuFailure = (note, retry, error);
            }
            else if (failure.Error != error)
            {
                // A new description in the same note; the row after it keeps its state.
                var note = new PopoverNote(error);
                menuHead!.Children[menuHead.Children.IndexOf(failure.Note)] = note;
                menuFailure = (note, failure.Retry, error);
            }
        }
        else if (menuFailure is { } gone)
        {
            menuHead!.Children.Remove(gone.Note);
            menuHead.Children.Remove(gone.Retry);
            menuFailure = null;
        }

        menuRows!.Apply(suggestions.Items.Select((item, index) => (index, item)).ToList()); // :249-258

        // :260 .onChange(of: suggestions.selected) { _, selected in proxy.scrollTo(selected) }. The row is brought into
        // view on the next main turn, once this show has put the list in the popover.
        if (menuSelected is { } previous && previous != suggestions.Selected
            && suggestions.Selected >= 0 && suggestions.Selected < menuRowsPanel!.Children.Count)
        {
            var target = menuRowsPanel.Children[(int)suggestions.Selected];
            MainActor.Post(() => target.BringIntoView());
        }
        menuSelected = suggestions.Selected;
        return menu;
    }

    // :250-256
    private Control MakeRow((int Index, Suggestion Item) row) =>
        new SuggestionPopoverRow(
            row.Item,
            isSelected: row.Index == state.Suggestions.Selected, // :253
            select: () => state.Suggestions = state.Suggestions with { Selected = row.Index }, // :254
            pick: () => Pick(row.Item)); // :255

    private void UpdateRow(Control control, (int Index, Suggestion Item) row) =>
        ((SuggestionPopoverRow)control).Update(
            row.Item,
            isSelected: row.Index == state.Suggestions.Selected,
            select: () => state.Suggestions = state.Suggestions with { Selected = row.Index },
            pick: () => Pick(row.Item));

    // :264-267
    private string CommandKey
    {
        get
        {
            if (Model.Thread(runtime.ThreadID) is not { } thread || workingDirectory is not { } directory) return ""; // :265
            return ProviderRegistry.CommandKey(thread.Provider, directory); // :266
        }
    }

    // :269-273. Internal for this unit's tests (InternalsVisibleTo Marrow.App.Tests); private in Swift.
    internal async Task LoadCommands(bool force = false)
    {
        var model = Model;
        if (model.Thread(runtime.ThreadID) is not { } thread || workingDirectory is not { } directory) return; // :270
        await model.Providers.LoadCommands(thread.Provider, directory, force); // :271
        if (controller.HasFocus) CursorMoved(controller.CursorLocation); // :272
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
                    // UPSTREAM DEFECT 50: Swift subscripts items with selected unchecked and would trap on an index out
                    // of range; PR-COMPOSER-045's windows_path makes that case a no-op.
                    if (state.Suggestions.Selected >= 0 && state.Suggestions.Selected < state.Suggestions.Items.Count)
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

    // :384-398 Command-Return: steer the running turn by queueing the draft behind it. Idle, there is nothing to steer
    // behind, so it just sends. The menu owns the chord while a turn runs; this path only sees it when the command is
    // disabled or rebound, so it falls back to send once the chord moves.
    private void Steer()
    {
        if (runtime.Draft.IsEmpty) return; // :389
        historyIndex = null; // :390
        state.Suggestions = new SuggestionState(); // :391
        // :392-393 KeyChord(keyCode: 36, modifiers: .command) is Return with Control on Windows (ED-099); the stored chord
        // is compared, not the key event.
        if (runtime.CanQueue && ShortcutStore.Shared.Current.Chord(AppShortcut.QueueChat) == new KeyChord(0x1C, ChordModifiers.Control))
        {
            runtime.QueueDraftAsFollowUp(); // :394
        }
        else
        {
            runtime.Send(); // :396
        }
    }

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

    // The list's state and the controller, for this unit's tests (InternalsVisibleTo Marrow.App.Tests).
    internal SuggestionState Suggestions => state.Suggestions;

    internal ComposerController Controller => controller;

    internal string? AttachmentNotice => state.AttachmentNotice;

    // :475-485 The built-in commands, then the provider's, ranked for the query (Provider.swift:306-323).
    private ValueList<Suggestion> CommandSuggestions(string matching)
    {
        if (Model.Thread(runtime.ThreadID) is not { } thread) return ValueList<Suggestion>.Empty; // :476
        var commands = new List<SlashCommand> { new() { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true } }; // :477
        if (thread.Provider.SupportsCompact) // :478
            commands.Add(new SlashCommand { Name = "compact", Detail = "Summarize the conversation to free up context", IsBuiltIn = true }); // :479
        if (Model.Providers.Commands.TryGetValue(CommandKey, out var loaded)) commands.AddRange(loaded); // :481
        var recent = Model.Providers.RecentCommands.TryGetValue(SwiftEnum<ProviderKind>.RawValue(thread.Provider), out var names)
            ? names
            : ValueList<string>.Empty; // :482
        return new ValueList<Suggestion>(SlashCommand.Suggestions(commands, matching, recent).Select(command => new Suggestion // :482-484
        {
            Value = "/" + command.Name + " ",
            Title = "/" + command.Name,
            Detail = command.Detail,
            Symbol = command.IsBuiltIn ? "command" : "sparkles",
            Command = command,
        }));
    }

    // :487-501 A command becomes a chip before the text; anything else is inserted in place of the typed word.
    private void Pick(Suggestion suggestion)
    {
        var range = state.Suggestions.Range; // :488
        var wasCommand = state.Suggestions.SuggestionKind == SuggestionState.Kind.Command; // :489
        state.Suggestions = new SuggestionState(); // :490
        if (wasCommand && suggestion.Command is { } c) // :491
        {
            controller.ReplaceCharacters(range.Location, range.Length, ""); // :492
            Transaction.WithAnimation(Chrome.PanelSlide, () => // :493-495
                runtime.Draft = runtime.Draft with { Command = new DraftCommand { Name = c.Name, Detail = c.Detail, IsBuiltIn = c.IsBuiltIn } });
            controller.Focus(); // :496
            return;
        }
        controller.ReplaceCharacters(range.Location, range.Length, suggestion.Value); // :499
        controller.Focus(); // :500
    }

    // :505-520 Files and images go through Storage.attach: the slots are counted here, the copies and transcodes run off
    // the main actor, and what lands past the cap is said.
    private void Attach(IReadOnlyList<AttachmentSource> sources)
    {
        var room = Math.Max(0, MaxAttachments - runtime.Draft.Attachments.Count); // :508
        var dropped = sources.Count - Math.Min(sources.Count, room); // :509
        // :510 Storage.attach(sources, to: Bindable(runtime).draft.attachments)
        Storage.Attach(sources, () => runtime.Draft.Attachments, attachments => runtime.Draft = runtime.Draft with { Attachments = attachments });
        if (dropped > 0) // :511
        {
            var isImage = sources.Count == 1 && sources[0] is AttachmentSource.Image; // :512
            var limit = MaxAttachments.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Note(isImage // :513-517
                ? "The image was left out: a message holds " + limit + " attachments"
                : dropped == 1
                    ? "One file was left out: a message holds " + limit + " attachments"
                    : dropped.ToString(System.Globalization.CultureInfo.InvariantCulture) + " files were left out: a message holds " + limit + " attachments");
        }
        controller.Focus(); // :519
    }

    // :522-524
    private void Note(string message) => Transaction.WithAnimation(Chrome.PanelSlide, () => state.AttachmentNotice = message);

    // :526-533 The native seam carries prompt as the accept label, not the title.
    private void ChooseFiles()
    {
        var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        MainActor.Spawn(async () =>
        {
            var paths = await ComposerFileDialog.Current.Current.Choose(owner,
                allowsMultipleSelection: true, canChooseDirectories: false, prompt: "Attach");
            if (paths is null) return; // :532 only OK attaches
            Attach(paths.Select(path => (AttachmentSource)new AttachmentSource.File(path)).ToArray());
        });
    }
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
    private IFrameSource? heightFrames;
    private Action<TimeSpan>? heightFrame;

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
        if (heightFrame is { } pending) heightFrames!.CancelFrame(pending);
        heightFrame = null;
        var resize = GetValue(AppEnvironment.WindowLiveResizeProperty);
        if (resize is null || resize.IsReshaping || double.IsNaN(clip.Height) || clip.Height == height)
        {
            clip.Height = height;
            return;
        }
        // :601-604 Only the top-aligned clip interpolates on the smooth 0.28 curve; each new height replaces it.
        var from = clip.Height;
        var animation = Animation.Smooth(0.28);
        var start = SwiftClock.Now;
        heightFrames = FrameClock.Source.Current;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            var complete = elapsed >= animation.Duration;
            clip.Height = complete ? height : from + (height - from) * animation.Progress(elapsed);
            if (complete) heightFrame = null;
            else heightFrames.RequestFrame(frame!);
        };
        heightFrame = frame;
        heightFrames.RequestFrame(frame);
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
        Action onSend)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; // :626
        var icon = SwiftImage.SystemName("paperclip").Font(Chrome.IconFont); // :634-635
        var attach = PlainControls.Button(icon, () =>
        {
            if (recentDownloadsPicker) showingRecents.Value = !showingRecents.Value; // :628-629
            else onChooseFiles(); // :631
        });
        // STOP #6: :637 .chip(active:) belongs to UI.Common.Components, whose ChipButtonStyleHelper is empty.
        // Only that styling site is unwritten; the action, label and presentation below are source behavior.
        attach.Focusable = false; // :638
        ToolTip.SetTip(attach, "Attach files"); // :639
        AutomationProperties.SetName(attach, "Attach files"); // :640
        var popup = new Popup { PlacementTarget = attach, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true }; // :641
        var presented = false;
        attach.Track(() => showingRecents.Value, shown =>
        {
            if (shown == presented) return;
            presented = shown;
            if (shown) popup.Child = new DownloadsPopover(
                pick: path => { showingRecents.Value = false; onAttach([path]); }, // :643
                chooseOther: () => { showingRecents.Value = false; onChooseFiles(); }); // :644
            Presentation.Presenter.Current.SetPresented(popup, shown);
            if (!shown) popup.Child = null;
        });
        popup.Closed += (_, _) => { if (showingRecents.Value) showingRecents.Value = false; };
        DetachedFromVisualTree += (_, _) => { if (presented) Presentation.Presenter.Current.SetPresented(popup, false); };
        row.Children.Add(attach);
        row.Children.Add(new ModelEffortSlot(runtime, thread, compact)); // :647
        if (!compact) row.Children.Add(new ContextMeter(runtime, thread.Provider, headsProvider)); // :650-651
        row.Children.Add(new SendButton(runtime, goesToHead, queueGoesToHead, onSend)); // :653
        Child = row;
    }

    // :655 .fixedSize(): measure the control row without compressing either axis.
    protected override Size MeasureOverride(Size availableSize) => PlainControls.MeasureFixed(this, Child, availableSize);
}

/// <summary>:659-675 <c>private struct ComposerWorkingDotsSlot: View</c>.</summary>
internal sealed class ComposerWorkingDotsSlot : Decorator
{
    // :662 let runtime; :664-674 body. Reading isRunning here keeps the pill's body off the run flag, so starting and
    // stopping a turn repaints the dots alone.
    public ComposerWorkingDotsSlot(ThreadRuntime runtime)
    {
        var panel = new Panel();
        var clip = new Border { Child = panel, ClipToBounds = true }.RoundedRectangle(22, RoundedCornerStyle.Continuous); // :671
        clip.SizeChanged += (_, e) => clip.Clip = SwiftShapes.RoundedRectangleGeometry(new Rect(e.NewSize), 22, RoundedCornerStyle.Continuous);
        Control? current = null;
        this.Track(() => runtime.IsRunning, running =>
        {
            if (running && current is null)
            {
                current = Dots().Transition(SwiftTransition.Opacity.Animation(Animation.EaseInOut(0.4))); // :672
                SwiftTransition.Insert(panel, 0, current);
            }
            else if (!running && current is { } leaving)
            {
                SwiftTransition.Remove(panel, leaving);
                current = null;
            }
        });
        Child = clip;
    }

    // :666-672 the dots, their frame, clip and transition. MARROW_STUB: part 3 of this unit (PR-COMPOSER-101).
    private static Control Dots()
    {
        var dots = new ComposerWorkingDots { VerticalAlignment = VerticalAlignment.Bottom }; // :667-669
        var geometry = new Grid { Children = { dots } }; // :666 GeometryReader
        geometry.SizeChanged += (_, e) => dots.Height = e.NewSize.Height * 0.4; // :668
        return geometry;
    }
}

/// <summary>:679-690 <c>private struct ModelEffortSlot: View</c>.</summary>
internal sealed class ModelEffortSlot : Decorator
{
    // :683-685 the view's inputs; :687-689 body
    public ModelEffortSlot(ThreadRuntime runtime, ChatThread thread, bool compact)
    {
        // STOP #6: :688 ModelEffortButton(thread, !runtime.Turns.IsEmpty, compact) is MARROW_STUB in EffortSlider.cs.
        // This single dependency site remains unwritten; no replacement model chip or canned content is supplied.
    }
}

/// <summary>:692-773 <c>private struct ContextMeter: View</c>.</summary>
internal sealed class ContextMeter : Decorator
{
    // :733-736 Swift's accessibility value is independent of the button's label and help.
    private sealed class ContextButton : Button
    {
        public string SpokenValue { get; set; } = "";
        public ContextButton(Control label, Action action)
        {
            Template = PlainControls.Button(label, action).Template;
            Content = label;
            Click += (_, _) => action();
        }
        protected override AutomationPeer OnCreateAutomationPeer() => new ContextPeer(this);
    }
    private sealed class ContextPeer(ContextButton owner) : ButtonAutomationPeer(owner), IValueProvider
    {
        public string Value => owner.SpokenValue;
        public bool IsReadOnly => true;
        public void SetValue(string? value) => throw new InvalidOperationException("The context value is read-only.");
    }
    // :702 @State isPresented
    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;
        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }
    private readonly ViewState state = new();
    // :698-700 the view's inputs; :704-762 body
    public ContextMeter(ThreadRuntime runtime, ProviderKind provider, ProviderKind? headsProvider)
    {
        ContextButton? button = null;
        Popup? popup = null;
        var presented = false;
        this.Track(() => (Usage: runtime.Usage, Model: AppEnvironment.GetAppModel(this),
                Credits: CreditsReader.ExposesCredits(provider) && AppEnvironment.GetAppModel(this).Settings.HasAPIKey(provider),
                PoppedOut: AppEnvironment.GetAppModel(this).Settings.ShowsUsagePanel), input =>
        {
            var fraction = input.Usage?.Fraction; // :705-706
            if (input.PoppedOut || !(fraction is not null || PlanLimitsReader.ExposesLimits(provider) || input.Credits)) // :709-713
            {
                if (presented && popup is not null) Presentation.Presenter.Current.SetPresented(popup, false);
                presented = false;
                state.IsPresented = false;
                Child = null;
                button = null;
                popup = null;
                return;
            }
            var ring = new Grid { Width = 15, Height = 15 }; // :717,725
            var track = new Avalonia.Controls.Shapes.Ellipse { StrokeThickness = 2.5 }; // :718-719
            track.Stroke = SwiftColor.Quaternary.Brush(track);
            var arc = new Avalonia.Controls.Shapes.Path
            {
                StrokeThickness = 2.5, StrokeLineCap = PenLineCap.Round, // :722
                RenderTransform = new RotateTransform(-90), RenderTransformOrigin = RelativePoint.Center, // :723
                Data = Arc(fraction ?? 0), // :721
            };
            arc.Stroke = ((fraction ?? 0) > 0.85 ? Chrome.Warning : Popovers.GetTint(this) ?? SwiftColor.AccentColor).Brush(arc); // :722
            ring.Children.Add(track); ring.Children.Add(arc);
            if (button is null)
            {
                button = new ContextButton(ring, () => state.IsPresented = !state.IsPresented); // :714-715
                button.Focusable = false; // :731
                button.Padding = new Thickness(6, 5); // :726-728 rectangular hit area
                popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true }; // :737
                popup.Closed += (_, _) => state.IsPresented = false;
                Child = button;
            }
            else button.Content = ring;
            var sentence = fraction is { } value ? ((long)(value * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "% of the context window used" : "";
            ToolTip.SetTip(button, fraction is not null ? sentence : input.Credits ? "Remaining credits" : "Usage limits"); // :732,764-767
            AutomationProperties.SetName(button, fraction is not null ? "Context window" : input.Credits ? "Remaining credits" : "Usage limits"); // :733,769-772
            button.SpokenValue = sentence; // :734-736, including the explicit empty value without a fraction
            AutomationProperties.SetHelpText(button, sentence);
            // :757 The same panel reads the latest usage, including the pair's distinct heads provider.
            if (state.IsPresented && popup is not null) popup.Child = Content(input.Usage, input.Model);
        });
        this.Track(() => state.IsPresented, shown =>
        {
            if (popup is null || shown == presented) return;
            presented = shown;
            if (shown) popup.Child = Content(runtime.Usage, AppEnvironment.GetAppModel(this));
            Presentation.Presenter.Current.SetPresented(popup, shown);
            if (!shown) popup.Child = null;
        });
        DetachedFromVisualTree += (_, _) => { if (presented && popup is not null) Presentation.Presenter.Current.SetPresented(popup, false); };

        Control Content(ContextUsage? usage, AppModel model)
        {
            var stack = new StackPanel { Spacing = 0 }; // :738
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(16,10,16,0) }; // :743,755-756
            header.ColumnDefinitions[1].MinWidth = 8; // :747
            var title = new TextBlock { Text = "Usage", VerticalAlignment = VerticalAlignment.Center }; // :744
            SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(title); // :745
            title.Foreground = Chrome.SecondaryText.Brush(title); // :746
            header.Children.Add(title);
            var popout = new ChromeCircleButton("arrow.up.right", "Open as a floating panel beside the chat", () =>
            {
                state.IsPresented = false; // :749
                Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.ShowsUsagePanel = true); // :750-752
            });
            Grid.SetColumn(popout, 2); header.Children.Add(popout); // :748
            stack.Children.Add(header);
            stack.Children.Add(new UsagePanel(usage, provider, headsProvider)); // :757 width and padding are owned by UsagePanel
            return stack.PresentedChrome(); // :759
        }
    }

    // :720-723 A circle trimmed from zero, before its -90 degree rotation.
    private static Geometry Arc(double fraction)
    {
        if (fraction >= 1) return new EllipseGeometry(new Rect(0, 0, 15, 15));
        var geometry = new StreamGeometry();
        if (fraction <= 0) return geometry;
        using var context = geometry.Open();
        context.BeginFigure(new Point(15, 7.5), false);
        var angle = fraction * 2 * Math.PI;
        context.ArcTo(new Point(7.5 + 7.5 * Math.Cos(angle), 7.5 + 7.5 * Math.Sin(angle)), new Size(7.5,7.5), 0,
            fraction > 0.5, SweepDirection.Clockwise);
        context.EndFigure(false);
        return geometry;
    }
}

/// <summary>:775-781 <c>@MainActor private enum SendShortcutText</c>, a caseless enum used as a namespace.</summary>
internal static class SendShortcutText
{
    // :779 static let queue = ShortcutStore.label(for: .queueChat)
    public static string? Queue { get; } = ShortcutStore.Label(AppShortcut.QueueChat);

    // :780 static let stop = ShortcutStore.hint(for: .stopTurn)
    public static string Stop { get; } = ShortcutStore.Hint(AppShortcut.StopTurn);
}

/// <summary>:783-796 <c>private struct SendButton: View</c>.</summary>
internal sealed class SendButton : Decorator
{
    // :784-791 the view's inputs; :793-795 body
    public SendButton(ThreadRuntime runtime, bool goesToHead, bool queueGoesToHead, Action send) =>
        Child = new SendRunState(runtime, goesToHead, queueGoesToHead, send); // :794
}

/// <summary>:798-813 <c>private struct SendRunState: View</c>.</summary>
internal sealed class SendRunState : Decorator
{
    // :801-804 the view's inputs; :806-812 body
    public SendRunState(ThreadRuntime runtime, bool goesToHead, bool queueGoesToHead, Action send) =>
        this.Track(() => (Running: runtime.IsRunning, Heads: runtime.IsRunning && runtime.HasWorkingHeads), input =>
            Child = new SendDraftState(runtime, input.Running, input.Heads, goesToHead, queueGoesToHead, send)); // :807-811
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
        Action send)
    {
        var image = SwiftImage.SystemName(isRunning ? "stop.fill" : "arrow.up").Font(Chrome.IconFont).ForegroundStyle(SwiftColor.White); // :831-833
        image.ContentTransitionSymbolEffect(SymbolEffectKind.Replace); // :834 MARROW_DEFERRED ED-182: symbol cross-fade
        var label = new Border { Width = 28, Height = 28, Child = image }.Capsule(RoundedCornerStyle.Circular); // :835-836 circle
        var button = PlainControls.Button(label, () => { if (isRunning) runtime.Interrupt(); else send(); }); // :828-829,838
        button.Focusable = false; // :839
        button.Margin = new Thickness(4,0,0,0); // :841
        ToolTip.SetTip(button, HelpText(isRunning, headsWorking, goesToHead, queueGoesToHead)); // :842
        AutomationProperties.SetName(button, isRunning ? "Stop" : goesToHead ? "Send to a head" : "Send"); // :843
        void Apply(bool empty, bool reverting)
        {
            var enabled = isRunning || !empty; // :826-827
            button.IsEnabled = enabled && !reverting; // :840
            label.Background = (enabled ? Popovers.GetTint(this) ?? SwiftColor.AccentColor : SwiftColor.Quaternary).Brush(label); // :836
        }
        Apply(runtime.Draft.IsEmpty, runtime.IsReverting);
        this.Track(() => (Empty: runtime.Draft.IsEmpty, Reverting: runtime.IsReverting), input => Apply(input.Empty, input.Reverting));
        Child = button;
    }

    // :848-865 The shortcuts are read from the launch-wide cache, not looked up per keystroke.
    private static string HelpText(bool isRunning, bool headsWorking, bool goesToHead, bool queueGoesToHead)
    {
        var queue = SendShortcutText.Queue;
        if (isRunning)
        {
            if (headsWorking) return queueGoesToHead ? "Heads are at work: your message goes to a head" : "Heads are at work: your message waits for the lead";
            var running = new List<string> { "Stop" + SendShortcutText.Stop, "Return steers" };
            if (queue is not null) running.Add(queue + " queues");
            return string.Join(" \u00b7 ", running);
        }
        var idle = new List<string> { goesToHead ? "Send to a head (Return) \u00b7 the lead reports back" : "Send (Return)" };
        if (queue is not null) idle.Add(queue + (headsWorking ? " queues until the heads report" : " queues while running"));
        return string.Join(" \u00b7 ", idle);
    }
}

/// <summary>:868-944 <c>private struct DraftChips: View</c>.</summary>
internal sealed class DraftChips : Decorator
{
    private readonly ThreadRuntime runtime;
    private readonly Panel rows = new();
    private Control? current;
    private bool evaluated;
    /// <summary>:871-874 <c>private struct ChipKey: Equatable</c>.</summary>
    private sealed record ChipKey
    {
        public required ValueList<Guid> QuoteIDs { get; init; } // :872
        public string? CommandName { get; init; } // :873
    }

    // :869 let runtime; :876-943 body
    public DraftChips(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        Child = rows;
        this.Track(() => (runtime.Draft.Quotes, runtime.Draft.Command), draft => Show(draft.Quotes, draft.Command)); // :877-878
    }

    private void Show(ValueList<ReplyQuote> quotes, DraftCommand? command)
    {
        var wasEvaluated = evaluated;
        evaluated = true;
        // :879 nothing to show: no row, so the column's zero spacing leaves no room for it
        if (quotes.Count == 0 && command is null)
        {
            if (current is { } leaving) Transaction.WithAnimation(Chrome.PanelSlide, () => SwiftTransition.Remove(rows, leaving));
            current = null;
            return;
        }
        var chipRow = Row(quotes, command);
        chipRow.Margin = new Thickness(0, 0, 0, 8); // :939 .padding(.bottom, 8)
        chipRow.Transition(SwiftTransition.Modifier(new ChipRowCollapse(true), new ChipRowCollapse(false))); // :940
        if (current is null && wasEvaluated)
            Transaction.WithAnimation(Chrome.PanelSlide, () => SwiftTransition.Insert(rows, 0, chipRow));
        else if (current is null) SwiftTransition.Insert(rows, 0, chipRow);
        else
        {
            // :941 Text edits leave the animation key alone. A nonempty single-line scroller keeps its height.
            rows.Children.Clear();
            SwiftTransition.Insert(rows, 0, chipRow);
        }
        current = chipRow;
    }

    // :880-938 the chips' scroller, and :940-941 its transition and animation. MARROW_STUB: part 3 of this unit
    // (PR-COMPOSER-086 to PR-COMPOSER-089).
    private Control Row(ValueList<ReplyQuote> quotes, DraftCommand? command)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; // :881
        if (command is not null) // :882
            row.Children.Add(Chip(command.IsBuiltIn ? "command" : "sparkles", "/" + command.Name, command.Detail,
                "Remove command", () => runtime.Draft = runtime.Draft with { Command = null }, true)); // :883-906
        foreach (var quote in quotes) // :908
            row.Children.Add(Chip("text.quote", quote.Excerpt, quote.Text, "Remove quote",
                () => runtime.Draft = runtime.Draft with { Quotes = new ValueList<ReplyQuote>(runtime.Draft.Quotes.Where(held => held.Id != quote.Id)) }, false)); // :909-935
        return new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; // :880
    }

    // :883-906,909-935 Each capsule combines the label and remove action; full detail is its tooltip.
    private static Control Chip(string symbol, string text, string detail, string removeLabel, Action remove, bool commandChip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; // :883,909
        row.Children.Add(SwiftImage.SystemName(symbol)
            .Font(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText)); // :884-886,910-912
        var title = new TextBlock { Text = text, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }; // :887,913,916-917
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: commandChip ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(title); // :888,914
        title.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(title); // :889,915
        if (!commandChip) { title.MaxWidth = 260; title.HorizontalAlignment = HorizontalAlignment.Left; } // :918
        row.Children.Add(title);
        var x = SwiftImage.SystemName("xmark").Font(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold)).ForegroundStyle(Chrome.SecondaryText); // :895-897,924-926
        var button = PlainControls.Button(x, () => Transaction.WithAnimation(Chrome.PanelSlide, remove)); // :890-899,919-928
        button.Focusable = false; // :900,929
        ToolTip.SetTip(button, removeLabel); // :901,930
        AutomationProperties.SetName(button, removeLabel); // :902,931
        row.Children.Add(button);
        var chrome = new ChipChrome().Body(row); // :904,933
        ToolTip.SetTip(chrome, detail); // :905,934
        AutomationProperties.SetName(chrome, text); // :906,935 combined chip name
        return chrome;
    }
}

/// <summary>:946-959 <c>private struct ChipRowCollapse: ViewModifier</c>.</summary>
internal sealed record ChipRowCollapse(bool Collapsed) : IAnimatableModifier
{
    private double? fraction;
    public ImmutableArray<double> AnimatableData => [fraction ?? (Collapsed ? 0 : 1)];
    public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) => this with { fraction = data[0] };
    // :953-958 func body(content:)
    public Control Body(Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Top; // :955
        var clip = new Border { Child = content, ClipToBounds = true }; // :956
        Update(clip);
        return clip;
    }

    public void Update(Control body)
    {
        var clip = (Border)body;
        var progress = AnimatableData[0];
        clip.Child!.Measure(Size.Infinity);
        clip.Height = progress == 1 ? double.NaN : Math.Max(0, clip.Child.DesiredSize.Height * progress); // :955, ED-098
        clip.Opacity = progress; // :957
    }
}

/// <summary>:961-970 <c>struct ChipChrome: ViewModifier</c>: the capsule behind a composer chip.</summary>
public sealed record ChipChrome
{
    // :963-969 func body(content:)
    public Control Body(Control content)
    {
        var capsule = new Border { Child = content, Padding = new Thickness(9,5,7,5) }.Capsule(RoundedCornerStyle.Continuous); // :965-968
        capsule.Background = Chrome.Overlay(0.12).Brush(capsule); // :968
        return capsule;
    }
}

/// <summary>:972-1033 <c>private struct DraftAttachments: View</c>.</summary>
internal sealed class DraftAttachments : Decorator
{
    private readonly ThreadRuntime runtime;
    private readonly AttachmentPreviewSlot preview = new(); // :977
    private readonly HashSet<Guid> drawn = []; // :978
    private ForEach<Attachment, Guid>? cells;
    private readonly Dictionary<Guid, Attachment> shown = [];
    // :973 let runtime; :980-988 body
    public DraftAttachments(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        this.Track(() => runtime.Draft.Attachments, Show); // :981
        DetachedFromVisualTree += (_, _) => preview.Close(); // :1031
    }

    private void Show(ValueList<Attachment> attachments)
    {
        // :982 an empty strip takes no room at all
        if (attachments.Count == 0)
        {
            preview.Retire(new HashSet<Guid>()); // :1028-1029
            Child = null;
            cells = null;
            this.shown.Clear();
            return;
        }
        if (Child is not null && cells is not null)
        {
            cells.Apply(attachments);
            preview.Retire(attachments.Select(attachment => attachment.Id).ToHashSet()); // :1028-1029
            return;
        }
        var shown = Strip(attachments); // :983
        // :984-986 The strip's own gap to the text below it: the composer stacks with no spacing.
        shown.Margin = new Thickness(0, 0, 0, 8);
        Child = shown;
    }

    // :990-1032 private func strip(_:), with :975-978 its @State. MARROW_STUB: part 3 of this unit (PR-COMPOSER-067,
    // PR-COMPOSER-068).
    private Control Strip(ValueList<Attachment> attachments)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 }; // :1000
        cells = new ForEach<Attachment, Guid>(row, attachment => attachment.Id, MakeCell, (control, attachment) =>
        {
            if (shown.TryGetValue(attachment.Id, out var old) && old == attachment) return;
            var arrival = (AttachmentArrival)control;
            // :1001-1002 Keep the reader behind the surviving cell; refresh only its content and arrival input.
            arrival.Children[1] = Cell(attachment);
            arrival.Update(!drawn.Contains(attachment.Id));
            shown[attachment.Id] = attachment;
        });
        cells.Apply(attachments); // :1001
        var layer = new Grid { Children = { new AttachmentAnchorCapture(view => preview.SetAnchor(view)), row } }; // :1024-1026
        return new ScrollViewer { Content = layer, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; // :991
    }

    // :1002,1021 new arrivals remember their id for the life of this draft strip.
    private Control MakeCell(Attachment attachment)
    {
        shown[attachment.Id] = attachment;
        var arrival = new AttachmentArrival(!drawn.Contains(attachment.Id), content: Cell(attachment));
        arrival.AttachedToVisualTree += (_, _) => drawn.Add(attachment.Id);
        return arrival;
    }

    // :1003-1019 The overhang is reserved inside this cell, not in its neighbor's gap.
    private Control Cell(Attachment attachment)
    {
        var thumbnail = new AttachmentThumbnail(attachment, preview, size: 48); // :1003
        var overlay = new Grid { Children = { thumbnail } }; // :1004
        var image = SwiftImage.SystemName("xmark.circle.fill").SymbolRenderingMode(SymbolRenderingMode.Palette)
            .ForegroundStyle(SwiftColor.White, SwiftColor.Black.Opacity(0.6)); // :1008-1010 MARROW_DEFERRED ED-182 palette
        var remove = PlainControls.Button(image, () => runtime.Draft = runtime.Draft with
        {
            Attachments = new ValueList<Attachment>(runtime.Draft.Attachments.Where(held => held.Id != attachment.Id)), // :1005-1006
        });
        remove.Padding = new Thickness(4); // :1011
        remove.Focusable = false; // :1014
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.RenderTransform = new TranslateTransform(6,-6); // :1015
        AutomationProperties.SetName(remove, "Remove " + attachment.Name); // :1016
        overlay.Children.Add(remove);
        return new Border { Child = overlay, Padding = new Thickness(0,2,8,0) }; // :1018-1019
    }
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
    public string Id => Value;
}

/// <summary>:1061-1101 <c>private struct SuggestionPopoverRow: View</c>.</summary>
internal sealed class SuggestionPopoverRow : Decorator
{
    // The @State the body reads (R-VIEW-2).
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        // :1067 @State private var isHovering = false
        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    private readonly ViewState state = new();
    private readonly Panel symbolSlot;
    private readonly MiddleTruncatingText title;
    private readonly TextBlock detail;
    private readonly AnimatedColor fill;
    private Suggestion item;
    private bool isSelected;
    private Action select;
    private Action pick;
    private bool? drawnHighlight;

    // :1062-1065 the view's inputs; :1069-1100 body
    public SuggestionPopoverRow(Suggestion item, bool isSelected, Action select, Action pick)
    {
        this.item = item;
        this.isSelected = isSelected;
        this.select = select;
        this.pick = pick;

        // :1071 HStack(spacing: 8). ENGINEERING DECISION: the title keeps its natural width and the detail takes the
        // rest, trimmed, after the spacer's 16; SwiftUI's own share-out between two truncating texts is not in the source.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,Auto,16,*"), ColumnSpacing = 8 };
        symbolSlot = new Panel { Width = 16 }; // :1072-1074 .frame(width: 16)
        row.Children.Add(symbolSlot);
        title = new MiddleTruncatingText { TextWrapping = TextWrapping.NoWrap }; // :1075-1078 .lineLimit(1).truncationMode(.middle)
        SwiftFont.Body.Apply(title); // :1076
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        // :1079 Spacer(minLength: 16): column 2.
        detail = new TextBlock
        {
            MaxLines = 1, // :1083 .lineLimit(1)
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        SwiftFont.Callout.Apply(detail); // :1081
        detail.Foreground = Chrome.SecondaryText.Brush(detail); // :1082
        Grid.SetColumn(detail, 3);
        row.Children.Add(detail);
        row.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Chrome.PrimaryText.Brush(row)); // :1085
        row.VerticalAlignment = VerticalAlignment.Center;

        fill = new AnimatedColor(row);
        var background = new Border
        {
            Child = row,
            Padding = new Thickness(8, 0), // :1086 .padding(.horizontal, 8)
            Height = 26, // :1087 .frame(height: 26)
            Background = fill.Brush, // :1088-1091, and :1092 .contentShape(.rect): the fill takes the hits, clear or not
        }.RoundedRectangle(6, RoundedCornerStyle.Continuous);
        Draw();
        ApplyFill(null);

        // :1070 Button(action: pick) { ... }, :1094 .buttonStyle(.plain), :1095 .focusable(false)
        var button = PlainControls.Button(background, () => this.pick());
        button.Focusable = false;
        // :1096-1099
        button.PointerEntered += (_, _) => Hover(true);
        button.PointerExited += (_, _) => Hover(false);
        Child = button;
    }

    // The menu's body run again with the row's new values (R-VIEW-1: a ForEach row keyed by its index).
    internal void Update(Suggestion item, bool isSelected, Action select, Action pick)
    {
        var redraw = item != this.item;
        this.item = item;
        this.isSelected = isSelected;
        this.select = select;
        this.pick = pick;
        if (redraw) Draw();
        ApplyFill(Transaction.Current);
    }

    private void Draw()
    {
        symbolSlot.Children.Clear();
        var symbol = SwiftImage.SystemName(item.Symbol).Font(SwiftFont.Callout); // :1072-1073
        symbol.Width = 16; // :1074
        symbolSlot.Children.Add(symbol);
        title.FullText = item.Title; // :1075 Text(verbatim: item.title)
        detail.Text = item.Detail; // :1080 Text(verbatim: item.detail)
    }

    // :1096-1099
    private void Hover(bool hovering)
    {
        Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering); // :1097
        ApplyFill(Chrome.Hover);
        if (hovering) select(); // :1098
    }

    // :1090 .fill(isSelected || isHovering ? Chrome.overlay(0.1) : Color.clear)
    private void ApplyFill(Animation? animation)
    {
        var highlight = isSelected || state.IsHovering;
        if (drawnHighlight == highlight) return;
        drawnHighlight = highlight;
        fill.Set(highlight ? Chrome.Overlay(0.1) : SwiftColor.Clear, animation);
    }
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

    private ValueList<Entry> entries = ValueList<Entry>.Empty; // :1118 @ObservationIgnored
    private string? directory; // :1119 @ObservationIgnored

    // :1121-1127 Built indexes per directory, shared by every composer, so switching threads in a project never lists and
    // lowercases its files again. A stale index is served and rebuilt behind it. A composer that finds its directory
    // already building joins that build rather than waiting on nothing; two builds run at most, and two indexes are
    // kept. Main-actor statics, read and written on main only.
    private static readonly RecentCache<string, (ValueList<Entry> Entries, DateTimeOffset BuiltAt)> Built = new(limit: 2); // :1125
    private static readonly Dictionary<string, TaskHandle<ValueList<Entry>>> Building = new(SwiftStringComparer.Instance); // :1126
    private const double Freshness = 60; // :1127

    // :1129-1159 func prepare(_:) async
    public async Task Prepare(string directory)
    {
        if (SwiftTask.IsCancelled) return; // :1130
        this.directory = directory; // :1131
        while (!SwiftTask.IsCancelled && this.directory is { } current && SwiftString.Equal(current, directory)) // :1132
        {
            if (Built.TryGetValue(directory, out var cached)) // :1133
            {
                entries = cached.Entries; // :1134
                if (!(SwiftDate.TimeIntervalSince(SwiftDate.Now, cached.BuiltAt) > Freshness)) return; // :1135
            }
            else
            {
                entries = ValueList<Entry>.Empty; // :1137
            }
            TaskHandle<ValueList<Entry>> task; // :1139
            if (Building.TryGetValue(directory, out var pending)) // :1140
            {
                task = pending; // :1141
            }
            else if (Building.Count >= 2) // :1142
            {
                // :1142 Self.building.values.first: any one of the two (a dictionary's order is unspecified, R-COLL-2).
                _ = await Building.Values.First().Value; // :1143
                continue; // :1144
            }
            else
            {
                task = MainActor.Spawn(async () => // :1146-1151
                {
                    var built = await Build(directory);
                    Built.Insert((built, SwiftDate.Now), directory);
                    Building.Remove(directory);
                    return built;
                });
                Building[directory] = task; // :1152
            }
            var fresh = await task.Value; // :1154
            if (SwiftTask.IsCancelled || !(this.directory is { } now && SwiftString.Equal(now, directory))) return; // :1155
            entries = fresh; // :1156
            return; // :1157
        }
    }

    // :1161-1173 @concurrent. The lowercased path, its last component, and what its length costs a score, taken once
    // here: a Swift string's count walks the whole string, and the search used to take it for every match.
    private static Task<ValueList<Entry>> Build(string directory) => System.Threading.Tasks.Task.Run(() => BuildBody(directory));

    private static async Task<ValueList<Entry>> BuildBody(string directory) =>
        new((await List(directory)).Select(path =>
        {
            var lowercased = SwiftString.Lowercased(path); // :1164
            return new Entry
            {
                Path = path, // :1166
                Lowercased = lowercased, // :1167
                Name = SwiftPath.LastPathComponent(lowercased), // :1168
                LengthPenalty = Math.Min(SwiftString.Count(path), 99), // :1169
                IsASCII = lowercased.All(unit => unit < 128), // :1170 utf8.allSatisfy { $0 < 128 }
            };
        }));

    // :1175-1183 The best matches for a query. Scored off the main actor: the index holds up to 60,000 paths and every
    // keystroke after an @ scored each of them. The caller drops the answer when the caret has moved on (the task is
    // cancelled), so the scan stops early too.
    public async Task<ValueList<string>> Search(string query, long limit = 8)
    {
        var needle = SwiftString.Lowercased(query); // :1180
        if (needle.Length == 0) // :1181
        {
            if (limit < 0) throw SwiftTraps.Fatal("Can't take a prefix of negative length from a collection"); // prefix(limit)
            return new ValueList<string>(entries.Take(checked((int)Math.Min(limit, entries.Count))).Select(entry => entry.Path));
        }
        return await Search(needle, entries, limit); // :1182
    }

    // :1185-1186 @concurrent private nonisolated static func search(_:in:limit:) async -> [String]
    private static Task<ValueList<string>> Search(string needle, ValueList<Entry> @in, long limit) =>
        System.Threading.Tasks.Task.Run(() => SearchBody(needle, @in, limit));

    // :1187-1216 The best `limit` so far, highest score first. An insertion into a list this short beats collecting every
    // match and sorting them all.
    private static ValueList<string> SearchBody(string needle, ValueList<Entry> entries, long limit)
    {
        if (limit <= 0) return ValueList<string>.Empty; // :1189
        var best = new List<(string Path, long Score)>(); // :1190-1191
        var isASCII = needle.All(unit => unit < 128); // :1192
        for (var offset = 0; offset < entries.Count; offset++) // :1193
        {
            if (offset % 2_048 == 0 && SwiftTask.IsCancelled) return ValueList<string>.Empty; // :1194
            var entry = entries[offset];
            var ascii = isASCII && entry.IsASCII; // :1195
            long score; // :1196
            if (SwiftString.Equal(entry.Name, needle)) score = 1_000; // :1197-1198
            else if (SwiftString.HasPrefix(entry.Name, needle)) score = 800; // :1199-1200
            else if (Contains(needle, entry.Name, ascii)) score = 600; // :1201-1202
            else if (Contains(needle, entry.Lowercased, ascii)) score = 400; // :1203-1204
            else if (IsSubsequence(needle, entry.Lowercased, ascii)) score = 100; // :1205-1206
            else continue; // :1207-1208
            var scored = score - entry.LengthPenalty; // :1210
            if (best.Count == limit && scored <= best[checked((int)limit) - 1].Score) continue; // :1211
            var index = best.FindIndex(candidate => candidate.Score < scored); // :1212 firstIndex { $0.score < scored }
            best.Insert(index < 0 ? best.Count : index, (entry.Path, scored)); // :1212-1213
            if (best.Count > limit) best.RemoveAt(best.Count - 1); // :1214
        }
        return new ValueList<string>(best.Select(candidate => candidate.Path)); // :1216
    }

    // :1219-1233 Substring search on the bytes when both sides are ASCII; a carriage return in the path is the one byte a
    // grapheme can span, so it takes the general path, as in isSubsequence. Swift's comment specifies a Character
    // walk (:1219-1222), retaining canonical equality (R-STR-2) rather than comparing raw UTF-16 units.
    private static bool Contains(string needle, string @in, bool ascii)
    {
        if (!ascii || @in.Contains('\r', StringComparison.Ordinal))
        {
            var pattern = SwiftString.Characters(needle).ToArray();
            var text = SwiftString.Characters(@in).ToArray();
            for (var start = 0; start <= text.Length - pattern.Length; start++)
            {
                var offset = 0;
                while (offset < pattern.Length && SwiftString.Equal(text[start + offset], pattern[offset])) offset++;
                if (offset == pattern.Length) return true;
            }
            return false;
        }
        // :1225-1231 memchr(base, 13) finds a carriage return: nil, the general path; otherwise memmem.
        return @in.Contains(needle, StringComparison.Ordinal); // :1225-1232 ASCII bytes
    }

    // :1235-1254 nonisolated static func isSubsequence(_:of:ascii:) -> Bool: on the bytes for ASCII, on Characters
    // otherwise, and for a path holding a carriage return, whose "\r\n" is one Character.
    public static bool IsSubsequence(string needle, string of, bool ascii)
    {
        if (ascii) // :1236
        {
            var remaining = System.Text.Encoding.UTF8.GetBytes(needle); // :1237
            var next = 0; // :1238 remaining.next(); nil once next reaches the end
            foreach (var @byte in System.Text.Encoding.UTF8.GetBytes(of)) // :1239
            {
                if (@byte == 13) return IsSubsequence(needle, of, ascii: false); // :1240
                if (next < remaining.Length && @byte == remaining[next]) // :1241
                {
                    next += 1; // :1242
                    if (next == remaining.Length) return true; // :1243
                }
            }
            return next == remaining.Length; // :1246
        }
        var characters = SwiftString.Characters(needle).ToList(); // :1248 var remaining = needle[...]
        var matched = 0;
        foreach (var character in SwiftString.Characters(of)) // :1249 where character == remaining.first
        {
            if (matched >= characters.Count || !SwiftString.Equal(character, characters[matched])) continue;
            matched += 1; // :1250
            if (matched == characters.Count) return true; // :1251
        }
        return matched == characters.Count; // :1253
    }

    // :1256-1264 @concurrent. A repository's tracked and untracked unignored files, capped at 60,000; else the walk.
    private static Task<IReadOnlyList<string>> List(string directory) => System.Threading.Tasks.Task.Run(() => ListBody(directory));

    private static async Task<IReadOnlyList<string>> ListBody(string directory)
    {
        var git = new Git(directory); // :1258
        if (await git.IsRepository()) // :1259
        {
            var files = await git.ListFiles(); // :1260
            if (files.Count != 0) return files.Take(60_000).ToList(); // :1261
        }
        return Enumerate(directory); // :1263
    }

    // :1266-1287 The walk: files only, relative to the directory, at most 20,000. Nothing a mention would want sits deeper
    // than level 16, and a tree that deep is usually a vendored or generated one whose name the list does not know.
    // DEVIATION PR-COMPOSER-057 windows_path: .skipsHiddenFiles is the Windows hidden attribute, and
    // .skipsPackageDescendants, which has no Windows counterpart, is dropped. ENGINEERING DECISION: the walk is the
    // enumerator's pre-order (a directory, then what it holds); a reparse point is the link itself, as a URL's
    // isDirectory reads a symbolic link, so it is listed and not entered.
    private static IReadOnlyList<string> Enumerate(string directory)
    {
        var skipped = new HashSet<string>(SwiftStringComparer.Instance)
        {
            "node_modules", "build", "dist", ".build", "DerivedData", "Pods", "target", // :1268
        };
        var root = new DirectoryInfo(directory); // :1267 URL(fileURLWithPath: directory)
        if (!root.Exists) return []; // :1269-1273 no enumerator
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.Hidden, // :1272 .skipsHiddenFiles
            IgnoreInaccessible = true,
        };
        var results = new List<string>(); // :1274

        // False once the cap stops the walk.
        bool Walk(DirectoryInfo folder, int level)
        {
            IEnumerable<FileSystemInfo> children;
            try { children = folder.EnumerateFileSystemInfos("*", options); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return true; }
            foreach (var child in children) // :1275
            {
                // :1278-1281 a skipped name, or a level past 16, is neither listed nor entered
                if (skipped.Contains(child.Name) || level > 16) continue;
                var attributes = child.Attributes;
                // :1282 directories are not listed; the enumerator enters them next
                if (attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (!Walk((DirectoryInfo)child, level + 1)) return false;
                    continue;
                }
                results.Add(ToolTitles.RelativePath(child.FullName, directory)); // :1283
                if (results.Count >= 20_000) return false; // :1284
            }
            return true;
        }

        Walk(root, 1);
        return results; // :1286
    }
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
