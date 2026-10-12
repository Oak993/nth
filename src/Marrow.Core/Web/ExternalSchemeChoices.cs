// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.3 "A short allow list of harmless schemes
// (mailto:, tel:) may be remembered per site; schemes on the block list ... are never offered", 8.1 "private tabs leave
// nothing"; ED-NEW-web-w8a-1). No Swift counterpart. The "Remember for this site" answers of the external-protocol prompt.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Web;

/// <summary>One remembered "open this app from this site" answer.</summary>
public sealed record ExternalSchemeChoice(WebProfileKind Profile, string Origin, string Scheme);

/// <summary>
/// The sites allowed to open a harmless app without asking again, per profile (browse and preview only). Only the schemes of
/// <see cref="ExternalProtocols.Rememberable"/> are ever kept. Every member runs on the UI thread.
/// </summary>
public sealed class ExternalSchemeChoices
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly List<ExternalSchemeChoice> choices = [];

    public IReadOnlyList<ExternalSchemeChoice> All => choices;

    public event Action? Changed;

    /// <summary>Whether <paramref name="origin"/> may open <paramref name="scheme"/> in a tab of <paramref name="kind"/> without asking.</summary>
    public bool IsAllowed(VisusTabKind kind, string? origin, string? scheme) =>
        VisusTabRules.MayRemember(kind) && origin is not null && scheme is not null && ExternalProtocols.Rememberable.Contains(scheme)
        && choices.Contains(new ExternalSchemeChoice(VisusTabRules.Profile(kind), origin, scheme.ToLowerInvariant()));

    /// <summary>Keeps the answer; false (and nothing kept) for a private or agent tab or a scheme that may not be remembered.</summary>
    public bool Remember(VisusTabKind kind, string? origin, string? scheme)
    {
        if (!VisusTabRules.MayRemember(kind) || origin is null || scheme is null || !ExternalProtocols.Rememberable.Contains(scheme)) return false;
        var choice = new ExternalSchemeChoice(VisusTabRules.Profile(kind), origin, scheme.ToLowerInvariant());
        if (choices.Contains(choice)) return true;
        choices.Add(choice);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes every answer of a site in a profile (Site permissions › Remove, Clear browsing data).</summary>
    public void Forget(WebProfileKind profile, string? origin = null)
    {
        if (choices.RemoveAll(choice => choice.Profile == profile && (origin is null || choice.Origin == origin)) > 0) Changed?.Invoke();
    }

    public string ToJson() => JsonSerializer.Serialize(choices, Json);

    /// <summary>The saved answers; anything a damaged or edited file holds beyond the rules is dropped.</summary>
    public static ExternalSchemeChoices FromJson(string? json)
    {
        var result = new ExternalSchemeChoices();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            foreach (var choice in JsonSerializer.Deserialize<List<ExternalSchemeChoice>>(json, Json) ?? [])
            {
                if (choice is null || choice.Profile is not (WebProfileKind.Browse or WebProfileKind.Preview)) continue;
                if (choice.Scheme is null || !ExternalProtocols.Rememberable.Contains(choice.Scheme) || choice.Origin is null) continue;
                var clean = choice with { Scheme = choice.Scheme.ToLowerInvariant() };
                if (!result.choices.Contains(clean)) result.choices.Add(clean);
            }
        }
        catch (JsonException)
        {
        }
        return result;
    }
}
