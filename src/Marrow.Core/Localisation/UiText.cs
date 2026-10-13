// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22: "every new string in the resource files"; ED-NEW-web-w22-1, -2). No Swift counterpart.
// How the app asks for one of its own words. A key names a string in the resource files (Localisation/Strings); the language is the Windows UI
// language when nth ships it and English otherwise; a key a language lacks reads in English, and a key no file has reads as itself (never a
// crash, and the key shows, so a missing one is seen at once; the tests scan the source so none is missing).
using System.Collections.Concurrent;
using System.Globalization;

namespace Marrow.Core.Localisation;

public static class UiText
{
    private sealed record Chosen(UiCatalogue Catalogue, string? Language);

    private static readonly AsyncLocal<Chosen?> scoped = new();
    private static readonly ConcurrentDictionary<string, byte> missing = new(StringComparer.Ordinal);

    // The last culture seen and the language it resolved to: Get runs for every label, so the answer is kept until the culture changes.
    private static volatile Tuple<CultureInfo, string>? lastResolved;

    /// <summary>The language the words are in now: the scope's if a test chose one, else the Windows UI language when nth ships it, else English.</summary>
    public static string Language
    {
        get
        {
            var chosen = scoped.Value;
            if (chosen?.Language is { } fixedLanguage) return fixedLanguage;
            var catalogue = chosen?.Catalogue ?? UiCatalogue.Embedded;
            var culture = CultureInfo.CurrentUICulture;
            if (chosen is null && lastResolved is { } last && ReferenceEquals(last.Item1, culture)) return last.Item2;
            var language = UiLanguages.Resolve(culture, UiLanguages.Shipped.Where(shipped => catalogue.Languages.Contains(shipped)));
            if (chosen is null) lastResolved = Tuple.Create(culture, language);
            return language;
        }
    }

    /// <summary>The keys asked for that no file has (in this process), for the tests and the log; empty when every key exists.</summary>
    public static IReadOnlyCollection<string> MissingKeys => [.. missing.Keys];

    /// <summary>A string as its file has it.</summary>
    public static string Get(string key)
    {
        var chosen = scoped.Value;
        var catalogue = chosen?.Catalogue ?? UiCatalogue.Embedded;
        var language = Language;
        if (catalogue.TryGet(language, key, out var template)) return template;
        if (!string.Equals(language, UiLanguages.Fallback, StringComparison.Ordinal) && catalogue.TryGet(UiLanguages.Fallback, key, out template)) return template;
        if (missing.Count < 256) missing.TryAdd(key, 0);
        return key;
    }

    /// <summary>A string with its <c>{0}</c>, <c>{1}</c> holes filled, the way an interpolated string would (the current culture).</summary>
    public static string Format(string key, params object?[] arguments) => FormatWith(CultureInfo.CurrentCulture, key, arguments);

    /// <summary>A string with its holes filled in a given culture (<see cref="CultureInfo.InvariantCulture"/> where the number must read the same everywhere).</summary>
    public static string FormatWith(IFormatProvider provider, string key, params object?[] arguments)
    {
        var template = Get(key);
        try
        {
            return string.Format(provider, template, arguments);
        }
        catch (FormatException)
        {
            // A translation with a broken hole must still show something readable.
            if (missing.Count < 256) missing.TryAdd(key + "#format", 0);
            return template;
        }
    }

    /// <summary>
    /// The form of <c>key</c> for a count: <c>key.one</c> or <c>key.other</c> (<c>key.other</c> when the language has no <c>one</c>). The count is
    /// <c>{0}</c> in the string; <paramref name="more"/> follow as <c>{1}</c>, <c>{2}</c>.
    /// </summary>
    public static string Plural(string key, long count, params object?[] more) => PluralWith(CultureInfo.CurrentCulture, key, count, more);

    public static string PluralWith(IFormatProvider provider, string key, long count, params object?[] more)
    {
        var language = Language;
        var catalogue = scoped.Value?.Catalogue ?? UiCatalogue.Embedded;
        var category = UiPlural.Category(language, count);
        // The language's own form for the count, else its own "other", else English's, so a translation with only "other" never reads in English.
        var form = category == UiPlural.One && !catalogue.TryGet(language, key + "." + UiPlural.One, out _) && catalogue.TryGet(language, key + "." + UiPlural.Other, out _)
            ? UiPlural.Other
            : category;
        object?[] arguments = [count, .. more];
        return FormatWith(provider, key + "." + form, arguments);
    }

    /// <summary>
    /// For a test: the words come from <paramref name="catalogue"/> (in <paramref name="language"/> when given, else by the thread's UI culture)
    /// until the result is disposed. The choice follows the test's own async flow, so parallel tests never see each other's.
    /// </summary>
    public static IDisposable Use(UiCatalogue catalogue, string? language = null)
    {
        var before = scoped.Value;
        scoped.Value = new Chosen(catalogue, language);
        return new Restore(before);
    }

    private sealed class Restore(Chosen? before) : IDisposable
    {
        public void Dispose() => scoped.Value = before;
    }
}
