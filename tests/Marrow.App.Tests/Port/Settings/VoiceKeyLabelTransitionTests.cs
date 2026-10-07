using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

[Collection("Headless")]
public sealed class VoiceKeyLabelTransitionTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "OD-0076")]
    public void Opening_and_closing_the_editor_transition_the_label_and_reduce_motion_settles_it(bool reduceMotion) => VoiceTypingPageRig.Run(headless, scene =>
    {
        EnvironmentValues.SetAccessibilityReduceMotion(scene.Page, reduceMotion);
        scene.Page.EngineControl.Select("cloud");
        scene.Advance(600);
        var label = Assert.IsType<TextBlock>(scene.Page.KeyButton.Child);
        scene.Page.KeyButton.Activate();
        Assert.Equal(reduceMotion ? "Cancel" : "Add key", label.Text);
        if (!reduceMotion)
        {
            Assert.Equal(1, label.Opacity);
            scene.Advance(20);
            Assert.Equal("Add key", label.Text);
            Assert.InRange(label.Opacity, .01, .99);
        }
        scene.Advance(600);
        Assert.Equal("Cancel", label.Text);
        Assert.Equal(1, label.Opacity);
        scene.Page.KeyButton.Activate();
        Assert.Equal(reduceMotion ? "Add key" : "Cancel", label.Text);
        scene.Advance(600);
        Assert.Equal("Add key", label.Text);
        Assert.Equal(1, label.Opacity);
        Assert.True(scene.Page.KeyButton.IsEnabled); // The editor fold closes, but the cloud key row stays open.
        Assert.True(scene.Page.KeyButton.Focusable);
    });
}
