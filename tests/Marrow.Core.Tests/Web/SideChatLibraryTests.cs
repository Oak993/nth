using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W6 (DESIGN.md 3.9 "Saved with the thread"): IsSideChat rides on the thread record. It is written only when
// set, so every other thread's file is what it was, and a file from before the flag reads as an ordinary thread.
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class SideChatLibraryTests
{
    private static ChatThread Thread() => new(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

    [Fact]
    public void A_side_chat_flag_round_trips_through_the_thread_json()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var thread = Thread() with { IsSideChat = true, IsInPanel = true, ParentThreadID = Guid.NewGuid() };
        var json = JsonSerializer.Serialize(thread, Coding.StorageOptions);
        Assert.Contains("\"isSideChat\":true", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<ChatThread>(json, Coding.StorageOptions)!;
        Assert.True(back.IsSideChat);
        Assert.Equal(thread, back);
    }

    [Fact]
    public void An_ordinary_thread_writes_no_flag_and_an_old_file_reads_as_an_ordinary_thread()
    {
        using var runtime = new TestRuntime();
        var json = JsonSerializer.Serialize(Thread(), Coding.StorageOptions);
        Assert.DoesNotContain("isSideChat", json, StringComparison.Ordinal);
        Assert.False(JsonSerializer.Deserialize<ChatThread>(json, Coding.StorageOptions)!.IsSideChat);
    }

    [Fact]
    public void A_flag_that_cannot_be_read_means_an_ordinary_thread_and_the_library_keeps_every_thread()
    {
        using var runtime = new TestRuntime();
        var json = JsonSerializer.Serialize(Thread(), Coding.StorageOptions);
        var broken = json[..^1] + ",\"isSideChat\":\"yes\"}";
        Assert.False(JsonSerializer.Deserialize<ChatThread>(broken, Coding.StorageOptions)!.IsSideChat);

        var project = new Project("P", "C:/p");
        var side = Thread() with { ProjectID = project.Id, IsSideChat = true, IsInPanel = true };
        var library = new Library { Projects = new ValueList<Project>([project]), Threads = new ValueList<ChatThread>([Thread() with { ProjectID = project.Id }, side]) };
        var restored = JsonSerializer.Deserialize<Library>(JsonSerializer.Serialize(library, Coding.StorageOptions), Coding.StorageOptions)!;
        Assert.Equal(2, restored.Threads.Count);
        Assert.Equal([false, true], restored.Threads.Select(thread => thread.IsSideChat));
    }
}
