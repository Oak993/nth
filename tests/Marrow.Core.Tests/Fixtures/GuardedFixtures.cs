using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>A label and the guarded word it must name, with the category of the card that asks about it.</summary>
public sealed record GuardedFixtureLabel(string Name, string Word, string Category);

/// <summary>A label the web guard must refuse for a reason: "payment", "account" or "captcha".</summary>
public sealed record GuardedFixtureDenial(string Name, string Refusal);

/// <summary>The same word written another way (a character a page may use for it, or none that a reader sees).</summary>
public sealed record GuardedFixtureVariant(string What, string Name, string Word);

/// <summary>One language's localised labels (tests/fixtures/web-surfaces/guarded/&lt;language&gt;.json).</summary>
public sealed record GuardedFixtureFile(
    string Language,
    IReadOnlyList<GuardedFixtureLabel> Guarded,
    IReadOnlyList<GuardedFixtureDenial> Denied,
    IReadOnlyList<string> Allowed,
    IReadOnlyList<GuardedFixtureVariant> Variants,
    IReadOnlyList<GuardedFixtureVariant> Mixed);

/// <summary>PORT-ONLY test fixture (web surfaces W22, ED-NEW-web-w22-1): reads the localised label files the guard tests run on.</summary>
public static class GuardedFixtures
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string Folder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "fixtures", "web-surfaces", "guarded"));

    public static GuardedFixtureFile Load(string language)
    {
        var path = Path.Combine(Folder(), language + ".json");
        return JsonSerializer.Deserialize<GuardedFixtureFile>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException(path + " is empty.");
    }
}
