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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Voice;
using Marrow.App.Runtime;
using Marrow.App.Views.Diff;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :1-1307.
// Forms (ED-095): SwiftUI views build their body in Decorators; ViewModifiers expose Body(Control), bindings are
// Binding<T>, and file URLs are path strings. NSRange is a (Location, Length) pair of UTF-16 offsets. Swift private
// top-level declarations remain internal, preserving every declared type and existing public signature.
// All five B3 parts have been audited against pinned 1.7.0. The native chooser is behind ComposerFileDialog for
// isolated tests. The ModelEffortButton and chip-style sites are implemented; the chip row keeps its scroller and chips
// (STOP #7 in the tests records what the foundation's ForEach still lacks) and the approval stack's own re-layout glides
// in window coordinates (WindowGlide, STOP #2 resolved); #8 records ProviderRegistry's missing notifications; #9 records the SlashCommand.Suggestions dependency
// after inherited cross-unit code removal.
// Diff popup and suggestion popup headless limits are documented in tests (STOP #3/#5). ED-180/182 retain the
// existing material, palette and symbol-transition deferrals (ED-181, the continuous corner, is closed); visual macOS parity is unverified.

/// <summary>:4-58 <c>struct ComposerArea: View</c>.</summary>
public sealed class ComposerArea : Decorator
{
    private readonly ThreadRuntime runtime;

    // :18-20 @State private var diffPopover = DiffPopoverCoordinator(): the changes popover presented from the tab.
    // Driven by runtime.isDiffVisible, so every opener (tab, Review, the changes chord) shares it.
    private readonly DiffPopoverCoordinator diffPopover = new();

    // :25-27 the approval cards, keyed by request id (R-VIEW-8), with the request each card was drawn from.
    private readonly StackPanel approvalStack;
    // PR-COMPOSER-003 (:56): the views of the area glide to their new places in the window when the id list changes, and a resolved card leaves the
    // layout at once and fades where it stood (WindowGlide; the area is pinned to the bottom of the chat, so a card above a new one rises).
    private readonly WindowGlide approvalGlide;
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
        var tabs = new TabSlot(runtime, diffPopover, showsChanges);
        group.Children.Add(tabs); // :29
        var pill = new ComposerView(runtime, workingDirectory, compactModelChip, takesFocusOnAppear);
        group.Children.Add(pill); // :30
        // PORT-ONLY (voice-SPEC V-12.7; VT-12): the floating pill fades any line it crosses (the tab above the composer) out over 0.16 s and back over 0.25 s after 0.05 s
        var fade = new LookMotion(group);
        var crossed = new LookNumber(fade, 1);
        fade.Frame += () => tabs.Opacity = crossed.Value;
        void Cross()
        {
            var floating = pill.VoiceFloatRect(group);
            var line = tabs.IsVisible && tabs.Bounds.Width > 0 && tabs.TranslatePoint(default, group) is { } at ? new Rect(at, tabs.Bounds.Size) : (Rect?)null;
            var hit = floating is { } pillRect && line is { } lineRect && pillRect.Intersects(lineRect);
            if (hit) crossed.To(0, LookCurve.Ease(0.16));
            else crossed.To(1, LookCurve.Ease(0.25), 0.05);
        }
        pill.VoiceFloatChanged += Cross;
        tabs.LayoutUpdated += (_, _) => Cross();
        pill.LayoutUpdated += (_, _) => Cross();

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
        // PORT-ONLY, owner-ruled (OD-0089, ED-251): the composer light, over the group's glass, under the tab and the pill
        layers.Children.Add(new ComposerLightSlot(runtime, pill));
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
        // PORT-ONLY: Tactus (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.7): the strip above the composer, after the approval cards. It
        // shares the group's row with the same 12-point gap (a hidden strip takes no room, its margin included), so the stack's rows
        // stay :24's: the cards, then the group (ED-NEW-web-w11-1, point 11).
        var strip = new Marrow.App.Views.Chat.TactusStrip(runtime) { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(strip, Dock.Top);
        stack.Children.Add(new DockPanel { LastChildFill = true, Children = { strip, layers } });
        approvalStack = stack;
        var departures = new Canvas { IsHitTestVisible = false }; // over the stack: where a resolved card fades, out of the layout
        approvalGlide = new WindowGlide(stack, departures);

        // :23 GlassEffectContainer(spacing: 8), independent of the VStack's layout spacing.
        // :52 .frame(maxWidth: 820): at most 820 wide, centred in the room the padding leaves.
        // PORT-ONLY (W19, ED-NEW-web-w19-2): the apps' context chips sit above the approval stack, outside it, so the Swift stack still holds only the
        // approval cards and the group (no slot, no spacing while no app has said anything: the row is not visible then).
        var container = new Decorator { Child = new StackPanel { Orientation = Orientation.Vertical, Children = { new AppContextChips(runtime), new Panel { Children = { stack, departures } } } }, MaxWidth = 820 };
        SwiftGlass.SetContainerSpacing(container, 8);
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
                if (changed) approvalGlide.Depart(approvalRows[id], SwiftTransition.Remove); // out of the layout, fading where it stood
                else SwiftTransition.Remove(approvalStack, approvalRows[id]);
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
        if (changed)
        {
            var before = approvalGlide.Capture();
            Transaction.WithAnimation(Chrome.PanelSlide, Apply);
            approvalGlide.GlideAfterLayout(before, Chrome.PanelSlide); // :56 the views that stay move with the change, in the window
        }
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
    private readonly Marrow.App.Port.MultiAccount.Opinions.AskOthersView? askOthers;
    private ComposerDraft? opinionDraft;
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
    private readonly Border padded;
    private readonly Grid root;
    // the attachment button's place, four easings of one 0 (inside) to 1 (outside) choice (CZ-14.1): the pill's width 0.42 s, the text's inset 0.34 s, the buttons' fade 0.28 s
    // and their scale 0.34 s
    private readonly AnimatedScalar widthPlace;
    private readonly AnimatedScalar insetPlace;
    private readonly AnimatedScalar fadePlace;
    private readonly AnimatedScalar scalePlace;
    // ED-NEW-voicefix-ui-9: 0 in place, 1 out of the open inline pill's way (a narrow composer): the inside attach button and the ask-others button, each on a spring
    private AnimatedScalar? attachYield;
    private AnimatedScalar? askYield;
    private bool attachYielding;
    private bool askYielding;
    private readonly ScaleTransform askYieldScale = new();
    private readonly Canvas outsideHost = new() { ClipToBounds = false };
    private readonly ComposerVoiceController? voice;
    private readonly VoicePillSlot? pillSlot; // PORT-ONLY (VT-12): the listening pill's layer, under the text and the controls
    private readonly VoiceLanguageQuestion? question; // PORT-ONLY (VT-12): the language question over the composer
    private readonly Canvas questionHost = new() { ClipToBounds = false };
    private readonly AnimatedScalar? insetRight; // V-12.6: the text column's right inset, eased
    private ComposerAttachButton? insideAttach;
    private ComposerAttachButton? outsideAttach;
    private Grid? layers;
    private bool attachShown;
    private VoiceNoteContent? shownNote;
    private bool noteInField;
    private TopLevel? escapeWatch;
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
        padded = new Border { Child = row, Padding = new Thickness(14, 8, 8, 8) };
        // :179-185 While a turn runs, accent dots flow along the pill's bottom two-fifths, behind the text.
        layers = new Grid();
        layers.Children.Add(new ComposerWorkingDotsSlot(runtime));
        layers.Children.Add(padded);
        // :186 .modifier(ComposerSurface())
        // PORT-ONLY, owner-ruled (OD-0091, voice-SPEC V-14.1): the attachment button sits at the pill's left, inside it or in the margin the pill gives up for it, so the
        // pill has a sibling overlay that lets the margin button overflow to its left.
        // Keep the plate itself as Child, and the overlay outside its panel-content clip.
        Child = new ComposerSurface().Body(layers);
        root = new Grid { ClipToBounds = false };
        VisualChildren.Add(root);
        LogicalChildren.Add(root);
        root.Children.Add(outsideHost); // the margin button hangs to the left of the pill: a canvas, because a negative margin would make its box wider than the button
        widthPlace = new AnimatedScalar(this, 0, _ => ApplyAttachPlace());
        insetPlace = new AnimatedScalar(this, 0, _ => ApplyAttachPlace());
        fadePlace = new AnimatedScalar(this, 0, _ => ApplyAttachPlace());
        scalePlace = new AnimatedScalar(this, 0, _ => ApplyAttachPlace());
        askOthers = Marrow.App.Port.MultiAccount.Opinions.AskOthersComposerMount.Install(textColumn, runtime);
        if (askOthers is not null) ApplyAttachPlace(); // its place beside the attach button (ED-NEW-voicefix-ui-6)
        if (askOthers is not null) askOthers.Sent += (_, _) =>
        {
            // A confirmation may finish after the user edits the draft. Clear only the draft actually submitted.
            if (opinionDraft is { } sent && runtime.Draft == sent) runtime.Draft = new ComposerDraft();
            opinionDraft = null;
        };
        if (VoiceFeature.Enabled)
        {
            voice = new ComposerVoiceController(new ComposerVoiceHost(() => Model.Settings, DraftSelection, ReplaceDraft, Send));
            root.Children.Add(new VoiceAnnouncer(voice) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            // PORT-ONLY (voice-SPEC V-13.3, V-12.2, V-12.11; VT-12): the glow over the composer's glass and under its content, the listening pill under the text and the controls (the orb stands over
            // it), the language question over the composer
            layers.Children.Insert(1, new VoiceGlowLayer(voice));
            pillSlot = new VoicePillSlot(voice);
            layers.Children.Insert(2, pillSlot);
            insetRight = new AnimatedScalar(this, 0, inset => { textColumn.Margin = new Thickness(0, 4, inset, 4); FadeBesidePill(inset); });
            textColumn.SizeChanged += (_, _) => FadeBesidePill(textColumn.Margin.Right);
            pillSlot.InsetTargetChanged += target => insetRight.Set(target, LookCurve.Ez(0.42), preserveFrameProgress: true); // T46: keep inset and pill on the same clock, including stalled paint callbacks.
            attachYield = new AnimatedScalar(this, 0, _ => ApplyAttachPlace());
            askYield = new AnimatedScalar(this, 0, _ => ApplyAttachPlace());
            pillSlot.InlineTargetChanged += YieldToPill;
            pillSlot.Pill.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) YieldToPill(); };
            if (askOthers is not null) askOthers.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) YieldToPill(); };
            question = new VoiceLanguageQuestion();
            questionHost.Children.Add(question);
            root.Children.Add(questionHost);
            question.Answered += voice.AnswerLanguage;
            question.Dismissed += voice.DismissQuestion;
            this.Track(() => voice.State == VoiceSessionState.Asking, asking =>
            {
                if (asking) question.Open();
                else question.Close();
            });
        }
        // :187-189 .geometryGroup(): the pill's width glides as the panels take or give room, and the text view and the
        // controls move as one with the glass. The port animates no frame of its own on a width change, so the row's
        // children are laid out with the pill in the same pass.

        // :142, :149, :151 the resolved thread's placeholder and the attach notice, handed to the column as values.
        this.Track(
            () => (Placeholder: Placeholder(Model.Thread(runtime.ThreadID)), Notice: state.AttachmentNotice),
            inputs => textColumn.Update(inputs.Placeholder, inputs.Notice));
        // :160-174
        this.Track(ControlInputs, ShowControls);
        // PORT-ONLY (voice-SPEC V-14.1): the attachment button's place follows voiceOrb.attach, and the change is eased (OD-0076).
        var placed = false; // the first showing is the setting as it is: no motion
        this.Track(() => Model.Settings.VoiceOrb.Attachment, attachment =>
        {
            var target = attachment == VoiceAttachment.Outside ? 1 : 0;
            widthPlace.Set(target, placed ? LookCurve.Ez(0.42) : null);
            insetPlace.Set(target, placed ? LookCurve.Ez(0.34) : null);
            fadePlace.Set(target, placed ? LookCurve.Ease(0.28) : null);
            scalePlace.Set(target, placed ? LookCurve.Ez(0.34) : null);
            placed = true;
        });
        if (voice is not null) StartVoiceViews(voice);

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
                voice?.ComposerLeaving(); // V-14.7: the chat column going behind another is the composer leaving
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

    protected override Size MeasureOverride(Size availableSize)
    {
        var plate = base.MeasureOverride(availableSize);
        root.Measure(availableSize);
        return new Size(Math.Max(plate.Width, root.DesiredSize.Width), Math.Max(plate.Height, root.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // T46 item 3: keep the existing row's height; centre a genuine single line, but keep source bottom
        // alignment while wrapped text, chips, attachments or notices grow the column upward.
        var singleLine = textColumn.IsSingleLine && (askOthers is null || askOthers.DesiredSize.Height <= 28);
        textColumn.AlignSingleLine(singleLine);
        if (askOthers is not null) askOthers.VerticalAlignment = singleLine ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        if (controls is not null) controls.VerticalAlignment = singleLine ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        if (insideAttach is not null)
        {
            insideAttach.VerticalAlignment = singleLine ? VerticalAlignment.Center : VerticalAlignment.Bottom;
            insideAttach.Margin = new Thickness(8, 0, 0, singleLine ? 0 : 8);
        }
        if (outsideAttach is not null) Canvas.SetBottom(outsideAttach, singleLine ? finalSize.Height / 2 - 18 : 4);
        var willBeSingleLine = textColumn.WillBeSingleLine && (askOthers is null || askOthers.DesiredSize.Height <= 28);
        PlaceInlineComposer(singleLine && voice is not null ? 1 : 0, willBeSingleLine && voice is not null ? 1 : 0);
        var size = base.ArrangeOverride(finalSize);
        root.Arrange(new Rect(finalSize));
        return size;
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
        // PORT-ONLY (voice-SPEC V-14.7): a composer leaving the screen stops a running session as a second click would; its text lands in the draft and is never sent.
        voice?.ComposerLeaving();
        WatchEscape(false);
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
        // :161-173. SwiftUI re-evaluates ComposerControls with the new values and keeps its views and their @State (the chip's
        // open popover among them), so the drawn controls are handed the new inputs and only a missing or new thread
        // draws them again.
        if (controls is { } drawn && inputs is { } update)
        {
            drawn.Update(update.Thread, update.GoesToHead, update.QueueGoesToHead, update.HeadsProvider);
            SetRecentDownloadsPicker(update.RecentDownloadsPicker);
            return;
        }
        if (controls is { } previous)
        {
            row.Children.Remove(previous);
        }
        controls = null;
        if (inputs is not { } value)
        {
            ShowAttach(false); // :160 no resolved thread: no controls at all, and the attachment button was one of them
            return;
        }
        var made = new ComposerControls(
            runtime,
            value.Thread,
            compact: compactModelChip,
            goesToHead: value.GoesToHead,
            queueGoesToHead: value.QueueGoesToHead,
            headsProvider: value.HeadsProvider,
            onSend: Send,
            voice: voice)
        {
            Margin = new Thickness(8, 0, 0, 0), // :145 spacing: 8
            HorizontalAlignment = HorizontalAlignment.Right, // :145-178 trailing padding; fixed fractional width must not centre in a rounded Auto column
            VerticalAlignment = VerticalAlignment.Bottom,
            UseLayoutRounding = false, // T46: a half-pixel row centre must not round before its glyphs rasterise
        };
        Grid.SetColumn(made, 1);
        row.Children.Add(made);
        controls = made;
        if (made.OrbSlot is { } orbSlot)
        {
            pillSlot?.Attach(orbSlot); // PORT-ONLY (VT-12): the pill and the question hang from the orb's slot
            question?.Attach(orbSlot, root);
            if (question is not null) question.ReturnFocus = () => orbSlot.HitTarget.Focus();
        }
        MakeAttachButtons(value.RecentDownloadsPicker);
        ShowAttach(true);
    }

    // T46 / OD-0132: the former bottom-aligned controls sat one point below the ordinary composer's centre.
    // Centre the single-line contents and retain their approved window anchor by moving the unchanged outline one point.
    //
    // ED-NEW-gate-fixes-6: the outline's point is not a step. Swift grows and shrinks the pill on smooth(0.28) (ComposerView.swift:604, ApplyHeight),
    // so the outline's point rides that same transition. The view still carries `rest` (the point of the state the contents are laid out in, flipped
    // with their alignment as before, so their window places and their places in the view are exactly what they were), and only the plate, the
    // shape that draws the outline, glides the rest of the way to where the text is going (WillBeSingleLine, known the moment the text changes);
    // the contents ride a counter-translation of the plate's, so they never move with it.
    private readonly TranslateTransform inlineComposerPlacement = new();
    private readonly TranslateTransform inlinePlateGlide = new();
    private readonly TranslateTransform inlineContentCounter = new();
    private AnimatedScalar? inlineInset;
    private double inlineRest;
    private double inlineGoal;
    private bool inlineSeen;
    private bool inlineGliding;

    private void PlaceInlineComposer(double rest, double goal)
    {
        inlineInset ??= new AnimatedScalar(this, 0, _ => ApplyInlinePlacement());
        inlineRest = rest;
        if (!inlineSeen || goal != inlineGoal)
        {
            // The first showing is the state as it is. Afterwards the point follows the height: the same curve and length, and none while the
            // height itself does not glide (a window being dragged, no live-resize state).
            var resize = GetValue(AppEnvironment.WindowLiveResizeProperty);
            var glides = inlineSeen && resize is not null && !resize.IsReshaping;
            inlineSeen = true;
            inlineGoal = goal;
            inlineInset.Set(goal, glides ? Animation.Smooth(0.28) : null, keepSpeed: true); // ED-NEW-perf-motion-11, as the height below
        }
        ApplyInlinePlacement();
    }

    private void ApplyInlinePlacement()
    {
        inlineComposerPlacement.Y = inlineRest;
        if (inlineRest != 0 || ReferenceEquals(RenderTransform, inlineComposerPlacement)) RenderTransform = inlineComposerPlacement;
        var glide = (inlineInset?.Value ?? 0) - inlineRest;
        inlinePlateGlide.Y = glide;
        inlineContentCounter.Y = -glide;
        var gliding = glide != 0;
        if (gliding != inlineGliding)
        {
            inlineGliding = gliding;
            if (Child is { } plate) plate.RenderTransform = gliding ? inlinePlateGlide : null;
            if (layers is not null) layers.RenderTransform = gliding ? inlineContentCounter : null;
        }
        pillSlot?.InvalidateArrange();
    }

    // ---- the attachment button at the pill's left (voice-SPEC V-14.1; owner-ruled, OD-0091 and OD-0086) ----

    // The width the pill gives up for the button outside it: the 36 point circle and 8 to the pill.
    private const double OutsideAttachWidth = 44;

    // The text starts 14 in from the pill's edge; the button inside it takes 34 and 6 more.
    private const double InsideAttachInset = 34;

    // ED-NEW-voicefix-ui-6: ink gaps measured at 100% and 125% were 23.2/24 points attach glyph to ask-others glyph and 7.2/7 ask-others glyph to text; moving the
    // ask-others glyph this far left makes the two gaps equal (15 to 15.5 points) at both scales.
    internal const double AskOthersPull = 8.25;

    private void MakeAttachButtons(bool recentDownloadsPicker)
    {
        if (insideAttach is not null)
        {
            SetRecentDownloadsPicker(recentDownloadsPicker);
            return;
        }
        var showingRecents = new Binding<bool>(() => state.ShowingRecents, shown => state.ShowingRecents = shown);
        Action<IReadOnlyList<string>> onAttach = paths => Attach(paths.Select(path => (AttachmentSource)new AttachmentSource.File(path)).ToList());
        insideAttach = new ComposerAttachButton(ComposerAttachPlace.Inside, recentDownloadsPicker, showingRecents, onAttach, ChooseFiles)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 8), // 8 from the left, on the controls row's bottom line (the pill pads 8)
        };
        outsideAttach = new ComposerAttachButton(ComposerAttachPlace.Outside, recentDownloadsPicker, showingRecents, onAttach, ChooseFiles) { UseLayoutRounding = false };
        // in the margin the pill gave up, centred on the last line of a one-line pill: 22 up from its foot, so 4 clear of the 36 point circle's own
        Canvas.SetLeft(outsideAttach, -OutsideAttachWidth);
        Canvas.SetBottom(outsideAttach, 4);
    }

    private void SetRecentDownloadsPicker(bool recentDownloadsPicker)
    {
        if (insideAttach is not null) insideAttach.RecentDownloadsPicker = recentDownloadsPicker;
        if (outsideAttach is not null) outsideAttach.RecentDownloadsPicker = recentDownloadsPicker;
    }

    // :160 the button is one of the controls: it is there when they are.
    private void ShowAttach(bool shown)
    {
        attachShown = shown;
        ApplyAttachPlace();
    }

    // 0 is inside the pill, 1 outside it; between, the two buttons cross over (the design's .attach.in and .attach.out: opacity and a scale from 0.6), the pill's
    // width and the text's inset ease between their two values. A button nobody sees is not in the tree, so Named(view, "Attach files") finds the one that shows.
    private void ApplyAttachPlace()
    {
        Yield();
        var shown = attachShown && insideAttach is not null && outsideAttach is not null;
        Margin = new Thickness(shown ? OutsideAttachWidth * widthPlace.Value : 0, Margin.Top, Margin.Right, Margin.Bottom);
        padded.Padding = new Thickness(14 + (shown ? InsideAttachInset * (1 - insetPlace.Value) : 0), 8, 8, 8);
        // ED-NEW-voicefix-ui-6 (owner 2026-10-11): beside the inside attach button the ask-others button sits AskOthersPull to the left of the text's start, so its
        // glyph is as far from the attach glyph as from the text; the text keeps its place
        var pull = shown ? AskOthersPull * (1 - insetPlace.Value) : 0;
        if (askOthers is not null)
        {
            askOthers.Margin = new Thickness(-pull, 0, pull, 0);
            // ED-NEW-voicefix-ui-9: out of the open pill's way, as the attach buttons cross over (opacity, and a scale from 0.6)
            var gone = AskGone;
            askOthers.Opacity = Math.Clamp(1 - gone, 0, 1);
            askYieldScale.ScaleX = askYieldScale.ScaleY = 1 - 0.4 * gone;
            if (!ReferenceEquals(askOthers.RenderTransform, askYieldScale)) askOthers.RenderTransform = askYieldScale;
            askOthers.IsHitTestVisible = gone < 0.5;
        }
        if (!shown)
        {
            if (insideAttach is not null) layers!.Children.Remove(insideAttach);
            if (outsideAttach is not null) outsideHost.Children.Remove(outsideAttach);
            return;
        }
        var inside = insideAttach!;
        var outside = outsideAttach!;
        var fade = fadePlace.Value;
        var scale = scalePlace.Value;
        // the inside button stays until all four have reached the outside; the outside one is there from the first step out until all four are back inside
        var insideHere = widthPlace.Value < 1 || insetPlace.Value < 1 || fade < 1 || scale < 1;
        var outsideHere = widthPlace.Value > 0 || insetPlace.Value > 0 || fade > 0 || scale > 0;
        if (insideHere)
        {
            if (!layers!.Children.Contains(inside)) layers.Children.Add(inside);
            var stay = 1 - AttachGone; // ED-NEW-voicefix-ui-9: out of the open pill's way
            inside.Show(Math.Clamp((1 - fade) * stay, 0, 1), (1 - scale) * stay);
        }
        else layers!.Children.Remove(inside);
        if (outsideHere)
        {
            if (!outsideHost.Children.Contains(outside)) outsideHost.Children.Add(outside);
            outside.Show(fade, scale);
        }
        else outsideHost.Children.Remove(outside);
    }

    // ED-NEW-voicefix-ui-9 (lead 2026-10-11, after the owner's narrow-composer finding): the open inline pill never runs over the buttons at the composer's left (they
    // draw over it). When the left end of the inline pill (at the width it is easing to, or where it is drawn while it folds) comes within PillClearance of a button's
    // layout box, that button goes out of its way as the attach buttons cross over (opacity, and a scale from 0.6): the ask-others button, and the attach button too
    // when the pill reaches it. Two motions hold it, the larger wins: a spring (0 in place, 1 gone), and the pill's own travel, so it is gone by the time the pill's end
    // reaches its clearance however fast the pill opens (the pill's end, on its way from the closed pill round the orb to the button's clearance, 0 to 1). It comes back
    // on the spring once the pill's end has folded back past its clearance, from wherever it shows then.
    private const double PillClearance = 6;
    private static readonly Animation YieldSpring = Animation.Spring(duration: 0.4, bounce: 0.12);
    private double attachNear;
    private double askNear;

    private double AttachGone => Math.Max(attachYield?.Value ?? 0, attachNear);
    private double AskGone => Math.Max(askYield?.Value ?? 0, askNear);

    private void YieldToPill()
    {
        if (Yield()) ApplyAttachPlace();
    }

    /// <summary>Brings the two buttons' yield up to date with the pill; true when what they show changed.</summary>
    private bool Yield()
    {
        if (pillSlot is null || attachYield is null || askYield is null) return false;
        var pill = pillSlot.Pill;
        double Here(double? slotX) => slotX is { } x && pillSlot.TranslatePoint(new Point(x, 0), this) is { } at ? at.X : double.PositiveInfinity;
        var target = Here(pillSlot.InlineLeftTarget);
        var home = Here(pillSlot.InlineHomeLeft);
        var drawn = pill.Style == VoiceListeningStyle.Pill && pill.ShownOpacity > 0 && pill.TranslatePoint(default, this) is { } end ? end.X : double.PositiveInfinity;
        var left = Math.Min(target, drawn);
        // the buttons' layout boxes decide (a button on its way out never changes the answer)
        double? Edge(Control? control) =>
            control is { IsVisible: true } && control.GetVisualParent() is Visual parent && parent.TranslatePoint(new Point(control.Bounds.Right, 0), this) is { } at ? at.X + PillClearance : null;
        var attachEdge = insideAttach is not null && attachShown && layers is not null && layers.Children.Contains(insideAttach) ? Edge(insideAttach) : null;
        var askEdge = askOthers is { Button.IsVisible: true } ? Edge(askOthers) : null;
        var attach = attachEdge is { } a && left < a;
        var ask = attach || askEdge is { } k && left < k;
        double Travel(double? edge) =>
            edge is not { } e || double.IsInfinity(drawn) || double.IsInfinity(home) ? 0 : home <= e ? 1 : Math.Clamp((home - drawn) / (home - e), 0, 1);
        var changed = false;
        void Follow(ref bool yielding, ref double near, AnimatedScalar spring, bool now, double? edge)
        {
            var travel = Travel(edge);
            var from = Math.Max(spring.Value, Math.Max(near, travel));
            var next = now ? travel : 0;
            if (next != near) { near = next; changed = true; }
            if (now == yielding) return;
            yielding = now;
            changed = true;
            if (!now) spring.Set(from, null); // back from wherever it shows
            spring.Set(now ? 1 : 0, YieldSpring);
        }
        Follow(ref attachYielding, ref attachNear, attachYield, attach, attachEdge);
        Follow(ref askYielding, ref askNear, askYield, ask, askEdge);
        return changed;
    }

    // ---- the voice views (voice-SPEC V-14.3, V-14.4, V-14.6, V-8.5) ----

    // Esc while voice input is live cancels it and is consumed, whatever has the keyboard in this window; the editor takes no typing meanwhile; a note shows in the field
    // or the notice line; the composer leaving stops the session.
    private void StartVoiceViews(ComposerVoiceController voiceController)
    {
        this.Track(() => voiceController.IsLive || voiceController.Preparing, live =>
        {
            WatchEscape(live);
            ApplyDictating();
        });
        // PORT-ONLY (voice-SPEC V-8.3, VT-11): the dictation layer follows the session; the editor is read-only while the session is live and while the layer still draws
        var layer = textColumn.Dictation;
        layer.ActiveChanged += ApplyDictating;
        voiceController.DictationBegan += layer.Begin;
        voiceController.Transcript += layer.Apply;
        voiceController.DictationAbandoned += layer.Abandon;
        this.Track(() => (voiceController.Note, runtime.Draft.Text.Length == 0), now =>
        {
            RefreshControllerVoiceNote();
        });
    }

    // The controller owns its note's arrival placement. A release-error writer must not
    // resurrect a field note in the notice line after the user has started typing.
    internal void RefreshControllerVoiceNote()
    {
        if (voice is null) return;
        var note = voice.Note;
        var fieldVisible = runtime.Draft.Text.Length == 0;
        if (!ReferenceEquals(note, shownNote))
        {
            shownNote = note;
            noteInField = fieldVisible;
        }
        textColumn.SetVoiceNote(note, noteInField, fieldVisible);
    }

    private void ApplyDictating()
    {
        if (controller.TextViewInstance is { } box) box.Dictating = voice is { IsLive: true } || textColumn.Dictation.IsActive;
    }

    private void WatchEscape(bool live)
    {
        if (live && escapeWatch is null && TopLevel.GetTopLevel(this) is { } top)
        {
            escapeWatch = top;
            top.AddHandler(InputElement.KeyDownEvent, OnVoiceKey, RoutingStrategies.Tunnel);
        }
        else if (!live && escapeWatch is { } watched)
        {
            watched.RemoveHandler(InputElement.KeyDownEvent, OnVoiceKey);
            escapeWatch = null;
        }
    }

    private void OnVoiceKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || voice is not { } active || !(active.IsLive || active.Preparing)) return;
        // V-14.4 "otherwise Esc keeps its existing meaning": a sheet or popover opened after the session went live is the keyboard's first (as an NSWindow's sheet or an NSPopover is a
        // key window of its own); this handler was added when the session went live, so it runs before theirs and has to ask
        if (sender is TopLevel top && (SheetKeyboard.IsTaken(top) || WindowTransients.Newest(top) is not null)) return;
        active.Cancel();
        e.Handled = true;
    }

    // The draft as the voice controller needs it: the text and the editor's selection (a caret at the end when the editor is not on screen).
    private DraftSelection DraftSelection()
    {
        if (controller.TextViewInstance is { LiveText: not null } drawing)
        {
            // a dictation is drawing the words: the draft the session began with is the editor's own text and selection
            var first = Math.Min(drawing.SelectionStart, drawing.SelectionEnd);
            return new DraftSelection(drawing.Text ?? "", first, Math.Abs(drawing.SelectionEnd - drawing.SelectionStart));
        }
        var text = runtime.Draft.Text;
        if (controller.TextViewInstance is not { } box || (box.Text ?? "") != text) return new DraftSelection(text, text.Length, 0);
        var start = Math.Min(box.SelectionStart, box.SelectionEnd);
        return new DraftSelection(text, start, Math.Abs(box.SelectionEnd - box.SelectionStart));
    }

    // One edit of the draft, the caret after it; the editor's own replacement keeps its undo stack (ComposerController.ReplaceCharacters).
    private void ReplaceDraft(int start, int length, string replacement)
    {
        var text = runtime.Draft.Text;
        if (controller.TextViewInstance is { } box && (box.LiveText is not null || (box.Text ?? "") == text))
        {
            // the words land as one edit of the editor's own text (V-8.7: one undo step); the editor stays read-only until the layer hands the picture back
            box.RunUnlocked(() => controller.ReplaceCharacters(start, length, replacement));
            return;
        }
        runtime.Draft = runtime.Draft with { Text = text[..start] + replacement + text[(start + length)..] };
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
    // OD-0130 (owner finding F5, 2026-10-08): "the text gets cut off right there; it should fade smoothly as it nears the pill". While the
    // voice pill has pushed the text column's right edge in (V-12.6's inset), the column's last 24 points fade to nothing instead of
    // ending in a hard clip; at rest (no inset) the column carries no mask, so typing is unaffected.
    private const double PillFadeWidth = 24;

    private void FadeBesidePill(double inset)
    {
        var width = textColumn.Bounds.Width;
        if (inset < 0.5) { textColumn.OpacityMask = null; return; }
        // ED-NEW-voicefix-ui-2: a column no wider than the fade (a narrow window, the pill open) fades from its left edge, and one the pill has squeezed to
        // nothing shows nothing (its content would only run on under the pill); both used to drop the mask and draw at full strength up to and under the pill
        if (width <= 0) { textColumn.OpacityMask = new SolidColorBrush(Colors.Transparent); return; }
        var start = Math.Max(0, (width - PillFadeWidth) / width);
        textColumn.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, start), new GradientStop(Colors.Transparent, 1) },
        };
    }

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
                else if (runtime.IsTerminalInDrawer) runtime.IsTerminalVisible = false; // web W4: only the drawer; a pane stays until it is closed
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
        if (Model.InstallerHoldsNewTurns) return;
        if (runtime.Draft.IsEmpty) return; // :371
        historyIndex = null; // :372
        state.Suggestions = new SuggestionState(); // :373
        if (!runtime.IsRunning && askOthers?.HasSelection == true)
        {
            opinionDraft = runtime.Draft;
            var message = opinionDraft.OutgoingText;
            _ = runtime.SpawnInstallerTurn(() => askOthers.SendAsync(message));
            return;
        }
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

    internal ComposerVoiceController? Voice => voice;

    /// <summary>PORT-ONLY (VT-12): the listening pill's layer (null without voice typing).</summary>
    internal VoicePillSlot? VoicePills => pillSlot;

    /// <summary>PORT-ONLY (VT-12): the language question (null without voice typing).</summary>
    internal VoiceLanguageQuestion? LanguageQuestion => question;

    /// <summary>PORT-ONLY (VT-12, V-12.7): the open floating pill's rectangle in <paramref name="relativeTo"/>'s coordinates, null when none is open.</summary>
    internal Rect? VoiceFloatRect(Visual relativeTo)
    {
        if (pillSlot?.FloatTarget is not { } rect || pillSlot.TranslatePoint(rect.TopLeft, relativeTo) is not { } origin) return null;
        return new Rect(origin, rect.Size);
    }

    /// <summary>PORT-ONLY (VT-12, V-12.7): the floating pill opened, closed or moved.</summary>
    internal event Action? VoiceFloatChanged
    {
        add { if (pillSlot is not null) pillSlot.FloatTargetChanged += value; }
        remove { if (pillSlot is not null) pillSlot.FloatTargetChanged -= value; }
    }

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
    private NoticeSlot? noticeSlot; // the notice's row: it folds open and shut, so the pill's height eases
    // PORT-ONLY (voice-SPEC V-14.6): a voice note shows over the placeholder in the field, or in a notice row of its own when the draft holds text.
    private readonly Panel noteHost = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = false };
    private FieldFade? placeholderFade;
    private bool dictationWords;
    private VoiceNoteLine? fieldLine;
    private FieldFade? fieldFade;
    private NoticeSlot? voiceSlot;
    private VoiceNoteLine? voiceLine;
    private IFrameSource? heightFrames;
    private Action<TimeSpan>? heightFrame;
    // The clip's glide under way (where it set out from, its goal, when and along what), so the next height carries its speed on.
    private (double From, double To, ContinuousInstant Start, Animation Curve)? heightGlide;
    private bool heightGlideRunning;

    // PORT-ONLY (ED-NEW-session-hotkeys): the text view's first measure is on its way to the clip. The source starts the text
    // height at its one-line size (:552, 20) and its first measure finds that same size, so nothing moves as a chat is first
    // shown; Windows' text metrics give a slightly taller line (22), so the port would grow the box under the reader's eyes as
    // a new chat comes in. The first height measured is the height the box is first drawn at; every later change glides.
    private bool firstMeasure;

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
        if (attachmentNotice is { } text)
        {
            // :576-583 a notice that was there when the column was made: no arrival, and the row is whole.
            noticeSlot = new NoticeSlot();
            noticeSlot.Place(Notice(text).Transition(ArrivedWithColumn));
            stack.Children.Add(noticeSlot);
        }

        // :584-594
        textView = new ComposerTextView(
            text: new Binding<string>(() => runtime.Draft.Text, value => runtime.Draft = runtime.Draft with { Text = value }),
            height: new Binding<double>(() => state.TextHeight, value =>
            {
                firstMeasure = textView is null || !textView.HasMeasured;
                state.TextHeight = value;
            }),
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
        // The note layer lies over the field; only its link takes the pointer, the rest of it lets the text view have it.
        stack.Children.Add(new Grid { Children = { clip, noteHost } });
        placeholderFade = new FieldFade(this, true, textView.SetPlaceholderFade);
        // PORT-ONLY (voice-SPEC V-8.2, VT-11): the draft follows the dictated words as they change, and the placeholder gives way to them
        textView.Dictation.TextChanged += value => runtime.Draft = runtime.Draft with { Text = value };
        textView.Dictation.HasWordsChanged += has =>
        {
            dictationWords = has;
            placeholderFade!.Set(!has && fieldLine is null);
        };
        Child = stack;
        ApplyHeight();
        this.Track(() => ComposerHeight, _ => ApplyHeight());
        // ComposerTextView.swift:293-306 updateNSView reads the draft through the text binding, so a draft written
        // elsewhere (recall, a send, a take-back) reaches the text view.
        this.Track(() => runtime.Draft.Text, _ => textView.Update(this.placeholder, this.onKey, this.onAttach));
    }

    /// <summary>PORT-ONLY (voice-SPEC V-8.3, VT-11): the layer that draws live dictation over the editor.</summary>
    internal DictationLayer Dictation => textView.Dictation;

    internal bool IsSingleLine => WillBeSingleLine && clip.Height <= 22;

    /// <summary>Whether the column is one line once the height glide (the clip) has arrived: the text view takes its height at once (:599).</summary>
    internal bool WillBeSingleLine => textView.Height <= 22 && stack.Children.Take(stack.Children.Count - 1).All(child => child.DesiredSize.Height <= .001);

    internal void AlignSingleLine(bool singleLine) => stack.VerticalAlignment = singleLine ? VerticalAlignment.Center : VerticalAlignment.Stretch;

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
            // :576-582 .transition(.softAppear), inside withAnimation(Chrome.panelSlide) (:523): the line's own arrival is the
            // transition's, and the room it takes opens under the change's transaction so the pill grows with it (OD-0076, S5).
            var slot = new NoticeSlot();
            stack.Children.Insert(2, slot);
            slot.Arrive(Notice(arriving).Transition(SwiftTransition.SoftAppear));
            noticeSlot = slot;
        }
        else if (attachmentNotice is null)
        {
            // :224 withAnimation(Chrome.panelSlide) { attachmentNotice = nil }: the line leaves on the transition's 0.12 s fade and
            // its room folds shut under the transaction.
            if (noticeSlot is { } leaving && notice is { } gone) leaving.Leave(gone, () => stack.Children.Remove(leaving));
            noticeSlot = null;
            notice = null;
        }
        else if (notice is { } shown)
        {
            shown.Text = attachmentNotice; // the same Text with new words
        }
    }

    /// <summary>
    /// PORT-ONLY (voice-SPEC V-14.6, OD-0076): the voice note, if any. In the field (<paramref name="inField"/> and the draft still empty, <paramref name="fieldVisible"/>)
    /// it crossfades with the placeholder; otherwise it shows in a notice row of its own, which folds open and shut as the attachment notice's does.
    /// </summary>
    internal void SetVoiceNote(VoiceNoteContent? note, bool inField, bool fieldVisible)
    {
        ShowFieldNote(note is not null && inField && fieldVisible ? note : null);
        ShowNoticeNote(note is not null && !inField ? note : null);
    }

    private void ShowFieldNote(VoiceNoteContent? wanted)
    {
        if (ReferenceEquals(wanted, fieldLine?.Content)) return;
        if (fieldLine is { } leaving && fieldFade is { } leavingFade)
        {
            leavingFade.Set(false);
            MainActor.AsyncAfter(TimeSpan.FromSeconds(0.3), () => noteHost.Children.Remove(leaving)); // gone once it has faded
            fieldLine = null;
            fieldFade = null;
        }
        if (wanted is null)
        {
            placeholderFade!.Set(!dictationWords);
            return;
        }
        var line = new VoiceNoteLine(wanted, VoiceNotePlace.Field) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var fade = new FieldFade(line, false, (opacity, rise) =>
        {
            line.Opacity = opacity;
            line.SetRise(rise);
        });
        noteHost.Children.Add(line);
        fieldLine = line;
        fieldFade = fade;
        fade.Set(true);
        placeholderFade!.Set(false);
    }

    private void ShowNoticeNote(VoiceNoteContent? wanted)
    {
        if (ReferenceEquals(wanted, voiceLine?.Content)) return;
        if (voiceSlot is { } slot && voiceLine is { } gone)
        {
            slot.Leave(gone, () => stack.Children.Remove(slot));
            voiceSlot = null;
            voiceLine = null;
        }
        if (wanted is null) return;
        var line = new VoiceNoteLine(wanted, VoiceNotePlace.Notice) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        var row = new NoticeSlot();
        stack.Children.Insert(2, row); // after the chips and the attachments, before the text
        row.Arrive(line.Transition(SwiftTransition.SoftAppear));
        voiceSlot = row;
        voiceLine = line;
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
        var first = firstMeasure;
        firstMeasure = false;
        // ED-NEW-perf-motion-11: the speed of the glide under way, read on its own clock before it is replaced.
        var speed = 0.0;
        if (heightGlide is { } glide && heightGlideRunning)
            speed = (glide.To - glide.From) * glide.Curve.Speed((SwiftClock.Now - glide.Start).TotalSeconds);
        heightGlideRunning = false;
        if (resize is null || resize.IsReshaping || first || double.IsNaN(clip.Height) || clip.Height == height)
        {
            clip.Height = height;
            return;
        }
        // :601-604 Only the top-aligned clip interpolates on the smooth 0.28 curve; each new height retargets it. SwiftUI's .smooth is a
        // spring, which keeps its velocity through a retarget (ED-NEW-perf-motion-11): a line typed mid-glide carries the glide on at its
        // speed instead of stopping it dead and setting off again.
        var from = clip.Height;
        var animation = Animation.Smooth(0.28).WithStartSpeed(speed / (height - from));
        var start = SwiftClock.Now;
        heightGlide = (from, height, start, animation);
        heightGlideRunning = true;
        heightFrames = FrameClock.Source.Current;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            var complete = elapsed >= animation.Duration;
            clip.Height = complete ? height : from + (height - from) * animation.Progress(elapsed);
            if (complete) { heightFrame = null; heightGlideRunning = false; }
            else heightFrames.RequestFrame(frame!);
        };
        heightFrame = frame;
        heightFrames.RequestFrame(frame);
    }
}

/// <summary>
/// PORT-ONLY (OD-0076, UI-COMPOSER-004; ED-282): the room the attachment notice takes in the text column. SwiftUI lays the
/// column out with the notice and animates the move under the transaction the change was made in (:523, :224), so the text view
/// and the pill's height ease; an Avalonia transition moves no sibling, so the notice's row folds open and shut under the same
/// transaction instead, its line top-aligned and clipped (ChipRowCollapse's way). The line itself keeps its own transition.
/// </summary>
internal sealed class NoticeSlot : Decorator
{
    private readonly Panel inner = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Border clip;
    private double natural;
    private double fraction = 1;
    private const double SoftRise = 4; // Chrome.swift:943
    private IFrameSource? source;
    private Action<TimeSpan>? frame;
    private IFrameSource? softSource;
    private Action<TimeSpan>? softFrame;

    public NoticeSlot()
    {
        clip = new Border { Child = inner, ClipToBounds = true };
        Child = clip;
    }

    /// <summary>A line that is there from the first render: no transition in, whole.</summary>
    public void Place(Control line) => SwiftTransition.Place(inner, 0, line);

    /// <summary>
    /// A line that arrives: the row opens from nothing and the line comes in as <c>.softAppear</c> does (Chrome.swift:949-955): its
    /// opacity from 0 and a 4 point rise, on smooth(0.32). The foundation's SoftAppear modifier carries no animatable data and so
    /// applies its identity at once (SwiftAnimation.cs Gather), which left the line at full opacity at its first frame; its removal half,
    /// the 0.12 s ease-out fade, is still the transition's.
    /// </summary>
    public void Arrive(Control line)
    {
        Apply(0);
        SwiftTransition.Place(inner, 0, line);
        Run(0, 1, Transaction.Current ?? Chrome.PanelSlide, null);
        var rise = new TranslateTransform(0, SoftRise);
        line.Opacity = 0;
        line.RenderTransform = rise;
        var start = SwiftClock.Now;
        var soft = Animation.SoftAppear;
        softSource = FrameClock.Source.Current;
        softFrame = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            var complete = elapsed >= soft.Duration;
            var progress = complete ? 1 : Math.Clamp(soft.Progress(elapsed), 0, 1);
            line.Opacity = progress; // Chrome.swift:942
            rise.Y = SoftRise * (1 - progress); // Chrome.swift:943
            if (complete) softFrame = null;
            else softSource!.RequestFrame(softFrame!);
        };
        softSource.RequestFrame(softFrame);
    }

    /// <summary>The line leaves on its transition's removal and the row folds shut; <paramref name="gone"/> runs when both are over.</summary>
    public void Leave(Control line, Action gone)
    {
        var fold = Transaction.Current ?? Chrome.PanelSlide;
        if (softFrame is not null) softSource!.CancelFrame(softFrame); // a line still arriving leaves from where it is
        softFrame = null;
        var removed = false;
        var folded = false;
        void Finish()
        {
            if (removed && folded) gone();
        }
        // The transition takes the line out of the row when its fade is over; the row's own list says when.
        void Watch(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        {
            if (inner.Children.Count != 0) return;
            inner.Children.CollectionChanged -= Watch;
            removed = true;
            Finish();
        }
        inner.Children.CollectionChanged += Watch;
        SwiftTransition.Remove(inner, line);
        Run(fraction, 0, fold, () => { folded = true; Finish(); });
    }

    private void Measure()
    {
        clip.Height = double.NaN;
        inner.Measure(Size.Infinity);
        natural = inner.DesiredSize.Height;
    }

    private void Apply(double to)
    {
        fraction = to;
        clip.Height = to >= 1 ? double.NaN : Math.Max(0, natural * to);
    }

    private void Run(double from, double to, Animation animation, Action? done)
    {
        if (frame is not null) source!.CancelFrame(frame);
        frame = null;
        Measure();
        Apply(from);
        var start = SwiftClock.Now;
        source = FrameClock.Source.Current;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            var complete = elapsed >= animation.Duration;
            Apply(complete ? to : from + (to - from) * animation.Progress(elapsed));
            if (complete)
            {
                frame = null;
                done?.Invoke();
            }
            else source!.RequestFrame(frame!);
        };
        source.RequestFrame(frame);
    }
}

/// <summary>
/// :609-657 <c>private struct ComposerControls: View</c>. PORT-ONLY, owner-ruled (OD-0091, OD-0086; voice-SPEC V-14.1, V-14.2): the attachment button (:626-646) is no longer the
/// first item here; it moved to the composer's left (<see cref="ComposerAttachButton"/>), and the voice orb's slot stands in its old place when voice typing is on.
/// </summary>
internal sealed class ComposerControls : Decorator
{
    private readonly ModelEffortSlot slot;
    private readonly ContextMeter? meter;
    private readonly SendButton send;

    /// <summary>PORT-ONLY (VT-12): the voice orb's slot, null without voice typing.</summary>
    internal VoiceOrbSlot? OrbSlot { get; }

    /// <summary>:613-623 the inputs a re-evaluated body is handed. The views below keep their state (the chip's open popover, the
    /// ring's popover) and take the new values.</summary>
    internal void Update(ChatThread thread, bool goesToHead, bool queueGoesToHead, ProviderKind? headsProvider)
    {
        slot.Update(thread);
        meter?.Update(thread.Provider, headsProvider);
        send.Update(goesToHead, queueGoesToHead);
    }

    // :613-623 the view's inputs; :625-656 body
    public ComposerControls(
        ThreadRuntime runtime,
        ChatThread thread,
        bool compact,
        bool goesToHead,
        bool queueGoesToHead,
        ProviderKind? headsProvider,
        Action onSend,
        ComposerVoiceController? voice = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; // :626
        if (voice is not null) row.Children.Add(OrbSlot = new VoiceOrbSlot(voice)); // V-14.2: the orb, where the attachment was
        // PORT-ONLY (OD-0115 composer=A, step 3a): the account capsule, only with a second account of the service or a pill relay; it sits before the model chip
        if (Marrow.App.Port.MultiAccount.Capsule.ComposerCapsuleSeams.Offer.Current(runtime) is { } capsuleSlot) { capsuleSlot.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(Marrow.App.Port.MultiAccount.Handshake.HandshakeCapsuleHost.With(Marrow.App.Port.MultiAccount.Link.LinkComposerHost.With(capsuleSlot, runtime), capsuleSlot, runtime)); }
        row.Children.Add(slot = new ModelEffortSlot(runtime, thread, compact)); // :647
        // PORT-ONLY Ignis U4 (OD-0134, ED-NEW-ignis-views): the Kindle capsule takes the ring's place in a supported provider's chat while Ignis is bound; else the ContextMeter
        if (!compact) row.Children.Add(Marrow.App.Port.Ignis.Views.IgnisViews.ComposerOffer(runtime.ThreadID, thread.Provider) ?? (meter = new ContextMeter(runtime, thread.Provider, headsProvider) { UseLayoutRounding = false })); // :650-651; retain the odd 27pt ring's half-point centre at fractional DPI
        row.Children.Add(send = new SendButton(runtime, goesToHead, queueGoesToHead, onSend) { UseLayoutRounding = false }); // :653; keep its painted disc centred at fractional DPI
        foreach (var child in row.Children) child.VerticalAlignment = VerticalAlignment.Center; // :626 HStack's default centre alignment
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
        // PORT-ONLY CZ-5 (customization-SPEC CZ-10.4, OD-0089): the dots' colour is the workingDots setting's for this thread's model; a change reaches the running dots eased (ComposerWorkingDots.Tint).
        // Registered before the run flag's hook, so dots that arrive wear it from the start.
        this.Track(() => Marrow.App.Port.Settings.WorkingDotsColor.For(AppEnvironment.GetAppModel(this), runtime.ThreadID), color => { tint = color; if (dots is not null) dots.Tint = color; });
        var panel = new Panel();
        var clip = new Border { Child = panel, ClipToBounds = true }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand(); // :671 `.clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))`: clipped to the shape (ED-181)
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

    // :666-672 the dots, their frame, clip and opacity transition (PR-COMPOSER-101).
    private Control Dots()
    {
        var dots = this.dots = new ComposerWorkingDots { VerticalAlignment = VerticalAlignment.Bottom, Tint = tint }; // :667-669
        var geometry = new Grid { Children = { dots } }; // :666 GeometryReader
        geometry.SizeChanged += (_, e) => dots.Height = e.NewSize.Height * 0.4; // :668
        return geometry;
    }

    private ComposerWorkingDots? dots;
    private SwiftColor tint = Chrome.Accent;
}

/// <summary>:679-690 <c>private struct ModelEffortSlot: View</c>.</summary>
internal sealed class ModelEffortSlot : Decorator
{
    // :683-685 the view's inputs; :687-689 body
    private readonly ModelEffortButton chip;
    private ChatThread thread;
    private bool hasHistory;

    public ModelEffortSlot(ThreadRuntime runtime, ChatThread thread, bool compact)
    {
        this.thread = thread;
        hasHistory = runtime.Turns.Count > 0;
        Child = chip = new ModelEffortButton(thread, hasHistory, compact); // :688
        // :688 the slot alone reads turns. The chip is handed the new value and stays, with its popover (:121).
        this.Track(() => runtime.Turns.Count > 0, history => chip.Update(this.thread, hasHistory = history));
    }

    /// <summary>:683-685 the thread the composer now draws; the chip keeps its popover and takes it.</summary>
    internal void Update(ChatThread thread) => chip.Update(this.thread = thread, hasHistory);
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
            Template = ComposerButtonTemplates.PlainWithPadding; // :726-728 include the rectangular padded hit area
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
        // :698-700 the inputs a re-evaluated body is handed
        private (ProviderKind Provider, ProviderKind? HeadsProvider) inputs;
        public (ProviderKind Provider, ProviderKind? HeadsProvider) Inputs { get => Read(inputs); set => Set(ref inputs, value); }
    }
    private readonly ViewState state = new();
    /// <summary>:698-700 the provider and heads provider the composer now draws; the ring and its popover stay and read them.</summary>
    internal void Update(ProviderKind provider, ProviderKind? headsProvider) => state.Inputs = (provider, headsProvider);
    // :698-700 the view's inputs; :704-762 body
    public ContextMeter(ThreadRuntime runtime, ProviderKind provider, ProviderKind? headsProvider)
    {
        state.Inputs = (provider, headsProvider);
        // :713 `if !isPoppedOut, ... { Button }`: the ring is an if-branch, so it arrives and leaves through the default opacity
        // transition under the change's own transaction (ED-282-5). The slot holds it while it is there.
        var slot = new Panel();
        Child = slot;
        ContextButton? button = null;
        Popup? popup = null;
        var presented = false;
        var inTree = false; // whether the ring is in the slot (a leaving ring is not)
        var arrived = false; // the first showing is the view arriving with its parent, which runs no transition
        this.Track(() => (Usage: runtime.Usage, Model: AppEnvironment.GetAppModel(this), Provider: state.Inputs.Provider, Heads: state.Inputs.HeadsProvider,
                Credits: CreditsReader.ExposesCredits(state.Inputs.Provider) && AppEnvironment.GetAppModel(this).Settings.HasAPIKey(state.Inputs.Provider),
                PoppedOut: AppEnvironment.GetAppModel(this).Settings.ShowsUsagePanel), input =>
        {
            var fraction = input.Usage?.Fraction; // :705-706
            if (input.PoppedOut || !(fraction is not null || PlanLimitsReader.ExposesLimits(input.Provider) || input.Credits)) // :709-713
            {
                if (presented && popup is not null) Presentation.Presenter.Current.SetPresented(popup, false);
                presented = false;
                state.IsPresented = false;
                if (inTree && button is { } leaving)
                {
                    inTree = false;
                    leaving.IsHitTestVisible = false; // a view on its way out takes no click
                    Transaction.WithAnimation(RingMotion(), () => SwiftTransition.Remove(slot, leaving));
                }
                arrived = true;
                return;
            }
            var ring = new Grid { Width = 15, Height = 15 }; // :717,725
            // :718-719 Circle().stroke(.quaternary, lineWidth: 2.5): the stroke is centred on the 15 point circle, as the arc's is. An Avalonia
            // Ellipse insets its outline by half the stroke, which drew the track 1.25 inside the arc (VIDEO static.composer-context-ring:
            // track centre-line radius 7.53), so the track is the same 15 by 15 circle geometry as a Path.
            var track = new Avalonia.Controls.Shapes.Path { StrokeThickness = 2.5, Data = new EllipseGeometry(new Rect(0, 0, 15, 15)) };
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
                button.HorizontalAlignment = HorizontalAlignment.Left; // :717-728 the label keeps its padded natural width
                button.Padding = new Thickness(6, 5); // :726-728 rectangular hit area
                popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true }; // :737
                popup.Closed += (_, _) => { popup.Child = null; state.IsPresented = false; };
            }
            else button.Content = ring;
            if (!inTree)
            {
                inTree = true;
                button.IsHitTestVisible = true;
                var arriving = button;
                if (!arrived) SwiftTransition.Place(slot, 0, arriving);
                else Transaction.WithAnimation(RingMotion(), () => SwiftTransition.Insert(slot, 0, arriving));
            }
            arrived = true;
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
            if (shown && popup.Child is null) popup.Child = Content(runtime.Usage, AppEnvironment.GetAppModel(this));
            Presentation.Presenter.Current.SetPresented(popup, shown);
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
            stack.Children.Add(new UsagePanel(usage, state.Inputs.Provider, state.Inputs.HeadsProvider)); // :757 width and padding are owned by UsagePanel
            var presentedContent = stack.PresentedChrome(); // :759
            // :757 SwiftUI hands the popover the presenting view's environment; the popup's own window sits outside this view's
            // tree, so the panel's inherited @Environment(AppModel.self) (UsagePanel.swift:9,27) is attached to it directly,
            // as ModelEffortButton does for its panel (EffortSlider.cs).
            AppEnvironment.SetAppModel(presentedContent, model);
            return presentedContent;
        }
    }

    // The ring comes and goes under the change's own transaction, else panelSlide (a frame clock that is not running cuts).
    private static Animation? RingMotion() => FrameClock.IsBound ? Transaction.Current ?? Chrome.PanelSlide : Transaction.Current;

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
    private readonly SendRunState state;
    public SendButton(ThreadRuntime runtime, bool goesToHead, bool queueGoesToHead, Action send) =>
        Child = state = new SendRunState(runtime, goesToHead, queueGoesToHead, send); // :794

    /// <summary>:784-791 the inputs a re-evaluated body is handed.</summary>
    internal void Update(bool goesToHead, bool queueGoesToHead) => state.Update(goesToHead, queueGoesToHead);
}

/// <summary>:798-813 <c>private struct SendRunState: View</c>.</summary>
internal sealed class SendRunState : Decorator
{
    // :801-804 the view's inputs; :806-812 body
    private sealed class Inputs : MainActorModel
    {
        private (bool GoesToHead, bool QueueGoesToHead) value;
        public (bool GoesToHead, bool QueueGoesToHead) Value { get => Read(value); set => Set(ref this.value, value); }
    }
    private readonly Inputs inputs = new();

    /// <summary>:801-804 the inputs a re-evaluated body is handed: the button is drawn again from them.</summary>
    internal void Update(bool goesToHead, bool queueGoesToHead) => inputs.Value = (goesToHead, queueGoesToHead);

    public SendRunState(ThreadRuntime runtime, bool goesToHead, bool queueGoesToHead, Action send)
    {
        inputs.Value = (goesToHead, queueGoesToHead);
        // PORT-ONLY (OD-0136 amendment 2, step 9): one face for every button this state makes, so the arrow flying out and the stop square popping in (and back)
        // play on the face the person is looking at when running flips, instead of a new face being shown already changed.
        var face = new NthSendFace();
        this.Track(() => (Running: runtime.IsRunning, Heads: runtime.IsRunning && runtime.HasWorkingHeads, ToHead: inputs.Value), input =>
            Child = new SendDraftState(runtime, input.Running, input.Heads, input.ToHead.GoesToHead, input.ToHead.QueueGoesToHead, send, face)); // :807-811
    }
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
        Action send,
        NthSendFace? shared = null)
    {
        // PORT-ONLY (OD-0136 amendment 2, step 9): the 32 px disc (was 28) is the nth send face: it draws the graphite (silver in the dark) gradient disc, its rim and
        // shadow, and the arrow or the stop square (:831-833, :835-836 were the accent disc with the arrow.up / stop.fill symbol). Running is set while the face is still in the
        // button it was last in, so the change animates; then the face is taken out for this button.
        var face = shared ?? new NthSendFace();
        face.IsRunning = isRunning;
        face.IsPressed = false;
        switch (face.Parent) { case Decorator held: held.Child = null; break; case Panel held: held.Children.Remove(face); break; }
        var label = new Border { Width = NthSendFace.Size, Height = NthSendFace.Size, Child = face }; // :835-836 circle
        var button = PlainControls.Button(label, () => { if (isRunning) runtime.Interrupt(); else send(); }); // :828-829,838
        button.Focusable = false; // :839
        // :841 the 4 point leading margin. PORT-ONLY (OD-0136 amendment 2, step 9): the face is 32 tall (was 28). The controls row, and the pill built round it (46), were laid out with a 28 tall
        // button, and the composer's centring (T46) rests on that height; so the button takes 2 points back above and below: it is laid out 28 tall and draws 32, centred on the same line,
        // and nothing else in the composer moves.
        button.Margin = new Thickness(4, -2, 0, -2);
        ToolTip.SetTip(button, HelpText(isRunning, headsWorking, goesToHead, queueGoesToHead)); // :842
        AutomationProperties.SetName(button, isRunning ? "Stop" : goesToHead ? "Send to a head" : "Send"); // :843
        button.PropertyChanged += (_, change) => { if (change.Property == Button.IsPressedProperty) face.IsPressed = button.IsPressed; }; // the face scales to 0.9 while held
        var dimmed = new AnimatedScalar(label, face.Dim, value => face.Dim = value); // the face dims and returns along a transition (OD-0076)
        void Apply(bool empty, bool reverting, Animation? animation)
        {
            var enabled = isRunning || !empty; // :826-827
            button.IsEnabled = enabled && !reverting; // :840
            // A disabled plain button dims its label: VIDEO content.composer-send-states (C 81.640, C 90.5): the empty composer's disc was a faint grey and its
            // arrow about half white, measured on the extracted frames as 0.48 to 0.51 of the white over the disc. The nth face keeps what it has at half: Dim 0.5.
            dimmed.Set(button.IsEnabled ? 1 : DisabledLabelOpacity, animation); // :836
        }
        Apply(runtime.Draft.IsEmpty, runtime.IsReverting, null);
        // The Mac changes the fill in one frame; the owner's OD-0076 has every change transitioned, in the Mac's motion language
        // for a hover (0.12 s ease-out), unless the change is made inside an animation of its own.
        this.Track(() => (Empty: runtime.Draft.IsEmpty, Reverting: runtime.IsReverting),
            input => Apply(input.Empty, input.Reverting, FrameClock.IsBound ? Transaction.Current ?? Animation.EaseOut(duration: 0.12) : null)); // a still frame clock (a view with no display) cuts, as AnimatedColor does
        Child = button;
    }

    /// <summary>PORT-ONLY: how much of itself a disabled send button's label keeps (VIDEO, see <c>Apply</c>).</summary>
    internal const double DisabledLabelOpacity = 0.5;

    /// <summary>:831 the button's symbol: stop.fill while running, else arrow.up. The Settings preview of the composer still draws this symbol (PreviewSendButton).</summary>
    internal static string Symbol(bool isRunning) => isRunning ? "stop.fill" : "arrow.up";

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
    private StackPanel? chipRow; // :881 the HStack the scroller holds
    private ForEach<ChipItem, string>? chips; // :882, :908 the command's chip and ForEach(quotes), by their identities
    private readonly Dictionary<string, ChipItem> drawn = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, ChipSlot> slots = new(SwiftStringComparer.Instance); // each chip's place in the row
    private ChipKey? shownKey;
    private bool evaluated;
    /// <summary>:871-874 <c>private struct ChipKey: Equatable</c>.</summary>
    private sealed record ChipKey
    {
        public required ValueList<Guid> QuoteIDs { get; init; } // :872
        public string? CommandName { get; init; } // :873
    }

    /// <summary>One chip of the row: the command's (identity "command", :882) or a quote's (its id, :908).</summary>
    private sealed record ChipItem(string Id, string Symbol, string Text, string Detail, string RemoveLabel, bool CommandChip);

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
        var key = new ChipKey { QuoteIDs = new ValueList<Guid>(quotes.Select(quote => quote.Id)), CommandName = command?.Name }; // :941
        // :879 nothing to show: no row, so the column's zero spacing leaves no room for it
        if (quotes.Count == 0 && command is null)
        {
            if (current is { } leaving) Transaction.WithAnimation(Chrome.PanelSlide, () => SwiftTransition.Remove(rows, leaving));
            current = null;
            chipRow = null;
            chips = null;
            drawn.Clear();
            slots.Clear();
            shownKey = key;
            return;
        }

        var items = Items(quotes, command);
        if (current is null)
        {
            // The row arrives whole: its chips are there as it grows from nothing, none of them arriving of their own.
            var scroller = Scroller();
            Transaction.WithAnimation(null, () => chips!.Apply(items));
            scroller.Margin = new Thickness(0, 0, 0, 8); // :939 .padding(.bottom, 8)
            scroller.Transition(SwiftTransition.Modifier(new ChipRowCollapse(true), new ChipRowCollapse(false))); // :940
            if (wasEvaluated)
                Transaction.WithAnimation(Chrome.PanelSlide, () => SwiftTransition.Insert(rows, 0, scroller));
            else SwiftTransition.Insert(rows, 0, scroller);
            current = scroller;
        }
        else if (!key.Equals(shownKey))
        {
            // :941 .animation(Chrome.panelSlide, value: ChipKey): a chip arriving or leaving the row that stays moves
            // under the spring; the scroller, and where it is scrolled to, are the same ones throughout.
            Transaction.WithAnimation(Chrome.PanelSlide, () =>
            {
                var staying = items.Select(item => item.Id).ToHashSet(SwiftStringComparer.Instance);
                var leaving = slots.Where(pair => !staying.Contains(pair.Key)).ToArray();
                chips!.Apply(items);
                // A leaving chip fades where it stands (the ForEach's removal) while its place closes, so the chips after it
                // follow it under the same spring instead of jumping when its fade is over (SwiftUI takes it out of the
                // layout at once and moves the others under the transaction).
                foreach (var (id, slot) in leaving)
                {
                    slots.Remove(id);
                    slot.FoldShut(Chrome.PanelSlide);
                }
            });
        }
        else
        {
            // Nothing in the key moved (a text edit re-evaluated the draft; a command's detail): drawn again in place,
            // with no animation.
            Transaction.WithAnimation(null, () => chips!.Apply(items));
        }
        shownKey = key;
    }

    private static List<ChipItem> Items(ValueList<ReplyQuote> quotes, DraftCommand? command)
    {
        var items = new List<ChipItem>();
        if (command is not null) // :882
            items.Add(new ChipItem("command", command.IsBuiltIn ? "command" : "sparkles", "/" + command.Name, command.Detail, "Remove command", true)); // :883-906
        foreach (var quote in quotes) // :908
            items.Add(new ChipItem(quote.Id.ToString(), "text.quote", quote.Excerpt, quote.Text, "Remove quote", false)); // :909-935
        return items;
    }

    // :880-881 the horizontal scroller and its HStack(spacing: 6)
    private ScrollViewer Scroller()
    {
        // HStack(spacing: 6): each chip's place keeps the 6 after it, and the row takes the last one back, so a leaving chip's whole
        // footprint, its gap too, can close (ChipSlot); the chips stand exactly 6 apart.
        chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(0, 0, -ChipSlot.Gap, 0) };
        chips = new ForEach<ChipItem, string>(chipRow, item => item.Id, Make, Update);
        drawn.Clear();
        slots.Clear();
        return new ScrollViewer { Content = chipRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; // :880
    }

    private Control Make(ChipItem item)
    {
        drawn[item.Id] = item;
        var chrome = new ChipChrome().Body(Content(item)); // :904, :933
        Describe(chrome, item);
        return slots[item.Id] = new ChipSlot((Border)chrome);
    }

    private void Update(Control slot, ChipItem item)
    {
        if (drawn.TryGetValue(item.Id, out var old) && old == item) return;
        drawn[item.Id] = item;
        var chrome = ((ChipSlot)slot).Chip;
        chrome.Child = Content(item);
        Describe(chrome, item);
    }

    // :905-906, :934-935 the chip's help and its combined accessibility name
    private static void Describe(Control chrome, ChipItem item)
    {
        ToolTip.SetTip(chrome, item.Detail);
        AutomationProperties.SetName(chrome, item.Text);
    }

    // :883-906, :909-935 Each capsule combines the label and the remove action; full detail is its tooltip.
    private Control Content(ChipItem item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; // :883, :909
        row.Children.Add(SwiftImage.SystemName(item.Symbol)
            .Font(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText)); // :884-886, :910-912
        var title = new TextBlock { Text = item.Text, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }; // :887, :913, :916-917
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: item.CommandChip ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(title); // :888, :914
        title.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(title); // :889, :915
        if (!item.CommandChip) { title.MaxWidth = 260; title.HorizontalAlignment = HorizontalAlignment.Left; } // :918
        row.Children.Add(title);
        var x = SwiftImage.SystemName("xmark").Font(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold)).ForegroundStyle(Chrome.SecondaryText); // :895-897, :924-926
        var button = PlainControls.Button(x, () => Transaction.WithAnimation(Chrome.PanelSlide, () => Remove(item))); // :890-899, :919-928
        button.Focusable = false; // :900, :929
        ToolTip.SetTip(button, item.RemoveLabel); // :901, :930
        AutomationProperties.SetName(button, item.RemoveLabel); // :902, :931
        row.Children.Add(button);
        return row;
    }

    private void Remove(ChipItem item)
    {
        if (item.CommandChip) runtime.Draft = runtime.Draft with { Command = null }; // :891-893
        else runtime.Draft = runtime.Draft with { Quotes = new ValueList<ReplyQuote>(runtime.Draft.Quotes.Where(held => held.Id.ToString() != item.Id)) }; // :920-922
    }
}

/// <summary>
/// PORT-ONLY (ED-282, UI-COMPOSER-007; OD-0076): a chip's place in the chip row. A leaving view in SwiftUI is out of the
/// layout at once and the others move under the transaction; a leaving row of the foundation's ForEach keeps its place until its fade
/// is over, so the chips after it jumped then. The place closes under the same animation as the fade instead, the chip keeping its
/// own width and its left edge where it stood.
/// </summary>
internal sealed class ChipSlot : Decorator
{
    private IFrameSource? source;
    private Action<TimeSpan>? frame;

    /// <summary>:881 the HStack's spacing.</summary>
    public const double Gap = 6;

    public ChipSlot(Border chip)
    {
        Chip = chip;
        Child = chip;
        Margin = new Thickness(0, 0, Gap, 0);
    }

    public Border Chip { get; }

    public void FoldShut(Animation animation)
    {
        if (frame is not null) source!.CancelFrame(frame);
        var natural = Bounds.Width > 0 ? Bounds.Width : Chip.DesiredSize.Width;
        Chip.MinWidth = natural; // the chip keeps its width however little room the place has
        Chip.HorizontalAlignment = HorizontalAlignment.Left;
        Width = natural;
        var start = SwiftClock.Now;
        source = FrameClock.Source.Current;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            var complete = elapsed >= animation.Duration;
            var open = complete ? 0 : Math.Max(0, 1 - animation.Progress(elapsed));
            Width = natural * open;
            Margin = new Thickness(0, 0, Gap * open, 0);
            if (complete) frame = null;
            else source!.RequestFrame(frame!);
        };
        source.RequestFrame(frame);
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

    // :990-1032 private func strip(_:), with :975-978 its preview and drawn-id state (PR-COMPOSER-067/068).
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
        remove.Template = ComposerButtonTemplates.PlainWithPadding; // :1011 the image's padding participates in layout
        remove.Focusable = false; // :1014
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.RenderTransform = new TranslateTransform(6,-6); // :1015
        AutomationProperties.SetName(remove, "Remove " + attachment.Name); // :1016
        overlay.Children.Add(remove);
        return new Border { Child = overlay, Padding = new Thickness(0,2,8,0) }; // :1018-1019
    }
}

// PORT-ONLY: the unit's .plain buttons whose Swift labels include padding. The shared plain template does not
// forward Button.Padding; forwarding it here preserves the source hit rectangles without changing another unit.
internal static class ComposerButtonTemplates
{
    public static readonly FuncControlTemplate<Button> PlainWithPadding = new((button, scope) =>
        new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = button[!ContentControl.ContentProperty],
            [!ContentPresenter.PaddingProperty] = button[!Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty],
        }.RegisterInNameScope(scope));
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
        // :1298 let shape = RoundedRectangle(cornerRadius: 22, style: .continuous). The composer pill is the one shape of OD-0102's corner band that a
        // reference frame measures: the video fits the circular arc on the one-line pill (r 22 on 46 pt, 0.156 px rms; the clamped continuous corner 1.148,
        // ED-275 (2)), so it stays the circular arc there (CircularInBand) while a taller composer, where Apple's corner fits, takes it.
        var shape = new Border { Child = content }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand();

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
                // :1304 content.glassEffect(.regular, in: shape), sampling app-owned content (OD-0005).
                shape.Background = null;
                shape.ClipToBounds = false;
                SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
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
