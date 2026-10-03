using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Tests;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the composer light's settings persist in the app's settings store under
// "composerLight", one JSON object with the playground's keys; Border beam is the default (OD-0089); a stored value
// survives a relaunch; an unreadable one is the default. Built on AppSettingsTests' throwaway settings file.
[Collection("AppSettings")]
public sealed class AppSettingsComposerLightTests
{
    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089: by default it will be on beam
    public void Nothing_stored_is_border_beam_with_the_playgrounds_values()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() => Assert.Equal(ComposerLightSettings.Default, new AppSettings().ComposerLight));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the user's choices persist (OD-0089) and are clamped to the playground's sliders
    public void A_choice_is_stored_and_read_back_after_a_relaunch()
    {
        using var scope = AppSettingsScope.Create();
        var chosen = ComposerLightSettings.Default with
        {
            Style = ComposerLightStyle.Aura,
            Beam = BorderBeamOptions.Default.WithType(BeamType.Line) with { Palette = BeamPalette.Ice, Duration = 7.31 },
            Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Accent, Rest = 25 },
        };
        scope.OnMain(() =>
        {
            var settings = new AppSettings { ComposerLight = chosen };
            Assert.Equal(7.32, settings.ComposerLight.Beam.Duration!.Value, 9);       // the slider's .02 step
            Assert.Contains(("set", "composerLight"), scope.Defaults.Writes);
            Assert.Equal(Encoding.UTF8.GetString(chosen.Clamped().ToJson()), Encoding.UTF8.GetString((byte[])scope.Defaults.Object("composerLight")!));
        });
        scope.Relaunch();
        scope.OnMain(() =>
        {
            var again = new AppSettings().ComposerLight;
            Assert.Equal(chosen.Clamped(), again);
            Assert.Equal(ComposerLightStyle.Aura, again.Style);
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // an unreadable stored value is the default, not a failure
    public void Unreadable_data_is_the_default()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() => scope.Defaults.Set("composerLight", Encoding.UTF8.GetBytes("{oops")));
        scope.Relaunch();
        scope.OnMain(() => Assert.Equal(ComposerLightSettings.Default, new AppSettings().ComposerLight));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // REVIEW: stored bytes that parse as JSON but hold invalid UTF-8 (or a lone surrogate escape) are the default, not an exception out of AppSettings' load
    public void Stored_text_that_is_not_valid_unicode_is_the_default()
    {
        using var scope = AppSettingsScope.Create();
        byte[] bad = [.. Encoding.ASCII.GetBytes("{\"style\":\""), 0xFF, .. Encoding.ASCII.GetBytes("\"}")];
        scope.OnMain(() => scope.Defaults.Set("composerLight", bad));
        scope.Relaunch();
        scope.OnMain(() => Assert.Equal(ComposerLightSettings.Default, new AppSettings().ComposerLight));
        scope.OnMain(() => scope.Defaults.Set("composerLight", Encoding.ASCII.GetBytes("{\"aura\":{\"c1\":\"\\ud800\"}}")));
        scope.Relaunch();
        scope.OnMain(() => Assert.Equal(ComposerLightSettings.Default, new AppSettings().ComposerLight));
    }
}
