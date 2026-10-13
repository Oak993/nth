using System.Text;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 5; ED-NEW-dextra-as19-7): an approved draft becomes an own skill through the own-skill service, as
// a folder the owner adds himself: the service checks the name and the description, copies the files into its store and pins them. The
// SKILL.md carries the steps, with the ones that ask marked, and its two frontmatter fields read back exactly, Hindi and quotes included.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-7")]
public sealed class TeachSkillWriterTests : IDisposable
{
    private readonly string staging = Path.Combine(Path.GetTempPath(), "as19-stage-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly TeachSkillRig rig = new();

    public TeachSkillWriterTests() => Directory.CreateDirectory(staging);

    public void Dispose() => Directory.Delete(staging, recursive: true);

    private TeachSkillWriter Writer() => new(rig.Sources, TimeProvider.System);

    private static TeachDraft Draft(string name = "send-weekly-report", string description = "Sends the weekly report.") =>
        TeachDraftBuilder.Skeleton(new TeachRecordingInfo("t0123456789ab", "dextra", new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), TeachRecordingState.Recorded),
        [
            new TeachEvent(1, TimeSpan.FromSeconds(2), TeachEventKind.Click) { Verb = "click", Element = new TeachElement("outlook", "Inbox", "button", "New mail") },
            new TeachEvent(2, TimeSpan.FromSeconds(9), TeachEventKind.Click) { Verb = "click", Element = new TeachElement("outlook", "New message", "button", "Send") },
        ], []) with { Name = name, Description = description };

    [Fact]
    public async Task A_good_draft_becomes_an_own_skill_whose_file_holds_the_steps_and_the_marks()
    {
        var saved = await Writer().Save(Draft(), staging, CancellationToken.None);
        Assert.True(saved.Ok);
        Assert.Null(saved.Problem);
        var item = Assert.Single(rig.Sources.List(CustomAddKind.Skills));
        Assert.Equal((saved.ItemId, "send-weekly-report", true), (item.Id, item.Name, item.Enabled));
        var text = Encoding.UTF8.GetString(rig.Installed[item.Id]["payload/SKILL.md"]);
        Assert.Contains("1. Click “New mail” (button) in Outlook\n", text, StringComparison.Ordinal);
        Assert.Contains("2. **Asks you (sends).** Click “Send” (button) in Outlook\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_name_and_the_description_read_back_exactly_as_the_skills_pipeline_reads_them()
    {
        var description = "Sends “the report” to \"Sam\" — भेजें رپورٹ \\ done.";
        var saved = await Writer().Save(Draft(description: description), staging, CancellationToken.None);
        Assert.True(saved.Ok, saved.Problem);
        var text = Encoding.UTF8.GetString(rig.Installed[saved.ItemId!]["payload/SKILL.md"]);
        var read = SkillFrontmatter.Disclosure(text);
        Assert.Equal(("send-weekly-report", description), (read.Name, read.Description));
        Assert.False(SkillFrontmatter.ExplicitOnly(text));
    }

    [Fact]
    public async Task A_name_an_own_skill_already_uses_is_said_and_nothing_more_is_added()
    {
        var first = await Writer().Save(Draft(), staging, CancellationToken.None);
        Assert.True(first.Ok);
        var second = await Writer().Save(Draft(), staging, CancellationToken.None);
        Assert.False(second.Ok);
        Assert.True(second.NameTaken);
        Assert.Equal("A skill named “send-weekly-report” already exists. Pick another name.", second.Problem);
        Assert.Single(rig.Sources.List(CustomAddKind.Skills));
    }

    [Fact]
    public async Task A_draft_that_cannot_be_saved_says_what_is_wrong_and_writes_nothing()
    {
        var saved = await Writer().Save(Draft(name: "Bad Name"), staging, CancellationToken.None);
        Assert.False(saved.Ok);
        Assert.Equal("Name the skill with lowercase letters, digits and single hyphens, up to 64 characters.", saved.Problem);
        Assert.Empty(rig.Sources.List(CustomAddKind.Skills));
        Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
        Assert.Empty(rig.Installed);
    }

    [Fact]
    public async Task A_store_that_cannot_commit_leaves_no_skill_and_says_so()
    {
        rig.FailCommit = true;
        var saved = await Writer().Save(Draft(), staging, CancellationToken.None);
        Assert.False(saved.Ok);
        Assert.StartsWith("nth could not add the skill", saved.Problem, StringComparison.Ordinal);
        Assert.Empty(rig.Sources.List(CustomAddKind.Skills));
        Assert.Empty(rig.Installed);
    }

    [Fact]
    public async Task A_cancelled_save_adds_nothing()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer().Save(Draft(), staging, cancel.Token));
        Assert.Empty(rig.Sources.List(CustomAddKind.Skills));
    }
}
