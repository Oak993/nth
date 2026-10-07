using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// V-16.3.1, Q-V-16 (BUILD-PLAN 1.1): the Providers masked field and eye, immediate protected save, blank removes, Add key / Cancel fold.
[Collection("Headless")]
public sealed class VoiceTypingKeyRowTests(HeadlessSession headless)
{
    private static TextBox Field(VoiceTypingSettingsPage page) => Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(page.KeyFold));

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T47")]
    public void A_refused_protected_save_shows_the_Providers_refusal_and_detaching_clears_the_field() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = new AppSettingsCredentialBackend(() => true) { RefuseWrites = true };
        using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        scene.Page.EngineControl.Select("cloud");
        scene.Page.KeyButton.Activate();
        scene.Advance(600);
        var field = Field(scene.Page);
        field.Text = "sk-refused-vt16";
        Assert.Null(new VoiceKeyStore().Read());
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(scene.Page.KeyFold),
            block => block.Text == "Credential Manager did not accept this key, so it was not saved.");
        Assert.DoesNotContain("sk-", System.Text.Encoding.UTF8.GetString(scene.Model.Settings.VoiceTyping.ToJson()), StringComparison.Ordinal);
        scene.Window.Content = null;
        VoiceTypingScene.Sync();
        Assert.Equal("", field.Text);
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T47")]
    public void The_cloud_key_row_opens_a_masked_accessible_field_and_Cancel_clears_and_closes_it() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(VoiceEngine.Local, scene.Model.Settings.VoiceTyping.Engine);
        scene.Page.EngineControl.Select("cloud");
        Assert.True(scene.Page.KeyButton.IsEnabled);
        Assert.True(scene.Page.KeyButton.Focusable);
        scene.Page.KeyButton.Activate();
        scene.Advance(600);
        var field = Field(scene.Page);
        Assert.Equal("Paste your OpenAI key", field.PlaceholderText);
        Assert.Equal("OpenAI key", AutomationProperties.GetName(field));
        Assert.Equal('\u2022', field.PasswordChar);
        Assert.Equal(200, field.Width);
        Assert.Equal("Cancel", Assert.IsType<TextBlock>(scene.Page.KeyButton.Child).Text);
        field.Text = "sk-synthetic-vt16";
        scene.Page.KeyButton.Activate();
        scene.Advance(600);
        Assert.Equal("", field.Text);
        Assert.Equal("Add key", Assert.IsType<TextBlock>(scene.Page.KeyButton.Child).Text);
        Assert.Equal("sk-synthetic-vt16", new VoiceKeyStore().Read()); // Providers saves immediately; Cancel closes, not rolls back
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T47")]
    public void Editing_replaces_the_protected_key_and_blank_removes_it_without_a_settings_copy() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var keys = new VoiceKeyStore();
        Assert.Equal("sk-existing-vt16", keys.Read());
        scene.Page.EngineControl.Select("cloud");
        scene.Page.KeyButton.Activate();
        scene.Advance(600);
        var field = Field(scene.Page);
        Assert.Equal("sk-existing-vt16", field.Text);
        field.Text = "  sk-replacement-vt16  ";
        Assert.Equal("sk-replacement-vt16", keys.Read());
        Assert.DoesNotContain("sk-", System.Text.Encoding.UTF8.GetString(scene.Model.Settings.VoiceTyping.ToJson()), StringComparison.Ordinal);
        field.Text = "";
        Assert.Null(keys.Read());
    }, arrange: _ => Assert.True(new VoiceKeyStore().Save("sk-existing-vt16")));

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T47")]
    public void The_Providers_eye_reveals_and_hides_and_switching_to_local_clears_the_field() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Page.EngineControl.Select("cloud");
        scene.Page.KeyButton.Activate();
        scene.Advance(600);
        var field = Field(scene.Page);
        field.Text = "sk-eye-vt16";
        var eye = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(scene.Page.KeyFold));
        eye.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal('\0', field.PasswordChar);
        Assert.Equal("Hide the API key", ToolTip.GetTip(eye));
        eye.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal('\u2022', field.PasswordChar);
        Assert.Equal("Show the API key", ToolTip.GetTip(eye));
        scene.Page.EngineControl.Select("local");
        Assert.Equal("", field.Text);
        Assert.Equal("sk-eye-vt16", new VoiceKeyStore().Read());
        scene.Page.EngineControl.Select("cloud");
        scene.Page.KeyButton.Activate();
        scene.Advance(600);
        Assert.Equal('\u2022', field.PasswordChar);
        Assert.Equal("sk-eye-vt16", Field(scene.Page).Text);
    });
}
