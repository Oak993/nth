// PORT-ONLY: SK-11f / DL-14 (skills-ONDEMAND-PLAN.md 5.1 S4, 5.5 R4). The seam between the model's request_tool call and the user's offer.
namespace Marrow.Services.Port.Skills.Providers;

/// <summary>
/// What request_tool needs from the app: whether the tool is offered at all, whether a name is in the catalogue, and the user's answer to
/// the offer. Showing the offer and downloading on the user's yes belong to the implementation (the offer flow, unit SK-12); this seam
/// never downloads anything itself. The wait can be cancelled by a stopped turn.
/// </summary>
public interface IToolRequestOffer
{
    /// <summary>True only while something from the catalogue is installed (ED-NEW-f-sk-11f 4): otherwise the tool is not offered to the model.</summary>
    bool Available { get; }

    /// <summary>True when the name is a catalogue item id or a program a catalogue item provides (the detector's own lookup).</summary>
    bool InCatalogue(string name);

    /// <summary>Shows the offer and waits for the user's answer.</summary>
    Task<ToolRequestOutcome> Ask(string name, string reason, CancellationToken cancellation);
}
