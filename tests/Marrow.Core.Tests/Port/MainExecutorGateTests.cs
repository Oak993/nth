using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

public sealed class MainExecutorGateTests
{
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void A_completion_stays_held_until_release_and_keeps_its_ambient_bindings()
    {
        using var runtime = new TestRuntime();
        using var gate = new MainExecutorGate(runtime.Loop);
        var binding = new Ambient<string>("outside");
        string? seen = null;
        using (binding.Override("inside")) gate.Enqueue(() => seen = binding.Current);
        runtime.RunUntilIdle();
        Assert.Null(seen);
        gate.Release();
        runtime.RunUntilIdle();
        Assert.Equal("inside", seen);
        Assert.Equal("outside", binding.Current);
    }

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void Release_preserves_order_and_future_deliveries_and_does_not_redefine_main_access()
    {
        using var runtime = new TestRuntime();
        using var gate = new MainExecutorGate(runtime.Loop);
        var seen = new List<int>();
        Assert.False(gate.CheckAccess());
        runtime.OnMain(() =>
        {
            Assert.True(gate.CheckAccess());
            gate.Enqueue(() => seen.Add(0));
        });
        Assert.Equal([0], seen);
        gate.Enqueue(() => seen.Add(1));
        gate.Enqueue(() => seen.Add(2));
        gate.Release();
        gate.Release();
        gate.Enqueue(() => seen.Add(3));
        runtime.RunUntilIdle();
        Assert.Equal([0, 1, 2, 3], seen);
    }
}
