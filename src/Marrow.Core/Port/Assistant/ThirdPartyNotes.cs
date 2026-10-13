// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2 "Never stored"; unit AS-12; ED-NEW-dextra-as12-3). No Swift twin.
// The third-party rule: "Dossiers on other people. Notes about a third party are kept only when the owner states them, and only
// what he said." Find asks whether a note is about someone other than the owner (a relation of his, a named person, a contact
// detail); StatedBy asks whether the owner's own words hold everything the note says. Pure; every pattern has a timeout.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>Why a note counts as being about someone else: the words that showed it.</summary>
public sealed record ThirdPartyFinding(string Quote, string Reason);

public static partial class ThirdPartyNotes
{
    private const int TimeoutMs = 250;
    private const RegexOptions Flags = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // People who are not the owner. "client", "customer" and "contact" are left out of the possessive form on purpose: "my client
    // library" is code, and a named client is caught by the next pattern.
    private const string Relation =
        "wife|husband|spouse|partner|girlfriend|boyfriend|fianc[eé]e?|mother|mom|mum|mama|father|dad|papa|parents?|brothers?|sisters?|siblings?|son|daughter|kids?|children|child|"
        + "grand(?:mother|father|ma|pa|son|daughter|parents?)|uncle|aunt|aunty|cousin|nephew|niece|in-laws?|boss|manager|supervisor|colleagues?|co-?workers?|teammates?|employees?|intern|"
        + "landlord|landlady|neighbou?rs?|roommate|flatmate|friends?|mentor|mentee|teacher|professor|doctor|dentist|therapist|lawyer|attorney|accountant|banker|recruiter|ex";

    private const string NamedRelation = Relation + "|clients?|customers?|tenants?|contacts?|vendors?|suppliers?|buddy";

    [GeneratedRegex(@"\b(?:my|his|her|their|our|your|the\s+owner'?s|the\s+owner’s|owner'?s|user'?s)\s+(?:[a-z-]+\s+){0,1}(?:" + Relation + @")\b", Flags, TimeoutMs)]
    private static partial Regex PossessedRelation();

    [GeneratedRegex(@"\b(?:" + NamedRelation + @")\s+(?:(?:is|was|named|called)\s+)?(?-i:[A-Z][\p{L}'’-]{1,})", Flags, TimeoutMs)]
    private static partial Regex RelationThenName();

    [GeneratedRegex(@"(?-i:\b(?:Mr|Mrs|Ms|Miss|Mx|Dr|Prof|Sir|Madam)\.?\s+[A-Z][\p{L}'’-]+)", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex Honorific();

    [GeneratedRegex(@"(?-i:\b[A-Z][\p{L}'’-]+)(?:'s|’s)\s+(?:birthday|salary|address|phone|number|email|health|illness|diagnosis|medication|religion|politics|age|relationship|girlfriend|boyfriend|wife|husband|kids?|children|family|income|debt|home|house|mortgage)\b", Flags, TimeoutMs)]
    private static partial Regex PersonalPossessive();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9-]{1,63}(?:\.[A-Za-z0-9-]{1,63}){1,5}", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\+\d[\d ()-]{7,18}\d|(?<![\d.])\(?\d{3}\)?[ .-]\d{3}[ .-]\d{4}(?![\d.])|(?<![\d.])\d{5}[ -]\d{5}(?![\d.])", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex PhoneNumber();

    // A capitalised word that is the subject of a verb only a person does ("Priya wants...", "Rahul lives in..."). Software and
    // platform names that read like subjects are left out.
    private const string NotAPerson =
        "The|This|That|These|Those|There|Here|It|Its|Windows|Linux|Mac|MacOS|Android|iOS|Python|Docker|Git|GitHub|GitLab|Droppy|Dextra|Astur|Asturs|Claude|Codex|Gemini|Chrome|Edge|Firefox|Node|Postgres|Redis|Kubernetes|Avalonia|Marrow|Tactus|Ignis|Visus|Armilla|Everything|Nothing|Something|Everyone|Anyone|Someone|Always|Never|Please|When|Before|After|Use|Run|Keep|Owner|Owners|User|Users";

    [GeneratedRegex(@"(?-i:(?<![A-Za-z])(?!(?:" + NotAPerson + @")\b)[A-Z]\p{L}{2,})\s+(?:wants?|likes?|prefers?|dislikes?|loves?|hates?|lives?|works?|thinks?|plans?|asked|told|knows?|trusts?|owes?|earns?|married|visits?|emailed)\b", Flags, TimeoutMs)]
    private static partial Regex NamedSubject();

    // He, she, him, her, his: the owner is "the owner", so these name someone else.
    [GeneratedRegex(@"\b(?:he|she|him|her|hers|his|himself|herself)\b", Flags, TimeoutMs)]
    private static partial Regex Pronoun();

    // Names the owner's own words give to people: "my manager Priya", "colleague Sam", "Dr. Rao", "Priya's birthday", "Asha is my sister".
    [GeneratedRegex(@"\b(?:my|his|her|their|our|the\s+owner'?s|owner'?s)\s+(?:[a-z-]+\s+){0,1}(?:" + Relation + @")\s+(?:(?:is|was|named|called)\s+)?(?-i:(?<name>[A-Z]\p{L}{1,}))", Flags, TimeoutMs)]
    private static partial Regex NameAfterPossessedRelation();

    [GeneratedRegex(@"\b(?:" + NamedRelation + @")\s+(?:(?:is|was|named|called)\s+)?(?-i:(?<name>[A-Z]\p{L}{1,}))", Flags, TimeoutMs)]
    private static partial Regex NameAfterRelation();

    [GeneratedRegex(@"(?-i:\b(?:Mr|Mrs|Ms|Miss|Mx|Dr|Prof|Sir|Madam)\.?\s+(?<name>[A-Z]\p{L}+))", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex NameAfterTitle();

    [GeneratedRegex(@"(?-i:(?<name>[A-Z]\p{L}+))(?:'s|’s)\s+(?:birthday|salary|address|phone|number|email|health|illness|age|family|wife|husband|kids?|children)\b", Flags, TimeoutMs)]
    private static partial Regex NameWithPossessive();

    [GeneratedRegex(@"(?-i:(?<name>[A-Z]\p{L}+))\s+(?:is|was)\s+(?:my|his|her|our|the\s+owner'?s)\s+(?:[a-z-]+\s+){0,1}(?:" + Relation + @")\b", Flags, TimeoutMs)]
    private static partial Regex NameThenIsRelation();

    /// <summary>
    /// The names of people in the text: those it ties to the owner or marks as people (the patterns <see cref="Find"/> uses). A
    /// later note that mentions one of them, without any marker of its own, is about that person.
    /// </summary>
    public static IReadOnlyList<string> NamesIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var names = new List<string>();
        try
        {
            foreach (var regex in new[] { NameAfterPossessedRelation(), NameAfterRelation(), NameAfterTitle(), NameWithPossessive(), NameThenIsRelation() })
                foreach (Match match in regex.Matches(text))
                    if (match.Groups["name"].Value is { Length: > 1 } name && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return names;
        }
        return names;
    }

    /// <summary>
    /// What makes the note about someone other than the owner, or null when nothing does. A relation of the owner's ("my wife"), a
    /// relation followed by a name ("colleague Sam"), a titled name ("Dr. Rao"), a named person's private matter ("Priya's
    /// birthday"), an email address or phone number, a capitalised subject doing what only a person does ("Priya wants..."), a
    /// he or she, and a name the owner's own words (<paramref name="ownerWords"/>) gave to a person all count. Reading never
    /// throws: text the patterns cannot finish on counts.
    /// </summary>
    public static ThirdPartyFinding? Find(string? text, string? ownerWords = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            if (PossessedRelation().Match(text) is { Success: true } relation) return new(relation.Value, "a person close to the owner");
            if (RelationThenName().Match(text) is { Success: true } named) return new(named.Value, "a named person");
            if (Honorific().Match(text) is { Success: true } titled) return new(titled.Value, "a named person");
            if (PersonalPossessive().Match(text) is { Success: true } matter) return new(matter.Value, "a named person's private matter");
            if (EmailAddress().Match(text) is { Success: true } email) return new(email.Value, "an email address");
            if (PhoneNumber().Match(text) is { Success: true } phone) return new(phone.Value, "a phone number");
            if (NamedSubject().Match(text) is { Success: true } subject) return new(subject.Value, "a named person");
            if (Pronoun().Match(text) is { Success: true } pronoun) return new(pronoun.Value, "someone other than the owner");
            // A name the owner's own words gave to a person, mentioned here without any marker of its own.
            foreach (var name in NamesIn(ownerWords))
                if (Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(TimeoutMs)))
                    return new(name, "a person the owner named");
            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return new(text.Length > 40 ? text[..40] : text, "text that could not be read");
        }
    }

    /// <summary>
    /// Whether the owner's words hold everything the note says: every word of the note that carries meaning is a word he used
    /// (plurals and "'s" folded). A note that adds anything he did not say ("Priya is nervous" from "Priya wants the report on
    /// Monday") is not stated by him. An empty statement states nothing.
    /// </summary>
    public static bool StatedBy(string? ownerWords, string noteText)
    {
        if (string.IsNullOrWhiteSpace(ownerWords)) return false;
        var said = MemoryRules.Tokens(ownerWords);
        var note = MemoryRules.Tokens(noteText);
        return note.Count > 0 && note.All(said.Contains);
    }
}
