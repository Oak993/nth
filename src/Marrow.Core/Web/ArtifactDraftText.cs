// PORT-ONLY: final security gate N4 (ED-NEW-secfinal-40; DESIGN.md web surfaces 6.5 "nth.send(text) puts text in the main composer
// as a draft. The user sends."). The words an artifact's nth.send puts into the composer, and the card that asks first:
//   Join     the draft after the text joins it (a blank line between when the draft has words). A text that would open the
//            message and opens like a command ("/plan", "/compact", "$skill": ThreadRuntime sends a message that opens with / or $
//            as a command) is led by "From the artifact: ", so a page can never start the owner's message as a command.
//   Preview  how the card shows the start of the text: the first lines, cut, every control, format (bidi, zero-width) or
//            separator character written as \uXXXX so the text cannot reorder or hide itself on the card.
// Pure; no Swift counterpart.
using System.Globalization;
using System.Text;
using Marrow.Core.Localisation;
using Marrow.Core.Port;

namespace Marrow.Core.Web;

public static class ArtifactDraftText
{
    /// <summary>How many characters of the text the card shows before it cuts with an ellipsis.</summary>
    public const int PreviewCharacters = 300;

    /// <summary>How many lines of the text the card shows.</summary>
    public const int PreviewLines = 6;

    /// <summary>
    /// Whether a message that opens with <paramref name="text"/> would be run as a command. The composer trims the message and
    /// SlashCommand.NameIn reads a leading "/" or "$"; white space, control and format (zero-width) characters ahead of it are
    /// skipped here too, so nothing in front can hide the mark.
    /// </summary>
    public static bool OpensACommand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format) continue;
            return c is '/' or '$';
        }

        return false;
    }

    /// <summary>The text as it may open a message: led by "From the artifact: " when it would open like a command.</summary>
    public static string ForOpening(string text) => OpensACommand(text) ? UiText.Get("tabula.draft.command_prefix") + text : text;

    /// <summary>The composer's draft after an artifact's text joins <paramref name="current"/> (the words so far; none when blank).</summary>
    public static string Join(string current, string text)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(text);
        return FoundationText.Trim(current, includeNewlines: true).Length == 0 ? ForOpening(text) : current.TrimEnd() + "\n\n" + text;
    }

    /// <summary>How the card shows the start of <paramref name="text"/>.</summary>
    public static string Preview(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var shown = new StringBuilder();
        var lines = 1;
        var at = 0;
        for (; at < text.Length && shown.Length < PreviewCharacters; at++)
        {
            var c = text[at];
            if (c == '\r') continue;
            if (c == '\n')
            {
                if (++lines > PreviewLines) break;
                shown.Append('\n');
                continue;
            }

            if (c == ' ' || char.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned))
                shown.Append(c);
            else
                shown.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        if (at >= text.Length) return shown.ToString();
        if (shown.Length > 0 && char.IsHighSurrogate(shown[^1])) shown.Length--;
        return shown.Append('…').ToString();
    }
}
