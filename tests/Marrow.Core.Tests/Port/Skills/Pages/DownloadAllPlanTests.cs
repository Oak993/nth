using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17, OD-0099 "Download all A" and the owner's review of version 3): each page's Download all "states the total download size and asks the user to confirm or cancel
// before anything downloads"; it takes the items on that page that are not on this PC yet; the large-download warning only above 100 MB.
public sealed class DownloadAllPlanTests
{
    private static DownloadAllPlan Plan(LibrarySection section, DriveSpace? free = null, params ItemRecord[] records) =>
        DownloadAllPlan.Build(section, PageFixtures.Rows(section, null, null, records), PageFixtures.Catalogue(), free);

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plan_sums_the_sizes_of_the_items_it_would_fetch_and_names_the_hosts()
    {
        var plan = Plan(LibrarySection.Skills);
        Assert.Equal(["SA"], plan.ToDownload);
        Assert.Equal(3.5, plan.DownloadMb, 6);
        Assert.Equal(3.5, plan.DiskMb, 6);
        Assert.Equal(["github.com"], plan.Hosts);
        Assert.Equal("3.5 MB", plan.DownloadFact);
        Assert.Equal("about 3.5 MB", plan.DiskFact);
        Assert.Equal("github.com", plan.HostsFact);
        Assert.Equal("1 item from github.com.", plan.Sub);
        Assert.Equal("Download all skills?", plan.Title);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Decision", "ED-NEW-dl-6")]
    public void An_item_this_build_cannot_fetch_stays_in_the_list_as_skipped_with_its_reason_and_is_neither_fetched_nor_counted()
    {
        var plan = DownloadAllPlan.Build(LibrarySection.Skills, PageFixtures.Rows(LibrarySection.Skills, null, null), PageFixtures.Catalogue(), null, id => id != "SA");
        Assert.Equal(PlanLineKind.CantFetch, plan.Lines.Single(l => l.Id == "SA").Kind);
        Assert.Empty(plan.ToDownload);
        Assert.Equal(0, plan.DownloadMb);
        Assert.Empty(plan.Hosts);
        Assert.False(plan.CanContinue);
        Assert.Equal(LibraryWords.NothingToDownload, plan.Statement);
        Assert.EndsWith(" · 1 can’t be downloaded in this build, skipped", plan.IncludedHeading, StringComparison.Ordinal);
        Assert.Equal("Included · 3 · 2 already here, skipped · 1 can’t be downloaded in this build, skipped", LibraryWords.Included(3, 2, 0, 0, 1));
        var here = DownloadAllPlan.Build(LibrarySection.Skills, PageFixtures.Rows(LibrarySection.Skills, null, null, PageFixtures.Rec("SA", ItemState.Installed, PageFixtures.Pin)), PageFixtures.Catalogue(), null, _ => false);
        Assert.Equal(PlanLineKind.AlreadyHere, here.Lines.Single(l => l.Id == "SA").Kind); // on this PC wins over fetchable
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Items_that_are_here_already_are_listed_but_not_fetched_or_counted()
    {
        var plan = Plan(LibrarySection.Plugins, null, PageFixtures.Rec("PA", ItemState.Installed, PageFixtures.Pin));
        Assert.Equal([PlanLineKind.AlreadyHere, PlanLineKind.NeedsTerms], plan.Lines.Select(l => l.Kind));
        Assert.Empty(plan.ToDownload);
        Assert.Equal(0, plan.DownloadMb);
        Assert.True(plan.IsEmpty);
        Assert.False(plan.CanContinue);
        Assert.Equal("Everything on this page is already here.", plan.Statement);
        var update = Plan(LibrarySection.Skills, null, PageFixtures.Rec("SA", ItemState.UpdateAvailable, "old", "new"), PageFixtures.Rec("SB", ItemState.Damaged, "x"));
        Assert.Equal(PlanLineKind.AlreadyHere, update.Lines[0].Kind);
        Assert.Equal(PlanLineKind.AlreadyHere, update.Lines[1].Kind);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_failed_item_is_fetched_again_and_one_that_is_being_fetched_is_not_part_of_the_plan()
    {
        var plan = Plan(LibrarySection.Skills, null, PageFixtures.Rec("SA", ItemState.Failed, null, PageFixtures.Pin, "boom"), PageFixtures.Rec("SB", ItemState.Downloading, null, PageFixtures.Pin));
        Assert.Equal(["SA"], plan.ToDownload);
        Assert.DoesNotContain(plan.Lines, l => l.Id == "SB");
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_under_terms_not_yet_accepted_and_an_item_with_no_size_are_left_out_and_say_why()
    {
        var plan = Plan(LibrarySection.Skills);
        Assert.Equal([PlanLineKind.Download, PlanLineKind.NoSize, PlanLineKind.NeedsTerms], plan.Lines.Select(l => l.Kind));
        Assert.Equal("Under Anthropic's terms", plan.Lines[2].TermsLabel);
        Assert.Equal("Included · 3 · 1 needs its terms first, skipped · 1 has no size yet, skipped", plan.IncludedHeading);
        Assert.Equal(["SA"], plan.ToDownload);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Terms_that_are_accepted_let_the_item_into_the_plan()
    {
        var accepted = AddonsState.Empty.AcceptTerms(PageFixtures.LicenceHash, "SC", new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var plan = DownloadAllPlan.Build(LibrarySection.Skills, PageFixtures.Rows(LibrarySection.Skills, accepted), PageFixtures.Catalogue(), null);
        Assert.Equal(["SA", "SC"], plan.ToDownload);
        Assert.Equal(3.78, plan.DownloadMb, 6);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Up_to_one_hundred_megabytes_the_size_is_stated_plainly_and_above_it_the_large_download_warning_opens_the_box()
    {
        var plain = Plan(LibrarySection.Skills);
        Assert.False(plain.Large);
        Assert.Equal("About 3.5 MB to download, about 3.5 MB on disk.", plain.Statement);
        var catalogue = PageFixtures.Catalogue();
        var big = catalogue with { Items = catalogue.Items.Select(i => i.Id == "SA" ? i with { ApproxSizeMb = 1040.2 } : i).ToList() };
        var plan = DownloadAllPlan.Build(LibrarySection.Skills, LibraryRows.Build(LibrarySection.Skills, big, AddonsState.Empty, PageFixtures.Records(), _ => null, new Switches()), big, null);
        Assert.True(plan.Large);
        Assert.Equal("This can be a large download: 1.04 GB (1,040.2 MB), and it takes about 1.0 GB of disk space once installed.", plan.Statement);
        var edge = big with { Items = big.Items.Select(i => i.Id == "SA" ? i with { ApproxSizeMb = 100 } : i).ToList() };
        Assert.False(DownloadAllPlan.Build(LibrarySection.Skills, LibraryRows.Build(LibrarySection.Skills, edge, AddonsState.Empty, PageFixtures.Records(), _ => null, new Switches()), edge, null).Large);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_free_space_is_shown_and_too_little_of_it_stops_Continue_with_the_amount_it_needs()
    {
        var roomy = Plan(LibrarySection.Skills, new DriveSpace("C:", 212_000_000_000));
        Assert.Equal("212 GB", roomy.FreeFact);
        Assert.True(roomy.EnoughRoom);
        Assert.True(roomy.CanContinue);
        Assert.Null(roomy.RoomNote);
        var tight = Plan(LibrarySection.Skills, new DriveSpace("C:", 7_000_000));
        Assert.Equal(7_700_000, tight.NeedBytes);
        Assert.False(tight.EnoughRoom);
        Assert.False(tight.CanContinue);
        Assert.Equal("Needs 8 MB free on C:.", tight.RoomNote);
        var unknown = Plan(LibrarySection.Skills);
        Assert.Null(unknown.FreeFact);
        Assert.True(unknown.CanContinue);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_hosts_read_as_a_list_and_more_than_three_are_folded()
    {
        Assert.Equal("github.com", DownloadAllPlan.HostsText(["github.com"]));
        Assert.Equal("a.org and b.org", DownloadAllPlan.HostsText(["a.org", "b.org"]));
        Assert.Equal("a.org, b.org and c.org", DownloadAllPlan.HostsText(["a.org", "b.org", "c.org"]));
        Assert.Equal("a.org, b.org, c.org and 2 more", DownloadAllPlan.HostsText(["a.org", "b.org", "c.org", "d.org", "e.org"]));
        Assert.Equal("", DownloadAllPlan.HostsText([]));
    }
}
