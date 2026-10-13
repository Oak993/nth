using System.Reflection;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.MCP;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10 landing check (ED-NEW-web-w10-9; E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11 "Always the system browser: MCP OAuth
// (MCPOAuth.cs:549), nth's own account sign-ins, update and store links"; the owner amendment keeps PR-MCP-139 for OAuth). The sign-in of an MCP server opens
// through the system seam (Workspace.Open, PR-MCP-139: "do not embed a web view"), which the link setting never touches: the Services assembly has no way to
// reach the app's chat links, and the setting being Visus or Ask changes nothing for an address handed to the system seam.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-9")]
public sealed class WebW10OAuthBoundaryTests
{
    [Fact]
    public void The_assembly_that_signs_an_MCP_server_in_cannot_reach_the_apps_chat_links()
    {
        var references = typeof(MCPOAuth).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToList();
        Assert.DoesNotContain(typeof(ChatLinks).Assembly.GetName().Name, references);
        Assert.DoesNotContain("Marrow.UI", references);
    }

    [Theory]
    [InlineData(ChatLinkTarget.Ask)]
    [InlineData(ChatLinkTarget.Visus)]
    [InlineData(ChatLinkTarget.DefaultBrowser)]
    public void Whatever_the_link_setting_says_the_system_seam_is_not_routed_to_Visus_or_the_question(ChatLinkTarget choice)
    {
        using var scope = AppSettingsScope.Create();
        using var sinks = new ChatLinkSinks();
        scope.OnMain(() =>
        {
            var web = new AppSettings().Web;
            web.ChatLinks = choice;
            using var attach = WebSettingsBinding.Attach(web);
            using var binding = WebSettingsBinding.Bind();
            // The seam as the app has it (nothing overrides it here): in a test host the real shell refuses, so the answer is false and nothing is opened.
            const string signIn = "https://auth.example.org/oauth/authorize?client_id=fake&response_type=code";
            Assert.False(Marrow.Services.Port.Workspace.Open.Current(SwiftURL.Create(signIn)!));
            Assert.Empty(sinks.Opened);
            Assert.Empty(sinks.Questions);
        });
    }
}
