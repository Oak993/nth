// Source: DroppyCode/UI/Markdown/LinkTitles.swift (B2 1fbf81e), :1-113.

using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;

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
            // :25-26 URLComponents(url:, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "v" })?.value
            var query = URLComponents.Create(url, resolvingAgainstBaseURL: false)?.QueryItems;
            if (query?.FirstOrDefault(item => SwiftString.Equal(item.Name, "v"))?.Value is not { } value)
                return null; // :26 guard let value = ... else { return nil }
            return Checked(value); // :27
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

    /// <summary>
    /// The title for a video, fetched at most once: null while the fetch is in flight, and null for good when
    /// there is none to have (private, deleted, offline), so the host-and-path form stands and the row never
    /// asks again. :43-56.
    /// </summary>
    /// <param name="oembedUrl">
    /// PORT-ONLY test seam (MetaAPI.cs precedent, src/Marrow.Services/Providers/MetaAPI.cs): overrides the
    /// request URL <see cref="OembedURL"/> builds, so tests can point at a loopback server (R-TEST-10) instead
    /// of the real endpoint. Every production call site passes null and gets <see cref="OembedURL"/>(id)
    /// unchanged, exactly as Swift's <c>title(forVideo:)</c> takes only <c>id</c>.
    /// </param>
    public static async Task<string?> TitleForVideo(string id, Uri? oembedUrl = null)
    {
        if (Store.Shared.Title(id) is { } known) return known; // :44
        if (!Store.Shared.Claim(id)) return null; // :45

        if ((oembedUrl ?? OembedURL(id)) is not { } url) // :46 guard let url = oembedURL(for: id)
        {
            Store.Shared.Finish(id, null); // :50
            return null; // :51
        }

        // :47 let (data, response) = try? await URLSession.shared.data(from: url)
        var fetch = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(url));
        if (fetch is not { } exchange || exchange.Response is not HTTPURLResponse { StatusCode: 200 }) // :48
        {
            Store.Shared.Finish(id, null); // :50
            return null; // :51
        }

        // :49 let payload = try? JSONDecoder().decode(OEmbed.self, from: data)
        var payload = SwiftTry.OptionalValue(() => DecodeOEmbed(exchange.Data));
        if (payload is not { } p)
        {
            Store.Shared.Finish(id, null); // :50
            return null; // :51
        }

        var title = p.Title.Trimmed(); // :53
        Store.Shared.Finish(id, title.Length == 0 ? null : title); // :54
        return title.Length == 0 ? null : title; // :55
    }

    /// <summary>Swift <c>OEmbed</c>, a private decoding target with one field. :58-60.</summary>
    private readonly record struct OEmbed(string Title);

    // :49 JSONDecoder().decode(OEmbed.self, from: data), per R-CODE-1/R-CODE-8.
    private static OEmbed DecodeOEmbed(byte[] data)
    {
        using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
        return new OEmbed(SwiftDecoding.Decode(document.RootElement, "title", SwiftDecoding.ReadString));
    }

    /// <summary>The oEmbed request URL for a video id, or null when the components make no URL. :62-69.</summary>
    private static Uri? OembedURL(string id)
    {
        var components = URLComponents.Create("https://www.youtube.com/oembed"); // :63
        if (components is null) return null;
        components = components with
        {
            QueryItems = new ValueList<URLQueryItem>(
            [
                new URLQueryItem("url", $"https://www.youtube.com/watch?v={id}"), // :65
                new URLQueryItem("format", "json"), // :66
            ]),
        }; // :64-67
        if (components.Url is not { } url) return null; // :68 return components?.url
        return new Uri(url.AbsoluteString); // :68, converted for URLSession.Data's Uri parameter (R-NET-1)
    }

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
