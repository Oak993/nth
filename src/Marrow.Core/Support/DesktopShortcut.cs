namespace Marrow.Core.Support;

/// <summary>How far a desktop scan may go (PLAN.md 8a, "Hook budgets").</summary>
public enum ShortcutScan
{
    /// <summary>Inside a Velopack hook: stop after 2,000 links or 3 s and record <c>scanCapped</c>.</summary>
    Hook,

    /// <summary>nth's self-heal and the uninstaller: no cap.</summary>
    Full,
}

/// <summary>What a shortcut to nth starts: copied from Velopack's Start shortcut, or <c>current\nth.exe</c> when Start is missing.</summary>
/// <param name="FromStart">True when the fields were copied from Start.</param>
public sealed record LaunchTarget(string Target, string Arguments, string WorkingDirectory, string IconPath, int IconIndex, bool FromStart);

/// <summary>A link a scan found, with what it read.</summary>
public sealed record FoundLink(string Path, ShellLink Link);

/// <summary>The links a scan kept, and whether it stopped at its cap first.</summary>
public sealed record ShortcutScanResult(IReadOnlyList<FoundLink> Links, bool Capped);

/// <summary>
/// What a repair or a removal did: links changed or removed, links found (for a repair, the ones nth made), whether the scan
/// stopped at its cap, whether the step was skipped because another writer held the lock for 2 s, links that failed,
/// and the first repair failure's HRESULT when available.
/// </summary>
public sealed record ShortcutChangeResult(int Changed, int Found, bool Capped, bool Busy, int Failed, int? HResult = null)
{
    public static ShortcutChangeResult WriterBusy { get; } = new(0, 0, Capped: false, Busy: true, Failed: 0);
}

/// <summary>What happened to Velopack's Start shortcut (<c>Programs\nth.lnk</c>).</summary>
public sealed record StartLinkResult(bool Found, bool Restamped = false, bool Retargeted = false, bool IconRepaired = false, bool Busy = false, int? HResult = null);

/// <summary>What the clean-up of 1.0.0's <c>Programs\nth\nth.lnk</c> did (VELOPACK_CHECK.md check 14).</summary>
public enum OldStartLinkOutcome
{
    /// <summary>There is no <c>Programs\nth\nth.lnk</c>.</summary>
    None,

    /// <summary>It was moved to <c>Programs\nth.lnk</c>, keeping every field and property.</summary>
    Moved,

    /// <summary><c>Programs\nth.lnk</c> into the root already existed, so the old link was deleted.</summary>
    DeletedOld,

    /// <summary>The old link points outside the root, or <c>Programs\nth.lnk</c> does: neither is nth's, both were left.</summary>
    LeftForeign,

    /// <summary>Another writer held the lock for 2 s; nothing was done.</summary>
    Busy,

    /// <summary>The shell refused (see the HRESULT).</summary>
    Failed,
}

/// <summary>The outcome of <see cref="DesktopShortcut.MoveOldStartLink"/>, and whether the emptied <c>Programs\nth</c> folder was removed.</summary>
public sealed record OldStartLinkResult(OldStartLinkOutcome Outcome, bool FolderRemoved = false, int? HResult = null);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U5, ED-NEW-inst-u5-1). Everything nth does with the
/// desktop shortcut and Velopack's Start shortcut, with no Windows calls: it reaches the shell only through
/// <see cref="IShellLinks"/>, so every rule is tested on <c>FakeShellLinks</c>. It is used by the app's three Velopack hooks
/// and its start-time self-heal (<c>InstallHooks</c>), by nth's Settings (unit U8: <see cref="Create"/>,
/// <see cref="FindMade"/>, <see cref="RemoveMade"/>), and by nth Setup (linked source: the post-exit Start re-stamp and the
/// uninstaller's <see cref="RemoveIntoRoot"/>).
/// <list type="bullet">
/// <item>The desktop shortcut is <c>&lt;Desktop&gt;\nth.lnk</c>, its fields copied from Start (<see cref="LaunchTarget(string, ShellLink?, Func{string, bool}, Func{string, string})"/>),
/// paths under <c>%LOCALAPPDATA%</c> stored with the variable, AppUserModelID nth's, and nth's marker.</item>
/// <item>"Made by nth" means the marker. Only those are repaired or removed by Settings; an unmarked link into the root is
/// the user's own and only gets its target re-pointed when that target is gone; every link into the root goes at uninstall.</item>
/// <item>Repair never creates; a file name is never changed; cloud placeholders are never read by a scan.</item>
/// <item>Create, repair and remove hold the one-writer lock (2 s wait, else skipped and reported).</item>
/// </list>
/// Errors never escape <see cref="Create"/> (it reports <c>failed</c> with the HRESULT); the other members report shell
/// failures in their results. This file is also compiled into src/Marrow.Setup, so it uses nothing from Marrow.Core but the
/// other linked installer files.
/// </summary>
public sealed class DesktopShortcut
{
    /// <summary><c>HRESULT_FROM_WIN32(ERROR_TIMEOUT)</c>: the step was skipped because another writer held the lock.</summary>
    public const int WriterBusyHResult = unchecked((int)0x800705B4);

    /// <summary><c>E_FAIL</c>, for an error that carries no HRESULT of its own.</summary>
    public const int UnknownFailureHResult = unchecked((int)0x80004005);

    private const string LocalAppDataVariable = "%LOCALAPPDATA%";

    private readonly IShellLinks shell;
    private readonly TimeProvider time;

    public DesktopShortcut(IShellLinks shell, string root, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException($"'{root}' is not a full path.", nameof(root));
        this.shell = shell;
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>The install root (<c>%LOCALAPPDATA%\Programs\nth</c> by default).</summary>
    public string Root { get; }

    /// <summary>Velopack's Start shortcut, <c>Programs\nth.lnk</c> (<c>--shortcuts StartMenuRoot</c>).</summary>
    public string StartLinkPath => Path.Combine(shell.KnownFolder(ShellKnownFolder.Programs), ShellLinkKeys.LinkFileName);

    /// <summary>1.0.0's Start shortcut, <c>Programs\nth\nth.lnk</c>.</summary>
    public string OldStartLinkPath => Path.Combine(shell.KnownFolder(ShellKnownFolder.Programs), InstallLayout.OldStartFolder, ShellLinkKeys.LinkFileName);

    /// <summary>The desktop shortcut's path on the user's own desktop (a OneDrive-redirected one included).</summary>
    public string DesktopLinkPath => Path.Combine(shell.KnownFolder(ShellKnownFolder.Desktop), ShellLinkKeys.LinkFileName);

    /// <summary>
    /// The folders a scan looks in: the user's desktop and, when the desktop is redirected and the profile's own
    /// <c>Desktop</c> folder still exists, that one too.
    /// </summary>
    public IReadOnlyList<string> DesktopFolders()
    {
        var desktop = Path.TrimEndingDirectorySeparator(shell.KnownFolder(ShellKnownFolder.Desktop));
        var profileDesktop = Path.Combine(shell.KnownFolder(ShellKnownFolder.Profile), "Desktop");
        return !string.Equals(desktop, profileDesktop, StringComparison.OrdinalIgnoreCase) && shell.FolderWriteTimeUtc(profileDesktop) is not null
            ? [desktop, profileDesktop]
            : [desktop];
    }

    /// <summary>
    /// The launch target (PLAN.md 8a, as amended by VELOPACK_CHECK.md check 2). When Start exists and its target is a file
    /// inside the root, its target, arguments, working directory and icon are copied, so the two shortcuts never disagree.
    /// Otherwise it is <c>&lt;root&gt;\current\nth.exe</c> in <c>&lt;root&gt;\current</c>, which is what Velopack itself
    /// writes: never the root stub (it would put Update.exe, labelled "Velopack", into every launch), and never Update.exe
    /// (<c>current\</c> keeps its path through updates, so no third fallback exists).
    /// </summary>
    public static LaunchTarget LaunchTarget(string root, ShellLink? start, Func<string, bool> fileExists, Func<string, string> expand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(expand);
        if (start is not null && Full(expand(start.Target)) is { } target && InstallLayout.IsInside(root, target) && fileExists(target))
        {
            var workingDirectory = Full(expand(start.WorkingDirectory)) ?? Path.GetDirectoryName(target) ?? "";
            var hasIcon = !string.IsNullOrWhiteSpace(start.IconPath);
            var icon = hasIcon ? Full(expand(start.IconPath)) ?? target : target;
            return new LaunchTarget(target, start.Arguments, workingDirectory, icon, hasIcon ? start.IconIndex : 0, FromStart: true);
        }
        var current = InstallLayout.CurrentExe(root);
        return new LaunchTarget(current, "", InstallLayout.CurrentDirectory(root), current, 0, FromStart: false);
    }

    /// <summary>The launch target on this machine now: Start is read if it exists.</summary>
    public LaunchTarget CurrentLaunchTarget(TimeSpan? readTimeout = null) => ReadLaunchTarget(() => readTimeout);

    private LaunchTarget ReadLaunchTarget(Func<TimeSpan?> readTimeout)
    {
        ShellLink? start = null;
        var startPath = StartLinkPath;
        if (shell.FileExists(startPath))
        {
            try { var allowance = readTimeout(); start = allowance is { } timeout ? shell.ReadLink(startPath, timeout) : shell.ReadLink(startPath); }
            catch (Exception error) when (IsShellFailure(error)) { } // an unreadable Start link counts as missing
        }
        return LaunchTarget(Root, start, shell.FileExists, shell.ExpandEnvironment);
    }

    /// <summary>
    /// The desktop shortcut nth writes for this launch target: target, working directory and icon stored with
    /// <c>%LOCALAPPDATA%</c> when they are under it (so a synced shortcut works on the user's other PCs), Start's arguments,
    /// show-command normal, description "nth", nth's AppUserModelID and nth's marker.
    /// </summary>
    public ShellLink Expected(LaunchTarget launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return new ShellLink(
            Target: EnvironmentForm(launch.Target),
            Arguments: launch.Arguments,
            WorkingDirectory: EnvironmentForm(launch.WorkingDirectory),
            IconPath: EnvironmentForm(launch.IconPath),
            IconIndex: launch.IconIndex,
            Description: ShellLinkKeys.Description,
            ShowCommand: ShellLinkKeys.ShowNormal,
            AppUserModelId: ShellLinkKeys.NthAppUserModelId,
            CreatedBy: ShellLinkKeys.CreatedByValue);
    }

    /// <summary>A full path under the user's <c>%LOCALAPPDATA%</c> written with the variable; any other path unchanged.</summary>
    public string EnvironmentForm(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var local = Path.TrimEndingDirectorySeparator(shell.KnownFolder(ShellKnownFolder.LocalAppData));
        return local.Length > 0 && path.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? LocalAppDataVariable + path[local.Length..]
            : path;
    }

    /// <summary>Whether the link's stored target, expanded and made full, is inside the root (never resolved).</summary>
    public bool IntoRoot(ShellLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return !string.IsNullOrWhiteSpace(link.Target) && InstallLayout.IsInside(Root, shell.ExpandEnvironment(link.Target));
    }

    /// <summary>Explorer's "Show desktop icons" is off; a shell that cannot say counts as shown.</summary>
    public bool IconsHidden()
    {
        try { return shell.DesktopIconsHidden(); }
        catch (Exception error) when (IsShellFailure(error)) { return false; }
    }

    /// <summary>
    /// What the install hook does about the desktop shortcut for these options (PLAN.md 8a's table): no file → nothing
    /// (null); a fresh install with the switch on → <see cref="Create"/>; off → <c>off</c>, nothing written; an update
    /// (<c>"kind":"update"</c>, which is how an upgrade through nth Setup reaches the install hook, VELOPACK_CHECK.md check 15)
    /// → <see cref="Repair"/> only, never a new shortcut, reported as <c>kept</c> when nth's shortcut is there and <c>off</c>
    /// when not.
    /// </summary>
    public InstallResult? Apply(InstallOptions? options, ShortcutScan scan = ShortcutScan.Hook, TimeSpan? allowance = null)
    {
        if (options is null) return null;
        var budget = new ShortcutBudget(time, scan, allowance);
        return options.Kind switch
        {
            InstallKind.Install when options.DesktopShortcut => Create(scan, budget),
            InstallKind.Install => new InstallResult(DesktopShortcutOutcome.Off, IconsHidden: budget.CanStartCall && IconsHidden()),
            InstallKind.Update => UpdateResult(scan, budget),
        };
    }

    private InstallResult UpdateResult(ShortcutScan scan, ShortcutBudget budget)
    {
        var hidden = budget.CanStartCall && IconsHidden();
        var repair = Repair(scan, budget);
        if (repair.Failed > 0)
            return InstallResult.FailedWith(repair.HResult ?? UnknownFailureHResult) with { IconsHidden = hidden, ScanCapped = repair.Capped };
        var present = repair.Found > 0 || (repair.Busy && SafeFileExists(DesktopLinkPath));
        return new InstallResult(present ? DesktopShortcutOutcome.Kept : DesktopShortcutOutcome.Off, IconsHidden: hidden, ScanCapped: repair.Capped);
    }

    /// <summary>
    /// Makes <c>&lt;Desktop&gt;\nth.lnk</c> (PLAN.md 8a). An existing <c>nth.lnk</c> (the one file that may be read even as a
    /// cloud placeholder) decides first: nth's marker → repaired, <c>kept</c>; unmarked into the root → its target re-pointed
    /// only if gone, <c>kept</c>; anything else → untouched, <c>name-taken</c> (never "nth (2)"). A shortcut nth made under
    /// another name (a user's rename) is <c>kept</c> too. Otherwise the link is written and Explorer told: <c>created</c>.
    /// Any shell error gives <c>failed</c> with its HRESULT; another writer holding the lock for 2 s gives <c>failed</c> with
    /// <see cref="WriterBusyHResult"/>. It never throws for the shell.
    /// </summary>
    public InstallResult Create(ShortcutScan scan = ShortcutScan.Full, TimeSpan? allowance = null) => Create(scan, new ShortcutBudget(time, scan, allowance));

    private InstallResult Create(ShortcutScan scan, ShortcutBudget budget)
    {
        var hidden = budget.CanStartCall && IconsHidden();
        try
        {
            using var writer = shell.TryEnterWriter(budget.WriterWait);
            if (writer is null) return InstallResult.FailedWith(WriterBusyHResult) with { IconsHidden = hidden };
            return CreateLocked(scan, budget) with { IconsHidden = hidden };
        }
        catch (Exception error) when (IsShellFailure(error))
        {
            return InstallResult.FailedWith(HResultOf(error)) with { IconsHidden = hidden };
        }
    }

    private InstallResult CreateLocked(ShortcutScan scan, ShortcutBudget budget)
    {
        var path = DesktopLinkPath;
        if (shell.FileExists(path))
        {
            if (!budget.CanStartCall) return CappedCreate();
            var existing = shell.ReadLink(path, budget.ReadTimeout);
            if (existing.IsMadeByNth)
            {
                var launch = CurrentLaunchTarget(budget.ReadTimeout);
                if (!budget.CanStartCall) return CappedCreate();
                RepairMade(path, existing, launch);
                return new InstallResult(DesktopShortcutOutcome.Kept);
            }
            if (IntoRoot(existing))
            {
                var launch = CurrentLaunchTarget(budget.ReadTimeout);
                if (!budget.CanStartCall) return CappedCreate();
                FixGoneTarget(path, existing, launch);
                return new InstallResult(DesktopShortcutOutcome.Kept);
            }
            return new InstallResult(DesktopShortcutOutcome.NameTaken);
        }
        var made = Scan(link => link.IsMadeByNth, scan, budget);
        if (made.Links.Count > 0)
        {
            if (!budget.CanStartCall) return CappedCreate();
            var current = CurrentLaunchTarget(budget.ReadTimeout);
            foreach (var found in made.Links)
            {
                if (!budget.CanStartCall) return CappedCreate();
                RepairMade(found.Path, found.Link, current);
            }
            return new InstallResult(DesktopShortcutOutcome.Kept, ScanCapped: made.Capped);
        }
        if (!budget.CanStartCall) return CappedCreate();
        var target = CurrentLaunchTarget(budget.ReadTimeout);
        if (!budget.CanStartCall) return CappedCreate();
        shell.WriteLink(path, Expected(target));
        Notify(ShellChange.Created, path);
        return new InstallResult(DesktopShortcutOutcome.Created, ScanCapped: made.Capped);
    }

    private static InstallResult CappedCreate() => InstallResult.FailedWith(WriterBusyHResult) with { ScanCapped = true };

    /// <summary>The desktop links nth made (marker), placeholders skipped.</summary>
    public ShortcutScanResult FindMade(ShortcutScan scan = ShortcutScan.Full) => Scan(link => link.IsMadeByNth, scan);

    /// <summary>The desktop links whose target is inside the root, marked or not, placeholders skipped.</summary>
    public ShortcutScanResult FindIntoRoot(ShortcutScan scan = ShortcutScan.Full) => Scan(IntoRoot, scan);

    /// <summary>
    /// Repair (PLAN.md 8a): every link nth made gets only the fields that differ from <see cref="Expected"/> rewritten
    /// (target, working directory, icon, AppUserModelID; compared as stored, so an absolute icon Velopack wrote goes back to
    /// the <c>%LOCALAPPDATA%</c> form); an unmarked link into the root gets only its target and working directory re-pointed,
    /// and only when its target is gone. File names stay; nothing is ever created.
    /// </summary>
    public ShortcutChangeResult Repair(ShortcutScan scan = ShortcutScan.Full, TimeSpan? allowance = null) => Repair(scan, new ShortcutBudget(time, scan, allowance));

    private ShortcutChangeResult Repair(ShortcutScan scan, ShortcutBudget budget)
    {
        using var writer = shell.TryEnterWriter(budget.WriterWait);
        if (writer is null) return ShortcutChangeResult.WriterBusy;
        var found = Scan(link => link.IsMadeByNth || IntoRoot(link), scan, budget);
        if (found.Links.Count == 0) return new ShortcutChangeResult(0, 0, found.Capped, Busy: false, Failed: 0);
        var capped = found.Capped;
        if (!budget.CanStartMutation) return new ShortcutChangeResult(0, found.Links.Count(l => l.Link.IsMadeByNth), Capped: true, Busy: false, Failed: 0);
        var launch = ReadLaunchTarget(() => budget.ReadTimeout);
        int changed = 0, failed = 0;
        int? hresult = null;
        foreach (var link in found.Links)
        {
            if (!budget.CanStartMutation) { capped = true; break; }
            try
            {
                if (link.Link.IsMadeByNth ? RepairMade(link.Path, link.Link, launch, budget) : FixGoneTarget(link.Path, link.Link, launch, budget)) changed++;
            }
            catch (Exception error) when (IsShellFailure(error)) { failed++; hresult ??= HResultOf(error); }
        }
        return new ShortcutChangeResult(changed, found.Links.Count(l => l.Link.IsMadeByNth), capped, Busy: false, failed, hresult);
    }

    /// <summary>nth's Settings switch off: deletes every link nth made (renamed ones included) and tells Explorer. A user's own shortcut is never touched.</summary>
    public ShortcutChangeResult RemoveMade(ShortcutScan scan = ShortcutScan.Full, TimeSpan? allowance = null) => Remove(link => link.IsMadeByNth, scan, new ShortcutBudget(time, scan, allowance));

    /// <summary>Uninstall: deletes every link into the root, marked or not, renamed ones included, and tells Explorer (none of them would work afterwards).</summary>
    public ShortcutChangeResult RemoveIntoRoot(ShortcutScan scan = ShortcutScan.Full, TimeSpan? allowance = null) => Remove(IntoRoot, scan, new ShortcutBudget(time, scan, allowance));

    private ShortcutChangeResult Remove(Func<ShellLink, bool> keep, ShortcutScan scan, ShortcutBudget budget)
    {
        using var writer = shell.TryEnterWriter(budget.WriterWait);
        if (writer is null) return ShortcutChangeResult.WriterBusy;
        var found = Scan(keep, scan, budget);
        var capped = found.Capped;
        int removed = 0, failed = 0;
        foreach (var link in found.Links)
        {
            if (!budget.CanStartCall) { capped = true; break; }
            try
            {
                shell.DeleteLink(link.Path);
                Notify(ShellChange.Deleted, link.Path);
                removed++;
            }
            catch (Exception error) when (IsShellFailure(error)) { failed++; }
        }
        return new ShortcutChangeResult(removed, found.Links.Count, capped, Busy: false, failed);
    }

    /// <summary>
    /// Re-stamps Start's AppUserModelID to nth's when it differs (PLAN.md 8a): load, set only <c>PKEY_AppUserModel_ID</c>,
    /// commit, save, so every other property (a ToastActivatorCLSID included) survives. A Start link that is missing or points
    /// outside the root is left alone.
    /// </summary>
    public StartLinkResult RestampStart() => FixStart(retarget: false);

    /// <summary>
    /// The self-heal's check of Start: the AppUserModelID as <see cref="RestampStart"/>, and, for a link into the root, a
    /// target that is gone re-pointed to <c>current\nth.exe</c> (absolute, as Velopack writes it) and an icon file that is
    /// gone pointed at it too, when <c>current\nth.exe</c> exists.
    /// </summary>
    public StartLinkResult HealStart() => FixStart(retarget: true);

    private StartLinkResult FixStart(bool retarget)
    {
        try
        {
            var path = StartLinkPath;
            if (!shell.FileExists(path)) return new StartLinkResult(Found: false);
            using var writer = shell.TryEnterWriter(ShellLinkKeys.WriterWait);
            if (writer is null) return new StartLinkResult(Found: true, Busy: true);
            var link = shell.ReadLink(path);
            if (!IntoRoot(link)) return new StartLinkResult(Found: true);
            var restamp = !string.Equals(link.AppUserModelId, ShellLinkKeys.NthAppUserModelId, StringComparison.Ordinal);
            var current = InstallLayout.CurrentExe(Root);
            var canPoint = retarget && shell.FileExists(current);
            var retargetTo = canPoint && !shell.FileExists(shell.ExpandEnvironment(link.Target)) ? current : null;
            var iconGone = canPoint && !string.IsNullOrWhiteSpace(link.IconPath) && !shell.FileExists(shell.ExpandEnvironment(link.IconPath));
            var change = new ShellLinkChange(
                Target: retargetTo,
                WorkingDirectory: retargetTo is null ? null : InstallLayout.CurrentDirectory(Root),
                IconPath: iconGone ? current : null,
                IconIndex: iconGone ? 0 : null,
                AppUserModelId: restamp ? ShellLinkKeys.NthAppUserModelId : null);
            if (change.IsEmpty) return new StartLinkResult(Found: true);
            shell.UpdateLink(path, change);
            Notify(ShellChange.Updated, path);
            return new StartLinkResult(Found: true, restamp, retargetTo is not null, iconGone);
        }
        catch (Exception error) when (IsShellFailure(error))
        {
            return new StartLinkResult(Found: true, HResult: HResultOf(error));
        }
    }

    /// <summary>
    /// The old Start folder clean-up (PLAN.md 8, as amended by VELOPACK_CHECK.md check 14). 1.0.0 was packed with
    /// <c>--shortcuts StartMenu</c>, and Velopack makes no root Start link while any Start link into the root exists, so the old
    /// link is <b>moved</b> to <c>Programs\nth.lnk</c> (a file move keeps every property and Velopack's fresh target, icon
    /// and AppUserModelID), deleted only when a <c>Programs\nth.lnk</c> into the root is already there, and the emptied
    /// <c>Programs\nth</c> folder is removed. A link that points outside the root, on either side, is left as it is.
    /// </summary>
    public OldStartLinkResult MoveOldStartLink()
    {
        try
        {
            var old = OldStartLinkPath;
            if (!shell.FileExists(old)) return new OldStartLinkResult(OldStartLinkOutcome.None);
            using var writer = shell.TryEnterWriter(ShellLinkKeys.WriterWait);
            if (writer is null) return new OldStartLinkResult(OldStartLinkOutcome.Busy);
            if (!IntoRoot(shell.ReadLink(old))) return new OldStartLinkResult(OldStartLinkOutcome.LeftForeign);
            var start = StartLinkPath;
            OldStartLinkOutcome outcome;
            if (shell.FileExists(start))
            {
                if (!IntoRoot(shell.ReadLink(start))) return new OldStartLinkResult(OldStartLinkOutcome.LeftForeign);
                shell.DeleteLink(old);
                Notify(ShellChange.Deleted, old);
                outcome = OldStartLinkOutcome.DeletedOld;
            }
            else
            {
                shell.MoveLink(old, start);
                Notify(ShellChange.Deleted, old);
                Notify(ShellChange.Created, start);
                outcome = OldStartLinkOutcome.Moved;
            }
            var folder = Path.GetDirectoryName(old)!;
            var removed = shell.DeleteEmptyFolder(folder);
            if (removed) Notify(ShellChange.Deleted, folder);
            return new OldStartLinkResult(outcome, removed);
        }
        catch (Exception error) when (IsShellFailure(error))
        {
            return new OldStartLinkResult(OldStartLinkOutcome.Failed, HResult: HResultOf(error));
        }
    }

    private bool RepairMade(string path, ShellLink link, LaunchTarget launch, ShortcutBudget? budget = null)
    {
        var expected = Expected(launch);
        var sameIcon = SamePath(link.IconPath, expected.IconPath) && link.IconIndex == expected.IconIndex;
        var change = new ShellLinkChange(
            Target: SamePath(link.Target, expected.Target) ? null : expected.Target,
            WorkingDirectory: SamePath(link.WorkingDirectory, expected.WorkingDirectory) ? null : expected.WorkingDirectory,
            IconPath: sameIcon ? null : expected.IconPath,
            IconIndex: sameIcon ? null : expected.IconIndex,
            AppUserModelId: string.Equals(link.AppUserModelId, expected.AppUserModelId, StringComparison.Ordinal) ? null : expected.AppUserModelId);
        if (change.IsEmpty) return false;
        if (budget is not null) shell.UpdateLink(path, change, budget.ReadTimeout);
        else shell.UpdateLink(path, change);
        Notify(ShellChange.Updated, path);
        return true;
    }

    private bool FixGoneTarget(string path, ShellLink link, LaunchTarget launch, ShortcutBudget? budget = null)
    {
        if (shell.FileExists(shell.ExpandEnvironment(link.Target))) return false;
        var expected = Expected(launch);
        var change = new ShellLinkChange(Target: expected.Target, WorkingDirectory: expected.WorkingDirectory);
        if (budget is not null) shell.UpdateLink(path, change, budget.ReadTimeout);
        else shell.UpdateLink(path, change);
        Notify(ShellChange.Updated, path);
        return true;
    }

    /// <summary>
    /// Reads the desktop links one by one, <c>nth*.lnk</c> first, skipping cloud placeholders (never opened, never counted).
    /// A hook scan stops before the 2,001st read or once 3 s have passed and reports <see cref="ShortcutScanResult.Capped"/>.
    /// A link that cannot be read is skipped.
    /// </summary>
    private ShortcutScanResult Scan(Func<ShellLink, bool> keep, ShortcutScan scan, ShortcutBudget? budget = null)
    {
        var started = time.GetTimestamp();
        budget ??= new ShortcutBudget(time, scan, null);
        var found = new List<FoundLink>();
        var reads = 0;
        foreach (var folder in DesktopFolders())
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in (string[])["nth*.lnk", "*.lnk"])
            {
                using var listing = shell.EnumerateLinks(folder, pattern).GetEnumerator();
                while (true)
                {
                    if (Capped()) return new ShortcutScanResult(found, Capped: true);
                    var next = listing.MoveNext();
                    // MoveNext includes the directory listing/attribute cost, even for skipped and duplicate entries.
                    if (Capped()) return new ShortcutScanResult(found, Capped: true);
                    if (!next) break;
                    var file = listing.Current;
                    if (!seen.Add(file.Path) || file.IsCloudPlaceholder) continue;
                    if (scan == ShortcutScan.Hook && reads >= ShellLinkKeys.ScanCapLinks)
                        return new ShortcutScanResult(found, Capped: true);
                    reads++;
                    ShellLink link;
                    try { link = scan == ShortcutScan.Hook ? shell.ReadLink(file.Path, ReadTimeout()) : shell.ReadLink(file.Path); }
                    catch (Exception error) when (IsShellFailure(error))
                    {
                        if (Capped()) return new ShortcutScanResult(found, Capped: true);
                        continue;
                    }
                    if (keep(link)) found.Add(new FoundLink(file.Path, link));
                }
            }
        }
        return new ShortcutScanResult(found, Capped: false);

        bool Capped() => scan == ShortcutScan.Hook && (time.GetElapsedTime(started) >= ShellLinkKeys.ScanCapTime || budget.Remaining <= TimeSpan.Zero);
        TimeSpan ReadTimeout()
        {
            var scanRemaining = ShellLinkKeys.ScanCapTime - time.GetElapsedTime(started);
            return scanRemaining < budget.ReadTimeout ? scanRemaining : budget.ReadTimeout;
        }
    }

    /// <summary>One deadline covers the writer wait, scan, launch read and every mutation of a desktop step.</summary>
    private sealed class ShortcutBudget(TimeProvider time, ShortcutScan scan, TimeSpan? allowance)
    {
        private readonly long started = time.GetTimestamp();
        private readonly TimeSpan limit = scan == ShortcutScan.Full ? Timeout.InfiniteTimeSpan
            : allowance ?? (ShellLinkKeys.WriterWait + ShellLinkKeys.ScanCapTime + ShellLinkKeys.CallTimeout);

        public TimeSpan Remaining => limit == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : TimeSpan.FromTicks(Math.Max(0, (limit - time.GetElapsedTime(started)).Ticks));
        public bool CanStartCall => Remaining >= ShellLinkKeys.CallTimeout;
        public bool CanStartMutation => Remaining > TimeSpan.Zero;
        public TimeSpan ReadTimeout => Remaining < ShellLinkKeys.CallTimeout ? Remaining : ShellLinkKeys.CallTimeout;
        public TimeSpan WriterWait => Remaining < ShellLinkKeys.WriterWait ? Remaining : ShellLinkKeys.WriterWait;
    }

    /// <summary>Tells Explorer; a refused notification never undoes or fails the change it reports.</summary>
    private void Notify(ShellChange change, string path)
    {
        try { shell.Notify(change, path); }
        catch (Exception error) when (IsShellFailure(error)) { } // Explorer catches up on its own next refresh
    }

    private bool SafeFileExists(string path)
    {
        try { return shell.FileExists(path); }
        catch (Exception error) when (IsShellFailure(error)) { return false; }
    }

    private static bool SamePath(string? stored, string? expected) => string.Equals(stored ?? "", expected ?? "", StringComparison.OrdinalIgnoreCase);

    private static string? Full(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>The errors the shell reports through <see cref="IShellLinks"/>: COM, file and access errors, and a call that did not answer.</summary>
    public static bool IsShellFailure(Exception error) =>
        error is System.Runtime.InteropServices.ExternalException or IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException;

    /// <summary>The error's HRESULT, or <see cref="UnknownFailureHResult"/> when it carries none.</summary>
    public static int HResultOf(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.HResult < 0 ? error.HResult : UnknownFailureHResult;
    }
}
