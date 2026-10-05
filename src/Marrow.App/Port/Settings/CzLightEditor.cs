// PORT-ONLY CZ-4 (customization-SPEC CZ-13.1): how the Customization page's rows read and edit the composer light. The page edits AppSettings.ComposerLight through `with` copies; every change reaches every composer at once
// (each ComposerLightSlot reads the property), and the property clamps, steps and writes it (the store write is coalesced, CZ-13.5). The two functions are the settings' own, so the page never holds a copy that could go stale.
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Port.Settings;

internal sealed class CzLightEditor(Func<ComposerLightSettings> get, Action<ComposerLightSettings> set, Action? flush = null)
{
    /// <summary>The settings as the app holds them now.</summary>
    internal ComposerLightSettings Settings => get();

    internal void Edit(Func<ComposerLightSettings, ComposerLightSettings> change) => set(change(get()));

    internal void EditBeam(Func<BorderBeamOptions, BorderBeamOptions> change) => Edit(s => s with { Beam = change(s.Beam) });

    internal void EditAura(Func<UiverseAuraOptions, UiverseAuraOptions> change) => Edit(s => s with { Aura = change(s.Aura) });

    /// <summary>A drag or a key has finished: the pending write goes to the store now (CZ-13.5, STATE.md's SA-1 note).</summary>
    internal void Commit() => flush?.Invoke();
}
