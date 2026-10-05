// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the English only switch's state in a model row.
using Marrow.Core.Port;

namespace Marrow.App.Port.Settings;

/// <summary>The one value the row's English only switch shows. Observable, so the switch follows it when the row changes its variant (a recommendation arrived) and not only when it is clicked.</summary>
internal sealed class SpeechModelRowChoice : MainActorModel
{
    private bool englishOnly;

    public bool EnglishOnly
    {
        get => Read(englishOnly);
        set => Set(ref englishOnly, value);
    }
}
