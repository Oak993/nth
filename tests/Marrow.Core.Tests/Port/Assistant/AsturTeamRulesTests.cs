using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.AsturTeamSample;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1, 9.2, 12.1; ED-NEW-dextra-as16-4): what a profile card must be before it can become an Astur. A name that is free and
// cannot be mistaken for a spoken command, a shape no other Astur has, at most six Asturs, a budget that means something, bounded lines, and rules that
// are never looser than Dextra's.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-4")]
public sealed class AsturTeamRulesTests
{
    private static AsturDraft Good(string name = "Channel C", AsturShape shape = AsturShape.Diamond) => new()
    {
        Name = name,
        Shape = shape,
        Area = "A channel: scripts and numbers.",
        Budget = new(15),
    };

    private static IReadOnlyList<string> Problems(AsturDraft draft, IReadOnlyList<AssistantProfile>? team = null, string? editing = null) =>
        AsturTeamRules.Problems(draft, team ?? Channels(), editing);

    [Fact]
    public void A_good_card_has_no_problems_and_becomes_a_profile_under_Dextra()
    {
        var draft = Good() with
        {
            Style = "short and plain",
            Aliases = new ValueList<string>(["the third one"]),
            Duties = new ValueList<DutyDraft>([new("Watch the numbers", true), new("Prepare the script", false)]),
            Budget = new(20, 5m),
            Template = "channel",
        };
        Assert.Empty(Problems(draft));
        var profile = AsturTeamRules.ToProfile(draft, "c3", DextraId, Now);
        Assert.Equal(("c3", "Channel C", DextraId), (profile.Id, profile.Name, profile.ParentId));
        Assert.Equal(AsturShape.Diamond, profile.Shape);
        Assert.Equal(new AsturBudget(20, 5m), profile.Budget);
        Assert.Equal(Now, profile.Created);
        Assert.Equal(2, profile.Duties.Count);
        Assert.Equal("channel", profile.Template);
        Assert.False(profile.IsRoot);
        Assert.Equal(["the third one"], profile.Aliases);
        Assert.Equal("short and plain", profile.Style);
    }

    [Theory]
    [InlineData("", "Give it a name.")]
    [InlineData("   ", "Give it a name.")]
    [InlineData("A​nn", null)]
    public void A_name_is_needed_and_cleaned(string name, string? problem)
    {
        var found = Problems(Good(name));
        if (problem is null) Assert.Empty(found);
        else Assert.Contains(problem, found);
    }

    [Theory]
    [InlineData("Channel, C")]
    [InlineData("Who@Home")]
    [InlineData("a:b")]
    [InlineData("a;b")]
    [InlineData("<b>")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("!!!")]
    public void A_name_is_letters_and_numbers_with_spaces(string name) =>
        Assert.Contains("A name is letters and numbers, with spaces; it has no @ , : ; or similar marks.", Problems(Good(name)));

    [Theory]
    [InlineData("Dextra")]
    [InlineData("dextra")]
    [InlineData("Astur")]
    [InlineData("Everyone")]
    [InlineData("Stop")]
    [InlineData("Hey")]
    [InlineData("Ask")]
    public void A_word_nth_listens_for_is_not_a_name(string name) =>
        Assert.Contains($"\"{name}\" is a word nth listens for; pick another name.", Problems(Good(name)));

    [Fact]
    public void A_name_is_at_most_forty_characters()
    {
        Assert.Empty(Problems(Good(new string('x', 40))));
        Assert.Contains("A name holds at most 40 characters.", Problems(Good(new string('x', 41))));
    }

    [Fact]
    public void A_name_another_assistant_answers_to_is_taken_by_name_or_by_alias_in_any_case_or_accent()
    {
        var team = Team(Astur("Channel A", AsturShape.Squircle, null, false, "The News Desk"), Astur("Café", AsturShape.Hexagon));
        Assert.Contains("Channel A already answers to \"channel a\".", Problems(Good("channel a"), team));
        Assert.Contains("Channel A already answers to \"the news desk\".", Problems(Good("the news desk"), team));
        Assert.Contains("Café already answers to \"CAFE\".", Problems(Good("CAFE"), team));
        Assert.Empty(Problems(Good("Channel"), team));                                  // a name that only begins another is free: the whole name wins when it is said
    }

    [Fact]
    public void An_Astur_edited_does_not_collide_with_itself_and_a_retired_ones_name_and_shape_are_free()
    {
        var team = Team(Astur("Channel A", AsturShape.Squircle, "a"), Astur("Old", AsturShape.Hexagon, "old", retired: true));
        Assert.Empty(Problems(Good("Channel A", AsturShape.Squircle), team, editing: "a"));
        Assert.NotEmpty(Problems(Good("Channel A", AsturShape.Diamond), team));
        Assert.Empty(Problems(Good("Old", AsturShape.Hexagon), team));
    }

    [Fact]
    public void Aliases_are_bounded_clean_and_not_shared()
    {
        var team = Channels();
        Assert.Contains("Channel B already answers to \"Channel B\".", Problems(Good() with { Aliases = new ValueList<string>(["Channel B"]) }, team));
        Assert.Contains("\"Channel C\" is listed twice.", Problems(Good() with { Aliases = new ValueList<string>(["Channel C"]) }, team));
        Assert.Contains("\"X\" is listed twice.", Problems(Good() with { Aliases = new ValueList<string>(["x", "X"]) }, team));
        Assert.Contains("\"dextra\" cannot be another name.", Problems(Good() with { Aliases = new ValueList<string>(["dextra"]) }, team));
        Assert.Contains("At most 6 other names.", Problems(Good() with { Aliases = new ValueList<string>(Enumerable.Range(0, 7).Select(n => "alias " + n)) }, team));
    }

    [Fact]
    public void There_are_at_most_six_Asturs_and_two_never_share_a_shape()
    {
        var six = Team([.. AsturShapes.All.Select((shape, n) => Astur("Astur " + n, shape))]);
        Assert.Contains("There can be at most 6 Asturs. Retire one first.", Problems(Good("Seventh", AsturShape.Star), six));
        var five = Team([.. AsturShapes.All.Take(5).Select((shape, n) => Astur("Astur " + n, shape))]);
        Assert.Empty(Problems(Good("Sixth", AsturShape.Star), five));
        Assert.Contains("Squircle belongs to Astur 0; pick another look.", Problems(Good("Sixth", AsturShape.Squircle), five));
        Assert.Contains("Pick a look.", Problems(Good() with { Shape = null }, five));
        // A retired Astur frees its place and its shape.
        var retired = Team([.. AsturShapes.All.Select((shape, n) => Astur("Astur " + n, shape, null, retired: n == 0))]);
        Assert.Empty(Problems(Good("Replacement", AsturShape.Squircle), retired));
    }

    [Fact]
    public void An_Astur_needs_an_area_and_the_lines_are_bounded()
    {
        Assert.Contains("Say what it looks after.", Problems(Good() with { Area = " " }));
        Assert.Contains("What it looks after holds at most 600 characters.", Problems(Good() with { Area = new string('a', 601) }));
        Assert.Contains("How it writes holds at most 300 characters.", Problems(Good() with { Style = new string('a', 301) }));
        Assert.Contains("At most 8 standing duties.", Problems(Good() with { Duties = new ValueList<DutyDraft>(Enumerable.Range(0, 9).Select(n => new DutyDraft("duty " + n))) }));
        Assert.Contains("A duty holds at most 200 characters.", Problems(Good() with { Duties = new ValueList<DutyDraft>([new DutyDraft(new string('a', 201))]) }));
        Assert.Contains("At most 8 connections.", Problems(Good() with { Wants = new ValueList<AsturWant>(Enumerable.Range(0, 9).Select(n => new AsturWant(WantKind.Site, "site " + n))) }));
        Assert.Empty(Problems(Good() with { Duties = new ValueList<DutyDraft>([new DutyDraft("  "), new DutyDraft("real")]) }));   // an empty line is dropped, not refused
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.5, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(100.5, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void The_daily_budget_is_between_one_and_a_hundred_percent(double percent, bool ok) =>
        Assert.Equal(ok, !Problems(Good() with { Budget = new(percent) }).Any(p => p.StartsWith("Its daily budget", StringComparison.Ordinal)));

    [Theory]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.01", true)]
    [InlineData("100000", true)]
    [InlineData("100001", false)]
    public void The_daily_dollar_limit_when_there_is_one_is_above_zero_and_bounded(string dollars, bool ok) =>
        Assert.Equal(ok, !Problems(Good() with { Budget = new(15, decimal.Parse(dollars, System.Globalization.CultureInfo.InvariantCulture)) }).Any(p => p.StartsWith("Its daily dollar", StringComparison.Ordinal)));

    [Fact]
    public void An_Astur_is_never_looser_than_Dextra_for_any_class()
    {
        var strictDextra = new AssistantProfile(DextraId, "Dextra")
        {
            ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.Never), KeyValuePair.Create(ActionClass.Login, ClassDefault.AsksEveryTime)]),
        };
        var team = new[] { strictDextra, Astur("Channel A", AsturShape.Squircle) };
        var looser = Good() with { ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.AlwaysConfirmed)]) };
        Assert.Contains("Channel C cannot be looser than its parent for Outward.", Problems(looser, team));
        var equalOrStricter = Good() with
        {
            ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.Never), KeyValuePair.Create(ActionClass.Login, ClassDefault.Never)]),
        };
        Assert.Empty(Problems(equalOrStricter, team));
        // Looser than the class's own default is looser too: Work runs alone by default, so "Alone" is no looser, and a choice below the default is caught.
        var belowDefault = Good() with { ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Money, ClassDefault.Alone)]) };
        Assert.NotEmpty(Problems(belowDefault, team));
    }

    [Fact]
    public void Without_Dextras_own_profile_nothing_can_be_made()
    {
        Assert.Contains(Problems(Good(), [Astur("Channel A", AsturShape.Squircle)]), p => p.Contains("profile is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_card_edited_from_an_existing_Astur_round_trips_and_a_name_made_unique_counts_up()
    {
        var team = Team(Astur("Channel", AsturShape.Squircle, "channel"), Astur("Channel 2", AsturShape.Hexagon, "channel-2"));
        Assert.Equal("Channel 3", AsturTeamRules.UniqueName("Channel", team));
        Assert.Equal("Fresh", AsturTeamRules.UniqueName("Fresh", team));
        Assert.Equal("Dextra 2", AsturTeamRules.UniqueName("Dextra", team));                 // a word nth listens for is never handed out
        var profile = AsturTeamRules.ToProfile(Good() with { Template = "inbox", Style = "brief" }, "c3", DextraId, Now);
        var draft = AsturTeamRules.ToDraft(profile);
        Assert.Equal(("Channel C", "brief", "inbox"), (draft.Name, draft.Style, draft.Template));
        Assert.Equal(AsturTemplates.Find("inbox")!.AsksYou, draft.AsksYou);
        Assert.Empty(AsturTeamRules.Problems(draft, Team(profile), editing: "c3"));
    }
}
