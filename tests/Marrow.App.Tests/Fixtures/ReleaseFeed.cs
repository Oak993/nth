using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.App;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Fixtures;

// Builders for the GitLab release JSON the checker reads (UpdateChecker.swift:277-327, the fields its CodingKeys
// name: tag_name, name, description, released_at, upcoming_release, assets.links[].name, .url, .direct_asset_url).
// Only the shape comes from the Swift; each test chooses the values it needs.
public static class ReleaseFeed
{
    /// <summary>One asset link object (UpdateChecker.swift:283-297).</summary>
    public static JsonObject Link(string name, string url, string? directAssetUrl = null)
    {
        var link = new JsonObject { ["name"] = name, ["url"] = url };
        if (directAssetUrl is not null) link["direct_asset_url"] = directAssetUrl;
        return link;
    }

    /// <summary>One release object (UpdateChecker.swift:299-311); a null argument leaves its key out.</summary>
    public static JsonObject Release(
        string tagName, JsonObject[] links, string? releasedAt = null, bool? upcomingRelease = null,
        string? description = null, string? name = null)
    {
        var release = new JsonObject { ["tag_name"] = tagName };
        if (name is not null) release["name"] = name;
        if (description is not null) release["description"] = description;
        if (releasedAt is not null) release["released_at"] = releasedAt;
        if (upcomingRelease is { } upcoming) release["upcoming_release"] = upcoming;
        var array = new JsonArray();
        foreach (var link in links) array.Add(link);
        release["assets"] = new JsonObject { ["links"] = array };
        return release;
    }

    /// <summary>The body of the releases API answer: a JSON array of releases.</summary>
    public static string Feed(params JsonObject[] releases)
    {
        var array = new JsonArray();
        foreach (var release in releases) array.Add(release);
        return array.ToJsonString();
    }

    /// <summary>Decodes release objects as the checker does, one GitLabRelease per element (UpdateChecker.swift:194).</summary>
    public static IReadOnlyList<GitLabRelease> Decode(params JsonObject[] releases)
    {
        using var document = JsonDocument.Parse(Feed(releases));
        return document.RootElement.EnumerateArray().Select(GitLabRelease.Decode).ToArray();
    }

    /// <summary>The running app as AppInfo reports it (Core/Support/Text.swift:3-9): a version and the build channel.</summary>
    public static AppInfo Running(string version, bool isDevelopment = false) =>
        AppInfo.Read(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CFBundleShortVersionString"] = version,
        }, isDevelopment);
}
