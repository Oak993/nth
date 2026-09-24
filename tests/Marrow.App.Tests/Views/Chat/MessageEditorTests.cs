using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Git;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Chat;

// STOP App.Views.Chat.MessageEditor #1
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/MessageEditor.swift:31-69 (B2 1fbf81e)
// Requirements: PR-CHAT-083, PR-CHAT-084, PR-CHAT-085, PR-CHAT-086, PR-GIT-124, PR-GIT-125, UI-CHAT-015, UI-CHAT-016, UI-CHAT-017, UI-CHAT-018, UI-CHAT-065, UI-CHAT-066, UI-CHAT-067, UI-CHAT-068, UI-GIT-052, UI-GIT-053, UI-GIT-054, UI-GIT-055, UI-GIT-056
// What is unclear or missing: The editor requires a ThreadRuntime and its preview state, but the provided declarations do not provide a safe way to construct a runtime for these tests: its constructor requires AppModel, whose construction API and test fixture are not supplied. The provided headless fixture only supplies a UI session and presenter seam. Constructing a runtime with null or bypassing its constructor would not test a valid Swift-shaped runtime. Therefore these tests cover only the independently constructible edit and preview values; they do not claim to verify the editor UI or resend behavior.
// Needed to proceed: A safe test construction path for ThreadRuntime and AppModel.
// Done so far: Tests for the edit's empty predicate and preview restorable-file statistics; no MessageEditor UI tests written.

[Collection("Headless")]
public sealed class MessageEditorTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-CHAT-017")] // App/Views/Chat/MessageEditor.swift:72-76
    [Trait("Req", "PR-CHAT-086")] // App/Views/Chat/MessageEditor.swift:72-76
    [Trait("Req", "PR-GIT-124")] // App/Views/Chat/MessageEditor.swift:72-76
    public void An_edit_without_attachments_or_non_whitespace_text_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var empty = new PromptEdit(Guid.NewGuid(), " \t\r\n", Marrow.Core.Port.ValueList<Marrow.Core.Models.Attachment>.Empty);
            var nonEmpty = new PromptEdit(Guid.NewGuid(), "x", Marrow.Core.Port.ValueList<Marrow.Core.Models.Attachment>.Empty);

            Assert.True(empty.IsEmpty);
            Assert.False(nonEmpty.IsEmpty);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-084")] // App/Views/Chat/MessageEditor.swift:100-114
    [Trait("Req", "UI-CHAT-016")] // App/Views/Chat/MessageEditor.swift:100-114
    [Trait("Req", "UI-GIT-054")] // App/Views/Chat/MessageEditor.swift:100-114
    public void Preview_statistics_and_restorable_count_exclude_kept_files()
    {
        var preview = new RevertPreview(new Marrow.Core.Port.ValueList<RevertPreview.File>(
        [
            new RevertPreview.File("one.txt", 3, 2, IsBinary: false),
            new RevertPreview.File("kept.txt", 7, 5, IsBinary: false, IsKept: true),
            new RevertPreview.File("binary.dat", 0, 0, IsBinary: true),
        ]));

        Assert.Equal(2, preview.Restorable.Count);
        Assert.Equal(3, preview.Additions);
        Assert.Equal(2, preview.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-085")] // App/Views/Chat/MessageEditor.swift:125-154
    [Trait("Req", "UI-CHAT-065")] // App/Views/Chat/MessageEditor.swift:125-146
    [Trait("Req", "UI-GIT-055")] // App/Views/Chat/MessageEditor.swift:125-154
    public void Preview_keeps_each_file_and_its_kept_state_in_the_list()
    {
        var preview = new RevertPreview(new Marrow.Core.Port.ValueList<RevertPreview.File>(
        [
            new RevertPreview.File("src/changed.cs", 4, 1, IsBinary: false),
            new RevertPreview.File("src/kept.cs", 2, 3, IsBinary: false, IsKept: true),
        ]));

        Assert.Equal(2, preview.Files.Count);
        Assert.Equal("src/changed.cs", preview.Files[0].Path);
        Assert.False(preview.Files[0].IsKept);
        Assert.Equal("src/kept.cs", preview.Files[1].Path);
        Assert.True(preview.Files[1].IsKept);
    }
}
