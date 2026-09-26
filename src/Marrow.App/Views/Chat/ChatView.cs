using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Sidebar;
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

// Source: DroppyCode/App/Views/Chat/ChatView.swift (B2 1fbf81e), :1-1730.
// Skeleton (c100 group 2): every type the file declares, private and nested ones included, the views' inputs, the
// records' shapes and the members the other cycle files use. Every view body and every member with behaviour is left to
// the implementing unit (MARROW_STUB), as are ChatView's private members (:48-65, :326-710), PanelScene's private
// flippedVertically (:1075-1082), GitStatusModel's private snapshot cache (:1286-1287) and apply (:1309-1314), and the
// private views' private members.
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

    // :7 @Bindable var runtime: ThreadRuntime; :76-321 body
    public ChatView(ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:76");

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

    // :36-52 private var isMovingAPanel: Bool. Extracted as a static helper so it is directly testable ahead of
    // ChatView's own body, which is not built here: it composes ThreadTimeline, TerminalPanel and ComposerView,
    // all still TODO units (see the CLOUD REPORT open points). ChatView's real body will call this with its own
    // @State drag fields once those land.
    internal static bool IsMovingAPanel(
        PanelDragState subagentDrag,
        PanelDragState hydraDrag,
        PanelDragState poppedDrag,
        PanelDragState usageDrag,
        IReadOnlyDictionary<Guid, PanelDragState> autoDrags) => // :49-51
        subagentDrag.Position is not null || hydraDrag.Position is not null
            || poppedDrag.Position is not null || usageDrag.Position is not null
            || autoDrags.Values.Any(drag => drag.Position is not null);

    /// <summary>:399-417 <c>private struct HydraCaptureFrame: ViewModifier</c>. The capture run it feeds is not ported
    /// (ED-001: WebsiteCaptures.isEnabled is always false, porting/dead_code.md entry 44).</summary>
    private sealed record HydraCaptureFrame(Guid ThreadID, WindowLiveResize LiveResize)
    {
        // :407-416 func body(content:)
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:407");
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
    private readonly TranslateTransform transform = new();
    private double offset; // :745 @State private var offset: CGFloat = 0
    private PanelReserve? previous;

    /// <summary>For tests: the current glide offset (:756's <c>offset(x:)</c>).</summary>
    internal double Offset => offset;

    // :747-770 func body(content:). The caller must reuse the same ReserveSlide instance across reserve changes
    // for the glide to track correctly, matching how @State persists across body re-evaluations by view identity.
    public Control Body(Control content)
    {
        if (previous is { } old && old != Reserve) // :757 onChange(of: reserve)
        {
            var shift = ((Reserve.Leading - Reserve.Trailing) - (old.Leading - old.Trailing)) / 2; // :760
            if (Slides && !AnimatesWidth && shift != 0) // :761
            {
                offset -= shift; // :762-767 an immediate, unanimated snap: "start from where it was"
                transform.X = offset;
                // :768 withAnimation(Chrome.panelSlide) { offset = 0 }. UNVERIFIED: settles at the animation's
                // duration rather than interpolating every frame in between (no Thickness/transform animation
                // primitive exists in this tree yet; only AnimatedColor does, Marrow.UI/Port/PlainControls.cs:185).
                var duration = TimeSpan.FromSeconds(Chrome.PanelSlide.Duration);
                MainActor.Spawn(async () =>
                {
                    await SwiftTask.TrySleep(duration);
                    offset = 0;
                    transform.X = 0;
                });
            }
            else
            {
                offset = 0; // :762 guard slides, !animatesWidth, shift != 0 else { offset = 0 }
                transform.X = 0;
            }
        }
        previous = Reserve;

        ChatView.SetReserveSlideLeading(content, Reserve.Leading); // :749
        ChatView.SetReserveSlideOffset(content, offset); // :750
        content.Margin = new Thickness(Reserve.Leading, 0, Reserve.Trailing, 0); // :751-752 one discrete step,
        // never an intermediate width: the source's own point of this modifier (PR-CHAT-158).
        content.RenderTransform = transform; // :756
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
    public PanelMembers(ThreadRuntime runtime, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:787");
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
    public PanelDockSide? LockedSide(FloatingPanelID excluding) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:849");

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
    public bool HasHeads =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:875");

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
        double? usageHeightOverride = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:879");

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
        double? usageHeightOverride = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:925");

    // :1053-1073 static func order(_:by:) -> [FloatingPanelID: (corner: PanelDockCorner, below: Int)]
    public static ValueMap<FloatingPanelID, (PanelDockCorner Corner, long Below)> Order(
        IReadOnlyList<(FloatingPanelID Id, PanelDockCorner Corner)> placed,
        IReadOnlyList<FloatingPanelID> by) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1053");
}

/// <summary>:1085-1104 <c>struct JumpToLatestButton: View</c>.</summary>
public sealed class JumpToLatestButton : Decorator
{
    // :1087 let scrollState; :1089-1103 body
    public JumpToLatestButton(TimelineScrollState scrollState) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1089");
}

/// <summary>PORT-ONLY. <c>GlassEffectContainer(spacing:)</c> (ChatView.swift:1123): on macOS 26 it groups nearby
/// glass shapes so they sample and morph as one compositor pass instead of each drawing its own. Each control
/// inside already carries its own glass (Chrome.ChromeGlassCapsule/ChromeGlassCircle), so grouping changes only
/// the compositor's cost, never what is drawn. MARROW_DEFERRED ED-180: this container does not merge sampling
/// passes; it lays out its content unchanged. Visual parity is UNVERIFIED without macOS (OD-0001).</summary>
internal sealed class GlassEffectContainer : Decorator
{
    public GlassEffectContainer(double spacing, Func<Control> content)
    {
        _ = spacing;
        Child = content();
    }
}

/// <summary>:1106-1182 <c>private struct ChatChromeRow: View</c>: the sticky glass controls across the top.</summary>
internal sealed class ChatChromeRow : Decorator
{
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

        void Build()
        {
            host.Children.Clear();
            var sidebarVisible = model.Sidebar.HoldsTrafficLights; // :1119
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center }; // :1124

            if (!sidebarVisible && !(model.Settings.SidebarFloats && model.Settings.SidebarOnlyFloats)) // :1128
            {
                row.Children.Add(new ThreadsButton(model)); // :1129-1131
            }
            row.Children.Add(new ChromeCircleButton(
                "square.and.pencil",
                "New thread" + ShortcutStore.Hint(AppShortcut.NewThread), // :1132
                () => model.NewThread(project))); // :1133

            if (model.Thread(runtime.ThreadID) is { } thread) // :1135
            {
                if (model.Settings.HydraEnabled && !thread.IsHelper) // :1138
                {
                    row.Children.Add(new HydraButton(thread, runtime)); // :1139-1140
                }
                row.Children.Add(new PermissionMenu(model, thread)); // :1142
            }
            if (git.IsRepository) // :1144
            {
                row.Children.Add(new BranchMenu(model, runtime, directory, git)); // :1145
            }
            row.Children.Add(new ChatChromeTitle(model, runtime, title)); // :1147-1151. Rebuilt (not animated) on
            // every title change because Build() runs again; the source disables crossfade explicitly (:1151),
            // and a freshly constructed control never crossfades with its predecessor.

            var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :1152
            trailing.Children.Add(new ChromeCapsule(() =>
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
            })); // :1153-1163
            trailing.Children.Add(new ChromeCapsule(() =>
            {
                var capsule = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
                capsule.Children.Add(new ChromeIconButton(
                    "terminal",
                    "Terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal), // :1165
                    () => runtime.IsTerminalVisible = !runtime.IsTerminalVisible, // :1166
                    isActive: runtime.IsTerminalVisible));
                capsule.Children.Add(new ChromeDivider()); // :1168
                capsule.Children.Add(new ChromeIconButton(
                    "plusminus",
                    "Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges), // :1169
                    runtime.ToggleDiff, // :1170
                    isActive: runtime.IsDiffVisible));
                return capsule;
            })); // :1164-1172
            row.Children.Add(trailing);

            var container = new GlassEffectContainer(2, () => row); // :1123
            // :1177-1178 the leading inset stacks on top of the row's own horizontal padding (SwiftUI applies each
            // .padding from the inside out); UNVERIFIED O: whether :1180's Chrome.panelSlide animates this margin
            // smoothly is not reproduced (no Thickness/margin transition primitive exists in this tree yet; only
            // AnimatedColor does, PlainControls.cs:185) — the value itself is correct at rest and on every rebuild,
            // only the in-between frames of the slide are not.
            container.Margin = new Thickness(
                Chrome.ChromeHorizontalPadding + (sidebarVisible ? 0 : Chrome.TrafficLightsWidth + Chrome.TrafficLightClearance),
                Chrome.ChromeTopPadding, // :1179
                Chrome.ChromeHorizontalPadding, // :1178
                0);
            host.Children.Add(container);
        }

        this.Track(() => (model.Sidebar.HoldsTrafficLights, model.Thread(runtime.ThreadID), git.IsRepository, git.Activity, git.Status, git.Branches, git.RemoteURL), _ => Build());
        Build();
    }
}

/// <summary>:1184-1240 <c>private struct ChatChromeTitle: View</c>. A real Button so the extended title bar
/// (which drags the window from this row's gaps) never swallows the click.</summary>
internal sealed class ChatChromeTitle : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    // :1191-1192 the view's inputs; :1198-1239 body
    public ChatChromeTitle(AppModel model, ThreadRuntime runtime, string title)
    {
        var state = new ViewState();
        var text = new TextBlock
        {
            Text = title.Length == 0 ? "New thread" : title, // :1203
            MaxLines = 1, // :1206
            TextTrimming = TextTrimming.CharacterEllipsis, // :1207 .truncationMode(.tail)
        };
        SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold).Apply(text); // :1204
        text.Foreground = (title.Length == 0 ? Chrome.SecondaryText : Chrome.PrimaryText).Brush(text); // :1205
        var padded = new Border
        {
            Padding = new Thickness(0, 6), // :1208
            Background = Brushes.Transparent, // :1209 .contentShape(.rect)
            Child = text,
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
            () => new PopoverMenu(() => BuildMenu(model, runtime, button, title))); // :1215-1230
        Child = PlainControls.Background(new Panel { Children = { button, popover } }, new NoWindowDragArea());
        HorizontalAlignment = HorizontalAlignment.Stretch; // :1238 .frame(maxWidth: .infinity, alignment: .leading)
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

    // :1231-1236. renameText is always reseeded from title right before use (:1200, :1226), so the alert's
    // initial text is just title.
    private static void ShowRenameAlert(AppModel model, ThreadRuntime runtime, string title) => MainActor.Spawn(() =>
        PromptPresentation.Presenter.Current.Show(
            "Rename thread",
            "Title",
            title,
            [
                new PromptButton("Rename", IsCancelRole: false, text => model.Rename(runtime.ThreadID, text)), // :1233-1235
                new PromptButton("Cancel", IsCancelRole: true, _ => { }), // :1236
            ]));
}

/// <summary>:1242-1265 <c>private struct ThreadsButton: View</c>. Its capture-frame hook (:1261-1263) is not ported
/// (ED-001: WebsiteCaptures.isEnabled is always false, porting/dead_code.md entry 44).</summary>
internal sealed class ThreadsButton : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    // :1248-1264 body
    public ThreadsButton(AppModel model)
    {
        var state = new ViewState();
        // :1251 let floats = model.settings.sidebarFloats
        var floats = model.Settings.SidebarFloats;
        var button = new ChromeCircleButton(
            "list.bullet",
            floats ? "Show the sidebar" : "Threads", // :1252
            () =>
            {
                if (floats) model.Sidebar.Toggle(); // :1253
                else state.IsPresented = !state.IsPresented;
            });
        var popover = PlainControls.Popover(
            button,
            new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
            () =>
            {
                var sidebar = new SidebarView(inPopover: true, dismiss: () => state.IsPresented = false); // :1256
                sidebar.Width = model.Sidebar.Width; // :1257
                sidebar.Height = 560;
                return sidebar.PresentedChrome(); // :1258
            });
        Child = new Panel { Children = { button, popover } };
        // :1260-1263 the capture-frame publish (WebsiteCaptures.threadsButtonFrame) is not ported (ED-001).
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
    // :1331 let thread; :1333-1342 body. AppModel added explicitly (no Avalonia counterpart of @Environment).
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
    // :1347-1349 the view's inputs; :1354-1386 body. AppModel added explicitly (no Avalonia @Environment).
    public BranchMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        var title = git.Activity ?? git.Status?.Branch ?? "Detached"; // :1357
        Child = new ChromeTextMenu("arrow.triangle.branch", title, "Branch", () => BuildMenu(model, runtime, directory, git)); // :1355-1359
    }

    // :1360-1379, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
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
        PromptPresentation.Presenter.Current.Show(
            "New branch",
            "Branch name",
            "",
            [
                new PromptButton("Create", IsCancelRole: false, name => CreateBranch(model, git, directory, name)), // :1383
                new PromptButton("Cancel", IsCancelRole: true, _ => { }), // :1384
            ]));

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
        // :1417-1419 deviation OD-0012 G4: "Finder" -> "File Explorer", opening the directory in it. The icon
        // and the installed-editor catalogue (Workspace.FileExplorerIcon, Workspace.InstalledEditors,
        // Workspace.Icon, PR-CHAT-181/182) are BLOCKED on an owner decision (UI/Common/Components.cs); this
        // menu still opens correctly, only its icon and the editor rows are missing until that lands.
        stack.Children.Add(new PopoverItem("File Explorer", () => Workspace.RevealInFileExplorer(directory)));
        try
        {
            foreach (var editor in Workspace.InstalledEditors) // :1420-1424
            {
                var chosen = editor;
                stack.Children.Add(new PopoverItem(chosen.Name, () => Workspace.Open(directory, chosen), symbol: "chevron.left.forwardslash.chevron.right"));
            }
        }
        catch (NotImplementedException)
        {
            // BLOCKED PR-CHAT-181/182: no editor catalogue yet (see Workspace.InstalledEditors).
        }
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
    // :1436-1437 the view's inputs; :1441-1465 body. AppModel added explicitly (no Avalonia @Environment).
    public ScriptsMenu(AppModel model, Project project, ThreadRuntime runtime)
    {
        Child = new ChromeMenuButton("play", "Run a project script", () => BuildMenu(model, project, runtime)); // :1442
    }

    // :1443-1455, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, Project project, ThreadRuntime runtime)
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
        // :1452-1454 the editor sheet itself (ScriptsEditor) is not part of this job's records; opening it
        // still needs that unit's own body, so this only reaches the still-stubbed ScriptsEditor.
        stack.Children.Add(new PopoverItem(
            project.Scripts.Count == 0 ? "Add a script…" : "Edit scripts…",
            () => { },
            symbol: project.Scripts.Count == 0 ? "plus" : "pencil"));
        return stack;
    }
}

/// <summary>:1468-1528 <c>private struct GitActionsMenu: View</c>.</summary>
internal sealed class GitActionsMenu : Decorator
{
    // :1470-1472 the view's inputs; :1476-1493 body. AppModel added explicitly (no Avalonia @Environment).
    public GitActionsMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git)
    {
        var changed = git.Status?.ChangedFiles ?? 0; // :1477
        Child = new ChromeMenuButton(
            "arrow.triangle.pull",
            changed == 0 ? "Commit, push and open pull requests" : $"{changed} changed files", // :1478
            () => BuildMenu(model, runtime, directory, git, changed));
    }

    // :1479-1487, extracted so a test can inspect the menu without a real popup (headless cannot open one).
    internal static Control BuildMenu(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git, long changed)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        stack.Children.Add(new PopoverNote(changed == 1 ? "1 changed file" : $"{changed} changed files")); // :1479
        stack.Children.Add(new PopoverDivider()); // :1480
        stack.Children.Add(new PopoverItem("Commit…", () => ShowCommitSheet(model, runtime, directory, git), symbol: "checkmark.circle", isEnabled: changed > 0)); // :1481
        stack.Children.Add(new PopoverItem("Push", () => Push(model, git, directory), symbol: "arrow.up.circle")); // :1482
        stack.Children.Add(new PopoverItem("Create pull request", () => OpenPullRequest(model, git, directory), symbol: "arrow.triangle.pull")); // :1483
        if (git.RemoteURL is { } url) // :1484
        {
            stack.Children.Add(new PopoverDivider()); // :1485
            stack.Children.Add(new PopoverItem("Open repository", () => InlineText.OpenUrl?.Invoke(url), symbol: "safari")); // :1486
        }
        return stack;
    }

    // :1489-1492 `.sheet(isPresented: $isCommitting) { CommitSheet(...).presentedChrome() }`. No generic `.sheet`
    // primitive exists yet in this tree (only `.popover` and the AlertPresenter/PromptPresenter modal-window
    // seam do), so this opens the same kind of plain modal window those use.
    private static void ShowCommitSheet(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git) => MainActor.Post(() =>
    {
        var window = new Avalonia.Controls.Window
        {
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            Width = 560, // :1604 .frame(width: 560)
            CanResize = false,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
        };
        var sheet = new CommitSheet(model, runtime, directory, git, window.Close);
        window.Content = sheet.PresentedChrome();
        window.Show();
    });

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
                body: body.Length == 0 ? "Opened from Marrow." : body); // :1515-1518 deviation OD-0012 F1
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

/// <summary>:1530-1650 <c>private struct CommitSheet: View</c>. The exact visual layout (spacing, fonts, the
/// monospaced summary box) is a UI-GIT-* record for a later part; this wires the controls PR-GIT-129/130/131
/// need to observe and drive: the message field, the summary, the generate button and the commit button.</summary>
internal sealed class CommitSheet : Decorator
{
    private readonly TextBox messageField;
    private readonly TextBlock summaryBlock;
    private readonly Button writeForMeButton;
    private readonly Button commitButton;
    private readonly CheckBox pushToggle;
    private bool isGenerating;
    private bool isCommitting;

    /// <summary>For tests: the field xUnit reads without needing a headless layout pass.</summary>
    public string Message { get => messageField.Text ?? ""; set => messageField.Text = value; }

    public string Summary => summaryBlock.Text ?? "";

    public bool IsGenerating => isGenerating;

    public bool IsCommitting => isCommitting;

    public bool PushesAfterCommit { get => pushToggle.IsChecked == true; set => pushToggle.IsChecked = value; }

    // :1533-1535 the view's inputs, plus AppModel and dismiss explicitly (no Avalonia @Environment or `.dismiss`).
    public CommitSheet(AppModel model, ThreadRuntime runtime, string directory, GitStatusModel git, Action dismiss)
    {
        messageField = new TextBox { AcceptsReturn = true, PlaceholderText = "Commit message" }; // :1558-1563
        summaryBlock = new TextBlock { IsVisible = false }; // :1547-1556 shown only when non-empty
        writeForMeButton = new Button { Content = "Write for me" }; // :1576-1592
        pushToggle = new CheckBox { Content = "Push after committing" }; // :1593
        var cancelButton = new Button { Content = "Cancel" }; // :1596
        commitButton = new Button { Content = "Commit", IsEnabled = false }; // :1598-1600

        writeForMeButton.Click += (_, _) => MainActor.Spawn(() => Generate(model, runtime, directory, null));
        cancelButton.Click += (_, _) => dismiss(); // :1596
        commitButton.Click += (_, _) => MainActor.Spawn(() => Commit(model, directory, git, dismiss));
        messageField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) commitButton.IsEnabled = Message.Trimmed().Length > 0 && !isCommitting; // :1600
        };

        Child = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                summaryBlock,
                messageField,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { writeForMeButton, pushToggle, cancelButton, commitButton } },
            },
        };

        MainActor.Spawn(() => Load(model, runtime, directory)); // :1605-1609 .task
    }

    // :1605-1609
    private async Task Load(AppModel model, ThreadRuntime runtime, string directory)
    {
        var changes = await new Git(directory).WorkingChanges(); // :1606
        summaryBlock.Text = changes.Summary; // :1607
        summaryBlock.IsVisible = changes.Summary.Length > 0; // :1547 `if !summary.isEmpty`
        if (Message.Length == 0) await Generate(model, runtime, directory, changes); // :1608
    }

    // :1612-1631
    private async Task Generate(AppModel model, ThreadRuntime runtime, string directory, (string Summary, string Patch)? changes)
    {
        if (model.TextEngine(runtime.Thread?.Provider) is not { } engine) return; // :1613
        isGenerating = true;
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
        }
    }

    // :1633-1649
    private async Task Commit(AppModel model, string directory, GitStatusModel git, Action dismiss)
    {
        isCommitting = true;
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
        }
    }
}

/// <summary>:1652-1730 <c>private struct ScriptsEditor: View</c>.</summary>
internal sealed class ScriptsEditor : Decorator
{
    // :1655 let projectID; :1659-1724 body
    public ScriptsEditor(Guid projectID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1659");
}
