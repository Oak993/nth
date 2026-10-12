// PORT-ONLY: web surfaces W16a (DESIGN.md 7, 10 names and values for every control; 14 W22 moves every new string into the
// resource files; ED-NEW-web-w16a-1). No Swift counterpart. Every word a live view shows or speaks of its own, in one place.
using System.Globalization;

namespace Marrow.UI.NthUi;

/// <summary>The live view's own words (the model's words come from the view's text).</summary>
public static class NthUiStrings
{
    public const string LiveAnswer = "Live answer";
    public const string Copy = "Copy";
    public const string Copied = "Copied";
    public const string CopyCode = "Copy code";
    public const string Open = "Open";
    public const string ExportCsv = "Download CSV";
    public const string ExportPng = "Save as image";
    public const string Send = "Send";
    public const string Attach = "Attach";
    public const string PreviousStep = "Previous step";
    public const string NextStep = "Next step";
    public const string ChooseDate = "Choose a date";
    public const string NoDate = "No date";
    public const string PreviousMonth = "Previous month";
    public const string NextMonth = "Next month";
    public const string Image = "Image";
    public const string Chart = "Chart";
    public const string Diagram = "Diagram";
    public const string Map = "Map";
    public const string LineArt = "Line art";
    public const string Code = "Code";
    public const string FollowUps = "Follow-up questions";
    public const string On = "On";
    public const string Off = "Off";

    public static string Fewer(string label) => string.Format(CultureInfo.InvariantCulture, "Fewer {0}", Lower(label));

    public static string More(string label) => string.Format(CultureInfo.InvariantCulture, "More {0}", Lower(label));

    public static string StepOf(int step, int count) => string.Format(CultureInfo.InvariantCulture, "{0} of {1}", step, count);

    public static string StepName(int step, int count, string title) =>
        string.Format(CultureInfo.InvariantCulture, "Step {0} of {1}: {2}", step, count, title);

    public static string ImageFrom(string host) => string.Format(CultureInfo.InvariantCulture, "Image from {0}", host);

    public static string Run(string tool) => string.Format(CultureInfo.InvariantCulture, "Run {0}", tool);

    public static string Set(string name) => string.Format(CultureInfo.InvariantCulture, "Set {0}", name);

    private static string Lower(string label) => label.Length == 0 ? label : char.ToLower(label[0], CultureInfo.InvariantCulture) + label[1..];
}
