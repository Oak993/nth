// REVIEW (UE-6 independent review): a release the packaging guard licenses must show at least one card in the app.
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;

namespace Marrow.App.Tests.Port.ReleaseNotes;

[Trait("Decision", "ED-NEW-ue-6")]
[Trait("Feature", "UE-6")]
[Trait("Review", "UE-6")]
public sealed class REVIEW_ReleaseNotesDigestParityTests
{
    [Fact]
    public void Notes_the_guard_accepts_are_never_an_empty_digest_in_the_app()
    {
        using var rig = new ReleaseNotesScriptRig();
        // A bold pseudo-heading ends the section for the app's digest (IsHeader), but not for the guard, which only resets on '#' headings.
        File.WriteAllText(rig.Changelog, "## [1.7.0] - 2026-10-05\n\n### New features\n\n**Thanks**\n\n- Someone\n");
        var result = rig.Run("scripts/extract-release-notes.ps1", "-Version", "1.7.0", "-ChangelogFile", rig.Changelog, "-OutputFile", rig.Output);
        if (result.ExitCode != 0) return; // refusing these notes is also correct
        Assert.False(UpdateReleaseNotesDigest.Parse(File.ReadAllText(rig.Output)).IsEmpty,
            "the guard licensed a release whose What's new shows no card (the only bullet is under a Thanks heading)");
    }
}
