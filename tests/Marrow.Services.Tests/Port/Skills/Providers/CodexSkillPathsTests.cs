// PORT-ONLY: SK-11b / WU-7 offline acceptance. A skill path from the listing generation maps to the same skill under the chat's own generation.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class CodexSkillPathsTests
{
    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_listed_path_becomes_the_same_relative_path_under_the_session_generation()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("A1", "pdf");
        var service = f.Open();
        using var listing = service.Listing("codex", [pdf], f.Switches((pdf, true)), f.Budget);
        using var chat = service.Start("codex", "chat-a", [pdf], f.Switches((pdf, true)), f.Budget);
        var listed = Assert.Single(listing.Skills);
        var rewritten = CodexSkillPaths.Rewrite(listed.File, listing.Skills, chat);
        Assert.Equal(Assert.Single(chat.Skills).File, rewritten);
        Assert.StartsWith(chat.Folder, rewritten, StringComparison.Ordinal);
        Assert.NotEqual(listed.File, rewritten);
        Assert.Equal(Path.GetRelativePath(listing.Folder, listed.File), Path.GetRelativePath(chat.Folder, rewritten));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_users_own_path_is_sent_unchanged()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("A1", "pdf");
        var service = f.Open();
        using var listing = service.Listing("codex", [pdf], f.Switches((pdf, true)), f.Budget);
        using var chat = service.Start("codex", "chat-a", [pdf], f.Switches((pdf, true)), f.Budget);
        var user = Path.Combine(Path.GetTempPath(), "home", ".agents", "skills", "mine", "SKILL.md");
        Assert.Equal(user, CodexSkillPaths.Rewrite(user, listing.Skills, chat));
        Assert.Equal(user, CodexSkillPaths.Rewrite(user, null, chat));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void The_mapping_still_works_after_the_listing_probe_is_released()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("A1", "pdf");
        var service = f.Open();
        var listing = service.Listing("codex", [pdf], f.Switches((pdf, true)), f.Budget);
        var snapshot = listing.Skills;
        var listedPath = snapshot[0].File;
        listing.Dispose();
        using var chat = service.Start("codex", "chat-a", [pdf], f.Switches((pdf, true)), f.Budget);
        Assert.Equal(chat.Skills[0].File, CodexSkillPaths.Rewrite(listedPath, snapshot, chat));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_listed_skill_the_chat_generation_left_out_is_refused_rather_than_sent_at_a_dead_path()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("A1", "pdf");
        var doc = f.Add("O3", "doc");
        var service = f.Open();
        using var listing = service.Listing("codex", [pdf, doc], f.Switches((pdf, true), (doc, true)), f.Budget);
        using var chat = service.Start("codex", "chat-a", [pdf, doc], f.Switches((pdf, true), (doc, false)), f.Budget);
        var listedDoc = listing.Skills.Single(s => s.Name == "doc");
        Assert.Throws<InvalidOperationException>(() => CodexSkillPaths.Rewrite(listedDoc.File, listing.Skills, chat));
    }
}
