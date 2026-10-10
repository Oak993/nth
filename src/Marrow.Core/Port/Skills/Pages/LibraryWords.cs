// PORT-ONLY: first-release feature unit SK-15 (DL-17). The words of the Plugins and Skills pages: words set S1 (Plain) of the owner's pick (OD-0099), taken from the final
// skills-downloads design page (E:\Droppy\work\design\decisions-2026-10-04\skills-downloads\src\sd-page.js, TX and LV). A string the page needed that the design does not draw is marked
// ENGINEERING DECISION (ED-354). Vendor and product names are plain text only (skills-PLAN.md 3.1), and no string names an image.
namespace Marrow.Core.Port.Skills.Pages;

public static class LibraryWords
{
    // ---- a row's status words (design: the row's accessory and the detail's action row)
    public const string Waiting = "Waiting";
    public const string Checking = "Checking\u2026";
    public const string Installed = "Installed";
    public const string ReadyInNewChats = "Ready in new chats"; // ONDEMAND-PLAN 4.4: the running chat keeps its generation.
    public const string DidNotDownload = "Didn\u2019t download";
    public const string Retry = "Retry";
    public const string Remove = "Remove";
    public const string Removing = "Removing\u2026";
    public const string Cancel = "Cancel";
    /// <summary>ENGINEERING DECISION: ONDEMAND-PLAN.md 3.7 names "Update available"; the button is the verb.</summary>
    public const string UpdateAvailable = "Update available";
    public const string Update = "Update";
    /// <summary>ENGINEERING DECISION: ONDEMAND-PLAN.md 3.5 names "damaged" and "Repair".</summary>
    public const string Damaged = "Damaged";
    public const string Repair = "Repair";

    /// <summary>The Download button: the design's "Download · 8.1 MB" with a no-break space before the dot.</summary>
    public static string Download(double? sizeMb) => sizeMb is { } mb ? "Download\u00A0\u00B7 " + SizeText.Short(mb) : "Download";

    // ---- the page
    public static string ListedHeading(int skills) => "Listed in every session: " + skills + (skills == 1 ? " skill" : " skills");

    public const string InEverySession = "In every session";
    public const string NotListed = "Not listed: not downloaded";

    /// <summary>The note under the switches (design costn, installed).</summary>
    public const string SwitchNote =
        "Every session lists each switched-on skill\u2019s name and description, so the model knows it exists and calls it when a task needs it. Its full instructions load only then. Switches apply to new chats.";

    public static string ListedOf(int on, int of) => on + " of " + of + " listed";
    public static string PluginSkillsListed(int skills) => skills + (skills == 1 ? " skill" : " skills") + " listed";
    public static string FolderSkills(int skills) => skills + (skills == 1 ? " skill" : " skills");

    public const string Needs = "Needs";
    public const string Source = "Source";
    public const string Licence = "Licence";
    public const string FoundOnThisPc = "Found on this PC";

    /// <summary>The remove confirmation's explanation (design whyRm).</summary>
    public const string RemoveWhy = "Its skills stop being listed in new chats.";

    public const string ProvidersHeading = "Already available through your providers";

    /// <summary>ENGINEERING DECISION: the plan's Q8 says the section "copies nothing and only shows what the user already has".</summary>
    public const string ProvidersNote = "Read-only. nth copies nothing from these.";

    public const string ResetAction = "Reset skills and tools";
    public const string ResetConfirm = "Reset";

    /// <summary>ENGINEERING DECISION: ONDEMAND-PLAN.md 3.8 says what Reset keeps and removes.</summary>
    public const string ResetWhy = "Removes everything downloaded for skills and tools. Your switches and accepted terms stay.";

    /// <summary>ENGINEERING DECISION: the unbound download seam fails closed with this reason (SK-16 binds the real one); it is the failure the row shows.</summary>
    public const string DownloadsUnavailable = "Downloading isn\u2019t available in this build yet.";

    /// <summary>UNRESOLVED until SK-2 supplies measured download and unpacked sizes; no exact-size claim.</summary>
    public const string SizeEstimateNote = "Sizes are estimates. Unpacked size is not known yet.";

    // ---- Download all
    public static string DownloadAllButton(LibrarySection section, double? remainingMb) =>
        remainingMb is { } mb ? "Download all " + section.Noun() + " \u00B7 " + SizeText.Short(mb) : "All " + section.Noun() + " are here";

    public static string ConfirmTitle(LibrarySection section) => "Download all " + section.Noun() + "?";
    public static string RunningTitle(LibrarySection section) => "Downloading all " + section.Noun();
    public const string PausedTitle = "Paused";
    public const string DoneTitle = "Downloaded";
    public static string PartialTitle(int done, int of) => done + " of " + of + " downloaded";

    public static string ItemsFrom(int items, string hosts) => items + (items == 1 ? " item" : " items") + " from " + hosts + ".";
    public const string RunningSub = "One at a time. Hide it and keep using nth.";
    public const string PausedSub = "Nothing is downloading. Resume carries on where it stopped.";
    public static string DoneSub(int items, string disk) => "All " + items + " are on this PC: " + disk + ".";
    public static string PartialSub(int failed) => failed + " didn\u2019t download. The rest are on this PC.";

    public const string EverythingHere = "Everything on this page is already here.";

    public static string Plain(double mb) => "About " + SizeText.Short(mb) + " to download, " + SizeText.Disk(mb) + " on disk.";
    public static string LargeWarning(double mb, double diskMb) =>
        "This can be a large download: " + SizeText.Exact(mb) + ", and it takes " + SizeText.Disk(diskMb) + " of disk space once installed.";
    public static string NeedsRoom(string need, string drive) => "Needs " + need + " free on " + drive + ".";

    public const string FactDownload = "Download";
    public const string FactOnDisk = "On disk";
    public static string FactFree(string drive) => "Free on " + drive;
    public const string FactFrom = "From";

    public static string Included(int all, int alreadyHere, int needTerms, int noSize)
    {
        var text = "Included \u00B7 " + all;
        if (alreadyHere > 0) text += " \u00B7 " + alreadyHere + " already here, skipped";
        if (needTerms > 0) text += " \u00B7 " + needTerms + (needTerms == 1 ? " needs its terms first, skipped" : " need their terms first, skipped");
        if (noSize > 0) text += " \u00B7 " + noSize + (noSize == 1 ? " has no size yet, skipped" : " have no size yet, skipped");
        return text;
    }

    /// <summary>ENGINEERING DECISION: the right side of a line the run leaves out because the catalogue holds no size for it yet (the size could not be stated before Continue).</summary>
    public const string SizeUnknown = "Size not known yet";

    public const string ConfirmFoot = "Nothing downloads until you press Continue.";
    public const string RunningFoot = "Cancel stops the rest. What has already downloaded stays.";
    public const string DoneFoot = "New skills start as listed in each detail; switch them there.";
    public const string PartialFoot = "Retry tries the failed ones again. Or download them yourself.";

    public const string Continue = "Continue";
    public const string Pause = "Pause";
    public const string Resume = "Resume";
    public const string Close = "Close";
    public const string Hide = "Hide";

    /// <summary>ENGINEERING DECISION: what a page says when the skills host could not be opened (SkillsStartup logged why).</summary>
    public const string Unavailable = "Skills and tools are unavailable in this run.";
    public static string RetryCount(int failed) => "Retry " + failed;

    /// <summary>The right side of a waiting line in the included list: "Waiting · 18 MB" (the design).</summary>
    public static string WaitingWith(double? sizeMb) => sizeMb is { } mb ? "Waiting " + (char)0x00B7 + " " + SizeText.Short(mb) : Waiting;

    public const string AlreadyHere = "Already here";
    public const string Downloaded = "Downloaded";

    public static string Progress(bool paused, int percent, double doneMb, double totalMb, int doneItems, int items)
    {
        var done = totalMb >= 10 ? Math.Round(doneMb, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " MB"
            : doneMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        return (paused ? "Paused \u00B7 " : "") + percent + "% \u00B7 " + done + " of " + SizeText.Short(totalMb) + " \u00B7 " + doneItems + " of " + items + " done";
    }
}
