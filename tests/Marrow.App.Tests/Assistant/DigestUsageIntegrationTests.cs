using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using static Marrow.App.Tests.Fixtures.DigestRigs;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 6 point 4, 5.2, 11.5; ED-NEW-dextra-as11-2, -4): the digest's usage section and the allowance suggestion over a real reserve. Every number
// names its window; the owner's floor shows as "your reserves hold" or as a crossed reserve; an allowance about to reset unused is offered only up to the room his floor
// leaves; use outside nth is found per window and told by the digest as unusual.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-2")]
public sealed class DigestUsageIntegrationTests
{
    private static readonly ReserveAccountRef Codex = new("codex:a", "Codex A", "codex");

    private static readonly DateTimeOffset Start = new(2026, 10, 13, 6, 0, 0, TimeSpan.Zero);

    private static ReserveSetup Reserve(ReserveSettings settings) => new()
    {
        Settings = settings,
        Calibrator = new UsageCalibrator(),
        Turns = new TurnCostHistory(),
        AccountOf = _ => Codex,
        AccountForNew = _ => Codex,
        AccountOfSnapshot = _ => Codex,
    };

    private static (DutyHostRig Rig, ReserveSettings Settings) Make(Func<IReadOnlyList<SpotProject>>? projects = null)
    {
        var settings = new ReserveSettings();
        var rig = new DutyHostRig(digest: Setup(projects: projects), reserve: Reserve(settings));
        rig.Host.Start();
        Assert.Null(rig.Host.Scheduler.SetEnabled(DigestDuty.Id, false));
        return (rig, settings);
    }

    private static void Read(DutyHostRig rig, double fiveHour, double weekly, TimeSpan weeklyResets, TimeSpan? fiveResets = null)
    {
        var now = rig.Clock.GetUtcNow();
        rig.Controller.Reserve!.OnReadings(Codex,
        [
            new ReserveWindow("five", ReserveWindowKind.FiveHour, fiveHour, now, now + (fiveResets ?? TimeSpan.FromHours(3))) { Title = "5-hour limit" },
            new ReserveWindow("week", ReserveWindowKind.Weekly, weekly, now, now + weeklyResets) { Title = "Weekly limit" },
        ]);
    }

    [Fact]
    public async Task The_canvas_example_reads_the_way_the_canvas_says_it_with_each_number_naming_its_window()
    {
        var (rig, _) = Make();
        using (rig)
        {
            Read(rig, 22, 69, TimeSpan.FromHours(8), TimeSpan.FromHours(3));          // the weekly window resets at 14:00; the 5-hour one at 09:00
            var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
            var usage = digest.Sections.Single(s => s.Part == DigestPart.Usage);
            Assert.Equal("Codex A: 22% of the 5-hour window used, resets at 09:00; 31% of the weekly window is unused and resets at 14:00, so I'm using it first.", usage.Items[0].Text);
            Assert.Equal(new DigestLink(DigestLinkKind.Account, "Codex A"), usage.Items[0].Link);
            Assert.Single(usage.Items);                                                  // no rule, so no reserve sentence
        }
    }

    [Fact]
    public async Task The_owners_floor_shows_as_the_reserve_holding_or_as_a_crossed_reserve_naming_the_window()
    {
        var (rig, settings) = Make();
        using (rig)
        {
            Assert.Null(settings.Add(new ReserveRule("r1", ReserveScope.Named("Codex A"), ReserveLifetime.Standing) { KeepPercent = 30 }));
            Read(rig, 22, 50, TimeSpan.FromHours(30));
            var holds = (await rig.Host.Digests!.Compose(DigestKind.Asked))!.Sections.Single(s => s.Part == DigestPart.Usage);
            Assert.Equal("Your reserves hold.", holds.Items[^1].Text);
            rig.Clock.Advance(TimeSpan.FromMinutes(5));
            Read(rig, 22, 75, TimeSpan.FromHours(30));
            var crossed = (await rig.Host.Digests.Compose(DigestKind.Asked))!.Sections.Single(s => s.Part == DigestPart.Usage);
            Assert.Equal("Codex A is past your reserve on the weekly window.", crossed.Items[^1].Text);
        }
    }

    [Fact]
    public async Task An_allowance_about_to_reset_unused_is_offered_up_to_the_room_the_floor_leaves_and_not_at_all_when_the_floor_leaves_none()
    {
        var project = new SpotProject(MissionRig.Project, "App", Start);
        var (rig, _) = Make(() => [project]);
        using (rig)
        {
            Read(rig, 22, 40, TimeSpan.FromHours(6));
            var open = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
            var offer = Assert.Single(open.Suggestions);
            Assert.Equal(SuggestionKind.AllowanceUnused, offer.Kind);
            Assert.Equal("60% of Codex A's weekly window is unused and resets at 12:00. Put up to 5% of it to work on App?", offer.Line);
            Assert.Equal("Up to 5% of Codex A's weekly window", offer.Cost);
        }
        var (tight, rules) = Make(() => [project]);
        using (tight)
        {
            Assert.Null(rules.Add(new ReserveRule("r1", ReserveScope.Named("Codex A"), ReserveLifetime.Standing) { KeepPercent = 30 }));
            Read(tight, 22, 69, TimeSpan.FromHours(6));                                  // 31% unused, but the floor is at 70: one point of room
            var held = (await tight.Host.Digests!.Compose(DigestKind.Asked))!;
            Assert.Empty(held.Suggestions);
        }
    }

    [Fact]
    public async Task Accepting_the_allowance_suggestion_starts_a_mission_that_names_the_window_in_its_brief()
    {
        var (rig, _) = Make(() => [new SpotProject(MissionRig.Project, "App", Start)]);
        using (rig)
        {
            Read(rig, 22, 40, TimeSpan.FromHours(6));
            var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
            var result = rig.Host.Digests.Accept(digest.Suggestions[0].Key);
            Assert.True(result.Ok, result.Message);
            var mission = Assert.Single(rig.Controller.Runner.Missions);
            Assert.Equal("Use the spare allowance on Codex A", mission.Mission.Title);
            Assert.Contains("weekly window", mission.Brief, StringComparison.Ordinal);
            Assert.Equal(MissionOrigin.Owner, mission.Mission.Origin);
        }
    }

    [Fact]
    public async Task Use_outside_nth_is_found_per_window_and_the_digest_names_the_window()
    {
        var (rig, _) = Make();
        using (rig)
        {
            Read(rig, 30, 10, TimeSpan.FromHours(30));
            rig.Clock.Advance(TimeSpan.FromMinutes(10));
            Read(rig, 49, 10, TimeSpan.FromHours(30));                                   // 19 points of the 5-hour window the ledger never saw
            var rows = rig.Controller.Reserve!.OutsideUses(TimeSpan.FromHours(24));
            var row = Assert.Single(rows);
            Assert.Equal(("codex:a", "Codex A", ReserveWindowKind.FiveHour), (row.AccountKey, row.AccountName, row.Kind));
            Assert.Equal(19, row.Points, 6);
            var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
            Assert.Equal(["19% of the 5-hour window on Codex A went on use outside nth in the last day."], digest.Sections.Single(s => s.Part == DigestPart.Unusual).Items.Select(i => i.Text));
        }
    }

    [Fact]
    public void Use_outside_nth_older_than_the_span_is_not_counted_and_a_guard_that_saw_none_has_none()
    {
        var (rig, _) = Make();
        using (rig)
        {
            Assert.Empty(rig.Controller.Reserve!.OutsideUses(TimeSpan.FromHours(24)));
            Read(rig, 30, 10, TimeSpan.FromHours(30));
            rig.Clock.Advance(TimeSpan.FromMinutes(10));
            Read(rig, 49, 10, TimeSpan.FromHours(30));
            Assert.Single(rig.Controller.Reserve.OutsideUses(TimeSpan.FromHours(24)));
            rig.Clock.Advance(TimeSpan.FromHours(30));
            Assert.Empty(rig.Controller.Reserve.OutsideUses(TimeSpan.FromHours(24)));
        }
    }
}
