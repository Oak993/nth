using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
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
    // :747-770 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:747");
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

/// <summary>:1106-1182 <c>private struct ChatChromeRow: View</c>: the sticky glass controls across the top.</summary>
internal sealed class ChatChromeRow : Decorator
{
    // :1109-1114 the view's inputs; :1116-1181 body
    public ChatChromeRow(
        ThreadRuntime runtime,
        ChromeScrollModel scrollChrome,
        string title,
        Project? project,
        string directory,
        GitStatusModel git) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1116");
}

/// <summary>:1184-1240 <c>private struct ChatChromeTitle: View</c>.</summary>
internal sealed class ChatChromeTitle : Decorator
{
    // :1191-1192 the view's inputs; :1198-1239 body
    public ChatChromeTitle(ThreadRuntime runtime, string title) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1198");
}

/// <summary>:1242-1265 <c>private struct ThreadsButton: View</c>. Its capture-frame hook (:1261-1263) is not ported
/// (ED-001: WebsiteCaptures.isEnabled is always false, porting/dead_code.md entry 44).</summary>
internal sealed class ThreadsButton : Decorator
{
    // :1248-1264 body
    public ThreadsButton() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1248");
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

    // :1289-1307 func refresh(_:force:) async
    public Task Refresh(string directory, bool force = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1289");

    // :1316-1325 func perform(_:_:) async -> Error?
    public Task<Exception?> Perform(string activity, Func<Task> work) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1316");
}

/// <summary>:1328-1343 <c>private struct PermissionMenu: View</c>.</summary>
internal sealed class PermissionMenu : Decorator
{
    // :1331 let thread; :1333-1342 body
    public PermissionMenu(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1333");
}

/// <summary>:1345-1409 <c>private struct BranchMenu: View</c>.</summary>
internal sealed class BranchMenu : Decorator
{
    // :1347-1349 the view's inputs; :1354-1386 body
    public BranchMenu(ThreadRuntime runtime, string directory, GitStatusModel git) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1354");
}

/// <summary>:1411-1432 <c>private struct OpenInMenu: View</c>.</summary>
internal sealed class OpenInMenu : Decorator
{
    // :1412 let directory; :1414-1431 body
    public OpenInMenu(string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1414");
}

/// <summary>:1434-1466 <c>private struct ScriptsMenu: View</c>.</summary>
internal sealed class ScriptsMenu : Decorator
{
    // :1436-1437 the view's inputs; :1441-1465 body
    public ScriptsMenu(Project project, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1441");
}

/// <summary>:1468-1528 <c>private struct GitActionsMenu: View</c>.</summary>
internal sealed class GitActionsMenu : Decorator
{
    // :1470-1472 the view's inputs; :1476-1493 body
    public GitActionsMenu(ThreadRuntime runtime, string directory, GitStatusModel git) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1476");
}

/// <summary>:1530-1650 <c>private struct CommitSheet: View</c>.</summary>
internal sealed class CommitSheet : Decorator
{
    // :1533-1535 the view's inputs; :1543-1610 body
    public CommitSheet(ThreadRuntime runtime, string directory, GitStatusModel git) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1543");
}

/// <summary>:1652-1730 <c>private struct ScriptsEditor: View</c>.</summary>
internal sealed class ScriptsEditor : Decorator
{
    // :1655 let projectID; :1659-1724 body
    public ScriptsEditor(Guid projectID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatView.swift:1659");
}
