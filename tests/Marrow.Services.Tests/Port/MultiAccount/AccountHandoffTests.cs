using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Support;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-4")]
[Trait("Rule", "R-TEST-4")]
public sealed class AccountHandoffTests
{
    [Fact]
    public void Archive_is_saved_in_the_thread_folder_and_size_report_counts_actual_utf8_bytes()
    {
        using var scratch = new StorageScratchFolder();
        var folder = Path.Combine(scratch.Root, "thread");
        var context = Context();
        var result = HandoffStore.Save(context, folder);
        Assert.Equal(folder, Path.GetDirectoryName(result.HandoffFile));
        Assert.Equal(Encoding.UTF8.GetBytes(result.FullConversation), File.ReadAllBytes(result.HandoffFile));
        Assert.Equal(new FileInfo(result.HandoffFile).Length, result.Size.FullBytes);
        Assert.Contains(result.HandoffFile, result.Text, StringComparison.Ordinal);
        var next = HandoffStore.Save(context with { Task = "next checkpoint" }, folder);
        Assert.NotEqual(result.HandoffFile, next.HandoffFile);
        Assert.True(File.Exists(result.HandoffFile));
    }

    [Theory]
    [InlineData("claude", "codex")]
    [InlineData("codex", "claude")]
    [InlineData("claude", "claude")]
    public async Task Cross_service_and_unsupported_transcript_handoffs_send_summary_as_the_first_message(string from, string to)
    {
        using var scratch = new StorageScratchFolder();
        var summary = HandoffStore.Save(Context(), Path.Combine(scratch.Root, "thread"));
        var source = new HandoffAccount(from == "claude" ? ProviderKind.Claude : ProviderKind.Codex, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(to == "claude" ? ProviderKind.Claude : ProviderKind.Codex, "B", Path.Combine(scratch.Root, "B"));
        var session = new HandoffSession();
        SessionConfiguration? captured = null;
        var handoff = new AccountHandoff(config => { captured = config; return session; });
        var result = await handoff.Start(summary, source, target, Config(target));
        Assert.Equal(HandoffMode.ContextTransfer, result.Mode);
        Assert.Null(captured!.ResumeID);
        Assert.Null(captured.ResumePath);
        Assert.Empty(captured.Transcript);
        Assert.Single(session.Inputs);
        Assert.Equal(summary.Text, session.Inputs[0].Text);
        Assert.Equal(RuntimeMode.Supervised, session.Inputs[0].RuntimeMode);
        Assert.True(session.StartedBeforeSend);
    }

    [Theory]
    [InlineData("claude", "projects/project/session-17.jsonl", NativeResumeMode.ClaudeId)]
    [InlineData("codex", "sessions/2026/10/05/rollout-session-17.jsonl", NativeResumeMode.CodexPath)]
    public async Task Same_service_copies_only_manifest_transcripts_and_preserves_resume_id_anchor_and_permissions(string service, string relative, NativeResumeMode mode)
    {
        using var scratch = new StorageScratchFolder();
        var provider = service == "claude" ? ProviderKind.Claude : ProviderKind.Codex;
        var source = new HandoffAccount(provider, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(provider, "B", Path.Combine(scratch.Root, "B"));
        var path = Path.Combine(source.TranscriptRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] recording = Encoding.UTF8.GetBytes("{\"session\":\"session-17\",\"text\":\"Café 🙂\"}\r\n");
        File.WriteAllBytes(path, recording);
        File.WriteAllText(Path.Combine(source.TranscriptRoot, "auth.json"), "fake credential sentinel; never copied");
        var summary = HandoffStore.Save(Context(), Path.Combine(scratch.Root, "thread"));
        var session = new HandoffSession { SessionId = "session-17" };
        SessionConfiguration? captured = null;
        var handoff = new AccountHandoff(config => { captured = config; return session; });
        var native = new NativeTranscript("session-17", "anchor-42", "test-version", mode, new[] { relative }, relative);
        var result = await handoff.Start(summary, source, target, Config(target), native, new(provider, "test-version", mode));
        Assert.Equal(HandoffMode.NativeResumePendingAcknowledgement, result.Mode);
        Assert.Equal(recording, File.ReadAllBytes(Path.Combine(target.TranscriptRoot, relative)));
        Assert.False(File.Exists(Path.Combine(target.TranscriptRoot, "auth.json")));
        Assert.Equal("session-17", captured!.ResumeID);
        Assert.Equal("anchor-42", captured.ResumeAt);
        Assert.Equal(RuntimeMode.Supervised, captured.RuntimeMode);
        Assert.Equal(InteractionMode.Build, captured.InteractionMode);
        Assert.Equal(target.TranscriptRoot, captured.Environment[service == "claude" ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME"]);
        if (mode == NativeResumeMode.CodexPath) Assert.Equal(Path.GetFullPath(Path.Combine(target.TranscriptRoot, relative)), captured.ResumePath);
        Assert.Empty(session.Inputs);
    }

    [Theory]
    [InlineData("../auth.json")]
    [InlineData("auth.json")]
    [InlineData("sessions/index.sqlite")]
    public async Task Unsafe_artifacts_are_rejected_before_session_launch(string relative)
    {
        using var scratch = new StorageScratchFolder();
        var source = new HandoffAccount(ProviderKind.Codex, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(ProviderKind.Codex, "B", Path.Combine(scratch.Root, "B"));
        var launched = false;
        var handoff = new AccountHandoff(_ => { launched = true; return new HandoffSession(); });
        var summary = HandoffStore.Save(Context(), Path.Combine(scratch.Root, "thread"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.Start(summary, source, target, Config(target),
            new("session-17", null, "v", NativeResumeMode.CodexPath, new[] { relative }, relative), new(ProviderKind.Codex, "v", NativeResumeMode.CodexPath)));
        Assert.False(launched);
    }

    [Fact]
    public async Task Resume_failure_stops_destination_rolls_back_copies_and_keeps_source_and_archive()
    {
        using var scratch = new StorageScratchFolder();
        const string relative = "projects/p/session-17.jsonl";
        var source = new HandoffAccount(ProviderKind.Claude, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(ProviderKind.Claude, "B", Path.Combine(scratch.Root, "B"));
        var original = Path.Combine(source.TranscriptRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllText(original, "offline transcript");
        var summary = HandoffStore.Save(Context(), Path.Combine(scratch.Root, "thread"));
        var session = new HandoffSession { FailStart = true };
        var handoff = new AccountHandoff(_ => session);
        await Assert.ThrowsAsync<IOException>(() => handoff.Start(summary, source, target, Config(target),
            new("session-17", null, "v", NativeResumeMode.ClaudeId, new[] { relative }, relative), new(ProviderKind.Claude, "v", NativeResumeMode.ClaudeId)));
        Assert.True(session.Stopped);
        Assert.Equal("offline transcript", File.ReadAllText(original));
        Assert.False(File.Exists(Path.Combine(target.TranscriptRoot, relative)));
        Assert.True(File.Exists(summary.HandoffFile));
    }

    [Fact]
    public async Task A_running_source_checkpoint_cannot_launch_or_repeat_tools_on_the_destination()
    {
        using var scratch = new StorageScratchFolder();
        var source = new HandoffAccount(ProviderKind.Claude, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(ProviderKind.Codex, "B", Path.Combine(scratch.Root, "B"));
        var summary = HandoffStore.Save(Context() with { IsQuiescent = false }, Path.Combine(scratch.Root, "thread"));
        var launched = false;
        var handoff = new AccountHandoff(_ => { launched = true; return new HandoffSession(); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.Start(summary, source, target, Config(target)));
        Assert.False(launched);
    }

    [Fact]
    public async Task Destination_root_mismatch_is_rejected_even_when_context_transfer_is_used()
    {
        using var scratch = new StorageScratchFolder();
        var source = new HandoffAccount(ProviderKind.Claude, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(ProviderKind.Claude, "B", Path.Combine(scratch.Root, "B"));
        var summary = HandoffStore.Save(Context(), Path.Combine(scratch.Root, "thread"));
        var launched = false;
        var handoff = new AccountHandoff(_ => { launched = true; return new HandoffSession(); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.Start(summary, source, target, Config(source)));
        Assert.False(launched);
    }

    [Fact]
    public async Task Version_mismatch_falls_back_to_context_without_copying_unverified_transcripts()
    {
        using var scratch = new StorageScratchFolder();
        var source = new HandoffAccount(ProviderKind.Codex, "A", Path.Combine(scratch.Root, "A"));
        var target = new HandoffAccount(ProviderKind.Codex, "B", Path.Combine(scratch.Root, "B"));
        var summary = HandoffStore.Save(Context(), Path.Combine(scratch.Root, "thread"));
        var session = new HandoffSession();
        var result = await new AccountHandoff(_ => session).Start(summary, source, target, Config(target),
            new("session-17", null, "v1", NativeResumeMode.CodexPath, new[] { "sessions/missing.jsonl" }, "sessions/missing.jsonl"),
            new(ProviderKind.Codex, "v2", NativeResumeMode.CodexPath));
        Assert.Equal(HandoffMode.ContextTransfer, result.Mode);
        Assert.Equal(summary.Text, Assert.Single(session.Inputs).Text);
        Assert.False(Directory.Exists(target.TranscriptRoot));
    }

    private static HandoffContext Context() => new(new ThreadDocument(Guid.NewGuid()) { Items = new TimelineItem[] {
        new(null, new TimelineItem.Content.User(new() { Text = "Keep Café 🙂 and 17 verbatim." })) } }, "MA-4", "current diff") { IsQuiescent = true };

    private static SessionConfiguration Config(HandoffAccount target) => new()
    {
        Provider = target.Provider, WorkingDirectory = SwiftURL.Create("file:///offline")!,
        Environment = new ValueMap<string, string>(new Dictionary<string, string> { [target.Provider == ProviderKind.Claude ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME"] = target.TranscriptRoot }),
        RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        ResumeID = "stale-source-id", Transcript = new TranscriptMessage[] { new() { TranscriptMessageRole = TranscriptMessage.Role.User, Text = "stale history" } },
    };
}
