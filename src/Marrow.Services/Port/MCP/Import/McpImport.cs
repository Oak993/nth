// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). Turns a server another app has into an nth custom server and brings it in through the very store calls "Add server" makes
// (MCPCustomEditor.Save: MCPStore.SaveCustom, then SaveCustomValues): the same model, the same validation, and the same split of values, plain ones in mcp.json and locked ones in the Credential Manager.
// A value is locked when its name or its shape says key or token. Duplicates (same name, or the same command and arguments, or the same address) are skipped, replaced or kept both.
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MCP.Import;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Port.MCP.Import;

/// <summary>Where imported servers go. The app's is <see cref="McpStoreImportTarget"/> over MCPStore; tests give their own.</summary>
public interface IMcpImportTarget
{
    IReadOnlyList<MCPCustomServer> Customs { get; }

    /// <summary>Ids no new custom may take (the catalogue's and the customs').</summary>
    IReadOnlySet<string> TakenIds { get; }

    bool IsConnected(string id);

    MCPCustomError? SaveCustom(MCPCustomServer server, string? replacing);

    bool SaveCustomValues(IReadOnlyList<(string Key, string Value, bool IsSecret)> values, string id, MCPCatalogEntry? replacingFieldsOf);

    void Refresh(string id);
}

/// <summary>The app's target: the MCP store, called on the UI thread like the editor calls it.</summary>
public sealed class McpStoreImportTarget(MCPStore store) : IMcpImportTarget
{
    public IReadOnlyList<MCPCustomServer> Customs => store.Customs;

    public IReadOnlySet<string> TakenIds
    {
        get
        {
            var taken = new HashSet<string>(MCPCatalog.Entries.Select(e => e.Id), SwiftStringComparer.Instance);
            foreach (var custom in store.Customs) taken.Add(custom.Id);
            return taken;
        }
    }

    public bool IsConnected(string id) => store.State(id).IsConnected;

    public MCPCustomError? SaveCustom(MCPCustomServer server, string? replacing) => store.SaveCustom(server, replacing);

    public bool SaveCustomValues(IReadOnlyList<(string Key, string Value, bool IsSecret)> values, string id, MCPCatalogEntry? replacingFieldsOf) => store.SaveCustomValues(values, id, replacingFieldsOf);

    public void Refresh(string id)
    {
        if (store.Entry(id) is { } entry) store.Refresh(entry);
    }
}

public sealed record McpImportFailure(string Name, string Reason);

public sealed record McpImportOutcome(int Imported, int Replaced, int Skipped, IReadOnlyList<McpImportFailure> Failed)
{
    public string Summary => McpImportWords.Done(Imported, Replaced, Skipped, Failed.Count);
}

public static partial class McpImport
{
    // a name that says it holds a secret: api_key, GITHUB_TOKEN, X-Api-Key, Authorization, client_secret, password, cookie, ...
    [GeneratedRegex(@"(key|token|secret|passw(or)?d|pwd|auth|authorization|credentials?|bearer|cookie|session|signature)(?![a-z])|(?<![a-z])(pat|private)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    // a value shaped like a key: well-known prefixes, a Bearer/Basic credential, or a long unbroken run of letters and digits (both)
    [GeneratedRegex(@"^(sk-|sk_|pk_|rk_|ghp_|gho_|ghu_|ghs_|ghr_|github_pat_|glpat-|xox[abposr]-|AKIA|AIza|ya29\.|eyJ|npm_|hf_|pplx-|gsk_|r8_|lin_api_|ntn_|secret_)|^(bearer|basic|token)\s+\S{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretValue();

    /// <summary>Whether a variable belongs in the Credential Manager (locked) rather than in mcp.json.</summary>
    public static bool IsSecret(string key, string value)
    {
        if (SecretName().IsMatch(key)) return true;
        var v = value.Trim();
        if (v.Length == 0) return false;
        if (SecretValue().IsMatch(v)) return true;
        // a long token: no blanks, no path or address, letters and digits both
        if (v.Length < 24 || v.Any(char.IsWhiteSpace) || v.Contains("://", StringComparison.Ordinal) || v.Contains('\\', StringComparison.Ordinal) || v.StartsWith('/') || v.StartsWith('.') || v.StartsWith('$') || v.StartsWith('%')) return false;
        if (v.Length > 1 && v[1] == ':') return false; // C:...
        return v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '=' or '+' or '/') && v.Any(char.IsAsciiDigit) && v.Any(char.IsAsciiLetter);
    }

    /// <summary>The server's variables as nth keeps them: duplicate names dropped (headers compare without case, as validation does), each one locked or not.</summary>
    public static IReadOnlyList<(string Key, string Value, bool IsSecret)> Values(ForeignMcpServer server)
    {
        var seen = new HashSet<string>(server.Kind == MCPCustomServer.Kind.Http ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var values = new List<(string, string, bool)>();
        foreach (var variable in server.Variables)
        {
            var key = FoundationText.Trim(variable.Key);
            if (key.Length == 0 || !seen.Add(key)) continue;
            values.Add((key, variable.Value, IsSecret(key, variable.Value)));
        }
        return values;
    }

    /// <summary>The custom server "Add server" would make for it, named <paramref name="name"/>.</summary>
    public static MCPCustomServer Draft(ForeignMcpServer server, string name)
    {
        var variables = new ValueList<MCPCustomServer.Variable>(Values(server).Select(v => new MCPCustomServer.Variable(v.Key, v.IsSecret)));
        return new MCPCustomServer(MCPCustomServer.MakeID(name), name, server.Kind)
        {
            Command = server.Command.Trimmed(),
            Args = new ValueList<string>(server.Args.Where(a => FoundationText.Trim(a).Length > 0)),
            Url = server.Url.Trimmed(),
            Env = server.Kind == MCPCustomServer.Kind.Stdio ? variables : ValueList<MCPCustomServer.Variable>.Empty,
            Headers = server.Kind == MCPCustomServer.Kind.Http ? variables : ValueList<MCPCustomServer.Variable>.Empty,
        };
    }

    /// <summary>The custom server already in nth that this one duplicates: the same name (so the same id), the same command and arguments, or the same address.</summary>
    public static MCPCustomServer? DuplicateOf(ForeignMcpServer server, IReadOnlyList<MCPCustomServer> customs)
    {
        var id = MCPCustomServer.MakeID(server.Name.Trimmed());
        var draft = Draft(server, server.Name.Trimmed());
        return customs.FirstOrDefault(c => SwiftString.Equal(c.Id, id))
            ?? customs.FirstOrDefault(c => c.ServerKind == draft.ServerKind && (draft.ServerKind == MCPCustomServer.Kind.Stdio
                ? string.Equals(c.Command.Trimmed(), draft.Command, StringComparison.OrdinalIgnoreCase) && c.Args.SequenceEqual(draft.Args, StringComparer.Ordinal)
                : string.Equals(Address(c.Url), Address(draft.Url), StringComparison.OrdinalIgnoreCase)));
    }

    private static string Address(string url) => url.Trimmed().TrimEnd('/');

    /// <summary>"name 2", "name 3", ...: the first that makes an id nobody has.</summary>
    public static string KeepBothName(string name, IReadOnlySet<string> takenIds)
    {
        for (var n = 2; ; n++)
        {
            var candidate = name.Trimmed() + " " + n;
            if (!takenIds.Contains(MCPCustomServer.MakeID(candidate))) return candidate;
        }
    }

    /// <summary>Brings the servers in, one by one, through the target; a duplicate gets <paramref name="choice"/>. Must run where the target may be called (the UI thread for the store).</summary>
    public static McpImportOutcome Import(IEnumerable<ForeignMcpServer> servers, DuplicateChoice choice, IMcpImportTarget target)
    {
        int imported = 0, replaced = 0, skipped = 0;
        var failed = new List<McpImportFailure>();
        foreach (var server in servers)
        {
            var name = server.Name.Trimmed();
            var values = Values(server);
            var existing = DuplicateOf(server, target.Customs);
            if (existing is not null && choice == DuplicateChoice.Skip) { skipped++; continue; }
            if (existing is not null && choice == DuplicateChoice.Replace)
            {
                var wasConnected = target.IsConnected(existing.Id);
                var draft = Draft(server, existing.Name) with { Id = existing.Id };
                if (target.SaveCustom(draft, existing.Id) is { } error) { failed.Add(new McpImportFailure(name, error.Message)); continue; }
                if (!target.SaveCustomValues(values, existing.Id, existing.CatalogEntry())) { failed.Add(new McpImportFailure(name, McpImportWords.ValuesNotSaved)); continue; }
                if (wasConnected) target.Refresh(existing.Id);
                replaced++;
                continue;
            }
            if (target.TakenIds.Contains(MCPCustomServer.MakeID(name))) name = KeepBothName(name, target.TakenIds); // Keep both, or a name the catalogue has
            var fresh = Draft(server, name);
            if (target.SaveCustom(fresh, null) is { } failure) { failed.Add(new McpImportFailure(name, failure.Message)); continue; }
            if (!target.SaveCustomValues(values, fresh.Id, null)) { failed.Add(new McpImportFailure(name, McpImportWords.ValuesNotSaved)); continue; }
            imported++;
        }
        return new McpImportOutcome(imported, replaced, skipped, failed);
    }
}
