// PORT-ONLY: first-release feature unit UE-2. The public listings "Check now" reads the latest version from (BUILD-PLAN.md 2.4), behind a seam that fails closed.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Updates;

/// <summary>The latest version a public listing names for a key (a provider id), or null when there is no listing for it.</summary>
public interface IVersionListing
{
    Task<string?> Latest(string key, CancellationToken cancellation);
}

public sealed class NoVersionListing : IVersionListing
{
    public Task<string?> Latest(string key, CancellationToken cancellation) => Task.FromResult<string?>(null);
}

/// <summary>
/// The listings, one per provider that has one: Claude Code's latest document on the npm registry and Codex's latest release on GitHub. Every other provider has no listing, so its row says
/// "Checks when updated". UNVERIFIED (ED-308): the two URLs and the shapes below are from the pages as the author knows them, not read in this session; the port's network
/// inventory entry names them. Parsing never guesses: anything that is not the expected shape is "no listing".
/// </summary>
public static class PublicVersionListings
{
    public const string ClaudeCodeUrl = "https://registry.npmjs.org/@anthropic-ai/claude-code/latest";

    public const string CodexUrl = "https://api.github.com/repos/openai/codex/releases/latest";

    /// <summary>The tag prefix of Codex's releases (skills-PLAN.md 6 names them rust-v0.136.0).</summary>
    public const string CodexTagPrefix = "rust-v";

    public static string? ParseNpmLatest(string json) => Property(json, "version");

    public static string? ParseGithubLatestRelease(string json, string tagPrefix)
    {
        var tag = Property(json, "tag_name");
        if (tag is null || !tag.StartsWith(tagPrefix, StringComparison.Ordinal)) return null;
        var rest = tag[tagPrefix.Length..];
        return rest.Length == 0 ? null : rest;
    }

    private static string? Property(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A listing through a fetch function. The default fetch throws, so a listing nobody wired to the network cannot reach it (fails closed); a fetch that throws is the caller's to handle (the orchestrator shows "Checks when updated").</summary>
public sealed class FetchedVersionListing : IVersionListing
{
    private readonly Func<Uri, CancellationToken, Task<string>> fetch;

    public FetchedVersionListing() : this((_, _) => throw new InvalidOperationException("No fetch is wired to this version listing."))
    {
    }

    public FetchedVersionListing(Func<Uri, CancellationToken, Task<string>> fetch) => this.fetch = fetch;

    public async Task<string?> Latest(string key, CancellationToken cancellation)
    {
        switch (key)
        {
            case "claude":
                return PublicVersionListings.ParseNpmLatest(await fetch(new Uri(PublicVersionListings.ClaudeCodeUrl), cancellation));
            case "codex":
                return PublicVersionListings.ParseGithubLatestRelease(await fetch(new Uri(PublicVersionListings.CodexUrl), cancellation), PublicVersionListings.CodexTagPrefix);
            default:
                return null;
        }
    }
}

/// <summary>Version texts as CLIs print them ("2.1.3 (Claude Code)", "codex-cli 0.136.0") and listings give them ("2.1.3"): compared by the first dotted number inside, plain equality, never order.</summary>
public static partial class VersionText
{
    [GeneratedRegex(@"\d+(?:\.\d+)+")] private static partial Regex Dotted();

    public static string Number(string text)
    {
        var match = Dotted().Match(text);
        return match.Success ? match.Value : text.Trim();
    }

    public static bool Same(string? installed, string? latest) => installed is not null && latest is not null && Number(installed) == Number(latest);
}
