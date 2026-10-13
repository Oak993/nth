// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2 "Web page watch", 16.2 "AS-10 (Visus)"; canvas 5; unit AS-10; ED-NEW-dextra-as10-7). No Swift twin.
// Looks at a web page on a schedule with plain HTTP: GET only, no sign-in, no cookies, no scripts, a proxy-free direct
// connection that refuses loopback, private and link-local addresses (a duty an agent proposed must not become a probe of the
// owner's router or a cloud metadata service), a size cap and a time cap. A page that needs scripts or a sign-in is refused with
// words for the owner ("needs the browser"): Visus is not in the tree yet, so nothing here ever drives a browser. A page that is
// down is reported as down; the last text is never offered as if it were current. The condition is judged on the region of the
// page's text between the duty's markers: changed (with a line diff), a price below an amount, or a text. A condition fires when
// it becomes true, not at every look while it stays true.
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public enum PageReadStatus { Ok, Unreachable, Blocked, TooLarge, NotText, NeedsBrowser, SignIn, Missing }

/// <summary>What one read of a page gave: its text (cut to the region), or why not.</summary>
public sealed record PageReading(PageReadStatus Status, string? Text = null, string? Problem = null)
{
    public bool Ok => Status == PageReadStatus.Ok;
}

/// <summary>What one look at a watched page decided. <see cref="State"/> is kept for the next look.</summary>
public sealed record PageCheck(bool Fired, string? Detail, string? Problem, JsonObject State, string? Diff = null)
{
    public bool Failed => Problem is not null;
}

public sealed class WebWatcherOptions
{
    /// <summary>A handler for tests. Null: a direct SocketsHttpHandler with the connect-time address guard.</summary>
    public HttpMessageHandler? Handler { get; init; }

    /// <summary>Allow loopback and private addresses. A test with a local fixture sets it; the product never does.</summary>
    public bool AllowLocalAddresses { get; init; }

    public int MaxBytes { get; init; } = 2 * 1024 * 1024;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public int MaxRedirects { get; init; } = 5;

    /// <summary>The longest region kept between looks (and shown in a diff).</summary>
    public int RegionLimit { get; init; } = 20_000;
}

public sealed partial class WebWatcher : IDisposable
{
    public const string UserAgent = "nth-page-watch/1 (read-only)";

    private readonly WebWatcherOptions o;
    private readonly HttpClient client;

    public WebWatcher(WebWatcherOptions? options = null)
    {
        o = options ?? new WebWatcherOptions();
        client = new HttpClient(o.Handler ?? Direct(o.AllowLocalAddresses), disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => client.Dispose();

    // ---- reading ----

    /// <summary>Reads a page and cuts out the region between the markers (either may be empty: the whole page).</summary>
    public async Task<PageReading> Read(string url, string? startMarker, string? endMarker, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return new PageReading(PageReadStatus.Blocked, Problem: "That is not a web address.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(o.Timeout);
        try
        {
            for (var hop = 0; hop <= o.MaxRedirects; hop++)
            {
                if (GuardRefusal(uri) is { } refused) return new PageReading(PageReadStatus.Blocked, Problem: refused);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.1");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location is not { } next) return new PageReading(PageReadStatus.Unreachable, Problem: "The page redirects nowhere.");
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                    if (LooksLikeSignIn(uri)) return new PageReading(PageReadStatus.SignIn, Problem: SignInWords);
                    continue;
                }
                if (status is 401 or 403 or 407) return new PageReading(PageReadStatus.SignIn, Problem: SignInWords);
                if (status >= 400) return new PageReading(PageReadStatus.Unreachable, Problem: $"The page answered {status.ToString(CultureInfo.InvariantCulture)} {response.ReasonPhrase}.");
                var kind = response.Content.Headers.ContentType?.MediaType ?? "text/html";
                if (!(kind.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || kind.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || kind is "application/json" or "application/xml" or "application/xhtml+xml"))
                    return new PageReading(PageReadStatus.NotText, Problem: $"The page is {kind}, not text.");
                var bytes = await Bounded(response, limit.Token).ConfigureAwait(false);
                if (bytes is null) return new PageReading(PageReadStatus.TooLarge, Problem: $"The page is larger than {(o.MaxBytes / 1024 / 1024).ToString(CultureInfo.InvariantCulture)} MB, so nth does not read it.");
                var raw = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
                var isHtml = kind.Contains("html", StringComparison.OrdinalIgnoreCase);
                var text = isHtml ? HtmlText.ToText(raw) : Normalize(raw);
                if (isHtml && HtmlText.NeedsBrowser(raw, text)) return new PageReading(PageReadStatus.NeedsBrowser, Problem: NeedsBrowserWords);
                return Cut(text, startMarker, endMarker);
            }
            return new PageReading(PageReadStatus.Unreachable, Problem: $"The page redirected more than {o.MaxRedirects.ToString(CultureInfo.InvariantCulture)} times.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PageReading(PageReadStatus.Unreachable, Problem: "The page did not answer in time.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or SocketException or InvalidOperationException or UriFormatException)
        {
            return new PageReading(PageReadStatus.Unreachable, Problem: "The page could not be reached: " + (error.InnerException?.Message ?? error.Message));
        }
    }

    public const string NeedsBrowserWords = "This page builds itself with scripts, so it needs the browser. nth cannot watch it yet.";

    public const string SignInWords = "This page asks for a sign-in. A watch never signs in.";

    private PageReading Cut(string text, string? startMarker, string? endMarker)
    {
        var begin = 0;
        var end = text.Length;
        if (!string.IsNullOrEmpty(startMarker))
        {
            var at = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return new PageReading(PageReadStatus.Missing, Problem: $"“{startMarker}” is not on the page any more, so nth cannot find the part to watch.");
            begin = at + startMarker.Length;
        }
        if (!string.IsNullOrEmpty(endMarker))
        {
            var at = text.IndexOf(endMarker, begin, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return new PageReading(PageReadStatus.Missing, Problem: $"“{endMarker}” is not on the page any more, so nth cannot find the part to watch.");
            end = at;
        }
        var region = text[begin..end].Trim();
        if (region.Length > o.RegionLimit) region = region[..o.RegionLimit];
        return new PageReading(PageReadStatus.Ok, region);
    }

    private async Task<byte[]?> Bounded(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > o.MaxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > o.MaxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        Encoding encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset.Trim('"')); }
            catch (ArgumentException) { encoding = Encoding.UTF8; }
        }
        return encoding.GetString(bytes);
    }

    private static string Normalize(string raw)
    {
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => WhitespaceRun().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    [GeneratedRegex(@"[ \t ]+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();

    private static bool LooksLikeSignIn(Uri uri) =>
        SignInPath().IsMatch(uri.AbsolutePath);

    [GeneratedRegex(@"/(log-?in|sign-?in|sso|oauth2?|authorize|auth)(/|$|\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignInPath();

    // ---- the guard ----

    /// <summary>Why a web address may not be read, or null. Literal addresses and "localhost" are judged here; names are judged again when they connect.</summary>
    public string? GuardRefusal(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) return "Only http and https pages can be watched.";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "An address with a name or password in it is not read.";
        if (o.AllowLocalAddresses) return null;
        var host = uri.IdnHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return "That address is on this PC or its network, so it is not watched.";
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal) && IsLocal(literal)) return "That address is on this PC or its network, so it is not watched.";
        return null;
    }

    /// <summary>Loopback, private, link-local, unique-local, multicast and unspecified addresses, and IPv4 ones inside IPv6.</summary>
    public static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0 || b[0] == 10 || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168) || (b[0] == 192 && b[1] == 0 && b[2] == 0) || (b[0] == 198 && b[1] is 18 or 19) || b[0] >= 224;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC || address.Equals(IPAddress.IPv6Any);
        return true;
    }

    // A direct connection (no proxy: a proxy would receive the name and resolve it where nth cannot check) that resolves the name
    // itself and refuses every local address, so a name that points at the owner's network is refused when it connects.
    private static SocketsHttpHandler Direct(bool allowLocal) => new()
    {
        UseProxy = false,
        UseCookies = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, token) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
            var allowed = addresses.Where(a => allowLocal || !IsLocal(a)).ToArray();
            if (allowed.Length == 0) throw new HttpRequestException("That name points at this PC or its network, so it is not watched.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    // ---- judging ----

    /// <summary>
    /// One look: reads the page and judges the duty's condition against what the last look kept in <paramref name="state"/>.
    /// A page that cannot be read is a problem, never a fire, and keeps the old state.
    /// </summary>
    public async Task<PageCheck> Check(PageWatchTrigger trigger, JsonObject? state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var kept = state ?? new JsonObject();
        var reading = await Read(trigger.Url, trigger.StartMarker, trigger.EndMarker, cancellationToken).ConfigureAwait(false);
        if (!reading.Ok) return new PageCheck(false, null, reading.Problem, kept);
        var region = reading.Text ?? "";
        var saved = state is { } s && s["url"]?.GetValue<string>() == trigger.Url;
        var before = saved ? state!["region"]?.GetValue<string>() : null;
        var wasSatisfied = saved && state!["satisfied"] is { } flag ? flag.GetValue<bool>() : (bool?)null;
        var next = new JsonObject { ["version"] = 1, ["url"] = trigger.Url, ["region"] = region, ["checked"] = now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) };
        switch (trigger.Condition)
        {
            case PageCondition.Changed:
            {
                if (before is null) return new PageCheck(false, null, null, next);
                var same = before == region;
                return same ? new PageCheck(false, null, null, next) : new PageCheck(true, "The page changed.", null, next, PageDiff.Make(before, region));
            }
            case PageCondition.ContainsText:
            {
                var has = region.Contains(trigger.Text ?? "", StringComparison.OrdinalIgnoreCase);
                next["satisfied"] = has;
                return new PageCheck(has && wasSatisfied != true, $"The page now contains “{trigger.Text}”.", null, next);
            }
            default:
            {
                if (PriceFinder.First(region) is not { } price)
                    return new PageCheck(false, null, "nth could not find a price in the part of the page it watches.", WithSatisfied(next, wasSatisfied));
                var below = price < (trigger.PriceBelow ?? 0);
                next["satisfied"] = below;
                next["price"] = price;
                var shown = "$" + price.ToString("0.##", CultureInfo.InvariantCulture);
                return new PageCheck(below && wasSatisfied != true, $"The price is {shown}, below ${(trigger.PriceBelow ?? 0).ToString("0.##", CultureInfo.InvariantCulture)}.", null, next);
            }
        }
    }

    private static JsonObject WithSatisfied(JsonObject next, bool? satisfied)
    {
        if (satisfied is { } value) next["satisfied"] = value;
        return next;
    }
}

/// <summary>A page's HTML as the text a person reads.</summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<(script|style|template|svg|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comments();

    [GeneratedRegex(@"</?(p|div|br|li|ul|ol|tr|td|th|table|section|article|header|footer|nav|h[1-6]|pre|blockquote|form|main|aside)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Blocks();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"<script\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"<noscript\b[^>]*>.*?(enable|requires?|turn on)[^<]{0,40}javascript", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AsksForScripts();

    public static string ToText(string html)
    {
        var text = Hidden().Replace(html, " ");
        text = Comments().Replace(text, " ");
        text = Blocks().Replace(text, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    [GeneratedRegex("""<(div|main|section)\b[^>]*\bid\s*=\s*["']?(root|app|__next|__nuxt|svelte)["']?|<app-root\b|\bng-app\b|\bdata-reactroot\b""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SinglePageRoot();

    /// <summary>
    /// A page whose own text is almost nothing but which carries scripts, or asks for them, or has the empty root a single-page app
    /// fills in, builds itself in a browser. A short page with a tracking script and a few real lines is not that.
    /// </summary>
    public static bool NeedsBrowser(string html, string text)
    {
        if (AsksForScripts().IsMatch(html) && text.Length < 400) return true;
        if (!ScriptTag().IsMatch(html)) return false;
        return text.Length < 25 || (text.Length < 200 && SinglePageRoot().IsMatch(html));
    }
}

/// <summary>The first price in a text: a currency sign or code, then an amount with thousands commas and up to two decimals.</summary>
public static partial class PriceFinder
{
    [GeneratedRegex(@"(?:[$€£¥]|\b(?:USD|EUR|GBP|PKR|Rs\.?)\s?)\s?(?<n>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WithSign();

    [GeneratedRegex(@"\b(?<n>\d{1,3}(?:,\d{3})*\.\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex Bare();

    public static decimal? First(string text)
    {
        var match = WithSign().Match(text);
        if (!match.Success) match = Bare().Match(text);
        return match.Success && decimal.TryParse(match.Groups["n"].Value.Replace(",", "", StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

/// <summary>A line diff of a watched region: what went and what came, short enough for a card.</summary>
public static class PageDiff
{
    public const int LineLimit = 300;
    public const int CharLimit = 2_000;

    public static string Make(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        var lines = a.Length <= LineLimit && b.Length <= LineLimit ? Lcs(a, b) : Sets(a, b);
        var text = string.Join("\n", lines);
        return text.Length <= CharLimit ? text : text[..CharLimit] + "\n…";
    }

    private static List<string> Lcs(string[] a, string[] b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
        var result = new List<string>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1]) result.Add("- " + a[x++]);
            else result.Add("+ " + b[y++]);
        }
        while (x < a.Length) result.Add("- " + a[x++]);
        while (y < b.Length) result.Add("+ " + b[y++]);
        return result;
    }

    private static List<string> Sets(string[] a, string[] b)
    {
        var old = new HashSet<string>(a, StringComparer.Ordinal);
        var now = new HashSet<string>(b, StringComparer.Ordinal);
        return [.. a.Where(l => !now.Contains(l)).Take(100).Select(l => "- " + l), .. b.Where(l => !old.Contains(l)).Take(100).Select(l => "+ " + l)];
    }
}
