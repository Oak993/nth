using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Sound;

// Source: DroppyCode/App/AppModel.swift (B3 52bc7926), :1-1374. Skeleton (c100 group 1): AppAlert (:5-9), the
// FinishRequest and ArchiveRequest shapes (:926-941), ThreadIndex's stored shape (:50-61) and the observed stored
// properties without observers (:103-106, :932, :941) are complete; every didSet (:30-45, :72-98), the init
// (:121-147), the cell machinery (:62-71, :149-227), the private task and flag state (:108-119, :1008) and every
// method and computed property are left to the implementing unit. AppModel is partial: the extensions in
// AppModel+Hydra.swift, AppModel+HydraMerge.swift and AppModel+HydraPairs.swift are parts of it in their own twins
// (R-FILE-1). Forms: URL is SwiftURL; an (inout T) -> Void closure is a Func<T, T> returning the changed copy
// (R-VAL-1); a Swift Int is long (R-NUM-4).

namespace Marrow.App;

// AppModel.swift:5-9 (R-VAL-1)
public sealed record AppAlert
{
    public Guid Id { get; } = Guid.NewGuid(); // AppModel.swift:6
    public required string Title { get; init; }   // AppModel.swift:7
    public required string Message { get; init; } // AppModel.swift:8
}

// AppModel.swift:11-1328. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed partial class AppModel : MainActorModel
{
    // AppModel.swift:50-61
    private sealed record ThreadIndex
    {
        public ValueMap<Guid, long> Positions { get; init; } = ValueMap<Guid, long>.Empty;              // AppModel.swift:51
        public ValueMap<Guid, ValueList<long>> Projects { get; init; } = ValueMap<Guid, ValueList<long>>.Empty; // AppModel.swift:52

        // AppModel.swift:54-60
        public ThreadIndex(IReadOnlyList<ChatThread> threads) // AppModel.swift:54-60
        {
            var positions = new Dictionary<Guid, long>();
            var projects = new Dictionary<Guid, List<long>>();
            for (var index = 0; index < threads.Count; index++)
            {
                var thread = threads[index];
                positions.TryAdd(thread.Id, index); // AppModel.swift:57 `if positions[thread.id] == nil`: the first position stands
                if (!projects.TryGetValue(thread.ProjectID, out var indices)) projects[thread.ProjectID] = indices = [];
                indices.Add(index);
            }
            Positions = new ValueMap<Guid, long>(positions);
            Projects = new ValueMap<Guid, ValueList<long>>(projects.Select(pair =>
                new KeyValuePair<Guid, ValueList<long>>(pair.Key, new ValueList<long>(pair.Value))));
        }
    }

    // AppModel.swift:917-921 (R-VAL-1). The Swift `let id = UUID()` is part of the synthesized ==.
    public sealed record FinishRequest(Guid ThreadID, bool Reopens) // AppModel.swift:919-920
    {
        public Guid Id { get; } = Guid.NewGuid(); // AppModel.swift:918
    }

    // AppModel.swift:927-930
    public sealed record ArchiveRequest(Guid ThreadID) // AppModel.swift:929
    {
        public Guid Id { get; } = Guid.NewGuid(); // AppModel.swift:928
    }

    private ValueList<Project> projects = ValueList<Project>.Empty;
    private ValueList<ChatThread> threads = ValueList<ChatThread>.Empty;
    private ThreadIndex threadIndex = new(ValueList<ChatThread>.Empty); // AppModel.swift:46
    private Dictionary<Guid, ObservedValue<Project?>> projectCells = []; // AppModel.swift:62
    private Dictionary<Guid, ObservedValue<ChatThread?>> threadCells = []; // AppModel.swift:63
    private Dictionary<Guid, ObservedValue<ValueList<Guid>>> childCells = []; // AppModel.swift:67
    private Dictionary<Guid, ObservedValue<bool>> selectionCells = []; // AppModel.swift:71
    private readonly HashSet<Guid> strayChildCells = []; // ED-NEW-lean-16: child cells asked for a parent that does not exist
    private Guid? selectedThreadID;
    private Guid? sidebarRevealThreadID;
    private bool isCommandPalettePresented = false;
    private AppAlert? alert;
    private FinishRequest? finishRequest;
    private ArchiveRequest? archiveRequest;

    private Dictionary<Guid, ThreadRuntime> runtimes = []; // AppModel.swift:108
    private Dictionary<Guid, TaskHandle> idleSessionStops = []; // AppModel.swift:109
    private TaskHandle? saveTask; // AppModel.swift:110
    private ContinuousInstant? lastLibrarySave; // AppModel.swift:111 (B3, PR-STORE-015/PR-LAUNCH-076)
    private TaskHandle? hydraMergeSweepTask; // AppModel.swift:112-117

    // PORT-ONLY (OD-0133): what the launch's boot hand-over waits for, the bootstrap up to its provider refresh
    private readonly TaskCompletionSource launchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// PORT-ONLY (OD-0133, amending OD-0108 for provider checks only): completes when <see cref="Bootstrap"/> reaches the
    /// provider refresh, everything before it done (recent documents, keychain, login environment, Hydra). The boot hand-over
    /// waits for this, not for the refresh, the sign-in checks and the catalogs after it: they finish after the window shows,
    /// as on the Mac (AppDelegate.swift:27 runs bootstrap in a Task after the window is shown).
    /// </summary>
    public Task LaunchReady => launchReady.Task;

    /// <summary>Completes <see cref="LaunchReady"/> (<see cref="Bootstrap"/>; the launch tests' scripted bootstraps).</summary>
    internal void MarkLaunchReady() => launchReady.TrySetResult();
    private bool persistenceEnabled = true; // AppModel.swift:118
    private HashSet<Guid> sessionsToRelease = []; // AppModel.swift:1008
    private bool libraryLoadFailed; // DEVIATION OD-0008: protect library.json after a failed load.
    private long dockUnreadCount; // AppModel.swift:1270-1273; shown as the taskbar overlay (ED-219).

    public AppSettings Settings { get; }          // AppModel.swift:15
    public ProviderRegistry Providers { get; }    // AppModel.swift:16
    public MCPStore Mcp { get; }                  // AppModel.swift:18 Swift: mcp
    public TerminalStore Terminals { get; }       // AppModel.swift:19
    public SidebarLayout Sidebar { get; }         // AppModel.swift:20

    // AppModel.swift:22. @ObservationIgnored private(set) var autoContinue: AutoContinue! (R-OBS-5).
    public AutoContinue AutoContinue { get; private set; } = null!;

    // AppModel.swift:25. @ObservationIgnored (R-OBS-5).
    public ValueMap<Guid, long> HydraHeadRetries { get; set; } = ValueMap<Guid, long>.Empty;

    // AppModel.swift:30-32
    public ValueList<Project> Projects
    {
        get => Read(projects);
        private set => Set(ref projects, value, _ => Write(Sync(ref projectCells, value, project => project.Id))); // AppModel.swift:30-32
    }

    // AppModel.swift:33-45
    public ValueList<ChatThread> Threads
    {
        get => Read(threads);
        private set => Set(ref threads, value, _ =>
        {
            Write(Sync(ref threadCells, value, thread => thread.Id));
            SyncChildCells(value);
            threadIndex = new ThreadIndex(value);
        }); // AppModel.swift:33-45
    }

    // AppModel.swift:72-98
    public Guid? SelectedThreadID
    {
        get => Read(selectedThreadID);
        set => Set(ref selectedThreadID, value, oldValue =>
        {
            if (oldValue != value)
            {
                if (oldValue is { } oldID) SelectionCell(oldID).Value = false;
                if (value is { } newID) SelectionCell(newID).Value = true;
                if (value is not null && assistantHomeShown) AssistantHomeShown = false; // PORT-ONLY (AS-8, ED-NEW-dextra-as8-8): picking a chat leaves the Assistant home
                SwitchLatency.NoteSwitch(value is { } selectedID ? Thread(selectedID)?.Title ?? "none" : "none");
            }
            if (value is { } selected)
            {
                var ids = new List<Guid> { selected };
                ids.AddRange(Children(selected).Select(child => child.Id));
                var weakSelf = new WeakReference<AppModel>(this);
                MainActor.Spawn(() =>
                {
                    if (weakSelf.TryGetTarget(out var model)) model.MarkRead(ids);
                    return Task.CompletedTask;
                });
                MainActor.Spawn(() =>
                {
                    if (weakSelf.TryGetTarget(out var model)) model.WarmNeighbors(selected);
                    return Task.CompletedTask;
                }); // AppModel.swift:90-91
            }
            if (oldValue != value)
            {
                ScheduleIdleSessionStop(oldValue);
            }
            if (value is { } id && Thread(id) is { } thread)
            {
                RememberLastProject(thread.ProjectID);
            }
        }); // AppModel.swift:72-98
    }

    // AppModel.swift:103
    public Guid? SidebarRevealThreadID { get => Read(sidebarRevealThreadID); set => Set(ref sidebarRevealThreadID, value); }

    // AppModel.swift:105
    public bool IsCommandPalettePresented { get => Read(isCommandPalettePresented); set => Set(ref isCommandPalettePresented, value); }

    // AppModel.swift:106
    public AppAlert? Alert { get => Read(alert); set => Set(ref alert, value); }

    // AppModel.swift:120-146
    public AppModel()
    {
        Settings = new AppSettings(); // AppModel.swift:121
        Providers = new ProviderRegistry(Settings); // AppModel.swift:122
        Mcp = new MCPStore(); // AppModel.swift:123
        Terminals = new TerminalStore(); // AppModel.swift:124
        Terminals.PreviewReady += terminal => Views.Browser.ProjectPreviewHost.Found(this, terminal); // PORT-ONLY: DESIGN 5.6, ED-NEW-web-w9-3.
        Sidebar = new SidebarLayout(); // AppModel.swift:125

        var load = Storage.LoadLibrary(); // AppModel.swift:126
        libraryLoadFailed = load is LibraryLoadResult.Failed;
        if (libraryLoadFailed)
            alert = new AppAlert
            {
                Title = "Could not read library.json",
                Message = "nth could not read library.json, so your projects and threads are hidden. The file will not be changed until it can be read.",
            }; // DEVIATION OD-0012: show the E4 library-load message.
        var library = load is LibraryLoadResult.Loaded loaded ? loaded.Library : new Library(); // AppModel.swift:126-128
        projects = library.Projects;
        threads = library.Threads;
        for (var index = 0; index < threads.Count; index++) // AppModel.swift:129-131
            if (threads[index].LastStatus == TurnStatus.Running)
                threads = threads.SetItem(index, threads[index] with { LastStatus = TurnStatus.Interrupted });
        for (var index = 0; index < threads.Count; index++) // AppModel.swift:132-136
        {
            if (threads[index].Hydra?.HydraHeadInfoStatus != HydraHeadInfo.Status.Running) continue;
            var hydra = threads[index].Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Stopped, FinishedAt = SwiftDate.Now };
            threads = threads.SetItem(index, threads[index] with { Hydra = hydra });
        }
        for (var index = 0; index < threads.Count; index++) // PORT-ONLY (web surfaces W3c, DESIGN 3.11): an unreadable saved pane layout is kept as workspace.bad.json
        {
            if (threads[index].DamagedWorkspace is not { } damaged) continue;
            Marrow.Services.Store.WorkspaceRecovery.Keep(threads[index].Id, damaged);
            threads = threads.SetItem(index, threads[index] with { DamagedWorkspace = null });
        }
        Write(Sync(ref projectCells, projects, project => project.Id)); // AppModel.swift:137-139
        Write(Sync(ref threadCells, threads, thread => thread.Id)); // AppModel.swift:137-140
        SyncChildCells(threads);
        threadIndex = new ThreadIndex(threads); // AppModel.swift:141
        AutoContinue = new AutoContinue(this); // AppModel.swift:142
        InitializeAccountReports(); // OD-0114: start only after bootstrap has loaded credentials and provider statuses.
        if (Settings.LastProjectID is { } lastID && Project(lastID) is null) Settings.LastProjectID = null; // AppModel.swift:143-145
        InitializePalette(); // OD-0064, ED-622: pack snapshot 0 before any window, cached Prime on the actor.
    }

    private static List<(ObservedValue<TValue?> Cell, TValue? Value)> Sync<TValue>(
        ref Dictionary<Guid, ObservedValue<TValue?>> cells,
        IReadOnlyList<TValue> values,
        Func<TValue, Guid> id)
        where TValue : class, IEquatable<TValue>
    {
        var writes = new List<(ObservedValue<TValue?> Cell, TValue? Value)>();
        var seen = new HashSet<Guid>(values.Count);
        foreach (var value in values)
        {
            var key = id(value);
            seen.Add(key);
            if (cells.TryGetValue(key, out var cell))
            {
                if (!EqualityComparer<TValue?>.Default.Equals(cell.Value, value)) writes.Add((cell, value));
            }
            else
            {
                cells[key] = new ObservedValue<TValue?>(value);
            }
        }
        foreach (var key in cells.Keys.ToArray())
        {
            if (seen.Contains(key)) continue;
            writes.Add((cells[key], null));
            cells.Remove(key);
        }
        return writes;
    } // AppModel.swift:157-178

    private static void Write<TValue>(IReadOnlyList<(ObservedValue<TValue?> Cell, TValue? Value)> writes)
    {
        foreach (var (cell, value) in writes) cell.Value = value;
    } // AppModel.swift:180-187

    private void SyncChildCells(IReadOnlyList<ChatThread> values)
    {
        var children = new Dictionary<Guid, List<Guid>>();
        foreach (var thread in values)
        {
            if (thread.ParentThreadID is not { } parentID) continue;
            if (!children.TryGetValue(parentID, out var ids)) children[parentID] = ids = [];
            ids.Add(thread.Id);
        }
        // A parent that still exists keeps its cell even with nothing under it, because that is exactly the cell a chat
        // with no heads yet is watching for its first one; only a parent that is gone loses its cell.
        foreach (var (parentID, cell) in childCells.ToArray()) // AppModel.swift:196-199
        {
            var value = children.TryGetValue(parentID, out var present) ? new ValueList<Guid>(present) : ValueList<Guid>.Empty;
            if (!cell.Value.Equals(value)) cell.Value = value;
        }
        foreach (var (parentID, ids) in children) // AppModel.swift:200-202
            if (!childCells.ContainsKey(parentID)) childCells[parentID] = new ObservedValue<ValueList<Guid>>(new ValueList<Guid>(ids));
        strayChildCells.Clear(); // ED-NEW-lean-16: what follows drops every cell of a parent that neither exists nor has children
        if (childCells.Count == children.Count) return; // AppModel.swift:203
        var live = values.Select(thread => thread.Id).ToHashSet(); // AppModel.swift:204
        foreach (var parentID in childCells.Keys.ToArray()) // AppModel.swift:205-207
            if (!live.Contains(parentID) && !children.ContainsKey(parentID)) childCells.Remove(parentID);
    } // AppModel.swift:189-210

    private ValueList<Guid> ChildIDs(Guid parentID)
    {
        if (!childCells.TryGetValue(parentID, out var cell))
        {
            childCells[parentID] = cell = new ObservedValue<ValueList<Guid>>(ValueList<Guid>.Empty);
            if (!threadCells.ContainsKey(parentID)) strayChildCells.Add(parentID); // ED-NEW-lean-16: the next thread event drops it
        }
        return cell.Value;
    } // AppModel.swift:212-219

    /// <summary>
    /// PORT-ONLY (ED-NEW-lean-16). Assigns <paramref name="value"/> to <see cref="Threads"/> when it differs from the list held
    /// only at <paramref name="changed"/> (ascending, each a real change), and every one of those keeps its id, project and
    /// parent: the cells, the child cells and the index then stay as they are but for the changed threads' own cells, so the
    /// event costs no pass over the library. Writes the cells and drops the stray child cells exactly as the setter's full
    /// sync (AppModel.swift:33-45, :157-210) would for such a list. Returns false, assigning nothing, for any other change
    /// (a thread added, removed, moved to another project or parent, a repeated id, or no net change), which takes the setter.
    /// </summary>
    private bool AssignThreadsInPlace(ValueList<ChatThread> value, IReadOnlyList<int> changed)
    {
        if (changed.Count == 0) return false; // the lists are equal (changes that cancel out): the setter, whose Set tells no one
        var held = threads;
        if (value.Count != held.Count || threadCells.Count != held.Count) return false; // threadCells holds one cell per distinct id
        foreach (var index in changed)
        {
            var before = held[index];
            var after = value[index];
            if (after.Id != before.Id || after.ProjectID != before.ProjectID || after.ParentThreadID != before.ParentThreadID) return false;
        }
        SetAlways(ref threads, value, _ =>
        {
            if (changed.Count == 1)
            {
                var thread = value[changed[0]];
                var cell = threadCells[thread.Id];
                if (!EqualityComparer<ChatThread?>.Default.Equals(cell.Value, thread)) cell.Value = thread;
            }
            else
            {
                var writes = new List<(ObservedValue<ChatThread?> Cell, ChatThread? Value)>(changed.Count);
                foreach (var index in changed)
                {
                    var thread = value[index];
                    var cell = threadCells[thread.Id];
                    if (!EqualityComparer<ChatThread?>.Default.Equals(cell.Value, thread)) writes.Add((cell, thread));
                }
                Write(writes);
            }
            if (strayChildCells.Count == 0) return;
            foreach (var parentID in strayChildCells) // AppModel.swift:203-207
                if (!threadCells.ContainsKey(parentID) && childCells.TryGetValue(parentID, out var stray) && stray.Value.Count == 0)
                    childCells.Remove(parentID);
            strayChildCells.Clear();
        }, nameof(Threads));
        return true;
    }

    // PORT-ONLY (ED-NEW-lean-16): where the held list with <paramref name="replaced"/> laid over it differs from the held list,
    // ascending: the replaced places whose thread is not equal to the held one.
    private static List<int> ChangedIndices(ValueList<ChatThread> held, Dictionary<int, ChatThread> replaced)
    {
        var changed = new List<int>(replaced.Count);
        foreach (var (index, value) in replaced)
            if (held[index] != value) changed.Add(index);
        changed.Sort();
        return changed;
    }

    // PORT-ONLY (ED-NEW-lean-16): the held list with one thread replaced, copied once by ValueList's constructor.
    private sealed class OneReplaced(ValueList<ChatThread> source, int index, ChatThread value) : ICollection<ChatThread>
    {
        public int Count => source.Count;
        public bool IsReadOnly => true;
        public void CopyTo(ChatThread[] array, int arrayIndex)
        {
            source.CopyTo(array, arrayIndex);
            array[arrayIndex + index] = value;
        }
        public IEnumerator<ChatThread> GetEnumerator()
        {
            for (var position = 0; position < source.Count; position++) yield return position == index ? value : source[position];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Contains(ChatThread item) => throw new NotSupportedException();
        public void Add(ChatThread item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Remove(ChatThread item) => throw new NotSupportedException();
    }

    // PORT-ONLY (ED-NEW-lean-16, follow-up): the held list with the threads at some places replaced, copied once by ValueList's
    // constructor.
    private sealed class SomeReplaced(ValueList<ChatThread> source, Dictionary<int, ChatThread> replaced) : ICollection<ChatThread>
    {
        public int Count => source.Count;
        public bool IsReadOnly => true;
        public void CopyTo(ChatThread[] array, int arrayIndex)
        {
            source.CopyTo(array, arrayIndex);
            foreach (var (index, value) in replaced) array[arrayIndex + index] = value;
        }
        public IEnumerator<ChatThread> GetEnumerator()
        {
            for (var position = 0; position < source.Count; position++)
                yield return replaced.TryGetValue(position, out var value) ? value : source[position];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Contains(ChatThread item) => throw new NotSupportedException();
        public void Add(ChatThread item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Remove(ChatThread item) => throw new NotSupportedException();
    }

    private ObservedValue<bool> SelectionCell(Guid id)
    {
        if (!selectionCells.TryGetValue(id, out var cell)) selectionCells[id] = cell = new ObservedValue<bool>(selectedThreadID == id);
        return cell;
    } // AppModel.swift:221-226

    // AppModel.swift:229-231. Swift: isSelected(_:).
    public bool IsSelected(Guid id) => SelectionCell(id).Value; // AppModel.swift:229-231

    // AppModel.swift:235-237. Swift: children(of:).
    public ValueList<ChatThread> Children(Guid parentID) => new(ChildIDs(parentID).Select(Thread).OfType<ChatThread>()); // AppModel.swift:235-237

    // Source: DroppyCode/App/AppModel.swift (B3 52bc792), :240-287.
    // :275-282. Sweep C verified the inherited ninth step with blocked offline updaters; no behavior change needed.
    public async Task Bootstrap()
    {
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: recent documents (UI scheduling)"))
        {
            var recent = threads
                .Where(thread => !thread.IsArchived && !thread.IsInPanel)
                .OrderByDescending(thread => thread.UpdatedAt)
                .Take(12)
                .Select(thread => thread.Id)
                .ToList(); // AppModel.swift:240-243
            WarmDocuments(recent);
        }
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: keychain (awaited worker)"))
        await Settings.LoadKeychainInBackground(); // AppModel.swift:244
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: login environment"))
        await LoginEnvironment.Load(); // AppModel.swift:245
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: Hydra copy sweep (UI)"))
        if (PerformanceProfileMaintenance.Allowed) SweepHydraCopies(); // AppModel.swift:247; explicit diagnostics cannot mutate copied real project paths.
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: Hydra merge resumption"))
        if (PerformanceProfileMaintenance.Allowed) await ResumeHydraMerges(); // AppModel.swift:248-249
        hydraMergeSweepTask?.Cancel(); // AppModel.swift:254
        var weakSelf = new WeakReference<AppModel>(this);
        if (PerformanceProfileMaintenance.Allowed) hydraMergeSweepTask = MainActor.Spawn(async () =>
        {
            while (true)
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(300)); // AppModel.swift:256-257
                if (SwiftTask.IsCancelled || !weakSelf.TryGetTarget(out var model)) return; // AppModel.swift:258
                await model.ResumeHydraMerges(); // AppModel.swift:259
            }
        }); // AppModel.swift:255-261
        MarkLaunchReady(); // OD-0133: the hand-over need not wait for what follows
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: provider refresh"))
        await Providers.RefreshAll(); // AppModel.swift:263
        StartAccountReports(); // OD-0114: Settings can read each connected account before a chat opens.
        await TaskGroup.Run(async group =>
        {
            foreach (var provider in new[] { ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.OpenRouter /* PORT-ONLY (ED-234) */ })
            {
                if (!Providers.Status(provider).IsInstalled) continue;
                group.AddTask(() => MainActor.Invoke(() => Providers.LoadCatalog(provider)));
            }
        }); // AppModel.swift:267-271
        if (Providers.Status(ProviderKind.Zai).IsInstalled) await Providers.LoadCatalog(ProviderKind.Zai); // AppModel.swift:272-274
        // The CLIs Marrow runs headless never update themselves, and a model newer than the installed CLI
        // fails outright. Their own updaters run here, in the background, at most every twelve hours; a
        // newer version reloads its catalog. (PR-LAUNCH-085, new at B3)
        var providers = Providers; // AppModel.swift:278 Swift: `Task { [providers] in ... }` captures only the registry, not self (R-ISO-4)
        if (PerformanceProfileMaintenance.Allowed) _ = MainActor.Spawn(async () =>
        {
            foreach (var provider in new[] { ProviderKind.Claude, ProviderKind.Codex }) // AppModel.swift:279
            {
                if (!providers.Status(provider).IsInstalled) continue; // AppModel.swift:279
                await providers.UpdateCLIIfDue(provider); // AppModel.swift:280
            }
        }); // AppModel.swift:275-282; un-awaited: bootstrap proceeds to the sweep below without waiting for it.
        // Last, once nothing the first click needs is waiting on the disk: the attachment files no
        // thread refers to any more go, a day after they were written.
        using (Marrow.UI.Port.AuroraBootTrace.Begin("Bootstrap: orphan attachments"))
        await Storage.SweepOrphanedAttachments(); // AppModel.swift:283-286; DEVIATION ED-001: capture guard dropped.
        StartPaletteChecks(); // OD-0064: loaded catalogues only; automatic network checks begin after bootstrap.
    }

    // MARK: - Lookup

    // AppModel.swift:283-285. Swift: project(_:).
    public Project? Project(Guid id) => projectCells.TryGetValue(id, out var cell) ? cell.Value : null; // AppModel.swift:283-285

    // AppModel.swift:288-290. Swift: thread(_:).
    public ChatThread? Thread(Guid id) => threadCells.TryGetValue(id, out var cell) ? cell.Value : null; // AppModel.swift:288-290

    // AppModel.swift:292-294
    public ChatThread? SelectedThread => Computed(() => selectedThreadID is { } id ? Thread(id) : null); // AppModel.swift:292-294

    // AppModel.swift:296-304
    public Project? CurrentProject => Computed(() =>
    {
        if (SelectedThread is { } selected && Project(selected.ProjectID) is { } current) return current; // AppModel.swift:297
        if (Settings.LastProjectID is { } lastID && Project(lastID) is { } last) return last; // AppModel.swift:298
        ChatThread? recent = null;
        foreach (var candidate in threads)
        {
            if (candidate.IsArchived || candidate.IsInPanel) continue;
            if (recent is null || candidate.UpdatedAt > recent.UpdatedAt) recent = candidate;
        }
        if (recent is not null && Project(recent.ProjectID) is { } recentProject) return recentProject; // AppModel.swift:299-302
        return projects.FirstOrDefault(); // AppModel.swift:303
    });

    private void RememberLastProject(Guid id)
    {
        if (Project(id) is null) return; // AppModel.swift:306-307
        if (Settings.LastProjectID != id) Settings.LastProjectID = id; // AppModel.swift:308-309
    }

    // AppModel.swift:314-332. Swift: threads(in:). Renamed: C# cannot hold the property Threads (:33) and a method Threads.
    public ValueList<ChatThread> ThreadsIn(Project project)
    {
        var indices = threadIndex.Projects.TryGetValue(project.Id, out var projectIndices)
            ? projectIndices
            : ValueList<long>.Empty;
        var shown = indices.Select(index => threads[checked((int)index)])
            .Where(thread => !thread.IsArchived && !thread.IsInPanel)
            .ToList();
        var shownIDs = shown.Select(thread => thread.Id).ToHashSet();
        var comparer = Comparer<ChatThread>.Create((lhs, rhs) =>
        {
            if (lhs.IsSettled != rhs.IsSettled) return lhs.IsSettled.CompareTo(rhs.IsSettled); // AppModel.swift:321
            if (lhs.IsSettled) return (rhs.SettledAt ?? rhs.UpdatedAt).CompareTo(lhs.SettledAt ?? lhs.UpdatedAt); // AppModel.swift:322
            if (lhs.IsPinned != rhs.IsPinned) return rhs.IsPinned.CompareTo(lhs.IsPinned); // AppModel.swift:323
            if (lhs.SortOrder != rhs.SortOrder)
            {
                if (lhs.SortOrder is null) return -1; // AppModel.swift:324-328
                if (rhs.SortOrder is null) return 1;
                return lhs.SortOrder.Value.CompareTo(rhs.SortOrder.Value);
            }
            return rhs.CreatedAt.CompareTo(lhs.CreatedAt); // AppModel.swift:329
        });
        return new ValueList<ChatThread>(shown
            .Where(thread => thread.ParentThreadID is not { } parentID || !shownIDs.Contains(parentID))
            .OrderBy(thread => thread, comparer)); // AppModel.swift:314-332
    }

    // AppModel.swift:337-353. Swift: moveThread(_:to:placeAfter:), @discardableResult.
    public bool MoveThread(Guid id, Guid targetID, bool placeAfter)
    {
        if (id == targetID || Thread(id) is not { } moving || Thread(targetID) is not { } target
            || moving.ProjectID != target.ProjectID || Project(moving.ProjectID) is not { } project
            || moving.IsSettled || target.IsSettled) return false; // AppModel.swift:339-341
        var before = ThreadsIn(project).Where(thread => !thread.IsSettled).Select(thread => thread.Id).ToList(); // AppModel.swift:342
        var order = before.Where(threadID => threadID != id).ToList(); // AppModel.swift:343-344
        var index = order.IndexOf(targetID);
        if (index < 0) return false; // AppModel.swift:345
        order.Insert(placeAfter ? index + 1 : index, id); // AppModel.swift:346
        if (order.SequenceEqual(before)) return false; // AppModel.swift:347
        UpdateThreads(order, (position, thread) => thread with
        {
            IsPinned = thread.Id == id ? target.IsPinned : thread.IsPinned,
            SortOrder = position,
        }); // AppModel.swift:348-351
        return true;
    }

    // AppModel.swift:357-371. Swift: moveInActivity(_:to:placeAfter:among:), @discardableResult.
    public bool MoveInActivity(Guid id, Guid targetID, bool placeAfter, IReadOnlyList<Guid> peers)
    {
        if (id == targetID || !peers.Contains(id) || !peers.Contains(targetID)) return false; // AppModel.swift:359
        var order = peers.Where(threadID => threadID != id).ToList(); // AppModel.swift:360-361
        var index = order.IndexOf(targetID);
        if (index < 0) return false; // AppModel.swift:362
        order.Insert(placeAfter ? index + 1 : index, id); // AppModel.swift:363
        if (order.SequenceEqual(peers)) return false; // AppModel.swift:364
        var zone = SwiftFormatters.CurrentTimeZone.Current; // AppModel.swift:374 Calendar.current's zone (ED-094)
        UpdateThreads(order, (position, thread) =>
            thread with { ActivityOrder = position, ActivityOrderDay = StartOfDay(thread.UpdatedAt, zone) }); // AppModel.swift:375-378
        return true;
    }

    // Calendar.startOfDay(for:) in the zone: the first instant of the wall-clock day the moment falls on. The offset is the one
    // in force at that midnight, not the moment's own (they differ on a day the clocks change); a midnight that does not
    // exist (a spring-forward gap) starts the day at the first valid time, and a repeated one at its earlier occurrence.
    private static DateTimeOffset StartOfDay(DateTimeOffset date, TimeZoneInfo zone)
    {
        var midnight = TimeZoneInfo.ConvertTime(date, zone).DateTime.Date;
        while (zone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(15);
        var offset = zone.IsAmbiguousTime(midnight) ? zone.GetAmbiguousTimeOffsets(midnight).Max() : zone.GetUtcOffset(midnight);
        return new DateTimeOffset(midnight, offset);
    }

    // AppModel.swift:373-375
    public ValueList<ChatThread> ArchivedThreads => new(threads.Where(thread => thread.IsArchived).OrderByDescending(thread => thread.UpdatedAt)); // AppModel.swift:373-375

    // AppModel.swift:378-380. nonisolated static.
    public static bool SettledFirst(ChatThread lhs, ChatThread rhs) =>
        (lhs.SettledAt ?? lhs.UpdatedAt) > (rhs.SettledAt ?? rhs.UpdatedAt); // AppModel.swift:378-380

    // AppModel.swift:384-390
    public ValueList<ChatThread> SidebarThreads =>
        new(projects.Where(project => project.IsExpanded).SelectMany(project => ThreadsIn(project)
            .SelectMany(thread => thread.FoldsHelpers || thread.IsSettled
                ? new[] { thread }
                : new[] { thread }.Concat(Helpers(thread.Id))))); // AppModel.swift:384-390

    // AppModel.swift:396-400. Swift: helpers(of:).
    public ValueList<ChatThread> Helpers(Guid parentID) =>
        new(Children(parentID).Where(thread => !thread.IsArchived && (!thread.IsInPanel || thread.IsHydraHead))
            .OrderByDescending(thread => thread.CreatedAt)); // AppModel.swift:396-400 (helpers(of:)); B3 checkout :405-409

    // AppModel.swift:405-413. Swift: panelHeads(of:).
    public ValueList<ChatThread> PanelHeads(Guid parentID) =>
        new(Children(parentID).Where(thread => thread.IsInPanel && !thread.IsArchived && thread.IsHydraHead)
            .OrderBy(thread => thread.Hydra?.Index ?? long.MaxValue).ThenBy(thread => thread.CreatedAt)); // AppModel.swift:405-413

    // AppModel.swift:417-419. Swift: panelSubagent(of:).
    public ChatThread? PanelSubagent(Guid parentID) =>
        Children(parentID).FirstOrDefault(thread => thread.IsInPanel && !thread.IsArchived && !thread.IsHydraHead && !thread.IsSideChat); // AppModel.swift:417-419; PORT-ONLY (web surfaces W6): the side chat has its own pane

    // AppModel.swift:421-423
    public void ToggleHelpersFold(Guid parentID) =>
        UpdateThread(parentID, thread => thread with { ExpandsHelpers = !thread.ExpandsHelpers }); // AppModel.swift:421-423

    // AppModel.swift:425-430. Swift: runtime(for:).
    public ThreadRuntime Runtime(Guid id) =>
        runtimes.TryGetValue(id, out var runtime) ? runtime : runtimes[id] = WithSkillsOffers(new ThreadRuntime(id, this)); // AppModel.swift:425-430

    // PORT-ONLY (SK-14, ED-384): once the app's skills host is bound, a chat gets its offer flow; with none bound (tests, a host that could not open) the chat has none and the provider is never held.
    private static ThreadRuntime WithSkillsOffers(ThreadRuntime runtime)
    {
        Marrow.Services.Port.Skills.Host.SkillsHost host;
        try { host = Marrow.Services.Port.Skills.Host.SkillsSeams.Host.Current; }
        catch (SwiftTrapException) { return runtime; }
        Marrow.App.Port.Skills.SkillsOfferFlows.Create(runtime, host);
        return runtime;
    }

    // AppModel.swift:432-434. Swift: existingRuntime(for:).
    public ThreadRuntime? ExistingRuntime(Guid id) =>
        runtimes.TryGetValue(id, out var runtime) ? runtime : null; // AppModel.swift:432-434

    // AppModel.swift:437
    public ValueList<ThreadRuntime> LiveRuntimes =>
        new(runtimes.Values); // AppModel.swift:437

    // AppModel.swift:441-445
    public void WarmDocuments(IReadOnlyList<Guid> ids) =>
        DocumentPrefetch.Shared.Warm(ids.Where(id => !runtimes.ContainsKey(id)).ToList()); // AppModel.swift:441-445

    // AppModel.swift:455-463. Swift: neighbors(of:).
    public ValueList<Guid> Neighbors(Guid? id) =>
        new(NeighborIDs(id)); // AppModel.swift:455-463

    private void WarmNeighbors(Guid? id) => WarmDocuments(Neighbors(id)); // AppModel.swift:449-451

    private void ScheduleIdleSessionStop(Guid? leavingID)
    {
        if (SelectedThreadID is { } selectedID && idleSessionStops.Remove(selectedID, out var selectedStop)) selectedStop.Cancel(); // AppModel.swift:471-472
        if (leavingID is not { } id || !runtimes.ContainsKey(id)) return; // AppModel.swift:473-476
        if (idleSessionStops.Remove(id, out var previousStop)) previousStop.Cancel(); // AppModel.swift:477
        var weakSelf = new WeakReference<AppModel>(this);
        idleSessionStops[id] = MainActor.Spawn(async () =>
        {
            while (true)
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(600)); // AppModel.swift:478
                if (SwiftTask.IsCancelled || !weakSelf.TryGetTarget(out var model)
                    || model.SelectedThreadID == id || !model.runtimes.TryGetValue(id, out var runtime)) return; // AppModel.swift:479-480
                if (runtime.IsRunning) continue; // AppModel.swift:481
                runtime.StopSession(); // AppModel.swift:482
                model.idleSessionStops.Remove(id); // AppModel.swift:483
                model.ReleaseIdleRuntime(id); // AppModel.swift:484
                return; // AppModel.swift:485
            }
        });
    } // AppModel.swift:470-488

    private IReadOnlyList<Guid> NeighborIDs(Guid? id)
    {
        if (id is not { } wanted || Thread(wanted) is not { } thread || Project(thread.ProjectID) is not { } project) return [];
        var order = ThreadsIn(project);
        var index = -1;
        for (var current = 0; current < order.Count; current++)
            if (order[current].Id == wanted) { index = current; break; }
        if (index < 0) return [];
        var neighbors = new List<Guid>();
        if (index > 0) neighbors.Add(order[index - 1].Id);
        if (index + 1 < order.Count) neighbors.Add(order[index + 1].Id);
        return neighbors;
    } // AppModel.swift:455-463

    // AppModel.swift:492-506. @discardableResult.
    public bool ReleaseIdleRuntime(Guid id)
    {
        if (!runtimes.TryGetValue(id, out var runtime) || SelectedThreadID == id || Thread(id) is not { } thread
            || thread.ParentThreadID is { } parentID && parentID == SelectedThreadID
            || !runtime.HoldsOnlyPersistedState || sessionsToRelease.Contains(id)
            || AutoContinue.ResumesAt.ContainsKey(id) || Terminals.Sessions(id).Count > 0
            || RunningDroppyHeads(id) != 0) return false; // AppModel.swift:493-499, ported as runningDroppyHeads(of:) == 0 // HD-001
        runtime.SaveNow(); // AppModel.swift:502
        runtime.CancelPersistence(); // AppModel.swift:503
        runtimes.Remove(id); // AppModel.swift:504
        Marrow.App.Views.Chat.TimelineBlockCache.Forget(id); // PORT-ONLY (ED-NEW-perf-memory-3): the block cache would keep the released transcript alive
        return true; // AppModel.swift:505
    }

    // AppModel.swift:508-511
    public string? WorkingDirectory => SelectedThread is { } thread && Project(thread.ProjectID) is { } project
        ? thread.WorktreePath ?? project.Path
        : null; // AppModel.swift:508-511

    // MARK: - Projects

    // AppModel.swift:515-524. Swift: addProject(at:), @discardableResult.
    public Project AddProject(SwiftURL url) => AddProjectPath(SwiftPath.StandardizedFileURLPath(FileSystemPath(url), null), url); // AppModel.swift:516-524

    // url.path as the file system reads it: a Windows folder's file URL ("file:///C:/x") has the Path "/C:/x", whose leading
    // slash is the URL's, not the drive's (ProjectFile.Scripts strips it the same way); without this the folder chosen in the
    // panel resolves against the current directory (SidebarViewTests STOP App.Views.Sidebar.SidebarView #4).
    private static string FileSystemPath(SwiftURL url)
    {
        var path = url.Path;
        return OperatingSystem.IsWindows() && path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':' ? path[1..] : path;
    }

    private Project AddProjectPath(string path, SwiftURL? projectURL = null)
    {
        var standardized = SwiftPath.StandardizedFileURLPath(path, null); // AppModel.swift:517
        if (projects.FirstOrDefault(project => SwiftPath.SamePath(project.Path, standardized)) is { } existing) return existing; // AppModel.swift:518-519
        var added = new Project(SwiftPath.LastPathComponent(standardized), standardized);
        added = added with { Scripts = ProjectFile.Scripts(projectURL ?? added.Url) }; // AppModel.swift:520-523
        Projects = Projects.Appending(added); // AppModel.swift:524
        ScheduleSave();
        return added;
    }

    // AppModel.swift:526-529
    public void ChooseProjectFolder()
    {
        if (AddProjectsFromPanel() is not { } added) return; // AppModel.swift:527
        NewThread(added); // AppModel.swift:528
    }

    // AppModel.swift:535-547. @discardableResult. The folder panel: every chosen folder joins the sidebar as a project and the
    // last one added comes back, or null when the panel was cancelled. Nothing else is made here, so a caller that already has
    // a thread on screen (the new-thread prompt) can move that thread into the folder instead of leaving a second empty one.
    // NSOpenPanel is the OpenPanel seam (PR-SIDEBAR-012), the same one the sidebar's Add project row drives.
    public Project? AddProjectsFromPanel() // AppModel.swift:535-547
    {
        var panel = new OpenPanel
        {
            CanChooseDirectories = true, // AppModel.swift:537
            CanChooseFiles = false, // AppModel.swift:538
            AllowsMultipleSelection = true, // AppModel.swift:539
            Prompt = "Add project", // AppModel.swift:540
            Message = "Choose a folder for your agents to work in.", // AppModel.swift:541
        };
        if (panel.RunModal() != ModalResponse.OK) return null; // AppModel.swift:542
        Project? added = null; // AppModel.swift:543
        foreach (var url in panel.Urls) added = AddProject(url); // AppModel.swift:544
        return added; // AppModel.swift:545
    }

    // AppModel.swift:549-556
    public void UpdateProject(Guid id, Func<Project, Project> change) =>
        UpdateProjectValue(id, change); // AppModel.swift:549-556

    private void UpdateProjectValue(Guid id, Func<Project, Project> change)
    {
        var index = projects.ToList().FindIndex(project => project.Id == id); // AppModel.swift:549-552
        if (index < 0) return;
        var changed = change(projects[index]);
        if (changed == projects[index]) return; // AppModel.swift:553
        Projects = projects.SetItem(index, changed); // AppModel.swift:554
        ScheduleSave();
    }

    // AppModel.swift:734-746
    private void UpdateThreads(IReadOnlyList<Guid> ids, Func<long, ChatThread, ChatThread> change)
    {
        // ED-NEW-lean-16 (follow-up): the replaced threads only, laid over the held list in one copy (was a list copy, then the array)
        var replaced = new Dictionary<int, ChatThread>();
        var changed = false;
        for (var position = 0; position < ids.Count; position++)
        {
            if (!threadIndex.Positions.TryGetValue(ids[position], out var storedIndex)) continue;
            var index = checked((int)storedIndex);
            var previous = replaced.TryGetValue(index, out var earlier) ? earlier : threads[index];
            var next = change(position, previous);
            replaced[index] = next;
            changed |= previous != next;
        }
        if (!changed) return;
        var value = new ValueList<ChatThread>(new SomeReplaced(threads, replaced));
        if (!AssignThreadsInPlace(value, ChangedIndices(threads, replaced))) Threads = value; // ED-NEW-lean-16
        ScheduleSave();
    }

    // AppModel.swift:558-567
    public void RemoveProject(Project project)
    {
        var projectThreads = threads.Where(thread => thread.ProjectID == project.Id).ToList();
        foreach (var thread in projectThreads) DiscardThreadState(thread); // AppModel.swift:559-561
        var removedThreadIDs = projectThreads.Select(thread => thread.Id).ToHashSet();
        if (SelectedThreadID is { } selectedID && removedThreadIDs.Contains(selectedID)) SelectedThreadID = null;
        Threads = new ValueList<ChatThread>(threads.Where(thread => thread.ProjectID != project.Id)); // AppModel.swift:562
        Projects = new ValueList<Project>(projects.Where(candidate => candidate.Id != project.Id)); // AppModel.swift:563
        if (Settings.LastProjectID == project.Id) Settings.LastProjectID = null; // AppModel.swift:564
        if (SelectedThread is null) SelectedThreadID = null; // AppModel.swift:565
        ScheduleSave(); // AppModel.swift:566
    }

    // MARK: - Threads

    // AppModel.swift:574-602
    public void OpenThreadOnLaunch() =>
        OpenThreadOnLaunchCore();

    private void OpenThreadOnLaunchCore()
    {
        if (CurrentProject is not { } project) return; // AppModel.swift:575
        var unused = threads
            .Where(thread => thread.ProjectID == project.Id && !thread.IsArchived && !thread.IsSettled && !thread.IsHelper
                && thread.LastStatus is null && !thread.HasCustomTitle && thread.Title == ChatThread.Untitled)
            .OrderByDescending(thread => thread.CreatedAt)
            .FirstOrDefault(); // AppModel.swift:576-581
        if (unused is not null)
        {
            var fresh = ApplyNewThreadPill(MakeThread(project, null)); // OD-0114 / ED-403; AppModel.swift:582-585
            UpdateThreadValue(unused.Id, thread =>
            {
                if (thread.Provider != fresh.Provider || thread.ProviderAccountID != fresh.ProviderAccountID)
                    thread = thread with { ProviderSessionID = null, ProviderResumeAt = null }; // AppModel.swift:587-590
                return thread with
                {
                    Provider = fresh.Provider,
                    ProviderAccountID = fresh.ProviderAccountID,
                    PillID = fresh.PillID,
                    PillStepIndex = fresh.PillStepIndex,
                    PillStepAccountID = fresh.PillStepAccountID,
                    Model = fresh.Model,
                    Effort = fresh.Effort,
                    FastMode = fresh.FastMode,
                    HydraPairID = fresh.HydraPairID,
                    HydraEnabled = fresh.HydraEnabled,
                }; // AppModel.swift:591-597
            }, accountSwitch: true);
            SelectedThreadID = unused.Id; // AppModel.swift:598
        }
        else
        {
            NewThread(project); // AppModel.swift:599-601
        }
    }

    // AppModel.swift:604-620. Swift: newThread(in:workspace:), @discardableResult.
    public ChatThread? NewThread(Project? project = null, WorkspaceMode? workspace = null) =>
        CreateNewThread(project, workspace); // AppModel.swift:605-620

    private ChatThread? CreateNewThread(Project? project, WorkspaceMode? workspace)
    {
        var destination = project ?? CurrentProject;
        if (destination is null)
        {
            ChooseProjectFolder(); // AppModel.swift:606-609
            return null;
        }
        var thread = ApplyNewThreadPill(MakeThread(destination, SelectedThread)); // OD-0114 / ED-403; AppModel.swift:610
        Threads = Threads.Appending(thread); // AppModel.swift:611
        UpdateProject(destination.Id, existing => existing with { IsExpanded = true }); // AppModel.swift:612
        RememberLastProject(destination.Id); // AppModel.swift:613
        SelectedThreadID = thread.Id; // AppModel.swift:614
        ScheduleSave(); // AppModel.swift:615
        if ((workspace ?? Settings.DefaultWorkspaceMode) == WorkspaceMode.Worktree)
            MainActor.Spawn(async () => await CreateWorktree(thread.Id)); // AppModel.swift:616-617
        return thread;
    }

    // AppModel.swift:657-712. A chat as it would start in `project`: on `current`'s provider, model and pair when made from an
    // open chat, otherwise on what was last used. Not yet in `threads`.
    private ChatThread MakeThread(Project project, ChatThread? current)
    {
        var installed = SwiftEnum<ProviderKind>.AllCases
            .Where(kind => Providers.Status(kind).IsInstalled && Settings.IsEnabled(kind)).ToList(); // AppModel.swift:660
        var pickerPairs = HydraPickerPairs; // AppModel.swift:662
        // The project's own pair, when it has one, sets the provider before anything else.
        var projectPair = global::Marrow.Core.Models.HydraPair.ProjectPair(project.HydraPairID, Settings.HydraEnabled, pickerPairs); // AppModel.swift:662
        // With no chat to carry from (a fresh launch), the pair last entered from the picker is where the new chat starts.
        // Before any pair has been entered since this was remembered, the chat last worked in stands in: its pair if it led
        // one, and none if it had left its pair for a model of its own.
        var lastPairID = Settings.LastHydraPairID; // AppModel.swift:667
        if (lastPairID is null)
        {
            ChatThread? recentThread = null; // AppModel.swift:668 max { $0.updatedAt < $1.updatedAt }: the first of the latest
            foreach (var candidate in threads)
            {
                if (candidate.IsHelper) continue;
                if (recentThread is null || candidate.UpdatedAt > recentThread.UpdatedAt) recentThread = candidate;
            }
            lastPairID = recentThread?.HydraPairID;
        }
        var lastPair = current is null && Settings.HydraEnabled
            ? lastPairID is { } lastID ? pickerPairs.FirstOrDefault(pair => pair.Id == lastID) : null
            : null; // AppModel.swift:669-671
        var provider = projectPair?.Provider ?? current?.Provider ?? lastPair?.Provider ?? Settings.DefaultProvider; // AppModel.swift:672
        if (installed.Count > 0 && !installed.Contains(provider)) provider = installed[0]; // AppModel.swift:673
        // A chat that starts on the project's pair takes that pair's lead model. A pair with no lead model keeps the current
        // chat's model, as the picker does.
        var carriesModel = projectPair?.OrchestratorModel is null && current?.Provider == provider; // AppModel.swift:676
        var model = carriesModel ? current?.Model : Providers.DefaultModel(provider)?.Id; // AppModel.swift:677
        var preference = Settings.Preference(provider, model); // AppModel.swift:678
        var effort = carriesModel ? current?.Effort : preference.Effort ?? Settings.LastEffort(provider); // AppModel.swift:679
        var fastMode = carriesModel ? current?.FastMode ?? false : preference.FastMode; // AppModel.swift:680
        var thread = new ChatThread(project.Id, provider, model, effort, Settings.DefaultRuntimeMode, fastMode); // AppModel.swift:681-688
        // A chat made from one that leads a pair carries the pair along with the model. With no chat to carry from, the last
        // pair is where the new chat starts, on its lead model and effort, provided its provider is still installed. The
        // project's pair wins over all of that: the user set it as the rule for this project, so every new chat here starts on it.
        if (projectPair is { } pair)
        {
            var lead = HydraLead(pair, thread); // AppModel.swift:695
            thread = thread with { Model = lead.Model, Effort = lead.Effort, FastMode = lead.FastMode, HydraPairID = pair.Id, HydraEnabled = true }; // AppModel.swift:696-700
        }
        else if (carriesModel)
        {
            thread = thread with { HydraPairID = current?.HydraPairID }; // AppModel.swift:702
        }
        else if (lastPair is { } last && last.Provider == provider)
        {
            var lead = HydraLead(last, thread); // AppModel.swift:704
            thread = thread with { Model = lead.Model, Effort = lead.Effort, FastMode = lead.FastMode, HydraPairID = last.Id, HydraEnabled = true }; // AppModel.swift:705-709
        }
        return thread;
    }

    // AppModel.swift:623-646. @discardableResult.
    public async Task<ChatThread?> ForwardThread(Guid sourceID) // AppModel.swift:624-646
    {
        if (Thread(sourceID) is not { } source || Project(source.ProjectID) is null) return null;
        var sourceRuntime = Runtime(sourceID);
        await sourceRuntime.EnsureLoaded();
        if (Thread(sourceID) is not { } resolved || Project(resolved.ProjectID) is not { } project) return null;
        var continuation = ThreadContinuation.Capture(sourceID, resolved.Title, sourceRuntime.SnapshotForPersistence());
        var forwarded = MakeThread(project, resolved) with
        {
            Title = "Continued: " + resolved.Title,
            HasCustomTitle = true,
            HiddenFromAssistant = resolved.HiddenFromAssistant || Marrow.Core.Port.Assistant.FeedChatFacts.From(resolved, Thread).Hidden, // PORT-ONLY (ED-NEW-dextra-cx-1): preserve inherited hiding when the continuation loses its parent link
        };
        Threads = Threads.Appending(forwarded);
        UpdateProject(project.Id, value => value with { IsExpanded = true });
        RememberLastProject(project.Id);
        var newRuntime = Runtime(forwarded.Id);
        if (!await newRuntime.InstallContinuation(continuation) || Thread(forwarded.Id) is null) return null;
        SelectedThreadID = forwarded.Id;
        ScheduleSave();
        return forwarded;
    }

    // AppModel.swift:705-712
    public void UpdateThread(Guid id, Func<ChatThread, ChatThread> change) =>
        UpdateThreadValue(id, change); // AppModel.swift:705-712

    private void UpdateThreadValue(Guid id, Func<ChatThread, ChatThread> change, bool accountSwitch = false)
    {
        if (!threadIndex.Positions.TryGetValue(id, out var position)) return; // AppModel.swift:705-708
        var index = checked((int)position);
        var changed = change(threads[index]); // AppModel.swift:709-710
        // PORT-ONLY: OD-0114 / ED-404. Account bindings belong to one service.
        if (changed.Provider != threads[index].Provider && !accountSwitch) changed = changed with { ProviderAccountID = null };
        if (changed == threads[index]) return; // AppModel.swift:711: nothing changed, nothing assigned or saved
        var before = threads[index];
        var value = new ValueList<ChatThread>(new OneReplaced(threads, index, changed)); // threads.SetItem(index, changed), one copy
        if (!AssignThreadsInPlace(value, [index])) Threads = value; // AppModel.swift:712; ED-NEW-lean-16
        ScheduleSave(); // AppModel.swift:713
        IgnisThreadChanged(before, changed); // PORT-ONLY ED-NEW-ignis-guards: a model switch's cost notice
    }

    // AppModel.swift:723-741. Swift: moveThread(_:toProject:). Moves a thread to another project's folder: what the old folder's
    // session and worktree left on it goes, the destination project opens so the row has a place to land, the last-worked-in
    // project follows, and the sidebar is asked to bring the moved row into view. The runtime stops first, so no turn carries
    // on against a folder the chat has left. The model itself never animates (callers wrap it in withAnimation).
    public void MoveThread(Guid id, Project toProject)
    {
        ExistingRuntime(id)?.StopSession(); // AppModel.swift:730
        UpdateThread(id, thread => thread with
        {
            ProjectID = toProject.Id,
            WorktreePath = null,
            Branch = null,
            ProviderSessionID = null,
            ProviderResumeAt = null,
        }); // AppModel.swift:731-737
        UpdateProject(toProject.Id, project => project with { IsExpanded = true }); // AppModel.swift:738
        RememberLastProject(toProject.Id); // AppModel.swift:739
        SidebarRevealThreadID = id; // AppModel.swift:740
    }

    // AppModel.swift:748-755. Swift: rename(_:to:).
    public void Rename(Guid id, string title) // AppModel.swift:748-755
    {
        var trimmed = title.Trimmed();
        if (trimmed.Length == 0) return;
        UpdateThread(id, thread => thread with { Title = trimmed, HasCustomTitle = true });
    }

    // AppModel.swift:757-772
    public void Archive(Guid id) // AppModel.swift:757-772
    {
        foreach (var helper in Helpers(id)) Archive(helper.Id);
        StopSideChat(id); // PORT-ONLY (web surfaces W6): an archived thread's side chat stops with it
        if (SelectedThreadID == id) SelectNeighbor(id);
        ExistingRuntime(id)?.StopSession();
        ReleaseHydraCopy(id); // AppModel.swift:771
        UpdateThread(id, thread => thread with
        {
            IsArchived = true,
            IsPinned = false,
            IsSettled = false,
            SettledAt = null,
        });
        UpdateDockBadge();
    }

    // AppModel.swift:774-781
    public void Unarchive(Guid id) // AppModel.swift:774-781
    {
        UpdateThread(id, thread => thread with { IsArchived = false });
        foreach (var helper in threads.Where(thread => thread.ParentThreadID == id && thread.IsArchived).ToList())
            UpdateThread(helper.Id, thread => thread with { IsArchived = false });
        UpdateDockBadge();
    }

    // AppModel.swift:787-807
    public void Settle(Guid id, bool sounds = true) // AppModel.swift:787-807
    {
        if (Thread(id) is not { IsSettled: false, IsHelper: false }) return;
        var now = SwiftDate.Now;
        UpdateThread(id, thread => thread with
        {
            IsSettled = true,
            SettledAt = now,
            IsPinned = false,
            HasUnread = false,
        });
        foreach (var helper in Helpers(id))
            UpdateThread(helper.Id, thread => thread with { IsSettled = true, SettledAt = now });
        UpdateDockBadge();
        if (sounds && Settings.SettleSound) SettleChime.Play();
        SaveLibrary();
    }

    // AppModel.swift:810-826
    public void Reopen(Guid id) // AppModel.swift:810-826
    {
        if (Thread(id) is not { IsSettled: true }) return;
        var now = SwiftDate.Now;
        UpdateThread(id, thread => thread with
        {
            IsSettled = false,
            SettledAt = null,
            UpdatedAt = now,
            ActivityOrder = null,
            ActivityOrderDay = null,
        });
        UpdateThreads(Children(id).Where(thread => thread.IsSettled).Select(thread => thread.Id).ToList(), (_, thread) => thread with
        {
            IsSettled = false,
            SettledAt = null,
        });
        SaveLibrary();
    }

    // AppModel.swift:829-831
    public void SettleAnimated(Guid id, bool sounds = true) // AppModel.swift:829-831
        => Transaction.WithAnimation(Chrome.SettleFlight, () => Settle(id, sounds));

    // AppModel.swift:834-836
    public void ReopenAnimated(Guid id) // AppModel.swift:834-836
        => Transaction.WithAnimation(Chrome.SettleFlight, () => Reopen(id));

    // AppModel.swift:839-842
    public void ReopenIfSettled(Guid id) // AppModel.swift:839-842
    {
        if (Thread(id)?.IsSettled == true) ReopenAnimated(id); // AppModel.swift:850
    }

    // AppModel.swift:853-858. What the menu's, the shortcut's and the palette's finish command does to the selected thread:
    // settles or archives it as Settings chose, and reopens a settled one. The title has no helper case (a settled helper
    // reads "Reopen thread", an unsettled one "Settle thread"), the source's own reading.
    public string FinishActionTitle // AppModel.swift:855-858
        => Settings.ThreadFinishAction == ThreadFinishAction.Settle && SelectedThread?.IsSettled == true
            ? "Reopen thread"
            : Settings.ThreadFinishAction.Title + " thread";

    // AppModel.swift:860-863
    public string FinishActionSymbol
    {
        get
        {
            if (Settings.ThreadFinishAction == ThreadFinishAction.Settle && SelectedThread?.IsSettled == true) return "arrow.uturn.backward";
            return Settings.ThreadFinishAction == ThreadFinishAction.Settle ? "checkmark.circle" : "archivebox";
        }
    }

    // AppModel.swift:865-882
    public void FinishSelectedThread() // AppModel.swift:865-882
    {
        if (SelectedThreadID is not { } id || Thread(id) is not { } thread) return; // AppModel.swift:866
        if (Settings.ThreadFinishAction == ThreadFinishAction.Archive || thread.IsHelper)
        {
            // A helper has no place of its own to settle into; it goes the archive's way.
            Transaction.WithAnimation(Chrome.PanelSlide, () => Archive(id)); // AppModel.swift:869-870
            return;
        }
        PostFinishRequest(id, thread.IsSettled); // AppModel.swift:872-880
    }

    // AppModel.swift:887-898. Settles the selected thread, or reopens it when it is settled already: the shortcut's toggle,
    // whatever General chose for Finish. A helper has no place of its own to settle into, so it stays as it is.
    public void SettleSelectedThread() // AppModel.swift:887-898
    {
        if (SelectedThreadID is not { } id || Thread(id) is not { IsHelper: false } thread) return; // AppModel.swift:888
        PostFinishRequest(id, thread.IsSettled); // AppModel.swift:889-897
    }

    // AppModel.swift:872-880, :889-897. The request is posted for the thread's row to take up; with no row to take it (the
    // sidebar is hidden, say) the thread just moves 80 ms later.
    private void PostFinishRequest(Guid id, bool reopens)
    {
        var request = new FinishRequest(id, reopens);
        AppModelFinishRequest = request;
        MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(80)); // AppModel.swift:876
            if (AppModelFinishRequest != request) return; // AppModel.swift:877
            AppModelFinishRequest = null; // AppModel.swift:878
            if (request.Reopens) ReopenAnimated(id); else SettleAnimated(id); // AppModel.swift:879
        });
    }

    // AppModel.swift:900-921. Asks before archiving the selected thread: its row shows the question, with Archive the answer
    // Return gives and Delete beside it.
    public void AskToArchiveSelectedThread() // AppModel.swift:902-921
    {
        if (SelectedThreadID is not { } id || Thread(id) is null) return; // AppModel.swift:903
        // Nothing in it, nothing typed for it: there is nothing to keep, so it goes the way an empty tab does, with no
        // question. A history still being read is not "nothing".
        if (ExistingRuntime(id) is { } runtime && !runtime.IsLoadingHistory && runtime.Turns.Count == 0
            && runtime.Continuation is null && runtime.Draft.IsEmpty && runtime.FollowUps.Count == 0 && !runtime.IsRunning)
        {
            Transaction.WithAnimation(Chrome.PanelSlide, () => Delete(id)); // AppModel.swift:908
            return; // AppModel.swift:909
        }
        var request = new ArchiveRequest(id); // AppModel.swift:911
        AppModelArchiveRequest = request; // AppModel.swift:912
        MainActor.Spawn(async () =>
        {
            // No row to ask on (the sidebar is hidden, say): the thread is archived, which is what Return would have done.
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(80)); // AppModel.swift:916
            if (AppModelArchiveRequest != request) return; // AppModel.swift:917
            AppModelArchiveRequest = null; // AppModel.swift:918
            Transaction.WithAnimation(Chrome.PanelSlide, () => Archive(id)); // AppModel.swift:919
        });
    }

    // AppModel.swift:923. Swift: finishRequest (R-NAME-3).
    public FinishRequest? AppModelFinishRequest { get => Read(finishRequest); set => Set(ref finishRequest, value); }

    // AppModel.swift:932. Swift: archiveRequest (R-NAME-3).
    public ArchiveRequest? AppModelArchiveRequest { get => Read(archiveRequest); set => Set(ref archiveRequest, value); }

    // AppModel.swift:934-957
    public void Delete(Guid id, bool removeWorktree = false) =>
        DeleteThread(id, removeWorktree); // AppModel.swift:934-957

    private void DeleteThread(Guid id, bool removeWorktree)
    {
        if (Thread(id) is not { } thread) return; // AppModel.swift:935
        foreach (var helper in Children(id)) Delete(helper.Id); // AppModel.swift:936-937
        if (SelectedThreadID == id) SelectNeighbor(id); // AppModel.swift:938
        // A head deleted mid-job reports to its lead as stopped, so the batch it was in settles and the lead's waiting heads and
        // merge go on rather than waiting for it.
        if (thread.IsHydraHead && thread.Hydra?.IsFinished == false) FinishHydraHead(id, TurnStatus.Interrupted, null); // AppModel.swift:950-952
        DiscardThreadState(thread); // AppModel.swift:944
        Threads = new ValueList<ChatThread>(threads.Where(candidate => candidate.Id != id)); // AppModel.swift:945
        if (Project(thread.ProjectID) is { } project)
        {
            var git = new Git(project.Path); // AppModel.swift:946-947
            var worktree = removeWorktree || thread.Hydra?.HasOwnCopy == true ? thread.WorktreePath : null; // AppModel.swift:948-949
            MainActor.Spawn(async () =>
            {
                await git.DeleteCheckpoints(id); // AppModel.swift:950-951
                if (worktree is not null) await SwiftTry.IgnoreAsync(() => git.RemoveWorktree(worktree)); // AppModel.swift:952
            }); // AppModel.swift:950-953
        }
        UpdateDockBadge(); // AppModel.swift:955
        SaveLibrary(); // AppModel.swift:956
    }

    // AppModel.swift:961-993
    public void DeleteArchivedThreads() =>
        DeleteAllArchivedThreads();

    private void DeleteAllArchivedThreads()
    {
        var archived = threads.Where(thread => thread.IsArchived).ToList(); // AppModel.swift:962
        if (archived.Count == 0) return; // AppModel.swift:963
        var archivedIDs = archived.Select(thread => thread.Id).ToHashSet(); // AppModel.swift:964-965
        var removed = archived.Concat(threads.Where(helper => helper.ParentThreadID is { } parent
            && archivedIDs.Contains(parent) && !archivedIDs.Contains(helper.Id))).ToList(); // AppModel.swift:966-969
        if (SelectedThreadID is { } selectedID && removed.Any(thread => thread.Id == selectedID)) SelectedThreadID = null; // AppModel.swift:970-972
        var cleanup = new List<(string Directory, Guid ThreadID, string? Copy)>(); // AppModel.swift:973-974
        foreach (var thread in removed)
        {
            DiscardThreadState(thread, deletingDocument: false); // AppModel.swift:975-976
            if (Project(thread.ProjectID) is { } project)
                cleanup.Add((project.Path, thread.Id, thread.Hydra?.HasOwnCopy == true ? thread.WorktreePath : null)); // AppModel.swift:977-979
        }
        Storage.DeleteDocuments(removed.Select(thread => thread.Id).ToList()); // AppModel.swift:981
        MainActor.Spawn(async () =>
        {
            foreach (var item in cleanup)
            {
                var git = new Git(item.Directory); // AppModel.swift:982-984
                await git.DeleteCheckpoints(item.ThreadID); // AppModel.swift:985
                if (item.Copy is not null) await SwiftTry.IgnoreAsync(() => git.RemoveWorktree(item.Copy)); // AppModel.swift:986
            }
        }); // AppModel.swift:982-988
        var removedIDs = removed.Select(thread => thread.Id).ToHashSet(); // AppModel.swift:989
        Threads = new ValueList<ChatThread>(threads.Where(thread => !removedIDs.Contains(thread.Id))); // AppModel.swift:990
        UpdateDockBadge(); // AppModel.swift:991
        ScheduleSave(); // AppModel.swift:992
    }

    private void DiscardThreadState(ChatThread thread, bool deletingDocument = true)
    {
        Marrow.App.Port.PcControl.TactusComposition.ChatClosed(thread.Id); // PORT-ONLY OD-0145 (Tactus, ED-NEW-pcctl-32): its engine, mirror rows and chat grants go
        if (idleSessionStops.Remove(thread.Id, out var stop)) stop.Cancel(); // AppModel.swift:1070-1071
        if (runtimes.Remove(thread.Id, out var runtime))
        {
            runtime.CancelPersistence(); // AppModel.swift:1072
            runtime.StopSession(); // AppModel.swift:1073
        }
        Marrow.App.Views.Chat.TimelineBlockCache.Forget(thread.Id); // PORT-ONLY (ED-NEW-perf-memory-3): a deleted thread's transcript is not kept
        Terminals.CloseAll(thread.Id); // AppModel.swift:1074-1075
        if (deletingDocument) Storage.DeleteDocument(thread.Id); // AppModel.swift:1076-1077
    }

    private void SelectNeighbor(Guid id)
    {
        var order = SidebarThreads;
        var index = order.ToList().FindIndex(thread => thread.Id == id);
        var neighbor = index >= 0 && index + 1 < order.Count ? order[index + 1]
            : index > 0 ? order[index - 1] : null;
        SelectedThreadID = neighbor?.Id; // AppModel.swift:1079-1086
    }

    // MARK: - Subagents

    // AppModel.swift:1003-1005. Swift: subagent(of:).
    public ChatThread? Subagent(Guid parentID) =>
        PanelSubagent(parentID); // AppModel.swift:1003-1005

    // AppModel.swift:1009-1012
    public void InsertThread(ChatThread thread)
    {
        Threads = Threads.Appending(thread); // AppModel.swift:1010
        ScheduleSave(); // AppModel.swift:1011
    }

    // AppModel.swift:1020-1051. Swift: spawnSubagent(from:title:prompt:), @discardableResult.
    public ChatThread? SpawnSubagent(Guid parentID, string title, string prompt)
    {
        if (InstallerHoldsNewTurns) return null;
        if (Thread(parentID) is not { } parent) return null; // AppModel.swift:1022
        if (Subagent(parentID) is { } existing) // AppModel.swift:1023
        {
            var existingRuntime = Runtime(existing.Id); // AppModel.swift:1024
            // PORT-ONLY (Dextra AS-7, ED-NEW-dextra-as7-15): the parent wrote this prompt, so the helper carries the
            // parent's taint as it stands now, not only as it stood when the helper opened.
            DextraHelperPrompted(parent, existing);
            if (existingRuntime.IsRunning) // AppModel.swift:1025
            {
                existingRuntime.EnqueueFollowUp(prompt, []); // AppModel.swift:1026
            }
            else
            {
                existingRuntime.Draft = new ComposerDraft { Text = prompt }; // AppModel.swift:1028-1029
                // PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-14): a mission's helper waits at the agent gate first.
                if (existing.MissionID is null) existingRuntime.Send(); // AppModel.swift:1030
                else MainActor.Spawn(async () =>
                {
                    await DextraAdmitHelper(existing);
                    existingRuntime.Send();
                });
            }
            return existing; // AppModel.swift:1032
        }

        var thread = new ChatThread( // AppModel.swift:1033-1040
            parent.ProjectID,
            parent.Provider,
            parent.Model,
            parent.Effort,
            parent.RuntimeMode,
            parent.FastMode)
        {
            ParentThreadID = parentID, // AppModel.swift:1041
            IsInPanel = true, // AppModel.swift:1042
            Title = title, // AppModel.swift:1043
            HasCustomTitle = true, // AppModel.swift:1044
            HiddenFromAssistant = parent.HiddenFromAssistant, // PORT-ONLY (AS-3a, ED-NEW-dextra-as3a-2): a helper of a hidden chat is hidden too
            // PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-12): a subagent a mission's chat opens is the mission's too, so it is
            // sealed, capped and in its own job like every mission descendant (12.3: never Full access, never the owner's rules).
            MissionID = parent.MissionID,
            AssistantID = parent.MissionID is null ? null : parent.AssistantID,
            // PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-14): it works in the mission's own folder, never the owner's tree.
            WorktreePath = parent.MissionID is null ? null : parent.WorktreePath,
            Branch = parent.MissionID is null ? null : parent.Branch,
        };
        // PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-14): a mission chat's helper is one of Dextra's agents of a running
        // mission (its approvals through the policy engine and the queue, Stop and Finish reaching it, the gate counting
        // it); with no running mission to take it, it does not open.
        if (thread.MissionID is not null && !DextraHelperAdded(parent, thread))
        {
            Runtime(parentID).AppendHydraNote("Dextra: a helper of a mission opens only while Dextra runs that mission.");
            return null;
        }
        Threads = Threads.Appending(thread); // AppModel.swift:1045
        ScheduleSave(); // AppModel.swift:1046
        var runtime = Runtime(thread.Id); // AppModel.swift:1047
        runtime.Draft = new ComposerDraft { Text = prompt }; // AppModel.swift:1048-1049
        if (thread.MissionID is null) runtime.Send(); // AppModel.swift:1050
        else MainActor.Spawn(async () => // PORT-ONLY (Dextra AS-5): a mission's helper waits at the agent gate first
        {
            await DextraAdmitHelper(thread);
            runtime.Send();
        });
        return thread; // AppModel.swift:1051
    }

    // AppModel.swift:1055-1068
    public void CloseSubagent(Guid id)
    {
        if (Thread(id) is null) return; // AppModel.swift:1056
        if (ExistingRuntime(id) is { } runtime) // AppModel.swift:1057
        {
            if (runtime.IsRunning) // AppModel.swift:1060
            {
                sessionsToRelease.Add(id); // AppModel.swift:1061
                runtime.Interrupt(); // AppModel.swift:1062
            }
            else
            {
                runtime.StopSession(); // AppModel.swift:1064
            }
        }
        UpdateThread(id, thread => thread with { IsInPanel = false }); // AppModel.swift:1067
    }

    // AppModel.swift:1089-1092
    public ProjectScript? TopScript // AppModel.swift:1089-1092
    {
        get
        {
            if (SelectedThread is not { } thread || Project(thread.ProjectID) is not { } project) return null;
            return project.Scripts.FirstOrDefault();
        }
    }

    // AppModel.swift:1094-1098. Swift: runScript(_:threadID:).
    public void RunScript(ProjectScript script, Guid threadID) // AppModel.swift:1094-1098
    {
        if (Thread(threadID) is not { } thread || Project(thread.ProjectID) is not { } project) return;
        Terminals.Run(script, threadID, thread.WorktreePath ?? project.Path);
        Runtime(threadID).IsTerminalVisible = true;
    }

    // AppModel.swift:1100-1108. Swift: selectThread(offset:).
    public void SelectThread(long offset) // AppModel.swift:1100-1108
    {
        var order = SidebarThreads;
        if (order.Count == 0) return;
        var currentIndex = SelectedThreadID is { } current
            ? order.ToList().FindIndex(thread => thread.Id == current)
            : -1;
        if (currentIndex < 0)
        {
            SelectedThreadID = order[0].Id;
            return;
        }
        var nextIndex = (int)(((currentIndex + offset) % order.Count + order.Count) % order.Count);
        SelectedThreadID = order[nextIndex].Id;
    }

    // AppModel.swift:1110-1114. Swift: selectThread(number:). Renamed: C# cannot overload SelectThread on the label alone.
    public void SelectThreadNumber(long number) // AppModel.swift:1110-1114
    {
        // PORT-ONLY (ED-NEW-session-hotkeys): number N is the Nth session row the sidebar shows (its layout, pinned first, the
        // activity groups, a collapsed group left out, search results), the row hold-Ctrl badges N; the source's order
        // (sidebarThreads, :384-390) stands while no sidebar has published one.
        var order = SessionNumbers.Order is { } published
            ? new ValueList<ChatThread>(published.Select(Thread).OfType<ChatThread>())
            : SidebarThreads;
        if (number < 1 || number > order.Count) return;
        var id = order[checked((int)(number - 1))].Id;
        SelectedThreadID = id;
        ComposerFocusRequested?.Invoke(id); // the chat opened is ready to type in
    }

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): the sidebar's session numbering and its hold-Ctrl badges.</summary>
    public Marrow.App.Views.Sidebar.SessionNumbers SessionNumbers { get; } = new();

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): Ctrl+1..9 opened this chat; its composer takes the keyboard.</summary>
    public event Action<Guid>? ComposerFocusRequested;

    // AppModel.swift:1116-1141. Swift: createWorktree(for:).
    public Task CreateWorktree(Guid threadID) =>
        CreateThreadWorktree(threadID); // AppModel.swift:1116-1141

    private async Task CreateThreadWorktree(Guid threadID)
    {
        if (Thread(threadID) is not { WorktreePath: null } thread || Project(thread.ProjectID) is not { } project) return; // AppModel.swift:1117
        var git = new Git(project.Path); // AppModel.swift:1118
        if (!await git.IsRepository() || !await git.HasCommits())
        {
            Alert = new AppAlert { Title = "Worktree unavailable", Message = "New worktrees need a git repository with at least one commit." }; // AppModel.swift:1119-1122
            return;
        }
        var suffix = SwiftString.Prefix(SwiftString.Lowercased(SwiftUUID.UuidString(threadID)), 8); // AppModel.swift:1123
        var folder = SwiftString.Lowercased(project.Name.Replace(" ", "-", StringComparison.Ordinal)); // AppModel.swift:1124
        var path = SwiftPath.AppendingPathComponent(Storage.WorktreesDirectory, folder + "-" + suffix); // AppModel.swift:1125
        var branch = "marrow/" + suffix; // DEVIATION OD-0012: F1 renames the worktree branch prefix.
        var baseBranch = (await git.Status())?.Branch; // AppModel.swift:1127
        try
        {
            await git.AddWorktree(path, branch, baseBranch); // AppModel.swift:1128-1129
            UpdateThread(threadID, current => current with { WorktreePath = path, Branch = branch }); // AppModel.swift:1130-1133
            foreach (var script in project.Scripts.Where(script => script.RunOnWorktreeCreate)) // AppModel.swift:1134
            {
                Terminals.Run(script, threadID, path); // AppModel.swift:1135
                Runtime(threadID).IsTerminalVisible = true; // AppModel.swift:1136
            }
        }
        catch (Exception error)
        {
            Alert = new AppAlert { Title = "Could not create a worktree", Message = error.Message }; // AppModel.swift:1138-1140
        }
    }

    // MARK: - Attention

    // AppModel.swift:1158-1169
    public void ThreadNeedsAttention(Guid id) // AppModel.swift:1158-1169
    {
        AssistantTaps.TapNeedsAttention(id); // PORT-ONLY (AS-2, ED-NEW-dextra-as2-8): the Assistant's live feed
        if (Thread(id) is not { } thread) return;
        if (IsApplicationActive() && IsOnScreen(thread))
        {
            MarkRead(id);
            return;
        }
        if (DecisionToastCovers(thread)) return; // PORT-ONLY (AS-9, ED-NEW-dextra-as9-1): a mission chat's decision makes its toast from Dextra's queue
        Notify(id, thread.Title, "Waiting for your decision.");
    }

    /// <summary>
    /// PORT-ONLY (Dextra AS-18, ED-NEW-dextra-as18-8): a chat's turn ended (the thread, how it ended, whether another turn follows). Raised on the main
    /// thread, after the Assistant's own taps. The voice conversation waits on it for the answer it speaks.
    /// </summary>
    public event Action<Guid, TurnStatus, bool>? ThreadTurnFinished;

    // AppModel.swift:1173-1216. Swift: turnFinished(_:status:continues:).
    public void TurnFinished(Guid id, TurnStatus status, bool continues) // AppModel.swift:1173-1216
    {
        if (IgnisTurnFinished(id, status)) return; // PORT-ONLY: Ignis (ED-NEW-ignis-runtime): a hidden keep-warm ping ends silently
        AssistantTaps.TapTurnFinished(id, status, continues); // PORT-ONLY (AS-2, ED-NEW-dextra-as2-8): the Assistant's live feed
        Dextra?.OnTurnFinished(id, status, continues); // PORT-ONLY (AS-5): a mission's lead or reviewer moves its mission on
        ThreadTurnFinished?.Invoke(id, status, continues); // PORT-ONLY (AS-18, ED-NEW-dextra-as18-8): the voice conversation waits on its chat's turn
        if (Thread(id) is { IsSideChat: true }) { SideChatTurnFinished(id, status); return; } // PORT-ONLY (web surfaces W6): a side chat's turn ends quietly
        var thread = Thread(id);
        var isVisible = IsApplicationActive() && thread is not null && IsOnScreen(thread);
        var isHead = thread?.IsHydraHead == true;
        UpdateThread(id, current => current with
        {
            LastStatus = status,
            UpdatedAt = SwiftDate.Now,
            HasUnread = isHead ? current.HasUnread : !isVisible,
        });
        UpdateDockBadge();
        if (sessionsToRelease.Remove(id)) ExistingRuntime(id)?.StopSession();
        if (AutoContinue.TurnFinished(id, status, continues)) return;
        // AppModel.swift:1195-1201. A head reports to its lead, which is the chat that chimes and notifies when
        // the whole job is done. A head with another turn coming (its report, after its budget ran out) reports
        // at the end of that one.
        if (isHead)
        {
            if (!continues && thread is { } head) HydraHeadTurnFinished(head, status);
            return;
        }
        if (continues) return;
        var chimed = Settings.ChimeWhenFinished && status != TurnStatus.Interrupted;
        if (chimed) FinishChime.Play();
        if (isVisible || !Settings.NotifyWhenFinished || thread is null || DecisionToastCovers(thread)) return; // AS-9: a mission's end is a status, never a push (13.1)
        var body = status switch
        {
            TurnStatus.Completed => "Finished.",
            TurnStatus.Interrupted => "Stopped.",
            TurnStatus.Failed => "Stopped with an error.",
            TurnStatus.Running => string.Empty,
            _ => string.Empty,
        };
        Notify(id, thread.Title, body, sound: !chimed);
    }

    // AppModel.swift:1158-1165: the selected thread itself, a helper in its parent's floating panel, or a sidebar helper shown
    // inline beneath its selected parent (only while the parent is unfolded and not settled). One reading of it, shared with
    // the notification gate.
    private bool IsOnScreen(ChatThread thread) => Marrow.App.Port.ToastNotifications.IsOnScreen(this, thread);

    // NSApp.isActive (AppModel.swift:1163, :1177, :1250, AppModel+HydraMerge.swift:869): the port's one reading of it,
    // shared with AppDelegate.swift's presentation gate (Port/ApplicationLifecycle.cs NSApplication.IsActive).
    private static bool IsApplicationActive() => Marrow.App.Port.NSApplication.IsActive.Current();

    // AppModel.swift:1218-1222. Swift: markRead(_: UUID).
    public void MarkRead(Guid id) // AppModel.swift:1218-1222
    {
        if (Thread(id)?.HasUnread != true) return;
        UpdateThread(id, thread => thread with { HasUnread = false });
        UpdateDockBadge();
    }

    // AppModel.swift:1226-1238. Swift: markRead(_: [UUID]).
    public void MarkRead(IReadOnlyList<Guid> ids) // AppModel.swift:1226-1238
    {
        var wanted = ids.ToHashSet();
        Dictionary<int, ChatThread>? replaced = null; // ED-NEW-lean-16 (follow-up): one copy of the list, as UpdateThreads
        for (var index = 0; index < threads.Count; index++)
        {
            var thread = threads[index];
            if (!wanted.Contains(thread.Id) || !thread.HasUnread) continue;
            (replaced ??= [])[index] = thread with { HasUnread = false };
        }
        if (replaced is null) return;
        var value = new ValueList<ChatThread>(new SomeReplaced(threads, replaced));
        if (!AssignThreadsInPlace(value, ChangedIndices(threads, replaced))) Threads = value; // ED-NEW-lean-16
        ScheduleSave();
        UpdateDockBadge();
    }

    // AppModel.swift (B3):1249-1255
    public void RequestNotificationPermission() // AppModel.swift:1249-1255
    {
        Marrow.App.Port.ToastNotifications.RequestPermissionOnce(this); // B3 :1249-1255, ED-220 (4)
    }

    // AppModel.swift (B3):1257-1268. Swift: notify(threadID:title:body:sound:) with `sound: UNNotificationSound? = .default`.
    // The source passes .default or nil (:1224); the existing bool API maps these to default or silent audio (ED-220).
    public void Notify(Guid threadID, string title, string body, bool sound = true) // AppModel.swift:1257-1268
    {
        // B3 :1257-1268, ED-001: capture-run mode is not ported. Keep the complete B3 screen gate local to this boundary.
        if (IsApplicationActive() && Thread(threadID) is { } thread && Marrow.App.Port.ToastNotifications.IsOnScreen(this, thread)) return;
        RequestNotificationPermission();
        var request = new Marrow.App.Port.ToastRequest(SwiftUUID.UuidString(threadID), title, body, sound,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["threadID"] = SwiftUUID.UuidString(threadID) });
        var center = Marrow.App.Port.ToastNotifications.Center.Current;
        MainActor.Spawn(async () =>
        {
            try { await center.Add(request); }
            catch (Exception) { } // :1268 Task { try? await center.add(request) }
        });
    }

    // AppModel.swift:1261-1264
    public void UpdateDockBadge() // AppModel.swift:1261-1264
    {
        // ED-219: NSApp.dockTile.badgeLabel (:1273) is the main window's taskbar overlay icon, cleared at zero.
        dockUnreadCount = threads.LongCount(thread => thread.HasUnread && !thread.IsArchived && !thread.IsInPanel);
        Marrow.App.Port.TaskbarBadge.Show(dockUnreadCount);
    }

    // MARK: - Text generation

    // AppModel.swift:1268-1285. Swift: textEngine(preferring:).
    public TextGeneration.Engine? TextEngine(ProviderKind? provider) // AppModel.swift:1268-1285
    {
        TextGeneration.Engine? Claude()
        {
            if (Providers.Status(ProviderKind.Claude).ProviderStatusAuth is ProviderStatus.Auth.SignedOut
                || Providers.Executable(ProviderKind.Claude) is not { } executable) return null;
            return new TextGeneration.Engine.Claude(executable.Path, Providers.Environment(ProviderKind.Claude));
        }

        TextGeneration.Engine? Codex()
        {
            if (Providers.Status(ProviderKind.Codex).ProviderStatusAuth is ProviderStatus.Auth.SignedOut
                || Providers.Executable(ProviderKind.Codex) is not { } executable
                || Providers.DefaultModel(ProviderKind.Codex)?.Id is not { } model) return null;
            return new TextGeneration.Engine.Codex(executable.Path, Providers.Environment(ProviderKind.Codex), model);
        }

        return Settings.TextGeneration switch
        {
            TextGenerationChoice.Off => null,
            TextGenerationChoice.Claude => Claude(),
            TextGenerationChoice.Codex => Codex(),
            TextGenerationChoice.Automatic => provider == ProviderKind.Codex ? Codex() ?? Claude() : Claude() ?? Codex(),
            _ => null,
        };
    }

    // MARK: - Persistence

    // AppModel.swift:1298-1314 (B3, PR-STORE-015/PR-LAUNCH-076: a throttle, not a cancel-and-restart debounce)
    public void ScheduleSave() =>
        ScheduleLibrarySave();

    private void ScheduleLibrarySave()
    {
        if (!persistenceEnabled || saveTask is not null) return; // AppModel.swift:1305; returns early while a save is pending, no cancel/restart
        var sinceLast = lastLibrarySave is { } last ? SwiftClock.Now - last : TimeSpan.FromSeconds(60); // AppModel.swift:1306
        var wait = new[] { TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(4) - sinceLast }.Max(); // AppModel.swift:1307
        var weakSelf = new WeakReference<AppModel>(this);
        saveTask = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(wait); // AppModel.swift:1309
            if (SwiftTask.IsCancelled || !weakSelf.TryGetTarget(out var model)) return; // AppModel.swift:1310
            model.saveTask = null; // AppModel.swift:1311
            model.SaveLibrary(); // AppModel.swift:1312
        }); // AppModel.swift:1308-1313
    }

    private void SaveLibrary()
    {
        if (!persistenceEnabled) return; // AppModel.swift:1317
        lastLibrarySave = SwiftClock.Now; // AppModel.swift:1318; every call, scheduled or direct, stamps this
        // ENGINEERING DECISION: the OD-0008 A10 "do not overwrite after a failed load" guard has no Swift
        // counterpart; the port adds it. Swift stamps lastLibrarySave unconditionally right after the
        // persistenceEnabled guard, so that statement order is kept and the OD-0008 check is placed after the
        // stamp rather than folded into the guard above it. This has no observable effect either way: a
        // failed-load run never reaches the write below under either ordering (both checks return before it),
        // and no other code reads lastLibrarySave; the ordering is chosen only to keep the Swift statement
        // sequence intact rather than to change behavior.
        if (libraryLoadFailed) return; // DEVIATION OD-0008 A10
        var library = new Library { Projects = projects, Threads = threads }; // AppModel.swift:1319-1321
        DiskWriter.Shared.EncodeAndWrite(library, Storage.LibraryURL); // AppModel.swift:1322-1323
    }

    // AppModel.swift:1327-1345
    public void SaveBeforeQuit() =>
        SaveSynchronouslyBeforeQuit(); // AppModel.swift:1327-1345

    private void SaveSynchronouslyBeforeQuit()
    {
        DisposeAssistantFeed(); // PORT-ONLY: owned feed/taps/watcher stop even when persistence is disabled (ED-NEW-dextra-cx-7)
        if (!persistenceEnabled) return; // AppModel.swift:1328
        persistenceEnabled = false; // AppModel.swift:1329
        saveTask?.Cancel(); // AppModel.swift:1330
        saveTask = null; // AppModel.swift:1331
        if (!libraryLoadFailed)
        {
            var library = new Library { Projects = projects, Threads = threads }; // AppModel.swift:1332-1334
            DiskWriter.Shared.WriteSynchronously(library, Storage.LibraryURL); // AppModel.swift:1335
        }
        DisposeThreadPorts();
        foreach (var runtime in runtimes.Values)
        {
            runtime.CancelPersistence(); // AppModel.swift:1336-1337
            runtime.StopSession(); // AppModel.swift:1338
            if (runtime.UnsavedSnapshot() is not { } snapshot) continue; // AppModel.swift:1339-1340
            DiskWriter.Shared.WriteSynchronously(snapshot, Storage.ThreadURL(runtime.ThreadID)); // AppModel.swift:1341
        }
        DiskWriter.Shared.WaitForQueuedWrites(); // AppModel.swift:1343
        Marrow.Services.Port.MultiAccount.HandoffJournalWriter.WaitForQueuedWrites(); // ED-NEW-perf-ui-thread-5: the handoff journals the runtimes just handed over
        Terminals.TerminateAll(); // AppModel.swift:1344
    }
}

// AppModel.swift:1349-1374. A caseless enum of static functions -> static class.
public static class ProjectFile
{
    // AppModel.swift:1350-1362. Swift: scripts(at:).
    public static ValueList<ProjectScript> Scripts(SwiftURL url) // AppModel.swift:1350-1362
    {
        try
        {
            if (!string.Equals(url.Scheme, "file", StringComparison.OrdinalIgnoreCase)) return ValueList<ProjectScript>.Empty;
            var path = url.Path;
            if (OperatingSystem.IsWindows() && path.Length >= 4 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':')
                path = path[1..];
            var data = File.ReadAllBytes(SwiftPath.AppendingPathComponent(path, "droppy-code.json"));
            if (JSONValue.Parse(data) is not { } json || json["scripts"]?.JSONValueArray is not { } entries)
                return ValueList<ProjectScript>.Empty;
            return new ValueList<ProjectScript>(entries.Select(entry =>
            {
                var name = entry["name"]?.JSONValueString;
                var command = entry["command"]?.JSONValueString;
                if (name is null || command is null) return null;
                return new ProjectScript(
                    name,
                    command,
                    Symbol(entry["icon"]?.JSONValueString),
                    entry["runOnWorktreeCreate"]?.JSONValueBool ?? false);
            }).OfType<ProjectScript>());
        }
        catch (Exception)
        {
            return ValueList<ProjectScript>.Empty;
        }
    }

    // AppModel.swift:1364-1373. Swift: symbol(for:).
    public static string Symbol(string? icon) // AppModel.swift:1364-1373
        => icon switch
        {
            "test" => "checkmark.seal",
            "lint" => "wand.and.stars",
            "configure" => "gearshape",
            "build" => "hammer",
            "debug" => "ant",
            _ => "play",
        };
}
