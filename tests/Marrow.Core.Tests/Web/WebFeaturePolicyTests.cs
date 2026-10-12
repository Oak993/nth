using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// DESIGN 8.4, every row, for browse, preview and agent tabs (and the artifacts column): "Browser features, each decided".
// ED-NEW-web-w8b-3: the table as data, and the DevTools Protocol calls a model step may never make (5.4 "The model cannot
// accept", 5.5, 8.4 "never synthesized by the model", "account sign-in is a hard deny").
[Trait("Decision", "ED-NEW-web-w8b-3")]
[Trait("Feature", "W-8b")]
public sealed class WebFeaturePolicyTests
{
    [Theory]
    [InlineData(WebFeature.Fullscreen, WebFeatureRule.Contained, WebFeatureRule.Contained, WebFeatureRule.Denied, WebFeatureRule.Denied)]
    [InlineData(WebFeature.PointerLock, WebFeatureRule.Asks, WebFeatureRule.Asks, WebFeatureRule.Denied, WebFeatureRule.Denied)]
    [InlineData(WebFeature.HttpAuth, WebFeatureRule.NthCard, WebFeatureRule.NthCard, WebFeatureRule.Denied, WebFeatureRule.Denied)]
    [InlineData(WebFeature.FileInput, WebFeatureRule.System, WebFeatureRule.System, WebFeatureRule.GuardedCard, WebFeatureRule.NthPicker)]
    [InlineData(WebFeature.FileDrop, WebFeatureRule.UserGesture, WebFeatureRule.UserGesture, WebFeatureRule.UserGesture, WebFeatureRule.NthPicker)]
    [InlineData(WebFeature.Passkeys, WebFeatureRule.System, WebFeatureRule.Denied, WebFeatureRule.Denied, WebFeatureRule.Denied)]
    [InlineData(WebFeature.ClipboardWrite, WebFeatureRule.UserGesture, WebFeatureRule.UserGesture, WebFeatureRule.NthCopyOnly, WebFeatureRule.Denied)]
    [InlineData(WebFeature.ClipboardRead, WebFeatureRule.Asks, WebFeatureRule.Asks, WebFeatureRule.Denied, WebFeatureRule.Denied)]
    [InlineData(WebFeature.Autofill, WebFeatureRule.System, WebFeatureRule.Denied, WebFeatureRule.Denied, WebFeatureRule.Denied)]
    [InlineData(WebFeature.ScriptDialogs, WebFeatureRule.NthCard, WebFeatureRule.NthCard, WebFeatureRule.AutoDismissed, WebFeatureRule.Denied)]
    public void Each_row_of_8_4_is_decided(WebFeature feature, WebFeatureRule browse, WebFeatureRule preview, WebFeatureRule agent, WebFeatureRule artifacts)
    {
        Assert.Equal(browse, WebFeaturePolicy.Rule(WebProfileKind.Browse, feature));
        Assert.Equal(preview, WebFeaturePolicy.Rule(WebProfileKind.Preview, feature));
        Assert.Equal(agent, WebFeaturePolicy.Rule(WebProfileKind.Agent, feature));
        Assert.Equal(artifacts, WebFeaturePolicy.Rule(WebProfileKind.Artifacts, feature));
    }

    [Fact]
    public void Every_feature_has_a_row()
    {
        Assert.Equal(10, Enum.GetValues<WebFeature>().Length);
        foreach (var feature in Enum.GetValues<WebFeature>())
            Assert.True(Enum.IsDefined(WebFeaturePolicy.Rule(WebProfileKind.Browse, feature)));
    }

    [Fact]
    public void A_private_tab_is_a_browse_tab_that_keeps_nothing()
    {
        Assert.Equal(WebFeatureRule.Contained, WebFeaturePolicy.Rule(WebProfileKind.Private, WebFeature.Fullscreen));
        Assert.Equal(WebFeatureRule.NthCard, WebFeaturePolicy.Rule(WebProfileKind.Private, WebFeature.HttpAuth));
        Assert.Equal(WebFeatureRule.System, WebFeaturePolicy.Rule(WebProfileKind.Private, WebFeature.Passkeys));
        Assert.Equal(WebFeatureRule.Denied, WebFeaturePolicy.Rule(WebProfileKind.Private, WebFeature.Autofill)); // it would keep what was typed
    }

    [Theory]
    [InlineData("Browser.setDownloadBehavior")]
    [InlineData("Page.setDownloadBehavior")]
    [InlineData("Browser.cancelDownload")]
    [InlineData("Browser.grantPermissions")]
    [InlineData("Browser.setPermission")]
    [InlineData("Browser.resetPermissions")]
    [InlineData("Page.setInterceptFileChooserDialog")]
    [InlineData("Page.handleFileChooser")]
    [InlineData("DOM.setFileInputFiles")]
    [InlineData("Input.setInterceptDrags")]
    [InlineData("WebAuthn.enable")]
    [InlineData("WebAuthn.addVirtualAuthenticator")]
    [InlineData("WebAuthn.addCredential")]
    [InlineData("Page.addScriptToEvaluateOnNewDocument")]
    [InlineData("Runtime.addBinding")]
    [InlineData("")]
    public void A_model_step_cannot_take_what_is_the_users(string method) => Assert.False(WebFeaturePolicy.ModelMayCall(method, "{}"));

    [Theory]
    [InlineData("Input.dispatchMouseEvent", "{\"type\":\"mousePressed\",\"x\":1,\"y\":2}")]
    [InlineData("Input.insertText", "{\"text\":\"hi\"}")]
    [InlineData("DOM.getDocument", "{}")]
    [InlineData("Page.captureScreenshot", null)]
    [InlineData("Input.dispatchDragEvent", "{\"type\":\"dragEnter\",\"x\":1,\"y\":1,\"data\":{\"items\":[{\"mimeType\":\"text/plain\",\"data\":\"x\"}],\"dragOperationsMask\":1}}")]
    [InlineData("Input.dispatchDragEvent", "{\"type\":\"drop\",\"x\":1,\"y\":1,\"data\":{\"items\":[],\"files\":[],\"dragOperationsMask\":1}}")]
    public void Ordinary_steps_pass(string method, string? parameters) => Assert.True(WebFeaturePolicy.ModelMayCall(method, parameters));

    [Theory]
    [InlineData("{\"type\":\"drop\",\"x\":1,\"y\":1,\"data\":{\"items\":[],\"files\":[\"C:\\\\Users\\\\d\\\\secret.pdf\"],\"dragOperationsMask\":1}}")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    public void A_model_never_drops_a_local_file_into_a_page(string parameters) =>
        Assert.False(WebFeaturePolicy.ModelMayCall("Input.dispatchDragEvent", parameters));
}
