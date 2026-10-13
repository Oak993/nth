// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 12.6b; the home menu "Teach"; unit AS-19; ED-NEW-dextra-as19-10). No Swift twin.
// What the Teach page shows and does, apart from how it draws: the button that starts a recording, the recordings that wait (with when each
// deletes itself), and for the one the owner opens, its draft: the name, the steps with the ones that ask marked, the inputs, the reasons, the
// obstacles and the questions, the dry run, the trial with him, and Save. Every job that reads or writes a file runs off the UI thread (ITeachWork)
// and its outcome is applied back on the thread that asked, one at a time and in the order asked; drafting (it asks a model) and saving are
// awaited, so the page stays alive. Starting a recording is the one exception: the strip is a window and goes up on the caller's thread. The
// words are in TeachViewWords.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

public sealed class TeachModel : IDisposable
{
    private readonly TeachController controller;
    private readonly TimeZoneInfo zone;
    private readonly ITeachWork work;
    private readonly SemaphoreSlim turn = new(1, 1);
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

    /// <param name="controller">Teach mode.</param>
    /// <param name="zone">The zone the times are shown in.</param>
    /// <param name="work">Where file work runs: the thread pool in the product, inline in a test.</param>
    public TeachModel(TeachController controller, TimeZoneInfo? zone = null, ITeachWork? work = null)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.zone = zone ?? TimeZoneInfo.Local;
        this.work = work ?? new PoolTeachWork();
        controller.Changed += OnChanged;
        _ = Do(Read, Apply, again: false);
    }

    /// <summary>Raised when anything the page shows changed. May be on any thread; the view marshals.</summary>
    public event Action? Changed;

    public TeachController Controller => controller;

    // ---- starting ----

    /// <summary>The line under the start button: why nothing started, or what is going on.</summary>
    public string StartStatus => startStatus;

    public bool Recording => controller.Active is not null;

    public bool CanStart => !disposed && controller.Active is null && controller.Trial is not { Result: null };

    /// <summary>Starts a recording on the caller's thread (the strip goes up there, and starting writes one small file).</summary>
    public void Start()
    {
        if (!CanStart) return;
        var result = controller.Start();
        startStatus = result.Refusal ?? "";
        Raise();
    }

    // ---- the recordings ----

    public IReadOnlyList<TeachRow> Rows => rows;

    /// <summary>Recordings whose manifest does not read: listed, left where they are.</summary>
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
        draft = null;
        Raise();
        if (id is not null) _ = Do(() => controller.Open(id), found => { if (selected == id) draft = found; });
    }

    /// <summary>Drafts a recording (asks the model if one is bound). The outcome lands when it is done.</summary>
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
            await Do(Read, Apply, again: false).ConfigureAwait(true);
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
            await Do(Read, Apply, again: false).ConfigureAwait(true);
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
        _ = Do(() => controller.Discard(id), gone =>
        {
            if (!gone) startStatus = "That recording is in use right now, so it was not deleted.";
            if (selected == id)
            {
                selected = null;
                draft = null;
            }
        });
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

    /// <summary>Whether the raw recording is kept after the skill is saved (as the manifest says).</summary>
    public bool Keep => rows.FirstOrDefault(r => r.Info.Id == selected)?.Info.Keep == true;

    public bool CanSave => !busy && draft is not null && selected is not null && controller.Trial is not { Result: null };

    public void Rename(string name) => Edit(d => d with { Name = TeachDraft.SkillNameFrom(name) });

    public void Describe(string text) => Edit(d => d with { Description = TeachDraft.Clean(text, TeachDraft.DescriptionChars) });

    public void Reword(int step, string text) => Edit(d => d.Reword(step, text));

    public void Flag(int step, bool flagged) => Edit(d => d.Flag(step, flagged));

    public void RemoveStep(int step) => Edit(d => d.Remove(step));

    public void SetKeep(bool keep)
    {
        if (selected is not { } id) return;
        _ = Do(() => controller.SetKeep(id, keep), _ => { });
    }

    public void Answer(string questionId, string option)
    {
        if (selected is not { } id) return;
        _ = Do(() => controller.Answer(id, questionId, option), updated => draft = updated ?? draft);
    }

    private void Edit(Func<TeachDraft, TeachDraft> change)
    {
        if (selected is not { } id) return;
        _ = Do(() => controller.Edit(id, change), updated =>
        {
            draft = updated ?? draft;
            dryRun = null;
            saveStatus = "";
        });
    }

    // ---- trying it ----

    public void DryRun()
    {
        if (selected is not { } id) return;
        _ = Do(() => controller.DryRunReport(id), report => dryRun = report);
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
            await Do(Read, Apply, again: false).ConfigureAwait(true);
        }
    }

    // ---- file work, off the UI thread ----

    // One thing at a time, in the order asked: the work runs on the pool and reads the list again before it ends, so what it changed is on
    // the page when it lands; the outcome is applied by the caller's thread, and the page is told. A change the work itself made to the
    // controller needs no second read (OnChanged ignores it), one made by anything else (the recorder) does.
    private async Task Do<T>(Func<T> read, Action<T> apply, bool again = true)
    {
        if (disposed) return;
        await turn.WaitAsync().ConfigureAwait(true);
        try
        {
            var (value, list) = await work.Run(() =>
            {
                ownWork = true;
                try
                {
                    var made = read();
                    return (made, again ? Read() : null);
                }
                finally
                {
                    ownWork = false;
                }
            }).ConfigureAwait(true);
            apply(value);
            Apply(list);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            draftStatus = "nth could not read or write the recording (" + error.GetType().Name + "). Nothing was changed.";
        }
        finally
        {
            turn.Release();
        }
        Raise();
    }

    [ThreadStatic]
    private static bool ownWork;

    private ListRead? Read()
    {
        var list = controller.List(out var bad);
        return new ListRead([.. list.Select(info => new TeachRow(info, TeachViewWords.Row(info, zone), TeachViewWords.Detail(info, zone),
            CanDraft: !info.Newer && info.State is TeachRecordingState.Recorded or TeachRecordingState.Interrupted,
            CanOpen: !info.Newer && info.State == TeachRecordingState.Drafted,
            CanDelete: info.State is not (TeachRecordingState.Recording or TeachRecordingState.Paused or TeachRecordingState.Saving)))], bad);
    }

    private sealed record ListRead(IReadOnlyList<TeachRow> Rows, IReadOnlyList<string> Unreadable);

    private void Apply(ListRead? found)
    {
        if (found is null) return;
        rows = found.Rows;
        unreadable = found.Unreadable;
        if (selected is { } id && rows.All(r => r.Info.Id != id))
        {
            selected = null;
            draft = null;
        }
    }

    private void OnChanged()
    {
        if (disposed || ownWork) return;
        _ = Do(Read, Apply, again: false);
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        controller.Changed -= OnChanged;
    }
}
