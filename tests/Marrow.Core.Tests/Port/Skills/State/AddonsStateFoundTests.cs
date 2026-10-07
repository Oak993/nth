using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Tests.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: "found-on-this-PC results merged". ONDEMAND-PLAN.md 3.6: "the PLAN.md 2.6 probe runs on the Settings page; a tool the user already has
// shows 'Found on this PC' with its path, and the offer never fires for it. 'Download Marrow's copy anyway' stays available." A probe run reports
// for the tools it probed; its answer replaces the earlier answer for exactly those tools (found, or no longer found) and leaves the others as they were
// (ED-295). A probe result never changes an item's download state: the copy Marrow downloaded and the copy the user has are independent.
[Trait("Spec", "DL-7")]
public sealed class AddonsStateFoundTests
{
    private static readonly SkillsCatalogue Catalogue = CatalogueFixtures.Real;

    private static AddonsState Merge(AddonsState state, string[] probed, params FoundOnPc[] results) => state.MergeFound(Catalogue, probed, results);

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Found_tools_are_recorded_with_their_paths_and_looked_up_by_id()
    {
        var state = Merge(AddonsState.Empty, ["T1", "T3", "T8"], new FoundOnPc("T1", @"C:\Python314\python.exe"), new FoundOnPc("T8", @"C:\gh\gh.exe"));
        Assert.True(state.IsFoundOnPc("T1"));
        Assert.Equal(@"C:\Python314\python.exe", state.FoundPath("T1"));
        Assert.False(state.IsFoundOnPc("T3"));
        Assert.Null(state.FoundPath("T3"));
        Assert.Equal(["T1", "T8"], state.Found.Select(f => f.ItemId));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_new_probe_replaces_the_answer_for_the_tools_it_probed_and_keeps_the_rest()
    {
        var first = Merge(AddonsState.Empty, ["T1", "T3", "T8"], new FoundOnPc("T1", @"C:\Python314\python.exe"), new FoundOnPc("T3", @"C:\LO\soffice.exe"), new FoundOnPc("T8", @"C:\gh\gh.exe"));
        // Second run probes T1 (moved), T3 (gone) and T5 (new); T8 was not probed this time.
        var second = Merge(first, ["T1", "T3", "T5"], new FoundOnPc("T1", @"D:\Python\python.exe"), new FoundOnPc("T5", @"C:\qpdf\qpdf.exe"));
        Assert.Equal(@"D:\Python\python.exe", second.FoundPath("T1"));
        Assert.False(second.IsFoundOnPc("T3"));
        Assert.Equal(@"C:\gh\gh.exe", second.FoundPath("T8"));
        Assert.Equal(@"C:\qpdf\qpdf.exe", second.FoundPath("T5"));
        Assert.Equal(["T1", "T5", "T8"], second.Found.Select(f => f.ItemId));
        Assert.Equal(StateFixtures.Text(second), StateFixtures.Text(Merge(second, ["T1", "T5"], new FoundOnPc("T1", @"D:\Python\python.exe"), new FoundOnPc("T5", @"C:\qpdf\qpdf.exe"))));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_empty_probe_changes_nothing_and_a_probe_that_finds_nothing_clears_what_it_probed()
    {
        var state = Merge(AddonsState.Empty, ["T1"], new FoundOnPc("T1", @"C:\p\python.exe"));
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(Merge(state, [])));
        Assert.Empty(Merge(state, ["T1"]).Found);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_merge_never_changes_an_item_s_state_or_the_other_sections()
    {
        var state = StateFixtures.Full();
        var merged = Merge(state, ["T1", "T8", "T9"], new FoundOnPc("T9", @"C:\uv\uv.exe"));
        Assert.Equal(state.Items, merged.Items);
        Assert.Equal(state.AcceptedTerms, merged.AcceptedTerms);
        Assert.Equal(state.Declined.Select(d => d.ChatId), merged.Declined.Select(d => d.ChatId));
        Assert.Equal(["T9"], merged.Found.Select(f => f.ItemId));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_catalogue_tools_can_be_found_and_a_result_must_be_for_a_probed_tool_and_have_an_absolute_path()
    {
        var state = StateFixtures.Full();
        var before = StateFixtures.Text(state);
        Assert.Throws<ArgumentException>(() => Merge(state, ["T99"], new FoundOnPc("T99", @"C:\x.exe")));
        Assert.Throws<ArgumentException>(() => Merge(state, ["P1"], new FoundOnPc("P1", @"C:\x.exe")));
        Assert.Throws<ArgumentException>(() => Merge(state, ["A1"], new FoundOnPc("A1", @"C:\x.exe")));
        Assert.Throws<ArgumentException>(() => Merge(state, ["T1"], new FoundOnPc("T3", @"C:\x.exe")));
        Assert.Throws<ArgumentException>(() => Merge(state, ["T1"], new FoundOnPc("T1", @"C:\a.exe"), new FoundOnPc("T1", @"C:\b.exe")));
        Assert.Throws<ArgumentException>(() => Merge(state, ["T1"], new FoundOnPc("T1", "python.exe")));
        Assert.Throws<ArgumentException>(() => Merge(state, ["T1", "T1"]));
        Assert.Throws<ArgumentException>(() => Merge(state, [""]));
        Assert.Equal(before, StateFixtures.Text(state));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Found_results_round_trip_through_the_file()
    {
        var state = Merge(AddonsState.Empty, ["T1", "T10"], new FoundOnPc("T10", @"C:\Users\Someone\AppData\Local\ms-playwright\chromium-1243\chrome-win\chrome.exe"));
        var back = AddonsStateJson.Parse(StateFixtures.Text(state));
        Assert.Equal(state.Found, back.Found);
    }
}
