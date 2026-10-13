using System.Text.Json;

// PORT-ONLY: web surfaces W10, Settings > Visus > "Show search suggestions" (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2 "Suggestions come from
// history and open tabs only, no remote suggestion calls unless the user enables them", 9 "Show search suggestions (off)"; ED-NEW-web-w10-7).
// No Swift counterpart. With the switch on, what is typed in the address field is also sent to the chosen search engine to ask for suggested
// searches. This file is the pure part: what may be sent at all, where, and how the answer is read. Nothing here touches the network.

namespace Marrow.Core.Web;

public static class SearchSuggestionRules
{
    /// <summary>The fewest characters worth asking about.</summary>
    public const int MinQueryLength = 2;

    /// <summary>The most characters ever sent; a longer text is not a search phrase.</summary>
    public const int MaxQueryLength = 100;

    /// <summary>The most suggestions kept from an answer.</summary>
    public const int MaxSuggestions = 5;

    /// <summary>The most remote suggestions the list shows (the typed row and the reader's own tabs and history keep the rest).</summary>
    public const int MaxShown = 3;

    /// <summary>The longest answer read, in bytes.</summary>
    public const int MaxResponseBytes = 16 * 1024;

    /// <summary>
    /// Whether the typed text may be sent: a search phrase only. An address, an app link, a local path, text with a scheme or credentials
    /// in it, text that looks like an email address or holds nine or more digits, and anything outside the length range never leaves the PC. A private tab never asks (the caller checks the tab).
    /// </summary>
    public static bool MaySend(string? typed, SearchEngine engine, Func<string, bool>? isRegisteredScheme = null)
    {
        var text = (typed ?? "").Trim();
        if (text.Length < MinQueryLength || text.Length > MaxQueryLength) return false;
        if (text.Any(char.IsControl)) return false;
        // Text that looks like an email address or a login, or holds a long number (a card, an account, a phone), is not a search to share before it is committed.
        if (text.Contains('@', StringComparison.Ordinal) || text.Count(char.IsDigit) >= 9) return false;
        return VisusAddress.Resolve(text, engine, isRegisteredScheme) is { Action: AddressAction.Search };
    }

    /// <summary>The address to ask, or null for a text that may not be sent. Always https and always a fixed host per engine.</summary>
    public static string? RequestUrl(SearchEngine engine, string? typed, Func<string, bool>? isRegisteredScheme = null)
    {
        if (!MaySend(typed, engine, isRegisteredScheme)) return null;
        var q = Uri.EscapeDataString(typed!.Trim());
        return engine switch
        {
            SearchEngine.Bing => "https://api.bing.com/osjson.aspx?query=" + q,
            SearchEngine.Google => "https://suggestqueries.google.com/complete/search?client=firefox&q=" + q,
            SearchEngine.Brave => "https://search.brave.com/api/suggest?q=" + q,
            SearchEngine.Startpage => "https://www.startpage.com/suggestions?format=opensearch&q=" + q,
            _ => "https://duckduckgo.com/ac/?type=list&q=" + q,
        };
    }

    /// <summary>
    /// The suggested searches in an answer, at most <see cref="MaxSuggestions"/>: the OpenSearch shape every engine here uses
    /// (<c>["query", ["a", "b"]]</c>), else an object with a "suggestions" list. Anything else, or text that is not JSON, is no suggestions. A
    /// suggestion that is empty, too long, or has control characters is dropped, and so is one equal to what was typed.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? body, string? typed = null)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];
        var list = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            JsonElement items = default;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2 && root[1].ValueKind == JsonValueKind.Array) items = root[1];
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("suggestions", out var named) && named.ValueKind == JsonValueKind.Array) items = named;
            if (items.ValueKind != JsonValueKind.Array) return [];
            var asked = (typed ?? "").Trim();
            foreach (var item in items.EnumerateArray())
            {
                var text = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object when item.TryGetProperty("phrase", out var phrase) && phrase.ValueKind == JsonValueKind.String => phrase.GetString(),
                    JsonValueKind.Object when item.TryGetProperty("text", out var shown) && shown.ValueKind == JsonValueKind.String => shown.GetString(),
                    _ => null,
                };
                text = text?.Trim();
                if (string.IsNullOrEmpty(text) || text.Length > MaxQueryLength || text.Any(char.IsControl)) continue;
                if (string.Equals(text, asked, StringComparison.OrdinalIgnoreCase) || list.Contains(text, StringComparer.OrdinalIgnoreCase)) continue;
                list.Add(text);
                if (list.Count >= MaxSuggestions) break;
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return list;
    }
}
