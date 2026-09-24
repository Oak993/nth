using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

public sealed class CommandCodeSessionTests
{
    [Fact]
    [Trait("Req", "PR-PROVCLI-206")] // DroppyCode/Services/Providers/CommandCodeSession.swift:115-124
    public void TranscriptExists_scans_immediate_project_folders_for_the_exact_transcript_name()
    {
        var root = ScratchDirectory();
        try
        {
            var projects = Path.Combine(root, ".commandcode", "projects");
            var first = Path.Combine(projects, "first");
            var second = Path.Combine(projects, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllBytes(Path.Combine(second, "session-123.jsonl"), []);

            Assert.True((bool)InvokeStatic("TranscriptExists", "session-123", new Dictionary<string, string> { ["HOME"] = root })!);
            Assert.False((bool)InvokeStatic("TranscriptExists", "session-123", new Dictionary<string, string> { ["HOME"] = Path.Combine(root, "missing-home") })!);
            Assert.False((bool)InvokeStatic("TranscriptExists", "session-123.jsonl", new Dictionary<string, string> { ["HOME"] = root })!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-218")] // DroppyCode/Services/Providers/CommandCodeSession.swift:209-218
    public void Prompt_names_only_attachments_that_exist()
    {
        var root = ScratchDirectory();
        try
        {
            var existing = Path.Combine(root, "image.png");
            File.WriteAllBytes(existing, []);
            var input = new TurnInput
            {
                Text = "Look at these.",
                Images = new ValueList<Attachment>(
                [
                    new Attachment { Name = "image.png", Path = existing, MimeType = "image/png" },
                    new Attachment { Name = "missing.png", Path = Path.Combine(root, "missing.png"), MimeType = "image/png" },
                ]),
                RuntimeMode = RuntimeMode.Supervised,
                InteractionMode = InteractionMode.Build,
            };

            Assert.Equal(
                "Look at these.\n\nAttached files (read them with read_file):\n- " + existing,
                (string)InvokeStatic("Prompt", input)!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-218")] // DroppyCode/Services/Providers/CommandCodeSession.swift:209-218
    public void Prompt_adds_nothing_when_no_attachment_exists()
    {
        var input = new TurnInput
        {
            Text = "Continue.",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };

        Assert.Equal("Continue.", (string)InvokeStatic("Prompt", input)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-204")] // DroppyCode/Services/Providers/CommandCodeSession.swift:90-92
    public void IsRunning_is_the_started_and_not_stopped_flag_pair()
    {
        var session = CreateSession();
        var type = session.GetType();

        Assert.False((bool)type.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!);
        SetField(session, "started", true);
        Assert.True((bool)type.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!);
        SetField(session, "stopped", true);
        Assert.False((bool)type.GetProperty("IsRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-220")] // DroppyCode/Services/Providers/CommandCodeSession.swift:237-239
    public async Task Compact_always_fails_with_the_source_message()
    {
        var session = CreateSession();
        var compact = session.GetType().GetMethod("Compact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        var error = await Assert.ThrowsAsync<ProviderError>(() => (Task)compact.Invoke(session, null)!);

        Assert.Equal("Command Code compacts its context on its own.", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-221")] // DroppyCode/Services/Providers/CommandCodeSession.swift:253
    public void AnswerQuestion_emits_no_event()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        session.GetType().GetMethod("AnswerQuestion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(session, ["request", new Dictionary<string, string[]>()]);

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-223")] // DroppyCode/Services/Providers/CommandCodeSession.swift:267-284
    public void Result_frame_adopts_a_different_nonempty_session_id()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"result","sessionId":"resume-123"}""");

        var ready = Assert.Single(events);
        Assert.Equal("SessionReady", ready.GetType().Name);
        Assert.Equal("resume-123", ready.GetType().GetProperty("SessionID")!.GetValue(ready));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-223")] // DroppyCode/Services/Providers/CommandCodeSession.swift:267-284
    public void Unknown_top_level_frame_is_ignored()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"unknown"}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-226")] // DroppyCode/Services/Providers/CommandCodeSession.swift:293-306
    public void Text_delta_without_message_start_opens_a_namespaced_message_lazily()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"text_delta","delta":"hello"}}""");

        var delta = Assert.Single(events);
        Assert.Equal("MessageDelta", delta.GetType().Name);
        Assert.Equal("hello", delta.GetType().GetProperty("Text")!.GetValue(delta));
        Assert.Matches("^cc-[0-9A-F]{8}-m1$", (string)delta.GetType().GetProperty("Id")!.GetValue(delta)!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-226")] // DroppyCode/Services/Providers/CommandCodeSession.swift:293-306
    public void Empty_text_delta_is_dropped()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"text_delta","delta":""}}""");

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-227")] // DroppyCode/Services/Providers/CommandCodeSession.swift:307-337
    public void Blank_thinking_end_does_not_complete_reasoning()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"thinking_start"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"thinking_delta","delta":"  \n"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"thinking_end"}}""");

        Assert.Single(events, item => item.GetType().Name == "ReasoningDelta");
        Assert.DoesNotContain(events, item => item.GetType().Name == "ReasoningCompleted");
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-227")] // DroppyCode/Services/Providers/CommandCodeSession.swift:307-337
    public void Whole_message_is_completed_from_content_and_clears_its_id()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"message_start"}}""");
        InvokeHandle(session, """{"type":"event","event":{"type":"message_end","content":[{"type":"text","text":"complete"}]}}""");

        var completed = Assert.Single(events);
        Assert.Equal("MessageCompleted", completed.GetType().Name);
        Assert.Equal("complete", completed.GetType().GetProperty("Text")!.GetValue(completed));
        Assert.Null(GetField(session, "currentMessageID"));
        Assert.Null(GetField(session, "currentThinkingID"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-224")] // DroppyCode/Services/Providers/CommandCodeSession.swift:286-431
    public void Unlisted_event_name_is_ignored()
    {
        var session = CreateSession();
        var events = CaptureEvents(session);

        InvokeHandle(session, """{"type":"event","event":{"type":"not_a_command_code_event"}}""");

        Assert.Empty(events);
    }

    private static string ScratchDirectory()
    {
        var root = TestDataDirectory.Read()
            ?? throw new InvalidOperationException("MARROW_PORT_DATA_DIR must be set for file-system tests.");
        var directory = Path.Combine(root, "CommandCodeSessionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static object CreateSession()
    {
        var root = ScratchDirectory();
        var workingDirectory = SwiftTraps.Unwrap(SwiftURL.Create(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri));
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Commandcode,
            Executable = null,
            WorkingDirectory = workingDirectory,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [configuration], culture: null)!;
    }

    private static List<ProviderEvent> CaptureEvents(object session)
    {
        var events = new List<ProviderEvent>();
        var property = session.GetType().GetProperty("OnEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        property.SetValue(session, (Action<ProviderEvent>)(events.Add));
        return events;
    }

    private static void InvokeHandle(object session, string json)
    {
        var message = JSONValue.Parse(json)!;
        session.GetType().GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [message]);
    }

    private static object? InvokeStatic(string name, params object[] arguments)
    {
        var type = typeof(ProviderSession).Assembly.GetType("Marrow.Services.Providers.CommandCodeSession", throwOnError: true)!;
        return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
    }

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
}
