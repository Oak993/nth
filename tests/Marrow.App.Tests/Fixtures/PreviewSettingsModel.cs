using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The two app settings the Customization stage reads (the composer light and the backdrop opacity), as an observable model with no store behind it, for the stage's tests (CZ-3).</summary>
internal sealed class PreviewSettingsModel : MainActorModel
{
    private ComposerLightSettings light = ComposerLightSettings.Default;
    private double backdrop = 0.5;

    internal ComposerLightSettings Light { get => Read(light); set => Set(ref light, value); }
    internal double Backdrop { get => Read(backdrop); set => Set(ref backdrop, value); }
}
