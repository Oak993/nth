// PORT-ONLY: SK-13 / ED-326 (6)(a). There is no Swift twin. The start-up composition of the skills and tools machinery.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Port.Skills;

/// <summary>
/// Program.Main calls <see cref="Bind"/> once, with the data root, right after the storage bindings: it opens THE host of that root (one pipeline per root, SK-9's generations bound to it) and
/// publishes it on <see cref="SkillsSeams.Host"/>. A host that cannot be opened (a state.json that does not parse, an unreadable catalogue) must not stop the app from starting: the failure is
/// logged, the seam stays unbound, and the skills pages and the providers' wiring see no host. Nothing here downloads, installs or runs a process; the production options bind none of those seams.
/// </summary>
internal static class SkillsStartup
{
    private static readonly Logger Log = new("skills", "startup");

    /// <returns>The host, or null when it could not be opened.</returns>
    internal static SkillsHost? Bind(string dataRoot, Func<SkillsHostOptions>? options = null, Action<SkillsHost>? publish = null)
    {
        try
        {
            var host = SkillsHost.Open(dataRoot, (options ?? (() => SkillsHostOptions.Production()))());
            (publish ?? SkillsSeams.Host.SetProcessDefault)(host);
            return host;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException or AppDomainUnloadedException))
        {
            Log.Error("skills: the host could not be opened, so the skills and tools features are off for this run: " + error.Message);
            return null;
        }
    }
}
