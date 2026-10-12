using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// DESIGN 8.3 external protocols.
[Trait("Decision", "ED-NEW-web-w8a-1")]
public sealed class ExternalProtocolTests
{
    [Theory]
    [InlineData("ms-msdt")]
    [InlineData("search-ms")]
    [InlineData("ms-officecmd")]
    [InlineData("ms-appinstaller")]
    [InlineData("javascript")]
    [InlineData("vbscript")]
    [InlineData("file")]
    public void The_design_block_list_is_blocked(string scheme) => Assert.True(ExternalProtocols.IsBlocked(scheme));

    [Theory]
    [InlineData("ms-word")]
    [InlineData("ms-excel")]
    [InlineData("ms-cxh")]
    [InlineData("hcp")]
    [InlineData("ms-its")]
    [InlineData("mhtml")]
    [InlineData("shell")]
    [InlineData("ms-settings")]
    [InlineData("microsoft-edge")]
    [InlineData("edge")]
    [InlineData("chrome")]
    [InlineData("MS-MSDT")]
    public void Other_code_running_and_internal_schemes_are_blocked_too(string scheme) => Assert.True(ExternalProtocols.IsBlocked(scheme));

    [Theory]
    [InlineData("mailto")]
    [InlineData("tel")]
    [InlineData("zoommtg")]
    [InlineData("vscode")]
    [InlineData("slack")]
    public void App_schemes_are_not_blocked(string scheme) => Assert.False(ExternalProtocols.IsBlocked(scheme));

    [Theory]
    [InlineData(WebProfileKind.Browse, "mailto:dana@example.com", true, ExternalLaunchDecision.AskRememberable)]
    [InlineData(WebProfileKind.Preview, "tel:+15550100", true, ExternalLaunchDecision.AskRememberable)]
    [InlineData(WebProfileKind.Browse, "zoommtg://zoom.us/join", true, ExternalLaunchDecision.Ask)]
    [InlineData(WebProfileKind.Browse, "mailto:dana@example.com", false, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Private, "mailto:dana@example.com", true, ExternalLaunchDecision.Ask)]
    [InlineData(WebProfileKind.Private, "zoommtg://zoom.us/join", false, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Agent, "mailto:dana@example.com", true, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Artifacts, "mailto:dana@example.com", true, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Browse, "ms-msdt:/id PCWDiagnostic", true, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Browse, "search-ms:query=calc", true, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Browse, "ms-officecmd:x", true, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Browse, "https://example.com/", true, ExternalLaunchDecision.Deny)]
    [InlineData(WebProfileKind.Browse, "not-a-uri", true, ExternalLaunchDecision.Deny)]
    public void A_launch_is_denied_asked_or_asked_with_remember(WebProfileKind profile, string uri, bool gesture, ExternalLaunchDecision decision) =>
        Assert.Equal(decision, ExternalProtocols.Decide(profile, uri, gesture));

    [Fact]
    public void The_app_name_is_the_systems_then_nths_then_another_app()
    {
        Assert.Equal("Outlook", ExternalProtocols.AppName("mailto", _ => "Outlook"));
        Assert.Equal("Mail", ExternalProtocols.AppName("mailto"));
        Assert.Equal("Mail", ExternalProtocols.AppName("mailto", _ => null));
        Assert.Equal("Zoom", ExternalProtocols.AppName("zoommtg"));
        Assert.Equal("another app", ExternalProtocols.AppName("acme-sync"));
    }

    [Fact]
    public void The_prompt_title_names_the_site_the_app_and_the_scheme() =>
        Assert.Equal("example.com wants to open Mail (mailto:)", ExternalProtocols.PromptTitle("example.com", "Mail", "mailto"));
}
