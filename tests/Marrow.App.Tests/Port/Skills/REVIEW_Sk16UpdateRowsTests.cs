using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Host;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Review", "SK-16")]
[Trait("Decision", "ED-308")]
public sealed class REVIEW_Sk16UpdateRowsTests(HeadlessSession headless)
{
    [Fact]
    public void REVIEW_A_skills_change_during_an_update_is_reconciled_when_the_run_finishes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var release = new TaskCompletionSource();
        var busy = new FakeUpdateItem("S1", "First set", UpdateItemKind.SkillsAndTools, "1", "2") { Gate = release.Task };
        var later = new FakeUpdateItem("S2", "Second set", UpdateItemKind.SkillsAndTools, "1", "1");
        var service = new UpdateEverythingService([busy], () => { });
        using var shared = UpdateEverythingService.OverrideShared(service);
        using var source = UpdateEverythingProduction.OverrideSource(_ => ([later], () => { }));
        UpdateEverythingProduction.Remember(null!);
        var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "review-sk16-rows-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            using var attach = SkillsUpdateRows.Attach(host);
            runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            Assert.True(service.IsBusy);
            host.ResetAsync().GetAwaiter().GetResult();
            runtime.RunUntilIdle();
            Assert.Equal(["S1"], service.Rows.Select(r => r.Id));
            release.TrySetResult();
            runtime.RunUntilIdle();
            Assert.False(service.IsBusy);
            Assert.Equal(["S2"], service.Rows.Select(r => r.Id));
        }
        finally
        {
            release.TrySetResult();
            runtime.RunUntilIdle();
            UpdateEverythingProduction.Forget();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    });
}
