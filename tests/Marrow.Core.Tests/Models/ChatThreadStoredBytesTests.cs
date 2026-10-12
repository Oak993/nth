using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// AS-3a (DESIGN.md 16): a library written by the build before the Assistant fields keeps loading, and the build with
// the fields writes it back byte for byte. The text below was captured from the build before AS-3a and is frozen.
public sealed class ChatThreadStoredBytesTests
{
    private static readonly Guid ProjectID = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlainID = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RichID = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HelperID = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Stamp = DateTimeOffset.Parse("2026-10-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private const string FrozenLibrary = """{"projects":[{"addedAt":"2026-10-01T10:00:00Z","id":"11111111-1111-1111-1111-111111111111","isExpanded":true,"name":"nth","path":"C:\\work\\nth","scripts":[]}],"threads":[{"createdAt":"2026-10-01T10:00:00Z","expandsHelpers":false,"fastMode":false,"hasCustomTitle":false,"hasUnread":false,"hydraEnabled":true,"hydraEnabledIsExplicit":true,"hydraSpawnCount":0,"id":"22222222-2222-2222-2222-222222222222","interactionMode":"build","isArchived":false,"isInPanel":false,"isPinned":false,"isSettled":false,"projectID":"11111111-1111-1111-1111-111111111111","provider":"claude","runtimeMode":"supervised","title":"New thread","updatedAt":"2026-10-01T10:00:00Z"},{"branch":"feature\/x","createdAt":"2026-10-01T10:00:00Z","effort":"high","expandsHelpers":false,"fastMode":true,"hasCustomTitle":true,"hasUnread":false,"hydraEnabled":true,"hydraEnabledIsExplicit":true,"hydraSpawnCount":0,"id":"33333333-3333-3333-3333-333333333333","interactionMode":"build","isArchived":false,"isInPanel":false,"isPinned":true,"isSettled":false,"model":"gpt-6","pillID":"66666666-6666-6666-6666-666666666666","pillStepAccountID":"acct","pillStepIndex":1,"projectID":"11111111-1111-1111-1111-111111111111","provider":"codex","providerAccountID":"55555555-5555-5555-5555-555555555555","providerResumeAt":"msg-9","providerSessionID":"sess-1","runtimeMode":"fullAccess","sortOrder":2.5,"title":"Rich","updatedAt":"2026-10-01T10:00:00Z","worktreePath":"C:\\work\\nth-wt"},{"createdAt":"2026-10-01T10:00:00Z","expandsHelpers":false,"fastMode":false,"hasCustomTitle":false,"hasUnread":false,"hydraEnabled":true,"hydraEnabledIsExplicit":true,"hydraSpawnCount":0,"id":"44444444-4444-4444-4444-444444444444","interactionMode":"build","isArchived":true,"isInPanel":true,"isPinned":false,"isSettled":false,"parentThreadID":"33333333-3333-3333-3333-333333333333","projectID":"11111111-1111-1111-1111-111111111111","provider":"claude","runtimeMode":"supervised","title":"New thread","updatedAt":"2026-10-01T10:00:00Z"}]}""";

    private static Library Written()
    {
        var project = new Project("nth", "C:\\work\\nth") with { Id = ProjectID, AddedAt = Stamp };
        var plain = new ChatThread(ProjectID, ProviderKind.Claude, null, null, RuntimeMode.Supervised) with { Id = PlainID, CreatedAt = Stamp, UpdatedAt = Stamp };
        var rich = new ChatThread(ProjectID, ProviderKind.Codex, "gpt-6", "high", RuntimeMode.FullAccess, true) with
        {
            Id = RichID, CreatedAt = Stamp, UpdatedAt = Stamp, Title = "Rich", HasCustomTitle = true,
            WorktreePath = "C:\\work\\nth-wt", Branch = "feature/x", ProviderSessionID = "sess-1", ProviderResumeAt = "msg-9",
            ProviderAccountID = Guid.Parse("55555555-5555-5555-5555-555555555555"), IsPinned = true, SortOrder = 2.5,
            PillID = Guid.Parse("66666666-6666-6666-6666-666666666666"), PillStepIndex = 1, PillStepAccountID = "acct",
        };
        var helper = new ChatThread(ProjectID, ProviderKind.Claude, null, null, RuntimeMode.Supervised) with
        {
            Id = HelperID, CreatedAt = Stamp, UpdatedAt = Stamp, ParentThreadID = RichID, IsInPanel = true, IsArchived = true,
        };
        return new Library { Projects = new ValueList<Project>([project]), Threads = new ValueList<ChatThread>([plain, rich, helper]) };
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Rule", "R-CODE-3")]
    public void The_build_with_the_assistant_fields_writes_a_library_that_has_none_byte_for_byte_as_before()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        Assert.Equal(FrozenLibrary, Encoding.UTF8.GetString(Coding.Encode(Written())));
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Rule", "R-CODE-1")]
    public void A_library_written_before_the_assistant_fields_loads_and_saves_again_unchanged()
    {
        using var runtime = new TestRuntime();
        var loaded = Coding.Decode<Library>(Encoding.UTF8.GetBytes(FrozenLibrary))!;
        Assert.Equal(3, loaded.Threads.Count);
        Assert.Single(loaded.Projects);
        Assert.Equal(Written().Threads, loaded.Threads);
        Assert.Equal(FrozenLibrary, Encoding.UTF8.GetString(Coding.Encode(loaded)));
    }
}
