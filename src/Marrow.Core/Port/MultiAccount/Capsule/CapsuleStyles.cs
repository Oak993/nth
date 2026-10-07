// PORT-ONLY: multi-account step 3a (OD-0115 composer=A, OD-0119). The twelve "Pill in the composer" styles (multi-account.html CPT, IDS, WIDE), the words of Settings > Customization.
namespace Marrow.Core.Port.MultiAccount.Capsule;

public static class CapsuleStyles
{
    public const string Default = "1";
    public const string UsageLeftLabel = "Usage left";

    private static readonly (string Id, string Name, string Description, int Width)[] All =
    [
        ("1", "Merged capsule", "The account capsule becomes the pill: a little wider, the current mark inside, a row of tiny step lights along its top edge.", 48),
        ("2", "Name below", "The capsule stays as it is. The step lights sit just above it and the pill’s name in tiny text under it.", 34),
        ("3", "Lights only", "Only a hairline of step lights over the capsule. The name appears on hover or focus.", 34),
        ("4", "Segmented rim", "The capsule’s own rim is split into one segment per step: lit is the current step, faint are used, dim are next.", 36),
        ("5", "Orbit", "Tiny step dots drift slowly around inside the rim; the current one glows.", 44),
        ("6", "Liquid fill", "The glass fills like a gauge with the current account’s use; step ticks are etched along the rim.", 44),
        ("7", "Stacked marks", "The next step’s mark peeks out behind the current one, like a stack of cards.", 46),
        ("8", "Aurora rim", "A faint aurora runs slowly around the rim.", 40),
        ("9", "Dial", "A round capsule: a hairline arc around the mark, one segment per step.", 22),
        ("10", "Minimal", "Just the mark. A single dot breathes beside it only while a Handshake runs.", 34),
        ("11", "Glow rim", "A glowing rim around dark glass. It blooms on hover, on press and during a Handshake.", 44),
        ("11o", "Glow rim · original colours", "The owner’s blue-to-pink gradient, as pasted.", 44),
    ];

    public static IReadOnlyList<string> Ids { get; } = All.Select(s => s.Id).ToArray();

    /// <summary>The cards of the Customization section: the glow card holds both glow variants.</summary>
    public static IReadOnlyList<string> Cards { get; } = All.Select(s => s.Id).Where(id => id != "11o").ToArray();

    public static IReadOnlyList<(string Id, string Label)> GlowVariants { get; } = [("11", "Usage light"), ("11o", "Original colours")];

    public static IReadOnlyList<(string Key, string Label)> UsageLegend { get; } = [("uP", "Plenty"), ("uH", "Half"), ("uL", "Low"), ("uR", "Almost out")];

    public static bool IsKnown(string? id) => id is not null && Ids.Contains(id);

    public static bool IsGlow(string id) => id is "11" or "11o";

    public static string Name(string id) => Find(id).Name;

    public static string Description(string id) => Find(id).Description;

    public static int Width(string id) => Find(id).Width;

    /// <summary>The card's one line: the description up to its first sentence (the page: split on ". ", closing with a full stop).</summary>
    public static string CardLine(string id)
    {
        var first = Description(id).Split(". ")[0];
        if (first.EndsWith('.')) first = first[..^1];
        return first + ".";
    }

    private static (string Id, string Name, string Description, int Width) Find(string id) =>
        All.FirstOrDefault(s => s.Id == id) is { Id: not null } found ? found : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown capsule style.");
}
