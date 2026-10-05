// PORT-ONLY: SK-13 / DL-16, skills-ONDEMAND-PLAN.md 3.8 and skills-PLAN.md WU-18 r3. There is no Swift twin. Registers the skills part of Velopack's before-uninstall hook.
using Marrow.Services.Port.Skills.Uninstall;
using Velopack;

namespace Marrow.App.Port.Skills;

/// <summary>
/// Program.RunVelopackHook hands this the builder's <c>OnBeforeUninstallFastCallback</c>. The callback unlocks and deletes <c>addons\</c> and the skills store and views of the data
/// root (<see cref="SkillsUninstall"/>) before Velopack removes the app. It never throws into Velopack (the process is about to be exited and a thrown hook would only add a
/// crash dialog to an uninstall): a failure leaves the folders unlocked or as they were. Nothing here runs an installer or Velopack; the registration is a seam so a test can capture
/// the callback and call it.
/// </summary>
internal static class SkillsUninstallHook
{
    internal static void Register(Action<VelopackHook> register, Func<string> dataRoot, Action<string>? body = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(dataRoot);
        var run = body ?? (root => SkillsUninstall.BeforeUninstall(root));
        register(_ =>
        {
            try { run(dataRoot()); }
            catch (Exception) { /* see the type's summary */ }
        });
    }
}
