using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.App.Port;

namespace Marrow.App.Tests.Port;

// No COM calls: exercise the production STA queue with a controlled work delegate.
public sealed class C4cShellReadRetryTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Disposing_after_an_unanswered_call_does_not_spend_another_full_call_timeout()
    {
        var shell = new ShellLinks(new Dictionary<Marrow.Core.Support.ShellKnownFolder, string>());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var method = typeof(ShellLinks).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "Invoke" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(int));
        try
        {
            Assert.Throws<TargetInvocationException>(() => method.Invoke(shell,
                [new Func<int>(() => { entered.Set(); release.Wait(); return 1; }), TimeSpan.FromMilliseconds(5)]));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            shell.Dispose();
            Assert.True(elapsed.Elapsed < ShellLinks.CallTimeout, elapsed.Elapsed.ToString());
        }
        finally { release.Set(); shell.Dispose(); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_short_read_timeout_is_unknown_and_the_next_read_can_succeed() => Check(5);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_full_read_timeout_is_unknown_and_the_next_read_can_succeed() => Check(5000);

    private static void Check(int milliseconds)
    {
        using var shell = new ShellLinks(new Dictionary<Marrow.Core.Support.ShellKnownFolder, string>());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var method = typeof(ShellLinks).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "Invoke" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(int));
        try
        {
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(shell,
                [new Func<int>(() => { entered.Set(); release.Wait(); return 1; }), TimeSpan.FromMilliseconds(milliseconds)]));
            Assert.Equal(unchecked((int)0x800705B4), Assert.IsType<COMException>(error.InnerException).HResult);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        }
        finally { release.Set(); }
        Assert.Equal(42, method.Invoke(shell, [new Func<int>(() => 42), ShellLinks.CallTimeout]));
    }
}
