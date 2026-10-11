using System.Globalization;

namespace Marrow.Core.Support;

/// <summary>What a Velopack hook knows when it runs: the install root, the user's local app data folder, the inherited
/// <c>NTH_SETUP_OPTIONS</c> value and the version Velopack passed.</summary>
public sealed record InstallHookContext(string Root, string LocalAppData, string? OptionsVariable, string Version);

/// <summary>One line per step (<c>name: outcome</c>), and the desktop result the install hook wrote, if any.</summary>
public sealed record InstallHookReport(IReadOnlyList<string> Steps, InstallResult? Result);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U5, ED-NEW-inst-u5-1). The work of the app's three
/// Velopack hooks and of nth's start-time self-heal, in the order VELOPACK_CHECK.md settles, each step capped inside the
/// hook's budget and none able to throw into Velopack:
/// <list type="bullet">
/// <item><b>After install</b> (<c>--veloapp-install</c>, 30 s; also an upgrade through nth Setup, check 15): move 1.0.0's
/// Start link, re-stamp Start, the desktop shortcut for <c>install-options.json</c> (none without the file), write
/// <c>install-result.json</c> at once, then copy the uninstaller. The uninstall key does not exist yet (Velopack writes it
/// after the hook); nth Setup re-points it after Setup.exe exits.</item>
/// <item><b>After update</b> (<c>--veloapp-updated</c>, 15 s): move 1.0.0's Start link, re-stamp Start, repair (never
/// create), copy the uninstaller, re-point the uninstall key (Velopack rewrote it just before, check 8, so this is final).
/// Velopack's own shortcut pass runs after this hook and rewrites icons and the AppUserModelID (check 1); the next
/// self-heal restores them.</item>
/// <item><b>Before uninstall</b> (<c>--veloapp-uninstall</c>, ours 30 s, Velopack's 60 s): remove every desktop link into
/// the root and the sign-in entry if it starts nth.</item>
/// <item><b>Self-heal</b> (nth's start, background, no budget): Start's AppUserModelID, target and icon, the desktop links
/// nth made, and the uninstall key.</item>
/// </list>
/// No step prunes old packages (Velopack does it at each start, check 9).
/// </summary>
public sealed class InstallHooks
{
    /// <summary>Velopack's budgets (its hooks page; VELOPACK_CHECK.md check 15): a hook that overruns is killed.</summary>
    public static readonly TimeSpan InstallBudget = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan UpdateBudget = TimeSpan.FromSeconds(15);

    /// <summary>Velopack allows 60 s; nth keeps its stricter 30 s.</summary>
    public static readonly TimeSpan UninstallBudget = TimeSpan.FromSeconds(30);

    /// <summary>A quick step (one or two links) starts only with this much budget left.</summary>
    public static readonly TimeSpan QuickStepNeed = TimeSpan.FromSeconds(2);

    /// <summary>A desktop step needs the writer wait, the scan cap and a margin.</summary>
    public static readonly TimeSpan DesktopStepNeed = ShellLinkKeys.WriterWait + ShellLinkKeys.ScanCapTime + TimeSpan.FromSeconds(2);

    /// <summary>Copying the 20-30 MB uninstaller.</summary>
    public static readonly TimeSpan CopyStepNeed = TimeSpan.FromSeconds(5);

    /// <summary>Measuring the footprint and re-pointing the key.</summary>
    public static readonly TimeSpan KeyStepNeed = TimeSpan.FromSeconds(3);

    private readonly IShellLinks shell;
    private readonly IUserRegistry registry;
    private readonly TimeProvider time;
    private readonly Func<string, UninstallerCopyResult> copyUninstaller;
    private readonly Func<string, long?> measureFootprintKb;
    private readonly Func<string, bool> fileExists;

    /// <param name="copyUninstaller">Defaults to <see cref="InstallLayout.CopyUninstaller"/>.</param>
    /// <param name="measureFootprintKb">Defaults to <see cref="UninstallEntry.MeasureFootprintKb"/>.</param>
    /// <param name="fileExists">Whether the root's uninstaller exists; defaults to <see cref="File.Exists(string)"/>.</param>
    public InstallHooks(IShellLinks shell, IUserRegistry registry, TimeProvider? time = null,
        Func<string, UninstallerCopyResult>? copyUninstaller = null, Func<string, long?>? measureFootprintKb = null, Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(registry);
        this.shell = shell;
        this.registry = registry;
        this.time = time ?? TimeProvider.System;
        this.copyUninstaller = copyUninstaller ?? InstallLayout.CopyUninstaller;
        this.measureFootprintKb = measureFootprintKb ?? UninstallEntry.MeasureFootprintKb;
        this.fileExists = fileExists ?? File.Exists;
    }

    /// <summary>The options file the install hook reads: the inherited variable, else the fixed path for this version (PLAN.md 8).</summary>
    public static string OptionsPath(InstallHookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return InstallOptions.OptionsPathFor(context.OptionsVariable, context.LocalAppData, context.Version);
    }

    public InstallHookReport AfterInstall(InstallHookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = new Run(time, InstallBudget);
        var shortcut = new DesktopShortcut(shell, context.Root, time);
        run.Step("start-move", QuickStepNeed, () => Describe(shortcut.MoveOldStartLink()));
        var restamped = false;
        run.Step("start-restamp", QuickStepNeed, () =>
        {
            var start = shortcut.RestampStart();
            restamped = start.Restamped;
            return Describe(start);
        });
        InstallResult? result = null;
        run.Step("desktop", DesktopStepNeed, () =>
        {
            string optionsPath;
            try { optionsPath = OptionsPath(context); }
            catch (ArgumentException) { return "no options file"; }
            if (InstallOptions.Read(optionsPath) is not { } options) return "no options file";
            result = (shortcut.Apply(options, ShortcutScan.Hook) ?? InstallResult.Missing) with { StartAppIdRestamped = restamped };
            result.Write(InstallOptions.ResultPathFor(optionsPath));
            return Describe(result);
        });
        run.Step("uninstaller-copy", CopyStepNeed, () => Describe(copyUninstaller(context.Root)));
        return run.Report(result);
    }

    public InstallHookReport AfterUpdate(InstallHookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = new Run(time, UpdateBudget);
        var shortcut = new DesktopShortcut(shell, context.Root, time);
        run.Step("start-move", QuickStepNeed, () => Describe(shortcut.MoveOldStartLink()));
        run.Step("start-restamp", QuickStepNeed, () => Describe(shortcut.RestampStart()));
        run.Step("desktop-repair", DesktopStepNeed, () => Describe(shortcut.Repair(ShortcutScan.Hook)));
        run.Step("uninstaller-copy", CopyStepNeed, () => Describe(copyUninstaller(context.Root)));
        run.Step("uninstall-key", KeyStepNeed, () => Describe(RepointKey(context.Root)));
        return run.Report(null);
    }

    public InstallHookReport BeforeUninstall(InstallHookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = new Run(time, UninstallBudget);
        var shortcut = new DesktopShortcut(shell, context.Root, time);
        run.Step("desktop-remove", DesktopStepNeed, () => Describe(shortcut.RemoveIntoRoot(ShortcutScan.Hook)));
        run.Step("sign-in", QuickStepNeed, () => UninstallEntry.RemoveSignInEntry(registry, context.Root) ? "removed" : "none");
        return run.Report(null);
    }

    /// <summary>nth's start-time self-heal (PLAN.md 8, item 2, and 8a). The caller runs it on a background thread after the first window, only in a Velopack install.</summary>
    public InstallHookReport SelfHeal(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var run = new Run(time, Timeout.InfiniteTimeSpan);
        var shortcut = new DesktopShortcut(shell, root, time);
        run.Step("start-heal", TimeSpan.Zero, () => Describe(shortcut.HealStart()));
        run.Step("desktop-repair", TimeSpan.Zero, () => Describe(shortcut.Repair(ShortcutScan.Full)));
        run.Step("uninstall-key", TimeSpan.Zero, () => Describe(RepointKey(root)));
        return run.Report(null);
    }

    private UninstallRepointResult RepointKey(string root) =>
        UninstallEntry.Repoint(registry, root, measureFootprintKb(root), fileExists(InstallLayout.UninstallerPath(root)));

    private static string Describe(OldStartLinkResult result) =>
        result.Outcome.ToString().ToLowerInvariant() + (result.FolderRemoved ? ", folder removed" : "") + HResultText(result.HResult);

    private static string Describe(StartLinkResult result) =>
        !result.Found ? "no start link"
        : result.Busy ? "busy"
        : result.HResult is { } failure ? "failed " + InstallResult.FormatHResult(failure)
        : string.Join(", ", new[] { result.Restamped ? "restamped" : null, result.Retargeted ? "retargeted" : null, result.IconRepaired ? "icon" : null }.OfType<string>().DefaultIfEmpty("unchanged"));

    private static string Describe(InstallResult result) =>
        InstallResult.OutcomeName(result.DesktopShortcut) + HResultText(result.HResult)
        + (result.IconsHidden ? ", icons hidden" : "") + (result.ScanCapped ? ", scan capped" : "") + (result.StartAppIdRestamped ? ", start restamped" : "");

    private static string Describe(ShortcutChangeResult result) =>
        result.Busy ? "busy"
        : string.Create(CultureInfo.InvariantCulture, $"{result.Changed} changed of {result.Found}") + (result.Failed > 0 ? string.Create(CultureInfo.InvariantCulture, $", {result.Failed} failed") : "") + (result.Capped ? ", scan capped" : "");

    private static string Describe(UninstallerCopyResult result) => result.Outcome.ToString().ToLowerInvariant() + HResultText(result.HResult);

    private static string Describe(UninstallRepointResult result) =>
        !result.KeyFound ? "no key"
        : !result.Ours ? "another install's key"
        : string.Create(CultureInfo.InvariantCulture, $"{result.Writes} written") + (result.UninstallerPresent ? "" : ", no uninstaller");

    private static string HResultText(int? hresult) => hresult is { } value ? " " + InstallResult.FormatHResult(value) : "";

    /// <summary>The steps of one hook run: each starts only while its need fits in what is left of the budget, and none throws.</summary>
    private sealed class Run(TimeProvider time, TimeSpan budget)
    {
        private readonly long started = time.GetTimestamp();
        private readonly List<string> steps = [];

        public void Step(string name, TimeSpan need, Func<string> step)
        {
            if (budget != Timeout.InfiniteTimeSpan && budget - time.GetElapsedTime(started) < need)
            {
                steps.Add(name + ": skipped, out of time");
                return;
            }
            try { steps.Add(name + ": " + step()); }
            catch (Exception error)
            {
                steps.Add(name + ": failed " + InstallResult.FormatHResult(DesktopShortcut.HResultOf(error)));
            }
        }

        public InstallHookReport Report(InstallResult? result) => new([.. steps], result);
    }
}
