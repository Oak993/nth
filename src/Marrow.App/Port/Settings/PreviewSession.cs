// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.3, CZ-4.6): what the Customization page's preview holds for as long as the page lives. The preview's maker and its
// state are session state, never persisted (the design stored the maker in localStorage for review convenience only).
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewSession
{
    /// <summary>The maker of <paramref name="defaultProvider"/>'s brand, at Rest (CZ-4.5.3, CZ-3.2).</summary>
    internal PreviewSession(ProviderKind defaultProvider) => Maker = PreviewMakers.Initial(defaultProvider);

    /// <summary>The preview model switch's pick: a key of <see cref="PreviewMakers.All"/>.</summary>
    internal string Maker { get; private set; }

    /// <summary>The chrome row's pick.</summary>
    internal PreviewState State { get; private set; } = PreviewState.Rest;

    /// <summary>CZ-4.6: what the preview's light and controls are given.</summary>
    internal ComposerLightPhase Phase => State switch
    {
        PreviewState.Typing => ComposerLightPhase.Typing,
        PreviewState.Running => ComposerLightPhase.Running,
        _ => ComposerLightPhase.Rest,
    };

    /// <summary>Raised once for each change of the maker or the state.</summary>
    internal event Action? Changed;

    internal void Pick(string maker)
    {
        PreviewMakers.IndexOf(maker); // an unknown maker is a programming error
        if (maker == Maker) return;
        Maker = maker;
        Changed?.Invoke();
    }

    internal void Pick(PreviewState state)
    {
        if (state == State) return;
        State = state;
        Changed?.Invoke();
    }

    /// <summary>The next (+1) or previous (-1) maker, wrapping around (CZ-4.5.1).</summary>
    internal void Step(int delta) => Pick(PreviewMakers.Step(Maker, delta));

    /// <summary>CZ-4.6: touching any Working dots control switches the pick to Running a turn.</summary>
    internal void WorkingDotsTouched() => Pick(PreviewState.Running);
}
