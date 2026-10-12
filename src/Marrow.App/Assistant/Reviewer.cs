// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.4 "The reviewer before Done"; unit AS-5; ED-NEW-dextra-as5-8). No Swift twin.
// What the reviewer gets and which model it runs on. It runs in a fresh chat of the mission, sealed like every other,
// in the provider's plan (read-only) mode; the runner refuses every write it asks for, so it adds no permission.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public static class Reviewer
{
    /// <summary>How much of the patch the reviewer's brief carries.</summary>
    public const int PatchChars = 20_000;

    /// <summary>How many changed files the brief names one by one; the rest are counted (ED-NEW-dextra-as5-13).</summary>
    public const int ChangesListed = 200;

    /// <summary>
    /// The reviewer's model: by default a model of the same provider other than the workers' (3.4), so it does not grade
    /// its own work; the workers' model when the provider lists no other. The router (AS-14) later adds "the cheapest
    /// account with room".
    /// </summary>
    public static string? PickModel(string? workers, IReadOnlyList<string> models) =>
        models.FirstOrDefault(m => !string.Equals(m, workers, StringComparison.OrdinalIgnoreCase)) ?? workers;

    /// <summary>The reviewer's brief: the mission's brief, what the lead reported, the deliverables and the evidence.</summary>
    public static string Brief(MissionRecord record, MergeReview? changes)
    {
        var text = new StringBuilder();
        text.AppendLine("You are the reviewer of the mission \"" + record.Mission.Title + "\". Check whether its result does what the brief asks.");
        text.AppendLine("You only read: do not change any file, run nothing that writes, and do not fix anything yourself.");
        text.AppendLine("Do not take the workers' word for it: check the files, the changes and the evidence themselves.");
        text.AppendLine();
        text.AppendLine("The brief:");
        text.AppendLine(record.Brief);
        text.AppendLine();
        text.AppendLine("What the lead reported:");
        text.AppendLine(string.IsNullOrWhiteSpace(record.Report) ? "(nothing)" : record.Report);
        if (record.Artifacts.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Deliverables:");
            foreach (var artifact in record.Artifacts) text.AppendLine("- " + artifact);
        }
        if (changes is not null)
        {
            text.AppendLine();
            text.AppendLine("The changes (" + changes.Summary + "):");
            foreach (var change in changes.Changes.Take(ChangesListed)) text.AppendLine("- " + change.Kind + ": " + change.Path);
            if (changes.Changes.Count > ChangesListed)
                text.AppendLine("- and " + (changes.Changes.Count - ChangesListed).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more files");
            if (changes.Patch.Length > 0)
            {
                text.AppendLine();
                text.AppendLine(changes.Patch.Length <= PatchChars ? changes.Patch : changes.Patch[..PatchChars] + "\n(the patch goes on)");
            }
        }
        if (record.Reviews.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Earlier reviews failed it for:");
            foreach (var review in record.Reviews.Where(r => !r.Passed)) text.AppendLine("- " + review.Reasons);
        }
        text.AppendLine();
        text.Append(ReviewVerdict.Instruction);
        return text.ToString();
    }

    /// <summary>The redirect a Fail sends the lead.</summary>
    public static string Redirect(ReviewVerdict verdict, int round) =>
        $"The reviewer did not pass the result (review {round} of {ReviewVerdict.Redirects + 1}). Fix this, then finish again:\n{verdict.Reasons}";

    /// <summary>The classes the reviewer may be asked about at all; anything else it asks for is refused.</summary>
    public static bool Reads(ActionClass actionClass) => actionClass is ActionClass.ReadLocal or ActionClass.ReadWeb;
}
