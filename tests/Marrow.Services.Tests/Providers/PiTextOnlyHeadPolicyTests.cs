using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// Official pi-mono CLI/extension contracts, pinned and cited in ED-460.
[Trait("Rule", "R-TEST-4")]
public sealed class PiTextOnlyHeadPolicyTests
{
    [Fact]
    public void A_head_excludes_every_other_extension_tool_skill_and_context_loader_even_for_full_access()
    {
        var arguments = PiTextOnlyHeadPolicy.Arguments("private-guard.mjs", "openai/model", "high");
        Assert.Equal(new[] { "--mode", "rpc", "--no-session", "--no-tools", "--no-extensions", "--no-skills", "--no-prompt-templates", "--no-themes", "--no-context-files", "-e", "private-guard.mjs", "--provider", "openai", "--model", "model", "--thinking", "high" }, arguments);
        Assert.DoesNotContain("--approve", arguments);
        Assert.DoesNotContain("--session", arguments);
    }

    [Theory]
    [InlineData("[]", "nonce", true)]
    [InlineData("[\"write\"]", "nonce", false)]
    [InlineData("[]", "old-nonce", false)]
    [InlineData("null", "nonce", false)]
    public void Only_a_fresh_runtime_proof_of_an_empty_tool_set_authorizes_the_head(string tools, string nonce, bool expected)
    {
        var reply = JSONValue.Parse("{\"messages\":[{\"role\":\"custom\",\"customType\":\"marrow-text-only-proof\",\"content\":\"" + nonce + "\",\"details\":{\"tools\":" + tools + "}}]}")!;
        Assert.Equal(expected, PiTextOnlyHeadPolicy.Verify(reply, "nonce"));
        Assert.False(PiTextOnlyHeadPolicy.Verify(JSONValue.Parse("{}")!, "nonce"));
    }
}
