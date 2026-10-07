using Marrow.Core.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class PillTargetSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_actual_chooser_target_carries_the_pills_effective_common_point_in_both_directions(bool earlier)
    {
        var store = new InMemoryAccountStore([new("a", "claude", "A"), new("b", "codex", "B")]);
        var limits = new InMemoryAccountLimits();
        var clock = new ManualTimeProvider();
        var pill = new Pill("Work", [new("a", HandshakeAt: HandshakePoint.At(90)), new("b", HandshakeAt: HandshakePoint.At(91))],
            HandshakeMode: PillHandshakeMode.SameForEvery, SameHandshake: HandshakePoint.At(98));
        var chooser = new NextAccountChooser(store, limits, clock);
        var target = earlier ? chooser.EarlierBack("b", pill, 1)! : Assert.IsType<ChooserResult.Move>(chooser.Next("a", pill, 0)).Target;
        Assert.NotNull(target);
        Assert.Equal(pill.Effective(earlier ? 0 : 1), target.Step);
        Assert.Equal(98, target.Step!.HandshakeAt!.Value.Percent);
    }
}
