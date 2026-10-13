using System.Text.Json.Nodes;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W10, Settings > Tabula > "Inline HTML widgets" and "Live answers in chat" (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; ED-NEW-web-w10-3). With inline HTML
// widgets off no model is offered html_widget, a call of it is refused with the reason, and the other two live-answer tools are untouched; with live
// answers off none of the three is offered.
[Collection(WebStaticSeamsCollection.Name)]
[Trait("Decision", "ED-NEW-web-w10-3")]
public sealed class LiveAnswerHtmlSwitchTests
{
    private static readonly Guid Chat = Guid.Parse("5c1e7a30-9d52-4e4b-8f60-3a1c0b9d7e24");

    [Fact]
    public void The_widget_tool_is_left_out_for_every_model_while_the_switch_is_off()
    {
        Assert.Equal(["ui_show", "ui_update", "html_widget"], LiveAnswerTools.Offered(lite: false, htmlWidgets: true));
        Assert.Equal(["ui_show", "ui_update"], LiveAnswerTools.Offered(lite: false, htmlWidgets: false));
        Assert.Equal(["ui_show", "ui_update"], LiveAnswerTools.Offered(lite: true, htmlWidgets: true));
        Assert.Equal(["ui_show", "ui_update"], LiveAnswerTools.Offered(lite: true, htmlWidgets: false));
    }

    [Fact]
    public async Task A_call_of_the_widget_tool_is_refused_with_the_reason_and_nothing_is_recorded()
    {
        var host = new LiveAnswerHost();
        host.BindHtmlEnabled(() => false);
        var result = await LiveAnswerTools.Run(host, Chat, lite: false, "html_widget", JsonNode.Parse("{\"html\":\"<p>hi</p>\"}")!);
        Assert.True(result.Failed);
        Assert.Equal("Error: Inline HTML widgets are turned off in Settings. Answer in text, a live view or an artifact instead.", result.Text);
        Assert.Empty(host.Ids(Chat));

        host.BindHtmlEnabled(() => true);
        var allowed = await LiveAnswerTools.Run(host, Chat, lite: false, "html_widget", JsonNode.Parse("{\"html\":\"<p>hi</p>\"}")!);
        Assert.False(allowed.Failed, allowed.Text);
        Assert.Single(host.Ids(Chat));
    }

    [Fact]
    public async Task The_view_tools_work_whatever_the_widget_switch_says()
    {
        var host = new LiveAnswerHost();
        host.BindHtmlEnabled(() => false);
        var arguments = new JsonObject { ["ui"] = "card \"A\"\n  text \"b\"" };
        var shown = await LiveAnswerTools.Run(host, Chat, lite: false, "ui_show", arguments);
        Assert.False(shown.Failed, shown.Text);
    }

    [Fact]
    public void The_server_lists_and_teaches_the_widget_tool_only_while_both_switches_are_on()
    {
        using var scope = new ArtifactHostScope();
        Assert.Contains("html_widget", UiMcpServer.Offered);
        scope.Host.Live.BindHtmlEnabled(() => false);
        Assert.DoesNotContain("html_widget", UiMcpServer.Offered);
        Assert.Contains("ui_show", UiMcpServer.Offered);
        Assert.DoesNotContain("html_widget", UiMcpServer.Instructions, StringComparison.Ordinal);
        scope.Host.Live.BindHtmlEnabled(() => true);
        scope.Host.Live.BindEnabled(() => false);
        Assert.DoesNotContain("ui_show", UiMcpServer.Offered);
        Assert.DoesNotContain("html_widget", UiMcpServer.Offered);
        Assert.Contains("artifact_create", UiMcpServer.Offered); // artifacts do not depend on live answers
    }
}
