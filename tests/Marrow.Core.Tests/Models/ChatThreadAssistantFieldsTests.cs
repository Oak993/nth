using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// AS-3a (DESIGN.md 15.3 and 16): ChatThread gains AssistantID, MissionID and HiddenFromAssistant. An absent key is
// unset, the keys are written only when set, and a damaged key fails closed. Expected values are written out here,
// never read back from the code under test.
public sealed class ChatThreadAssistantFieldsTests
{
    private static readonly Guid AssistantID = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid MissionID = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static ChatThread Plain() => new(Guid.NewGuid(), ProviderKind.Claude, null, null, RuntimeMode.Supervised);

    private static ChatThread Decode(string extraJson = "")
    {
        var json = "{\"id\":\"cccccccc-0000-0000-0000-00000000000c\",\"projectID\":\"dddddddd-0000-0000-0000-00000000000d\"" +
            (extraJson.Length > 0 ? "," + extraJson : "") + "}";
        return ChatThreadConverter.Decode(JsonDocument.Parse(json).RootElement);
    }

    private static string[] Keys(ChatThread thread)
    {
        using var document = JsonDocument.Parse(Coding.Encode(thread));
        return [.. document.RootElement.EnumerateObject().Select(p => p.Name)];
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-1")]
    [Trait("Rule", "R-CODE-3")]
    public void The_three_fields_round_trip_on_a_thread_and_inside_a_library()
    {
        using var runtime = new TestRuntime(); // ChatThread reads UserDefaults
        var marked = Plain() with { AssistantID = AssistantID, MissionID = MissionID, HiddenFromAssistant = true };
        var other = Plain() with { MissionID = MissionID };

        var back = Coding.Decode<ChatThread>(Coding.Encode(marked))!;
        Assert.Equal(AssistantID, back.AssistantID);
        Assert.Equal(MissionID, back.MissionID);
        Assert.True(back.HiddenFromAssistant);
        Assert.Equal(marked, back);

        var library = new Library { Threads = new ValueList<ChatThread>([marked, other, Plain()]) };
        var loaded = Coding.Decode<Library>(Coding.Encode(library))!;
        Assert.Equal(library.Threads, loaded.Threads);
        Assert.Equal((AssistantID, MissionID, true), (loaded.Threads[0].AssistantID, loaded.Threads[0].MissionID, loaded.Threads[0].HiddenFromAssistant));
        Assert.Equal((null, MissionID, false), (loaded.Threads[1].AssistantID, loaded.Threads[1].MissionID, loaded.Threads[1].HiddenFromAssistant));
        Assert.Equal((null, null, false), (loaded.Threads[2].AssistantID, loaded.Threads[2].MissionID, loaded.Threads[2].HiddenFromAssistant));
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-1")]
    [Trait("Rule", "R-CODE-1")]
    public void A_thread_without_the_keys_or_with_null_keys_has_no_assistant_no_mission_and_is_not_hidden()
    {
        using var runtime = new TestRuntime();
        foreach (var json in new[] { "", "\"assistantID\":null,\"missionID\":null,\"hiddenFromAssistant\":null" })
        {
            var thread = Decode(json);
            Assert.Null(thread.AssistantID);
            Assert.Null(thread.MissionID);
            Assert.False(thread.HiddenFromAssistant);
        }
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-1")]
    [Trait("Rule", "R-CODE-3")]
    public void The_keys_are_written_only_when_set()
    {
        using var runtime = new TestRuntime();
        var none = Keys(Plain());
        Assert.DoesNotContain("assistantID", none);
        Assert.DoesNotContain("missionID", none);
        Assert.DoesNotContain("hiddenFromAssistant", none);

        var notHidden = Plain() with { HiddenFromAssistant = false };
        Assert.DoesNotContain("hiddenFromAssistant", Keys(notHidden));

        var onlyAssistant = Keys(Plain() with { AssistantID = AssistantID });
        Assert.Contains("assistantID", onlyAssistant);
        Assert.DoesNotContain("missionID", onlyAssistant);
        Assert.DoesNotContain("hiddenFromAssistant", onlyAssistant);

        var onlyMission = Keys(Plain() with { MissionID = MissionID });
        Assert.Contains("missionID", onlyMission);
        Assert.DoesNotContain("assistantID", onlyMission);

        var hidden = Keys(Plain() with { HiddenFromAssistant = true });
        Assert.Contains("hiddenFromAssistant", hidden);
        Assert.DoesNotContain("assistantID", hidden);
        Assert.DoesNotContain("missionID", hidden);

        // Swift writes a UUID in capitals (uuidString), as every other id key here does.
        using var document = JsonDocument.Parse(Coding.Encode(Plain() with { AssistantID = AssistantID, MissionID = MissionID, HiddenFromAssistant = true }));
        Assert.Equal("AAAAAAAA-0000-0000-0000-00000000000A", document.RootElement.GetProperty("assistantID").GetString());
        Assert.Equal("BBBBBBBB-0000-0000-0000-00000000000B", document.RootElement.GetProperty("missionID").GetString());
        Assert.True(document.RootElement.GetProperty("hiddenFromAssistant").GetBoolean());
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-1")]
    [Trait("Rule", "R-CODE-1")]
    public void A_key_that_cannot_be_read_fails_closed_and_never_drops_the_thread()
    {
        using var runtime = new TestRuntime();
        foreach (var bad in new[] { "\"broken\"", "42", "true", "{}", "[]" })
        {
            var thread = Decode($"\"assistantID\":{bad},\"missionID\":{bad},\"hiddenFromAssistant\":{bad.Replace("true", "\"yes\"", StringComparison.Ordinal)}");
            // Still a mission chat and still an assistant's chat, of a mission and an assistant nobody knows; still hidden.
            Assert.Equal(Guid.Empty, thread.AssistantID);
            Assert.Equal(Guid.Empty, thread.MissionID);
            Assert.True(thread.HiddenFromAssistant);
            Assert.Equal(Guid.Parse("cccccccc-0000-0000-0000-00000000000c"), thread.Id);
        }
        Assert.False(Decode("\"hiddenFromAssistant\":false").HiddenFromAssistant);
        Assert.True(Decode("\"hiddenFromAssistant\":true").HiddenFromAssistant);
        var kept = Coding.Decode<ChatThread>(Coding.Encode(Decode("\"missionID\":\"broken\"")))!;
        Assert.Equal(Guid.Empty, kept.MissionID);

        var library = Coding.Decode<Library>(System.Text.Encoding.UTF8.GetBytes(
            "{\"projects\":[],\"threads\":[{\"id\":\"cccccccc-0000-0000-0000-00000000000c\",\"projectID\":\"dddddddd-0000-0000-0000-00000000000d\",\"missionID\":7}]}"))!;
        Assert.Single(library.Threads);
        Assert.Equal(Guid.Empty, library.Threads[0].MissionID);
    }

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-1")]
    [Trait("Rule", "R-CODE-3")]
    public void A_new_thread_starts_with_none_and_every_copy_keeps_the_fields()
    {
        using var runtime = new TestRuntime();
        var fresh = Plain();
        Assert.Null(fresh.AssistantID);
        Assert.Null(fresh.MissionID);
        Assert.False(fresh.HiddenFromAssistant);

        var marked = fresh with { AssistantID = AssistantID, MissionID = MissionID, HiddenFromAssistant = true };
        var copy = marked with { Title = "Renamed", IsPinned = true };
        Assert.Equal((AssistantID, MissionID, true), (copy.AssistantID, copy.MissionID, copy.HiddenFromAssistant));
        var relayed = marked.WithPillRelay(new Marrow.Core.Accounts.Pills.PillThreadState(Guid.NewGuid(), 1, "a"));
        Assert.Equal((AssistantID, MissionID, true), (relayed.AssistantID, relayed.MissionID, relayed.HiddenFromAssistant));
        var folded = marked.WithFoldsHelpers(false);
        Assert.Equal((AssistantID, MissionID, true), (folded.AssistantID, folded.MissionID, folded.HiddenFromAssistant));
        var reloaded = Coding.Decode<ChatThread>(Coding.Encode(copy))!;
        Assert.Equal(copy, reloaded);
    }
}
