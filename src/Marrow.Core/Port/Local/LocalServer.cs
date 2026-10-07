using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Local;

// PORT-ONLY: OD-0114 / ED-NEW-ma-3. A keyless server is an independent entry, with no hosted subscription quota.
public sealed record LocalServer
{
    public LocalServer(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Use an HTTP server address.", nameof(address));
        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTP server address without credentials, a query or a fragment.", nameof(address));
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/v1", StringComparison.Ordinal)) path += "/v1";
        BaseURL = new UriBuilder(uri) { Path = path + "/" }.Uri;
        Id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(BaseURL.AbsoluteUri)));
    }

    public string Id { get; }
    public Uri BaseURL { get; }
    public ProviderKind Provider => ProviderKind.Local;
    public bool HasLimit => false;
    public string LimitLabel => "No limit";
    public string PrivacyLabel => BaseURL.IsLoopback ? "Stays on this PC" : IsPrivateAddress ? "Stays on your network" : "Uses " + BaseURL.Host;
    public string LimitSummary => BaseURL.IsLoopback ? "No limit, stays on this PC" : IsPrivateAddress ? "No limit, stays on your network" : "No limit, uses " + BaseURL.Host;
    private bool IsPrivateAddress
    {
        get
        {
            if (!IPAddress.TryParse(BaseURL.Host.Trim('[', ']'), out var ip)) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var bytes = ip.GetAddressBytes();
            return ip.AddressFamily == AddressFamily.InterNetwork
                ? bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254)
                : ip.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
        }
    }
    public Uri NativeURL(string path) => new(new Uri(BaseURL, "../"), path.TrimStart('/'));
}

public sealed record LocalCapabilities(bool? ToolCalling = null, int ContextWindow = 4096, bool ContextReported = false, bool Vision = false);

public sealed record LocalModel(LocalServer Server, string Id, string Name, LocalCapabilities Capabilities)
{
    // Existing model plumbing persists this opaque selection; only the session sends the server's original model id.
    public string SelectionId => "local/" + Server.Id + "/" + Uri.EscapeDataString(Id);
    public ModelOption Option => new(SelectionId, Name, Server.BaseURL.Authority + " · " + Server.LimitSummary + " · "
        + (Capabilities.ToolCalling switch { true => "Tool calling supported", false => "Tool calling unsupported", _ => "Tool support unknown" })
        + " · " + Capabilities.ContextWindow + (Capabilities.ContextReported ? " context" : " context fallback"));
}

public sealed record LocalEntry(LocalServer Server, IReadOnlyList<LocalModel> Models, LocalServerKind Kind = LocalServerKind.Unknown);
public sealed record LocalTarget(LocalServer Server, LocalModel Model, IReadOnlyList<LocalModel> Models);
