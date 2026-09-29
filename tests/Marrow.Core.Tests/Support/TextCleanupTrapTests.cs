using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Audit fix (svc): Core/Support/Text.swift:17-30. A negative suffix(count) and prefix(limit - 1) with limit 0 are Swift preconditions:
// the process traps and nothing can catch it, so the port raises SwiftTrapException (ED-078), never a catchable argument exception.
public sealed class TextCleanupTrapTests
{
    [Trait("Req", "PR-STORE-108")] // Core/Support/Text.swift:17-25
    [Fact]
    public void A_negative_last_lines_count_traps()
    {
        Assert.Throws<SwiftTrapException>(() => TextCleanup.LastLines("error", -1));
    }

    [Trait("Req", "PR-STORE-109")] // Core/Support/Text.swift:27-30
    [Fact]
    public void A_zero_summary_limit_over_a_non_empty_line_traps()
    {
        Assert.Throws<SwiftTrapException>(() => TextCleanup.SingleLine("error", 0));
    }
}
