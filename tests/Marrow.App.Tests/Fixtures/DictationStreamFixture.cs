using System.Text.Json;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// VT-11 test fixture: the voice design's own simulated speaker, live words and final pass (voice-composer.js SCRIPT, SYN, STREAM, planFinal), made by
/// tests/fixtures/voice/make-dictation-stream.js and committed. Times are seconds from the click that starts listening. A stream event is <c>add</c> (a word arrives, in its first guess) or
/// <c>rev</c> (its text is revised); a click is the second click at <see cref="DictationStreamClick.Tl"/>, with the final pass the design plans for it.
/// </summary>
public sealed class DictationStreamFixture
{
    public required IReadOnlyList<DictationStreamEvent> Stream { get; init; }

    public required IReadOnlyList<DictationStreamClick> Clicks { get; init; }

    public static DictationStreamFixture Load()
    {
        var path = "";
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "voice", "dictation-stream.json");
            if (!File.Exists(candidate)) continue;
            path = candidate;
            break;
        }
        if (path.Length == 0) throw new FileNotFoundException("tests/fixtures/voice/dictation-stream.json was not found above " + AppContext.BaseDirectory);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return new DictationStreamFixture
        {
            Stream = root.GetProperty("stream").EnumerateArray().Select(e => new DictationStreamEvent(e.GetProperty("t").GetDouble(), e.GetProperty("kind").GetString()!, e.GetProperty("i").GetInt32(),
                e.GetProperty("text").GetString()!)).ToList(),
            Clicks = root.GetProperty("clicks").EnumerateArray().Select(c => new DictationStreamClick(
                c.GetProperty("tl").GetDouble(),
                c.GetProperty("final").EnumerateArray().Select(e => new DictationStreamEvent(e.GetProperty("at").GetDouble(), e.GetProperty("kind").GetString()!, e.GetProperty("i").GetInt32(),
                    e.GetProperty("text").GetString()!)).ToList(),
                c.GetProperty("duration").GetDouble())).ToList(),
        };
    }
}
