// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2 fallback; ED-NEW-web-w12-2). The fenced fallback between a session
// and its chat: the answer's text runs through ArtifactFenceParser as it streams, so the chat shows a chip where an
// nth-artifact block is, and each block that closes is saved as an artifact (a new one, or a new version when the chat
// already has its id). Every other event passes through untouched; with artifacts off, everything does. No Swift
// counterpart.
using System.Text;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.Services.Web;

public sealed class ArtifactFenceFilter(Guid thread, ArtifactHost host, Func<(ArtifactAuthor Author, string? Prompt)> caller)
{
    private readonly Dictionary<string, Message> messages = new(StringComparer.Ordinal);

    /// <summary>The chat whose answers this filter reads.</summary>
    public Guid Thread { get; } = thread;

    /// <summary>The events the chat should see for one event of the session (none, one, or a notice beside it).</summary>
    public IReadOnlyList<ProviderEvent> Filter(ProviderEvent @event)
    {
        if (host.Store is not { } store) return [@event];
        var output = new List<ProviderEvent>();
        switch (@event)
        {
            case ProviderEvent.MessageDelta delta:
            {
                var message = Of(delta.Id, store);
                message.Raw.Append(delta.Text);
                var step = message.Parser.Feed(delta.Text);
                Commit(store, message, step.Events, output);
                if (step.Display.Length > 0)
                {
                    message.Shown.Append(step.Display);
                    output.Insert(0, delta with { Text = step.Display });
                }
                return output;
            }
            case ProviderEvent.MessageCompleted completed:
            {
                string display;
                if (messages.Remove(completed.Id, out var message) && string.Equals(message.Raw.ToString(), completed.Text, StringComparison.Ordinal))
                {
                    var step = message.Parser.Finish();
                    Commit(store, message, step.Events, output);
                    display = message.Shown.Append(step.Display).ToString();
                }
                else
                {
                    // No deltas, or a final text that differs from them: parse it whole; blocks already saved stay saved.
                    message ??= new Message(this, store);
                    var (shown, blocks) = ArtifactFenceParser.Parse(completed.Text, message.Chip);
                    Commit(store, message, blocks, output);
                    display = shown;
                }
                output.Insert(0, completed with { Text = display });
                return output;
            }
            case ProviderEvent.TurnCompleted or ProviderEvent.Exited:
            {
                // A message the session never completed: what the parser still held is shown, an open block is saved as it is.
                foreach (var (id, message) in messages)
                {
                    var step = message.Parser.Finish();
                    if (step.Display.Length > 0) output.Add(new ProviderEvent.MessageDelta(id, step.Display));
                    Commit(store, message, step.Events, output);
                }
                messages.Clear();
                output.Add(@event);
                return output;
            }
            default:
                return [@event];
        }
    }

    private Message Of(string id, ArtifactStore store)
    {
        if (!messages.TryGetValue(id, out var message)) messages[id] = message = new Message(this, store);
        return message;
    }

    private void Commit(ArtifactStore store, Message message, IEnumerable<ArtifactFenceEvent> events, List<ProviderEvent> output)
    {
        foreach (var closed in events.OfType<ArtifactFenceEvent.Closed>())
        {
            if (!message.Saved.Add(closed.Index)) continue;
            var header = closed.Header;
            var id = message.Id(closed.Index, header);
            var known = store.Find(Thread, id);
            var kind = header.Kind ?? known?.Kind ?? Guess(closed.Content, header.Language); // an opener with only an id updates that artifact
            var title = header.Title ?? known?.Title ?? id;
            try
            {
                IReadOnlyDictionary<string, string>? files = null;
                if (kind == ArtifactKind.Project)
                {
                    files = ArtifactFenceProject.Files(closed.Content);
                    if (files.Count == 0) throw new ArtifactException("the project block has no files; fence each file with its path, like ```css path=style.css");
                }
                var (author, prompt) = caller();
                var existing = store.Find(Thread, id);
                if (existing is not null && existing.Kind == kind)
                {
                    IReadOnlyDictionary<string, string?>? changed = null;
                    if (files is not null)
                    {
                        // A block carries the whole project: files it no longer has are deleted.
                        var all = files.ToDictionary(file => file.Key, file => (string?)file.Value, StringComparer.OrdinalIgnoreCase);
                        foreach (var old in existing.Latest.Files) all.TryAdd(old, null);
                        changed = all;
                    }
                    store.Update(Thread, id, new ArtifactRevision
                    {
                        Content = files is null ? closed.Content : null,
                        Files = changed,
                        Entry = header.Entry,
                        Title = header.Title,
                    }, author, prompt);
                }
                else
                {
                    store.Create(Thread, new ArtifactDraft
                    {
                        Kind = kind,
                        Title = title,
                        Id = existing is null ? id : null, // the same id with another kind is a new artifact
                        Language = header.Language,
                        Content = files is null ? closed.Content : null,
                        Files = files,
                        Entry = header.Entry,
                    }, author, prompt);
                }
            }
            catch (Exception error) when (error is ArtifactException or IOException or UnauthorizedAccessException)
            {
                output.Add(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = $"Tabula could not save “{ArtifactStore.CleanTitle(title)}”: {error.Message}" }));
            }
        }
    }

    /// <summary>A block's kind when its opener names none: a page, an SVG, code in a named language, else a document.</summary>
    internal static ArtifactKind Guess(string content, string? language)
    {
        var start = content.TrimStart();
        if (start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return ArtifactKind.Html;
        if (start.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (start.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && start.Contains("<svg", StringComparison.OrdinalIgnoreCase))) return ArtifactKind.Svg;
        return language is { Length: > 0 } ? ArtifactKind.Code : ArtifactKind.Markdown;
    }

    private sealed class Message
    {
        private readonly ArtifactFenceFilter owner;
        private readonly ArtifactStore store;
        private readonly Dictionary<int, string> ids = [];

        public Message(ArtifactFenceFilter owner, ArtifactStore store)
        {
            this.owner = owner;
            this.store = store;
            Parser = new ArtifactFenceParser(Chip);
        }

        public ArtifactFenceParser Parser { get; }
        public StringBuilder Raw { get; } = new();
        public StringBuilder Shown { get; } = new();
        public HashSet<int> Saved { get; } = [];

        /// <summary>The chip for a block, under the id it will be saved with (resolved once, when the block opens).</summary>
        public string Chip(int index, ArtifactFenceHeader header)
        {
            var id = Id(index, header);
            var existing = header.Title is null || header.Kind is null ? store.Find(owner.Thread, id) : null; // an update may name only the id
            return ArtifactFenceParser.Chip(id, header.Title ?? existing?.Title, header.Kind ?? existing?.Kind, header.Language ?? existing?.Language);
        }

        /// <summary>
        /// The block's id: the opener's own (in normal form), which updates the chat's artifact of that id; else a new
        /// id made from its title. An opener that names an id the chat has under another kind gets a new id at once,
        /// so its chip opens the artifact the block becomes.
        /// </summary>
        public string Id(int index, ArtifactFenceHeader header)
        {
            if (ids.TryGetValue(index, out var id)) return id;
            id = ArtifactStore.NormalizeId(header.Id) ?? store.NewId(owner.Thread, header.Title ?? "artifact");
            if (header.Kind is { } kind && store.Find(owner.Thread, id) is { } existing && existing.Kind != kind)
                id = store.NewId(owner.Thread, header.Title ?? existing.Title);
            return ids[index] = id;
        }
    }
}
