using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 9 item 7 and 3 (the canvas's Update, "What's new in 1.1.0"); VELOPACK_CHECK.md
// findings 5 and 13 (ED-NEW-inst-fix-1). Update's "What's new" card was never fed, so it never showed. Its lines now come from the
// release notes the packaging script validated and embedded, read with the app's own parser (the About digest's rules).
public sealed class SetupReleaseNotesTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void New_features_come_first_then_refinements_then_fixes_with_the_markdown_taken_off_and_three_at_most()
    {
        const string notes = "# nth 1.1.0\r\n\r\n### Bug Fixes\r\n- Fixed a crash when a thread closed\r\n\r\n## New Features\r\n- **Ignis** answers faster\r\n- Open a [thread](https://example.invalid) from the tray\r\n\r\n### Refinements\r\n* `Tactus` is calmer\r\n- Second refinement\r\n";

        Assert.Equal(["Ignis answers faster", "Open a thread from the tray", "Tactus is calmer"], SetupReleaseNotes.Lines(notes));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Fewer_lines_than_the_card_holds_are_all_shown_in_order()
    {
        Assert.Equal(["Calmer start", "No crash on close"], SetupReleaseNotes.Lines("### Fixes\n- No crash on close\n### Improvements\n- Calmer start\n"));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("# nth 1.1.0\nJust words, no bullets.\n")]
    [InlineData("### Other\n- not a known header\n")]
    [InlineData("- a bullet before any header\n")]
    public void Notes_with_nothing_for_the_card_give_no_lines_which_hides_it(string? notes) =>
        Assert.Empty(SetupReleaseNotes.Lines(notes));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_build_without_notes_gives_none_and_the_window_is_given_the_embedded_ones()
    {
        Assert.Empty(SetupReleaseNotes.Embedded());
        Assert.Equal(3, SetupReleaseNotes.MaxLines);
        var program = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs"));
        Assert.Contains("ReleaseNotes = SetupReleaseNotes.Embedded(),", program, StringComparison.Ordinal);
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "Directory.Build.targets"));
        Assert.Contains("LogicalName=\"" + SetupReleaseNotes.ResourceName + "\"", targets, StringComparison.Ordinal);
    }
}
