using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-15: the cost guard as the runner's only gate, for the tests that read the guard alone. In the app the reserve is the runner's
/// gate and asks the guard (ReserveGuard.Costs); this stands in for it without a reserve.
/// </summary>
internal sealed class CostGuardGate(CostGuard guard) : IReserveGate
{
    public void MissionBegan(MissionRecord mission, ReserveStart? start) => guard.MissionBegan(mission, start);

    public bool BeforeTurn(MissionRecord mission, Guid? thread, ProviderKind provider, string? model, string purpose) =>
        !guard.IsHeld(mission.Id) && guard.BeforeTurn(mission, thread, provider, model, purpose);

    public void TurnStarted(Guid mission, Guid thread, string? model) => guard.TurnStarted(mission, thread, model);

    public void TurnEnded(Guid thread) => guard.TurnEnded(thread);

    public void MissionStopped(Guid mission) => guard.MissionStopped(mission);

    public void MissionEnded(Guid mission) => guard.MissionEnded(mission);
}
