// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.5 "Asturs on Armilla", 9.1; unit AS-16; ED-NEW-dextra-as16-1). No Swift twin.
// The six Astur node shapes and the tint each has (2.5): the owner picks the shape, two Asturs never share one, so two never look
// alike. The ids are the ones the create_assistant tool names ("rounded_triangle"); the tints are the canvas's, for the dark theme and
// the light one. Pure.
namespace Marrow.Core.Port.Assistant;

public static class AsturShapes
{
    /// <summary>How many Asturs there can be at once: one for each shape (2.5: 0 to 6 nodes on the orbit).</summary>
    public const int MostAsturs = 6;

    /// <summary>The shapes in the order the picker shows them.</summary>
    public static IReadOnlyList<AsturShape> All { get; } = Enum.GetValues<AsturShape>();

    /// <summary>The shape's id in tool arguments and in the stored profile.</summary>
    public static string Id(AsturShape shape) => shape switch
    {
        AsturShape.Squircle => "squircle",
        AsturShape.Hexagon => "hexagon",
        AsturShape.Diamond => "diamond",
        AsturShape.RoundedTriangle => "rounded_triangle",
        AsturShape.Clover => "clover",
        AsturShape.Star => "star",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>The name the picker and a screen reader give the shape.</summary>
    public static string Title(AsturShape shape) => shape switch
    {
        AsturShape.Squircle => "Squircle",
        AsturShape.Hexagon => "Hexagon",
        AsturShape.Diamond => "Diamond",
        AsturShape.RoundedTriangle => "Rounded triangle",
        AsturShape.Clover => "Clover",
        AsturShape.Star => "Star",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>The shape an id names ("hex" and "triangle" too, as the canvas calls them); null for anything else.</summary>
    public static AsturShape? Parse(string? id) => id?.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_') switch
    {
        "squircle" => AsturShape.Squircle,
        "hexagon" or "hex" => AsturShape.Hexagon,
        "diamond" => AsturShape.Diamond,
        "rounded_triangle" or "triangle" => AsturShape.RoundedTriangle,
        "clover" => AsturShape.Clover,
        "star" => AsturShape.Star,
        _ => null,
    };

    /// <summary>The shape's tint on the dark theme and on the light one, as #RRGGBB (2.5).</summary>
    public static (string Dark, string Light) Tint(AsturShape shape) => shape switch
    {
        AsturShape.Squircle => ("#8FD8B8", "#2E8B66"),
        AsturShape.Hexagon => ("#F2A99E", "#C0574A"),
        AsturShape.Diamond => ("#A9C1FA", "#4A6FD0"),
        AsturShape.RoundedTriangle => ("#EBCB82", "#9C7414"),
        AsturShape.Clover => ("#C9B0F5", "#7E5BC4"),
        AsturShape.Star => ("#86D3E3", "#2A8FA6"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>The first shape (in the picker's order) that none of <paramref name="taken"/> has; null when all six are taken.</summary>
    public static AsturShape? FirstFree(IEnumerable<AsturShape> taken)
    {
        var used = taken.ToHashSet();
        foreach (var shape in All) if (!used.Contains(shape)) return shape;
        return null;
    }
}
