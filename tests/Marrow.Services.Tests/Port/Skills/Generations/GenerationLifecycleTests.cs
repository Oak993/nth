// PORT-ONLY: SK-9 lifecycle and failure acceptance; no provider subprocess is started.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Feature", "SK-9")]
[Trait("Spec", "DL-9")]
public sealed class GenerationLifecycleTests
{
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-spare-claim
    public void Ready_spare_is_claimed_by_rename_and_key_depends_on_entries_not_chat_or_project()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var s = f.Switches((item, true));
        var service = f.Open();
        var key = service.Prepare("codex", [item], s, f.Budget);
        var spare = Assert.Single(Directory.GetDirectories(Path.Combine(f.Paths.Views, "codex"), "spare-*"));
        using var first = service.Start("codex", "one", [item], s, f.Budget);
        Assert.Equal(GenerationLaunchReason.ReadySpare, service.LastLaunch!.Reason);
        using var second = service.Start("codex", "two", [item], s, f.Budget);
        Assert.False(Directory.Exists(spare));
        Assert.Equal(key, first.Key);
        Assert.Equal(first.Key, second.Key);
        Assert.NotEqual(first.Folder, second.Folder);
        Assert.True(first.Verify());
        using var project = service.Start("codex", "project", [item], s, f.Budget, [new("pdf", "user skill")]);
        Assert.NotEqual(key, project.Key);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-spare-repair
    public void Damaged_spare_is_rebuilt_from_verified_store_before_launch()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var s = f.Switches((item, true));
        var service = f.Open();
        service.Prepare("codex", [item], s, f.Budget);
        var spare = Assert.Single(Directory.GetDirectories(Path.Combine(f.Paths.Views, "codex"), "spare-*"));
        f.Trees.Unlock(spare);
        File.WriteAllText(Path.Combine(spare, "pdf", "SKILL.md"), "poisoned");
        f.Trees.Lock(spare);
        using var g = service.Start("codex", "one", [item], s, f.Budget);
        Assert.True(g.Verify());
        Assert.Contains("BODY SENTINEL", g.Invoke("pdf"), StringComparison.Ordinal);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-direct-store-GC
    public void Direct_store_GC_during_publication_cannot_delete_the_leased_target()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var locksAtChatPath = 0;
        f.Fake.BeforeLock = path =>
        {
            if (Path.GetFileName(path) != "chat-one") return;
            if (++locksAtChatPath == 2) f.Store!.Collect(); // after tmp rename, before final lock: target lease must already protect it.
        };
        using var g = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        Assert.Equal(2, locksAtChatPath);
        Assert.True(g.Verify());
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-1.4-skill-creator
    public void Skill_creator_uses_the_approved_source_for_each_provider()
    {
        using var f = new GenerationFixture();
        var a = f.Add("A1", "skill-creator");
        var o = f.Add("O1", "skill-creator");
        var service = f.Open();
        var s = f.Switches((a, true), (o, true));
        using var claude = service.Start("claude", "a", [a, o], s, f.Budget);
        using var pi = service.Start("pi", "p", [a, o], s, f.Budget);
        using var codex = service.Start("codex", "c", [a, o], s, f.Budget);
        Assert.StartsWith("A1/", Assert.Single(claude.Skills).Id, StringComparison.Ordinal);
        Assert.StartsWith("O1/", Assert.Single(pi.Skills).Id, StringComparison.Ordinal);
        Assert.Empty(codex.Skills);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: ONDEMAND-1-unmodified-explicit-only
    public void Explicit_only_without_an_upstream_flag_is_left_out_of_file_loading_providers()
    {
        using var f = new GenerationFixture();
        var item = f.Add("O2", "screenshot", explicitFlag: false);
        var service = f.Open();
        using var claude = service.Start("claude", "a", [item], f.Switches((item, true)), f.Budget);
        using var native = service.Start("native", "b", [item], f.Switches((item, true)), f.Budget);
        Assert.Empty(claude.Skills);
        Assert.Contains(item.Skills[0].Id, claude.LeftOut);
        Assert.Single(native.Skills);
        Assert.Empty(native.Catalogue);
        Assert.Contains("BODY SENTINEL", native.Invoke("screenshot"), StringComparison.Ordinal);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-fail-closed-clash-seam
    public void Missing_clash_snapshot_refuses_to_build()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        f.Open();
        using var service = new SkillGenerations(f.Paths, f.Store!, f.Fake);
        Assert.Throws<InvalidOperationException>(() => service.Start("codex", "a", [item], f.Switches((item, true)), f.Budget));
    }
}
