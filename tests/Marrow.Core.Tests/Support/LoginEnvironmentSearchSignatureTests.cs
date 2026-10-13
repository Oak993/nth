using Marrow.Core.Support;
using Xunit;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (ED-NEW-perf-ui-thread-2): the string a caller keeps beside a remembered Which answer. It names the PATH, the PATHEXT and the home folder Which(name) searches with,
// so a remembered answer is looked up again exactly when the search it came from is not the search now.
public sealed class LoginEnvironmentSearchSignatureTests
{
    [Fact]
    [Trait("Decision", "ED-171")]
    public void The_signature_is_the_same_for_the_same_search_and_names_the_path_and_the_home()
    {
        _ = LoginEnvironment.Current; // the process's one Loader is made here, from the real home, not under the test home below (it would stay that way for every other test)
        using var path = LoginEnvironment.PathForTesting.Override(@"C:\one;C:\two");
        using var home = LoginEnvironment.HomeForTesting.Override(@"C:\home-a");
        var signature = LoginEnvironment.SearchSignature();

        Assert.Equal(signature, LoginEnvironment.SearchSignature());
        Assert.Contains(@"C:\one;C:\two", signature, StringComparison.Ordinal);
        Assert.Contains(@"C:\home-a", signature, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-171")]
    public void The_signature_differs_when_the_path_or_the_home_differs()
    {
        _ = LoginEnvironment.Current; // see above: the shared Loader is made before any test home is set
        using var path = LoginEnvironment.PathForTesting.Override(@"C:\one;C:\two");
        using var home = LoginEnvironment.HomeForTesting.Override(@"C:\home-a");
        var signature = LoginEnvironment.SearchSignature();

        using (LoginEnvironment.PathForTesting.Override(@"C:\one")) Assert.NotEqual(signature, LoginEnvironment.SearchSignature());
        using (LoginEnvironment.PathForTesting.Override(@"C:\two;C:\one")) Assert.NotEqual(signature, LoginEnvironment.SearchSignature());
        using (LoginEnvironment.HomeForTesting.Override(@"C:\home-b")) Assert.NotEqual(signature, LoginEnvironment.SearchSignature());
        Assert.Equal(signature, LoginEnvironment.SearchSignature());
    }
}
