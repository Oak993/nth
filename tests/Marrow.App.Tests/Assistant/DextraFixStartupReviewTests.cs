using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-1")]
[Trait("Decision", "ED-NEW-dextra-fix-2")]
public sealed class DextraFixStartupReviewTests
{
    [Fact]
    public void A_first_foreground_launch_can_keep_working_on_close()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        Assert.Null(launch.Run(StartupMode.Normal, out _));
        Assert.NotNull(launch.OnMain(() => launch.Model.AssistantHost.EnsureStarted()));
        Assert.Null(launch.Model.Duties!.Switches.SetKeepWorking(true));
        Assert.True(launch.Model.Duties.Switches.KeepWorkingWhenClosed && launch.Model.Tray is not null);
        Assert.Same(launch.Tray, launch.Model.Tray);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_disabled_hidden_launch_keeps_an_open_affordance_without_starting_Dextra(bool on, bool done)
    {
        using var temp = new AssistantTempRoot();
        DextraFixStartupTests.DutyFile(temp);
        using var launch = new BackgroundLaunchRig(temp);
        launch.OnMain(() => launch.Model.AssistantHost.Settings.Value = launch.Model.AssistantHost.Settings.Value with { On = on, FirstRunDone = done });
        Assert.Null(launch.Run(new StartupMode(true, null, null), out _));
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
        Assert.Same(launch.Tray, launch.Model.Tray);
        Assert.True(launch.Tray.Visible);
        Assert.Contains(launch.Tray.Model!.Groups.SelectMany(g => g.Items), i => i.Id == Marrow.App.Tray.AppTray.OpenId);
        Assert.False(temp.Keys.HasKey());
    }

    [Fact]
    public void A_saved_Astur_duty_retains_its_profile_and_parent_for_policy_evaluation()
    {
        using var temp = new AssistantTempRoot();
        const string parent = "22222222000040008000000000000002";
        using var launch = new BackgroundLaunchRig(temp, Guid.ParseExact(parent, "N"));
        const string astur = "11111111000040008000000000000001";
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveProfile(parent, AssistantProfileFile.ToJson(new AssistantProfile(parent, "Dextra")));
            store.SaveProfile(astur, AssistantProfileFile.ToJson(new AssistantProfile(astur, "Research") { ParentId = parent }));
            store.SaveDuties(astur, DutyJson.Write(new DutyDocument([new Duty("d1", "Weekly", astur, new ManualTrigger(), "Write a note.")])));
        }
        Assert.NotNull(launch.Run(new StartupMode(true, null, null), out _));
        var profiles = launch.Model.AssistantHost.Profiles();
        Assert.Contains(profiles, p => p.Id == astur && p.ParentId == parent && p.Name == "Research");
        var policy = launch.OnMain(() => launch.Model.Dextra!.PolicyContext(new AssistantChat(Guid.NewGuid(), astur, null, AssistantChatRole.Home, ProviderKind.Codex, "fake")));
        Assert.NotNull(policy);
    }
}
