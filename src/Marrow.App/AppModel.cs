using Marrow.App.Runtime;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;

// Source: DroppyCode/App/AppModel.swift (B2 1fbf81e), :1-1356. Skeleton (c100 group 1): AppAlert (:5-9), the
// FinishRequest and ArchiveRequest shapes (:917-932), ThreadIndex's stored shape (:50-61) and the observed stored
// properties without observers (:103-106, :923, :932) are complete; every didSet (:30-45, :72-98), the init
// (:120-146), the cell machinery (:62-71, :148-226), the private task and flag state (:108-118, :999) and every
// method and computed property are left to the implementing unit. AppModel is partial: the extensions in
// AppModel+Hydra.swift, AppModel+HydraMerge.swift and AppModel+HydraPairs.swift are parts of it in their own twins
// (R-FILE-1). Forms: URL is SwiftURL; an (inout T) -> Void closure is a Func<T, T> returning the changed copy
// (R-VAL-1); a Swift Int is long (R-NUM-4).

namespace Marrow.App;

// :5-9 (R-VAL-1)
public sealed record AppAlert
{
    public Guid Id { get; } = Guid.NewGuid(); // :6
    public required string Title { get; init; }   // :7
    public required string Message { get; init; } // :8
}

// :11-1328. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed partial class AppModel : MainActorModel
{
    // :50-61
    private sealed record ThreadIndex
    {
        public ValueMap<Guid, long> Positions { get; init; } = ValueMap<Guid, long>.Empty;              // :51
        public ValueMap<Guid, ValueList<long>> Projects { get; init; } = ValueMap<Guid, ValueList<long>>.Empty; // :52

        // :54-60
        public ThreadIndex(IReadOnlyList<ChatThread> threads) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:54");
    }

    // :917-921 (R-VAL-1). The Swift `let id = UUID()` is part of the synthesized ==.
    public sealed record FinishRequest(Guid ThreadID, bool Reopens) // :919-920
    {
        public Guid Id { get; } = Guid.NewGuid(); // :918
    }

    // :927-930
    public sealed record ArchiveRequest(Guid ThreadID) // :929
    {
        public Guid Id { get; } = Guid.NewGuid(); // :928
    }

    private ValueList<Project> projects = ValueList<Project>.Empty;
    private ValueList<ChatThread> threads = ValueList<ChatThread>.Empty;
    private Guid? selectedThreadID;
    private Guid? sidebarRevealThreadID;
    private bool isCommandPalettePresented = false;
    private AppAlert? alert;
    private FinishRequest? finishRequest;
    private ArchiveRequest? archiveRequest;

    public AppSettings Settings { get; }          // :15
    public ProviderRegistry Providers { get; }    // :16
    public MCPStore Mcp { get; }                  // :18 Swift: mcp
    public TerminalStore Terminals { get; }       // :19
    public SidebarLayout Sidebar { get; }         // :20

    // :22. @ObservationIgnored private(set) var autoContinue: AutoContinue! (R-OBS-5).
    public AutoContinue AutoContinue { get; private set; } = null!;

    // :25. @ObservationIgnored (R-OBS-5).
    public ValueMap<Guid, long> HydraHeadRetries { get; set; } = ValueMap<Guid, long>.Empty;

    // :30-32
    public ValueList<Project> Projects
    {
        get => Read(projects);
        private set => Set(ref projects, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:31"));
    }

    // :33-45
    public ValueList<ChatThread> Threads
    {
        get => Read(threads);
        private set => Set(ref threads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:34"));
    }

    // :72-98
    public Guid? SelectedThreadID
    {
        get => Read(selectedThreadID);
        set => Set(ref selectedThreadID, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:73"));
    }

    // :103
    public Guid? SidebarRevealThreadID { get => Read(sidebarRevealThreadID); set => Set(ref sidebarRevealThreadID, value); }

    // :105
    public bool IsCommandPalettePresented { get => Read(isCommandPalettePresented); set => Set(ref isCommandPalettePresented, value); }

    // :106
    public AppAlert? Alert { get => Read(alert); set => Set(ref alert, value); }

    // :120-146
    public AppModel() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:120");

    // :229-231. Swift: isSelected(_:).
    public bool IsSelected(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:229");

    // :235-237. Swift: children(of:).
    public ValueList<ChatThread> Children(Guid parentID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:235");

    // :239-278
    public Task Bootstrap() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:239");

    // MARK: - Lookup

    // :283-285. Swift: project(_:).
    public Project? Project(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:283");

    // :288-290. Swift: thread(_:).
    public ChatThread? Thread(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:288");

    // :292-294
    public ChatThread? SelectedThread =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:292");

    // :296-304
    public Project? CurrentProject =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:296");

    // :314-332. Swift: threads(in:). Renamed: C# cannot hold the property Threads (:33) and a method Threads.
    public ValueList<ChatThread> ThreadsIn(Project project) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:314");

    // :337-353. Swift: moveThread(_:to:placeAfter:), @discardableResult.
    public bool MoveThread(Guid id, Guid targetID, bool placeAfter) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:338");

    // :357-371. Swift: moveInActivity(_:to:placeAfter:among:), @discardableResult.
    public bool MoveInActivity(Guid id, Guid targetID, bool placeAfter, IReadOnlyList<Guid> peers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:358");

    // :373-375
    public ValueList<ChatThread> ArchivedThreads =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:373");

    // :378-380. nonisolated static.
    public static bool SettledFirst(ChatThread lhs, ChatThread rhs) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:378");

    // :384-390
    public ValueList<ChatThread> SidebarThreads =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:384");

    // :396-400. Swift: helpers(of:).
    public ValueList<ChatThread> Helpers(Guid parentID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:396");

    // :405-413. Swift: panelHeads(of:).
    public ValueList<ChatThread> PanelHeads(Guid parentID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:405");

    // :417-419. Swift: panelSubagent(of:).
    public ChatThread? PanelSubagent(Guid parentID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:417");

    // :421-423
    public void ToggleHelpersFold(Guid parentID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:421");

    // :425-430. Swift: runtime(for:).
    public ThreadRuntime Runtime(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:425");

    // :432-434. Swift: existingRuntime(for:).
    public ThreadRuntime? ExistingRuntime(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:432");

    // :437
    public ValueList<ThreadRuntime> LiveRuntimes =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:437");

    // :441-445
    public void WarmDocuments(IReadOnlyList<Guid> ids) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:441");

    // :455-463. Swift: neighbors(of:).
    public ValueList<Guid> Neighbors(Guid? id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:455");

    // :492-506. @discardableResult.
    public bool ReleaseIdleRuntime(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:493");

    // :508-511
    public string? WorkingDirectory =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:508");

    // MARK: - Projects

    // :515-524. Swift: addProject(at:), @discardableResult.
    public Project AddProject(SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:516");

    // :526-529
    public void ChooseProjectFolder() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:526");

    // :535-547. @discardableResult.
    public Project? AddProjectsFromPanel() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:536");

    // :549-556
    public void UpdateProject(Guid id, Func<Project, Project> change) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:549");

    // :558-567
    public void RemoveProject(Project project) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:558");

    // MARK: - Threads

    // :574-602
    public void OpenThreadOnLaunch() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:574");

    // :604-620. Swift: newThread(in:workspace:), @discardableResult.
    public ChatThread? NewThread(Project? project = null, WorkspaceMode? workspace = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:605");

    // :623-646. @discardableResult.
    public Task<ChatThread?> ForwardThread(Guid sourceID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:624");

    // :705-712
    public void UpdateThread(Guid id, Func<ChatThread, ChatThread> change) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:705");

    // :720-732. Swift: moveThread(_:toProject:).
    public void MoveThread(Guid id, Project toProject) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:720");

    // :748-755. Swift: rename(_:to:).
    public void Rename(Guid id, string title) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:748");

    // :757-772
    public void Archive(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:757");

    // :774-781
    public void Unarchive(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:774");

    // :787-807
    public void Settle(Guid id, bool sounds = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:787");

    // :810-826
    public void Reopen(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:810");

    // :829-831
    public void SettleAnimated(Guid id, bool sounds = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:829");

    // :834-836
    public void ReopenAnimated(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:834");

    // :839-842
    public void ReopenIfSettled(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:839");

    // :846-849
    public string FinishActionTitle =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:846");

    // :851-854
    public string FinishActionSymbol =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:851");

    // :856-873
    public void FinishSelectedThread() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:856");

    // :878-889
    public void SettleSelectedThread() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:878");

    // :893-912
    public void AskToArchiveSelectedThread() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:893");

    // :923. Swift: finishRequest (R-NAME-3).
    public FinishRequest? AppModelFinishRequest { get => Read(finishRequest); set => Set(ref finishRequest, value); }

    // :932. Swift: archiveRequest (R-NAME-3).
    public ArchiveRequest? AppModelArchiveRequest { get => Read(archiveRequest); set => Set(ref archiveRequest, value); }

    // :934-957
    public void Delete(Guid id, bool removeWorktree = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:934");

    // :961-993
    public void DeleteArchivedThreads() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:961");

    // MARK: - Subagents

    // :1003-1005. Swift: subagent(of:).
    public ChatThread? Subagent(Guid parentID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1003");

    // :1009-1012
    public void InsertThread(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1009");

    // :1020-1051. Swift: spawnSubagent(from:title:prompt:), @discardableResult.
    public ChatThread? SpawnSubagent(Guid parentID, string title, string prompt) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1021");

    // :1055-1068
    public void CloseSubagent(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1055");

    // :1089-1092
    public ProjectScript? TopScript =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1089");

    // :1094-1098. Swift: runScript(_:threadID:).
    public void RunScript(ProjectScript script, Guid threadID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1094");

    // :1100-1108. Swift: selectThread(offset:).
    public void SelectThread(long offset) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1100");

    // :1110-1114. Swift: selectThread(number:). Renamed: C# cannot overload SelectThread on the label alone.
    public void SelectThreadNumber(long number) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1110");

    // :1116-1141. Swift: createWorktree(for:).
    public Task CreateWorktree(Guid threadID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1116");

    // MARK: - Attention

    // :1158-1169
    public void ThreadNeedsAttention(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1158");

    // :1173-1216. Swift: turnFinished(_:status:continues:).
    public void TurnFinished(Guid id, TurnStatus status, bool continues) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1173");

    // :1218-1222. Swift: markRead(_: UUID).
    public void MarkRead(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1218");

    // :1226-1238. Swift: markRead(_: [UUID]).
    public void MarkRead(IReadOnlyList<Guid> ids) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1226");

    // :1240-1246
    public void RequestNotificationPermission() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1240");

    // :1248-1259. Swift: notify(threadID:title:body:sound:) with `sound: UNNotificationSound? = .default`. No port
    // type stands for UNNotificationSound; the only values the source passes are .default and nil (:1215), so the
    // skeleton carries them as true and false. UNRESOLVED: the Windows notification unit decides the final form.
    public void Notify(Guid threadID, string title, string body, bool sound = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1248");

    // :1261-1264
    public void UpdateDockBadge() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1261");

    // MARK: - Text generation

    // :1268-1285. Swift: textEngine(preferring:).
    public TextGeneration.Engine? TextEngine(ProviderKind? provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1268");

    // MARK: - Persistence

    // :1289-1297
    public void ScheduleSave() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1289");

    // :1309-1327
    public void SaveBeforeQuit() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1309");
}

// :1331-1356. A caseless enum of static functions -> static class.
public static class ProjectFile
{
    // :1332-1344. Swift: scripts(at:).
    public static ValueList<ProjectScript> Scripts(SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1332");

    // :1346-1355. Swift: symbol(for:).
    public static string Symbol(string? icon) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppModel.swift:1346");
}
