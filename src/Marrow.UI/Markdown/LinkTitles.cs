// Source: DroppyCode/UI/Markdown/LinkTitles.swift (B2 1fbf81e), :1-113. Partial port: VideoID's "?v=<id>" case
// (:25-27) and TitleForVideo/OembedURL/OEmbed (:43-69) are not ported, each with a STOP note below (see report).

using Marrow.Core.Port;

namespace Marrow.UI.Markdown;

/// <summary>
/// The video behind a bare video link in chat: a YouTube watch link reads as the video's own title instead of
/// "youtube.com/watch". Swift :1-9.
/// </summary>
public static class LinkTitles
{
    /// <summary>
    /// The video id a link carries, or null for anything that is not a video link. :13-31.
    /// </summary>
    // STOP UI.Markdown.LinkTitles #1 (missing dependency, PR-THEME-134). Swift :25-27 falls back to
    //   URLComponents(url:, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "v" })?.value
    // for the "?v=<id>" shape ("youtube.com/watch?v=..."). SwiftURL (src/Marrow.Core/Port/SwiftURL.cs) exposes
    // no public Query or QueryItems reader; its one percent-decoder, RFC3986Parser.PercentDecode, is `internal`
    // to Marrow.Core, unreachable from Marrow.UI, and no F.URL query-item unit is built. Needed to proceed: a
    // public, evidenced query-item reader on SwiftURL (or a new F.URL sub-unit) reachable from Marrow.UI. Done
    // so far: the youtu.be and the /shorts, /embed, /live, /v path-prefix shapes below are implemented and
    // tested (LinkTitlesTests.cs). For a youtube.com-family host whose first path segment is not one of those
    // four (a plain "/watch?v=..." included), this returns null rather than guess: the caller's existing
    // "not a video link" fallback applies (:29), so nothing incorrect is shown, but that shape's title is not
    // fetched until the dependency above lands.
    public static string? VideoID(SwiftURL url)
    {
        if (url.Host is not { } host) return null; // :14 url.host?.lowercased() guard
        var lowered = SwiftString.Lowercased(host); // :14
        var bare = SwiftString.HasPrefix(lowered, "www.") ? SwiftString.DropFirst(lowered, 4) : lowered; // :15
        var segments = SwiftString.Split(url.Path, "/"); // :16 url.path.split(separator: "/").map(String.init)

        if (SwiftString.Equal(bare, "youtu.be")) // :18
        {
            if (segments.Count == 0) return null; // :19 guard let id = segments.first
            return Checked(segments[0]); // :20
        }

        if (SwiftString.Equal(bare, "youtube.com") || SwiftString.Equal(bare, "m.youtube.com") ||
            SwiftString.Equal(bare, "music.youtube.com") || SwiftString.Equal(bare, "youtube-nocookie.com")) // :21
        {
            if (segments.Count >= 2 && IsRecognizedPathPrefix(segments[0])) // :22
                return Checked(segments[1]); // :23
            return null; // :25-27, see the STOP note above pending the query-item dependency
        }

        return null; // :29 default
    }

    // :22 ["shorts", "embed", "live", "v"].contains(segments[0])
    private static bool IsRecognizedPathPrefix(string segment) =>
        SwiftString.Equal(segment, "shorts") || SwiftString.Equal(segment, "embed") ||
        SwiftString.Equal(segment, "live") || SwiftString.Equal(segment, "v");

    /// <summary>
    /// The title for a URL, if one has been fetched. :35-38.
    /// </summary>
    public static string? Cached(SwiftURL url)
    {
        if (VideoID(url) is not { } id) return null; // :36
        return Store.Shared.Title(id); // :37
    }

    // STOP UI.Markdown.LinkTitles #2 (missing dependency, PR-THEME-135). Swift :43-56 title(forVideo:) makes a
    // GET to YouTube's oEmbed endpoint via URLSession.shared.data(from:). R-NET-1 requires every such call to
    // go through the one shared HttpClient in the F.HTTP unit (src/Marrow.Services/Port/Http.cs), documented
    // there as serving exactly this site, but Http.cs lives in Marrow.Services and Marrow.UI.csproj references
    // only Marrow.Core (Services and UI are siblings; UI cannot reach Services without a forbidden project
    // reference and a LayeringTests violation). Building the request URL (:62-69 oembedURL(for:)) is blocked
    // the same way as STOP #1: it needs URLComponents-style query-value percent-encoding, and
    // RFC3986Parser.PercentEncode is `internal` to Marrow.Core. Needed to proceed: an owner/engineering
    // decision moving this call (or a delegate to it) across the Services/UI boundary, the way MarkdownView's
    // hydra-fence rendering and MCP.swift's MCPProxy/Storage.root uses already move up a layer (CLAUDE.md /
    // HANDOFF.md section 9), plus a public query-item encoder reachable from Marrow.UI. Done so far: nothing
    // implemented at this site (no TitleForVideo, OembedURL or OEmbed in this file). The Store type these would
    // call (claim/finish/the 400-entry flush, PR-THEME-136) is implemented and fully tested below without
    // needing the network call itself.

    /// <summary>
    /// Titles by video id (:79-111). A claimed id counts as answered, so a streaming row that restarts its task
    /// on every token still asks for the same video once. Locked, not <c>MainActor</c>, because <see cref="Cached"/>
    /// runs on whichever thread builds a paragraph, warm-up included.
    /// </summary>
    private sealed class Store
    {
        public static readonly Store Shared = new(); // :83

        // :85-88
        private struct State
        {
            public Dictionary<string, string> Titles;
            public HashSet<string> Claimed;
        }

        private readonly Mutex<State> state = new(new State
        {
            Titles = new Dictionary<string, string>(SwiftStringComparer.Instance),
            Claimed = new HashSet<string>(SwiftStringComparer.Instance),
        }); // :90

        // :92-94
        public string? Title(string id) =>
            state.WithLock((ref State s) => s.Titles.TryGetValue(id, out var title) ? title : null);

        /// <summary>Whether this caller is the one that should do the fetch. :96-99.</summary>
        public bool Claim(string id) =>
            state.WithLock((ref State s) => s.Claimed.Add(id)); // Set.insert(_:).inserted

        /// <summary>
        /// The answer, title or none. Every stored title is discarded in one go, wholesale, at 400 entries, and
        /// <c>claimed</c> is never cleared, so an id whose title was flushed can never be claimed again in this
        /// run (upstream defect 49, reproduced, PR-THEME-136). :101-110.
        /// </summary>
        public void Finish(string id, string? title)
        {
            state.WithLock((ref State s) =>
            {
                if (title is { } t) // :104
                {
                    if (s.Titles.Count >= 400) s.Titles.Clear(); // :105 removeAll(keepingCapacity: true)
                    s.Titles[id] = t; // :106
                }
                s.Claimed.Add(id); // :108, unconditional: a failed fetch is never retried either
            });
        }
    }

    /// <summary>
    /// A video id is long enough to be one and made of the characters YouTube uses; anything else (a bare
    /// "/watch", a playlist link) is no video to ask a title for. :71-77.
    /// </summary>
    private static string? Checked(string value)
    {
        if (SwiftString.Count(value) < 8) return null; // :74
        foreach (var character in SwiftString.Characters(value)) // :75
        {
            if (SwiftCharacter.IsLetter(character) || SwiftCharacter.IsNumber(character) ||
                SwiftString.Equal(character, "-") || SwiftString.Equal(character, "_"))
                continue;
            return null;
        }
        return value; // :76
    }
}
