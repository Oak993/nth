using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Web;

// PORT-ONLY: approved DESIGN 5.6, 13.1; ED-NEW-web-w9-1. Pure banner/terminal-line detection, no port probing.
public sealed partial class DevServerDetector
{
    private readonly StringBuilder pending = new();
    private readonly List<string> servers = [];
    private readonly IReadOnlyCollection<string> lan;
    private readonly string? explicitUrl;

    public DevServerDetector(string? previewUrl = null, IReadOnlyCollection<string>? lanAddresses = null)
    {
        explicitUrl = ValidExplicit(previewUrl);
        lan = lanAddresses ?? [];
        if (explicitUrl is not null) servers.Add(explicitUrl);
    }
    public string? Primary => explicitUrl ?? servers.FirstOrDefault();
    public IReadOnlyList<string> Servers => servers;
    public int BufferedCharacters => pending.Length;
    public event Action? Changed;

    public void Feed(string chunk)
    {
        foreach (var c in chunk)
        {
            if (c is '\r' or '\n')
            {
                Offer(Detect(pending.ToString(), lan)); pending.Clear();
            }
            else
            {
                if (pending.Length == 16_384) pending.Remove(0, 8192);
                pending.Append(c);
            }
        }
        // Banner consumers also hand complete lines without a newline. A slash terminates the host/port;
        // chunked host/port reads are retained until the line ends, so :5 never wins before :5173 arrives.
        if (chunk.EndsWith("/", StringComparison.Ordinal)) { Offer(Detect(pending.ToString(), lan)); pending.Clear(); }
    }

    public void Offer(IEnumerable<string> urls)
    {
        var changed = false;
        foreach (var url in urls)
            if (ValidExplicit(url) is { } valid && !servers.Contains(valid, StringComparer.Ordinal)) { servers.Add(valid); changed = true; }
        if (changed) Changed?.Invoke();
    }

    public static string? ValidExplicit(string? value) => value is not null && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Port > 0 ? value : null;

    public static IReadOnlyList<string> Detect(string line, IReadOnlyCollection<string>? lanAddresses = null)
    {
        if (line.Length > 16_384) line = line[..16_384];
        var clean = Ansi().Replace(line, "");
        var urls = new List<string>();
        var matches = Urls().Matches(clean);
        foreach (Match match in matches)
        {
            var text = match.Value.TrimEnd(')', ',', '.', ';');
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Port <= 0) continue;
            var host = uri.IdnHost.Trim('[', ']');
            var wildcard = host is "0.0.0.0" or "::";
            if (!(wildcard || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1"
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || (lanAddresses?.Contains(host, StringComparer.OrdinalIgnoreCase) ?? false))) continue;
            var normalized = wildcard ? new UriBuilder(uri) { Host = "localhost" }.Uri.AbsoluteUri : uri.AbsoluteUri;
            if (!urls.Contains(normalized, StringComparer.Ordinal)) urls.Add(normalized);
        }
        // Never reinterpret a public URL's log line as a local server just because it also says "port".
        if (matches.Count != 0) return urls;
        var port = PortBanner().Match(clean);
        if (port.Success && int.TryParse(port.Groups["port"].Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 65535)
            urls.Add("http://localhost:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
        return urls;
    }

    [GeneratedRegex("\u001b(?:\\[[0-?]*[ -/]*[@-~]|\\][^\u0007\u001b]*(?:\u0007|\u001b\\\\))", RegexOptions.CultureInvariant)]
    private static partial Regex Ansi();
    [GeneratedRegex("https?://[^\\s<>\\\"'`|]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Urls();
    [GeneratedRegex(@"^\s*(?:listening on port |Serving HTTP on (?:0\.0\.0\.0|::|localhost) port |\*\* Angular Live Development Server is listening on localhost:)(?<port>[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PortBanner();
}
