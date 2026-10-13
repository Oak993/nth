using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-4")]
public sealed class AsturFileDeleteAuditTests
{
    [Fact]
    public async Task A_delete_failure_logs_the_action_and_id_without_echoing_a_secret_from_the_IO_failure()
    {
        using var rig = await AsturRig.OpenAsync();
        var made = await rig.Controller.CreateByOwner(AsturTeamRig.Draft("Shop", AsturShape.Squircle));
        Assert.True(made.Ok, made.Problem);
        var id = made.Profile!.Id;
        File.WriteAllText(Path.Combine(rig.Store.AssistantFolder(id), "fake.txt"), "fake");
        const string sentinel = "FAKE-SECRET-dextra-delete-12345";
        using var failure = AssistantStore.AsturDeleteCheckpoint.Override(_ => throw new IOException(sentinel));
        Assert.False((await rig.Controller.DeleteAsturFiles(id)).Ok);
        var actions = rig.Audit.Where(a => a.Tool == "team/delete-files").ToArray();
        Assert.Equal(2, actions.Length);
        Assert.All(actions, a => { Assert.Equal(id, a.Target); Assert.DoesNotContain(sentinel, a.Result, StringComparison.Ordinal); });
    }
}
