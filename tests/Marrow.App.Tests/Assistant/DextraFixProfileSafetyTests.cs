using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-1")]
public sealed class DextraFixProfileSafetyTests
{
    [Fact]
    public void Restart_keeps_saved_safety_rules_and_retirement_without_reactivating_duties()
    {
        using var temp = new AssistantTempRoot();
        const string parent = "22222222000040008000000000000002", astur = "11111111000040008000000000000001";
        var root = new AssistantProfile(parent, "Dextra")
        {
            ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Run, ClassDefault.Never)]),
            AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(Field: "upload")]),
        };
        var child = new AssistantProfile(astur, "Research")
        {
            ParentId = parent, Shape = AsturShape.Star, Retired = true,
            Connections = new ValueList<string>(["fake-connector"]),
            ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.Never)]),
            AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(Field: "send")]),
        };
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveProfile(parent, AssistantProfileFile.ToJson(root));
            store.SaveProfile(astur, AssistantProfileFile.ToJson(child));
            store.SaveDuties(astur, DutyJson.Write(new DutyDocument([new Duty("retired-duty", "Weekly", astur, new ManualTrigger(), "Write a note.")])));
        }
        using var launch = new BackgroundLaunchRig(temp, Guid.ParseExact(parent, "N"));
        Assert.NotNull(launch.Run(new StartupMode(true, null, null), out _));
        Assert.Equal(root, launch.Model.AssistantHost.Profiles().Single(p => p.Id == parent));
        Assert.Equal(child, launch.Model.AssistantHost.Profiles().Single(p => p.Id == astur));
        Assert.DoesNotContain(launch.Model.Duties!.Scheduler.Duties, d => d.Duty.Id == "retired-duty");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unusable_saved_root_cannot_fall_back_to_blank_safety_rules(bool newer)
    {
        using var temp = new AssistantTempRoot();
        const string parent = "22222222000040008000000000000002";
        var document = AssistantProfileFile.ToJson(new AssistantProfile(parent, "Dextra"));
        if (newer) document["version"] = AssistantProfileFile.FormatVersion + 1;
        else document["id"] = "11111111000040008000000000000001";
        using (var store = AssistantStore.Open(temp.Roots))
        {
            store.SaveProfile(parent, document);
            store.SaveDuties(parent, DutyJson.Write(new DutyDocument([new Duty("d1", "Weekly", parent, new ManualTrigger(), "Write a note.")])));
        }
        var path = Path.Combine(temp.Roots.AssistantRoot, "assistants", parent, "profile.json");
        var before = File.ReadAllBytes(path);
        using var launch = new BackgroundLaunchRig(temp, Guid.ParseExact(parent, "N"));
        Assert.Null(launch.Run(new StartupMode(true, null, null), out var problem));
        Assert.NotNull(problem);
        Assert.Null(launch.Model.Dextra);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
