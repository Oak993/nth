using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1, 15.3; ED-NEW-dextra-as16-9): an assistant's profile as assistants/<id>/profile.json holds it. The format carries a version; the
// first release's file still loads; fields this build does not know survive a load and a save; a file from a newer version is read for nothing; a value of the wrong
// type is skipped; the id inside must be the folder's; text is one clean line cut to the card's limits.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-9")]
public sealed class AssistantProfileFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    private static AssistantProfile Full() => new("channel-c", "Channel C")
    {
        ParentId = "dextra-1",
        Aliases = new ValueList<string>(["the third one", "CC"]),
        Shape = AsturShape.Clover,
        Area = "A channel: scripts and numbers.",
        Style = "short and plain",
        Voice = "Microsoft Zira",
        HomeThread = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Template = "channel",
        Created = Now,
        SharesBrowserOf = "channel-a",
        ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Money, ClassDefault.Never), KeyValuePair.Create(ActionClass.Login, ClassDefault.AsksEveryTime)]),
        AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), "send"), new AskFirstRule(Field: "upload")]),
        Connections = new ValueList<string>(["youtube", "gmail"]),
        Skills = new ValueList<string>(["weekly-numbers"]),
        Duties = new ValueList<DutyDraft>([new("Watch the numbers, weekdays 09:00"), new("Prepare the next script", false)]),
        Wants = new ValueList<AsturWant>([new(WantKind.Site, "YouTube Studio · read · you sign in once", true), new(WantKind.Folder, "Its own folder")]),
        Budget = new AsturBudget(15, 2.5m),
        Model = new AsturModel("claude", "sonnet", Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
    };

    private static ProfileDocument Read(string folder, JsonObject document)
    {
        var read = AssistantProfileFile.Read(folder, document);
        Assert.Null(read.Problem);
        return read.Document!;
    }

    [Fact]
    public void A_profile_round_trips_through_its_file_exactly()
    {
        var profile = Full();
        var json = AssistantProfileFile.ToJson(profile);
        Assert.Equal(1, (int)json["version"]!);
        var back = Read("channel-c", json);
        Assert.Equal(profile, back.Profile);
        Assert.Empty(back.Extra);
        // Written again it is the same document.
        Assert.Equal(json.ToJsonString(), AssistantProfileFile.ToJson(back.Profile, back.Extra).ToJsonString());
        // And through the text form the store keeps.
        Assert.Equal(profile, Read("channel-c", (JsonObject)JsonNode.Parse(json.ToJsonString())!).Profile);
    }

    [Fact]
    public void The_first_releases_smallest_file_still_loads_with_defaults()
    {
        var old = new JsonObject { ["id"] = "channel-c", ["name"] = "Channel C", ["parent"] = "dextra-1", ["shape"] = "hexagon", ["area"] = "News" };
        var profile = Read("channel-c", old).Profile;
        Assert.Equal(("Channel C", "dextra-1", AsturShape.Hexagon, "News"), (profile.Name, profile.ParentId, profile.Shape, profile.Area));
        Assert.Empty(profile.Aliases);
        Assert.Empty(profile.Duties);
        Assert.Empty(profile.Wants);
        Assert.Null(profile.Budget);
        Assert.Null(profile.HomeThread);
        Assert.False(profile.Retired);
        Assert.Equal("", profile.Style);
    }

    [Fact]
    public void Fields_this_build_does_not_know_survive_a_load_and_a_save()
    {
        var json = AssistantProfileFile.ToJson(Full());
        json["futureField"] = new JsonObject { ["keep"] = new JsonArray(1, 2, 3) };
        json["another"] = "text";
        var back = Read("channel-c", json);
        Assert.Equal(["another", "futureField"], back.Extra.Select(p => p.Key).Order(StringComparer.Ordinal));
        var saved = AssistantProfileFile.ToJson(back.Profile with { Area = "Changed" }, back.Extra);
        Assert.Equal("text", (string)saved["another"]!);
        Assert.Equal(3, saved["futureField"]!["keep"]!.AsArray().Count);
        Assert.Equal("Changed", (string)saved["area"]!);
    }

    [Fact]
    public void A_file_of_a_newer_version_is_read_for_nothing()
    {
        var json = AssistantProfileFile.ToJson(Full());
        json["version"] = AssistantProfileFile.FormatVersion + 1;
        var read = AssistantProfileFile.Read("channel-c", json);
        Assert.Null(read.Document);
        Assert.True(read.Newer);
        Assert.Contains("newer nth", read.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void A_file_that_names_another_assistant_than_its_folder_is_ignored(string folder)
    {
        var read = AssistantProfileFile.Read(folder, AssistantProfileFile.ToJson(Full()));
        Assert.Null(read.Document);
        Assert.NotNull(read.Problem);
    }

    [Fact]
    public void A_file_with_no_name_a_bad_parent_or_no_object_gives_a_problem_never_a_throw()
    {
        var noName = AssistantProfileFile.ToJson(Full());
        noName["name"] = "  ";
        Assert.Contains("no name", AssistantProfileFile.Read("channel-c", noName).Problem, StringComparison.Ordinal);
        var selfParent = AssistantProfileFile.ToJson(Full());
        selfParent["parent"] = "channel-c";
        Assert.Contains("parent", AssistantProfileFile.Read("channel-c", selfParent).Problem, StringComparison.Ordinal);
        var badParent = AssistantProfileFile.ToJson(Full());
        badParent["parent"] = "..\\..\\Windows";
        Assert.Contains("parent", AssistantProfileFile.Read("channel-c", badParent).Problem, StringComparison.Ordinal);
        Assert.Contains("could not be read", AssistantProfileFile.Read("channel-c", null).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_of_the_wrong_type_are_skipped_not_repaired()
    {
        var json = new JsonObject
        {
            ["version"] = 1, ["id"] = "channel-c", ["name"] = "Channel C", ["parent"] = "dextra-1",
            ["aliases"] = 5, ["shape"] = 7, ["area"] = new JsonArray(), ["retired"] = "yes", ["homeThread"] = "not a guid", ["created"] = 12,
            ["budget"] = "lots", ["model"] = new JsonObject { ["provider"] = 3 }, ["duties"] = "all", ["wants"] = new JsonArray("x", 4),
            ["classRules"] = new JsonObject { ["Money"] = "Never", ["Nonsense"] = "Never", ["Login"] = "Sideways", ["Run"] = 3 },
            ["askFirst"] = new JsonArray(new JsonObject { ["class"] = "Nonsense" }, new JsonObject(), "x"),
        };
        var profile = Read("channel-c", json).Profile;
        Assert.Empty(profile.Aliases);
        Assert.Null(profile.Shape);
        Assert.Equal("", profile.Area);
        Assert.False(profile.Retired);
        Assert.Null(profile.HomeThread);
        Assert.Null(profile.Created);
        Assert.Null(profile.Budget);
        Assert.Null(profile.Model);
        Assert.Empty(profile.Duties);
        Assert.Empty(profile.Wants);
        Assert.Equal(new[] { ActionClass.Money }, profile.ClassRules.Keys.ToArray());                        // only an action type and a rule nth knows are kept
        Assert.Empty(profile.AskFirst);                                                    // a rule with nothing in it asks nothing
    }

    [Fact]
    public void Text_is_one_clean_line_cut_to_the_cards_limits_and_lists_are_bounded()
    {
        var json = AssistantProfileFile.ToJson(Full());
        json["name"] = "Chan​nel\n C";
        json["area"] = new string('a', 2000);
        json["style"] = "line one\nline two‮";
        json["aliases"] = new JsonArray([.. Enumerable.Range(0, 20).Select(n => (JsonNode)("alias " + n))]);
        json["duties"] = new JsonArray([.. Enumerable.Range(0, 20).Select(n => (JsonNode)new JsonObject { ["text"] = "duty " + n })]);
        json["budget"] = new JsonObject { ["dailyPercent"] = 900, ["dailyDollars"] = -5 };
        var profile = Read("channel-c", json).Profile;
        Assert.Equal("Channel C", profile.Name);
        Assert.True(profile.Area.Length <= AsturTeamRules.AreaMax);
        Assert.Equal("line one line two", profile.Style);
        Assert.Equal(AsturTeamRules.AliasesMax, profile.Aliases.Count);
        Assert.Equal(AsturTeamRules.DutiesMax, profile.Duties.Count);
        Assert.Equal(new AsturBudget(100), profile.Budget);                                // a percent past 100 is held to 100; a negative limit is no limit
    }

    [Fact]
    public void Dextras_own_profile_has_no_shape_and_no_slice_whatever_the_file_says()
    {
        var json = AssistantProfileFile.ToJson(new AssistantProfile("dextra-1", "Dextra") { Shape = AsturShape.Star, Budget = new AsturBudget(50), Created = Now });
        var profile = Read("dextra-1", json).Profile;
        Assert.True(profile.IsRoot);
        Assert.Null(profile.Shape);
        Assert.Null(profile.Budget);
        Assert.False(AssistantProfileFile.ToJson(profile).ContainsKey("parent"));
    }

    [Fact]
    public void Keys_written_only_when_set_keep_a_plain_profile_small()
    {
        var json = AssistantProfileFile.ToJson(new AssistantProfile("dextra-1", "Dextra"));
        foreach (var absent in new[] { "parent", "shape", "voice", "homeThread", "template", "created", "retired", "sharesBrowserOf", "budget", "model" })
            Assert.False(json.ContainsKey(absent), absent);
    }
}
