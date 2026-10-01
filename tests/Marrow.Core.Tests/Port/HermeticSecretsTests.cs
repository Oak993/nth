using System.Collections;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-TEST-10: no provider secret is visible to a test, however the test process was started (HermeticSecrets).
public sealed class HermeticSecretsTests
{
    [Fact]
    [Trait("Decision", "ED-211")] // the fail-closed test guards around providers
    public void No_secret_shaped_variable_is_visible_inside_a_test()
    {
        var visible = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(HermeticSecrets.IsSecret).ToList();
        Assert.Empty(visible);
    }

    [Fact]
    [Trait("Decision", "ED-211")]
    public void Clearing_removes_api_keys_and_tokens_and_keeps_other_variables()
    {
        Environment.SetEnvironmentVariable("MARROW_GUARD_PROBE_API_KEY", "x");
        Environment.SetEnvironmentVariable("MARROW_GUARD_PROBE_AUTH_TOKEN", "x");
        Environment.SetEnvironmentVariable("MARROW_GUARD_PROBE_PATHLIKE", "kept");
        try
        {
            HermeticSecrets.Clear();
            Assert.Null(Environment.GetEnvironmentVariable("MARROW_GUARD_PROBE_API_KEY"));
            Assert.Null(Environment.GetEnvironmentVariable("MARROW_GUARD_PROBE_AUTH_TOKEN"));
            Assert.Equal("kept", Environment.GetEnvironmentVariable("MARROW_GUARD_PROBE_PATHLIKE"));
            Assert.Contains("MARROW_GUARD_PROBE_API_KEY", HermeticSecrets.Removed);
        }
        finally { Environment.SetEnvironmentVariable("MARROW_GUARD_PROBE_PATHLIKE", null); }
    }
}
