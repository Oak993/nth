using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Providers;
using System.Text.Json;

namespace Marrow.Services.Port.MultiAccount;

// PORT-ONLY: OD-0114; ED-407; multi-account-FEASIBILITY section 4.
public sealed record HandoffAccount(ProviderKind Provider, string AccountId, string TranscriptRoot);
public enum NativeResumeMode { ClaudeId, CodexPath }
public sealed record NativeTranscript(string SessionId, string? ResumeAt, string Version, NativeResumeMode Mode,
    ValueList<string> ArtifactPaths, string ResumeRelativePath);

// The caller supplies a capability only after verifying this exact installed CLI version supports the transfer.
// There is deliberately no ambient capability or inference from the presence of a transcript file.
public sealed record NativeResumeCapability(ProviderKind Provider, string Version, NativeResumeMode Mode);
public enum HandoffMode { ContextTransfer, NativeResumePendingAcknowledgement }
public sealed record HandoffStarted(ProviderSession Session, string SessionId, HandoffMode Mode);

public sealed class AccountHandoff(Func<SessionConfiguration, ProviderSession> createSession)
{
    /// <summary>
    /// Launch after the caller has drained/stopped the source at a completed-turn boundary. This foundation
    /// does not select accounts or advance a runtime binding. A native launch still requires a continuity
    /// acknowledgement; it is never reported as verified by a successful process launch alone.
    /// </summary>
    public async Task<HandoffStarted> Start(HandoffSummary summary, HandoffAccount source, HandoffAccount target,
        SessionConfiguration configuration, NativeTranscript? native = null, NativeResumeCapability? capability = null)
    {
        if (configuration.Provider != target.Provider) throw new InvalidOperationException("The destination provider does not match its launch configuration.");
        if (source.Provider == target.Provider && string.Equals(source.AccountId, target.AccountId, StringComparison.Ordinal))
            throw new InvalidOperationException("A handoff requires another account.");
        if (!File.Exists(summary.HandoffFile)) throw new InvalidOperationException("Save the full conversation before handing it over.");
        if (!string.Equals(File.ReadAllText(summary.HandoffFile), summary.FullConversation, StringComparison.Ordinal))
            throw new InvalidOperationException("The saved handoff conversation does not match its checkpoint.");
        if (target.Provider is ProviderKind.Claude or ProviderKind.Codex) ValidateRoots(source, target, configuration);
        var checkpoint = JsonSerializer.Deserialize<HandoffContext>(summary.FullConversation)!;
        if (!checkpoint.IsQuiescent || checkpoint.History.Turns.Any(turn => turn.Status == TurnStatus.Running)
            || checkpoint.History.Items.Any(item => item.TimelineItemContent is TimelineItem.Content.Tool tool && tool.Value.ToolCallStatus == ToolCall.Status.Running))
            throw new InvalidOperationException("Stop and checkpoint the source before launching a handoff.");

        var useNative = source.Provider == target.Provider && native is not null && capability is not null
            && capability.Provider == target.Provider && string.Equals(capability.Version, native.Version, StringComparison.Ordinal)
            && capability.Mode == native.Mode;
        var copied = new List<string>();
        ProviderSession? session = null;
        try
        {
            var launch = configuration with { ResumeID = null, ResumeAt = null, ResumePath = null, Transcript = ValueList<TranscriptMessage>.Empty };
            if (useNative)
            {
                var manifest = ValidateManifest(source, target, native!);
                foreach (var (from, to) in manifest)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    // Never overwrite destination state. Only transcripts in the explicit manifest are copied.
                    using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    copied.Add(to);
                    using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await input.CopyToAsync(output);
                }
                launch = launch with
                {
                    ResumeID = native!.SessionId,
                    ResumeAt = native.ResumeAt,
                    ResumePath = native.Mode == NativeResumeMode.CodexPath ? UnderRoot(target.TranscriptRoot, native.ResumeRelativePath) : null,
                };
            }

            session = createSession(launch);
            var id = await session.Start();
            if (useNative)
            {
                if (!string.Equals(id, native!.SessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The destination did not resume the requested native session.");
                return new(session, id, HandoffMode.NativeResumePendingAcknowledgement);
            }

            // This is the first turn of a new session, for CLI, API and local providers alike.
            // Avoid replaying ApiTranscript as well: every message already appears verbatim in the summary.
            await session.Send(new TurnInput
            {
                Text = summary.Text, Images = ValueList<Attachment>.Empty, Model = launch.Model, Effort = launch.Effort,
                FastMode = launch.FastMode, RuntimeMode = launch.RuntimeMode, InteractionMode = launch.InteractionMode,
            });
            return new(session, id, HandoffMode.ContextTransfer);
        }
        catch
        {
            session?.Stop();
            foreach (var path in copied) File.Delete(path);
            throw;
        }
    }

    private static void ValidateRoots(HandoffAccount source, HandoffAccount target, SessionConfiguration configuration)
    {
        if (!Path.IsPathFullyQualified(source.TranscriptRoot) || !Path.IsPathFullyQualified(target.TranscriptRoot)
            || string.Equals(Path.GetFullPath(source.TranscriptRoot), Path.GetFullPath(target.TranscriptRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native account roots must be distinct absolute paths.");
        var key = target.Provider switch
        {
            ProviderKind.Claude => "CLAUDE_CONFIG_DIR",
            ProviderKind.Codex => "CODEX_HOME",
            _ => throw new InvalidOperationException("This provider has no verified native transcript mechanism."),
        };
        if (!configuration.Environment.TryGetValue(key, out var configured)
            || !string.Equals(Path.GetFullPath(configured), Path.GetFullPath(target.TranscriptRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The destination CLI must use its own account root.");
        RejectLinks(source.TranscriptRoot);
        RejectLinks(target.TranscriptRoot);
    }

    private static List<(string Source, string Target)> ValidateManifest(HandoffAccount source, HandoffAccount target, NativeTranscript native)
    {
        if (string.IsNullOrWhiteSpace(native.SessionId) || native.ArtifactPaths.Count == 0
            || !native.ArtifactPaths.Contains(native.ResumeRelativePath, StringComparer.Ordinal))
            throw new InvalidOperationException("The native manifest must include its resume transcript.");
        if (source.Provider == ProviderKind.Claude && native.Mode != NativeResumeMode.ClaudeId
            || source.Provider == ProviderKind.Codex && native.Mode != NativeResumeMode.CodexPath)
            throw new InvalidOperationException("The native resume mechanism does not match the provider.");

        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in native.ArtifactPaths)
        {
            var from = UnderRoot(source.TranscriptRoot, relative);
            var to = UnderRoot(target.TranscriptRoot, relative);
            var normalized = relative.Replace('\\', '/');
            var allowed = source.Provider switch
            {
                ProviderKind.Claude => normalized.StartsWith("projects/", StringComparison.Ordinal)
                    && normalized.EndsWith(".jsonl", StringComparison.Ordinal),
                ProviderKind.Codex => (normalized.StartsWith("sessions/", StringComparison.Ordinal) || normalized.StartsWith("archived_sessions/", StringComparison.Ordinal))
                    && (normalized.EndsWith(".jsonl", StringComparison.Ordinal) || normalized.EndsWith(".jsonl.zst", StringComparison.Ordinal)),
                _ => false,
            };
            if (!allowed || !seen.Add(to)) throw new InvalidOperationException("Only explicitly listed native transcript artifacts may be copied.");
            RejectLinks(from);
            RejectLinks(to);
            if (!File.Exists(from)) throw new InvalidOperationException("A required native transcript artifact is missing.");
            if (File.Exists(to)) throw new InvalidOperationException("A native destination artifact already exists.");
            result.Add((from, to));
        }
        return result;
    }

    private static string UnderRoot(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || parts.Any(part => part is "" or "." or ".." || part.Contains(':', StringComparison.Ordinal)))
            throw new InvalidOperationException("A transcript artifact must be a relative path within its account root.");
        return Path.Combine([Path.GetFullPath(root), .. parts]);
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Native transcripts cannot traverse symbolic links or junctions.");
        }
    }
}
