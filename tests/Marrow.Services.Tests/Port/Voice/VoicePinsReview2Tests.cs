using System.Reflection;

namespace Marrow.Services.Tests.Port.Voice;

// Review round 2 of VT-0. Shared-PC runs and the merge gate add "&FullyQualifiedName!~Native" (a case-insensitive contains) to keep
// real-window tests off this PC; a VT-0 test whose name holds "native" or "natives" is silently dropped from every such run, so the
// natives pin, the trim target's only guard and the VC++ import check never run there (33 tests exist, 29 run under the filter).
public sealed class VoicePinsReview2Tests
{
    [Fact]
    [Trait("Feature", "VT-0")]
    [Trait("Decision", "ED-267")]
    public void REVIEW2_every_VT0_test_runs_under_the_shared_PC_filter()
    {
        var dropped = typeof(VoicePinsTests).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any())
            .Select(m => m.Name)
            .Where(n => n.Contains("native", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(dropped);
    }
}
