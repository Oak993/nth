// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 5, 12.6, 15.1; unit AS-19; ED-NEW-dextra-as19-11). No Swift twin.
// Two small forwarding handles for things that exist before teach mode can be made. The stop scope: StartDextra takes its stop scopes when it
// builds the controller, and the teach controller needs that controller's decision queue, so the scope is handed in first and bound after.
// The skills backend: the own-skill service is the skills host's, opened at start-up; teach mode asks the host each time it saves and says
// so plainly when there is none.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Assistant;

/// <summary>Stop all reaches a teach recording through this scope, whichever controller it is bound to (none: nothing to stop).</summary>
public sealed class TeachStopScope : IStopScope
{
    private volatile IStopScope? target;

    public string Name => "teach";

    public void Bind(IStopScope? scope) => target = scope;

    public void Stop() => target?.Stop();
}

/// <summary>The own-skill service of the skills host the app opened, asked afresh each time.</summary>
public sealed class TeachSkillsBackend : ICustomAddBackend
{
    public Task<IReadOnlyList<CustomAddCandidate>> Check(CustomAddRequest request, CancellationToken cancel) => Host().Check(request, cancel);

    public Task<OwnItem> Add(CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel) => Host().Add(candidate, progress, cancel);

    public Task<OwnItem> Replace(string replacingId, CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel) => Host().Replace(replacingId, candidate, progress, cancel);

    /// <summary>The own skills now (settling a save that was cut short reads them).</summary>
    public static IReadOnlyList<OwnItem> Skills()
    {
        try { return SkillsSeams.Host.Current.CustomSources.List(CustomAddKind.Skills); }
        catch (SwiftTrapException) { return []; }
    }

    private static CustomSources Host()
    {
        try { return SkillsSeams.Host.Current.CustomSources; }
        catch (SwiftTrapException) { throw new InvalidOperationException("The skills store is not open yet."); }
    }
}
