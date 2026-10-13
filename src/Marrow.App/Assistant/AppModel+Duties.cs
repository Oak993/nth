// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4, 15.1; unit AS-10; ED-NEW-dextra-as10-13). No Swift twin.
// The duties as the app holds them: one DutyHost over Dextra's controller and store, started by BackgroundStart (at launch, when
// there are duties) or by AS-8 when it starts Dextra. It lives in AppModel, not in a window, so it keeps running with the window
// closed: that is the hidden window (the visual tree is dropped, ParkMain in WindowManager; the model, its sessions, its timers and
// this host stay).
using Marrow.App.Assistant;
using Marrow.App.Tray;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App;

public sealed partial class AppModel
{
    /// <summary>The duties' host while Dextra's duties run; null before <see cref="StartDuties"/>.</summary>
    public DutyHost? Duties { get; private set; }

    /// <summary>
    /// The duty tools StartDextra's controller is made with (it is made before the scheduler exists): StartDuties binds the real ones, and
    /// until then a duty tool says the duties are not running. Whoever starts Dextra gets this by default.
    /// </summary>
    public ForwardingDutyTools DutyToolsGate { get; } = new();

    /// <summary>
    /// Starts the duties over Dextra's controller and store. <paramref name="tools"/> is the forwarding handle StartDextra was given (the
    /// tool host is made before the scheduler and bound here). Starts the scheduler, so what came due while nth was closed is caught up
    /// or skipped. Returns the host; AppModel disposes it with the safety services.
    /// </summary>
    public DutyHost StartDuties(AssistantController controller, AssistantStore store, StartupMode mode, ForwardingDutyTools? tools = null,
        Func<IReadOnlyList<string>>? assistantIds = null, AppTray? tray = null, Action? quit = null, DutySeams? seams = null, DigestSetup? digest = null, bool withDigest = true)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(store);
        Duties?.Dispose();
        var host = new DutyHost(new DutyHostOptions
        {
            Store = store,
            Controller = controller,
            Events = AssistantEvents,
            AssistantIds = assistantIds ?? (() => store.AssistantIds()),
            OnMain = work => MainActor.Post(work),
            Projects = () => [.. Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path))],
            FolderGranted = path => IsFolderOpenToDextra(path, controller),
            ExePath = () => Environment.ProcessPath ?? "",
            Mode = mode,
            Tray = tray ?? Tray,
            Quit = quit,
            Web = seams?.Web ?? new WebWatcher(),
            RunKey = seams?.RunKey ?? new RegistryRunKey(),
            TaskHost = seams?.TaskHost ?? new SchtasksHost(),
            Processes = seams?.Processes ?? SystemProcessRunner.Instance,
            Desktop = seams?.Desktop ?? new SystemDesktopWatch(),
            Time = seams?.Time ?? TimeProvider.System,
            Notify = NotifyDutyFinding,
            ChatProject = thread => Thread(thread)?.ProjectID,
            AssistantName = id => id,
            AccountNames = () => [.. Settings.Accounts.Accounts.Select(a => Settings.Accounts.DisplayName(a.Id)).OfType<string>().Where(n => !string.IsNullOrWhiteSpace(n))],
            Digest = withDigest ? digest ?? DigestSetupOf(controller) : null,
        });
        (tools ?? DutyToolsGate).Bind(host.Tools);
        Duties = host;
        return host;
    }

    /// <summary>
    /// What the morning digest reads of the app beyond the controller (AS-11): which sign-ins expired, the owner's chats as habits (a chat hidden from
    /// the Assistant is left out, one on a local model or a private account is marked local), the projects with when he last worked in each, and the
    /// notes' folding. <paramref name="auditDirectory"/> is where the activity log's day files are (their last closed day is sealed into the digest);
    /// without it the digest has no seal. The delegate's answers (AS-22) are the questions it answered since the digest's last (its book, counted per chat, each with Override in the card); each Astur's paragraph has no source yet (AS-16) and is left empty.
    /// </summary>
    public DigestSetup DigestSetupOf(AssistantController controller, string? auditDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new DigestSetup
        {
            DayFinal = auditDirectory is null ? null : day => AuditVerifier.DayFinal(auditDirectory, day),
            ExpiredLogins = () => [.. Settings.Accounts.Accounts.Where(a => a.Enabled && !IsAccountConnected(a))
                .Select(a => Settings.Accounts.DisplayName(a.Id)).OfType<string>().Where(n => !string.IsNullOrWhiteSpace(n))],
            Habits = () => ChatHabits.Of(Threads, DateTimeOffset.UtcNow, thread => FeedChatFacts.From(thread, Thread, AssistantTaps.IsPrivateAccount),
                project => Projects.FirstOrDefault(p => p.Id == project)?.Name),
            Projects = () => [.. Projects.Select(p => new SpotProject(p.Id, p.Name,
                Threads.Where(t => t.ProjectID == p.Id && t.MissionID is null && t.AssistantID is null).Select(t => t.UpdatedAt).DefaultIfEmpty(DateTimeOffset.MinValue).Max()))],
            AnsweredSince = since => controller.Responder is { } responder
                ? [.. responder.Book.Digest(since).Select(line => new DigestAnswered(line.Title, line.Count, new DigestLink(DigestLinkKind.Thread, line.Chat.ToString("D"))))]
                : [],
            FoldNotes = () => controller.Notes?.Store.FoldIfDue(),
        };
    }

    // A folder inside one of the owner's projects, or one he granted Dextra (a standing folder grant), is open to it (7.2).
    private bool IsFolderOpenToDextra(string path, AssistantController controller) =>
        Projects.Any(p => PathRules.IsInside(path, p.Path, strict: false))
        || controller.Grants.Any(g => g.Target.Kind == GrantTargetKind.Folder && g.Target.Covers(GrantTarget.Folder(path)));

    private void DisposeDuties()
    {
        DutyToolsGate.Bind(null);
        Duties?.Dispose();
        Duties = null;
    }
}
