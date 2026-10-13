// PORT-ONLY: web surfaces W14, Tabula's streaming render (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.4 "The pane opens the
// moment artifact_create starts streaming. Preview shows a skeleton until the document has a body; Code shows the text
// arriving"; 12 "Artifact preview refresh while streaming: every 250 ms"; ED-NEW-web-w14-1). No Swift counterpart. The
// artifacts a chat is writing right now, before the store has them: a fenced nth-artifact block while it streams (its id,
// title, kind and the text so far) and an artifact_create call that has started. The pane listens and draws a skeleton;
// the text is read at most every 250 ms, so a long block never costs more than a few copies of itself.
using Marrow.Core.Models;

namespace Marrow.Services.Web;

/// <summary>An artifact being written: where it will land (its id when known), its title and kind, and the text so far.</summary>
/// <param name="Key">The stream's own key within its chat (a message's block, or a tool call).</param>
/// <param name="Id">The id it will be saved under, when the opener names it or the block has chosen it.</param>
/// <param name="IsUpdate">The stream writes a new version of an artifact the chat has.</param>
public sealed record ArtifactStream(Guid Thread, string Key, string? Id, string? Title, ArtifactKind? Kind, string Content, bool IsUpdate, DateTimeOffset Started);

public sealed class ArtifactStreams(TimeProvider? time = null)
{
    /// <summary>The text of a stream is read at most this often (DESIGN 12: every 250 ms).</summary>
    public static readonly TimeSpan ContentInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<(Guid Thread, string Key), Entry> open = [];

    /// <summary>A stream began or its text moved on. Raised on the thread that fed it (the chat's event loop).</summary>
    public event Action<ArtifactStream>? Changed;

    /// <summary>A stream ended: its block closed (the store has it now, or refused it), or its turn ended.</summary>
    public event Action<ArtifactStream>? Ended;

    /// <summary>The streams of a chat, oldest first.</summary>
    public IReadOnlyList<ArtifactStream> Of(Guid thread)
    {
        lock (gate) return open.Values.Where(entry => entry.Value.Thread == thread).Select(entry => entry.Value).OrderBy(stream => stream.Started).ToList();
    }

    /// <summary>A stream starts (again: a second begin for the same key only updates what it knows).</summary>
    public void Begin(Guid thread, string key, string? id, string? title, ArtifactKind? kind, bool isUpdate = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArtifactStream stream;
        lock (gate)
        {
            if (open.TryGetValue((thread, key), out var entry))
            {
                entry.Value = entry.Value with { Id = id ?? entry.Value.Id, Title = title ?? entry.Value.Title, Kind = kind ?? entry.Value.Kind, IsUpdate = isUpdate || entry.Value.IsUpdate };
            }
            else
            {
                entry = new Entry(new ArtifactStream(thread, key, id, title, kind, "", isUpdate, clock.GetUtcNow()));
                open[(thread, key)] = entry;
            }
            stream = entry.Value;
        }
        Changed?.Invoke(stream);
    }

    /// <summary>
    /// The stream's text moved on. <paramref name="content"/> is read only when the last read is at least
    /// <see cref="ContentInterval"/> old (or <paramref name="force"/>), so a fast stream is copied four times a second.
    /// </summary>
    public void Update(Guid thread, string key, Func<string> content, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArtifactStream stream;
        lock (gate)
        {
            if (!open.TryGetValue((thread, key), out var entry)) return;
            var now = clock.GetUtcNow();
            if (!force && entry.ReadAt is { } read && now - read < ContentInterval) return;
            entry.ReadAt = now;
            entry.Value = entry.Value with { Content = content() };
            stream = entry.Value;
        }
        Changed?.Invoke(stream);
    }

    /// <summary>A stream ended; nothing when it is not open.</summary>
    public void End(Guid thread, string key)
    {
        ArtifactStream stream;
        lock (gate)
        {
            if (!open.Remove((thread, key), out var entry)) return;
            stream = entry.Value;
        }
        Ended?.Invoke(stream);
    }

    /// <summary>Every stream of a chat ends (its turn ended or its session went away).</summary>
    public void EndAll(Guid thread, Func<ArtifactStream, bool>? which = null)
    {
        List<ArtifactStream> ended;
        lock (gate)
        {
            ended = open.Values.Select(entry => entry.Value).Where(stream => stream.Thread == thread && (which?.Invoke(stream) ?? true)).ToList();
            foreach (var stream in ended) open.Remove((thread, stream.Key));
        }
        foreach (var stream in ended) Ended?.Invoke(stream);
    }

    /// <summary>
    /// A tool call that creates an artifact (6.4 "the moment artifact_create starts"): nth-ui's artifact_create as each provider
    /// names it ("nth-ui · artifact_create", "mcp__nth-ui__artifact_create", "Call nth-ui artifact_create").
    /// </summary>
    public static bool IsCreateCall(string? title, string? detail)
    {
        var text = string.Concat(((title ?? "") + " " + (detail ?? "")).Where(c => !char.IsWhiteSpace(c) && c != '\u00B7' && c != '_' && c != '-')).ToLowerInvariant();
        return text.Contains("nthui", StringComparison.Ordinal) && text.Contains("artifactcreate", StringComparison.Ordinal);
    }

    private sealed class Entry(ArtifactStream value)
    {
        public ArtifactStream Value { get; set; } = value;

        public DateTimeOffset? ReadAt { get; set; }
    }
}
