// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 2, 12.4; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// The reading of text that came from the screen or from a model for instructions meant for an agent. The control that says "Send" and the
// step that says "Click Send to email it to the client" are ordinary words in a draft, and the guard marks them; so the patterns that
// match the name of an action (send, run, pay) are left out here. What stays are the claims and overrides: "ignore your rules", "the
// owner already approved this", "from now on you are", "create a duty", "do not tell the owner".
namespace Marrow.Core.Port.Assistant;

public static class TeachScan
{
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal)
    {
        "send-to", "send-this", "run-this", "money", "hindi-send", "urdu-send", "roman-send",
    };

    /// <summary>The first instruction in the text that is not just the name of an action, or null.</summary>
    public static InstructionFinding? Find(string? text) => InstructionScanner.Find(text, Ordinary);
}
