// PORT-ONLY: Tactus, PC control (OD-0140, OD-0145, ED-NEW-pcctl-40; DESIGN.md 4.7, 10.10). Tactus's settings in nth's
// own settings store (UserDefaults-style keys under "pcControl."), read on every use so a change in Settings applies at
// the next call. Missing or mistyped values read as the defaults: Tactus off (OD-0140), confirmations on, the real mouse
// and keyboard (rung R4) off. A store that cannot be read reads as Tactus off.
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public static class PcSettingsStore
{
    public const string Enabled = "pcControl.enabled";
    public const string ConfirmGuarded = "pcControl.confirmGuarded";
    public const string ShowPointer = "pcControl.showPointer";
    public const string Detail = "pcControl.detail";
    public const string ReadTextFromImages = "pcControl.ocr";
    public const string LogTypedText = "pcControl.logTypedText";
    public const string KeepScreenshots = "pcControl.keepScreenshots";
    public const string RealInput = "pcControl.realInput";
    public const string FastHelper = "pcControl.fastHelper";
    public const string DeniedApps = "pcControl.deniedApps";

    /// <summary>DESIGN.md 10.10 `pcControl.stopChord`.</summary>
    public const string StopChord = "pcControl.stopChord";

    /// <summary>DESIGN.md 10.10 `pcControl.pauseChord`.</summary>
    public const string PauseChord = "pcControl.pauseChord";

    /// <summary>DESIGN.md 10.10 `pcControl.answerChord`.</summary>
    public const string AnswerChord = "pcControl.answerChord";

    /// <summary>DESIGN.md 10.10 `pcControl.pill.anchor`: "left", "centre" or "right".</summary>
    public const string PillAnchor = "pcControl.pill.anchor";

    /// <summary>DESIGN.md 10.10 `pcControl.pill.monitor`: "latest", "main" or a monitor's device path.</summary>
    public const string PillMonitor = "pcControl.pill.monitor";

    /// <summary>DESIGN.md 10.10 `pcControl.pill.monitorName` (display only).</summary>
    public const string PillMonitorName = "pcControl.pill.monitorName";

    /// <summary>DESIGN.md 10.10 `pcControl.pill.answerInPill`.</summary>
    public const string AnswerInPill = "pcControl.pill.answerInPill";

    /// <summary>DESIGN.md 10.10 `pcControl.resumeContinuesChat`.</summary>
    public const string ResumeContinuesChat = "pcControl.resumeContinuesChat";

    /// <summary>"Sound when Tactus needs a moment" (DESIGN.md 4.7; ED-NEW-pcctl-29).</summary>
    public const string Sound = "pcControl.sound";

    /// <summary>"Use for new apps" in Separate workspace (DESIGN.md 4.7; ED-NEW-pcctl-29): "auto", "always" or "never".</summary>
    public const string WorkspaceUse = "pcControl.workspace.use";

    private static readonly string[] Anchors = ["left", "centre", "right"];
    private static readonly string[] WorkspaceUses = ["auto", "always", "never"];

    /// <summary>The settings as the store holds them; the defaults for anything missing, and Tactus off when the store cannot be read.</summary>
    public static PcSettings Read(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var defaults = PcSettings.Default;
        try
        {
            return new PcSettings
            {
                Enabled = Bool(store, Enabled, defaults.Enabled),
                ConfirmGuardedActions = Bool(store, ConfirmGuarded, defaults.ConfirmGuardedActions),
                ShowPointer = Bool(store, ShowPointer, defaults.ShowPointer),
                Detail = store.Object(Detail) is string detail && string.Equals(detail, "high", StringComparison.Ordinal) ? PcDetail.High : PcDetail.Standard,
                ReadTextFromImages = Bool(store, ReadTextFromImages, defaults.ReadTextFromImages),
                LogTypedText = Bool(store, LogTypedText, defaults.LogTypedText),
                KeepScreenshots = Bool(store, KeepScreenshots, defaults.KeepScreenshots),
                RealInput = Bool(store, RealInput, defaults.RealInput),
                FastHelper = Bool(store, FastHelper, defaults.FastHelper),
                UserDeniedApps = Strings(store, DeniedApps),
                StopChord = Text(store, StopChord, defaults.StopChord),
                PauseChord = Text(store, PauseChord, defaults.PauseChord),
                AnswerChord = store.Object(AnswerChord) is string answer ? answer : defaults.AnswerChord,
                PillAnchor = Choice(store, PillAnchor, Anchors, defaults.PillAnchor),
                PillMonitor = Text(store, PillMonitor, defaults.PillMonitor),
                PillMonitorName = store.Object(PillMonitorName) is string { Length: > 0 } monitorName ? monitorName : defaults.PillMonitorName,
                AnswerInPill = Bool(store, AnswerInPill, defaults.AnswerInPill),
                ResumeContinuesChat = Bool(store, ResumeContinuesChat, defaults.ResumeContinuesChat),
                Sound = Bool(store, Sound, defaults.Sound),
                WorkspaceUse = Choice(store, WorkspaceUse, WorkspaceUses, defaults.WorkspaceUse),
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return defaults;
        }
    }

    /// <summary>Writes every setting (the Settings page's one call).</summary>
    public static void Write(ISettingsStore store, PcSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        store.Set(Enabled, settings.Enabled);
        store.Set(ConfirmGuarded, settings.ConfirmGuardedActions);
        store.Set(ShowPointer, settings.ShowPointer);
        store.Set(Detail, settings.Detail == PcDetail.High ? "high" : "standard");
        store.Set(ReadTextFromImages, settings.ReadTextFromImages);
        store.Set(LogTypedText, settings.LogTypedText);
        store.Set(KeepScreenshots, settings.KeepScreenshots);
        store.Set(RealInput, settings.RealInput);
        store.Set(FastHelper, settings.FastHelper);
        store.Set(DeniedApps, settings.UserDeniedApps.ToList());
        store.Set(StopChord, settings.StopChord);
        store.Set(PauseChord, settings.PauseChord);
        store.Set(AnswerChord, settings.AnswerChord);
        store.Set(PillAnchor, settings.PillAnchor);
        store.Set(PillMonitor, settings.PillMonitor);
        if (settings.PillMonitorName is { Length: > 0 } monitorName) store.Set(PillMonitorName, monitorName);
        else store.RemoveObject(PillMonitorName);
        store.Set(AnswerInPill, settings.AnswerInPill);
        store.Set(ResumeContinuesChat, settings.ResumeContinuesChat);
        store.Set(Sound, settings.Sound);
        store.Set(WorkspaceUse, settings.WorkspaceUse);
    }

    private static bool Bool(ISettingsStore store, string key, bool fallback) => store.Object(key) is bool value ? value : fallback;

    private static string Choice(ISettingsStore store, string key, string[] choices, string fallback) =>
        store.Object(key) is string value && choices.Contains(value, StringComparer.Ordinal) ? value : fallback;

    private static string Text(ISettingsStore store, string key, string fallback) =>
        store.Object(key) is string { Length: > 0 } value ? value : fallback;

    private static IReadOnlyList<string> Strings(ISettingsStore store, string key) =>
        store.Object(key) is System.Collections.IEnumerable items and not string
            ? items.OfType<string>().Where(item => item.Length > 0).ToList()
            : [];
}
