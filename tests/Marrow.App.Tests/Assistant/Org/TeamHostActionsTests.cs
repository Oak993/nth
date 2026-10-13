using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (the notes of AS-16: "The Team tab (AS-17) calls CreateByOwner, UpdateAstur, RetireAstur and the ProfileSheet in its Add and Edit modes"; DESIGN.md rev 5, 9.2, 9.4; ED-NEW-dextra-as17-6): what the Team
// tab's buttons do in the app, with the controller's calls and the home's sheet layer stood in for.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-6")]
public sealed class TeamHostActionsTests(HeadlessSession headless)
{
    private static IReadOnlyList<AssistantProfile> Profiles { get; } = [Dextra, ChannelA with { HomeThread = Guid.Parse("11111111-1111-1111-1111-111111111111") }, ChannelB];

    private static TeamActions Build(FakeTeamHost host, FakeAsturDesk? desk = null) => TeamHostActions.Build(() => Profiles, desk, host.Seams);

    [Fact]
    public void Talk_to_Dextra_shows_the_home_and_talk_to_an_Astur_opens_its_own_thread_else_says_it_has_none()
    {
        var host = new FakeTeamHost();
        host.Threads.Add(Profiles[1].HomeThread!.Value);
        var actions = Build(host);

        actions.Talk!("root");
        actions.Talk!("a");
        actions.Talk!("b");
        actions.Talk!("nobody");

        Assert.Equal(["Home", "Thread", "Said Channel B has no thread of its own yet."], host.Calls);
    }

    [Fact]
    public void An_Astur_whose_thread_is_gone_is_told_the_same_way()
    {
        var host = new FakeTeamHost();
        Build(host).Talk!("a");

        Assert.Equal(["Thread", "Said Channel A has no thread of its own yet."], host.Calls);
    }

    [Fact]
    public void Settings_of_Dextra_open_Settings_and_of_an_Astur_open_its_card_in_Edit_mode()
    {
        headless.OnUiThread(() =>
        {
            var host = new FakeTeamHost();
            var desk = new FakeAsturDesk();
            var actions = Build(host, desk);

            actions.Edit!("root");
            Assert.Equal(["Settings"], host.Calls);

            actions.Edit!("b");
            Assert.Equal("Team", host.SheetTitle);
            var sheet = Nodes<ProfileSheet>(host.Sheet!).Single();
            Assert.Contains("Edit Channel B", Texts(sheet));
            Assert.Equal("Channel B", sheet.NameBox!.Text);
            Assert.Equal("Save", Texts(sheet.CreateButton!).Single());
        });
    }

    [Fact]
    public void Saving_the_edited_card_updates_that_Astur_through_the_controller_and_closes_the_sheet()
    {
        headless.OnUiThread(() =>
        {
            var host = new FakeTeamHost();
            var desk = new FakeAsturDesk();
            Build(host, desk).Edit!("b");
            var sheet = Nodes<ProfileSheet>(host.Sheet!).Single();
            using var stage = new HomeStage(host.Sheet!, 500, 900);

            sheet.NameBox!.Text = "Channel Beta";
            stage.Layout();
            Click(sheet.CreateButton!);
            stage.Layout();

            Assert.Equal(["Update b Channel Beta"], desk.Calls);
            Assert.Contains("Close", host.Calls);
            Assert.Null(host.Sheet);
        });
    }

    [Fact]
    public void A_refused_save_stays_open_and_lists_what_the_controller_found()
    {
        headless.OnUiThread(() =>
        {
            var host = new FakeTeamHost();
            var desk = new FakeAsturDesk { UpdateAnswer = ["A name another assistant answers to."] };
            Build(host, desk).Edit!("b");
            var sheet = Nodes<ProfileSheet>(host.Sheet!).Single();
            using var stage = new HomeStage(host.Sheet!, 500, 900);

            Click(sheet.CreateButton!);
            stage.Layout();

            Assert.Equal(["Update b Channel B"], desk.Calls);
            Assert.NotNull(host.Sheet);
            Assert.Contains("A name another assistant answers to.", sheet.ProblemsText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Add_an_Astur_opens_the_card_in_Add_mode_with_the_standing_reserve_line_and_Create_goes_through_the_desk()
    {
        headless.OnUiThread(() =>
        {
            var host = new FakeTeamHost();
            var desk = new FakeAsturDesk();
            Build(host, desk).Add!();
            var sheet = Nodes<ProfileSheet>(host.Sheet!).Single();
            using var stage = new HomeStage(host.Sheet!, 500, 900);

            Assert.Contains("Add an Astur", Texts(sheet));
            Assert.Null(sheet.CreateButton);
            Click(sheet.TemplateButtons[0]);
            stage.Layout();
            Assert.Contains("Keeps 30% on Claude 20x (your standing rule).", Texts(sheet));
            Click(sheet.CreateButton!);
            stage.Layout();

            Assert.Single(desk.Calls);
            Assert.StartsWith("Create ", desk.Calls[0], StringComparison.Ordinal);
            Assert.Null(host.Sheet);
        });
    }

    [Fact]
    public void Without_a_desk_there_is_no_Add_and_no_Retire_and_an_Astur_has_no_card_to_edit()
    {
        var host = new FakeTeamHost();
        var actions = Build(host);

        Assert.Null(actions.Add);
        Assert.Null(actions.Retire);
        actions.Edit!("b");
        Assert.Empty(host.Calls);
        Assert.NotNull(actions.Talk);
    }

    [Fact]
    public async Task Retire_asks_the_desk_and_gives_back_its_sentence()
    {
        var host = new FakeTeamHost();
        var desk = new FakeAsturDesk { RetireAnswer = "It has missions at work. Stop them first." };
        var actions = Build(host, desk);

        Assert.Equal("It has missions at work. Stop them first.", await actions.Retire!("b"));
        desk.RetireAnswer = null;
        Assert.Null(await actions.Retire!("b"));
        Assert.Equal(["Retire b", "Retire b"], desk.Calls);
    }

    [Fact]
    public void A_chip_opens_its_lead_chat_and_a_chat_that_is_gone_is_told()
    {
        var host = new FakeTeamHost();
        var there = Guid.NewGuid();
        host.Threads.Add(there);
        var actions = Build(host);

        actions.OpenChat!(there);
        actions.OpenChat!(Guid.NewGuid());

        Assert.Equal(["Thread", "Thread", "Said That chat is not here any more."], host.Calls);
    }
}
