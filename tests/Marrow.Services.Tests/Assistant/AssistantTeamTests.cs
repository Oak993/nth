using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.Services.Tests.Fixtures.AsturTeamRig;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1, 9.2, 15.3; ED-NEW-dextra-as16-10, -11): Dextra and its Asturs in the store. Every change is checked against the team's rules
// first, written off the caller's thread, and only then shown; one change runs at a time; an Astur's day budget is a standing cap set before its profile is written
// and taken back if the write fails; a file nth cannot use is left alone and named.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-10")]
public sealed class AssistantTeamTests : IDisposable
{
    private readonly AsturTeamRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public async Task Dextras_own_profile_is_made_once_and_is_still_there_at_the_next_start()
    {
        Assert.Empty(rig.Team.Profiles);
        var root = await rig.RootAsync();
        Assert.Equal("Dextra", root.Name);
        Assert.True(root.IsRoot);
        Assert.Equal(root, (await rig.Team.EnsureRootAsync()).Profile);                        // idempotent: no second Dextra
        Assert.Single(rig.Team.Profiles);
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "assistants", root.Id, "folder")));
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "memory", root.Id)));
        var again = rig.Open();
        Assert.Equal(root, again.Root);
        Assert.Empty(again.Problems);
    }

    [Fact]
    public async Task Creating_an_Astur_writes_its_profile_its_three_folders_and_its_day_slice_and_tells_listeners_once()
    {
        await rig.RootAsync();
        var changes = 0;
        rig.Team.Changed += () => changes++;
        var made = await rig.Team.CreateAsync(Draft("Channel C", AsturShape.Diamond) with { Budget = new(20, 5m), Template = "channel" });

        Assert.True(made.Ok);
        var astur = made.Profile!;
        Assert.Equal(("a2", "Channel C", rig.Team.Root!.Id), (astur.Id, astur.Name, astur.ParentId));
        Assert.Equal(rig.Clock.GetUtcNow(), astur.Created);
        Assert.Equal(1, changes);
        Assert.Contains(astur, rig.Team.Profiles);
        Assert.Contains(astur, rig.Team.Active);
        Assert.True(File.Exists(rig.ProfilePath("a2")));
        foreach (var folder in new[] { Path.Combine("assistants", "a2", "folder"), Path.Combine("assistants", "a2", "browser"), Path.Combine("memory", "a2") })
            Assert.True(Directory.Exists(Path.Combine(rig.Root, folder)), folder);
        // The day slice is a standing per-assistant cap (13.2), which the cost guard enforces.
        var cap = Assert.Single(rig.Budgets.Caps);
        Assert.Equal((AssistantTeam.BudgetCapId("a2"), CostCapScope.AssistantDay, "a2", 20.0, 5m), (cap.Id, cap.Scope, cap.AssistantId, cap.Percent, cap.Dollars));
        // It is what the next start reads.
        Assert.Equal(astur, rig.Open().Get("a2"));
    }

    [Fact]
    public async Task A_refused_card_changes_nothing_no_file_no_folder_no_slice_no_event()
    {
        await rig.RootAsync();
        await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle));
        var changes = 0;
        rig.Team.Changed += () => changes++;
        var before = rig.Team.Profiles;

        foreach (var bad in new[]
        {
            Draft("channel a", AsturShape.Hexagon),                                   // the name is taken
            Draft("Other", AsturShape.Squircle),                                      // the look is taken
            Draft("Other", AsturShape.Hexagon) with { Budget = new(0) },              // the budget means nothing
            Draft("Other", AsturShape.Hexagon) with { Area = "" },
            Draft("Dextra", AsturShape.Hexagon),
        })
        {
            var refused = await rig.Team.CreateAsync(bad);
            Assert.False(refused.Ok);
            Assert.NotNull(refused.Problem);
        }
        Assert.Equal(0, changes);
        Assert.Equal(before, rig.Team.Profiles);
        Assert.Single(rig.Budgets.Caps);
        Assert.Equal(["a2"], rig.Store.AssistantIds().Where(id => id != rig.Team.Root!.Id));
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "assistants", "a3")));
    }

    [Fact]
    public async Task Two_taps_at_once_cannot_make_a_seventh_Astur_or_two_of_one_name()
    {
        await rig.RootAsync();
        var asturs = await Task.WhenAll(AsturShapes.All.Concat(AsturShapes.All.Take(2)).Select((shape, n) =>
            Task.Run(() => rig.Team.CreateAsync(Draft("Astur " + n, shape)))));
        // Eight cards want six places, and only six shapes exist: exactly six are made.
        Assert.Equal(6, asturs.Count(a => a.Ok));
        Assert.Equal(6, AsturTeamRules.ActiveAsturs(rig.Team.Profiles).Count());
        Assert.Equal(6, rig.Budgets.Caps.Count);
        Assert.Equal(6, rig.Team.Profiles.Where(p => !p.IsRoot).Select(p => p.Shape).Distinct().Count());

        var same = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => rig.Team.CreateAsync(Draft("Twin", AsturShape.Star)))));
        Assert.DoesNotContain(same, r => r.Ok);                                       // all six places are taken
        var first = rig.Team.Profiles.First(p => !p.IsRoot);
        await rig.Team.RetireAsync(first.Id);
        var twins = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => rig.Team.CreateAsync(Draft("Twin", first.Shape!.Value)))));
        Assert.Single(twins, r => r.Ok);                                              // one place, one name, one look: one Astur
    }

    [Fact]
    public async Task Creating_with_no_Dextra_profile_is_refused()
    {
        var refused = await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle));
        Assert.False(refused.Ok);
        Assert.Contains("profile is missing", refused.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_profile_that_cannot_be_written_makes_no_Astur_and_gives_the_slice_back()
    {
        await rig.RootAsync();
        // A directory where the profile file belongs: the atomic move fails.
        Directory.CreateDirectory(rig.ProfilePath("a2"));
        var refused = await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle));
        Assert.False(refused.Ok);
        Assert.Contains("could not be saved", refused.Problem, StringComparison.Ordinal);
        Assert.Empty(rig.Budgets.Caps);
        Assert.Single(rig.Team.Profiles);
        Assert.Null(rig.Team.Find("Channel A"));
    }

    [Fact]
    public async Task A_slice_that_cannot_be_set_stops_the_creation_before_anything_is_written()
    {
        await rig.RootAsync();
        rig.Budgets.Load(new JsonObject { ["version"] = ReserveSettings.FormatVersion + 1 });         // a newer version's file: read-only
        var refused = await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle));
        Assert.False(refused.Ok);
        Assert.Contains("daily budget could not be set", refused.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(rig.ProfilePath("a2")));
        Assert.Single(rig.Team.Profiles);
    }

    [Fact]
    public async Task An_id_that_is_not_safe_is_never_used_as_a_folder()
    {
        rig.Open(newId: () => "..\\..\\evil");
        var refused = await rig.Team.EnsureRootAsync();                               // Dextra's own id is the unsafe one: refused, nothing written
        Assert.False(refused.Ok);
        Assert.Empty(rig.Team.Profiles);
        Assert.False(Directory.Exists(Path.GetFullPath(Path.Combine(rig.Root, "..", "evil"))));
    }

    [Fact]
    public async Task Editing_a_card_saves_the_change_keeps_what_is_not_on_it_and_swaps_the_slice()
    {
        var root = await rig.RootAsync();
        var created = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        await rig.Team.SetHomeThreadAsync(created.Id, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var edited = AsturTeamRules.ToDraft(rig.Team.Get(created.Id)!) with { Name = "The News Channel", Aliases = new ValueList<string>(["news"]), Budget = new(30, 12m), Style = "brief" };
        var result = await rig.Team.UpdateAsync(created.Id, edited);

        Assert.True(result.Ok);
        var now = result.Profile!;
        Assert.Equal(("The News Channel", "brief"), (now.Name, now.Style));
        Assert.Equal(["news"], now.Aliases);
        Assert.Equal((created.Id, root.Id, created.Created), (now.Id, now.ParentId, now.Created));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), now.HomeThread);
        var cap = Assert.Single(rig.Budgets.Caps);                                               // replaced, not added
        Assert.Equal((30.0, 12m), (cap.Percent, cap.Dollars));
        Assert.Equal(now, rig.Open().Get(created.Id));
    }

    [Fact]
    public async Task An_edit_that_breaks_a_rule_or_names_nobody_is_refused_and_nothing_moves()
    {
        await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        var b = (await rig.Team.CreateAsync(Draft("Channel B", AsturShape.Hexagon))).Profile!;
        Assert.Contains("already answers", (await rig.Team.UpdateAsync(b.Id, AsturTeamRules.ToDraft(b) with { Name = "channel a" })).Problem, StringComparison.Ordinal);
        Assert.Contains("belongs to", (await rig.Team.UpdateAsync(b.Id, AsturTeamRules.ToDraft(b) with { Shape = AsturShape.Squircle })).Problem, StringComparison.Ordinal);
        Assert.Equal("That Astur is not on the team.", (await rig.Team.UpdateAsync("nobody", AsturTeamRules.ToDraft(b))).Problem);
        Assert.Equal("That Astur is not on the team.", (await rig.Team.UpdateAsync(rig.Team.Root!.Id, AsturTeamRules.ToDraft(b))).Problem);   // Dextra is not edited as an Astur
        Assert.Equal(b, rig.Team.Get(b.Id));
        Assert.Equal(a, rig.Team.Get(a.Id));
        Assert.Equal(2, rig.Budgets.Caps.Count);
    }

    [Fact]
    public async Task A_loosened_rule_is_refused_when_Dextra_is_stricter()
    {
        await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        var loose = AsturTeamRules.ToDraft(a) with { ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.Alone)]) };
        Assert.Contains("looser than its parent", (await rig.Team.UpdateAsync(a.Id, loose)).Problem, StringComparison.Ordinal);
        var strict = AsturTeamRules.ToDraft(a) with { ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.Never)]) };
        Assert.True((await rig.Team.UpdateAsync(a.Id, strict)).Ok);
    }

    [Fact]
    public async Task Retiring_an_Astur_unlists_it_frees_its_name_shape_and_slice_and_keeps_its_folders()
    {
        await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        var memory = Path.Combine(rig.Root, "memory", a.Id);
        File.WriteAllText(Path.Combine(memory, "note.md"), "kept");
        var retired = await rig.Team.RetireAsync(a.Id);

        Assert.True(retired.Ok);
        Assert.True(retired.Profile!.Retired);
        Assert.DoesNotContain(rig.Team.Active, p => p.Id == a.Id);
        Assert.Contains(rig.Team.Profiles, p => p.Id == a.Id && p.Retired);                      // a mission of it still has its chain
        Assert.Null(rig.Team.Find("Channel A"));
        Assert.Empty(rig.Budgets.Caps);
        Assert.True(File.Exists(Path.Combine(memory, "note.md")));                                // nothing is deleted
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "assistants", a.Id, "folder")));
        Assert.True((await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Ok);   // its name and look can be used again
        Assert.True((await rig.Team.RetireAsync(a.Id)).Ok);                                      // retiring twice is quiet
        Assert.Equal("Dextra cannot be retired.", (await rig.Team.RetireAsync(rig.Team.Root!.Id)).Problem);
        Assert.Equal("That Astur is not on the team.", (await rig.Team.RetireAsync("nobody")).Problem);
        Assert.True(rig.Open().Get(a.Id)!.Retired);
    }

    [Fact]
    public async Task Sharing_a_browser_profile_is_the_owners_choice_one_step_and_undone_by_a_retirement()
    {
        await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        var b = (await rig.Team.CreateAsync(Draft("Channel B", AsturShape.Hexagon))).Profile!;
        var c = (await rig.Team.CreateAsync(Draft("Channel C", AsturShape.Diamond))).Profile!;
        Assert.All(rig.Team.Profiles, p => Assert.Null(p.SharesBrowserOf));                       // nothing is shared until he says so

        Assert.True((await rig.Team.ShareBrowserAsync(b.Id, a.Id)).Ok);
        Assert.Equal(a.Id, AsturIsolation.BrowserProfileOwner(rig.Team.Get(b.Id)!, rig.Team.Profiles));
        Assert.Contains("cannot lend it on", (await rig.Team.ShareBrowserAsync(c.Id, b.Id)).Problem, StringComparison.Ordinal);                // B borrows, so B cannot lend
        Assert.Contains("used by another assistant", (await rig.Team.ShareBrowserAsync(a.Id, c.Id)).Problem, StringComparison.Ordinal);          // B uses A's, so A cannot borrow
        Assert.Contains("own browser profile", (await rig.Team.ShareBrowserAsync(c.Id, c.Id)).Problem, StringComparison.Ordinal);
        Assert.Equal("That assistant is not on the team.", (await rig.Team.ShareBrowserAsync(c.Id, "nobody")).Problem);

        await rig.Team.RetireAsync(a.Id);                                                         // the lender goes: the borrower is back on its own profile
        Assert.Null(rig.Team.Get(b.Id)!.SharesBrowserOf);
        Assert.Null(rig.Open().Get(b.Id)!.SharesBrowserOf);
        Assert.True((await rig.Team.ShareBrowserAsync(b.Id, c.Id)).Ok);
        Assert.True((await rig.Team.ShareBrowserAsync(b.Id, null)).Ok);                           // and back to its own
        Assert.Null(rig.Team.Get(b.Id)!.SharesBrowserOf);
    }

    [Fact]
    public async Task The_home_thread_is_recorded_once_and_the_first_one_is_kept()
    {
        await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        var first = Guid.NewGuid();
        Assert.Equal(first, (await rig.Team.SetHomeThreadAsync(a.Id, first)).Profile!.HomeThread);
        Assert.Equal(first, (await rig.Team.SetHomeThreadAsync(a.Id, Guid.NewGuid())).Profile!.HomeThread);
        Assert.Equal(first, rig.Open().Get(a.Id)!.HomeThread);
        Assert.False((await rig.Team.SetHomeThreadAsync("nobody", first)).Ok);
    }

    [Fact]
    public async Task An_Astur_is_found_by_its_id_its_name_or_another_name_it_answers_to_and_only_when_the_name_is_one()
    {
        await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle) with { Aliases = new ValueList<string>(["The News Desk"]) })).Profile!;
        await rig.Team.CreateAsync(Draft("Channel B", AsturShape.Hexagon));
        Assert.Equal(a, rig.Team.Find(a.Id));
        Assert.Equal(a, rig.Team.Find("channel  a"));
        Assert.Equal(a, rig.Team.Find("the news desk"));
        Assert.Null(rig.Team.Find("Channel"));                                                    // not a whole name: not guessed
        Assert.Null(rig.Team.Find(""));
        Assert.Null(rig.Team.Find(null));
        Assert.Equal("Dextra", rig.Team.Find("dextra")!.Name);
    }

    [Fact]
    public async Task A_cancelled_change_throws_and_changes_nothing()
    {
        await rig.RootAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle), cancelled.Token));
        Assert.Single(rig.Team.Profiles);
        Assert.Empty(rig.Budgets.Caps);
    }

    [Fact]
    public async Task Fields_nth_does_not_know_and_a_newer_files_bytes_are_left_as_they_were()
    {
        var root = await rig.RootAsync();
        var a = (await rig.Team.CreateAsync(Draft("Channel A", AsturShape.Squircle))).Profile!;
        // A field a later nth wrote into Channel A's file survives an edit.
        var text = JsonNode.Parse(File.ReadAllText(rig.ProfilePath(a.Id)))!.AsObject();
        text["futureField"] = new JsonObject { ["x"] = 1 };
        rig.Store.SaveProfile(a.Id, text);
        var reopened = rig.Open();
        await reopened.UpdateAsync(a.Id, AsturTeamRules.ToDraft(reopened.Get(a.Id)!) with { Style = "edited" });
        Assert.Equal(1, (int)JsonNode.Parse(File.ReadAllText(rig.ProfilePath(a.Id)))!["futureField"]!["x"]!);

        // A profile of a newer version is not loaded, is named, and is not touched by what is done to the others.
        var newer = AssistantProfileFile.ToJson(new AssistantProfile("zz-newer", "Newer") { ParentId = root.Id, Shape = AsturShape.Star, Area = "x" });
        newer["version"] = 9;
        rig.Store.SaveProfile("zz-newer", newer);
        var bytes = File.ReadAllBytes(rig.ProfilePath("zz-newer"));
        var again = rig.Open();
        Assert.Null(again.Get("zz-newer"));
        Assert.Contains(again.Problems, p => p.Contains("newer nth", StringComparison.Ordinal));
        await again.UpdateAsync(a.Id, AsturTeamRules.ToDraft(again.Get(a.Id)!) with { Style = "again" });
        Assert.True((await again.CreateAsync(Draft("Channel B", AsturShape.Hexagon))).Ok);
        Assert.Equal(bytes, File.ReadAllBytes(rig.ProfilePath("zz-newer")));
    }

    [Fact]
    public async Task A_planted_file_cannot_speak_for_another_assistant_or_make_a_second_Dextra_or_an_orphan_Astur()
    {
        var root = await rig.RootAsync();
        // The id inside is not the folder's.
        var forged = AssistantProfileFile.ToJson(new AssistantProfile("someone-else", "Forged") { ParentId = root.Id, Shape = AsturShape.Star, Area = "x" });
        rig.Store.SaveProfile("planted-1", forged);
        // A second root.
        rig.Store.SaveProfile("planted-2", AssistantProfileFile.ToJson(new AssistantProfile("planted-2", "Second Dextra") { Created = rig.Clock.GetUtcNow().AddDays(1) }));
        // An Astur of a parent nth does not know.
        rig.Store.SaveProfile("planted-3", AssistantProfileFile.ToJson(new AssistantProfile("planted-3", "Orphan") { ParentId = "nobody", Shape = AsturShape.Star, Area = "x" }));
        var team = rig.Open();
        Assert.Equal([root.Id], team.Profiles.Select(p => p.Id));
        Assert.Equal(3, team.Problems.Count);
        Assert.Contains(team.Problems, p => p.Contains("names another assistant", StringComparison.Ordinal));
        Assert.Contains(team.Problems, p => p.Contains("second Dextra", StringComparison.Ordinal));
        Assert.Contains(team.Problems, p => p.Contains("Orphan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Asturs_are_listed_in_the_order_they_were_made_after_Dextra()
    {
        await rig.RootAsync();
        foreach (var (name, shape) in new[] { ("Zed", AsturShape.Squircle), ("Amy", AsturShape.Hexagon), ("Mia", AsturShape.Diamond) })
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(1));
            await rig.Team.CreateAsync(Draft(name, shape));
        }
        Assert.Equal(["Dextra", "Zed", "Amy", "Mia"], rig.Team.Profiles.Select(p => p.Name));
        Assert.Equal(["Dextra", "Zed", "Amy", "Mia"], rig.Open().Profiles.Select(p => p.Name));
    }
}
