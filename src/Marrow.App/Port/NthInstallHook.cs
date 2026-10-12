using Marrow.Core.Support;
using Velopack;

namespace Marrow.App.Port;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U5, ED-NEW-inst-u5-1). Program.RunVelopackHook hands
/// this Velopack's <c>OnAfterInstallFastCallback</c>, <c>OnAfterUpdateFastCallback</c> and
/// <c>OnBeforeUninstallFastCallback</c> (Velopack adds callbacks to one multicast delegate, so the skills uninstall hook
/// still runs first). Each callback runs <see cref="InstallHooks"/> with a fresh shell and registry and never throws into
/// Velopack: a thrown hook only makes Velopack log an error and exit -1, and the install goes on regardless (VELOPACK_CHECK.md
/// check 15). <see cref="StartSelfHeal"/> is nth's start-time self-heal. Like SkillsUninstallHook, the registration is a
/// seam so a test can capture the callbacks and call them on fakes; nothing here runs an installer or Velopack.
/// </summary>
internal static class NthInstallHook
{
    internal static void Register(
        Action<VelopackHook> afterInstall,
        Action<VelopackHook> afterUpdate,
        Action<VelopackHook> beforeUninstall,
        Func<SemanticVersion, InstallHookContext> context,
        Func<IShellLinks> shell,
        Func<IUserRegistry> registry,
        Action<InstallHookReport>? reported = null)
    {
        ArgumentNullException.ThrowIfNull(afterInstall);
        ArgumentNullException.ThrowIfNull(afterUpdate);
        ArgumentNullException.ThrowIfNull(beforeUninstall);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(registry);
        afterInstall(version => Run(version, context, shell, registry, reported, static (hooks, c) => hooks.AfterInstall(c)));
        afterUpdate(version => Run(version, context, shell, registry, reported, static (hooks, c) => hooks.AfterUpdate(c)));
        beforeUninstall(version => Run(version, context, shell, registry, reported, static (hooks, c) => hooks.BeforeUninstall(c)));
    }

    /// <summary>The hook context of an app running from <c>&lt;root&gt;\current\</c>, for the version Velopack passed.</summary>
    internal static InstallHookContext ContextFor(string baseDirectory, SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new InstallHookContext(
            InstallLayout.RootOf(baseDirectory) ?? throw new InvalidOperationException("The app has no install root."),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable(InstallOptions.EnvironmentVariable),
            version.ToFullString());
    }

    /// <summary>
    /// nth's start-time self-heal (PLAN.md 8, item 2; 8a): only in a Velopack install, on a pool thread, never blocking the
    /// caller and never throwing. Returns the running heal (for tests), or null outside a Velopack install.
    /// </summary>
    internal static Task<InstallHookReport?>? StartSelfHeal(string baseDirectory, Func<string, bool> installedByVelopack, Func<IShellLinks> shell, Func<IUserRegistry> registry)
    {
        ArgumentNullException.ThrowIfNull(installedByVelopack);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(registry);
        if (!installedByVelopack(baseDirectory) || InstallLayout.RootOf(baseDirectory) is not { } root) return null;
        return Task.Run(() =>
        {
            try
            {
                var links = shell();
                try { return new InstallHooks(links, registry()).SelfHeal(root); }
                finally { (links as IDisposable)?.Dispose(); }
            }
            catch (Exception)
            {
                return null; // a heal that cannot run waits for the next start
            }
        });
    }

    private static void Run(SemanticVersion version, Func<SemanticVersion, InstallHookContext> context, Func<IShellLinks> shell, Func<IUserRegistry> registry,
        Action<InstallHookReport>? reported, Func<InstallHooks, InstallHookContext, InstallHookReport> hook)
    {
        try
        {
            var links = shell();
            try
            {
                var report = hook(new InstallHooks(links, registry()), context(version));
                reported?.Invoke(report);
            }
            finally
            {
                (links as IDisposable)?.Dispose();
            }
        }
        catch (Exception)
        {
            // See the type's summary: the hook never throws into Velopack.
        }
    }
}
