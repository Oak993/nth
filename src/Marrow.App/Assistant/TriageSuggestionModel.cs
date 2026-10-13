// PORT-ONLY: DESIGN.md 5.4, 6, 13.2; ED-NEW-dextra-open-3.
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class TriageSuggestionModel(Func<bool, BrainPick> pick, Func<BrainModel, DelegateCall?> prepare) : ISuggestionModel
{
    public async Task<string?> Reword(string prompt, bool local, CancellationToken cancellationToken)
    {
        var call = await MainActor.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chosen = pick(local).Model;
            if (chosen is null || local && !chosen.RunsLocally) return null;
            var ready = prepare(chosen);
            if (ready is null || local && ready.Provider != Marrow.Core.Models.ProviderKind.Local) return null;
            return ready.Admit?.Invoke(Guid.Empty) == false ? null : ready;
        }).ConfigureAwait(false);
        if (call is null) return null;
        try { return await call.Ask(prompt, cancellationToken).ConfigureAwait(false); }
        finally { await MainActor.Run(() => call.Release?.Invoke()).ConfigureAwait(false); }
    }
}
