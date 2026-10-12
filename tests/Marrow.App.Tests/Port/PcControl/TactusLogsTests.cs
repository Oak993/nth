using Marrow.App.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the activity log's places (OD-0140, ED-NEW-pcctl-28; DESIGN.md 4.6, 4.7 "Activity log", unit 14). A
// chat's log folder is the one PcAuditLog writes; "Open log folder" makes and opens the root; a chat with no log opens
// nothing; "Clear logs" hands each chat's folder to the Recycle Bin part and never deletes it itself.
[Trait("Decision", "ED-NEW-pcctl-28")]
public sealed class TactusLogsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tactus-logs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void The_logs_open_and_clear_through_their_parts()
    {
        var opened = new List<string>();
        var recycled = new List<string>();
        var logs = new TactusLogs(() => root, opened.Add, recycled.Add);

        Assert.Equal(Path.Combine(root, "chat-1"), logs.FolderOf("chat-1"));
        Assert.Equal(Path.Combine(root, "a_b"), logs.FolderOf("a/b"));
        Assert.False(logs.HasLog("chat-1"));
        Assert.False(logs.OpenChat("chat-1"));
        Assert.Equal(0, logs.Clear());

        logs.OpenRoot();
        Assert.True(Directory.Exists(root));
        Assert.Equal(new[] { root }, opened);

        Directory.CreateDirectory(logs.FolderOf("chat-1"));
        File.WriteAllText(Path.Combine(logs.FolderOf("chat-1"), "2026-10-12.jsonl"), "{}\n");
        Directory.CreateDirectory(logs.FolderOf("chat-2"));
        File.WriteAllText(Path.Combine(logs.FolderOf("chat-2"), "2026-10-11.jsonl"), "{}\n");
        Assert.True(logs.HasLog("chat-1"));
        Assert.True(logs.OpenChat("chat-1"));
        Assert.Equal(logs.FolderOf("chat-1"), opened[^1]);

        Assert.Equal(2, logs.Clear());
        Assert.Equal(new[] { logs.FolderOf("chat-1"), logs.FolderOf("chat-2") }, recycled.Order(StringComparer.Ordinal));
        // the page never deletes: the folders are still there for the bin part to move
        Assert.True(Directory.Exists(logs.FolderOf("chat-1")));
    }
}
