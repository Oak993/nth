// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 "Teach mode", 15.3, 16.2; unit AS-19; ED-NEW-dextra-as19-1, -2). No Swift twin.
// What a teach recording is: the events of the owner's clicks, typed text and keys (and the browser steps Visus gives), the
// lines he spoke while he worked, and the manifest that says how it stands. Pure data: TeachRecorder (Marrow.App) writes it,
// TeachJson (Marrow.Services) keeps it, TeachDraftBuilder reads it. Nothing here holds a frame: frames are files beside it.
namespace Marrow.Core.Port.Assistant;

/// <summary>The numbers every part of teach mode shares.</summary>
public static class TeachLimits
{
    /// <summary>The longest recording, counting only the time it was running, not paused (10: 60 minutes).</summary>
    public static readonly TimeSpan Recording = TimeSpan.FromMinutes(60);

    /// <summary>A recording left paused this long ends itself (what was recorded is kept).</summary>
    public static readonly TimeSpan PausedIdle = TimeSpan.FromHours(2);

    /// <summary>A recording nobody turned into a skill is deleted this long after its last change, and the list says when (ED-NEW-dextra-as19-7).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    /// <summary>How often a frame is taken beside the one on each click (10: one frame per second).</summary>
    public static readonly TimeSpan FrameEvery = TimeSpan.FromSeconds(1);

    /// <summary>The strip shows the time left once this little is left.</summary>
    public static readonly TimeSpan CountdownFrom = TimeSpan.FromMinutes(5);

    public const int MaxEvents = 20_000;

    /// <summary>The widest a frame is kept; a wider screen is scaled down to it.</summary>
    public const int FrameWidth = 1280;

    /// <summary>A recording's frames stop being taken every second at this many bytes (clicks still get theirs) ...</summary>
    public const long FrameSoftBytes = 400L * 1024 * 1024;

    /// <summary>... and the recording ends cleanly at this many.</summary>
    public const long FrameHardBytes = 600L * 1024 * 1024;

    /// <summary>The longest typed text a step keeps; the length is always kept whole.</summary>
    public const int TypedChars = 400;

    public const int NameChars = 120;

    public const int NarrationChars = 600;

    /// <summary>The most steps a draft holds (the dry run's step list holds the same: DryRunLog.StepLimit).</summary>
    public const int Steps = 200;

    public const int Questions = 8;

    public const int Parameters = 12;
}

/// <summary>How the elements under a click were looked up (16.2): UI Automation called directly, or Tactus's lookup when it lands.</summary>
public enum TeachSource { Direct, Tactus }

/// <summary>The element a click landed on or a key went to. <see cref="Name"/> is what the control says it is (a button's label).</summary>
public sealed record TeachElement(string App, string Window, string ControlType, string Name)
{
    /// <summary>UI Automation says the control holds a password (<c>IsPassword</c>).</summary>
    public bool IsPassword { get; init; }

    /// <summary>The element could not be looked up (the app does not answer UI Automation): its name and type are empty.</summary>
    public bool IsUnknown => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(ControlType);
}

public enum TeachEventKind { Click, Type, Shortcut, Browser, Private }

/// <summary>One thing the owner did, at <see cref="At"/> into the recording (pauses do not count).</summary>
public sealed record TeachEvent(int Seq, TimeSpan At, TeachEventKind Kind)
{
    /// <summary>The verb: click, right-click, double-click, type, press, open, submit, select.</summary>
    public string Verb { get; init; } = "";

    public TeachElement? Element { get; init; }

    /// <summary>Type: the text, or "[secret]". Shortcut: the chord ("Ctrl+S"). Browser: what was done. Never a password.</summary>
    public string? Text { get; init; }

    /// <summary>Type: how many characters were typed (kept even when the text is not).</summary>
    public int Length { get; init; }

    /// <summary>The text was a secret (a password field, a key, a card number) and is not kept.</summary>
    public bool Secret { get; init; }

    /// <summary>Browser: the page address, with no query or fragment.</summary>
    public string? Url { get; init; }

    /// <summary>The frame taken with it: a path relative to the recording's folder ("frames/0007.jpg").</summary>
    public string? Frame { get; init; }
}

/// <summary>A line the owner spoke while he worked, from the local speech engine only (10; 12: no cloud voice).</summary>
public sealed record TeachNarrationLine(TimeSpan Start, TimeSpan End, string Text);

public enum TeachRecordingState
{
    /// <summary>The hooks are on and events are being written.</summary>
    Recording,
    Paused,
    /// <summary>It ended and nothing has drafted it yet.</summary>
    Recorded,
    /// <summary>nth closed while it ran: what was written is kept, and it can be drafted.</summary>
    Interrupted,
    Drafted,
    /// <summary>The save began and its outcome is not recorded yet (ED-NEW-dextra-as19-7).</summary>
    Saving,
    /// <summary>The skill was added; the raw recording goes (or stays, when the owner kept it).</summary>
    Saved,
}

public enum TeachStopReason { Owner, StopKey, StopAll, Cap, Full, PausedTooLong, Closed }

/// <summary>The manifest of one recording: how it stands, without its content.</summary>
public sealed record TeachRecordingInfo(string Id, string AssistantId, DateTimeOffset Started, TeachRecordingState State)
{
    /// <summary>The version of the file this was read from; a newer one is read for display and left untouched.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Recorded time, pauses not counted.</summary>
    public TimeSpan Elapsed { get; init; }

    public int Events { get; init; }

    public int Frames { get; init; }

    public long FrameBytes { get; init; }

    public int Narration { get; init; }

    public TeachSource Source { get; init; } = TeachSource.Direct;

    /// <summary>When it last changed (a recording, a draft edit): the retention counts from here.</summary>
    public DateTimeOffset Changed { get; init; }

    public TeachStopReason? Ended { get; init; }

    /// <summary>The owner keeps the raw recording after the skill is saved (10.6).</summary>
    public bool Keep { get; init; }

    /// <summary>The name the skill is saved under, while it is being saved or after (the guard against saving twice).</summary>
    public string? SkillName { get; init; }

    /// <summary>The id of the own item once it is added.</summary>
    public string? SkillId { get; init; }

    /// <summary>The version of the file this was read from (a newer one is shown and left untouched).</summary>
    public int Version { get; init; } = CurrentVersion;

    public bool Newer => Version > CurrentVersion;

    /// <summary>Fields a newer build wrote that this one does not know, as JSON text by name; they survive a save.</summary>
    public ValueMap<string, string> Unknown { get; init; } = ValueMap<string, string>.Empty;

    /// <summary>When it will be deleted if nobody saves it; null once saved.</summary>
    public DateTimeOffset? ExpiresAt => State is TeachRecordingState.Saved ? null : Changed + TeachLimits.Retention;
}
