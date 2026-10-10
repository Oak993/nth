// REVIEW (SK-13 independent review): the uninstall hook must never follow a junction out of the app's own addons and skills folders.
using Marrow.Services.Port.Skills.Uninstall;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Uninstall;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-16")]
[Trait("Platform", "WINDOWS-ONLY")]
[Trait("Rule", "R-TEST-9")]
[Trait("Review", "SK-13")]
[Collection("Dev data root env")]
public sealed class REVIEW_SkillsUninstallLinkTests
{
    private static string Victim(StoreFixture f)
    {
        var parent = Path.GetDirectoryName(f.Root)!;
        var victim = Path.Combine(parent, "review-victim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(victim, "deep"));
        File.WriteAllText(Path.Combine(victim, "keep.txt"), "user data");
        File.WriteAllText(Path.Combine(victim, "deep", "keep2.txt"), "user data 2");
        return victim;
    }

    private static void Plain(StoreFixture f)
    {
        foreach (var d in new[] { "addons/tools/T1-3.14.8-aaaa1111/sub0", "skills/store/a/b", "skills/views/claude/chat-one" })
        {
            var dir = Path.Combine(f.Root, d.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "f.txt"), "x");
        }
        File.WriteAllText(f.Paths.EnabledFile, "sw");
    }

    private static void AssertIntact(string victim)
    {
        Assert.True(File.Exists(Path.Combine(victim, "keep.txt")), "a file outside the app's folders was deleted through a link");
        Assert.True(File.Exists(Path.Combine(victim, "deep", "keep2.txt")), "a nested file outside the app's folders was deleted through a link");
    }

    [WindowsOnlyTheory]
    [InlineData("addons")]
    [InlineData("addons/tools")]
    [InlineData("skills")]
    [InlineData("skills/store")]
    [InlineData("skills/views")]
    [InlineData("skills/views/claude")]
    [InlineData("addons/tools/T1-3.14.8-aaaa1111/sub0")]
    public void A_junction_in_the_tree_is_removed_as_a_link_and_its_target_is_untouched(string where)
    {
        using var f = new StoreFixture(windows: true);
        Plain(f);
        var victim = Victim(f);
        try
        {
            var link = Path.Combine(f.Root, where.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(link)) Directory.Delete(link, true);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            TestLinks.Directory(Path.GetDirectoryName(f.Root)!, link, victim);
            SkillsUninstall.BeforeUninstall(f.Root);
            AssertIntact(victim);
            Assert.True(File.Exists(f.Paths.EnabledFile) || where == "skills", "enabled.json is kept");
        }
        finally { try { Directory.Delete(victim, true); } catch (IOException) { } }
    }

    [WindowsOnlyFact]
    public void A_junction_for_enabled_json_and_for_an_unlisted_child_of_skills_is_not_followed()
    {
        using var f = new StoreFixture(windows: true);
        Plain(f);
        var victim = Victim(f);
        try
        {
            TestLinks.Directory(Path.GetDirectoryName(f.Root)!, Path.Combine(f.Paths.Skills, "extra"), victim);
            SkillsUninstall.BeforeUninstall(f.Root);
            AssertIntact(victim);
        }
        finally { try { Directory.Delete(victim, true); } catch (IOException) { } }
    }

    [WindowsOnlyFact]
    public void A_data_root_under_a_junction_deletes_nothing_it_was_not_told_to()
    {
        using var f = new StoreFixture(windows: true);
        var parent = Path.GetDirectoryName(f.Root)!;
        var victim = Victim(f);
        try
        {
            Directory.CreateDirectory(Path.Combine(victim, "addons"));
            File.WriteAllText(Path.Combine(victim, "addons", "mine.txt"), "x");
            var linkedRoot = Path.Combine(parent, "review-linkroot-" + Guid.NewGuid().ToString("N"));
            TestLinks.Directory(parent, linkedRoot, victim);
            try { SkillsUninstall.BeforeUninstall(Path.Combine(linkedRoot, "Nth")); } catch (Exception e) when (e is ArgumentException or IOException) { }
            AssertIntact(victim);
        }
        finally { try { Directory.Delete(victim, true); } catch (IOException) { } }
    }

    [WindowsOnlyTheory]
    [InlineData("C:\\")]
    [InlineData(@"relative\path")]
    [InlineData("")]
    public void An_odd_data_root_is_refused_before_anything_is_touched(string root)
    {
        Assert.ThrowsAny<ArgumentException>(() => SkillsUninstall.BeforeUninstall(root));
    }
}
