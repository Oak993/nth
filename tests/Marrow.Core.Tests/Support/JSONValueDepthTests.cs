using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Audit fix (svc): DroppyCode/Core/Support/JSONValue.swift:59-63 serializes through JSONSerialization, which has no 64-level writer limit,
// so a valid 100-level tree renders in full and never falls back to the four bytes "null".
public sealed class JSONValueDepthTests
{
    [Trait("Req", "PR-STORE-117")] // Core/Support/JSONValue.swift:59-65
    [Fact]
    public void A_valid_hundred_level_tree_renders_in_full()
    {
        JSONValue value = new JSONValue.Int(7);
        for (var i = 0; i < 100; i++) value = new JSONValue.Array(new ValueList<JSONValue>(new[] { value }));

        var expected = new string('[', 100) + "7" + new string(']', 100);

        Assert.Equal(expected, value.CompactString);
    }
}
