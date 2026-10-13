// PORT-ONLY: web surfaces W22 (ED-NEW-web-w22-1). No Swift counterpart. The plural forms a string file may give a count: "key.one" and "key.other".
namespace Marrow.Core.Localisation;

public static class UiPlural
{
    public const string One = "one";
    public const string Other = "other";

    /// <summary>
    /// The form of a whole-number count in a language (Unicode CLDR): English and Urdu use "one" for 1; Hindi for 0 and 1. A language this does
    /// not know counts as English, and its file may still give both forms.
    /// </summary>
    public static string Category(string language, long count) => language switch
    {
        "hi" => count is 0 or 1 ? One : Other,
        _ => count == 1 ? One : Other,
    };
}
