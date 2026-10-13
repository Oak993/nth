// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 12.6b; the home menu "Teach"; unit AS-19; ED-NEW-dextra-as19-10). No Swift twin.
// What the Teach page shows and does, apart from how it draws: the button that starts a recording, the recordings that wait (with when each
// deletes itself), and for the one the owner opens, its draft: the name, the steps with the ones that ask marked, the inputs, the reasons, the
// obstacles and the questions, the dry run, the trial with him, and Save. Every long job (drafting asks a model, saving writes files) is
// awaited, so the page stays alive and the outcome lands back on the thread that asked. The words are in TeachViewWords.
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

public sealed class TeachModel : IDisposable
{
    private readonly TeachController controller;
    private readonly TimeZoneInfo zone;
    private string? selected;
    private TeachDraft? draft;
    private string startStatus = "";
    private string draftStatus = "";
    private string saveStatus = "";
    private string? dryRun;
    private string? deleteAsked;
    private bool busy;
    private bool disposed;
    private IReadOnlyList<TeachRow> rows = [];
    private IReadOnlyList<string> unreadable = [];

    public TeachModel(TeachController controller, TimeZoneInfo? zone = null)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.zone = zone ?? TimeZoneInfo.Local;
        controller.Changed += OnChanged;
        Reload();
    }

    /// <summary>Raised when anything the page shows changed. May be on any thread; the view marshals.</summary>
    public event Action? Changed;

    public TeachController Controller => controller;

    // ---- starting ----

    /// <summary>The line under the start button: why nothing started, or what is going on.</summary>
    public string StartStatus => startStatus;

    public bool Recording => controller.Active is not null;

    public bool CanStart => !disposed && controller.Active is null && controller.Trial is not { Result: null };

    public void Start()
    {
        if (!CanStart) return;
        var result = controller.Start();
        startStatus = result.Refusal ?? "";
        Raise();
    }

    // ---- the recordings ----

    public IReadOnlyList<TeachRow> Rows => rows;

    /// <summary>Recordings whose manifest does not read: listed, left where they are, and deletable by id.</summary>
    public IReadOnlyList<string> Unreadable => unreadable;

    public string? SelectedId => selected;

    public string? DeleteAsked => deleteAsked;

    /// <summary>Opens a recording: its draft if it has one.</summary>
    public void Select(string? id)
    {
        selected = id;
        deleteAsked = null;
        dryRun = null;
        saveStatus = "";
        draftStatus = "";
        draft = id is null ? null : controller.Open(id);
        Raise();
    }

    /// <summary>Drafts the selected recording (asks the model if one is bound). The outcome lands when it is done.</summary>
    public async Task DraftAsync(string id)
    {
        if (busy) return;
        busy = true;
        selected = id;
        draftStatus = "Drafting…";
        Raise();
        try
        {
            var result = await controller.Draft(id).ConfigureAwait(true);
            draft = result.Draft;
            draftStatus = result.Problem ?? "";
        }
        finally
        {
            busy = false;
            Reload();
            Raise();
        }
    }

    public async Task RedraftAsync()
    {
        if (busy || selected is not { } id) return;
        busy = true;
        draftStatus = "Drafting…";
        dryRun = null;
        Raise();
        try
        {
            var result = await controller.Redraft(id).ConfigureAwait(true);
            draft = result.Draft;
            draftStatus = result.Problem ?? "";
        }
        finally
        {
            busy = false;
            Reload();
            Raise();
        }
    }

    /// <summary>The first press asks "Delete it for real"; the second deletes. Escape or another recording takes the ask back.</summary>
    public void Delete(string id)
    {
        if (deleteAsked != id)
        {
            deleteAsked = id;
            Raise();
            return;
        }
        deleteAsked = null;
        if (!controller.Discard(id)) startStatus = "That recording is in use right now, so it was not deleted.";
        if (selected == id) Select(null);
        Reload();
        Raise();
    }

    public void CancelDelete()
    {
        if (deleteAsked is null) return;
        deleteAsked = null;
        Raise();
    }

    // ---- the draft ----

    public bool Busy => busy;

    public TeachDraft? Draft => draft;

    public string DraftStatus => draftStatus;

    public string SaveStatus => saveStatus;

    public string? DryRunText => dryRun;

    public bool Keep => selected is { } id && controller.Recordings.Info(id) is { Keep: true };

    public bool CanSave => !busy && draft is not null && selected is not null && controller.Trial is not { Result: null };

    public void Rename(string name) => Edit(d => d with { Name = TeachDraft.SkillNameFrom(name) });

    public void Describe(string text) => Edit(d => d with { Description = TeachDraft.Clean(text, TeachDraft.DescriptionChars) });

    public void Reword(int step, string text) => Edit(d => d.Reword(step, text));

    public void Flag(int step, bool flagged) => Edit(d => d.Flag(step, flagged));

    public void RemoveStep(int step) => Edit(d => d.Remove(step));

    public void SetKeep(bool keep)
    {
        if (selected is { } id) controller.SetKeep(id, keep);
        Raise();
    }

    public void Answer(string questionId, string option)
    {
        if (selected is not { } id) return;
        draft = controller.Answer(id, questionId, option) ?? draft;
        Raise();
    }

    private void Edit(Func<TeachDraft, TeachDraft> change)
    {
        if (selected is not { } id) return;
        draft = controller.Edit(id, change) ?? draft;
        dryRun = null;
        saveStatus = "";
        Raise();
    }

    // ---- trying it ----

    public void DryRun()
    {
        if (selected is { } id) dryRun = controller.DryRunReport(id);
        Raise();
    }

    public TrialView? Trial => controller.Trial;

    public void StartTrial()
    {
        if (selected is not { } id) return;
        var (_, refusal) = controller.StartTrial(id);
        if (refusal is not null) draftStatus = refusal;
        Raise();
    }

    public void AnswerTrial(TrialAnswer answer)
    {
        controller.AnswerTrial(answer);
        Raise();
    }

    public void PauseTrial() => controller.PauseTrial();

    public void ResumeTrial() => controller.ResumeTrial();

    public void StopTrial() => controller.StopTrial();

    public void CloseTrial() => controller.ClearTrial();

    // ---- saving ----

    public async Task SaveAsync()
    {
        if (!CanSave || selected is not { } id || draft is not { } saving) return;
        var name = saving.Name;
        var keep = Keep;
        busy = true;
        saveStatus = "Saving…";
        Raise();
        try
        {
            var saved = await controller.Save(id, keep).ConfigureAwait(true);
            if (saved.Ok)
            {
                saveStatus = $"Saved as the skill “{name}”." + (keep ? " The recording is kept." : " The recording was deleted.");
                if (!keep) draft = null;
                selected = keep ? id : null;
            }
            else
            {
                saveStatus = saved.Problem ?? "It was not saved.";
            }
        }
        finally
        {
            busy = false;
            Reload();
            Raise();
        }
    }

    // ---- refreshing ----

    private void OnChanged()
    {
        if (disposed) return;
        Reload();
        Raise();
    }

    private void Reload()
    {
        var list = controller.List(out var bad);
        unreadable = bad;
        rows = [.. list.Select(info => new TeachRow(info, TeachViewWords.Row(info, zone), TeachViewWords.Detail(info, zone),
            CanDraft: !info.Newer && info.State is TeachRecordingState.Recorded or TeachRecordingState.Interrupted,
            CanOpen: !info.Newer && info.State == TeachRecordingState.Drafted,
            CanDelete: info.State is not (TeachRecordingState.Recording or TeachRecordingState.Paused or TeachRecordingState.Saving)))];
        if (selected is { } id && rows.All(r => r.Info.Id != id))
        {
            selected = null;
            draft = null;
        }
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        controller.Changed -= OnChanged;
    }
}
