// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Taint follows text", 15.3; unit AS-7; ED-NEW-dextra-as7-14). No Swift twin.
// The home chats' taint on disk. A home chat's taint lasts until the owner's own message (12.4), and its provider session
// survives a restart with what it read still in its context, so the taint must survive too. One small document at the
// store's root: its version, and each tainted home chat's sources in the order they came (the mission journal's form).
// Fields nth does not know are kept on save; a document a newer nth wrote is left untouched.
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public static class HomeTaintJournal
{
    public const int Version = 1;

    /// <summary>The most tainted home chats the document holds; past it the save fails (and is reported), none is dropped.</summary>
    public const int MaxChats = 10_000;

    /// <summary>
    /// The document for these chats (clean ones are left out), keeping every field of <paramref name="kept"/> nth does
    /// not know. Throws when there are more than <see cref="MaxChats"/> tainted chats (the caller reports it; the taint
    /// stands in memory).
    /// </summary>
    public static JsonObject ToJson(IReadOnlyDictionary<Guid, Taint> chats, JsonObject? kept = null)
    {
        ArgumentNullException.ThrowIfNull(chats);
        var tainted = chats.Where(p => p.Value.IsTainted).OrderBy(p => p.Key).ToList();
        if (tainted.Count > MaxChats) throw new InvalidOperationException($"More than {MaxChats} home chats are tainted.");
        var body = kept?.DeepClone() as JsonObject ?? [];
        body["version"] = Version;
        var map = new JsonObject();
        foreach (var (thread, taint) in tainted) map[thread.ToString("D")] = MissionJournal.TaintJson(taint);
        body["chats"] = map;
        return body;
    }

    /// <summary>A document a newer nth wrote: nth leaves it untouched and cannot trust what it says.</summary>
    public static bool IsNewer(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body["version"] is JsonValue value && value.TryGetValue<int>(out var version) && version > Version;
    }

    /// <summary>The chats' taint, or null when the document is damaged (no version, a bad id, a source nth cannot read).</summary>
    public static IReadOnlyDictionary<Guid, Taint>? FromJson(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            if (body["version"] is not JsonValue value || !value.TryGetValue<int>(out var version) || version is < 1 or > Version) return null;
            if (body["chats"] is not JsonObject chats) return null;
            var result = new Dictionary<Guid, Taint>();
            foreach (var (key, node) in chats)
            {
                if (!Guid.TryParse(key, out var thread) || node is not JsonArray array || array.Count == 0) return null;
                var sources = new List<TaintSource>();
                foreach (var item in array)
                {
                    if (item is not JsonObject source || source["origin"] is not JsonValue origin || !origin.TryGetValue<string>(out var name)
                        || !Enum.TryParse<ContentOrigin>(name, ignoreCase: false, out var kind) || !Enum.IsDefined(kind)) return null;
                    var detail = "";
                    if (source["detail"] is { } text && (text is not JsonValue line || !line.TryGetValue(out detail!))) return null;
                    sources.Add(new TaintSource(kind, detail ?? ""));
                }
                result[thread] = new Taint(new ValueList<TaintSource>(sources));
            }
            return result;
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
