using System.Globalization;
using Marrow.Core.Localisation;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22 "every new string in the resource files"; ED-NEW-web-w22-2). The source and the English files agree: every
// word the code asks for is in the files, every word in the files is asked for somewhere, no key is built at run time, and each string fills the holes
// its callers give it. A word that is missing would show as its key; this finds it before the app does.
[Trait("Decision", "ED-NEW-web-w22-2")]
public sealed class UiStringsSourceTests
{
    private static IReadOnlyDictionary<string, string> English => UiCatalogue.Embedded.Entries("en");

    private static IEnumerable<string> KeysAsked(UiTextCall call)
    {
        if (call.Key is null) yield break;
        if (call.Method.StartsWith("Plural", StringComparison.Ordinal))
        {
            yield return call.Key + ".other";
            yield return call.Key + ".one";
        }
        else yield return call.Key;
    }

    [Fact]
    public void TheSourceAsksForWordsAndTheScanFindsThem()
    {
        var calls = UiTextCalls.Scan();

        Assert.NotEmpty(calls);
        Assert.Contains(calls, call => call.File.EndsWith("WebFailureCard.cs", StringComparison.Ordinal) && call.Key == "failure.crashed_title");
    }

    [Fact]
    public void NoKeyIsBuiltAtRunTime()
    {
        var built = UiTextCalls.Scan().Where(call => call.Key is null).Select(call => $"{call.File}:{call.Line} {call.Method}").ToList();

        Assert.Empty(built);
    }

    [Fact]
    public void EveryKeyTheCodeAsksForIsInTheEnglishFiles()
    {
        var missing = UiTextCalls.Scan().SelectMany(call => KeysAsked(call).Where(key => !English.ContainsKey(key)).Select(key => $"{call.File}:{call.Line} {key}")).Distinct().ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryEnglishKeyIsAskedForSomewhere()
    {
        var asked = UiTextCalls.Scan().SelectMany(KeysAsked).ToHashSet(StringComparer.Ordinal);

        var unused = English.Keys.Where(key => !asked.Contains(key)).Order(StringComparer.Ordinal).ToList();

        Assert.Empty(unused);
    }

    [Fact]
    public void PluralFormsComeInPairsAndOnlyPluralCallsUseThem()
    {
        var problems = new List<string>();
        foreach (var key in English.Keys.Where(key => key.EndsWith(".one", StringComparison.Ordinal)))
            if (!English.ContainsKey(key[..^".one".Length] + ".other")) problems.Add(key + " has no .other");
        foreach (var call in UiTextCalls.Scan().Where(call => call.Key is not null))
        {
            var plural = call.Method.StartsWith("Plural", StringComparison.Ordinal);
            if (plural && (!English.ContainsKey(call.Key + ".other") || !English.ContainsKey(call.Key + ".one")))
                problems.Add($"{call.File}:{call.Line} Plural of {call.Key} needs .one and .other");
            if (!plural && (call.Key!.EndsWith(".one", StringComparison.Ordinal) || call.Key.EndsWith(".other", StringComparison.Ordinal)))
                problems.Add($"{call.File}:{call.Line} {call.Key} is a plural form asked for as a whole word");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void AWordAskedForWithGetHasNoHolesAndOneAskedForWithFormatHasThem()
    {
        var problems = new List<string>();
        foreach (var call in UiTextCalls.Scan().Where(call => call.Key is not null && English.ContainsKey(call.Key)))
        {
            var holes = UiCatalogue.Holes(English[call.Key!]);
            if (call.Method == "Get" && holes.Count > 0) problems.Add($"{call.File}:{call.Line} {call.Key} has holes but is read with Get");
            if (call.Method is "Format" or "FormatWith" && holes.Count == 0) problems.Add($"{call.File}:{call.Line} {call.Key} has no holes but is read with {call.Method}");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void HolesAreNumberedFromZeroWithoutGaps()
    {
        var problems = new List<string>();
        foreach (var (key, template) in English)
        {
            var holes = UiCatalogue.Holes(template);
            if (!holes.SequenceEqual(Enumerable.Range(0, holes.Count))) problems.Add($"{key}: holes {string.Join(",", holes)}");
            if (key.EndsWith(".one", StringComparison.Ordinal) || key.EndsWith(".other", StringComparison.Ordinal))
            {
                // a plural form always has the count as {0}, because the count picks the form
                if (holes.Count > 0 && holes[0] != 0) problems.Add($"{key}: a plural form has no {{0}}");
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void EveryEnglishStringFormatsWithItsOwnHolesWhateverTheValues()
    {
        var problems = new List<string>();
        foreach (var (key, template) in English)
        {
            var holes = UiCatalogue.Holes(template);
            object?[] numbers = [.. Enumerable.Repeat<object?>(1234.5, holes.Count)];
            object?[] words = [.. Enumerable.Repeat<object?>("a b", holes.Count)];
            foreach (var arguments in new[] { numbers, words })
            {
                try
                {
                    _ = string.Format(CultureInfo.InvariantCulture, template, arguments);
                }
                catch (FormatException)
                {
                    problems.Add(key + " does not format");
                    break;
                }
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void NoWordIsEmpty()
    {
        var problems = English.Where(pair => pair.Value.Length == 0).Select(pair => pair.Key + " is empty").ToList();

        Assert.Empty(problems);
    }
}
