# Background jobs

One list of everything nth runs in the background. The Background tasks pane draws it (web surfaces design 3.10); the
assistant's mission board reads the same list. This page is for whoever adds a source.

## The model

- `BackgroundJob` (Marrow.Core.Models) is one job at one moment: an immutable record. A source hands out a new one when
  anything about the job changes. Its id is unique across sources and never changes for the life of the job
  (`hydra:<thread>`, `helper:<thread>`, `terminal:<session>`, `download:<entry>`; yours: pick a prefix).
- `Kind`: HydraHead, Helper, Tactus, BrowserSession, Terminal, Download, Workflow.
- `State`: Running, Pausing, Paused, Stopping, Done, Failed, Stopped. Done, Failed and Stopped are "finished".
- Live fields (they change as the job runs and never change the card's layout): `Tokens`, `ToolUses`, `Activity`,
  `Progress`, `Subject`, `FinishedAt`. Every other field is the job's shape; a change in it changes the list.
- `ThreadId` is the thread the job belongs to. Null means app-wide (a download): every thread's list shows it.
- `CanPause` and `CanStop` say which buttons the card has. `TranscriptThreadId` gives the card "View transcript".
  `DevServerUrl` gives it "Open preview".

## The aggregate

`BackgroundJobs` (Marrow.Core.Models) merges the sources. `AppModel.BackgroundJobs` is the app's one instance.

- `Refresh()` asks each source for its jobs, orders them (running first, the oldest start first so a new job joins the end;
  then the finished ones, the one that ended last first), hides what was cleared, and drops a finished job ten minutes
  after it ended. A source that throws loses only its own jobs. A repeated id keeps the first.
- `Attach()` returns a lease. While at least one lease is held a 4 Hz timer refreshes on the main actor; the last lease
  disposed stops the timer. The pane holds a lease while it is on screen.
- `Stop(id)` asks the source; the job reads Stopping until the source says it is over (15 seconds at most). `Pause(id)`
  and `Resume(id)` only reach jobs with `CanPause` and in the right state. `ClearDone(thread, scope)` hides the finished
  jobs of the list shown and tells their sources (`Dismiss`).
- `Changed` is raised when the list the pane draws changes (a job came, went, ended, changed state or order). `Ticked` is
  raised on every refresh. Neither carries a layout: the pane only gives live texts new words.

## Adding a source

Implement `IBackgroundJobSource` (`Snapshot`, `Stop`, `Pause`, `Resume`, `Dismiss`) and `AddSource` it, or, for anything
that runs outside the app's models, publish into `AppModel.PublishedJobs`:

```csharp
model.PublishedJobs.Publish(new BackgroundJob
{
    Id = "tactus:" + runId, Kind = BackgroundJobKind.Tactus, Title = "Fill the Q3 sheet", KindLabel = "Tactus", Subject = "Excel",
    ThreadId = threadId, StartedAt = start, Tokens = 41_200, ToolUses = 38, Activity = "Typing in C7",
    CanPause = true, TranscriptThreadId = threadId,
}, new BackgroundJobControl(Stop: () => run.Stop(), Pause: () => run.Pause(), Resume: () => run.Resume()));
```

Publish again with the same id as the job moves on (a new snapshot replaces the old one and keeps the handles). Publish it
as Done, Failed or Stopped when it ends, so it sits in the Done group; `Retire(id)` makes it vanish. `Publish` and `Retire`
are safe from any thread.

The sources the app has today: Hydra heads (the panel's heads), helpers and the panel's subagent (child chats that ran),
terminals (a scripted session, or a shell while a program runs in it; dev servers flagged through
`TerminalJobSource.DevServerUrlOf`, which dev-server detection sets), and downloads (every `Downloader` job, through
`DownloadJobRegistry`). Tactus streams, Visus agent tabs and workflows publish.

## What the pane guarantees

- A tick draws and never lays out: live values are `LiveText` (a fixed width from a reserve string, tabular digits), the
  progress line and the activity label are drawn, not measured. A test pins this (`IsMeasureValid` after a tick).
- Cards animate by opacity and a transform only, on interruptible springs; Reduce Motion eases in 0.18 s with no scale.
- A finished card recedes by its tint, not by dimming its words, so text keeps 4.5:1.
