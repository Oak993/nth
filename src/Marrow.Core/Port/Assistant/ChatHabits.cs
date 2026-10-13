// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2 "the owner doing the same thing by hand three times (the same kind of chat, the same folder, the same day)", 5.4; unit AS-11;
// ED-NEW-dextra-as11-4). No Swift twin.
// The owner's chats read as habits for the spotter: his own top-level chats (not Dextra's missions or home chats, not heads or helpers), grouped by project and by
// what the title says without its numbers. A chat hidden from the Assistant is not read at all (5.4); one on a local model or a private account is kept, but marked
// local, so its words never go to a cloud model. Pure: no clock, no store.
using System.Globalization;
using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Assistant;

public static class ChatHabits
{
    /// <summary>How far back the owner's chats are read.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(35);

    /// <summary>The words of a title that make its kind: the first few, in lower case, without digits or marks.</summary>
    public const int KindWords = 4;

    /// <summary>The longest a habit's label runs (masked first, then cut).</summary>
    public const int LabelChars = 80;

    /// <summary>A title shorter than this says nothing about its kind.</summary>
    public const int MinKindChars = 6;

    private static readonly HashSet<string> Blank = new(StringComparer.Ordinal) { "new chat", "untitled", "new thread", "chat" };

    /// <summary>
    /// The habits in these chats: for each project and kind of title, when the owner started it. <paramref name="facts"/> says whether a chat is hidden,
    /// local or private (FeedChatFacts.From); <paramref name="project"/> names a project. A kind with fewer than three starts is not returned.
    /// </summary>
    public static IReadOnlyList<ChatHabit> Of(IEnumerable<ChatThread> chats, DateTimeOffset now, Func<ChatThread, FeedChatFacts> facts, Func<Guid, string?> project)
    {
        ArgumentNullException.ThrowIfNull(chats);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(project);
        var groups = new Dictionary<(Guid Project, string Kind), (string Label, DateTimeOffset Latest, bool Local, List<DateTimeOffset> Starts)>();
        foreach (var chat in chats)
        {
            // His own work only: a mission, a home chat, a head or a helper is Dextra's or Hydra's, not a thing he did by hand.
            if (chat.MissionID is not null || chat.AssistantID is not null || chat.ParentThreadID is not null || chat.IsArchived) continue;
            if (now - chat.CreatedAt > Lookback || chat.CreatedAt > now) continue;
            var about = facts(chat);
            if (about.Hidden) continue;
            // The label is shown in a suggestion, written into a duty's instructions and may go to a model: a title is data, so a secret in it is masked and a long one cut.
            var title = FeedShield.Excerpt(chat.Title, LabelChars);
            var kind = Kind(title);
            if (kind.Length < MinKindChars || Blank.Contains(kind)) continue;
            var key = (chat.ProjectID, kind);
            var local = about.IsLocalModel || about.IsPrivateAccount;
            if (groups.TryGetValue(key, out var found))
            {
                found.Starts.Add(chat.CreatedAt);
                groups[key] = (chat.CreatedAt > found.Latest ? title : found.Label, chat.CreatedAt > found.Latest ? chat.CreatedAt : found.Latest, found.Local || local, found.Starts);
            }
            else groups[key] = (title, chat.CreatedAt, local, [chat.CreatedAt]);
        }
        return [.. groups.Where(g => g.Value.Starts.Count >= 3 && project(g.Key.Project) is not null)
            .OrderBy(g => g.Key.Project).ThenBy(g => g.Key.Kind, StringComparer.Ordinal)
            .Select(g => new ChatHabit(g.Key.Project.ToString("N") + ":" + g.Key.Kind.Replace(' ', '-'), g.Value.Label, g.Key.Project, project(g.Key.Project)!, [.. g.Value.Starts.Order()])
            {
                LocalOnly = g.Value.Local,
            })];
    }

    /// <summary>A title's kind: lower case, letters only, the first few words ("Re-price listings 14" and "re-price listings 21" are one kind).</summary>
    public static string Kind(string title)
    {
        var text = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetter(c)) text.Append(c);
            else if (char.IsWhiteSpace(c) || c is '-' or '_' or ':' or '/') text.Append(' ');
        }
        var words = text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(KindWords);
        return string.Join(' ', words);
    }
}
