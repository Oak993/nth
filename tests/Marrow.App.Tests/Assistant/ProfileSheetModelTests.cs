using Marrow.App.Assistant.Views;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.2; the approved canvas "Create an Astur"; ED-NEW-dextra-as16-15): what the profile sheet shows and does, apart from how it
// looks. A card from a sentence or a template, the problems the team's rules find as the owner types, the six looks (none that another Astur has), Create only when
// nothing is wrong, and what the host says after it.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-15")]
public sealed class ProfileSheetModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static AssistantProfile Dextra() => new("dextra", "Dextra") { Created = Now };

    private static AssistantProfile Astur(string name, AsturShape shape, string id) =>
        new(id, name) { ParentId = "dextra", Shape = shape, Area = "Looks after something.", Created = Now };

    private sealed class Host
    {
        public List<AssistantProfile> Team { get; } = [Dextra(), Astur("Channel A", AsturShape.Squircle, "a")];

        public List<AsturDraft> Created { get; } = [];

        public int Cancelled { get; set; }

        public IReadOnlyList<string> Answer { get; set; } = [];

        public Func<AsturDraft, Task<IReadOnlyList<string>>>? Slow { get; set; }

        public ProfileSheetModel Sheet(ProfileSheetMode mode = ProfileSheetMode.Add, AsturDraft? draft = null, string? editing = null, string reserve = "") =>
            new(mode, () => Team, async made =>
            {
                Created.Add(made);
                return Slow is null ? Answer : await Slow(made);
            }, () => Cancelled++, draft, editing, reserve);
    }

    [Fact]
    public void An_Add_sheet_starts_with_no_card_and_a_sentence_or_a_template_makes_one()
    {
        var host = new Host();
        var sheet = host.Sheet();
        var changes = 0;
        sheet.Changed += () => changes++;
        Assert.Null(sheet.Draft);
        Assert.Empty(sheet.Problems);
        Assert.False(sheet.CanCreate);
        Assert.Equal(("Add an Astur", "Create"), (sheet.Title, sheet.CreateTitle));
        Assert.Contains("nothing is made until you create it", sheet.Subtitle, StringComparison.Ordinal);

        sheet.Sentence = "Make an assistant that runs my online store";
        sheet.Draw();
        Assert.Equal("store", sheet.Draft!.Template);
        Assert.Equal("Store", sheet.Draft.Name);
        Assert.Equal("Drafted from your words: the Store template.", sheet.Status);
        Assert.True(sheet.CanCreate);

        sheet.UseTemplate("finance");
        Assert.Equal(("Ledger", "finance"), (sheet.Draft.Name, sheet.Draft.Template));
        Assert.Equal("Make a finance watcher that only reads", sheet.Sentence);                          // the box takes the template's sentence
        Assert.Equal("The Finance watcher (read-only) template.", sheet.Status);
        Assert.Equal(2, changes);

        sheet.Sentence = "Keep an eye on the garden";
        sheet.Draw();
        Assert.Null(sheet.Draft.Template);
        Assert.Equal("Drafted a blank card from your words.", sheet.Status);
        sheet.UseTemplate("nonsense");                                                                    // an unknown template changes nothing
        Assert.Null(sheet.Draft.Template);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void A_sheet_that_is_not_for_adding_has_its_card_from_the_start_and_ignores_sentences_and_templates()
    {
        var host = new Host();
        var draft = AsturDrafter.FromTemplate("channel", host.Team);
        var review = host.Sheet(ProfileSheetMode.Review, draft);
        Assert.Equal(("New Astur", "Create"), (review.Title, review.CreateTitle));
        Assert.Contains("Dextra drafted this profile", review.Subtitle, StringComparison.Ordinal);
        review.UseTemplate("store");
        review.Sentence = "x";
        review.Draw();
        Assert.Equal("channel", review.Draft!.Template);
        var edit = host.Sheet(ProfileSheetMode.Edit, AsturTeamRules.ToDraft(host.Team[1]), "a");
        Assert.Equal(("Edit Channel A", "Save"), (edit.Title, edit.CreateTitle));
        Assert.Throws<ArgumentException>(() => host.Sheet(ProfileSheetMode.Review));
        Assert.Throws<ArgumentException>(() => host.Sheet(ProfileSheetMode.Edit));
    }

    [Fact]
    public void The_problems_are_listed_as_the_owner_types_and_Create_is_there_only_when_there_are_none()
    {
        var host = new Host();
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        var edits = 0;
        sheet.Edited += () => edits++;
        var changes = 0;
        sheet.Changed += () => changes++;

        sheet.SetName("");
        Assert.Equal(["Give it a name."], sheet.Problems);
        Assert.False(sheet.CanCreate);
        sheet.SetName("Dextra");
        Assert.Equal(["\"Dextra\" is a word nth listens for; pick another name."], sheet.Problems);
        sheet.SetName("channel a");
        Assert.Equal(["Channel A already answers to \"channel a\"."], sheet.Problems);
        sheet.SetName("Channel B");
        Assert.Empty(sheet.Problems);
        Assert.True(sheet.CanCreate);
        sheet.SetArea("");
        Assert.Equal(["Say what it looks after."], sheet.Problems);
        sheet.SetArea("The second channel.");
        sheet.SetStyle(new string('x', AsturTeamRules.StyleMax + 1));
        Assert.Equal(["How it writes holds at most 300 characters."], sheet.Problems);
        sheet.SetStyle("short and plain");
        Assert.Empty(sheet.Problems);
        Assert.Equal(8, edits);
        Assert.Equal(0, changes);                                                                         // typing never rebuilds the sheet: the caret stays
    }

    [Fact]
    public void The_problems_follow_the_team_while_the_sheet_is_open()
    {
        var host = new Host();
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        sheet.SetName("Channel C");
        Assert.True(sheet.CanCreate);
        host.Team.Add(Astur("Channel C", AsturShape.Diamond, "c"));                                       // another tap made one meanwhile
        Assert.Equal(["Channel C already answers to \"Channel C\"."], sheet.Problems);
        Assert.False(sheet.CanCreate);
    }

    [Fact]
    public void The_six_looks_show_who_has_each_and_a_taken_one_cannot_be_picked()
    {
        var host = new Host();
        host.Team.Add(Astur("Channel B", AsturShape.Hexagon, "b"));
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        Assert.Equal(AsturShape.Diamond, sheet.Draft!.Shape);                                              // the first one nobody has
        var looks = sheet.Shapes;
        Assert.Equal(["Squircle", "Hexagon", "Diamond", "Rounded triangle", "Clover", "Star"], looks.Select(l => l.Title));
        Assert.Equal(["Channel A", "Channel B", null, null, null, null], looks.Select(l => l.TakenBy));
        Assert.Equal([false, false, true, false, false, false], looks.Select(l => l.Selected));
        Assert.Equal([false, false, true, true, true, true], looks.Select(l => l.Available));

        sheet.SetShape(AsturShape.Squircle);
        Assert.Equal(AsturShape.Diamond, sheet.Draft.Shape);
        sheet.SetShape(AsturShape.Star);
        Assert.Equal(AsturShape.Star, sheet.Draft.Shape);
        Assert.Equal("Star picked.", sheet.Status);
        Assert.Single(sheet.Shapes, l => l.Selected);
    }

    [Fact]
    public void An_Astur_being_edited_may_keep_its_own_name_and_look()
    {
        var host = new Host();
        var sheet = host.Sheet(ProfileSheetMode.Edit, AsturTeamRules.ToDraft(host.Team[1]), "a");
        Assert.Empty(sheet.Problems);
        Assert.Equal(AsturShape.Squircle, sheet.Shapes.Single(l => l.Selected).Shape);
        Assert.Null(sheet.Shapes.Single(l => l.Selected).TakenBy);
        Assert.True(sheet.CanCreate);
    }

    [Fact]
    public void Duties_are_ticked_and_unticked_and_the_words_say_which()
    {
        var host = new Host();
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        sheet.ToggleDuty(1);
        Assert.Equal([true, false, true], sheet.Draft!.Duties.Select(d => d.Keep));
        Assert.Equal("Not kept: Keep a backlog of ideas from comments", sheet.Status);
        sheet.ToggleDuty(1);
        Assert.Equal("Kept: Keep a backlog of ideas from comments", sheet.Status);
        sheet.ToggleDuty(7);
        sheet.ToggleDuty(-1);
        Assert.Equal([true, true, true], sheet.Draft.Duties.Select(d => d.Keep));
    }

    [Theory]
    [InlineData("20", "5", true, 20.0, 5.0)]
    [InlineData("12.5", "", true, 12.5, null)]
    [InlineData(" 7 ", " 0.5 ", true, 7.0, 0.5)]
    [InlineData("0", "", false, 0.0, null)]
    [InlineData("101", "", false, 101.0, null)]
    [InlineData("abc", "", false, 15.0, null)]
    [InlineData("15", "five", false, 15.0, null)]
    [InlineData("15", "-3", false, 15.0, -3.0)]
    public void The_budget_boxes_take_numbers_and_say_so_when_they_are_not_numbers(string percent, string dollars, bool ok, double expectedPercent, double? expectedDollars)
    {
        var host = new Host();
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        sheet.SetBudget(percent, dollars);
        Assert.Equal(expectedPercent, sheet.Draft!.Budget.DailyPercent);
        Assert.Equal(expectedDollars is null ? null : (decimal?)(decimal)expectedDollars, sheet.Draft.Budget.DailyDollars);
        Assert.Equal(ok, sheet.Problems.Count == 0);
        if (percent == "abc") Assert.Equal("Type the daily percent as a number, like 15.", sheet.Problems[0]);
        if (dollars == "five") Assert.Equal("Type the daily dollars as a number, like 5, or leave it empty.", sheet.Problems[0]);
        sheet.SetBudget("15", "");
        Assert.Empty(sheet.Problems);
    }

    [Fact]
    public async Task Create_hands_the_finished_card_to_the_host_and_is_done_when_the_host_has_nothing_to_add()
    {
        var host = new Host();
        var sheet = host.Sheet();
        sheet.UseTemplate("inbox");
        sheet.SetName("  The Inbox ");
        Assert.True(await sheet.Create());

        var made = Assert.Single(host.Created);
        Assert.Equal("inbox", made.Template);
        Assert.True(sheet.Done);
        Assert.False(sheet.CanCreate);                                                                    // once is enough
        Assert.Equal("Created The Inbox.", sheet.Status);
        Assert.False(await sheet.Create());
        Assert.Single(host.Created);
    }

    [Fact]
    public async Task What_the_host_still_finds_wrong_comes_back_to_the_sheet_and_the_card_stays_open()
    {
        var host = new Host { Answer = ["There can be at most 6 Asturs. Retire one first."] };
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        Assert.False(await sheet.Create());
        Assert.False(sheet.Done);
        Assert.Equal(["There can be at most 6 Asturs. Retire one first."], sheet.Problems);
        Assert.False(sheet.CanCreate);
        sheet.SetName("Another");                                                                         // an edit clears what the host said; it asks again on Create
        Assert.Empty(sheet.Problems);
        Assert.True(sheet.CanCreate);
        host.Answer = [];
        Assert.True(await sheet.Create());
        Assert.Equal(2, host.Created.Count);
    }

    [Fact]
    public async Task A_host_that_throws_is_told_to_the_owner_not_to_the_app()
    {
        var host = new Host { Slow = _ => throw new IOException("The disk is full.") };
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        Assert.False(await sheet.Create());
        Assert.Equal(["It could not be saved: The disk is full."], sheet.Problems);
        Assert.False(sheet.Busy);
    }

    [Fact]
    public async Task A_second_tap_while_the_first_is_still_working_is_ignored()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        var host = new Host { Slow = _ => gate.Task };
        var sheet = host.Sheet();
        sheet.UseTemplate("channel");
        var first = sheet.Create();
        Assert.True(sheet.Busy);
        Assert.False(sheet.CanCreate);
        Assert.False(await sheet.Create());
        Assert.Single(host.Created);
        gate.SetResult([]);
        Assert.True(await first);
        Assert.False(sheet.Busy);
    }

    [Fact]
    public void Cancel_tells_the_host_and_the_reserve_line_and_the_isolation_note_are_the_words_of_the_canvas()
    {
        var host = new Host();
        var sheet = host.Sheet(reserve: "Keeps 30% on Claude 20x (your standing rule).");
        sheet.Cancel();
        Assert.Equal(1, host.Cancelled);
        Assert.Equal("Keeps 30% on Claude 20x (your standing rule).", sheet.ReserveLine);
        Assert.Equal("Own browser profile and folder. Logins never shared.", ProfileSheetModel.Isolation);
        Assert.Equal(6, ProfileSheetModel.Templates.Count);
    }
}
