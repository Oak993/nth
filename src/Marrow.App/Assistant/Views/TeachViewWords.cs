// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 12.6b; the home menu "Teach"; unit AS-19; ED-NEW-dextra-as19-10). No Swift twin.
// The words of the Teach page and the recording strip, the row of a recording in the list, and where the model's file work runs.
using System.Globalization;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>The words of the Teach page and the strip, in one place.</summary>
public static class TeachViewWords
{
    public const string Title = "Teach Dextra";
    public const string Subtitle = "Show Dextra how you do something once. It watches, writes the steps down as a skill you can read and try, and keeps nothing of your screen unless you say so.";
    public const string StartTitle = "Watch me do this";
    public const string StartDetail = "Dextra watches your clicks, the text you type, and what you say if the microphone is on, and takes a picture of the screen on each click and once a second. All of it stays on this PC. Passwords are never kept, and windows that hold them (password managers, private browser windows, security prompts) are not recorded at all.";
    public const string RecordingsTitle = "Recordings";
    public const string NoRecordings = "Nothing is waiting. A recording stays here until you turn it into a skill or delete it.";
    public const string DraftTitle = "Draft it";
    public const string OpenTitle = "Open the draft";
    public const string DeleteTitle = "Delete";
    public const string DeleteAskTitle = "Delete it for real";
    public const string KeepAskTitle = "Keep it";
    public const string StepsTitle = "Steps";
    public const string InputsTitle = "Inputs";
    public const string DecisionsTitle = "Decisions";
    public const string ObstaclesTitle = "Watch out for";
    public const string QuestionsTitle = "Questions";
    public const string BoundaryTitle = "Approval boundary";
    public const string NameTitle = "Skill name";
    public const string DescriptionTitle = "What it does";
    public const string DryRunTitle = "Dry run";
    public const string TrialTitle = "Try it with me";
    public const string RedraftTitle = "Draft again";
    public const string SaveTitle = "Save as a skill";
    public const string KeepTitle = "Keep the recording after saving";
    public const string PauseHereTitle = "Pause here in the trial";
    public const string RemoveTitle = "Take out";
    public const string PauseTitle = "Pause";
    public const string ResumeTitle = "Resume";
    public const string StopTitle = "Stop";
    public const string CloseTitle = "Done";
    public const string ModelNote = "A model writes the draft up from the steps as text. It never sees your screen pictures.";

    public static string State(TeachRecordingState state) => state switch
    {
        TeachRecordingState.Recording => "Recording",
        TeachRecordingState.Paused => "Paused",
        TeachRecordingState.Recorded => "Recorded",
        TeachRecordingState.Interrupted => "Cut short",
        TeachRecordingState.Drafted => "Drafted",
        TeachRecordingState.Saving => "Saving",
        _ => "Saved",
    };

    /// <summary>"Asks you · sends", "You do it yourself", "Pauses for you": the mark beside a step.</summary>
    public static string Mark(TeachStep step) => step.Guard switch
    {
        { OwnerDoesIt: true } => "You do it yourself",
        { } guard => "Asks you · " + guard.Word,
        _ => step.Flagged ? "Pauses for you" : "",
    };

    public static string Row(TeachRecordingInfo info, TimeZoneInfo zone)
    {
        var started = TimeZoneInfo.ConvertTime(info.Started, zone).ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
        return $"{started} · {State(info.State)} · {TeachWords.Clock(info.Elapsed)}";
    }

    public static string Detail(TeachRecordingInfo info, TimeZoneInfo zone)
    {
        var parts = new List<string> { $"{info.Events} {(info.Events == 1 ? "step" : "steps")}", $"{info.Frames} pictures" };
        if (info.Narration > 0) parts.Add($"{info.Narration} {(info.Narration == 1 ? "line" : "lines")} you said");
        if (info.Newer) parts.Add("made by a newer nth");
        else if (info.ExpiresAt is { } expires) parts.Add("deletes itself on " + TimeZoneInfo.ConvertTime(expires, zone).ToString("d MMM", CultureInfo.InvariantCulture));
        else if (info.Keep) parts.Add("kept");
        return string.Join(" · ", parts);
    }
}

/// <summary>One recording in the list.</summary>
public sealed record TeachRow(TeachRecordingInfo Info, string Title, string Detail, bool CanDraft, bool CanOpen, bool CanDelete);

/// <summary>Runs the Teach model's file work away from the UI thread; the outcome is applied by the awaiting method, back on the thread that asked.</summary>
public interface ITeachWork
{
    Task<T> Run<T>(Func<T> work);
}

/// <summary>The product's work: the thread pool.</summary>
public sealed class PoolTeachWork : ITeachWork
{
    public Task<T> Run<T>(Func<T> work) => Task.Run(work);
}
