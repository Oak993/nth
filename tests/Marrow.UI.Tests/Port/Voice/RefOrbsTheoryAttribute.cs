namespace Marrow.UI.Tests.Port.Voice;

/// <summary>
/// VT-8, V-T50: a theory that compares with the reference PNGs of ref-orbs.js. The committed PNGs are the lead's run on Windows (ANGLE D3D11); while the folder holds none
/// the theory is reported as SKIPPED with that reason, never as passed. With any PNG present every row runs, and a row whose own PNG is missing fails.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RefOrbsTheoryAttribute : TheoryAttribute
{
    public RefOrbsTheoryAttribute()
    {
        var folder = OrbRig.ReferenceFolder();
        if (!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*.png").Any())
            Skip = "reference PNGs pending (lead's Windows run): " + folder + " holds no PNG; run tests/fixtures/voice/ref-orbs.js on Windows (ANGLE D3D11) and commit its output.";
    }
}
