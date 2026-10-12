using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// ED-NEW-lean-3: a frozen copy of TextCleanup.StripANSI and TextCleanup.WithoutEmDashes as they were before the lean pass
// (claude/release-integration 0a946c558), kept only as the oracle TextCleanupLeanEquivalenceTests compares the shipped code with.
// SwiftString.Nfc is internal, so its one-line body is copied here. Do not change this file: it is the reference.
internal static class TextCleanupFrozen
{
    public static string StripANSI(string text)
    {
        var characters = SwiftString.Characters(text).ToArray();
        if (characters.Length == 0) return text;
        var starts = new int[characters.Length + 1];
        for (var i = 0; i < characters.Length; i++) starts[i + 1] = starts[i] + characters[i].Length;

        StringBuilder? output = null;
        var copiedThrough = 0;
        var search = 0;
        while (search < characters.Length)
        {
            if (!IsAsciiPatternCharacter(characters[search], out var character) || character != '\u001B')
            { search++; continue; }
            var position = search + 1;
            if (!TryAsciiPatternCharacter(characters, position, out character) || character != '[') { search++; continue; }
            position++;
            while (TryAsciiPatternCharacter(characters, position, out character) &&
                   (character is >= '0' and <= '9' or ';' or '?')) position++;
            while (TryAsciiPatternCharacter(characters, position, out character) &&
                   character is >= ' ' and <= '/') position++;
            if (!TryAsciiPatternCharacter(characters, position, out character) || character is < '@' or > '~')
            { search++; continue; }
            position++;
            (output ??= new StringBuilder(text.Length)).Append(text, starts[copiedThrough], starts[search] - starts[copiedThrough]);
            copiedThrough = position;
            search = position;
        }
        return output is null ? text : output.Append(text, starts[copiedThrough], text.Length - starts[copiedThrough]).ToString();
    }

    public static string WithoutEmDashes(string text)
    {
        if (!TextCleanup.MayContainEmDash(Encoding.UTF8.GetBytes(text))) return text;
        var found = false;
        foreach (var character in SwiftString.Characters(text))
            if (IsEmDash(character)) { found = true; break; }
        if (!found) return text;
        var output = new StringBuilder(text.Length);
        foreach (var character in SwiftString.Characters(text))
            output.Append(IsEmDash(character) ? "-" : character);
        return output.ToString();
    }

    private static bool TryAsciiPatternCharacter(string[] characters, int index, out char character)
    {
        character = default;
        return index < characters.Length && IsAsciiPatternCharacter(characters[index], out character);
    }

    private static bool IsAsciiPatternCharacter(string element, out char character)
    {
        character = default;
        var normalized = Nfc(element);
        if (normalized.Length != 1 || normalized[0] > 0x7F) return false;
        character = normalized[0];
        return true;
    }

    private static string Nfc(string text) => text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);

    private static bool IsEmDash(string character) => character is "—" or "―" or "⸺" or "⸻";
}
