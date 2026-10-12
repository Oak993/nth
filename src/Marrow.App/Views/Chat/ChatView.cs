using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Sidebar;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Usage;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792), :1-1731.
// The column, its measured pane and compositor state stay mounted; child inputs are applied only when changed.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; CGPoint/CGSize are Avalonia Point/Size; `URL` is SwiftURL; a Swift `Error` value is
// an Exception. The `@Entry` environment values of `extension EnvironmentValues` (:727-735) are inherited attached
// properties on the class named after the Swift file (R-NAME-4, R-VIEW-6), which is the file's own view ChatView.
// Swift `private` top-level types are `internal` here: C# has no private top-level type, and `file` cannot appear in
// another type's member signatures (CS9051). A static member named like a nested type takes the owner's name as a
// prefix (R-NAME-3): PanelScene.geometry(...) is PanelSceneGeometry beside the nested type Geometry.

/// <summary>:4-711 <c>struct ChatView: View</c>, with the file's <c>extension EnvironmentValues</c> (:727-735).</summary>
public sealed class ChatView : Decorator
{
    private const double HydraMinimumPresence = 1.6; // :74

    private readonly ThreadRuntime runtime; // :7
    private AppModel? model; // :5 inherited on attach.
    private readonly GitStatusModel git = new(); // :9
    private ChromeScrollModel scrollChrome = new(); // :10
    private TimelineScrollState scrollState = new(); // :11
    private double columnHeight; // :14
    private Size paneSize; // :17
    private double composerAreaHeight; // :19
    private PanelDragState subagentDrag = new(), hydraDrag = new(), poppedDrag = new(), usageDrag = new(), autoDrag = new(); // :22-28
    private Dictionary<Guid, PanelDragState> autoDrags = []; // :30
    private double? usageContentHeight; // :33
    private PanelResizeState panelResize = new(); // :35
    private DateTimeOffset? hydraShownSince; // :66
    private bool hydraLingers; // :67
    private TaskHandle? hydraLingerTask; // :68
    private Guid? sceneThreadID; // :72
    private HydraPresence? previousPresence;
    private Guid[] previousAutoIDs = [];
    private Tracked<bool>? dragObservation;
    private readonly Grid column = new() { RowDefinitions = new RowDefinitions("*,Auto") };
    private readonly Grid pane = new();
    private readonly Border timelineHost = new();
    private readonly Grid composerHost = new();
    private readonly Panel floatingHost = new();
    private readonly Panel terminalHost = new();
    private readonly Dictionary<FloatingPanelID, (Control View, object Inputs)> mountedPanels = [];
    private Dictionary<string, object?>? panelKeys; // the values the overlays' animations were last keyed on.
    private readonly Dictionary<Control, (Panel Host, Control Surface)> panelSurfaces = [];
    private ThreadTimeline? timeline;
    private ComposerArea? composer;
    private ChatChromeRow? chrome;
    private TerminalPanel? terminal;
    private PanelMembers? members;
    private PanelScene scene = null!;
    private string? workingDirectory;
    private string directory = "";
    private Project? project;
    private ReserveSlide timelineSlide = new(new PanelReserve(), false);
    private ReserveSlide composerSlide = new(new PanelReserve(), false, AnimatesWidth: true);
    private (string? Project, string? Directory, bool Rewind, double Height, bool Minimap)? timelineInputs;
    private string? composerDirectory;
    private bool? terminalWasVisible;
    private string? terminalDirectory;
    private (TerminalPanel Panel, string? Directory)? leavingTerminal; // the drawer last taken out, while its removal may still be running.
    private readonly ChatFrameAnimation terminalPush;
    private (bool Showing, double Height)? push; // the terminal push under way: the pane's settled size is read through it.
    private long? seenDiffRevision;

    // :7 @Bindable var runtime: ThreadRuntime; :76-321 body
    public ChatView(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        var conversation = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(composerHost, 1);
        conversation.Children.Add(timelineHost);
        conversation.Children.Add(composerHost); // :124-140 bottom safe-area inset consumes viewport height.
        pane.Children.Add(conversation);
        column.Children.Add(pane);
        Grid.SetRow(terminalHost, 1);
        column.Children.Add(terminalHost); // :98,275-280 zero gap, terminal outside the conversation pane.
        Child = column.DetailSheet(); // :281-282
        this.Transition(SwiftTransition.Identity); // :283-288, no column transition on a switch.
        terminalPush = new ChatFrameAnimation(pane);
        terminalPush.AttachLifetime();
        // A drawer that has finished leaving gives its row back; the pane no longer reaches under it.
        terminalHost.Children.CollectionChanged += (_, _) =>
        {
            if (terminalHost.Children.Count != 0) return;
            terminalPush.Stop();
            push = null;
            pane.Margin = default;
        };

        this.Track(() => AppEnvironment.GetAppModel(this), attachedModel =>
        {
            model = attachedModel;
            RefreshInputs();
        });
        this.Track(() => new PanelMembers(this.runtime, AppEnvironment.GetAppModel(this)), value =>
        {
            members = value; // :85-90 membership observation separated from per-frame geometry.
            RefreshInputs();
        });
        this.Track(() =>
        {
            var app = AppEnvironment.GetAppModel(this);
            var thread = app.Thread(this.runtime.ThreadID);
            return (thread, thread is null ? null : app.Project(thread.ProjectID));
        }, _ => RefreshInputs()); // :77-84
        this.Track(() =>
        {
            var app = AppEnvironment.GetAppModel(this);
            var resize = AppEnvironment.GetWindowLiveResize(this);
            return (app.Settings.PanelSize, app.Settings.UsagePanelHeight, app.Settings.UsagePanelDock,
                app.Settings.HydraShowsHeadDetails, app.Settings.HydraAutoPopsHeads,
                this.runtime.SubagentPanelDock, this.runtime.HydraPanelDock, this.runtime.HydraPoppedPanelDock,
                this.runtime.HydraAutoPanelDocks, this.runtime.HydraAutoPopHeld, this.runtime.PanelStackOrder,
                panelResize.IsActive, panelResize.LiveSize, panelResize.LiveUsageHeight,
                app.Sidebar.IsDragging, app.Sidebar.IsSliding, this.runtime.IsTerminalResizing, resize.IsActive);
        }, _ => RefreshScene()); // :59-64,89-95
        this.Track(() =>
        {
            var app = AppEnvironment.GetAppModel(this);
            return app.Sidebar.IsDragging || app.Sidebar.IsSliding || this.runtime.IsTerminalResizing;
        }, value => AppEnvironment.GetWindowLiveResize(this).IsPaneResizing = value); // :252-254 initial:true.
        bool? wasResizing = null;
        this.Track(() => AppEnvironment.GetWindowLiveResize(this).IsActive, active =>
        {
            if (wasResizing == true && !active) ReclampPanels(); // :257-264
            wasResizing = active;
        });
        this.Track(() => this.runtime.IsTerminalVisible, _ => RefreshTerminal()); // :275-278,290
        this.Track(() => this.runtime.IsHydraPanelHidden, hidden =>
        {
            if (!hidden || !hydraLingers) return;
            hydraLingerTask?.Cancel();
            Transaction.WithAnimation(Chrome.PanelSlide, () => { hydraLingers = false; RefreshScene(); });
        }); // :223-227 a tap cancels a held panel's remaining beat.
        this.Task(() => ResolveDirectory(), value => git.Refresh(value, force: false)); // :295-297
        this.Track(() => this.runtime.DiffRevision, revision =>
        {
            if (seenDiffRevision is not null && seenDiffRevision != revision)
                MainActor.Spawn(() => git.Refresh(ResolveDirectory())); // :298-300
            seenDiffRevision = revision;
        });
        // ThreadRuntime.threadID is immutable in both ports. This observation retains the source switch reset
        // for a bound input replacement; the owning RootView coordinator caches one column per thread id.
        this.Track(() => this.runtime.ThreadID, id =>
        {
            if (sceneThreadID is { } previous && previous != id) ResetThreadScene(); // :306-319
            sceneThreadID = id;
        });
        pane.SizeChanged += (_, _) => NotePaneSize(SettledPaneSize()); // :241-249
        composerHost.SizeChanged += (_, _) => NoteComposerAreaHeight(composerHost.Bounds.Height); // :129-138
        SizeChanged += (_, _) =>
        {
            if (columnHeight == Bounds.Height) return;
            columnHeight = Bounds.Height; RefreshScene(); // :289 full column, independent of composer/terminal height.
        };
        AttachedToVisualTree += (_, _) =>
        {
            sceneThreadID = this.runtime.ThreadID; // :320
            ObserveDrags();
        };
        DetachedFromVisualTree += (_, _) => { dragObservation?.Dispose(); dragObservation = null; };
    }

    /// <summary>
    /// PORT-ONLY (ED-NEW-session-hotkeys): the parts a chat switch moves. What differs from chat to chat crosses: <c>Content</c>
    /// (the transcript, the panels, the terminal drawer; <c>Rise</c>, the transcript, settles up a few points as it comes in),
    /// the composer's <c>Draft</c> when the two drafts differ, and the title's words (<see cref="PrepareTitleCross"/>). What is
    /// glass is drawn once, never two over each other: the chat opened draws its <c>Chrome</c> (the header's capsules) and its
    /// <c>Composer</c> from the first frame and the chat left takes its own away at once. The <c>Backdrop</c> (the sheet's wash
    /// and tint) is drawn by the lowest column only and the <c>Veils</c> (the top veil) by the top one, so neither doubles.
    /// </summary>
    internal ChatSwitchParts SwitchParts => new(
        [timelineHost, floatingHost, terminalHost],
        timelineHost,
        chrome,
        composerHost,
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(composerHost).OfType<ComposerTextColumn>().FirstOrDefault(),
        Child is Panel sheet ? sheet.Children.OfType<Avalonia.Controls.Shapes.Shape>().ToArray() : [],
        pane.Children.OfType<PaneTopVeil>().ToArray());

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): the thread's title as the header shows it ("" for a new thread).</summary>
    internal string Title => model?.Thread(runtime.ThreadID)?.Title ?? "";

    /// <summary>
    /// PORT-ONLY (ED-NEW-session-hotkeys): what the composer's draft shows (its text and its chips' words), compared between the
    /// chat left and the chat opened: the draft crosses only when the two differ.
    /// </summary>
    internal string DraftWords => SwitchParts.Draft is { } draft ? ChatSwitchParts.WordsOf(draft) : "";

    /// <summary>
    /// PORT-ONLY (ED-NEW-session-hotkeys): the title lays the words of the chat left over its own, before the column is laid out
    /// (so they keep their own width), for the switch to cross: <see cref="TitleCross"/>.
    /// </summary>
    internal void PrepareTitleCross(string previousTitle) => chrome?.PrepareTitleCross(previousTitle);

    /// <summary>
    /// PORT-ONLY (ED-NEW-session-hotkeys): while a switch crosses the title, the words left (fading out) and this chat's words
    /// (fading in); render-only, so no frame lays the header out. Null when there is nothing to cross.
    /// </summary>
    internal (Control Leaving, Control Arriving)? TitleCross => chrome?.TitleCross;

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): the cross is over: this chat's words alone, whole.</summary>
    internal void EndTitleCross() => chrome?.EndTitleCross();

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): the column's composer text box, the one a chat switch puts the keyboard in.</summary>
    internal ComposerNSTextView? ComposerBox =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(composerHost).OfType<ComposerNSTextView>().FirstOrDefault();

    // :80-84 thread/project values supplied to the children once per input change.
    private string ResolveDirectory()
    {
        var app = AppEnvironment.GetAppModel(this);
        var thread = app.Thread(runtime.ThreadID);
        return thread?.WorktreePath ?? (thread is null ? null : app.Project(thread.ProjectID)?.Path) ?? LoginEnvironment.HomeDirectory;
    }

    private void RefreshInputs() // :77-95,146-153
    {
        if (model is null) return;
        var thread = model.Thread(runtime.ThreadID);
        project = thread is null ? null : model.Project(thread.ProjectID);
        workingDirectory = thread is null ? null : thread.WorktreePath ?? project?.Path;
        directory = workingDirectory ?? LoginEnvironment.HomeDirectory;
        members ??= new PanelMembers(runtime, model);
        if (composer is null || composerDirectory != workingDirectory)
        {
            composerDirectory = workingDirectory;
            composerHost.Children.Clear();
            composer = new ComposerArea(runtime, workingDirectory);
            composerHost.Children.Add(composer);
            composerHost.Children.Add(new JumpToLatestButton(scrollState) { VerticalAlignment = VerticalAlignment.Top }); // :125-128
            composerSlide = new(new PanelReserve(), false, AnimatesWidth: true);
        }
        if (chrome is null)
        {
            pane.Children.Add(new PaneTopVeil(scrollChrome) { VerticalAlignment = VerticalAlignment.Top }); // :143-145
            chrome = new ChatChromeRow(model, runtime, scrollChrome, thread?.Title ?? "", project, directory, git) { VerticalAlignment = VerticalAlignment.Top };
            pane.Children.Add(chrome);
            pane.Children.Add(floatingHost); // :155-240 overlays above the chrome/conversation.
        }
        else chrome.UpdateInputs(thread?.Title ?? "", project, directory);
        LinkParagraphView.SetMergeRequestTarget(this, new MergeRequestTarget(runtime.ThreadID,
            link => model.SpawnSubagent(runtime.ThreadID, link.Title, link.Prompt))); // :292-294
        RefreshScene();
        RefreshTerminal();
    }

    private void RefreshScene() // :85-95,155-240
    {
        if (model is null || members is null || composer is null) return;
        scene = new PanelScene(members, runtime, model, paneSize, composerAreaHeight, usageContentHeight,
            hydraLingers, LivePreferredSize(panelResize), panelResize.LiveUsageHeight);
        NoteHydraPresence(new HydraPresence { ThreadID = runtime.ThreadID, Present = members.Heads.Count != 0 && scene.IsMeasured });
        var holds = panelResize.IsActive || model.Sidebar.IsDragging || model.Sidebar.IsSliding || runtime.IsTerminalResizing;
        var switching = sceneThreadID != runtime.ThreadID;
        var slides = scene.IsMeasured && !AppEnvironment.GetWindowLiveResize(this).IsActive && !holds && !switching;
        var thread = model.Thread(runtime.ThreadID);
        var minimap = !scene.DockedSides.Contains(PanelDockSide.Leading) &&
            (paneSize == default || paneSize.Width - scene.Reserve.Leading - scene.Reserve.Trailing >= 900); // :108-110
        var inputs = (project?.Name, workingDirectory, thread?.Provider.SupportsRewind ?? false, columnHeight, minimap);
        if (timeline is null)
        {
            timelineInputs = inputs;
            timeline = new ThreadTimeline(runtime, scrollChrome, scrollState, inputs.Item1, inputs.Item2, inputs.Item3, inputs.Item4, inputs.Item5);
            timelineHost.Child = timeline; // :116-119 .id(threadID); children receive the unchanged snapshot inputs.
        }
        else if (timelineInputs != inputs)
        {
            timelineInputs = inputs; // :97-123 the timeline's identity is the thread's: a changed layout input updates it in place.
            timeline.UpdateInputs(inputs.Item1, inputs.Item2, inputs.Item3, inputs.Item4, inputs.Item5);
        }
        timeline.IsHitTestVisible = !IsMovingAPanel(subagentDrag, hydraDrag, poppedDrag, usageDrag, autoDrags); // :112-115
        timelineSlide = timelineSlide with { Reserve = scene.Reserve, Slides = slides };
        timelineSlide.Body(timeline); // :122-123
        composerSlide = composerSlide with { Reserve = scene.Reserve, Slides = slides };
        composerSlide.Body(composer); // :140
        var ids = scene.AutoPopped.Select(head => head.Id).ToArray();
        if (!previousAutoIDs.SequenceEqual(ids)) { previousAutoIDs = ids; NoteAutoPanelIDs(ids); } // :266-269
        DrawPanels(holds, switching);
    }

    private void ObserveDrags() // :48-52,112-115 only the timeline loses hit testing.
    {
        dragObservation?.Dispose();
        if (VisualRoot is null) return;
        dragObservation = new Tracked<bool>(() => IsMovingAPanel(subagentDrag, hydraDrag, poppedDrag, usageDrag, autoDrags), moving =>
        {
            if (timeline is not null) timeline.IsHitTestVisible = !moving;
        });
    }

    private void NoteAutoPanelIDs(Guid[] ids) // :266-269 initial:true, one state per visible automatic head.
    {
        foreach (var id in ids) if (!autoDrags.ContainsKey(id)) autoDrags[id] = new PanelDragState();
        foreach (var id in autoDrags.Keys.Where(id => !ids.Contains(id)).ToArray()) autoDrags.Remove(id);
        ObserveDrags();
    }

    private void NoteComposerAreaHeight(double height) // :129-138
    {
        var old = composerAreaHeight;
        if (old == height) return;
        composerAreaHeight = height;
        if (height > old + 0.5 && old > 0 && !AppEnvironment.GetWindowLiveResize(this).IsActive)
            scrollState.NoteComposerAreaGrowth();
        RefreshScene();
    }

    // :241-249 onGeometryChange reads the size SwiftUI's layout settled at, and a layout animation (the terminal's push, :290) only
    // interpolates what is drawn: paneSize takes its final value once. The port's push animates the pane's own bottom margin
    // (PushTerminal), so the measured size passes through every value between (and layout rounding makes each a fraction off the
    // last); the scene reads the size the push is heading for, the column less the drawer's row when it opens.
    private Size SettledPaneSize() => push is { } heading
        ? new Size(pane.Bounds.Width, Math.Max(0, columnHeight - (heading.Showing ? heading.Height : 0)))
        : pane.Bounds.Size;

    private void NotePaneSize(Size size) // :241-249
    {
        if (paneSize == size) return;
        paneSize = size;
        RefreshScene();
        if (AppEnvironment.GetWindowLiveResize(this).IsActive) ReclampPanels();
    }

    private void ReclampPanels() // :248-249,260-264
    {
        if (scene is null) return;
        subagentDrag.Reclamp(scene.Layout); hydraDrag.Reclamp(scene.Layout); poppedDrag.Reclamp(scene.Layout); usageDrag.Reclamp(scene.Layout);
        foreach (var drag in autoDrags.Values) drag.Reclamp(scene.Layout);
    }

    private void NoteHydraPresence(HydraPresence presence) // :187-225
    {
        if (previousPresence == presence) return;
        var old = previousPresence;
        previousPresence = presence;
        if (old is null) return; // onChange has no initial:true at this site.
        hydraLingerTask?.Cancel();
        if (old.ThreadID != presence.ThreadID || presence.Present)
        {
            hydraLingers = false;
            hydraShownSince = presence.Present ? SwiftDate.Now : null;
            return;
        }
        if (runtime.IsHydraPanelHidden) { hydraLingers = false; return; }
        var shown = hydraShownSince is { } since ? SwiftDate.TimeIntervalSince(SwiftDate.Now, since) : double.PositiveInfinity;
        var remaining = HydraMinimumPresence - shown;
        if (remaining <= 0) return;
        hydraLingers = true;
        hydraLingerTask = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(SwiftTask.Seconds(remaining));
            if (SwiftTask.IsCancelled) return;
            Transaction.WithAnimation(Chrome.PanelSlide, () => { hydraLingers = false; RefreshScene(); });
        });
        // This pass was calculated before the presence callback; re-evaluate the held empty panel on the next actor turn.
        MainActor.Post(RefreshScene);
    }

    private void ResetThreadScene() // :306-319
    {
        hydraLingerTask?.Cancel(); hydraLingers = false;
        scrollChrome = new(); scrollState = new();
        subagentDrag = new(); hydraDrag = new(); poppedDrag = new(); usageDrag = new(); autoDrag = new(); autoDrags = [];
        panelResize = new(); previousAutoIDs = []; previousPresence = null;
        ObserveDrags();
    }

    private void RefreshTerminal() // :275-278,290
    {
        if (model is null) return;
        var visible = runtime.IsTerminalVisible;
        var changed = terminalWasVisible is { } was && was != visible;
        void Apply()
        {
            var animation = Transaction.Current;
            if (terminal is not null && (!visible || terminalDirectory != directory))
            {
                var leaving = terminal;
                SwiftTransition.RemoveContinuing(terminalHost, leaving); // closed mid-open: back down from where it is (OD-0076; ED-278 (10)).
                leavingTerminal = (leaving, terminalDirectory);
                terminal = null;
                if (!visible && animation is not null) PushTerminal(showing: false, animation, leaving.Bounds.Height, rowHeld: true);
            }
            if (visible && terminal is null)
            {
                terminalDirectory = directory;
                var rowHeld = terminalHost.Children.Count != 0; // a drawer still leaving holds its row (and the pane's reach under it).
                // Reopened while it is still closing: the leaving drawer comes back from where it is, the same TerminalPanel (OD-0076;
                // ED-278 (10)); a new one would start a full drawer below under the chat box that is already rising.
                if (leavingTerminal is { } back && back.Directory == directory && SwiftTransition.IsRemoving(back.Panel))
                {
                    terminal = back.Panel;
                    SwiftTransition.InsertContinuing(terminalHost, terminalHost.Children.Count, terminal);
                    terminal.TakeKeyboardIfWanted(); // never left the visual tree: the reopen's focus request is spent here (TerminalPanel.swift:224-239).
                }
                else
                {
                    terminal = new TerminalPanel(runtime, directory).Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
                    SwiftTransition.Insert(terminalHost, terminalHost.Children.Count, terminal);
                }
                leavingTerminal = null;
                if (changed && animation is not null)
                    PushTerminal(showing: true, animation, double.IsNaN(terminal.Height) ? model!.Settings.TerminalHeight : terminal.Height, rowHeld);
            }
        }
        if (changed && sceneThreadID == runtime.ThreadID) Transaction.WithAnimation(Chrome.PanelSlide, Apply);
        else Apply();
        terminalWasVisible = visible;
    }

    // :96,276-279,290 the drawer joins the column's VStack under `.animation(_, value: isTerminalVisible)`: the layout change
    // is animated, so the thread view above it (timeline, chat box, panels) is pushed up and let back down in lock-step with
    // the drawer's move from the bottom edge, never cut to its new height (2-chat-use.mov C 132.2-132.55, the push measured
    // frame by frame; OD-0076). The drawer's row takes its height at once, so the pane reaches under it by the part of the
    // drawer still below its resting place: its bottom edge is the drawer's top on every frame. A push that starts while another is
    // under way (the drawer reopened while it is still closing, or closed while it is still opening) goes on from where the pane is:
    // the margin it has now, never a full drawer below (SwiftUI retargets the animated layout from its presentation; OD-0076). A
    // fresh opening starts a full drawer below, as the drawer's row takes its height at once; a drawer that still holds its row (the
    // one leaving) has already given the pane its reach, and the new push continues from the margin that reach has now.
    private void PushTerminal(bool showing, Animation animation, double height, bool rowHeld)
    {
        terminalPush.Stop();
        var from = showing && !rowHeld ? -height : pane.Margin.Bottom;
        var to = showing ? 0 : -height;
        push = (showing, height);
        terminalPush.Start(animation, progress =>
        {
            pane.Margin = new Thickness(0, 0, 0, from + (to - from) * progress);
            if (progress >= 1 && showing) push = null; // an open drawer has its row: the pane's own size is the settled one again.
        });
        // :241-249,290 paneSize takes the layout's settled value once, and the panels re-place under the same panelSlide as the thread
        // view's push (the bottom-docked ones rise with the chat box): the scene is told the size the push heads for now, not
        // the per-frame sizes the margin makes the pane pass through (those would each start a fresh settle from where the panel is).
        if (columnHeight > 0) NotePaneSize(SettledPaneSize());
    }

    private static SwiftTransition PanelTransition => SwiftTransition.Asymmetric(
        SwiftTransition.Scale(0.92).Combined(SwiftTransition.Opacity),
        SwiftTransition.Scale(0.96).Combined(SwiftTransition.Opacity)); // :601-607

    // :354-356,395-397,451-453,483-485,528-530. Transition the surface inside its positioned wrapper.
    private Control PlacePanel(PanelDragState drag, Point rest, Size size, Control surface, PanelResize resize, bool holds)
    {
        var host = new Panel();
        var placed = new PlacedPanel(drag, rest, size, host, resize, holds);
        panelSurfaces[placed] = (host, surface);
        placed.PresentSurface = presented => SizeSurface(placed, presented);
        host.Children.CollectionChanged += (_, _) =>
        {
            if (host.Children.Count != 0) return;
            floatingHost.Children.Remove(placed);
            panelSurfaces.Remove(placed);
        };
        return placed;
    }

    // :541-545 the frame of a placed panel includes its surface. The placement presents each size of a settle and the surface
    // follows, so a panel that grows or shrinks (the usage panel's reset to its content height, :574-577; the pane changing size
    // under it) glides under panelSlide rather than taking its final size ahead of the placement.
    private void SizeSurface(Control placed, Size size)
    {
        if (!panelSurfaces.TryGetValue(placed, out var panel)) return;
        panel.Surface.Width = size.Width;
        panel.Surface.Height = size.Height;
        // SubagentPanel.swift:299,340-354 frames both borders explicitly; Hydra/Usage frames its root.
        // :345-355 the scrim is the clipped glass's sibling underneath it (PR-HYDRA-229).
        if (panel.Surface is SubagentPanel { Child: Grid { Children: [SwiftShadow.Caster, Border scrim, Border shape] } }) // the scrim's shadow first (ED-358)
        {
            scrim.Width = shape.Width = size.Width;
            scrim.Height = shape.Height = size.Height;
        }
    }

    private void RemovePanel(Control placed)
    {
        if (panelSurfaces.TryGetValue(placed, out var panel)) SwiftTransition.Remove(panel.Host, panel.Surface);
    }

    private void DrawPanels(bool holds, bool switching) // :155-240,326-530
    {
        if (model is null) return;
        var wanted = new HashSet<FloatingPanelID>();
        // :163,180,224,232,240 each overlay animates on its own value only (`.animation(_, value:)`): the helper's id, the
        // team panel's shown flag, the popped head's id, the automatic heads' ids, the usage providers. A panel that only
        // waited for the pane's first measurement (the column's first frames, or a thread's) appears in place, as SwiftUI
        // shows it when no keyed value changed; one whose value changed grows in and fades out on panelSlide.
        var keys = new Dictionary<string, object?>
        {
            ["subagent"] = scene.Subagent?.Id,
            ["hydra"] = scene.ShowsHydra,
            ["popped"] = scene.Popped?.Id,
            ["auto"] = string.Join(",", scene.AutoPopped.Select(head => head.Id)),
            ["usage"] = scene.Usage,
        };
        var previousKeys = panelKeys;
        panelKeys = keys;
        bool Animates(FloatingPanelID id)
        {
            var key = id switch
            {
                FloatingPanelID.Subagent => "subagent",
                FloatingPanelID.Hydra => "hydra",
                FloatingPanelID.Popped => "popped",
                FloatingPanelID.Auto => "auto",
                _ => "usage",
            };
            return !switching && (previousKeys is null || !Equals(previousKeys[key], keys[key]));
        }
        void Mount(FloatingPanelID id, object inputs, Func<Control> create)
        {
            wanted.Add(id);
            if (mountedPanels.TryGetValue(id, out var prior) && Equals(prior.Inputs, inputs))
            {
                UpdatePanelPlacement(id, (PlacedPanel)prior.View, holds);
                return;
            }
            var surface = create();
            if (prior.View is not null)
            {
                // Membership owns placement/grip lifetime even when the conversation's inputs change.
                var existing = panelSurfaces[prior.View];
                Transaction.WithAnimation(null, () =>
                {
                    SwiftTransition.Insert(existing.Host, 0, surface);
                    SwiftTransition.Remove(existing.Host, existing.Surface);
                });
                panelSurfaces[prior.View] = (existing.Host, surface);
                mountedPanels[id] = (prior.View, inputs);
                ((PlacedPanel)prior.View).PresentSurfaceNow(); // the new surface joins the frame the placement presents now.
                UpdatePanelPlacement(id, (PlacedPanel)prior.View, holds);
                return;
            }
            var control = PlacePanel(CurrentDrag(id), CurrentOrigin(id), CurrentSize(id), surface, MountedResizer(id), holds);
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.VerticalAlignment = VerticalAlignment.Top;
            mountedPanels[id] = (control, inputs);
            floatingHost.Children.Add(control);
            var panel = panelSurfaces[control];
            Transaction.WithAnimation(Animates(id) ? Chrome.PanelSlide : null,
                () => SwiftTransition.Insert(panel.Host, 0, panel.Surface));
        }
        if (scene.Subagent is { } helper && scene.IsMeasured)
            Mount(new FloatingPanelID.Subagent(), (helper, project), () => MakeSubagentPanel(helper, holds));
        if (scene.ShowsHydra)
            Mount(new FloatingPanelID.Hydra(), (scene.Heads, workingDirectory, project), () => MakeHydraPanel(scene.Heads, new FloatingPanelID.Hydra(), hydraDrag, runtime.HydraPanelDock, holds, popped: false));
        if (scene.Popped is { } head && scene.ShowsPopped)
            Mount(new FloatingPanelID.Popped(), (head, workingDirectory, project), () => MakeHydraPanel([head], new FloatingPanelID.Popped(), poppedDrag, runtime.HydraPoppedPanelDock, holds, popped: true));
        foreach (var autoHead in scene.AutoPopped)
            Mount(new FloatingPanelID.Auto(autoHead.Id), (autoHead, workingDirectory, project), () => MakeHydraPanel([autoHead], new FloatingPanelID.Auto(autoHead.Id), autoDrags.GetValueOrDefault(autoHead.Id) ?? autoDrag, runtime.HydraPanelDock.AcrossTheColumn, holds, popped: true));
        if (scene.Usage is { } usage && scene.ShowsUsage)
            Mount(new FloatingPanelID.Usage(), usage, () => MakeUsagePanel(usage, holds));
        foreach (var id in mountedPanels.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            var leaving = mountedPanels[id].View; mountedPanels.Remove(id);
            Transaction.WithAnimation(Animates(id) ? Chrome.PanelSlide : null, () => RemovePanel(leaving));
        }
    }

    private PanelScene.PanelSlot Slot(FloatingPanelID id, PanelDockCorner fallback) =>
        scene.Slots.GetValueOrDefault(id) ?? new PanelScene.PanelSlot { Corner = fallback, Below = 0, Lift = 0 }; // :327,360,421,457,491

    // :327-530,533-590 / R-VIEW-1. Placement changes never detach the conversation or its captured grip.
    private PanelScene.PanelSlot CurrentSlot(FloatingPanelID id) => Slot(id, id switch
    {
        FloatingPanelID.Subagent => runtime.SubagentPanelDock,
        FloatingPanelID.Hydra => runtime.HydraPanelDock,
        FloatingPanelID.Popped => runtime.HydraPoppedPanelDock,
        FloatingPanelID.Usage => model!.Settings.UsagePanelDock,
        FloatingPanelID.Auto auto => runtime.HydraAutoPanelDocks.GetValueOrDefault(auto.Value, runtime.HydraPanelDock.AcrossTheColumn),
        _ => runtime.HydraPanelDock,
    });

    private Point CurrentOrigin(FloatingPanelID id) => id is FloatingPanelID.Usage
        ? UsageOrigin(CurrentSlot(id), scene) : Rest(CurrentSlot(id), scene);

    private PanelDragState CurrentDrag(FloatingPanelID id) => id switch
    {
        FloatingPanelID.Subagent => subagentDrag,
        FloatingPanelID.Hydra => hydraDrag,
        FloatingPanelID.Popped => poppedDrag,
        FloatingPanelID.Usage => usageDrag,
        FloatingPanelID.Auto auto => autoDrags.GetValueOrDefault(auto.Value) ?? autoDrag,
        _ => autoDrag,
    };

    private Size CurrentSize(FloatingPanelID id) => id is FloatingPanelID.Usage
        ? new Size(scene.Layout.PanelSize.Width, scene.UsageHeight ?? scene.Layout.PanelSize.Height) : scene.Layout.PanelSize;

    private PanelResize CurrentResizer(FloatingPanelID id) => id is FloatingPanelID.Usage
        ? UsageResizer(model!, panelResize, CurrentSlot(id).Corner, scene, CurrentSize(id), PanelScene.UsageMinimumHeight)
        : Resizer(model!, panelResize, CurrentSlot(id).Corner, scene);

    private PanelResize MountedResizer(FloatingPanelID id) => new()
    {
        Corner = CurrentSlot(id).Corner,
        OnResize = (translation, axes) => CurrentResizer(id).OnResize(translation, axes),
        OnResizeEnd = () => CurrentResizer(id).OnResizeEnd(),
        OnReset = () => CurrentResizer(id).OnReset(),
    };

    private void UpdatePanelPlacement(FloatingPanelID id, PlacedPanel placed, bool holds)
    {
        // The host's and the surface's frames come from the placement's own presentation (PanelPlacementView.Present), at once for a
        // placement that does not settle and on every frame of one that does.
        var size = CurrentSize(id);
        placed.UpdatePlacement(CurrentOrigin(id), size, holds);
        // SubagentPanel.swift:599-615. Moving to a different dock updates the existing grips' free edges.
        var dock = CurrentSlot(id).Corner;
        foreach (var grips in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(placed).OfType<PanelResizeGrips>())
            foreach (var handle in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(grips).OfType<PanelDragHandle>())
            {
                var width = !double.IsNaN(handle.Width);
                var height = !double.IsNaN(handle.Height);
                var horizontal = width ? (dock.Side == PanelDockSide.Leading ? HorizontalAlignment.Right : HorizontalAlignment.Left) : HorizontalAlignment.Stretch;
                var vertical = height ? (dock.IsTop ? VerticalAlignment.Bottom : VerticalAlignment.Top) : VerticalAlignment.Stretch;
                var changed = handle.HorizontalAlignment != horizontal || handle.VerticalAlignment != vertical;
                handle.HorizontalAlignment = horizontal;
                handle.VerticalAlignment = vertical;
                if (changed && width && height && handle.Child is DragHandleView grip)
                {
                    var cursor = new Cursor(dock switch
                    {
                        PanelDockCorner.TopLeading => StandardCursorType.BottomRightCorner,
                        PanelDockCorner.TopTrailing => StandardCursorType.BottomLeftCorner,
                        PanelDockCorner.BottomLeading => StandardCursorType.TopRightCorner,
                        _ => StandardCursorType.TopLeftCorner,
                    });
                    grip.Cursor = grip.HandleCursor = grip.DragCursor = cursor;
                }
            }
    }

    private void DragPanel(FloatingPanelID id, PanelDragState drag, Size translation, Point rest, bool release = false) // :336-346,368-378,434-444,469-479,510-520
    {
        var position = release ? drag.Release() : drag.Move(translation, rest, scene.Layout);
        if (position is not { } point || model is null) return;
        if (id is FloatingPanelID.Auto auto) DockAuto(runtime, auto.Value, point, scene);
        else if (id is FloatingPanelID.Usage) DockUsage(model, point, scene);
        else
        {
            var current = id is FloatingPanelID.Subagent ? runtime.SubagentPanelDock : id is FloatingPanelID.Hydra ? runtime.HydraPanelDock : runtime.HydraPoppedPanelDock;
            Dock(id, new Binding<PanelDockCorner>(() => current, next => { current = next; SetCorner(model, runtime, id, next); }), point, scene);
        }
        var corner = Slot(id, runtime.HydraPanelDock.AcrossTheColumn).Corner;
        if (id is FloatingPanelID.Auto a) corner = runtime.HydraAutoPanelDocks.GetValueOrDefault(a.Value, corner);
        else if (id is FloatingPanelID.Subagent) corner = runtime.SubagentPanelDock;
        else if (id is FloatingPanelID.Hydra) corner = runtime.HydraPanelDock;
        else if (id is FloatingPanelID.Popped) corner = runtime.HydraPoppedPanelDock;
        else if (id is FloatingPanelID.Usage) corner = model.Settings.UsagePanelDock;
        Reorder(model, runtime, id, point, corner, scene);
    }

    private Control MakeSubagentPanel(ChatThread helper, bool holds) // :326-356
    {
        var id = new FloatingPanelID.Subagent();
        var content = new SubagentPanel(helper, scene.Layout.PanelSize, helper.WorktreePath ?? project?.Path, project?.Name,
            translation => DragPanel(id, subagentDrag, translation, CurrentOrigin(id)), () => DragPanel(id, subagentDrag, default, CurrentOrigin(id), release: true),
            () => Transaction.WithAnimation(Chrome.PanelSlide, () => model!.CloseSubagent(helper.Id))).Transition(PanelTransition);
        return content;
    }

    private Control MakeHydraPanel(IReadOnlyList<ChatThread> heads, FloatingPanelID id, PanelDragState drag, PanelDockCorner fallback, bool holds, bool popped) // :359-397,420-485
    {
        var content = new HydraPanel(runtime, heads, scene.Layout.PanelSize, workingDirectory, project?.Name,
            translation => DragPanel(id, drag, translation, CurrentOrigin(id)), () => DragPanel(id, drag, default, CurrentOrigin(id), release: true),
            () => Transaction.WithAnimation(Chrome.PanelSlide, () =>
            {
                if (id is FloatingPanelID.Auto a) { runtime.HydraAutoPopHeld = runtime.HydraAutoPopHeld.Mutating(values => values.Add(a.Value)); runtime.HydraSelectedHeadID = a.Value; }
                else if (popped) { runtime.HydraSelectedHeadID = heads[0].Id; runtime.HydraPoppedHeadID = null; }
                else model!.DismissHydraHeads(runtime.ThreadID);
            }), isPoppedOut: popped, popOut: popped ? null : head => Transaction.WithAnimation(Chrome.PanelSlide, () =>
            {
                runtime.HydraPoppedHeadID = head; runtime.HydraPoppedPanelDock = runtime.HydraPanelDock.AcrossTheColumn;
                if (runtime.HydraSelectedHeadID == head) runtime.HydraSelectedHeadID = null;
            })).Transition(PanelTransition);
        return content;
    }

    private Control MakeUsagePanel(PanelUsage usage, bool holds) // :490-530
    {
        var id = new FloatingPanelID.Usage();
        var full = scene.Layout.PanelSize; var size = new Size(full.Width, scene.UsageHeight ?? full.Height);
        var content = new UsageFloatingPanel(runtime, usage.Provider, usage.HeadsProvider, size,
            height => { if (usageContentHeight != height) { usageContentHeight = height; MainActor.Post(RefreshScene); } },
            translation => DragPanel(id, usageDrag, translation, CurrentOrigin(id)), () => DragPanel(id, usageDrag, default, CurrentOrigin(id), release: true),
            () => Transaction.WithAnimation(Chrome.PanelSlide, () => model!.Settings.ShowsUsagePanel = false)).Transition(PanelTransition);
        return content;
    }

    // :731 @Entry var reserveSlideLeading: CGFloat = 0, an inherited environment value (R-VIEW-6).
    public static readonly AttachedProperty<double> ReserveSlideLeadingProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("ReserveSlideLeading", typeof(ChatView), 0, inherits: true);

    public static double GetReserveSlideLeading(Control control) => control.GetValue(ReserveSlideLeadingProperty);

    public static void SetReserveSlideLeading(Control control, double value) => control.SetValue(ReserveSlideLeadingProperty, value);

    // :734 @Entry var reserveSlideOffset: CGFloat = 0, an inherited environment value (R-VIEW-6).
    public static readonly AttachedProperty<double> ReserveSlideOffsetProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("ReserveSlideOffset", typeof(ChatView), 0, inherits: true);

    public static double GetReserveSlideOffset(Control control) => control.GetValue(ReserveSlideOffsetProperty);

    public static void SetReserveSlideOffset(Control control, double value) => control.SetValue(ReserveSlideOffsetProperty, value);

    // :36-52 private var isMovingAPanel: Bool. The mounted body reads its own drag fields through this helper.
    internal static bool IsMovingAPanel(
        PanelDragState subagentDrag,
        PanelDragState hydraDrag,
        PanelDragState poppedDrag,
        PanelDragState usageDrag,
        IReadOnlyDictionary<Guid, PanelDragState> autoDrags) => // :49-51
        subagentDrag.Position is not null || hydraDrag.Position is not null
            || poppedDrag.Position is not null || usageDrag.Position is not null
            || autoDrags.Values.Any(drag => drag.Position is not null);

    // :554-590. The mounted grips share the column's model and resize state.
    internal static PanelResize UsageResizer(AppModel model, PanelResizeState resize, PanelDockCorner corner, PanelScene scene, Size size, double minimum) => new()
    {
        Corner = corner,
        OnResize = (translation, axes) =>
        {
            var start = resize.Begin(size); // :559
            var across = corner.Side == PanelDockSide.Trailing ? -1 : 1; // :562
            var down = corner.IsTop ? 1 : -1; // :563
            if (axes.Contains(PanelResizeAxes.Width)) // :564-568
                resize.LiveSize = scene.Layout.Fitted(new Size(start.Width + translation.Width * across, scene.Layout.PanelHeight));
            if (axes.Contains(PanelResizeAxes.Height)) // :569-571
                resize.LiveUsageHeight = Math.Max(minimum, start.Height + translation.Height * down);
        },
        OnResizeEnd = () => CommitPanelResize(model, resize), // :573
        OnReset = () => Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.UsagePanelHeight = null), // :574-577
    };

    // :533-551 shared panel grips.
    internal static PanelResize Resizer(AppModel model, PanelResizeState resize, PanelDockCorner corner, PanelScene scene) => new()
    {
        Corner = corner,
        OnResize = (translation, axes) =>
        {
            var start = resize.Begin(scene.Layout.PanelSize);
            var across = corner.Side == PanelDockSide.Trailing ? -1 : 1;
            var down = corner.IsTop ? 1 : -1;
            resize.LiveSize = scene.Layout.Fitted(new Size(
                start.Width + (axes.Contains(PanelResizeAxes.Width) ? translation.Width * across : 0),
                start.Height + (axes.Contains(PanelResizeAxes.Height) ? translation.Height * down : 0)));
        },
        OnResizeEnd = () => CommitPanelResize(model, resize),
        OnReset = () => Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.PanelSize = null),
    };

    // :595-598. Only the live timeline width is quantized; commit keeps the exact preference.
    internal static Size? LivePreferredSize(PanelResizeState resize) => resize.IsActive && resize.LiveSize is { } live
        ? new Size(Math.Round(live.Width / 12, MidpointRounding.AwayFromZero) * 12, live.Height) : null;

    // :611-617 the key path is expressed as an explicit binding (R-VIEW-7).
    internal static void Dock(FloatingPanelID id, Binding<PanelDockCorner> corner, Point position, PanelScene scene)
    {
        var wanted = scene.Layout.DockCorner(position, corner.Value, scene.Docks);
        var next = scene.LockedSide(id) is { } side ? PanelDockCorner.Make(side, wanted.IsTop) : wanted;
        if (next != corner.Value) corner.Value = next;
    }

    // :620-625 usage corner is shared by every chat.
    internal static void DockUsage(AppModel model, Point position, PanelScene scene) =>
        Dock(new FloatingPanelID.Usage(), new Binding<PanelDockCorner>(() => model.Settings.UsagePanelDock, value => model.Settings.UsagePanelDock = value), position, scene);

    // :628-634.
    internal static void DockAuto(ThreadRuntime runtime, Guid id, Point position, PanelScene scene)
    {
        var panel = new FloatingPanelID.Auto(id);
        var current = runtime.HydraAutoPanelDocks.TryGetValue(id, out var stored) ? stored
            : scene.Slots.TryGetValue(panel, out var slot) ? slot.Corner : runtime.HydraPanelDock.AcrossTheColumn;
        var wanted = scene.Layout.DockCorner(position, current, scene.Docks);
        var next = scene.LockedSide(panel) is { } side ? PanelDockCorner.Make(side, wanted.IsTop) : wanted;
        if (!runtime.HydraAutoPanelDocks.TryGetValue(id, out var existing) || existing != next)
            runtime.HydraAutoPanelDocks = runtime.HydraAutoPanelDocks.Mutating(values => values[id] = next);
    }

    // :641-690. Swap on a full side, otherwise displace through the first free slot and rewrite its order.
    internal static void Reorder(AppModel model, ThreadRuntime runtime, FloatingPanelID id, Point position, PanelDockCorner corner, PanelScene scene)
    {
        var side = corner.Side;
        var others = scene.Slots.Where(entry => entry.Value.Corner.Side == side && entry.Key != id).ToArray();
        var slots = scene.SlotsOnSide.TryGetValue(side, out var count) ? count : 1;
        var n = Math.Max(slots, others.Length + 1);
        var target = scene.Layout.SlotIndex(position, n, scene.Docks);
        var occupancy = new Dictionary<long, FloatingPanelID>();
        foreach (var (panel, slot) in others) occupancy[slot.Corner.IsTop ? slot.Below : n - 1 - slot.Below] = panel;
        long? previous = scene.Slots.TryGetValue(id, out var old) && old.Corner.Side == side
            ? old.Corner.IsTop ? old.Below : n - 1 - old.Below : null;
        var full = !(others.Length + 1 > slots);
        if (occupancy.TryGetValue(target, out var other) && previous is { } before && full)
        {
            occupancy[before] = other;
            occupancy[target] = id;
        }
        else if (occupancy.TryGetValue(target, out other))
        {
            occupancy.Remove(target);
            occupancy[target] = id;
            var forward = previous is null || previous < target;
            var displaced = other;
            var next = target + (forward ? 1 : -1);
            while (true)
            {
                if (next < 0 || next >= n)
                {
                    var free = Enumerable.Range(0, checked((int)n)).Select(index => (long)index);
                    if (!forward) free = free.Reverse();
                    foreach (var index in free)
                        if (!occupancy.ContainsKey(index)) { occupancy[index] = displaced; break; }
                    break;
                }
                if (!occupancy.TryGetValue(next, out var occupant)) { occupancy[next] = displaced; break; }
                occupancy[next] = displaced;
                displaced = occupant;
                next += forward ? 1 : -1;
            }
        }
        else occupancy[target] = id;
        foreach (var (index, panel) in occupancy) SetCorner(model, runtime, panel, PanelDocks.Corner(index, n, side).Corner);
        var occupiedCorners = occupancy.ToDictionary(entry => entry.Value, entry => PanelDocks.Corner(entry.Key, n, side).Corner);
        var top = occupancy.Where(entry => occupiedCorners[entry.Value].IsTop).OrderBy(entry => entry.Key).Select(entry => entry.Value);
        var bottom = occupancy.Where(entry => !occupiedCorners[entry.Value].IsTop).OrderByDescending(entry => entry.Key).Select(entry => entry.Value);
        var onSide = occupancy.Values.ToHashSet();
        var kept = runtime.PanelStackOrder.Where(panel => !onSide.Contains(panel) && panel != id);
        var order = new ValueList<FloatingPanelID>(kept.Concat(top).Concat(bottom));
        if (order != runtime.PanelStackOrder) runtime.PanelStackOrder = order;
    }

    // :693-704.
    internal static void SetCorner(AppModel model, ThreadRuntime runtime, FloatingPanelID id, PanelDockCorner corner)
    {
        switch (id)
        {
            case FloatingPanelID.Subagent: if (runtime.SubagentPanelDock != corner) runtime.SubagentPanelDock = corner; break;
            case FloatingPanelID.Hydra: if (runtime.HydraPanelDock != corner) runtime.HydraPanelDock = corner; break;
            case FloatingPanelID.Popped: if (runtime.HydraPoppedPanelDock != corner) runtime.HydraPoppedPanelDock = corner; break;
            case FloatingPanelID.Usage: if (model.Settings.UsagePanelDock != corner) model.Settings.UsagePanelDock = corner; break;
            case FloatingPanelID.Auto head:
                if (!runtime.HydraAutoPanelDocks.TryGetValue(head.Value, out var old) || old != corner)
                    runtime.HydraAutoPanelDocks = runtime.HydraAutoPanelDocks.Mutating(values => values[head.Value] = corner);
                break;
        }
    }

    // :581-590. Preferences are written once on release; clear the live state afterwards.
    internal static void CommitPanelResize(AppModel model, PanelResizeState resize)
    {
        if (resize.LiveSize is { } size) model.Settings.PanelSize = size;
        if (resize.LiveUsageHeight is { } height) model.Settings.UsagePanelHeight = height;
        resize.LiveSize = null;
        resize.LiveUsageHeight = null;
        resize.End();
    }

    // :707-710.
    internal static Point Rest(PanelScene.PanelSlot slot, PanelScene scene)
    {
        var count = scene.SlotsOnSide.TryGetValue(slot.Corner.Side, out var slots) ? slots : 1;
        var stacked = scene.Docks.Stacked(slot.Corner, slot.Below, count, scene.Layout);
        return new Point(stacked.X, slot.Corner.IsTop ? stacked.Y + slot.Lift : stacked.Y - slot.Lift);
    }

    // :496-503. A bottom-docked panel retains its full-height panel's bottom edge.
    internal static Point UsageOrigin(PanelScene.PanelSlot slot, PanelScene scene)
    {
        var rest = Rest(slot, scene);
        var full = scene.Layout.PanelSize;
        var height = scene.UsageHeight ?? full.Height;
        return slot.Corner.IsTop ? rest : new Point(rest.X, rest.Y + full.Height - height);
    }

    /// <summary>:399-417 <c>private struct HydraCaptureFrame: ViewModifier</c>. The capture run it feeds is not ported
    /// (ED-001: WebsiteCaptures.isEnabled is always false, porting/dead_code.md entry 44).</summary>
    private sealed record HydraCaptureFrame(Guid ThreadID, WindowLiveResize LiveResize)
    {
        // :407-416 func body(content:)
        public Control Body(Control content) => content; // :414-415 non-capture branch; ED-001 excludes the other branch.
    }
}

/// <summary>:721-725 <c>private struct HydraPresence: Equatable</c>.</summary>
internal sealed record HydraPresence
{
    public required Guid ThreadID { get; init; } // :723
    public required bool Present { get; init; } // :724
}

/// <summary>:713-720, :737-771 <c>private struct ReserveSlide: ViewModifier</c>.</summary>
internal sealed record ReserveSlide(PanelReserve Reserve, bool Slides, bool AnimatesWidth = false)
{
    // :745 @State persists across copies of this modifier at the same view identity.
    private sealed class SlideState
    {
        public readonly TranslateTransform Transform = new();
        public double Offset;
        public PanelReserve? Previous;
        public ChatFrameAnimation? Animation;
    }

    private readonly SlideState state = new();
    internal double Offset => state.Offset;

    // :747-770. Padding snaps for the conversation, glides for the composer; offset glides only for the conversation.
    public Control Body(Control content)
    {
        if (state.Animation is null)
        {
            state.Animation = new ChatFrameAnimation(content);
            state.Animation.AttachLifetime();
        }
        var margin = new Thickness(Reserve.Leading, 0, Reserve.Trailing, 0);
        var changed = state.Previous is { } old && old != Reserve;
        ChatView.SetReserveSlideLeading(content, Reserve.Leading); // :749
        content.RenderTransform = state.Transform; // :756
        void OffsetTo(double value)
        {
            state.Offset = value;
            state.Transform.X = value;
            ChatView.SetReserveSlideOffset(content, value); // :750, per animation frame.
        }
        if (changed)
        {
            state.Animation.Stop();
            var previous = state.Previous!;
            var shift = ((Reserve.Leading - Reserve.Trailing) - (previous.Leading - previous.Trailing)) / 2; // :760
            if (Slides && !AnimatesWidth && shift != 0) // :761
            {
                content.Margin = margin; // :751-752: one layout at the final width.
                var from = state.Offset - shift; // :766
                state.Animation.Start(Chrome.PanelSlide, progress => OffsetTo(from * (1 - progress))); // :768
            }
            else
            {
                OffsetTo(0); // :762
                if (Slides && AnimatesWidth) // :754
                {
                    var from = content.Margin;
                    state.Animation.Start(Chrome.PanelSlide, progress => content.Margin = new Thickness(
                        from.Left + (margin.Left - from.Left) * progress, 0,
                        from.Right + (margin.Right - from.Right) * progress, 0));
                }
                else content.Margin = margin;
            }
        }
        else if (state.Previous is null)
        {
            content.Margin = margin; // :719-720: first measurement does not glide.
            OffsetTo(0);
        }
        state.Previous = Reserve;
        return content;
    }
}
/// <summary>:773-808 <c>@MainActor private struct PanelMembers</c>: which threads sit in the chat's floating panels.</summary>
internal sealed record PanelMembers
{
    public ChatThread? Subagent { get; init; } // :778
    public bool IsDocked { get; init; } // :779
    public ValueList<ChatThread> Heads { get; init; } // :780
    public ChatThread? Popped { get; init; } // :781
    public bool IsHydraHidden { get; init; } // :782
    public PanelUsage? Usage { get; init; } // :785

    // :787-807 init(runtime:model:)
    public PanelMembers(ThreadRuntime runtime, AppModel model)
    {
        Subagent = model.PanelSubagent(runtime.ThreadID); // :790
        IsDocked = Subagent is not null; // :791
        IsHydraHidden = runtime.IsHydraPanelHidden; // :792
        var team = IsHydraHidden ? ValueList<ChatThread>.Empty : model.PanelHeads(runtime.ThreadID); // :793
        var poppedHead = team.Count > 1 ? team.FirstOrDefault(t => t.Id == runtime.HydraPoppedHeadID) : null; // :794
        Popped = poppedHead; // :795
        Heads = new ValueList<ChatThread>(team.Where(t => t.Id != poppedHead?.Id)); // :796
        if (model.Thread(runtime.ThreadID) is { } thread && model.Settings.ShowsUsagePanel) // :797
        {
            ProviderKind? headsProvider = null;
            if (model.HydraIsOn(thread) && model.HydraPair(thread) is { } pair) // :799
            {
                var provider = model.HydraHeadsProvider(pair); // :800
                headsProvider = provider == thread.Provider ? null : provider; // :801
            }
            Usage = new PanelUsage { Provider = thread.Provider, HeadsProvider = headsProvider }; // :803
        }
        else
        {
            Usage = null; // :805
        }
    }
}

/// <summary>:810-815 <c>struct PanelUsage: Equatable</c>: whose limits the usage panel shows.</summary>
public sealed record PanelUsage
{
    public required ProviderKind Provider { get; init; } // :813
    public ProviderKind? HeadsProvider { get; init; } // :814
}

/// <summary>:817-1083 <c>@MainActor private struct PanelScene</c>: everything the chat's floating panels need.</summary>
internal sealed record PanelScene
{
    public SubagentPanelLayout Layout { get; init; } // :821
    public ChatThread? Subagent { get; init; } // :825
    public bool IsDocked { get; init; } // :826
    public ValueList<ChatThread> Heads { get; init; } // :830
    public ChatThread? Popped { get; init; } // :831

    /// <summary>:832-839 <c>struct PanelSlot</c>.</summary>
    public sealed record PanelSlot
    {
        public required PanelDockCorner Corner { get; init; } // :836
        public required long Below { get; init; } // :837
        public required double Lift { get; init; } // :838
    }

    public ValueList<ChatThread> AutoPopped { get; init; } // :841
    public ValueMap<FloatingPanelID, PanelSlot> Slots { get; init; } // :842
    public ValueMap<PanelDockSide, long> SlotsOnSide { get; init; } // :843

    // :849-857 func lockedSide(excluding:) -> PanelDockSide?
    public PanelDockSide? LockedSide(FloatingPanelID excluding)
    {
        if (Layout.FitsBothSides) return null; // :850
        var others = Slots.Where(kv => !Equals(kv.Key, excluding)).ToList(); // :851
        if (others.Count == 0) return null; // :852
        var hydraSlot = others.Where(kv => kv.Key is FloatingPanelID.Hydra).Select(kv => kv.Value).FirstOrDefault();
        if (hydraSlot is { } hydra) return hydra.Corner.Side; // :853
        var leading = others.Count(kv => kv.Value.Corner.Side == PanelDockSide.Leading); // :854
        var trailing = others.Count - leading; // :855
        return leading >= trailing ? PanelDockSide.Leading : PanelDockSide.Trailing; // :856
    }

    public double? UsageHeight { get; init; } // :861
    public PanelUsage? Usage { get; init; } // :863
    public bool IsMeasured { get; init; } // :864
    public bool ShowsHydra { get; init; } // :865
    public bool ShowsPopped { get; init; } // :866
    public bool ShowsUsage { get; init; } // :867
    public bool IsHydraDocked { get; init; } // :868
    public ValueList<PanelDockSide> DockedSides { get; init; } // :871
    public PanelReserve Reserve { get; init; } // :872
    public PanelDocks Docks { get; init; } // :873

    // :875 var hasHeads: Bool
    public bool HasHeads => Heads.Count > 0;

    // :879-899 init(members:runtime:model:paneSize:composerAreaHeight:usageContentHeight:holdsHydra:preferredSize:usageHeightOverride:)
    public PanelScene(
        PanelMembers members,
        ThreadRuntime runtime,
        AppModel model,
        Size paneSize,
        double composerAreaHeight,
        double? usageContentHeight,
        bool holdsHydra = false,
        Size? preferredSize = null,
        double? usageHeightOverride = null)
    {
        var geometry = PanelSceneGeometry(members, runtime, model, paneSize, composerAreaHeight, usageContentHeight, holdsHydra, preferredSize, usageHeightOverride); // :880
        Subagent = members.Subagent; // :881
        IsDocked = members.IsDocked; // :882
        AutoPopped = geometry.AutoPopped; // :883
        Slots = geometry.Slots; // :884
        SlotsOnSide = geometry.SlotsOnSide; // :885
        UsageHeight = geometry.UsageHeight; // :886
        Heads = new ValueList<ChatThread>(members.Heads.Where(head => !geometry.AutoPopped.Any(a => a.Id == head.Id))); // :887
        Popped = members.Popped; // :888
        Usage = members.Usage; // :889
        IsMeasured = geometry.IsMeasured; // :890
        ShowsHydra = geometry.ShowsHydra; // :891
        ShowsPopped = geometry.ShowsPopped; // :892
        ShowsUsage = geometry.ShowsUsage; // :893
        IsHydraDocked = geometry.IsHydraDocked; // :894
        DockedSides = geometry.DockedSides; // :895
        Layout = geometry.Layout; // :896
        Reserve = geometry.Reserve; // :897
        Docks = geometry.Docks; // :898
    }

    /// <summary>:901-917 <c>struct Geometry</c>: the geometry alone, from the members and the measured pane.</summary>
    public sealed record Geometry
    {
        public required SubagentPanelLayout Layout { get; init; } // :904
        public required ValueList<ChatThread> AutoPopped { get; init; } // :905
        public required ValueMap<FloatingPanelID, PanelSlot> Slots { get; init; } // :906
        public required ValueMap<PanelDockSide, long> SlotsOnSide { get; init; } // :907
        public double? UsageHeight { get; init; } // :908
        public required bool IsMeasured { get; init; } // :909
        public required bool ShowsHydra { get; init; } // :910
        public required bool ShowsPopped { get; init; } // :911
        public required bool ShowsUsage { get; init; } // :912
        public required bool IsHydraDocked { get; init; } // :913
        public required ValueList<PanelDockSide> DockedSides { get; init; } // :914
        public required PanelReserve Reserve { get; init; } // :915
        public required PanelDocks Docks { get; init; } // :916
    }

    // :920 static let usageMinimumHeight
    public static double UsageMinimumHeight => HydraPanel.StripHeight + 44;

    // :925-1051 static func geometry(members:runtime:model:paneSize:composerAreaHeight:usageContentHeight:holdsHydra:preferredSize:usageHeightOverride:) -> Geometry
    public static Geometry PanelSceneGeometry(
        PanelMembers members,
        ThreadRuntime runtime,
        AppModel model,
        Size paneSize,
        double composerAreaHeight,
        double? usageContentHeight,
        bool holdsHydra = false,
        Size? preferredSize = null,
        double? usageHeightOverride = null)
    {
        var isMeasured = paneSize != default; // :926 paneSize != .zero
        var preferred = preferredSize ?? model.Settings.PanelSize; // :927
        var showsHydra = (members.Heads.Count > 0 || holdsHydra) && isMeasured; // :928
        var showsPopped = members.Popped is not null && showsHydra; // :929
        var showsUsage = members.Usage is not null && isMeasured; // :930
        var isHydraDocked = showsHydra; // :931
        var helperShowsProgress = members.Subagent?.IsHydraHead ?? true; // :932
        var compact = !model.Settings.HydraShowsHeadDetails && helperShowsProgress && (showsHydra || members.Subagent is not null); // :933
        var single = new SubagentPanelLayout { Pane = paneSize, ComposerAreaHeight = composerAreaHeight, StackDepth = 1, Preferred = preferred, Compact = compact }; // :936
        var slotHeight = single.PanelHeight; // :937
        var gap = SubagentPanelLayout.Gap; // :938
        var placed = new List<(FloatingPanelID Id, PanelDockCorner Corner)>();
        if (members.IsDocked) placed.Add((new FloatingPanelID.Subagent(), runtime.SubagentPanelDock)); // :940
        if (isHydraDocked) placed.Add((new FloatingPanelID.Hydra(), runtime.HydraPanelDock)); // :941
        if (showsPopped) placed.Add((new FloatingPanelID.Popped(), runtime.HydraPoppedPanelDock)); // :942
        if (showsUsage) placed.Add((new FloatingPanelID.Usage(), model.Settings.UsagePanelDock)); // :943
        var usageCorner = model.Settings.UsagePanelDock; // :944
        double? usageHeight = null;
        if (showsUsage) // :946
        {
            var under = Order(placed, runtime.PanelStackOrder).TryGetValue(new FloatingPanelID.Usage(), out var u) ? u.Below : 0; // :947
            var room = single.VerticalRoom - under * (slotHeight + gap); // :948
            var wanted = usageHeightOverride ?? model.Settings.UsagePanelHeight ?? usageContentHeight ?? slotHeight; // :949
            usageHeight = Math.Min(Math.Max(UsageMinimumHeight, room), Math.Max(UsageMinimumHeight, wanted)); // :950
        }
        var usageLift = Math.Max(0, (usageHeight ?? 0) - slotHeight); // :954
        var autoPopped = new List<ChatThread>(); // :955
        var layoutFitsBothSides = single.FitsBothSides; // :956
        var smallest = compact ? SubagentPanelLayout.CompactHeight : SubagentPanelLayout.MinHeight; // :960
        var natural = Math.Min(3, Math.Max(1, (long)((single.VerticalRoom + gap) / (smallest + gap)))); // :961
        if (model.Settings.HydraAutoPopsHeads && showsHydra && members.Heads.Count > 1) // :962
        {
            var candidates = members.Heads.Skip(1).Where(h => !runtime.HydraAutoPopHeld.Contains(h.Id)).ToList(); // :963
            foreach (var candidate in candidates.Where(c => runtime.HydraAutoPanelDocks.ContainsKey(c.Id)).ToList()) // :964
            {
                var corner = runtime.HydraAutoPanelDocks[candidate.Id]; // :965
                placed.Add((new FloatingPanelID.Auto(candidate.Id), corner)); // :966
                autoPopped.Add(candidate); // :967
            }
            candidates.RemoveAll(c => runtime.HydraAutoPanelDocks.ContainsKey(c.Id)); // :969
            var usageExtra = usageLift > 0 ? (long)Math.Ceiling(usageLift / (slotHeight + gap)) : 0; // :971
            var corners = layoutFitsBothSides
                ? [runtime.HydraPanelDock.AcrossTheColumn, runtime.HydraPanelDock]
                : new[] { runtime.HydraPanelDock }; // :973
            foreach (var corner in corners)
            {
                var other = FlippedVertically(corner); // :974
                var usageOnSide = showsUsage && (usageCorner == corner || usageCorner == other); // :975
                var used = placed.Count(p => p.Corner == corner) + placed.Count(p => p.Corner == other) + (usageOnSide ? usageExtra : 0); // :976
                var free = Math.Max(0, natural - used); // :977
                var take = Math.Min(free, candidates.Count); // :978 Swift evaluates the range before removing any candidates.
                for (var i = 0; i < take; i++)
                {
                    var head = candidates[0];
                    candidates.RemoveAt(0); // :979
                    placed.Add((new FloatingPanelID.Auto(head.Id), corner)); // :980
                    autoPopped.Add(head); // :981
                }
            }
        }
        if (!layoutFitsBothSides) // :987
        {
            var sidesSeen = placed.Select(p => p.Corner.Side).Distinct().ToList(); // :988
            if (sidesSeen.Count > 1) // :989
            {
                var hydraEntry = placed.FirstOrDefault(p => p.Id is FloatingPanelID.Hydra);
                var home = hydraEntry.Id is FloatingPanelID.Hydra ? hydraEntry.Corner.Side
                    : placed.Count(p => p.Corner.Side == PanelDockSide.Leading) >= placed.Count(p => p.Corner.Side == PanelDockSide.Trailing) ? PanelDockSide.Leading : PanelDockSide.Trailing; // :990-991
                placed = [.. placed.Select(p => (p.Id, PanelDockCorner.Make(home, p.Corner.IsTop)))]; // :992
            }
        }
        var slotsByID = new Dictionary<FloatingPanelID, PanelSlot>(); // :995
        var ordered = Order(placed, runtime.PanelStackOrder); // :996
        var usageBelow = ordered.TryGetValue(new FloatingPanelID.Usage(), out var ub) ? ub.Below : (long?)null; // :997
        foreach (var (id, entry) in ordered) // :998
        {
            var lift = usageBelow is { } ubv && entry.Corner == usageCorner && showsUsage && id is not FloatingPanelID.Usage && entry.Below > ubv
                ? usageLift // :1000-1001
                : 0; // :1003
            slotsByID[id] = new PanelSlot { Corner = entry.Corner, Below = entry.Below, Lift = lift }; // :1005
        }
        var placedCorners = placed.Select(p => p.Corner).ToList(); // :1007
        var sides = placedCorners.Select(c => c.Side).Distinct().ToHashSet(); // :1008
        var dockedSides = new[] { PanelDockSide.Leading, PanelDockSide.Trailing }.Where(sides.Contains).ToList(); // :1009
        var slotsOnSide = new Dictionary<PanelDockSide, long>();
        foreach (var side in new[] { PanelDockSide.Leading, PanelDockSide.Trailing }) // :1011
            slotsOnSide[side] = Math.Max(natural, placed.Count(p => p.Corner.Side == side)); // :1012
        var stackDepth = 1L; // :1019
        foreach (var side in new[] { PanelDockSide.Leading, PanelDockSide.Trailing }) // :1020
        {
            var n = slotsOnSide.GetValueOrDefault(side, 1); // :1021
            var indices = slotsByID.Values.Where(s => s.Corner.Side == side)
                .Select(s => s.Corner.IsTop ? s.Below : n - 1 - s.Below).OrderBy(x => x).ToList(); // :1022-1025
            if (indices.Count <= 1) continue; // :1026
            var closest = indices.Zip(indices.Skip(1), (a, b) => b - a).DefaultIfEmpty(1).Min(); // :1027
            var depth = (long)Math.Ceiling((double)(n - 1) / Math.Max(1, closest)) + 1; // :1028
            stackDepth = Math.Max(stackDepth, depth); // :1029
        }
        var layout = new SubagentPanelLayout { Pane = paneSize, ComposerAreaHeight = composerAreaHeight, StackDepth = stackDepth, Preferred = preferred, Compact = compact }; // :1031
        var reserve = new PanelReserve
        {
            Leading = sides.Contains(PanelDockSide.Leading) ? layout.ComposerReserve : 0, // :1033
            Trailing = sides.Contains(PanelDockSide.Trailing) ? layout.ComposerReserve : 0, // :1034
        };
        return new Geometry
        {
            Layout = layout,
            AutoPopped = new ValueList<ChatThread>(autoPopped),
            Slots = new ValueMap<FloatingPanelID, PanelSlot>(slotsByID),
            SlotsOnSide = new ValueMap<PanelDockSide, long>(slotsOnSide),
            UsageHeight = usageHeight,
            IsMeasured = isMeasured,
            ShowsHydra = showsHydra,
            ShowsPopped = showsPopped,
            ShowsUsage = showsUsage,
            IsHydraDocked = isHydraDocked,
            DockedSides = new ValueList<PanelDockSide>(dockedSides),
            Reserve = reserve,
            Docks = new PanelDocks(layout, reserve), // :1049
        };
    }

    // :1053-1073 static func order(_:by:) -> [FloatingPanelID: (corner: PanelDockCorner, below: Int)]
    public static ValueMap<FloatingPanelID, (PanelDockCorner Corner, long Below)> Order(
        IReadOnlyList<(FloatingPanelID Id, PanelDockCorner Corner)> placed,
        IReadOnlyList<FloatingPanelID> by)
    {
        var rank = new Dictionary<FloatingPanelID, long>();
        foreach (var id in by) // :1055
            if (!rank.ContainsKey(id)) rank[id] = rank.Count;
        var result = new Dictionary<FloatingPanelID, (PanelDockCorner Corner, long Below)>();
        var corners = placed.Select(p => p.Corner).Distinct().ToList(); // :1059
        foreach (var corner in corners)
        {
            var inCorner = placed
                .Select((entry, offset) => (entry, offset))
                .Where(x => x.entry.Corner == corner) // :1061
                .OrderBy(x => rank.TryGetValue(x.entry.Id, out var r) ? r : long.MaxValue) // :1063-1065
                .ThenBy(x => x.offset)
                .Select(x => x.entry)
                .ToList(); // :1067
            for (var below = 0; below < inCorner.Count; below++)
                result[inCorner[below].Id] = (inCorner[below].Corner, below); // :1068-1069
        }
        return new ValueMap<FloatingPanelID, (PanelDockCorner Corner, long Below)>(result);
    }

    // :1075-1082 private static func flippedVertically(_:) -> PanelDockCorner
    private static PanelDockCorner FlippedVertically(PanelDockCorner corner) => corner switch
    {
        PanelDockCorner.TopLeading => PanelDockCorner.BottomLeading, // :1077
        PanelDockCorner.BottomLeading => PanelDockCorner.TopLeading, // :1078
        PanelDockCorner.TopTrailing => PanelDockCorner.BottomTrailing, // :1079
        PanelDockCorner.BottomTrailing => PanelDockCorner.TopTrailing, // :1080
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}

/// <summary>:1085-1104 <c>struct JumpToLatestButton: View</c>.</summary>
public sealed class JumpToLatestButton : Decorator
{
    // :1087 let scrollState; :1089-1103 body
    public JumpToLatestButton(TimelineScrollState scrollState)
    {
        var host = new Panel();
        Height = 0; // :1095 zero flow height, above the composer.
        Child = host;
        ChatBottomOverlay? frame = null;
        this.Track(() => scrollState.ShowsJumpButton, shown =>
        {
            if (shown && frame is null)
            {
                var button = new ChromeCircleButton("arrow.down", "Jump to latest", scrollState.JumpToLatest)
                { HorizontalAlignment = HorizontalAlignment.Center };
                // :1094-1101 the transition is on the zero-height frame with the button's 10 points of bottom padding, not on the button: it scales about the frame's centre (the
                // point at the top of the composer, centred on the column), so the circle rises by more than the offset's 10 points while it grows. Measured on the reference
                // runtime (2-chat-use.mov 10.357-10.478: the rise fits 19.6 points of travel on the spring, 20.4 for this geometry, and cannot fit the offset's 10).
                frame = new ChatBottomOverlay(button, 10) // :1094 bottom padding.
                    .Transition(SwiftTransition.Asymmetric(
                        SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity).Combined(SwiftTransition.Offset(y: 10)),
                        SwiftTransition.Scale(0.85).Combined(SwiftTransition.Opacity).Combined(SwiftTransition.Offset(y: 6)))); // :1096-1101
                SwiftTransition.Insert(host, host.Children.Count, frame);
            }
            else if (!shown && frame is { } leaving)
            {
                frame = null;
                SwiftTransition.Remove(host, leaving);
            }
        });
    }
}

/// <summary>PORT-ONLY. <c>GlassEffectContainer(spacing:)</c> (ChatView.swift:1123): on macOS 26 it groups nearby
/// glass shapes so they sample and morph as one compositor pass instead of each drawing its own. Each control
/// inside carries its own glass (Chrome.ChromeGlassCapsule/ChromeGlassCircle); the container's spacing is set on the
/// material foundation (SwiftGlass.SetContainerSpacing, F.GlassMaterial), which draws every glass piece under it in the
/// one material pass of the container, as the composer's container (ComposerView.swift:23) and the diff chrome's
/// (DiffInspector.swift:25) are. Visual parity of the material itself is UNVERIFIED without macOS (OD-0001, OD-0005).</summary>
internal sealed class GlassEffectContainer : Decorator
{
    public double Spacing { get; }
    public GlassEffectContainer(double spacing, Func<Control> content)
    {
        Spacing = spacing; // :1123
        SwiftGlass.SetContainerSpacing(this, spacing); // :1120-1123 one glass pass for the whole row (PR-CHAT-171, UI-CHAT-073)
        Child = content();
    }
}

/// <summary>:1106-1182 <c>private struct ChatChromeRow: View</c>: the sticky glass controls across the top.</summary>
internal sealed class ChatChromeRow : Decorator
{
    // :1130,1140 .transition(.softAppear) (Chrome.swift:938-955): arrives fading in with a 4-point rise under
    // Animation.softAppear, leaves with .opacity.animation(.easeOut(duration: 0.12)). SwiftUI animates the opacity and offset
    // the modifier's body sets; the shared SoftAppear (a modifier with no animatable data) switched them at once, so the
    // insertion is decomposed into those layer effects, as ThreadTimeline's SoftAppearTransition does.
    internal static SwiftTransition SoftAppear => SwiftTransition.Asymmetric(
        SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 4)).Animation(Animation.SoftAppear),
        SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.12)));

    private Action<string, Project?, string>? updateInputs;
    private readonly ChatChromeTitle titleView = null!;

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): see <see cref="ChatView.PrepareTitleCross"/>.</summary>
    internal void PrepareTitleCross(string previousTitle) => titleView.PrepareCross(previousTitle);

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): see <see cref="ChatView.TitleCross"/>.</summary>
    internal (Control Leaving, Control Arriving)? TitleCross => titleView.Cross;

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): see <see cref="ChatView.EndTitleCross"/>.</summary>
    internal void EndTitleCross() => titleView.EndCross();

    /// <summary>The words the title draws now, the ones arriving if a crossfade is under way (tests).</summary>
    internal string TitleWords => titleView.Words;

    internal void UpdateInputs(string title, Project? project, string directory) => updateInputs?.Invoke(title, project, directory); // :1112-1115,1147-1151
    // :1109-1114 the view's inputs, plus AppModel explicitly (no Avalonia @Environment). scrollChrome is not read
    // here (the row has no member citing it); it stays a parameter so the caller's signature matches the Swift
    // view's own inputs and PR-CHAT-159 (a different member) can use it once ChatView's own body is built.
    public ChatChromeRow(
        AppModel model,
        ThreadRuntime runtime,
        ChromeScrollModel scrollChrome,
        string title,
        Project? project,
        string directory,
        GitStatusModel git)
    {
        _ = scrollChrome;
        var host = new Panel();
        Child = host;
        var titleView = new ChatChromeTitle(model, runtime, title); // :1194-1196 title state survives other row changes.
        this.titleView = titleView;
        // The row's own children persist across refreshes, as SwiftUI keeps a view's identity (and its @State, such as an open
        // popover) while its body re-evaluates: only the controls whose branch changes come and go, and the two that carry
        // .transition(.softAppear) (ThreadsButton :1129-1131, HydraButton :1139-1140) arrive and leave through it.
        var row = new ChatChromeRowStack { Spacing = 10, VerticalAlignment = VerticalAlignment.Center }; // :1124, :1238
        var container = new GlassEffectContainer(2, () => row); // :1123
        host.Children.Add(container);
        ThreadsButton? threadsButton = null;
        HydraButton? hydraButton = null;
        var placed = new List<Control>();
        var leaving = new HashSet<Control>();
        var built = false;
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :1152
        ChromeCapsule? toggles = null;
        ChromeIconButton? terminalToggle = null, diffToggle = null;
        (string, string) toggleHelps = default;

        bool? previousThreadsShown = null;
        updateInputs = (nextTitle, nextProject, nextDirectory) =>
        {
            title = nextTitle; project = nextProject; directory = nextDirectory;
            titleView.UpdateTitle(title); // :1147-1151 no title animation on the Mac; the words crossfade (OD-0076, ED-278 (14)).
            Build();
        };
        void Reconcile(List<Control> desired, HashSet<Control> transitioning, HashSet<Control> silent)
        {
            leaving.RemoveWhere(control => !row.Children.Contains(SwiftTransition.Container(control)));
            var wanted = desired.Select(SwiftTransition.Container).ToHashSet();
            var lingering = leaving.Select(SwiftTransition.Container).ToHashSet();
            foreach (var child in row.Children.ToArray())
            {
                if (wanted.Contains(child) || lingering.Contains(child)) continue;
                var owner = placed.FirstOrDefault(control => ReferenceEquals(SwiftTransition.Container(control), child));
                if (owner is not null && built)
                {
                    placed.Remove(owner);
                    leaving.Add(owner);
                    SwiftTransition.Remove(row, owner); // :1139-1140,1129-1131 .transition(.softAppear): a quick fade out
                }
                else
                {
                    if (owner is not null) placed.Remove(owner);
                    row.Children.Remove(child);
                }
            }
            var position = 0;
            foreach (var control in desired)
            {
                var wrapped = SwiftTransition.Container(control);
                if (leaving.Remove(control)) row.Children.Remove(wrapped); // taken back mid-flight: it arrives again
                while (position < row.Children.Count && lingering.Contains(row.Children[position])) position++;
                if (position < row.Children.Count && ReferenceEquals(row.Children[position], wrapped))
                {
                    position++;
                    continue;
                }
                var at = row.Children.IndexOf(wrapped);
                if (at >= 0)
                {
                    row.Children.Move(at, position++);
                    continue;
                }
                if (transitioning.Contains(control))
                {
                    placed.Add(control);
                    if (built && !silent.Contains(control)) SwiftTransition.Insert(row, position, control);
                    else SwiftTransition.Place(row, position, control);
                }
                else row.Children.Insert(position, control);
                position++;
            }
            // A removed control gives up its slot at once (the stack closes up around it) and fades where it stood.
            row.Departing.Clear();
            foreach (var control in leaving) row.Departing.Add(SwiftTransition.Container(control));
        }
        void Build()
        {
            // :1119 `let sidebarVisible = model.sidebar.holdsTrafficLights` and :1179 `.animation(Chrome.panelSlide, value: sidebarVisible)`:
            // on the Mac the window buttons lead the row while the sidebar does not hold them, so the row glides when they come or go.
            // DEVIATION OD-0044 (ED-NEW-sidebarmodes-8): the caption buttons are Windows ones at the top right, nothing leads the row and
            // the list button no longer follows them (ED-NEW-sidebarmodes-1), so the row neither reads nor glides on that flag: it used
            // to rebuild and glide part-way through every column slide for nothing. The list button comes and goes with Panel only, and
            // the row closes up or opens for it on panelSlide (the same glide).
            var threadsShown = !(model.Settings.SidebarFloats && model.Settings.SidebarOnlyFloats);
            if (built && previousThreadsShown is { } wasShown && wasShown != threadsShown)
                row.Glide(Chrome.PanelSlide);
            previousThreadsShown = threadsShown;
            var desired = new List<Control>();
            var transitioning = new HashSet<Control>();
            var silent = new HashSet<Control>();

            // :1128 DEVIATION (ED-NEW-sidebarmodes-1): the Mac draws the list button only while the column is away
            // (`!sidebarVisible && !onlyFloats`) and hides an open column from its menu bar's View > Toggle sidebar (AppCommands.swift:38).
            // The port has no menu bar (OD-0008 C3), so with the Mac's rule an open column had no visible way to close. The button
            // stays in Column and Floating whether the column is open or not, and leaves (through .softAppear) only in Panel only,
            // which the setting's own words say has "no toolbar button". Column and Floating share the one button: switching
            // between them keeps it in place.
            if (threadsShown)
            {
                threadsButton ??= new ThreadsButton(model).Transition(SoftAppear); // :1129-1131
                desired.Add(threadsButton);
                transitioning.Add(threadsButton);
            }
            else threadsButton = null;
            desired.Add(new ChromeCircleButton(
                "square.and.pencil",
                "New thread" + ShortcutStore.Hint(AppShortcut.NewThread), // :1132
                () => model.NewThread(project))); // :1133

            if (model.Thread(runtime.ThreadID) is { } thread) // :1135
            {
                if (model.Settings.HydraEnabled && !thread.IsHelper) // :1138
                {
                    // :1139-1140 the view keeps its identity and its state while its `thread` input changes.
                    if (hydraButton is null)
                        hydraButton = new HydraButton(thread, runtime).Transition(SoftAppear);
                    else
                        hydraButton.Update(thread);
                    desired.Add(hydraButton);
                    transitioning.Add(hydraButton);
                }
                else hydraButton = null;
                desired.Add(new PermissionMenu(model, thread)); // :1142
            }
            else hydraButton = null;
            if (git.IsRepository) // :1144
            {
                desired.Add(new BranchMenu(model, runtime, directory, git)); // :1145
            }
            desired.Add(titleView); // :1147-1151: no title animation; its popover state is retained.

            var openCapsule = new ChromeCapsule(() =>
            {
                var capsule = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
                capsule.Children.Add(new OpenInMenu(directory)); // :1154
                if (project is not null) // :1155
                {
                    capsule.Children.Add(new ChromeDivider()); // :1156
                    capsule.Children.Add(new ScriptsMenu(model, project, runtime)); // :1157
                }
                if (git.IsRepository) // :1159
                {
                    capsule.Children.Add(new ChromeDivider()); // :1160
                    capsule.Children.Add(new GitActionsMenu(model, runtime, directory, git)); // :1161
                }
                return capsule;
            }); // :1153-1163
            // :1164-1172 the toggle capsule keeps its buttons across a refresh (a SwiftUI body re-evaluates and the views keep their identity), so
            // the "on" circle of a button fades in and out where the flag it follows changes: the terminal's under the column's
            // `.animation(Chrome.panelSlide, value: runtime.isTerminalVisible)` (:290); the Changes button's, which the Mac snaps, on the
            // hover curve (OD-0076). A changed tooltip (the shortcut's hint) builds the capsule afresh.
            var terminalHelp = "Terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal); // :1165
            var changesHelp = "Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges); // :1169
            var togglesBuilt = false;
            if (toggles is null || toggleHelps != (terminalHelp, changesHelp))
            {
                var terminalButton = terminalToggle = new ChromeIconButton("terminal", terminalHelp,
                    () => runtime.IsTerminalVisible = !runtime.IsTerminalVisible, // :1166
                    isActive: runtime.IsTerminalVisible);
                var changesButton = diffToggle = new ChromeIconButton("plusminus", changesHelp,
                    runtime.ToggleDiff, // :1170
                    isActive: runtime.IsDiffVisible);
                toggles = new ChromeCapsule(() =>
                {
                    var capsule = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
                    capsule.Children.Add(terminalButton);
                    capsule.Children.Add(new ChromeDivider()); // :1168
                    capsule.Children.Add(changesButton);
                    return capsule;
                }); // :1164-1172
                toggleHelps = (terminalHelp, changesHelp);
                togglesBuilt = true;
            }
            else
            {
                Transaction.WithAnimation(Chrome.PanelSlide, () => terminalToggle!.Update(runtime.IsTerminalVisible));
                Transaction.WithAnimation(Chrome.Hover, () => diffToggle!.Update(runtime.IsDiffVisible));
            }
            if (trailing.Children.Count == 0) { trailing.Children.Add(openCapsule); trailing.Children.Add(toggles); }
            else
            {
                trailing.Children[0] = openCapsule;
                if (togglesBuilt) trailing.Children[1] = toggles;
            }
            desired.Add(trailing);
            Reconcile(desired, transitioning, silent);
            built = true;

            // :1177-1180 padding follows ownership of the caption buttons through panelSlide frames. DEVIATION OD-0044: the
            // caption buttons are Windows ones at the window's top right, so nothing leads the row and the leading inset is the
            // padding whoever holds them; its trailing end keeps clear of them instead (WindowChrome.CaptionTrailingReserve).
            foreach (var group in host.Children.OfType<GlassEffectContainer>())
                group.Margin = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, WindowChrome.CaptionTrailingReserve, 0);
        }

        // :1165-1170 the terminal and changes buttons read runtime.isTerminalVisible/isDiffVisible directly inside
        // the SwiftUI body, so a toggle re-renders the whole row there; tracked here too so isActive (PR-TERM-072)
        // stays live rather than frozen at the value captured when this row was last built.
        this.Track(() => (model.Settings.SidebarFloats, model.Settings.SidebarOnlyFloats, model.Settings.HydraEnabled, model.Thread(runtime.ThreadID), git.IsRepository, git.Activity, git.Status, git.Branches, git.RemoteURL, runtime.IsTerminalVisible, runtime.IsDiffVisible, ShortcutStore.Hint(AppShortcut.NewThread), ShortcutStore.Hint(AppShortcut.ToggleTerminal), ShortcutStore.Hint(AppShortcut.ToggleChanges)), _ => Build());
        Build();
    }
}

/// <summary>:1184-1240 <c>private struct ChatChromeTitle: View</c>. A real Button so the extended title bar
/// (which drags the window from this row's gaps) never swallows the click.</summary>
internal sealed class ChatChromeTitle : Decorator
{
    private TextBlock titleText = new();
    private string currentTitle;
    private Panel? wordsHost; // the words on screen; a crossfade's leaving words sit in wordsLayer, over them, out of the layout.
    private Canvas? wordsLayer;
    private bool shownEmpty;

    // OD-0076 (content that swaps crossfades), ED-278 (14): the Mac's title Text takes new words with no animation
    // (ChatView.swift:1147-1151); the port fades the old words out and the new ones in on one ease-out of the Mac's motion language.
    private static Animation WordsSwap => Animation.EaseOut(duration: 0.2);

    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    // :1191-1192 the view's inputs; :1198-1239 body
    public ChatChromeTitle(ThreadRuntime runtime, string title)
    {
        currentTitle = title;
        ChatSheetPresentation.BuildWhenAttached(this, (model, _) => new ChatChromeTitle(model, runtime, currentTitle));
    }

    // :1185 @Environment(AppModel.self); explicit model overload for callers already holding the environment.
    public ChatChromeTitle(AppModel model, ThreadRuntime runtime, string title)
    {
        currentTitle = title;
        var state = new ViewState();
        var text = NewWords(title);
        titleText = text;
        shownEmpty = title.Length == 0;
        wordsLayer = new Canvas { IsHitTestVisible = false }; // no size of its own: the leaving words take no room
        wordsHost = new Panel { Children = { text, wordsLayer } };
        crossLayer = new Canvas { IsHitTestVisible = false };
        var crossHost = new Panel { Children = { wordsHost, crossLayer } }; // PORT-ONLY (ED-NEW-session-hotkeys): see PrepareCross
        var padded = new Border
        {
            Padding = new Thickness(0, 6), // :1208
            Background = Brushes.Transparent, // :1209 .contentShape(.rect)
            Child = crossHost,
        };
        var button = PlainControls.Button(padded, () => state.IsPresented = !state.IsPresented); // :1199-1202
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        // :1212 U+2014 em dash kept verbatim (deviation OD-0012 G3, as OD-0008 B14 keeps the other eight).
        ToolTip.SetTip(button, "Thread title — click to copy or rename");
        AutomationProperties.SetName(button, "Thread title"); // :1213
        // :1214 .accessibilityAddTraits(.isButton): a real Button already reports AutomationControlType.Button.
        var popover = PlainControls.Popover(
            button,
            new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
            () => new PopoverMenu(() => BuildMenu(model, runtime, button, currentTitle))); // :1215-1230
        // :1215-1217,1238 the button is the words' own width; the frame after it takes the row's leftover width, so the
        // popover hangs under the words, and the leftover gap stays the title bar's (it drags the window, ED-063).
        var pressable = PlainControls.Background(button, new NoWindowDragArea());
        pressable.HorizontalAlignment = HorizontalAlignment.Left;
        Child = new Panel { Children = { pressable, popover } };
        HorizontalAlignment = HorizontalAlignment.Stretch; // :1238 .frame(maxWidth: .infinity, alignment: .leading)
    }

    // :1203-1207 the words, their face and their colour.
    private static TextBlock NewWords(string title)
    {
        var text = new TextBlock
        {
            Text = title.Length == 0 ? "New thread" : title, // :1203
            MaxLines = 1, // :1206
            TextTrimming = TextTrimming.CharacterEllipsis, // :1207 .truncationMode(.tail)
        };
        SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold).Apply(text); // :1204
        text.Foreground = (title.Length == 0 ? Chrome.SecondaryText : Chrome.PrimaryText).Brush(text); // :1205
        return text;
    }

    // :1147-1151,1203-1205 a title input change: the Mac's Text takes the new words with no animation, and the button/popover state keeps
    // its identity. OD-0076 (owner-ruled deviation, the lead's decision for PR-CHAT-176 / UI-CHAT-076): on a drawn title the old words
    // fade out where they stand while the new ones fade in; Reduce Motion, or words not yet laid out, cut.
    internal void UpdateTitle(string title)
    {
        currentTitle = title;
        if (Child is ChatChromeTitle body) body.UpdateTitle(title);
        var empty = title.Length == 0;
        var shown = empty ? "New thread" : title;
        if (titleText.Text == shown && shownEmpty == empty) return; // the words on screen are these: a refresh that leaves the title as it is changes nothing
        if (wordsHost is null || wordsLayer is null || titleText.Bounds.Width <= 0
            || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion)
        {
            titleText.Text = shown;
            titleText.Foreground = (empty ? Chrome.SecondaryText : Chrome.PrimaryText).Brush(titleText);
            shownEmpty = empty;
            return;
        }
        var leaving = titleText;
        var arriving = NewWords(title);
        shownEmpty = empty;
        titleText = arriving;
        // The leaving words keep the width they were laid out at (so their own truncation) and take no room.
        leaving.Width = leaving.Bounds.Width;
        wordsHost.Children.Remove(leaving);
        wordsLayer.Children.Add(leaving);
        Transaction.WithAnimation(WordsSwap, () =>
        {
            SwiftTransition.RemoveContinuing(wordsLayer, leaving); // words still arriving leave from where they have got to
            SwiftTransition.Insert(wordsHost, 0, arriving);
        });
    }

    // PORT-ONLY (ED-NEW-session-hotkeys): a chat switch's title. The column opened lays the words of the chat left over its own
    // (in their own layer, at their own width and truncation) before it is laid out; the switch then crosses the two by
    // opacity alone, in step with the transcripts, so the header's words cross while its glass stays one and no frame lays
    // it out. At rest the layer is transparent and this title's words are whole.
    private Canvas? crossLayer; // no size of its own: the words left take no room
    private TextBlock? crossWords;
    private bool crossing;

    internal string Words => Child is ChatChromeTitle body ? body.Words : titleText.Text ?? "";

    internal (Control Leaving, Control Arriving)? Cross =>
        Child is ChatChromeTitle body ? body.Cross : crossing && crossWords is not null && wordsHost is not null ? (crossWords, wordsHost) : null;

    internal void PrepareCross(string previousTitle)
    {
        if (Child is ChatChromeTitle body)
        {
            body.PrepareCross(previousTitle);
            return;
        }
        EndCross();
        var empty = previousTitle.Length == 0;
        var shown = empty ? "New thread" : previousTitle;
        if (crossLayer is null || (shown == titleText.Text && empty == shownEmpty)) return; // the same words: nothing crosses
        crossWords ??= NewCrossWords(crossLayer);
        crossWords.Text = shown;
        crossWords.Foreground = (empty ? Chrome.SecondaryText : Chrome.PrimaryText).Brush(crossWords);
        crossWords.MaxWidth = Math.Max(0, Bounds.Width); // the title's whole lane, so they truncate as they did
        crossing = true;
    }

    internal void EndCross()
    {
        if (Child is ChatChromeTitle body)
        {
            body.EndCross();
            return;
        }
        crossing = false;
        if (crossWords is not null) crossWords.Opacity = 0;
        if (wordsHost is not null) wordsHost.Opacity = 1;
    }

    private static TextBlock NewCrossWords(Canvas layer)
    {
        var words = NewWords("");
        words.Opacity = 0;
        words.IsHitTestVisible = false;
        layer.Children.Add(words);
        return words;
    }

    // :1219-1229, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, ThreadRuntime runtime, Control anchor, string title)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new PopoverSectionHeader("Thread title")); // :1220
        stack.Children.Add(new PopoverItem("Copy title", () => CopyTitle(anchor, title), symbol: "doc.on.doc")); // :1221-1224
        stack.Children.Add(new PopoverItem("Rename…", () => ShowRenameAlert(model, runtime, title), symbol: "pencil")); // :1225-1228
        return stack;
    }

    // :1222-1223 clear then set (observable: a prior clipboard flavour does not survive).
    private static void CopyTitle(Control anchor, string title) => MainActor.Spawn(async () =>
    {
        var clipboard = TopLevel.GetTopLevel(anchor)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.ClearAsync();
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(title));
        await clipboard.SetDataAsync(data);
    });

    // :1231-1237. Reseed renameText from title (:1200, :1226); ED-216 supplies the shared field/roles/keyboard seam.
    private static void ShowRenameAlert(AppModel model, ThreadRuntime runtime, string title) => MainActor.Spawn(() =>
    {
        var renameText = title;
        return AlertPresentation.Show(new AlertRequest("Rename thread", null,
            [
                new AlertButton("Rename", () => model.Rename(runtime.ThreadID, renameText)), // :1233-1235
                new AlertButton("Cancel", () => { }) { Role = AlertButtonRole.Cancel }, // :1236
            ], TextFields: [new AlertTextField("Title", new Binding<string>(() => renameText, text => renameText = text))])); // :1232
    });
}

/// <summary>:1242-1265 <c>private struct ThreadsButton: View</c>, the toolbar's sidebar button. Its capture-frame hook (:1261-1263)
/// is not ported (ED-001: WebsiteCaptures.isEnabled is always false, porting/dead_code.md entry 44).
/// DEVIATION (ED-NEW-sidebarmodes-1): the Mac's press (:1251-1254) docks the column in Floating but, in Column, hangs a 560-pt
/// popover of the sidebar from the button (:1255-1259) and never shows or hides the column, against the Mac's own words for
/// Column ("the list button in the toolbar shows and hides it", AppSettings.swift:63; SettingsView.swift:445). Here the press
/// always shows or hides the window's sidebar column (SidebarLayout.Toggle, the Mac's own Floating press and its View >
/// Toggle sidebar), so Column does what its words say; the popover is gone. Its words follow the column on the same button.</summary>
internal sealed class ThreadsButton : Decorator
{
    // :1248-1264 body
    public ThreadsButton() => ChatSheetPresentation.BuildWhenAttached(this, (model, _) => new ThreadsButton(model));

    // :1243 @Environment(AppModel.self).
    public ThreadsButton(AppModel model)
    {
        var button = new ChromeCircleButton("list.bullet", Help(model), () => model.Sidebar.Toggle()); // :1249-1253
        Child = button;
        this.Track(() => Help(model), help => button.Help = help); // in place: no rebuild, no arrival, the hover kept
    }

    /// <summary>:1252 the button's words: what the press does now, with the Toggle sidebar chord (Ctrl+B unless remapped).</summary>
    internal static string Help(AppModel model) =>
        (model.Sidebar.IsVisible ? "Hide the sidebar" : "Show the sidebar") + ShortcutStore.Hint(AppShortcut.ToggleSidebar);
}

/// <summary>PORT-ONLY (ED-NEW-sidebarmodes-9): the toolbar's sidebar button on the window's empty pages (WelcomeView, NoThreadView), where
/// no thread is open and so no chat toolbar is drawn. Column's words promise "the list button in the toolbar shows and hides it"; on the
/// Mac the empty pages have the menu bar's View &gt; Toggle sidebar, and the port has no menu bar (OD-0008 C3), so a fresh install, or a
/// window whose last thread was archived, with the column collapsed had no button to bring the column out. The same button, in the same
/// place as the chat toolbar's first slot (14 from the leading edge, 12 from the top, ChatView.swift:1177-1178), on the same glass; it
/// leaves through .softAppear in Panel only, as the toolbar's does.</summary>
internal sealed class EmptyPageSidebarButton : Decorator
{
    public EmptyPageSidebarButton()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, 0, 0);
        var slot = new Panel();
        Child = new GlassEffectContainer(2, () => slot); // :1123 the row's own glass container
        ThreadsButton? button = null;
        var built = false;
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return !(model.Settings.SidebarFloats && model.Settings.SidebarOnlyFloats); // the toolbar's own rule (ChatChromeRow)
        }, shown =>
        {
            if (shown && button is null)
            {
                button = new ThreadsButton(AppEnvironment.GetAppModel(this)).Transition(ChatChromeRow.SoftAppear);
                if (built) SwiftTransition.Insert(slot, slot.Children.Count, button);
                else SwiftTransition.Place(slot, slot.Children.Count, button);
            }
            else if (!shown && button is { } leaving)
            {
                SwiftTransition.Remove(slot, leaving);
                button = null;
            }
            built = true;
        });
    }
}

/// <summary>:1267-1326 <c>@MainActor @Observable final class GitStatusModel</c> (R-OBS-6).</summary>
public sealed class GitStatusModel : MainActorModel
{
    private bool isRepository;
    private GitStatus? status;
    private ValueList<GitBranch> branches = ValueList<GitBranch>.Empty;
    private SwiftURL? remoteURL;
    private string? activity;

    // :1270 private(set) var isRepository = false
    public bool IsRepository { get => Read(isRepository); private set => Set(ref isRepository, value); }

    // :1271 private(set) var status: GitStatus?
    public GitStatus? Status { get => Read(status); private set => Set(ref status, value); }

    // :1272 private(set) var branches: [GitBranch] = []
    public ValueList<GitBranch> Branches { get => Read(branches); private set => Set(ref branches, value); }

    // :1273 private(set) var remoteURL: URL?
    public SwiftURL? RemoteURL { get => Read(remoteURL); private set => Set(ref remoteURL, value); }

    // :1274 private(set) var activity: String?
    public string? Activity { get => Read(activity); private set => Set(ref activity, value); }

    /// <summary>:1276-1282 <c>private struct Snapshot</c>.</summary>
    private sealed record Snapshot
    {
        public bool IsRepository { get; init; } = false; // :1277
        public GitStatus? Status { get; init; } // :1278
        public ValueList<GitBranch> Branches { get; init; } = ValueList<GitBranch>.Empty; // :1279
        public SwiftURL? RemoteURL { get; init; } // :1280
        public DateTimeOffset FetchedAt { get; init; } = SwiftDate.Now; // :1281
    }

    private const double Freshness = 30; // :1287

    // :1284-1286 The last read per directory, shared by every chat (a MainActor-isolated static: every caller
    // runs on main, so no lock). Switching threads shows the branch at once and only runs git again when the
    // read is stale, a turn changed files, or forced.
    private static readonly Dictionary<string, Snapshot> Snapshots = new(SwiftStringComparer.Instance);

    // :1289-1307 func refresh(_:force:) async
    public async Task Refresh(string directory, bool force = true)
    {
        if (Snapshots.TryGetValue(directory, out var cached)) // :1290
        {
            Apply(cached); // :1291
            if (!force && (SwiftDate.Now - cached.FetchedAt).TotalSeconds < Freshness) return; // :1292
        }
        var git = new Git(directory); // :1294
        var isRepository = await git.IsRepository(); // :1295
        var snapshot = new Snapshot { IsRepository = isRepository };
        if (snapshot.IsRepository) // :1296
        {
            // :1297-1299 async let status/branches/remoteURL: three concurrent children (R-TASK-5).
            await using var scope = new AsyncLetScope();
            var statusTask = scope.Start(git.Status);
            var branchesTask = scope.Start(git.Branches);
            var remoteURLTask = scope.Start(git.RemoteWebURL);
            snapshot = snapshot with
            {
                Status = await statusTask, // :1300
                Branches = new ValueList<GitBranch>(await branchesTask), // :1301
                RemoteURL = await remoteURLTask, // :1302
            };
        }
        snapshot = snapshot with { FetchedAt = SwiftDate.Now }; // :1304
        Snapshots[directory] = snapshot; // :1305
        Apply(snapshot); // :1306
    }

    // :1309-1314
    private void Apply(Snapshot snapshot)
    {
        IsRepository = snapshot.IsRepository;
        Status = snapshot.Status;
        Branches = snapshot.Branches;
        RemoteURL = snapshot.RemoteURL;
    }

    // :1316-1325 func perform(_:_:) async -> Error?
    public async Task<Exception?> Perform(string activity, Func<Task> work)
    {
        Activity = activity; // :1317
        try
        {
            await work(); // :1320
            return null; // :1321
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) // :1322-1323
        {
            return error;
        }
        finally
        {
            Activity = null; // :1318 defer
        }
    }
}

/// <summary>:1328-1343 <c>private struct PermissionMenu: View</c>.</summary>
internal sealed class PermissionMenu : Decorator
{
    // :1331 let thread; :1329 @Environment(AppModel.self), resolved when the original view input is attached.
    public PermissionMenu(ChatThread thread) => ChatSheetPresentation.BuildWhenAttached(this, (model, _) => new PermissionMenu(model, thread));

    // :1333-1342 explicit environment overload.
    public PermissionMenu(AppModel model, ChatThread thread)
    {
        var mode = thread.RuntimeMode;
        Child = new ChromeCircleMenu(mode.Symbol, $"{mode.Title} · {mode.Summary}", () => BuildMenu(model, thread)); // :1334
    }

    // :1335-1341, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, ChatThread thread)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new PopoverSectionHeader("Permissions")); // :1335
        foreach (var item in Enum.GetValues<RuntimeMode>()) // :1336 RuntimeMode.allCases, declaration order
        {
            var chosen = item;
            stack.Children.Add(new PopoverItem(
                chosen.Title,
                () => model.UpdateThread(thread.Id, current => current with { RuntimeMode = chosen }), // :1339-1340
                symbol: chosen.Symbol,
                isChecked: thread.RuntimeMode == chosen)); // :1337-1338
        }
        return stack;
    }
}

/// <summary>:1345-1409 <c>private struct BranchMenu: View</c>.</summary>
internal sealed class BranchMenu : Decorator
{
    // :1347-1349 original view inputs; :1346 inherited AppModel.
    public BranchMenu(ThreadRuntime runtime, string directory, GitStatusModel git) =>
        ChatSheetPresentation.BuildWhenAttached(this, (model, _) => new BranchMenu(model, runtime, directory, git));

    // :1354-1386 explicit environment overload.
    public BranchMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        var title = git.Activity ?? git.Status?.Branch ?? "Detached"; // :1357
        Child = new ChromeTextMenu("arrow.triangle.branch", title, "Branch", () => BuildMenu(model, runtime, directory, git)); // :1355-1359
    }

    // :1360-1379, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        // :1354-1378 the menu body reads runtime.isRunning, git.branches and the thread's worktree and turns, so an open
        // menu re-evaluates (a running turn disables every non-current branch) instead of keeping a stale snapshot.
        var host = new Decorator { Child = BuildItems(model, runtime, directory, git) };
        host.Track(() => (runtime.IsRunning, git.Branches, runtime.Thread?.WorktreePath, runtime.Turns.Count),
            _ => host.Child = BuildItems(model, runtime, directory, git));
        return host;
    }

    private static Control BuildItems(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new PopoverSectionHeader("Switch branch")); // :1360
        foreach (var branch in git.Branches.Take(25)) // :1361 .prefix(25)
        {
            var name = branch.Name;
            stack.Children.Add(new PopoverItem(
                name,
                () => SwitchBranch(model, git, directory, name), // :1367
                isChecked: branch.IsCurrent,
                isEnabled: !branch.IsCurrent && !runtime.IsRunning)); // :1362-1369
        }
        stack.Children.Add(new PopoverDivider()); // :1370
        stack.Children.Add(new PopoverItem("New branch…", () => // :1371-1374
            ShowNewBranchAlert(model, git, directory), symbol: "plus"));
        if (runtime.Thread?.WorktreePath is null && runtime.Turns.Count == 0) // :1375
        {
            stack.Children.Add(new PopoverItem("Use a new worktree for this thread", () => // :1376-1378
                MainActor.Spawn(() => model.CreateWorktree(runtime.ThreadID)), symbol: "square.stack.3d.up"));
        }
        return stack;
    }

    // :1381-1385
    private static void ShowNewBranchAlert(AppModel model, GitStatusModel git, string directory) => MainActor.Spawn(() =>
    {
        var branchName = ""; // :1372 reset before presenting.
        return AlertPresentation.Show(new AlertRequest("New branch", null,
            [
                new AlertButton("Create", () => CreateBranch(model, git, directory, branchName)), // :1383
                new AlertButton("Cancel", () => { }) { Role = AlertButtonRole.Cancel }, // :1384
            ], TextFields: [new AlertTextField("Branch name", new Binding<string>(() => branchName, text => branchName = text))])); // :1382
    });

    // :1388-1396
    private static void SwitchBranch(AppModel model, GitStatusModel git, string directory, string name) => MainActor.Spawn(async () =>
    {
        var repository = new Git(directory);
        var error = await git.Perform("Switching", () => repository.SwitchBranch(name));
        if (error is not null) model.Alert = new AppAlert { Title = "Could not switch branch", Message = SwiftErrors.LocalizedDescription(error) };
        await git.Refresh(directory);
    });

    // :1398-1408
    private static void CreateBranch(AppModel model, GitStatusModel git, string directory, string branchName)
    {
        var name = branchName.Trimmed().Replace(" ", "-", StringComparison.Ordinal); // :1399
        if (name.Length == 0) return; // :1400
        MainActor.Spawn(async () =>
        {
            var repository = new Git(directory);
            var error = await git.Perform("Creating", () => repository.CreateBranch(name));
            if (error is not null) model.Alert = new AppAlert { Title = "Could not create branch", Message = SwiftErrors.LocalizedDescription(error) };
            await git.Refresh(directory);
        });
    }
}

/// <summary>:1411-1432 <c>private struct OpenInMenu: View</c>.</summary>
internal sealed class OpenInMenu : Decorator
{
    // :1412 let directory; :1414-1431 body
    public OpenInMenu(string directory)
    {
        Child = new ChromeMenuButton("arrow.up.forward.app", "Open in another app", () => BuildMenu(directory)); // :1415
    }

    // :1416-1429, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(string directory)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new PopoverSectionHeader("Open in")); // :1416
        // :1417-1419 deviation OD-0012 G4: "Finder" -> "File Explorer". The row runs `NSWorkspace.shared.open(URL(fileURLWithPath: directory))`:
        // it OPENS the chat's directory (a window showing its contents), which is not Workspace.revealInFinder
        // (Components.swift:163-165, activateFileViewerSelecting: the parent folder with the directory selected). The UI layer's twin of
        // NSWorkspace.shared.open is ShellWorkspace.OpenWithDefaultApp (ShellExecuteW "open" opens a folder in File Explorer), as
        // DownloadsPopover's own open of the downloads folder uses it; RevealInFileExplorer stays for the source's revealInFinder sites.
        stack.Children.Add(new PopoverItem("File Explorer", () => ShellWorkspace.OpenWithDefaultApp(directory), image: Workspace.FileExplorerIcon as IImage));
        foreach (var editor in Workspace.InstalledEditors) // :1420-1424
            stack.Children.Add(new PopoverItem(editor.Name, () => Workspace.Open(directory, editor),
                symbol: "chevron.left.forwardslash.chevron.right", image: Workspace.Icon(editor) as IImage));
        stack.Children.Add(new PopoverDivider()); // :1425
        stack.Children.Add(new PopoverItem("Copy path", () => CopyPath(directory), symbol: "doc.on.doc")); // :1426-1429
        return stack;
    }

    // :1427-1428 clear then set.
    private static void CopyPath(string directory) => MainActor.Spawn(async () =>
    {
        var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime
            ? TopLevel.GetTopLevel(lifetime.MainWindow)?.Clipboard
            : null;
        if (clipboard is null) return;
        await clipboard.ClearAsync();
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(directory));
        await clipboard.SetDataAsync(data);
    });
}

/// <summary>:1434-1466 <c>private struct ScriptsMenu: View</c>.</summary>
internal sealed class ScriptsMenu : Decorator
{
    // :1436-1437 original view inputs; :1435 inherited AppModel.
    public ScriptsMenu(Project project, ThreadRuntime runtime) =>
        ChatSheetPresentation.BuildWhenAttached(this, (model, _) => new ScriptsMenu(model, project, runtime));

    // :1441-1465 explicit environment overload.
    public ScriptsMenu(AppModel model, Project project, ThreadRuntime runtime)
    {
        Child = new ChromeMenuButton("play", "Run a project script", () => BuildMenuCore(model, project, runtime, this)); // :1442
    }

    // :1443-1455, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, Project project, ThreadRuntime runtime)
        => BuildMenuCore(model, project, runtime, null);

    private static Control BuildMenuCore(AppModel model, Project project, ThreadRuntime runtime, Control? anchor)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        if (project.Scripts.Count > 0) // :1443
        {
            stack.Children.Add(new PopoverSectionHeader("Scripts")); // :1444
            foreach (var script in project.Scripts) // :1445
            {
                var chosen = script;
                stack.Children.Add(new PopoverItem(chosen.Name, () => model.RunScript(chosen, runtime.ThreadID), symbol: chosen.Symbol)); // :1446-1448
            }
            stack.Children.Add(new PopoverDivider()); // :1450
        }
        // :1452-1454,1456-1463 editor presentation through R-VIEW-9.
        stack.Children.Add(new PopoverItem(
            project.Scripts.Count == 0 ? "Add a script…" : "Edit scripts…",
            () => MainActor.Post(() => PresentScriptsEditor(model, project.Id, anchor ?? stack)),
            symbol: project.Scripts.Count == 0 ? "plus" : "pencil"));
        return stack;
    }

    private static void ShowScriptsEditor(AppModel model, Guid projectID) => MainActor.Post(() =>
        PresentScriptsEditor(model, projectID, null));

    // :1456-1463. `.presentationBackground { WindowBackdrop(opacity:) }`: glass over the app's own window (OD-0005 (b)), so the sheet is drawn
    // in its window and its backdrop's glass is the app's material (ED-333).
    private static void PresentScriptsEditor(AppModel model, Guid projectID, Control? anchor) => ChatSheetPresentation.Show(anchor, dismiss =>
    {
        var editor = new ScriptsEditor(model, projectID, dismiss).PresentedChrome();
        var backdrop = new WindowBackdrop(model.Settings.BackdropOpacity, overAppContent: true, roundedHairline: true); // clipped to its own 26 pt shape just below: its hairline follows that curve (ED-338)
        backdrop.Track(() => model.Settings.BackdropOpacity, opacity => backdrop.Update(opacity, null));
        return new Border { Child = new Panel { Children = { backdrop, editor } }, ClipToBounds = true }
            .RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);
    }, inWindow: true);
}

/// <summary>:1468-1528 <c>private struct GitActionsMenu: View</c>.</summary>
internal sealed class GitActionsMenu : Decorator
{
    // :1470-1472 original view inputs; :1469 inherited AppModel.
    public GitActionsMenu(ThreadRuntime runtime, string directory, GitStatusModel git) =>
        ChatSheetPresentation.BuildWhenAttached(this, (model, _) => new GitActionsMenu(model, runtime, directory, git));

    // :1476-1493 explicit environment overload.
    public GitActionsMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        var changed = git.Status?.ChangedFiles ?? 0; // :1477
        Child = new ChromeMenuButton(
            "arrow.triangle.pull",
            changed == 0 ? "Commit, push and open pull requests" : $"{changed} changed files", // :1478
            () => BuildMenuCore(model, runtime, directory, git, changed, this));
    }

    // :1479-1487, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git, long changed)
        => BuildMenuCore(model, runtime, directory, git, changed, null);

    private static Control BuildMenuCore(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git, long changed, Control? anchor)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new PopoverNote(changed == 1 ? "1 changed file" : $"{changed} changed files")); // :1479
        stack.Children.Add(new PopoverDivider()); // :1480
        stack.Children.Add(new PopoverItem("Commit…", () => MainActor.Post(() => PresentCommitSheet(model, runtime, directory, git, anchor ?? stack)), symbol: "checkmark.circle", isEnabled: changed > 0)); // :1481
        stack.Children.Add(new PopoverItem("Push", () => Push(model, git, directory), symbol: "arrow.up.circle")); // :1482
        stack.Children.Add(new PopoverItem("Create pull request", () => OpenPullRequest(model, git, directory), symbol: "arrow.triangle.pull")); // :1483
        if (git.RemoteURL is { } url) // :1484
        {
            stack.Children.Add(new PopoverDivider()); // :1485
            stack.Children.Add(new PopoverItem("Open repository", () => InlineText.OpenUrl?.Invoke(url), symbol: "safari")); // :1486
        }
        return stack;
    }

    // :1489-1492 sheet through R-VIEW-9's shared presentation seam.
    private static void ShowCommitSheet(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git) => MainActor.Post(() =>
        PresentCommitSheet(model, runtime, directory, git, null));

    private static void PresentCommitSheet(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git, Control? anchor) =>
        ChatSheetPresentation.Show(anchor, dismiss => new CommitSheet(model, runtime, directory, git, dismiss).PresentedChrome());

    // :1495-1503
    private static void Push(AppModel model, GitStatusModel git, string directory) => MainActor.Spawn(async () =>
    {
        var repository = new Git(directory);
        var error = await git.Perform("Pushing", repository.Push);
        if (error is not null) model.Alert = new AppAlert { Title = "Push failed", Message = SwiftErrors.LocalizedDescription(error) };
        await git.Refresh(directory);
    });

    // :1505-1527
    private static void OpenPullRequest(AppModel model, GitStatusModel git, string directory) => MainActor.Spawn(async () =>
    {
        var repository = new Git(directory);
        SwiftURL? url = null;
        var error = await git.Perform("Opening pull request", async () =>
        {
            await repository.Push(); // :1510
            var subject = await TryOutput(repository, ["log", "-1", "--pretty=%s"]); // :1511-1512
            var body = await TryOutput(repository, ["log", "-1", "--pretty=%b"]); // :1513-1514
            url = await repository.CreatePullRequest(
                title: subject.Length == 0 ? "Update" : subject,
                body: body.Length == 0 ? "Opened from nth." : body); // :1515-1518 deviation OD-0012 F1
        });
        if (error is not null) model.Alert = new AppAlert { Title = "Could not open a pull request", Message = SwiftErrors.LocalizedDescription(error) }; // :1520-1521
        else if (url is not null) InlineText.OpenUrl?.Invoke(url); // :1522-1523
        await git.Refresh(directory); // :1525
    });

    // :1511-1514 `(try? await ...)?.trimmed ?? ""`
    private static async Task<string> TryOutput(Git repository, IReadOnlyList<string> arguments)
    {
        try { return (await repository.Output(arguments)).Trimmed(); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return ""; }
    }
}

/// <summary>:1530-1650 <c>private struct CommitSheet: View</c>.</summary>
internal sealed class CommitSheet : Decorator
{
    private readonly AppModel model = null!;
    private readonly ThreadRuntime runtime;
    private readonly TextBox messageField = null!;
    private readonly Border messageFrame = null!;
    private readonly TextBlock messagePlaceholder = null!;
    private readonly TextBlock summaryBlock = null!;
    private readonly Border summaryFrame = null!;
    private readonly Button writeForMeButton = null!;
    private readonly SwiftImage writeForMeIcon = null!; // :1584-1587 sparkles, swapped for a spinner while generating
    private readonly ProgressBar writeForMeSpinner = null!; // :1582-1584 ProgressView().controlSize(.small)
    private readonly Button commitButton = null!;
    private readonly CheckBox pushToggle = null!;
    private bool isGenerating;
    private bool isCommitting;
    private string initialMessage = "";
    private bool initialPushes;
    private CommitSheet? InheritedBody => Child as CommitSheet;

    /// <summary>For tests: the field xUnit reads without needing a headless layout pass.</summary>
    public string Message
    {
        get => InheritedBody?.Message ?? messageField?.Text ?? initialMessage;
        set { if (InheritedBody is { } body) body.Message = value; else if (messageField is not null) messageField.Text = value; else initialMessage = value; }
    }

    public string Summary => InheritedBody?.Summary ?? summaryBlock?.Text ?? "";

    public bool IsGenerating => InheritedBody?.IsGenerating ?? isGenerating;

    public bool IsCommitting => InheritedBody?.IsCommitting ?? isCommitting;

    public bool PushesAfterCommit
    {
        get => InheritedBody?.PushesAfterCommit ?? (pushToggle?.IsChecked ?? initialPushes);
        set { if (InheritedBody is { } body) body.PushesAfterCommit = value; else if (pushToggle is not null) pushToggle.IsChecked = value; else initialPushes = value; }
    }

    public Button WriteForMeButton => InheritedBody?.WriteForMeButton ?? writeForMeButton;

    public Button CommitButton => InheritedBody?.CommitButton ?? commitButton;

    public Border SummaryFrame => InheritedBody?.SummaryFrame ?? summaryFrame;

    public Border MessageFrame => InheritedBody?.MessageFrame ?? messageFrame;

    // :1533-1535 original view inputs; :1531-1532 model/dismiss environments inherited on attachment.
    public CommitSheet(ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        this.runtime = runtime;
        Width = 560; // :1604
        ChatSheetPresentation.BuildWhenAttached(this, (model, dismiss) =>
            new CommitSheet(model, runtime, directory, git, dismiss) { Message = initialMessage, PushesAfterCommit = initialPushes });
    }

    // :1543-1610 explicit environment overload.
    public CommitSheet(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git, Action dismiss)
    {
        this.model = model;
        this.runtime = runtime;

        var heading = new TextBlock { Text = "Commit changes" }; // :1545
        SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold).Apply(heading); // :1546

        summaryBlock = new TextBlock { HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Left }; // :1549-1552
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced).Apply(summaryBlock); // :1550
        summaryFrame = new Border // :1547-1556
        {
            Child = new ScrollViewer { Content = summaryBlock, MaxHeight = 110 }, // :1548,1553
            Padding = new Thickness(10), // :1554
            IsVisible = false, // shown only once Load finds a non-empty summary
        }.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :1555
        summaryFrame.Background = Chrome.Overlay(0.05).Brush(summaryFrame);

        messageField = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "Commit message", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(8) }; // :1558-1563,1567
        ChatDialogControls.TextEditor(messageField, showsPrompt: false);
        SwiftFont.Body.Apply(messageField); // :1559
        messagePlaceholder = new TextBlock { Text = "Commit message", IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(13, 8) }; // :1562-1567.
        messagePlaceholder.Foreground = Chrome.SecondaryText.Brush(messagePlaceholder);
        SwiftFont.Default.Apply(messagePlaceholder); // :1563 no font of its own: SwiftUI's implicit default, the editor's 13 pt (ED-059) in the face's own line box (ED-290 (1)).
        messageFrame = new Border // :1557-1571
        {
            Child = new Panel { Children = { messageField, messagePlaceholder } },
            MinHeight = 130, // :1570
        }.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :1571
        messageFrame.Background = Chrome.Overlay(0.05).Brush(messageFrame);

        writeForMeIcon = SwiftImage.SystemName("sparkles"); // :1586
        writeForMeSpinner = new ProgressBar { IsIndeterminate = true, IsVisible = false }; // :1582-1584; native metrics STOP #5.
        writeForMeSpinner.SetValue(ChatDialogControls.InputStyleProperty, "SmallProgressView"); // :1584 .controlSize(.small)
        writeForMeButton = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { writeForMeSpinner, writeForMeIcon, new TextBlock { Text = "Write for me" } } } }; // :1578-1589
        pushToggle = new CheckBox { Content = "Push after committing" }; // :1593
        var cancelButton = new Button { Content = "Cancel" }; // :1596
        commitButton = new Button { Content = "Commit", IsEnabled = false }; // :1598-1600
        ChatDialogControls.Glass(writeForMeButton);
        ChatDialogControls.Checkbox(pushToggle);
        ChatDialogControls.Glass(cancelButton);
        cancelButton.IsCancel = true; // :1596 cancel role.
        ChatDialogControls.Glass(commitButton, prominent: true);

        writeForMeButton.Click += (_, _) => MainActor.Spawn(() => Generate(model, runtime, directory, null));
        cancelButton.Click += (_, _) => dismiss(); // :1596
        commitButton.Click += (_, _) => MainActor.Spawn(() => Commit(model, directory, git, dismiss));
        messageField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdateMessageState(); // :1562,1600
        };

        UpdateWriteForMeState();

        Child = new Border { Padding = new Thickness(20), Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, // :1544 .leading applies to content, not the width of the stack.
            Spacing = 14, // :1544
            Children =
            {
                heading,
                summaryFrame,
                messageFrame,
                ChatDialogControls.Footer(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { writeForMeButton, pushToggle } }, cancelButton, commitButton, spacing: 12),
            },
        } }; // :1603 .padding(20).
        Width = 560; // :1604

        // :1575,1591-1592. Refresh's observable Statuses write re-evaluates the engine and the help/disabled state.
        this.Track(() => (CanGenerate, model.Providers.Statuses), _ => UpdateWriteForMeState());
        this.Task(() => directory, value => Load(model, runtime, value)); // :1605-1609 .task, cancelled on disappearance.
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { dismiss(); e.Handled = true; } }; // :1596 cancel role, including popup presentation.
    }

    // :1605-1609
    private async Task Load(AppModel model, ThreadRuntime runtime, string directory)
    {
        var changes = await new Git(directory).WorkingChanges(); // :1606
        summaryBlock.Text = changes.Summary; // :1607
        summaryFrame.IsVisible = changes.Summary.Length > 0; // :1547 `if !summary.isEmpty`
        if (Message.Length == 0) await Generate(model, runtime, directory, changes); // :1608
    }

    // :1575 `let canGenerate = model.textEngine(preferring: runtime.thread?.provider) != nil`
    private bool CanGenerate => model.TextEngine(runtime.Thread?.Provider) is not null;

    // :1582-1592
    private void UpdateWriteForMeState()
    {
        writeForMeSpinner.IsVisible = isGenerating; // :1582-1584
        writeForMeIcon.IsVisible = !isGenerating; // :1585-1587
        writeForMeButton.IsEnabled = !isGenerating && CanGenerate; // :1591
        var canGenerate = CanGenerate;
        ToolTip.SetTip(writeForMeButton, canGenerate
            ? "Write a commit message from these changes"
            : "Turn on text generation in Settings > Source control"); // :1592
        messageField.PlaceholderText = isGenerating ? "Writing a message…" : "Commit message"; // :1563
        messagePlaceholder.Text = messageField.PlaceholderText;
    }

    // :1562,1600 SwiftUI re-evaluates both reads on any @State change.
    private void UpdateMessageState()
    {
        messagePlaceholder.IsVisible = Message.Length == 0;
        commitButton.IsEnabled = Message.Trimmed().Length > 0 && !isCommitting;
    }

    // :1612-1631
    private async Task Generate(AppModel model, ThreadRuntime runtime, string directory, (string Summary, string Patch)? changes)
    {
        if (model.TextEngine(runtime.Thread?.Provider) is not { } engine) return; // :1613
        isGenerating = true;
        UpdateWriteForMeState();
        try
        {
            var resolved = changes ?? await new Git(directory).WorkingChanges(); // :1616-1621
            if (await TextGeneration.CommitMessage(resolved.Summary, resolved.Patch, model.Settings.CommitInstructions, engine, directory) is { } text) // :1622-1628
            {
                Message = text; // :1629
            }
        }
        finally
        {
            isGenerating = false; // :1615 defer
            UpdateWriteForMeState();
        }
    }

    // :1633-1649
    private async Task Commit(AppModel model, string directory, GitStatusModel git, Action dismiss)
    {
        isCommitting = true;
        UpdateMessageState();
        try
        {
            var repository = new Git(directory);
            var text = Message.Trimmed(); // :1637
            var pushes = PushesAfterCommit; // :1638
            var error = await git.Perform("Committing", async () =>
            {
                await repository.CommitAll(text); // :1640
                if (pushes) await repository.Push(); // :1641
            });
            await git.Refresh(directory); // :1643
            if (error is not null) model.Alert = new AppAlert { Title = "Commit failed", Message = SwiftErrors.LocalizedDescription(error) }; // :1645
            else dismiss(); // :1647
        }
        finally
        {
            isCommitting = false; // :1635 defer
            UpdateMessageState();
        }
    }
}

/// <summary>:1652-1730 <c>private struct ScriptsEditor: View</c>.</summary>
internal sealed class ScriptsEditor : Decorator
{
    private readonly AppModel model = null!;
    private readonly Guid projectID;
    private readonly Action dismiss = null!;
    private readonly StackPanel rows = new() { Orientation = Orientation.Vertical, Spacing = 10 }; // :1667
    private readonly ForEach<ProjectScript, Guid> cards = null!;
    private readonly Dictionary<Guid, (TextBox Name, TextBox Command, CheckBox Worktree)> fields = [];
    private readonly Button saveButton = null!;
    private List<ProjectScript> scripts = [];

    // :1655 original view input; :1653-1654 inherited model/dismiss environments.
    public ScriptsEditor(Guid projectID)
    {
        this.projectID = projectID;
        Width = 580; Height = 460; // :1722
        ChatSheetPresentation.BuildWhenAttached(this, (model, dismiss) => new ScriptsEditor(model, projectID, dismiss));
    }

    // :1659-1724 explicit environment overload.
    public ScriptsEditor(AppModel model, Guid projectID, Action dismiss)
    {
        this.model = model;
        this.projectID = projectID;
        this.dismiss = dismiss;
        cards = new ForEach<ProjectScript, Guid>(rows, script => script.Id, BuildCard, (_, script) =>
        {
            var field = fields[script.Id];
            field.Name.Text = script.Name;
            field.Command.Text = script.Command;
            field.Worktree.IsChecked = script.RunOnWorktreeCreate;
        }); // :1668 ForEach($scripts), identity survives array changes and appearance.
        this.Font(SwiftFont.Default); // SwiftUI's implicit default (no .font above :1660), ED-059's 13 pt in the face's own line box (ED-290 (1)).

        var heading = new TextBlock { Text = "Project scripts" }; // :1661
        SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold).Apply(heading); // :1662
        var explanation = new TextBlock
        {
            // :1663 DEVIATION OD-0012 F1: "Droppy Code" -> "Marrow"; "droppy-code.json" kept verbatim (OD-0008 B13).
            Text = "Scripts run in a terminal at the project root. nth also picks up scripts from droppy-code.json.",
        };
        SwiftFont.Callout.Apply(explanation); // :1664
        explanation.Foreground = Chrome.SecondaryText.Brush(explanation); // :1665

        var scroll = new ScrollViewer { Content = rows, MinHeight = 220 }; // :1666,1700

        var addButton = new Button { Content = "Add script" }; // :1701-1707
        ChatDialogControls.SetSymbol(addButton, "plus"); // :1705.
        ChatDialogControls.Glass(addButton);
        addButton.Click += (_, _) =>
        {
            scripts = [.. scripts, new ProjectScript("New script", "")]; // :1703
            Rebuild();
        };
        var cancelButton = new Button { Content = "Cancel" }; // :1709
        ChatDialogControls.Glass(cancelButton);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => dismiss();
        saveButton = new Button { Content = "Save" }; // :1712-1717
        ChatDialogControls.Glass(saveButton, prominent: true);
        saveButton.Click += (_, _) =>
        {
            model.UpdateProject(projectID, p => p with { Scripts = new ValueList<ProjectScript>(scripts) }); // :1713
            dismiss(); // :1714
        };
        var footer = ChatDialogControls.Footer(addButton, cancelButton, saveButton); // :1708 Spacer().

        Child = new Border { Padding = new Thickness(20), Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, // :1660 leading content; the stack fills the fixed frame.
            Spacing = 14, // :1660
            Children = { heading, explanation, scroll, footer },
        } }; // :1721 .padding(20), inside the fixed frame.
        Width = 580; // :1722
        Height = 460;

        Load(); // :1723 .onAppear
        AttachedToVisualTree += (_, _) => Load(); // :1723 reset the local copy on every appearance.
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { dismiss(); e.Handled = true; } }; // :1709 cancel role.
    }

    /// <summary>For tests: the editor's current local copy (never the project's own array).</summary>
    internal IReadOnlyList<ProjectScript> Scripts => scripts;

    internal Button SaveButton => saveButton;

    // :1723 .onAppear { scripts = model.project(projectID)?.scripts ?? [] }
    private void Load()
    {
        scripts = model.Project(projectID) is { } project ? [.. project.Scripts] : [];
        Rebuild();
    }

    private void Rebuild()
    {
        cards.Apply(scripts.ToArray());
        foreach (var id in fields.Keys.Where(id => !scripts.Any(script => script.Id == id)).ToArray()) fields.Remove(id);
        UpdateSaveState();
    }

    // :1726-1729. Literally trimmingCharacters(in: .whitespaces), the already-verified FoundationText.Trim with
    // includeNewlines: false (T-M2-023): tabs and Unicode space separators are stripped, newlines are not.
    private static bool IsIncomplete(ProjectScript script) => FoundationText.Trim(script.Command).Length == 0;

    private void UpdateSaveState()
    {
        var incomplete = scripts.Any(IsIncomplete); // :1711
        saveButton.IsEnabled = !incomplete; // :1717
        ToolTip.SetTip(saveButton, incomplete ? "Every script needs a command." : "Save these scripts"); // :1718
    }

    // :1669-1696
    private Control BuildCard(ProjectScript script)
    {
        var nameField = new TextBox { Text = script.Name, PlaceholderText = "Name" }; // :1672-1673
        ChatDialogControls.TextEditor(nameField, style: "RoundedBorder"); // :1673, STOP #5 native bezel metrics.
        nameField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Update(script.Id, s => s with { Name = nameField.Text ?? "" });
        };
        var trashIcon = SwiftImage.SystemName("trash");
        TextElement.SetForeground(trashIcon, Chrome.Danger.Brush(trashIcon)); // :1674 destructive role.
        var trash = PlainControls.Button(trashIcon, () => { }); // :1674-1679 borderless template.
        trash.Click += (_, _) =>
        {
            scripts = [.. scripts.Where(s => s.Id != script.Id)]; // :1675
            Rebuild();
        };
        // :1671 HStack { TextField, Button }: the roundedBorder field is flexible and takes what the borderless button leaves of the row, 8 points between them
        // (SwiftUI's default HStack spacing between a field and a button; its value on the Mac is not in the source: ENGINEERING DECISION ED-290-2, UNVERIFIED), both on one centre line.
        trash.Margin = new Thickness(8, 0, 0, 0);
        trash.VerticalAlignment = VerticalAlignment.Center;
        nameField.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(trash, 1);
        top.Children.Add(nameField);
        top.Children.Add(trash);

        var commandField = new TextBox { Text = script.Command, PlaceholderText = "Command" }; // :1681-1682
        ChatDialogControls.TextEditor(commandField, style: "RoundedBorder"); // :1682, STOP #5.
        SwiftFont.System(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced).Apply(commandField); // :1683 (Cascadia Mono, OD-0009)
        // :1686-1691 `.overlay { if incomplete { RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Chrome.danger.opacity(0.65), lineWidth: 1) } }`:
        // the outline is drawn over the field and takes no room, so the field keeps its size whether or not it shows.
        var outline = new SwiftShapeBorder { BorderThickness = new Thickness(1), IsHitTestVisible = false }.RoundedRectangle(6, RoundedCornerStyle.Continuous); // :1688
        var commandFrame = new Panel { Children = { commandField, outline } };
        void UpdateOutline() =>
            outline.BorderBrush = IsIncomplete(scripts.FirstOrDefault(s => s.Id == script.Id) ?? script)
                ? Chrome.Danger.Opacity(0.65).Brush(outline) : null; // :1687,1689
        UpdateOutline();
        commandField.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            Update(script.Id, s => s with { Command = commandField.Text ?? "" }); // :1681 updates only this field of the binding.
            UpdateOutline();
            UpdateSaveState();
        };

        var worktreeToggle = new CheckBox { Content = "Run when a worktree is created", IsChecked = script.RunOnWorktreeCreate }; // :1692-1693
        ChatDialogControls.Checkbox(worktreeToggle);
        fields[script.Id] = (nameField, commandField, worktreeToggle);
        worktreeToggle.IsCheckedChanged += (_, _) => Update(script.Id, s => s with { RunOnWorktreeCreate = worktreeToggle.IsChecked == true });

        var body = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, // :1670 VStack(alignment: .leading) inside a card that fills the scroll area: its fields are flexible and take the card's width
            Spacing = 8, // :1670
            Children = { top, commandFrame, worktreeToggle },
            Margin = new Thickness(14), // :1695
        };
        return new ChromeCard(() => body); // :1669
    }

    private void Update(Guid id, Func<ProjectScript, ProjectScript> change) =>
        scripts = [.. scripts.Select(s => s.Id == id ? change(s) : s)];
}
