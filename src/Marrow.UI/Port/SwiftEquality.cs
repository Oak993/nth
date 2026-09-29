using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY. Swift <c>==</c> for a generic <c>Hashable</c> value a control compares its options with: a String is
/// equal by canonical equivalence (R-STR-2, <see cref="SwiftString.Equal"/>) and a floating-point NaN is not equal to
/// itself (R-VAL-4), where .NET's default equality is ordinal for strings and says NaN equals NaN.
/// </summary>
public static class SwiftEquality
{
    public static bool Equal<T>(T left, T right)
    {
        if (left is string a && right is string b) return SwiftString.Equal(a, b);
        if (left is double d && right is double e) return d == e;
        if (left is float f && right is float g) return f == g;
        return EqualityComparer<T>.Default.Equals(left, right);
    }
}
