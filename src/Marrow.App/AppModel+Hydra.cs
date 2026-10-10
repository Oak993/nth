using System.Diagnostics;
using System.Globalization;
using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App;

// Source: DroppyCode/App/AppModel+Hydra.swift (B3 52bc792), :132-1143; HydraWatchdog :114-127.
public sealed partial class AppModel
{
    // :114-127
    private static partial class HydraWatchdog
    {
        internal const string StalledNote = "Stalled: no edits for 10 minutes; stopped by Nth. Resend with a sharper brief.";
        internal static readonly HashSet<Guid> StalledHeadIDs = [];
        internal static readonly Dictionary<Guid, long> ToolCounts = [];
        internal static readonly HashSet<Guid> Labelled = [];
        internal static TaskHandle? Loop;
        internal const double ThinkingAfter = 180;
        internal const double StalledAfter = 600;
        internal static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    }

    // :132-134
    public bool HydraIsOn(ChatThread thread) =>
        Settings.HydraEnabled && thread.HydraEnabled && !thread.IsHelper;

    // :141-143
    public static bool HydraIsNative(ProviderKind provider) =>
        provider is ProviderKind.Claude or ProviderKind.Codex or ProviderKind.Copilot;

    // :148-151
    public HydraPair? HydraPair(ChatThread thread)
    {
        var pair = Settings.HydraPair(thread.HydraPairID);
        return pair?.Provider == thread.Provider ? pair : null;
    }

    // :156-160
    public ProviderKind HydraHeadsProvider(HydraPair pair)
    {
        if (pair.WorkerProvider is not { } provider || provider == pair.Provider ||
            !Providers.CountsAsInstalled(provider) || !Settings.IsEnabled(provider))
            return pair.Provider;
        return provider;
    }

    // :165-169
    public string? HydraHeadsFallbackNote(HydraPair pair)
    {
        if (pair.WorkerProvider is not { } provider || provider == pair.Provider ||
            HydraHeadsProvider(pair) != pair.Provider)
            return null;
        var reason = Providers.Status(provider).IsInstalled
            ? "is switched off in Settings"
            : "is not installed on this PC";
        return $"The pair's heads run on {pair.Provider.DisplayName} for now: {provider.DisplayName} {reason}.";
    }

    // :178-184
    public static string? HydraHeadsEffort(string? leadEffort, IReadOnlyList<string> scale)
    {
        if (string.IsNullOrEmpty(leadEffort)) return null;
        var leadIndex = -1;
        for (var index = 0; index < scale.Count; index++)
            if (scale[index] == leadEffort)
            {
                leadIndex = index;
                break;
            }
        if (leadIndex < 0) return leadEffort;
        var working = -1;
        for (var index = 0; index < scale.Count; index++)
            if (scale[index] == "medium")
            {
                working = index;
                break;
            }
        if (working < 0 && scale.Count > 0) working = (scale.Count - 1) / 2;
        return working >= 0 && leadIndex > working ? scale[working] : leadEffort;
    }

    // :189-229
    public HydraLaunch? HydraLaunch(ChatThread thread)
    {
        if (!HydraIsOn(thread)) return null;

        var pair = HydraPair(thread);
        var headsProvider = pair is null ? thread.Provider : HydraHeadsProvider(pair);
        var elsewhere = headsProvider != thread.Provider;
        var keepsModel = pair is null || !pair.SendsHeadsElsewhere || elsewhere;
        var workerModel = keepsModel ? pair?.WorkerModel : null;
        var workerEffort = keepsModel ? pair?.WorkerEffort : null;
        if (workerEffort is null && Settings.HydraTempersHeadEffort && !elsewhere)
        {
            var effortModel = Providers.Model(thread.Model, thread.Provider);
            workerEffort = HydraHeadsEffort(thread.Effort, effortModel?.Efforts ?? ValueList<string>.Empty);
        }

        var projects = Projects
            .Select((project, index) => (project, index))
            .OrderBy(item => item.project.Id == thread.ProjectID ? 0 : 1)
            .ThenBy(item => item.index)
            .Select(item => new HydraProjectRef { Name = item.project.Name, Path = item.project.Path });

        string? label = null;
        if (elsewhere)
        {
            var resolved = workerModel is { } model ? Providers.Model(model, headsProvider) : null;
            var modelName = resolved?.ShortName ?? pair?.WorkerModel ?? Providers.DefaultModel(headsProvider)?.ShortName ?? "the default model";
            label = $"{modelName} on {headsProvider.DisplayName}";
        }

        var maxHeads = new[] { pair?.MaxHeads, Settings.HydraMaxHeads }
            .Where(cap => cap is not null)
            .Select(cap => cap!.Value)
            .DefaultIfEmpty()
            .Min();
        long? cap = pair?.MaxHeads is null && Settings.HydraMaxHeads is null ? null : maxHeads;

        return new HydraLaunch
        {
            HeadsProvider = headsProvider,
            RunsNatively = !elsewhere && HydraIsNative(thread.Provider),
            HeadsLabel = label,
            WorkerModel = workerModel,
            WorkerEffort = workerEffort,
            MaxHeads = cap,
            HeadProfiles = pair?.HeadProfiles ?? ValueList<HydraHeadProfile>.Empty,
            IsolatesHeads = Settings.HydraIsolateHeads,
            AutoMerges = Settings.HydraAutoMerge,
            ReviewsHeads = Settings.HydraReviewHeads,
            Projects = new ValueList<HydraProjectRef>(projects),
            AntiSlopEnabled = Settings.AntiSlopEnabled,
        };
    }

    // :234-256
    public void SetHydra(bool on, Guid id)
    {
        if (Thread(id) is not { } thread) return;
        if (!on)
        {
            UpdateThread(id, value => value with { HydraEnabled = false });
            Settings.HydraDefaultEnabled = false;
            return;
        }

        var pair = Settings.HydraPair(thread.Provider, thread.Model);
        UpdateThread(id, value => value with { HydraEnabled = true, HydraPairID = pair?.Id });
        Settings.HydraDefaultEnabled = true;
        if (pair?.OrchestratorModel is { } leadModel && leadModel != thread.Model &&
            Providers.Model(leadModel, thread.Provider) is not null)
        {
            UpdateThread(id, value => value with { Model = leadModel, Effort = pair.OrchestratorEffort ?? value.Effort });
        }
        else if (pair?.OrchestratorEffort is { } effort)
        {
            UpdateThread(id, value => value with { Effort = effort });
        }
    }

    // :261-263
    public ValueList<ChatThread> HydraHeads(Guid parentID) => PanelHeads(parentID);

    // :266-268
    public long RunningDroppyHeads(Guid parentID) =>
        HydraHeads(parentID).LongCount(head => head.Hydra is
            { HydraHeadInfoKind: HydraHeadInfo.Kind.Droppy, HydraHeadInfoStatus: HydraHeadInfo.Status.Running });

    // :273-275
    public long RunningHydraHeads(Guid parentID) =>
        HydraHeads(parentID).LongCount(head => head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running);

    // :283-294
    public long HydraUnmergedFileCount(Guid leadID)
    {
        if (ExistingRuntime(leadID) is not { } runtime) return 0;

        var paths = new HashSet<string>(SwiftStringComparer.Instance);
        foreach (var turn in runtime.HydraUnmergedTurns)
        {
            if (turn.Status == TurnStatus.Running || turn.TouchedPaths is not { } touchedPaths) continue;
            paths.UnionWith(touchedPaths);
        }
        foreach (var head in HydraTeam(leadID))
        {
            if (head.Hydra is not { MergedAt: null } info || info.Landing?.PatchPath is not null || info.Landing?.Error is not null)
                continue;
            if (info.Landing is { } landing) paths.UnionWith(landing.Files.Select(file => file.Path));
        }

        return paths.Count(path => !TouchedPaths.IsBuildOutput(path) &&
            Projects.Any(project => TouchedPaths.Relative(path, project.Path) is not null));
    }

    // :297-301
    public ValueList<ChatThread> HydraTeam(Guid parentID) =>
        new(Children(parentID)
            .Where(head => !head.IsArchived && head.IsHydraHead)
            .OrderBy(head => head.Hydra?.Index ?? 0));

    // :304-308
    public ValueList<string> WorkingHydraHeadNames(Guid parentID, IReadOnlyCollection<long>? excluding = null) =>
        new(HydraHeads(parentID)
            .Where(head => head.Hydra is { HydraHeadInfoKind: HydraHeadInfo.Kind.Droppy, HydraHeadInfoStatus: HydraHeadInfo.Status.Running } info &&
                (excluding is null || !excluding.Contains(info.Index)))
            .Select(head => head.Hydra!.Persona.Name));

    // :319-343
    public Project? HydraProject(string? reference, ChatThread lead)
    {
        if (reference is null || reference.Trimmed().Length == 0) return Project(lead.ProjectID);
        var expanded = SwiftPath.ExpandingTildeInPath(reference);

        var named = Projects.FirstOrDefault(project => SwiftString.CaseInsensitiveCompare(project.Name, expanded) == 0);
        if (named is not null) return named;

        var matching = Projects
            .Where(project => SwiftPath.SamePath(project.Path, expanded) || TouchedPaths.Relative(expanded, project.Path) is not null)
            .OrderByDescending(project => project.Path.Length)
            .FirstOrDefault();
        if (matching is not null) return matching;

        if (!SwiftPath.IsAbsolute(expanded) || !Directory.Exists(expanded)) return null;
        var directory = SwiftPath.StandardizingPath(expanded);
        while (directory.Length > 0)
        {
            var gitMarker = SwiftPath.AppendingPathComponent(directory, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
            {
                return AddProjectPath(directory);
            }
            var parent = Directory.GetParent(directory)?.FullName;
            if (parent is null || SwiftPath.SamePath(parent, directory)) break;
            directory = parent;
        }
        return null;
    }

    // :354-361
    public ChatThread? SpawnNativeHead(Guid parentID, AgentSpawn spawn)
    {
        if (Thread(parentID) is not { } parent) return null;
        return InsertHydraHead(parent, spawn.Description, HydraHeadInfo.Origin.Delegated, null, null,
            null, spawn.Profile, spawn);
    }

    // :371-385
    public ChatThread? SpawnDroppyHead(
        Guid parentID,
        string task,
        HydraHeadInfo.Origin origin,
        IReadOnlyList<Attachment> attachments,
        Guid? batchID,
        long? preferredIndex,
        string? project,
        string? profile,
        Func<HydraPersona, HydraPrompts.Workplace, string> brief)
    {
        if (Thread(parentID) is not { } parent) return null;
        var head = InsertHydraHead(parent, task, origin, batchID, preferredIndex, project, profile, null);
        if (head is null) return null;
        var id = head.Id;
        MainActor.Spawn(() => StartDroppyHead(id, attachments, brief));
        return head;
    }

    // :398-428
    private ChatThread? InsertHydraHead(
        ChatThread parent,
        string task,
        HydraHeadInfo.Origin origin,
        Guid? batchID,
        long? preferredIndex,
        string? projectReference,
        string? profileName,
        AgentSpawn? nativeSpawn)
    {
        var launch = HydraLaunch(parent);
        var targetProject = HydraProject(projectReference, parent);
        var unresolvedProject = targetProject is null && projectReference is { } reference && reference.Trimmed().Length > 0;
        if (unresolvedProject) targetProject = Project(parent.ProjectID);

        var routedProfile = launch is not null && profileName is not null ? launch.Profile(profileName) : null;
        var canonicalProfile = routedProfile?.Name;
        var projectID = targetProject?.Id ?? parent.ProjectID;
        var sameProject = targetProject?.Id == parent.ProjectID;
        var (index, counter) = AllocateHydraRosterIndex(parent, preferredIndex);
        var persona = HydraRoster.Persona(index);
        if (unresolvedProject)
        {
            var leadProjectName = Project(parent.ProjectID)?.Name ?? "this chat's project";
            Runtime(parent.Id).AppendHydraNote(
                $"Hydra: no project named {projectReference} is in the sidebar or on this PC, so {persona.Name} works in {leadProjectName}.");
        }
        var provider = nativeSpawn is not null
            ? parent.Provider
            : routedProfile?.Provider ?? launch?.HeadsProvider ?? parent.Provider;
        var elsewhere = provider != parent.Provider;
        var model = nativeSpawn?.Model ?? routedProfile?.Model ?? launch?.WorkerModel ??
            (elsewhere ? Providers.DefaultModel(provider)?.Id : parent.Model);
        var effort = routedProfile?.Effort ?? launch?.WorkerEffort ?? (elsewhere ? null : parent.Effort);
        var title = task.Length == 0
            ? persona.Name
            : !sameProject && targetProject is not null
                ? $"{persona.Name} · {TextCleanup.SingleLine(task, 60)} · {targetProject.Name}"
                : $"{persona.Name} · {TextCleanup.SingleLine(task, 60)}";
        if (canonicalProfile is not null) title += " · " + canonicalProfile;

        var info = new HydraHeadInfo(index, task, nativeSpawn is null ? HydraHeadInfo.Kind.Droppy : HydraHeadInfo.Kind.Native, origin)
        {
            BatchID = nativeSpawn is null ? batchID : null,
            Profile = canonicalProfile,
            NativeID = nativeSpawn?.Id,
            NativeTaskID = nativeSpawn?.TaskID,
            ToolUseID = nativeSpawn?.ToolUseID,
            IsBackground = nativeSpawn?.IsBackground ?? true,
            CanStop = nativeSpawn is null || nativeSpawn.TaskID is not null || parent.Provider == ProviderKind.Codex,
        };
        var head = new ChatThread(projectID, provider, model, effort,
            nativeSpawn is null ? RuntimeMode.FullAccess : parent.RuntimeMode)
        {
            ParentThreadID = parent.Id,
            IsInPanel = true,
            Title = title,
            HasCustomTitle = true,
            FastMode = false,
            Hydra = info,
            WorktreePath = sameProject ? parent.WorktreePath : null,
            Branch = sameProject ? parent.Branch : null,
        };

        UpdateThread(parent.Id, value => value with { HydraSpawnCount = counter });
        InsertThread(head);
        var parentRuntime = Runtime(parent.Id);
        parentRuntime.IsHydraPanelHidden = false;
        parentRuntime.HydraSelectedHeadID = head.Id;

        if (nativeSpawn is not null)
        {
            Runtime(head.Id).RehearseTurn(nativeSpawn.Prompt);
        }
        return head;
    }

    // :559-565
    private void UpdateHydraHead(Guid id, Func<HydraHeadInfo, HydraHeadInfo> change) =>
        UpdateThread(id, thread => thread.Hydra is { } info ? thread with { Hydra = change(info) } : thread);

    private (long Index, long Counter) AllocateHydraRosterIndex(ChatThread parent, long? preferredIndex)
    {
        var team = HydraTeam(parent.Id);
        var previous = parent.HydraSpawnCount;
        var sequential = team.Count == 0 ? 0 : previous;
        var taken = new HashSet<long>(team.Where(head => head.Hydra is not null).Select(head => head.Hydra!.Index));

        long? pinned = null;
        if (preferredIndex is >= 0)
        {
            var candidate = preferredIndex.Value;
            while (taken.Contains(candidate) && team.Any(head => head.Hydra is { IsFinished: true } info && info.Index == candidate))
                candidate += HydraRoster.Personas.Count;
            if (!taken.Contains(candidate)) pinned = candidate;
        }

        var index = sequential;
        if (pinned is { } pinnedIndex)
        {
            index = pinnedIndex;
        }
        else
        {
            while (taken.Contains(index)) index++;
        }
        var nextCounter = Math.Max((pinned is null ? index : sequential) + 1, previous + 1);
        return (index, nextCounter);
    }

    // :495-528
    private async Task StartDroppyHead(Guid id, IReadOnlyList<Attachment> attachments,
        Func<HydraPersona, HydraPrompts.Workplace, string> brief)
    {
        if (Thread(id) is not { Hydra: { } info } head ||
            head.ParentThreadID is not { } parentID || Thread(parentID) is not { } parent ||
            HydraCheckout(head, parent) is not { } checkout)
            return;

        var provider = head.Provider;
        if (provider != parent.Provider && head.Model is null)
        {
            await Providers.LoadCatalog(provider);
            if (Providers.DefaultModel(provider) is { } defaultModel)
            {
                UpdateThread(id, value => value with { Model = defaultModel.Id });
            }
        }

        if (string.IsNullOrWhiteSpace(checkout)) return;

        var workplace = (HydraPrompts.Workplace)new HydraPrompts.Workplace.Shared(checkout);
        if (Settings.HydraIsolateHeads && await MakeHydraCopy(head, checkout) is { } copy)
        {
            UpdateThread(id, value => value with { WorktreePath = copy.Path, Branch = null });
            UpdateHydraHead(id, value => value with { BaseTree = copy.Tree });
            workplace = new HydraPrompts.Workplace.OwnCopy(copy.Path);
        }

        if (Thread(id) is not { Hydra: { HydraHeadInfoStatus: HydraHeadInfo.Status.Running } }) return;
        var started = SwiftDate.Now;
        UpdateHydraHead(id, value => value with { ExecutionStartedAt = started, ExecutionFinishedAt = null });

        var persona = info.Persona;
        var text = brief(persona, workplace);
        Runtime(id).SendHydraBrief(text, attachments);
    }

    private async Task<(string Path, string Tree)?> MakeHydraCopy(ChatThread head, string checkout)
    {
        if (head.Hydra is not { } info || Project(head.ProjectID) is not { } project) return null;
        if (head.ParentThreadID is { } parentID && ExistingRuntime(parentID)?.HydraLanding is { } landingTask)
            await landingTask.Completion;

        var git = new Git(checkout);
        var projectSlug = SwiftString.Lowercased(project.Name.Replace(' ', '-'));
        var personaSlug = SwiftString.Lowercased(info.Persona.Name.Replace(' ', '-'));
        var id = SwiftString.Lowercased(SwiftUUID.UuidString(head.Id));
        var path = SwiftPath.AppendingPathComponent(Storage.WorktreesDirectory,
            $"{projectSlug}-{personaSlug}-{id[..8]}");
        try
        {
            if (!await git.IsRepository() || !await git.HasCommits()) return null;
            var capture = await HydraTreeCache.Capture(checkout, git);
            Directory.CreateDirectory(Storage.WorktreesDirectory);
            await git.AddDetachedWorktree(path, capture.Commit);
            return (path, capture.Tree);
        }
        catch
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch { }
            return null;
        }
    }

    // :572-629
    public void FinishHydraHead(Guid id, TurnStatus status, string? summary = null, HydraLanding? landing = null)
    {
        if (Thread(id) is not { Hydra: { } info } head || info.IsFinished) return;
        if (HydraWatchdog.StalledHeadIDs.Remove(id))
            summary = string.Join("\n\n", new[] { HydraWatchdog.StalledNote, summary }.Where(text => !string.IsNullOrEmpty(text)));

        var result = status switch
        {
            TurnStatus.Completed => HydraHeadInfo.Status.Completed,
            TurnStatus.Failed => HydraHeadInfo.Status.Failed,
            TurnStatus.Interrupted => HydraHeadInfo.Status.Stopped,
            TurnStatus.Running => HydraHeadInfo.Status.Running,
            _ => throw new UnreachableException(),
        };
        var runtime = ExistingRuntime(id);
        var updated = info with
        {
            HydraHeadInfoStatus = result,
            Summary = summary ?? info.Summary,
            FinishedAt = SwiftDate.Now,
            Landing = landing ?? info.Landing,
            Activity = runtime?.HydraActivity ?? info.Activity,
            ToolCalls = Math.Max(info.ToolCalls, runtime?.HydraToolCalls ?? 0),
            Tokens = Math.Max(info.Tokens, runtime?.HydraTokens ?? 0),
        };
        UpdateThread(id, thread => thread with { Hydra = updated });

        // AppModel+Hydra.swift:615-619: retain prior values on the record, but report only this finish's payload.
        if (head.ParentThreadID is { } parentID && Thread(parentID) is not null)
            Runtime(parentID).HydraHeadFinished(id, updated, result, summary, landing,
                info.HasOwnCopy ? head.WorktreePath : null);

        if (Settings.HydraAutoClearFinished && result == HydraHeadInfo.Status.Completed)
            ReleaseHydraCopy(id);

        if (Settings.HydraAutoClearFinished)
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(HydraPanel.FinishHold + 0.2)); // AppModel+Hydra.swift:604 .seconds(HydraPanel.finishHold + 0.2), coupled to the hold
                // AppModel+Hydra.swift:605-609: the head may have a different parent after the hold.
                if (Thread(id) is not { IsInPanel: true, Hydra.IsFinished: true } currentHead) return;
                UpdateThread(id, thread => thread with { IsInPanel = false });
                if (currentHead.ParentThreadID is not { } leadID) return;
                var leadRuntime = Runtime(leadID);
                if (leadRuntime.HydraSelectedHeadID == id) leadRuntime.HydraSelectedHeadID = null;
                if (leadRuntime.HydraPoppedHeadID == id) leadRuntime.HydraPoppedHeadID = null;
                leadRuntime.HydraAutoPopHeld = leadRuntime.HydraAutoPopHeld.Mutating(ids => ids.Remove(id));
                leadRuntime.HydraAutoPanelDocks = leadRuntime.HydraAutoPanelDocks.Mutating(docks => docks.Remove(id));
                leadRuntime.PanelStackOrder = new ValueList<FloatingPanelID>(leadRuntime.PanelStackOrder
                    .Where(panel => panel is not FloatingPanelID.Auto auto || auto.Value != id));
            });
    }

    // :634-716
    public void HydraHeadTurnFinished(ChatThread head, TurnStatus status)
    {
        if (head.Hydra is not { HydraHeadInfoKind: HydraHeadInfo.Kind.Droppy } info) return;
        var runtime = Runtime(head.Id);
        var finishedAt = SwiftDate.Now;
        UpdateHydraHead(head.Id, value => value with { ExecutionFinishedAt = finishedAt });

        var turnID = runtime.Turns.LastOrDefault(turn => turn.CompletedAt is not null)?.Id;
        var items = runtime.Entries.Select(entry => entry.Item).ToList();
        var error = turnID is { } closedTurnID
            ? items.Where(item => item.TurnID == closedTurnID)
                .Select(item => item.TimelineItemContent)
                .OfType<TimelineItem.Content.Notice>()
                .Select(notice => notice.Value)
                .LastOrDefault(notice => notice.NoticeLevel == Notice.Level.Error)?.Message
            : null;
        var assistant = items.AsEnumerable().Reverse()
            .Where(item => item.TimelineItemContent is TimelineItem.Content.Assistant)
            .Select(item => (item.TurnID, Message: ((TimelineItem.Content.Assistant)item.TimelineItemContent).Value))
            .ToList();
        var partial = status == TurnStatus.Failed && turnID is { } failedTurnID
            ? assistant.FirstOrDefault(item => item.TurnID == failedTurnID && item.Message.Text.Trimmed().Length > 0).Message?.Text.Trimmed() ?? ""
            : "";
        var spoken = status == TurnStatus.Failed
            ? null
            : assistant.FirstOrDefault().Message?.Text ?? "";
        var report = status == TurnStatus.Failed ? FailedTurnReport(error, partial) : spoken;
        var toolCount = runtime.Entries.LongCount(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Tool);
        UpdateHydraHead(head.Id, value => value with { ToolCalls = toolCount });
        if (!head.Provider.IsAPIKeyBased) ExistingRuntime(head.Id)?.StopSession();

        var firstUser = runtime.Entries
            .Select(entry => entry.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>()
            .Select(user => user.Value)
            .FirstOrDefault();
        var previousRetries = HydraHeadRetries.TryGetValue(head.Id, out var retries) ? retries : 0;
        if (status == TurnStatus.Failed && IsLaunchFailure(error ?? "") && toolCount == 0 && previousRetries < 2 &&
            firstUser is { } brief && (brief.Text.Trimmed().Length > 0 || brief.Attachments.Count > 0))
        {
            var attempt = previousRetries + 1;
            HydraHeadRetries = HydraHeadRetries.Mutating(values => values[head.Id] = attempt);
            var stagger = ((info.Index % 4 + 4) % 4) * 0.5;
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(SwiftTask.Seconds(attempt * 2 + stagger));
                if (ExistingRuntime(head.Id) is not { IsRunning: false } retryRuntime ||
                    Thread(head.Id)?.Hydra?.HydraHeadInfoStatus != HydraHeadInfo.Status.Running)
                    return;
                // AppModel+Hydra.swift:696-697 retries through the composer's ordinary send path.
                retryRuntime.Draft = new ComposerDraft { Text = brief.Text, Attachments = ValueList<Attachment>.Empty };
                retryRuntime.Send();
            });
            return;
        }

        MainActor.Spawn(async () =>
        {
            HydraLanding? landing = null;
            if (status == TurnStatus.Completed && info.HasOwnCopy)
                landing = await LandHydraHead(head.Id);

            if (ExistingRuntime(head.Id)?.IsRunning == true) return;
            if (Thread(head.Id)?.Hydra?.IsFinished == true)
                UpdateHydraHead(head.Id, value => value with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running });
            FinishHydraHead(head.Id, status, report, landing);
        });
    }

    private static string FailedTurnReport(string? error, string partial)
    {
        var parts = new List<string> { error ?? "The turn failed without a recorded error." };
        var trimmed = partial.Trimmed();
        if (trimmed.Length > 0) parts.Add("Partial output before the failure:\n\n" + trimmed);
        return string.Join("\n\n", parts);
    }

    private static bool IsLaunchFailure(string report)
    {
        var lowered = SwiftString.Lowercased(report);
        return new[]
        {
            "database is locked", "database locked", "sqlite_busy", "did not start a session",
            "the agent exited", "stopped unexpectedly",
        }.Any(lowered.Contains);
    }

    private sealed record HydraPatch(string After, string Text, ValueList<HydraLanding.File> Files, long DroppedBuildOutputFiles);

    // :747-795
    private async Task<HydraLanding?> LandHydraHead(Guid id)
    {
        if (Thread(id) is not { Hydra: { } info, WorktreePath: { } copy } head ||
            head.ParentThreadID is not { } parentID || Thread(parentID) is not { } lead ||
            HydraCheckout(lead) is not { } checkout)
            return null;

        HydraPatch? patch;
        try
        {
            patch = await PrepareHydraPatch(copy, info.BaseTree);
        }
        catch (Exception error)
        {
            return new HydraLanding { Error = error.Message };
        }
        if (patch is null) return null;

        var landing = new HydraLanding
        {
            Files = patch.Files,
            DroppedBuildOutputFiles = patch.DroppedBuildOutputFiles,
        };
        await WaitForSettledCheckout(parentID, patch.Files.Select(file => file.Path).ToArray());

        var leadRuntime = ExistingRuntime(parentID);
        var previous = leadRuntime?.HydraLanding;
        var task = MainActor.Spawn(async () =>
        {
            if (previous is not null) await previous.Completion;
            return await ApplyHydraHead(id, landing, patch, checkout, parentID);
        });
        if (leadRuntime is not null) leadRuntime.HydraLanding = task;
        return await task.Value;
    }

    private static async Task<HydraPatch?> PrepareHydraPatch(string copy, string? baseTree)
    {
        if (baseTree is null) return null;
        var git = new Git(copy);
        var after = await git.CurrentTree();
        if (after == baseTree) return null;
        var (paths, dropped) = await HydraWork(git, baseTree, after);
        if (paths.Count == 0) return null;
        var patch = await git.Diff(baseTree, after, binary: true, paths: paths);
        if (patch.Length == 0) return null;
        var repositoryURL = SwiftURL.Create("file:///" + copy.Replace('\\', '/'))
            ?? throw new IOException("Could not create the head copy URL.");
        var edits = await ThreadRuntime.FileEdits(patch, repositoryURL);
        var files = new ValueList<HydraLanding.File>(edits.Select(edit => new HydraLanding.File
        {
            Path = edit.Path,
            Additions = edit.Additions,
            Deletions = edit.Deletions,
        }));
        if (files.Count == 0) return null;
        return new HydraPatch(after, patch, files, dropped);
    }

    // :797-809
    private static async Task<(IReadOnlyList<string> Paths, long Dropped)> HydraWork(Git copyGit, string from, string to)
    {
        var changed = await copyGit.ChangedPaths(from, to);
        var paths = changed.Where(path => !TouchedPaths.IsBuildOutput(path)).ToArray();
        return (paths, changed.Count - paths.Length);
    }

    // :816-833
    private async Task WaitForSettledCheckout(Guid parentID, IReadOnlyList<string> paths)
    {
        if (ExistingRuntime(parentID) is not { } leadRuntime) return;
        var wanted = new HashSet<string>(paths, SwiftStringComparer.Instance);
        var deadline = SwiftDate.Now.AddMinutes(30);
        while (SwiftDate.Now < deadline)
        {
            var busy = leadRuntime.Phase != RuntimePhase.Idle && wanted.Count > 0 &&
                wanted.Any(leadRuntime.HydraCurrentTurnTouchedPaths.Contains);
            if (!busy && !leadRuntime.IsHydraMerging) return;
            if (!await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250))) return;
        }
    }

    private async Task<HydraLanding> ApplyHydraHead(Guid id, HydraLanding landing, HydraPatch patch, string checkout, Guid parentID)
    {
        if (Thread(id) is not { Hydra: { } info } head || Thread(parentID) is null) return landing;
        var checkoutGit = new Git(checkout);
        UpdateHydraHead(id, value => value with { LandingStartedAt = SwiftDate.Now, LandingFinishedAt = null });
        try
        {
            if (await checkoutGit.HasOperationInProgress())
            {
                landing = landing with
                {
                    PatchPath = KeepHydraPatch(patch.Text, info, head.Id),
                    Error = "a rebase or merge is underway in the checkout, or its git state cannot be read",
                };
                NoteHydraLanding(landing, info, parentID);
                return landing;
            }

            try
            {
                var conflicts = await checkoutGit.Apply(patch.Text);
                landing = landing with { Conflicts = new ValueList<string>(conflicts) };
                if (conflicts.Count == 0)
                    UpdateHydraHead(id, value => value with { BaseTree = patch.After });
            }
            catch (Exception error)
            {
                landing = landing with
                {
                    PatchPath = KeepHydraPatch(patch.Text, info, head.Id),
                    Error = error.Message,
                };
            }

            HydraTreeCache.Invalidate(checkout);
            ExistingRuntime(parentID)?.NoteDiffChanged();
            NoteHydraLanding(landing, info, parentID);
            return landing;
        }
        finally
        {
            UpdateHydraHead(id, value => value with { LandingFinishedAt = SwiftDate.Now });
        }
    }

    private string? KeepHydraPatch(string text, HydraHeadInfo info, Guid headID)
    {
        var slug = SwiftString.Lowercased(info.Persona.Name.Replace(' ', '-'));
        var id = SwiftString.Lowercased(SwiftUUID.UuidString(headID));
        var path = Path.Combine(Storage.PatchesDirectory, $"{slug}-{id[..8]}.patch");
        try
        {
            Directory.CreateDirectory(Storage.PatchesDirectory);
            FileOps.WriteAtomic(path, Encoding.UTF8.GetBytes(text));
            return path;
        }
        catch
        {
            return null;
        }
    }

    // :886-909
    private void NoteHydraLanding(HydraLanding landing, HydraHeadInfo info, Guid parentID)
    {
        if (landing.Conflicts.Count > 0)
        {
            var fileWord = landing.Conflicts.Count == 1 ? "file" : "files";
            var markedFiles = landing.Conflicts.Count == 1 ? "this file" : "these files";
            var paths = string.Join(", ", landing.Conflicts.Select(path => $"`{path}`"));
            var message = $"{info.Persona.Name}'s work landed; the lead is settling a conflict in {landing.Conflicts.Count} {fileWord}.\n{paths}\n\n" +
                $"The checkout had moved on where {info.Persona.Name} was working, so the merge was three-way and left markers in {markedFiles}. The lead resolves them before the work goes out; nothing to do on your side.";
            Runtime(parentID).AppendHydraNote(message);
        }

        if (landing.PatchPath is { } patchPath)
        {
            var paths = string.Join(", ", landing.Files.Select(file => $"`{file.Path}`"));
            var message = $"{info.Persona.Name}'s work did not land and was kept as a patch.\n{paths}\n\n";
            if (landing.Error is { } error) message += $"Why it did not land: {error}.\n\n";
            message += $"Apply it with `git apply --3way {patchPath}` and settle what conflicts.";
            Runtime(parentID).AppendHydraNote(message);
        }
    }

    // :915-1010
    public void StopHydraHead(Guid id)
    {
        if (Thread(id) is not { Hydra: { } info } head || info.IsFinished) return;
        if (info.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
        {
            if (ExistingRuntime(id) is { IsRunning: true } runtime) runtime.Interrupt();
            else FinishHydraHead(id, TurnStatus.Interrupted);
            return;
        }

        // AppModel+Hydra.swift:926-927: runtime(for: parentID) makes the lead's runtime when it is not live.
        if (head.ParentThreadID is not { } parentID || info.NativeID is not { } nativeID) return;
        var leadRuntime = Runtime(parentID);
        MainActor.Spawn(async () =>
        {
            if (!await leadRuntime.StopNativeHead(nativeID))
                UpdateHydraHead(id, value => value with { CanStop = false });
        });
    }

    public void StopAllHydraHeads(Guid parentID)
    {
        foreach (var head in HydraHeads(parentID).Where(head => head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running))
            StopHydraHead(head.Id);
    }

    public void DismissHydraHeads(Guid parentID)
    {
        ClearFinishedHydraHeads(parentID);
        var runtime = Runtime(parentID); // AppModel+Hydra.swift:946 runtime(for: parentID)
        runtime.IsHydraPanelHidden = true;
        runtime.HydraSelectedHeadID = null;
        runtime.HydraPoppedHeadID = null;
        runtime.HydraAutoPopHeld = ValueSet<Guid>.Empty;
        runtime.HydraAutoPanelDocks = ValueMap<Guid, PanelDockCorner>.Empty;
        runtime.PanelStackOrder = new ValueList<FloatingPanelID>(runtime.PanelStackOrder
            .Where(panel => panel is not FloatingPanelID.Auto));
    }

    public void ClearFinishedHydraHeads(Guid parentID)
    {
        foreach (var head in HydraHeads(parentID).Where(head => head.Hydra?.IsFinished == true))
        {
            UpdateThread(head.Id, thread => thread with { IsInPanel = false });
            ReleaseHydraCopy(head.Id);
        }
    }

    public void ReleaseHydraCopy(Guid id)
    {
        if (Thread(id) is not { Hydra: { HasOwnCopy: true } info, WorktreePath: { } copy } head ||
            Project(head.ProjectID) is not { } project || ExistingRuntime(id) is { IsRunning: true })
            return;
        var unlanded = info.HydraHeadInfoStatus != HydraHeadInfo.Status.Completed || info.Landing is null;
        ExistingRuntime(id)?.StopSession();
        UpdateThread(id, thread => thread with { WorktreePath = null, Branch = null });
        UpdateHydraHead(id, value => value with { BaseTree = null });
        var parentID = head.ParentThreadID;
        var baseTree = info.BaseTree;
        _ = TaskHandle.Detached(async () =>
        {
            if (unlanded && baseTree is { } start)
                await KeepHydraCopyAsPatch(id, info.Persona.Name, copy, start, parentID);
            try { await new Git(project.Path).RemoveWorktree(copy); }
            catch
            {
                try { FileOps.RemoveItem(copy); }
                catch { }
            }
        });
    }

    // :991-1010
    private async Task KeepHydraCopyAsPatch(Guid id, string name, string copy, string baseTree, Guid? parentID)
    {
        try
        {
            var git = new Git(copy);
            var after = await git.CurrentTree();
            if (after == baseTree) return;
            var (paths, _) = await HydraWork(git, baseTree, after);
            if (paths.Count == 0) return;
            var patch = await git.Diff(baseTree, after, binary: true, paths: paths);
            if (patch.Length == 0) return;
            var slug = SwiftString.Lowercased(name.Replace(' ', '-'));
            var suffix = SwiftString.Lowercased(SwiftUUID.UuidString(id))[..8];
            var path = Path.Combine(Storage.PatchesDirectory, $"{slug}-{suffix}-unlanded.patch");
            Directory.CreateDirectory(Storage.PatchesDirectory);
            FileOps.WriteAtomic(path, Encoding.UTF8.GetBytes(patch));
            var message = $"{name}'s unfinished work was kept as a patch before its copy was cleared.\n{path}\n\n" +
                $"Nothing of it had landed in your checkout. Apply it with `git apply --3way {path}` if you still want it.";
            if (parentID is { } leadID)
                await MainActor.Run(() => Runtime(leadID).AppendHydraNote(message));
        }
        catch
        {
            // The copy is still removed after a failed patch write, matching the cleanup path's best-effort semantics.
        }
    }

    // :1021-1058
    public void StartHydraWatchdog()
    {
        if (HydraWatchdog.Loop is not null) return;
        HydraWatchdog.Loop = MainActor.Spawn(async () =>
        {
            while (true)
            {
                await SwiftTask.TrySleep(HydraWatchdog.Tick);
                if (SwiftTask.IsCancelled) return;
                WatchHydraHeads();
            }
        });
    }

    private void WatchHydraHeads()
    {
        var counts = new Dictionary<Guid, long>();
        foreach (var head in Threads.Where(thread => thread.Hydra is
                     { HydraHeadInfoKind: HydraHeadInfo.Kind.Droppy, HydraHeadInfoStatus: HydraHeadInfo.Status.Running }))
        {
            if (ExistingRuntime(head.Id) is not { IsRunning: true } runtime) continue;
            var idle = runtime.HydraIdleSeconds;
            var tools = runtime.HydraToolCalls;
            counts[head.Id] = tools;
            if (idle >= HydraWatchdog.StalledAfter && runtime.HydraEditCount == 0 &&
                HydraWatchdog.ToolCounts.TryGetValue(head.Id, out var previous) && previous == tools)
            {
                HydraWatchdog.StalledHeadIDs.Add(head.Id);
                HydraWatchdog.Labelled.Remove(head.Id);
                StopHydraHead(head.Id);
                continue;
            }

            if (idle >= HydraWatchdog.ThinkingAfter)
            {
                var label = $"Thinking for {Math.Floor(idle / 60).ToString(CultureInfo.InvariantCulture)} min…";
                if (!StringComparer.Ordinal.Equals(runtime.HydraActivity, label)) runtime.HydraActivity = label;
                HydraWatchdog.Labelled.Add(head.Id);
            }
            else if (HydraWatchdog.Labelled.Remove(head.Id) && runtime.HydraActivity is { } activity &&
                     activity.StartsWith("Thinking for ", StringComparison.Ordinal))
            {
                runtime.HydraActivity = null;
            }
        }

        HydraWatchdog.ToolCounts.Clear();
        foreach (var (id, count) in counts) HydraWatchdog.ToolCounts[id] = count;
        HydraWatchdog.Labelled.IntersectWith(counts.Keys);
    }

    // :1064-1143
    public void SweepHydraCopies()
    {
        var projects = Projects.ToList();
        var projectIDsWithCopies = Threads
            .Where(thread => thread.Hydra?.HasOwnCopy == true)
            .Select(thread => thread.ProjectID)
            .ToHashSet();
        MainActor.Spawn(async () =>
        {
            foreach (var project in projects.Where(project => projectIDsWithCopies.Contains(project.Id)))
                await new Git(project.Path).PruneWorktrees();
            await SweepOrphanHydraCopies(projects);
        });

        foreach (var thread in Threads.Where(thread => thread.Hydra?.HasOwnCopy == true && thread.WorktreePath is not null).ToList())
        {
            if (thread.WorktreePath is not { } path || Directory.Exists(path)) continue;
            UpdateThread(thread.Id, value => value with { WorktreePath = null, Branch = null });
            UpdateHydraHead(thread.Id, value => value with { BaseTree = null });
        }
        StartHydraWatchdog();
    }

    private async Task SweepOrphanHydraCopies(IReadOnlyList<Project> projects)
    {
        if (!Directory.Exists(Storage.WorktreesDirectory)) return;
        var named = new HashSet<string>(Threads
            .Where(thread => thread.WorktreePath is not null)
            .Select(thread => Path.GetFileName(thread.WorktreePath!)), StringComparer.OrdinalIgnoreCase);
        var projectSlugs = projects.Select(project => (Project: project,
                Slug: SwiftString.Lowercased(project.Name.Replace(' ', '-'))))
            .OrderByDescending(item => item.Slug.Length).ToList();
        var personas = HydraRoster.Personas
            .Select(persona => SwiftString.Lowercased(persona.Name.Replace(' ', '-')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in Directory.EnumerateDirectories(Storage.WorktreesDirectory).ToArray())
        {
            var name = Path.GetFileName(folder);
            if (named.Contains(name) || DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) < TimeSpan.FromSeconds(600)) continue;
            var match = projectSlugs.FirstOrDefault(item => name.StartsWith(item.Slug + "-", StringComparison.OrdinalIgnoreCase));
            if (match.Project is null || !IsHydraOrphanTail(name[(match.Slug.Length + 1)..], personas)) continue;
            try { await new Git(match.Project.Path).RemoveWorktree(folder); }
            catch
            {
                try { FileOps.RemoveItem(folder); }
                catch { }
            }
        }
    }

    private static bool IsHydraOrphanTail(string tail, HashSet<string> personas)
    {
        foreach (var persona in personas)
        {
            var prefix = persona + "-";
            if (!tail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = tail[prefix.Length..];
            if (IsEightHex(suffix)) return true;
            var roundEnd = suffix.IndexOf('-', StringComparison.Ordinal);
            if (roundEnd <= 0 || !suffix[..roundEnd].All(char.IsAsciiDigit)) continue;
            if (IsEightHex(suffix[(roundEnd + 1)..])) return true;
        }
        return false;
    }

    private static bool IsEightHex(string value) => value.Length == 8 && value.All(Uri.IsHexDigit);

    internal (string? LastUserPrompt, string? LastReply, ValueList<string> TouchedPaths) HydraChatContext(Guid parentID)
    {
        if (ExistingRuntime(parentID) is not { } runtime) return (null, null, ValueList<string>.Empty);
        string? prompt = null;
        string? reply = null;
        foreach (var item in runtime.Entries.Reverse().Select(entry => entry.Item))
        {
            if (prompt is null && item.TimelineItemContent is TimelineItem.Content.User user && !user.Value.IsFromHydra)
                prompt = user.Value.Text;
            if (reply is null && item.TimelineItemContent is TimelineItem.Content.Assistant assistant)
                reply = assistant.Value.Text;
            if (prompt is not null && reply is not null) break;
        }

        var currentTurn = runtime.Turns.LastOrDefault()?.Id;
        var paths = new List<string>();
        var seen = new HashSet<string>(SwiftStringComparer.Instance);
        if (currentTurn is { } turnID)
        {
            foreach (var entry in runtime.Entries.Where(entry => entry.TurnID == turnID && entry.Item.TimelineItemContent is TimelineItem.Content.Tool))
            {
                var tool = (TimelineItem.Content.Tool)entry.Item.TimelineItemContent;
                foreach (var edit in tool.Value.Edits)
                    if (edit.Path.Length > 0 && seen.Add(edit.Path)) paths.Add(edit.Path);
            }
        }
        return (prompt, reply, new ValueList<string>(paths));
    }

    // :310-313
    private string? HydraCheckout(ChatThread lead) => lead.WorktreePath ?? Project(lead.ProjectID)?.Path;

    // :344-347. The checkout a head's copy is made from and its work lands in: the lead's own for a head in the
    // lead's project (which may be the lead's own worktree, not the bare project path), else the head's project folder.
    private string? HydraCheckout(ChatThread head, ChatThread lead) =>
        head.ProjectID == lead.ProjectID ? HydraCheckout(lead) : Project(head.ProjectID)?.Path;

    // :28-107
    private static class HydraTreeCache
    {
        private const double LifetimeSeconds = 3;
        private static readonly object Gate = new();
        private static readonly Dictionary<string, ReadyCapture> Captures = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, InFlightCapture> InFlight = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> Generations = new(StringComparer.OrdinalIgnoreCase);

        private sealed record ReadyCapture(long Generation, DateTimeOffset CapturedAt, string Tree, string Commit);
        private sealed record InFlightCapture(long Generation, Task<CaptureResult> Task);
        internal sealed record CaptureResult(string Tree, string Commit);

        internal static void Invalidate(string checkout)
        {
            var key = Key(checkout);
            lock (Gate)
            {
                Generations[key] = Generation(key) + 1;
                Captures.Remove(key);
            }
        }

        internal static async Task<CaptureResult> Capture(string checkout, Git git)
        {
            var key = Key(checkout);
            while (true)
            {
                SwiftTask.CheckCancellation();
                long generation;
                ReadyCapture? ready;
                Task<CaptureResult>? running;
                var startedCapture = false;
                lock (Gate)
                {
                    generation = Generation(key);
                    Captures.TryGetValue(key, out ready);
                    running = InFlight.TryGetValue(key, out var entry) && entry.Generation == generation ? entry.Task : null;
                    if (ready is null || ready.Generation != generation ||
                        (SwiftDate.Now - ready.CapturedAt).TotalSeconds >= LifetimeSeconds)
                        ready = null;
                    if (running is null && ready is null)
                    {
                        Task<CaptureResult> capture;
                        using (ExecutionContext.SuppressFlow())
                        {
                            capture = Task.Run(async () =>
                            {
                                var tree = await git.CurrentTree();
                                var commit = await git.CommitTree(tree, "Nth: a Hydra head's starting point");
                                return new CaptureResult(tree, commit);
                            });
                        }
                        running = capture;
                        startedCapture = true;
                        InFlight[key] = new InFlightCapture(generation, capture);
                    }
                }

                if (ready is not null)
                {
                    var currentTree = await git.CurrentTree();
                    SwiftTask.CheckCancellation();
                    lock (Gate)
                    {
                        if (Generation(key) != generation) continue;
                        if (currentTree == ready.Tree) return new CaptureResult(ready.Tree, ready.Commit);
                        if (Captures.TryGetValue(key, out var current) && ReferenceEquals(current, ready)) Captures.Remove(key);
                    }
                    continue;
                }

                try
                {
                    var made = await running!;
                    if (!startedCapture) SwiftTask.CheckCancellation();
                    lock (Gate)
                    {
                        if (Generation(key) != generation) continue;
                        Captures[key] = new ReadyCapture(generation, SwiftDate.Now, made.Tree, made.Commit);
                        if (InFlight.TryGetValue(key, out var entry) && ReferenceEquals(entry.Task, running)) InFlight.Remove(key);
                        return made;
                    }
                }
                catch
                {
                    lock (Gate)
                        if (InFlight.TryGetValue(key, out var entry) && ReferenceEquals(entry.Task, running)) InFlight.Remove(key);
                    throw;
                }
            }
        }

        private static long Generation(string key) => Generations.TryGetValue(key, out var generation) ? generation : 0;
        private static string Key(string path) => SwiftPath.Normalize(SwiftPath.StandardizingPath(path));
    }
}
