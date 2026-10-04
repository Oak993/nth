// PORT-ONLY: native Windows SK-9 boundaries; use SK-5's native fixtures and unlock teardown.
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Feature", "SK-9")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class WindowsGenerationTests
{
    [WindowsOnlyFact, Trait("Feature", "SK-9"), Trait("Rule", "PLAN-2.1-generation-ACL")]
    public void Claimed_generation_keeps_ACL_and_GC_runs_the_unlock_order()
    {
        using var f = new GenerationFixture(windows: true);
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        service.Prepare("codex", [item], f.Switches((item, true)), f.Budget);
        var g = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        Assert.True(g.Verify());
        Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(g.Folder, "json.py"), "planted"));
        Assert.Throws<UnauthorizedAccessException>(() => File.AppendAllText(g.Skills[0].File, "changed"));
        Assert.Throws<UnauthorizedAccessException>(() => Directory.Delete(g.Folder, true));
        var path = g.Folder;
        g.Dispose();
        service.Collect();
        Assert.False(Directory.Exists(path));
    }

    [WindowsOnlyFact, Trait("Feature", "SK-9"), Trait("Rule", "PLAN-2.1-generation-junction")]
    public void Junction_in_generation_is_rejected_without_reading_its_target()
    {
        using var f = new GenerationFixture(windows: true);
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        using var g = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        var outside = Path.Combine(f.Paths.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sentinel"), "untouched");
        f.Trees.Unlock(g.Folder);
        WindowsJunction.Create(f.Paths.Root, Path.Combine(g.Folder, "junction"), outside);
        Assert.False(g.Verify());
        Assert.Throws<IOException>(() => g.Invoke("pdf"));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(outside, "sentinel")));
    }

    [WindowsOnlyFact, Trait("Feature", "SK-9"), Trait("Rule", "PLAN-2.1-held-generation-resume")]
    public void Held_old_chat_moves_to_a_new_path_and_GC_retries_after_handle_closes()
    {
        using var f = new GenerationFixture(windows: true);
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var old = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        var path = old.Folder;
        using (var held = new FileStream(old.Skills[0].File, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            old.Dispose();
            using var next = service.Start("codex", "one", [item], f.Switches((item, false)), f.Budget);
            Assert.NotEqual(path, next.Folder);
            Assert.Equal(path, next.PreviousFolder);
            Assert.True(Directory.Exists(path));
        }
        service.Collect();
        Assert.False(Directory.Exists(path));
    }
}
