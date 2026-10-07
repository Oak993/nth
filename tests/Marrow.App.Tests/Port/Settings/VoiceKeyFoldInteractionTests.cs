using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// Lead ruling: a collapsed cloud-key row must leave no enabled/focusable key button in the keyboard path.
[Collection("Headless")]
public sealed class VoiceKeyFoldInteractionTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")]
    public void The_key_button_is_inert_when_folded_and_reenables_on_every_cloud_open(bool externalChange) => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.False(scene.Page.KeyFold.Open);
        Assert.False(scene.Page.KeyButton.IsEnabled);
        Assert.False(scene.Page.KeyButton.Focusable);
        Assert.False(scene.Page.KeyButton.Focus());
        scene.Page.KeyButton.Activate();
        Assert.Equal("Add key", Assert.IsType<TextBlock>(scene.Page.KeyButton.Child).Text);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            if (externalChange)
            {
                scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud };
                VoiceTypingScene.Sync();
            }
            else scene.Page.EngineControl.Select("cloud");
            Assert.True(scene.Page.KeyFold.Open);
            Assert.True(scene.Page.KeyButton.IsEnabled);
            Assert.True(scene.Page.KeyButton.Focusable);
            scene.Advance(600);
            Assert.True(scene.Page.KeyButton.Focus());
            scene.Page.KeyButton.Activate();
            scene.Advance(600);
            Assert.Equal("Cancel", Assert.IsType<TextBlock>(scene.Page.KeyButton.Child).Text);

            if (externalChange)
            {
                scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Engine = VoiceEngine.Local };
                VoiceTypingScene.Sync();
            }
            else scene.Page.EngineControl.Select("local");
            // Inert as soon as closing starts, including while the row is still fading away.
            Assert.False(scene.Page.KeyFold.Open);
            Assert.False(scene.Page.KeyButton.IsEnabled);
            Assert.False(scene.Page.KeyButton.Focusable);
            Assert.False(scene.Page.KeyButton.Focus());
            scene.Page.KeyButton.Activate();
            scene.Advance(600);
            Assert.Equal(0, scene.Page.KeyFold.Fraction);
            Assert.Equal("Add key", Assert.IsType<TextBlock>(scene.Page.KeyButton.Child).Text);
            Assert.False(scene.Page.KeyButton.IsEnabled);
            Assert.False(scene.Page.KeyButton.Focusable);
            Assert.All(LogicalQuery.SelfAndDescendants<TextBox>(scene.Page.KeyFold), field => Assert.Equal("", field.Text));
        }
    });

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")]
    public void A_page_loaded_on_cloud_enables_the_key_button_then_makes_it_inert_on_local() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.True(scene.Page.KeyFold.Open);
        Assert.True(scene.Page.KeyButton.IsEnabled);
        Assert.True(scene.Page.KeyButton.Focusable);
        scene.Page.EngineControl.Select("local");
        Assert.False(scene.Page.KeyButton.IsEnabled);
        Assert.False(scene.Page.KeyButton.Focusable);
        Assert.False(scene.Page.KeyButton.Focus());
    }, arrange: model => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });
}
