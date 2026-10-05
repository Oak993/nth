namespace Marrow.UI.Tests.Port.Voice;

/// <summary>
/// VT-9, V-T60: a theory that compares with the reference PNGs of ref-glow.js (the Windows run: headless Chrome, ANGLE D3D11). While the folder holds none the theory is reported
/// as SKIPPED with that reason, never as passed. With any PNG present every row runs, and a row whose own PNG is missing fails.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RefGlowTheoryAttribute : TheoryAttribute
{
    public RefGlowTheoryAttribute()
    {
        var folder = VoiceGlowRig.ReferenceFolder();
        if (!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*.png").Any())
            Skip = "reference PNGs pending: " + folder + " holds no PNG; run tests/fixtures/voice/ref-glow.js on Windows (ANGLE D3D11) and commit its output.";
    }
}
