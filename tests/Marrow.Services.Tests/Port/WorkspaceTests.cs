using Marrow.Core.Port;
using Marrow.Services.Port;
using Xunit;

namespace Marrow.Services.Tests.Port;

// Source under test: DroppyCode/UI/Markdown/LinkParagraphView.swift (B3 52bc792) via PA-0020/ED-203. Workspace's
// generic web-address opener (MCPOAuth.swift:434) hands the URL's string straight to ShellExecuteW; ED-203
// refuses it first when the URL has no scheme, or a host with no scheme (the two-slash relative-host form).
public sealed class WorkspaceTests
{
    [Fact]
    [Trait("Decision", "ED-203")]
    public void A_scheme_less_url_is_refused_before_shellexecute()
    {
        var url = SwiftURL.Create("calc")!;
        Assert.False(Workspace.Open.Current(url));
    }

    [Fact]
    [Trait("Decision", "ED-203")]
    public void A_scheme_less_two_slash_host_url_is_refused_before_shellexecute()
    {
        var url = SwiftURL.Create("//host/share")!;
        Assert.False(Workspace.Open.Current(url));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // review A29 finding 5: was `Workspace.Open.Current(url)`, which called the real
                                  // ShellExecuteW with no override and opened the user's default browser on every
                                  // test run (R-TEST-10: no test launches a real shell action). The gate is now its
                                  // own seam (Workspace.ShouldAttemptShellExecute), asserted directly, with nothing
                                  // downstream of it invoked.
    public void An_absolute_url_with_a_scheme_passes_the_gate_before_shellexecute()
    {
        var url = SwiftURL.Create("https://example.com")!;
        Assert.True(Workspace.ShouldAttemptShellExecute(url));
    }
}
