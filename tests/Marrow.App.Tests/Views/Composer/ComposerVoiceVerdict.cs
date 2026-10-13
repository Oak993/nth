namespace Marrow.App.Tests.Views.Composer;

/// <summary>What the tests of ComposerVoiceCleanPillTests (and its per-scale classes) read from one identity's render (ED-NEW-voicefix-ui-10).</summary>
internal sealed record ComposerVoiceVerdict(double ShownOpacity, double RenderScaling, int ExpectedWidth, int FrameWidth, float[] Uniforms, float[] UniformsWithout, int Worst, string Where,
    IReadOnlyList<string> NotInTheDesign, IReadOnlyList<string> NotInTheDesignOverTheShippedGlass);
