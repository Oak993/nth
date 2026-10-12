// PORT-ONLY: Tactus, Settings › Tactus (OD-0140, OD-0145, ED-NEW-pcctl-29; DESIGN.md 4.7 and its 2026-10-11 updates, 10.1,
// 10.10; units P16, P29, unit 13). No Swift twin. The page: the master switch with the lead sentence; Apps (each allowed app
// with its icon, Use, its scope and its remove button, or the empty line); Never allowed (the built-in kinds, read-only, and the
// user's own blocked apps with "Block another app"); Safety (confirm guarded actions, keep screenshots, real mouse and
// keyboard, which stays off until the user turns it on, its privacy line, the sound, the Stop key, the Pause key and Answer
// Tactus with their Change buttons, and every stop path in one line); Activity log; Pill (Position, Show on, answer in the
// pill, continue the chat); Separate workspace (preview: its readiness, "Set up" with the command to copy, what changes, the
// undo and the limits, "Watch workspace", and "Use for new apps"; P18, ED-NEW-pcws-10); Advanced (the Tactus pointer,
// screenshot detail, OCR, typed text, forget learned app behaviour). Built from the Settings pages' own parts (ChromeSection, ChromeCard, ChromeRow,
// ChromeSegmentedPicker, SettingsSwitch, CzMenuButton, CzCapsuleButton), as the Ignis page is. Every change writes the
// whole settings through the page's parts (PcSettingsStore.Write in the app) and the master switch going off stops
// everything (stop path 9). "Fast helper for simple steps" is not shown: pc_delegate is not part of this version.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.PcControl;

/// <summary>What the Tactus page reads and changes; the app binds the store, the grants and the native checks.</summary>
public sealed class PcSettingsPageParts
{
    public required Func<PcSettings> Read { get; init; }

    /// <summary>Writes the whole settings (the store's one write).</summary>
    public required Action<PcSettings> Write { get; init; }

    /// <summary>The master switch went off: stop path 9.</summary>
    public Action? SwitchedOff { get; init; }

    public PcGrants? Grants { get; init; }

    /// <summary>Whether a chord types a character on one of the installed layouts (ChordValidator).</summary>
    public Func<PcHotkey, bool> TypesCharacter { get; init; } = _ => false;

    /// <summary>The monitors for "Show on": device path and name.</summary>
    public Func<IReadOnlyList<(string Id, string Name)>> Monitors { get; init; } = () => [];

    public Action? OpenLogFolder { get; init; }

    public Action? ClearLogs { get; init; }

    /// <summary>Clears PcCapabilities (what Tactus learned about each app).</summary>
    public Action? ForgetLearned { get; init; }

    /// <summary>The separate workspace's status ("Not set up", "Ready", "Running, 2 apps"), read off the UI thread.</summary>
    public Func<CancellationToken, Task<WorkspaceStatus>> ReadWorkspace { get; init; } = _ => Task.FromResult(WorkspaceStatus.NotSetUp);

    /// <summary>nth's exe, as the setup commands name it.</summary>
    public string WorkspaceExe { get; init; } = Environment.ProcessPath ?? "nth.exe";

    /// <summary>Puts text on the clipboard for the setup commands' Copy buttons; null: the page's own window's clipboard.</summary>
    public Func<string, Task>? Copy { get; init; }

    /// <summary>"Watch workspace": shows the view-only picture in picture; false when no workspace runs.</summary>
    public Func<bool>? WatchWorkspace { get; init; }

    /// <summary>Opens Windows' Remote Desktop settings page (nth only links to it).</summary>
    public Action? OpenRemoteDesktopSettings { get; init; }

    /// <summary>Whether this build lets Tactus use the workspace (WorkspacePreview); false: the page says it is a preview.</summary>
    public bool WorkspaceReleased { get; init; } = WorkspacePreview.Released;

    /// <summary>A file picker for "Block another app": the exe path chosen, or null.</summary>
    public Func<Task<string?>>? PickApp { get; init; }

    /// <summary>The page with nothing bound: the settings live in memory only (galleries, previews).</summary>
    public static PcSettingsPageParts Memory(PcSettings? start = null)
    {
        var value = start ?? PcSettings.Default;
        return new PcSettingsPageParts { Read = () => value, Write = next => value = next };
    }
}

public sealed class PcControlSettingsPage : Decorator
{
    public const bool IsReady = true;

    public const string Title = "Tactus";
    public const string Subtitle = "Let models in nth see and use apps you allow.";
    public const string MasterDetail = "Let models in your chats see and use apps on this PC.";
    public const string AppsTitle = "Apps";
    public const string AppsEmpty = "No apps yet. When a model asks to use an app, you choose here or in the pill.";
    public const string NeverTitle = "Never allowed";
    public const string BlockAnother = "Block another app";
    public const string SafetyTitle = "Safety";
    public const string ConfirmRow = "Confirm sends, deletes and payments";
    public const string ScreenshotsRow = "Keep screenshots in chat history";
    public const string RealInputRow = "Allow real mouse and keyboard when needed";
    public const string RealInputDetail = "Off: steps that need your real mouse or keyboard come back to the model unanswered.";
    public const string PrivacyLine = "To know when you are still, nth notes only the time of your last key or click, never which key. Keys you press during a borrowed step are held for a moment, put back, then forgotten. Outside a borrowed step, nth recognises only Esc and the Tactus keys, and keeps no key.";
    public const string FocusHelp = "If an app will not come forward for a borrowed step, Windows' ForegroundLockTimeout may be holding it back. nth never changes it.";
    public const string SoundRow = "Sound when Tactus needs a moment";
    public const string StopKeyRow = "Stop key";
    public const string StopKeyDetail = "Stops Tactus from anywhere. Esc stops a step only while Tactus is using your mouse or keyboard.";
    public const string PauseKeyRow = "Pause key";
    public const string PauseKeyDetail = "Pauses or resumes Tactus in every app. It resumes only when every app is paused, and only from your own keyboard.";
    public const string AnswerKeyRow = "Answer Tactus";
    public const string AnswerKeyDetail = "A key that puts your keyboard in the pill's card. Not set: the tray menu does the same.";
    public const string StopPathsLine = "Stop on the pill, Ctrl+Alt+Esc, Esc three times in a window Tactus is using, Stop in the chat, Stop Tactus in the tray, or this switch.";
    public const string LogTitle = "Activity log";
    public const string LogDetail = "Each chat keeps a log of what Tactus did in it.";
    public const string OpenLogs = "Open log folder";
    public const string ClearLogsLabel = "Clear logs";
    public const string PillTitle = "Pill";
    public const string PositionRow = "Position";
    public const string ShowOnRow = "Show on";
    public const string ShowOnLatest = "Monitor with the latest Tactus step";
    public const string ShowOnMain = "Main monitor";
    public const string AnswerInPillRow = "Answer approvals and questions in the pill";
    public const string AnswerInPillDetail = "Off: asks stay in the chat and the pill shows only the badge.";
    public const string ContinueRow = "When you resume, continue the chat";
    public const string ContinueDetail = "Within 10 minutes of a paused turn, and only if you have not written since. Otherwise the chat asks you to continue.";
    public const string WorkspaceTitle = "Separate workspace";
    public const string WorkspaceLine = "Tactus gets its own mouse and keyboard in a second desktop. Your open windows stay yours.";
    public const string WorkspaceStatusRow = "Status";
    public const string WorkspaceNotSetUp = "Not set up";
    public const string WorkspaceUseRow = "Use for new apps";
    public const string SetUp = "Set up";
    public const string SetUpOpenHelp = "Shows the steps: what changes, the command, the undo.";
    public const string SetUpCloseHelp = "Hides the steps. Esc";
    public const string WatchRow = "Watch workspace";
    public const string WatchLabel = "Watch";
    public const string WatchDetail = "View only. Take over gives it your mouse and keyboard until you give it back.";
    public const string WatchNotRunning = "Nothing runs there yet.";
    public const string CopyLabel = "Copy";
    public const string CopyEnableName = "Copy the setup command";
    public const string CopyUndoName = "Copy the undo command";
    public const string Copied = "Copied.";
    public const string CopyFailed = "Could not copy. Select the command and copy it yourself.";
    public const string OpenRemoteDesktop = "Open Remote Desktop settings";
    public const string LimitsTitle = "Limits";
    public const int WorkspaceRefreshMs = 3000;
    public const string AdvancedTitle = "Advanced";
    public const string PointerRow = "Show Tactus pointer";
    public const string DetailRow = "Screenshot detail";
    public const string DetailStandard = "Standard (best size for this model)";
    public const string DetailHigh = "High (full detail for small text)";
    public const string OcrRow = "Read text from images (OCR)";
    public const string TypedRow = "Log typed text";
    public const string ForgetRow = "Forget learned app behaviour";
    public const string ForgetLabel = "Forget";
    public const string ChangeLabel = "Change";
    public const string SetLabel = "Set";
    public const string NotSet = "Not set";
    public const string PressKeys = "Press the keys";

    /// <summary>The words Settings search finds the page by: DESIGN.md 4.7's, then the pill v2 rows (P29).</summary>
    public static IReadOnlyList<string> Keywords { get; } =
        ["tactus", "pc control", "computer use", "control pc", "automation", "click", "apps", "pill", "pause", "answer", "position"];

    /// <summary>The built-in kinds of apps Tactus never uses (PcPolicy), read-only.</summary>
    public static IReadOnlyList<string> BuiltInNever { get; } =
        ["Terminals and command prompts", "Password managers", "Security apps", "Sign-in, lock and system screens", "Other AI agent apps"];

    /// <summary>The app binds the parts (the store, the grants, the native checks); null: the page keeps its settings in memory.</summary>
    public static Func<PcSettingsPageParts?> Bound { get; set; } = () => null;

    private readonly PcSettingsPageParts parts;
    private readonly StackPanel appsRows = new();
    private readonly StackPanel deniedRows = new();
    private readonly Dictionary<string, ChordButton> chords = new(StringComparer.Ordinal);
    private readonly Avalonia.Threading.DispatcherTimer workspaceRefresh;
    private CancellationTokenSource? workspaceRead;
    private PcSettings value;

    public PcControlSettingsPage()
        : this(Bound() ?? PcSettingsPageParts.Memory())
    {
    }

    public PcControlSettingsPage(PcSettingsPageParts parts)
    {
        this.parts = parts;
        value = parts.Read();

        var lead = new TextBlock { Text = Subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        SwiftFont.Subheadline.Apply(lead);
        lead.Foreground = Chrome.SecondaryText.Brush(lead);

        MasterSwitch = Switch(Title, () => value.Enabled, on => Change(s => s with { Enabled = on }));
        var master = new StackPanel { Children = { new ChromeRow(Title, () => MasterSwitch, MasterDetail) } };

        // Safety
        var safety = Rows(
            new ChromeRow(ConfirmRow, () => Switch(ConfirmRow, () => value.ConfirmGuardedActions, on => Change(s => s with { ConfirmGuardedActions = on })), null),
            new ChromeRow(ScreenshotsRow, () => Switch(ScreenshotsRow, () => value.KeepScreenshots, on => Change(s => s with { KeepScreenshots = on })), null),
            new ChromeRow(RealInputRow, () => RealInputSwitch = Switch(RealInputRow, () => value.RealInput, on => Change(s => s with { RealInput = on })), RealInputDetail),
            Line(PrivacyLine),
            Line(FocusHelp),
            new ChromeRow(SoundRow, () => Switch(SoundRow, () => value.Sound, on => Change(s => s with { Sound = on })), null),
            new ChromeRow(StopKeyRow, () => Chord(StopKeyRow, value.StopChord, PcHotkey.StopDefault.Format(), required: true), StopKeyDetail),
            new ChromeRow(PauseKeyRow, () => Chord(PauseKeyRow, value.PauseChord, PcHotkey.PauseDefault.Format(), required: true), PauseKeyDetail),
            new ChromeRow(AnswerKeyRow, () => Chord(AnswerKeyRow, value.AnswerChord, "", required: false), AnswerKeyDetail),
            Line(StopPathsLine));

        // Activity log
        var openLogs = Capsule(OpenLogs, parts.OpenLogFolder);
        var clearLogs = Capsule(ClearLogsLabel, parts.ClearLogs);
        var log = Rows(new ChromeRow(LogTitle, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { openLogs, clearLogs } }, LogDetail));

        // Pill
        PositionPicker = new ChromeSegmentedPicker<string>(
            new ChromeSegmentedOption<string>[] { new("left", "Left"), new("centre", "Centre"), new("right", "Right") },
            new Binding<string>(() => value.PillAnchor, anchor => Change(s => s with { PillAnchor = anchor })));
        AutomationProperties.SetName(PositionPicker, PositionRow);
        ShowOnMenu = new CzMenuButton(ShowOnRow, ShowOnOptions(), value.PillMonitor);
        ShowOnMenu.SelectionChanged += id => Change(s => s with { PillMonitor = id, PillMonitorName = parts.Monitors().FirstOrDefault(m => m.Id == id).Name });
        var pill = Rows(
            new ChromeRow(PositionRow, () => PositionPicker, null),
            new ChromeRow(ShowOnRow, () => ShowOnMenu, null),
            new ChromeRow(AnswerInPillRow, () => Switch(AnswerInPillRow, () => value.AnswerInPill, on => Change(s => s with { AnswerInPill = on })), AnswerInPillDetail),
            new ChromeRow(ContinueRow, () => Switch(ContinueRow, () => value.ResumeContinuesChat, on => Change(s => s with { ResumeContinuesChat = on })), ContinueDetail));

        // Separate workspace (preview): the status, "Set up" folding the steps open below it, Watch, Use for new apps
        WorkspaceStatusText = new TextBlock { Text = WorkspaceNotSetUp, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(WorkspaceStatusText, WorkspaceStatusRow);
        AutomationProperties.SetLiveSetting(WorkspaceStatusText, AutomationLiveSetting.Polite);
        SetUpButton = Capsule(SetUp, ToggleSetUp);
        AutomationProperties.SetHelpText(SetUpButton, SetUpOpenHelp);
        EnableCommandText = WorkspaceSetup.Command(parts.WorkspaceExe, WorkspaceSetup.EnableCommand);
        DisableCommandText = WorkspaceSetup.Command(parts.WorkspaceExe, WorkspaceSetup.DisableCommand);
        CopyEnableButton = CopyButton(CopyEnableName, EnableCommandText);
        CopyDisableButton = CopyButton(CopyUndoName, DisableCommandText);
        CopyNote = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        SwiftFont.Footnote.Apply(CopyNote);
        AutomationProperties.SetLiveSetting(CopyNote, AutomationLiveSetting.Polite);
        SetUpFold = new CzFoldRow(SetUpSteps(), false);
        SetUpFold.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !SetUpFold.Open) return;
            e.Handled = true;
            ToggleSetUp();
        };
        WatchButton = Capsule(WatchLabel, parts.WatchWorkspace is null ? null : Watch);
        WatchButton.Dimmed = true;
        AutomationProperties.SetName(WatchButton, WatchRow);
        WorkspacePicker = new ChromeSegmentedPicker<string>(
            new ChromeSegmentedOption<string>[] { new("auto", "Auto"), new("always", "Always"), new("never", "Never") },
            new Binding<string>(() => value.WorkspaceUse, use => Change(s => s with { WorkspaceUse = use })));
        AutomationProperties.SetName(WorkspacePicker, WorkspaceUseRow);
        var status = new StackPanel
        {
            Children =
            {
                new ChromeRow(WorkspaceStatusRow, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { WorkspaceStatusText, SetUpButton } }, null),
                SetUpFold,
            },
        };
        var workspace = parts.WorkspaceReleased
            ? Rows(Line(WorkspaceLine), status, new ChromeRow(WatchRow, () => WatchButton, WatchDetail), new ChromeRow(WorkspaceUseRow, () => WorkspacePicker, null))
            : Rows(Line(WorkspaceLine + " " + WorkspacePreview.NotReleasedLine), status, new ChromeRow(WatchRow, () => WatchButton, WatchDetail), new ChromeRow(WorkspaceUseRow, () => WorkspacePicker, null));
        workspaceRefresh = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(WorkspaceRefreshMs) };
        workspaceRefresh.Tick += (_, _) => _ = RefreshWorkspace();

        // Advanced
        DetailPicker = new ChromeSegmentedPicker<PcDetail>(
            new ChromeSegmentedOption<PcDetail>[] { new(PcDetail.Standard, "Standard"), new(PcDetail.High, "High") },
            new Binding<PcDetail>(() => value.Detail, detail => Change(s => s with { Detail = detail })));
        AutomationProperties.SetName(DetailPicker, DetailRow);
        var forget = Capsule(ForgetLabel, parts.ForgetLearned);
        var advanced = Rows(
            new ChromeRow(PointerRow, () => Switch(PointerRow, () => value.ShowPointer, on => Change(s => s with { ShowPointer = on })), null),
            new ChromeRow(DetailRow, () => DetailPicker, DetailStandard + ". " + DetailHigh + "."),
            new ChromeRow(OcrRow, () => Switch(OcrRow, () => value.ReadTextFromImages, on => Change(s => s with { ReadTextFromImages = on })), null),
            new ChromeRow(TypedRow, () => Switch(TypedRow, () => value.LogTypedText, on => Change(s => s with { LogTypedText = on })), null),
            new ChromeRow(ForgetRow, () => forget, null));

        BlockButton = Capsule(BlockAnother, parts.PickApp is null ? null : () => _ = Block());

        Child = new StackPanel
        {
            Spacing = Chrome.SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { lead, new ChromeCard(() => master) } },
                new ChromeSection(AppsTitle, () => new ChromeCard(() => appsRows)),
                new ChromeSection(NeverTitle, () => new ChromeCard(() => new StackPanel { Children = { deniedRows, new ChromeRowDivider(), new ChromeRow(BlockAnother, () => BlockButton, null) } })),
                new ChromeSection(SafetyTitle, () => new ChromeCard(() => safety)),
                new ChromeSection(LogTitle, () => new ChromeCard(() => log)),
                new ChromeSection(PillTitle, () => new ChromeCard(() => pill)),
                new ChromeSection(WorkspaceTitle, () => new ChromeCard(() => workspace)),
                new ChromeSection(AdvancedTitle, () => new ChromeCard(() => advanced)),
            },
        };
        RebuildApps();
        RebuildDenied();
        Action grantsChanged = () => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildApps);
        AttachedToVisualTree += (_, _) =>
        {
            if (parts.Grants is { } grants) grants.Changed += grantsChanged;
            Reload();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (parts.Grants is { } grants) grants.Changed -= grantsChanged;
            workspaceRefresh.Stop();
            workspaceRead?.Cancel();
        };
    }

    public SettingsSwitch MasterSwitch { get; }

    public SettingsSwitch? RealInputSwitch { get; private set; }

    public ChromeSegmentedPicker<string> PositionPicker { get; }

    public CzMenuButton ShowOnMenu { get; }

    public ChromeSegmentedPicker<string> WorkspacePicker { get; }

    public ChromeSegmentedPicker<PcDetail> DetailPicker { get; }

    public TextBlock WorkspaceStatusText { get; }

    public CzCapsuleButton SetUpButton { get; }

    /// <summary>The setup steps under the status row: what changes, the command, the undo, Remote Desktop, the limits.</summary>
    public CzFoldRow SetUpFold { get; }

    public CzCapsuleButton CopyEnableButton { get; }

    public CzCapsuleButton CopyDisableButton { get; }

    /// <summary>"Copied." or why not, under the steps.</summary>
    public TextBlock CopyNote { get; }

    public CzCapsuleButton WatchButton { get; }

    /// <summary>The command that turns child sessions on, as the panel shows and copies it.</summary>
    public string EnableCommandText { get; }

    /// <summary>The undo command.</summary>
    public string DisableCommandText { get; }

    /// <summary>The workspace's status as last read.</summary>
    public WorkspaceStatus Workspace { get; private set; } = WorkspaceStatus.NotSetUp;

    public CzCapsuleButton BlockButton { get; }

    /// <summary>The settings as the page holds them.</summary>
    public PcSettings Value => value;

    /// <summary>The allowed apps as listed: name, Use, scope.</summary>
    public IReadOnlyList<PcGrant> Apps { get; private set; } = [];

    /// <summary>The chord buttons by row title (Stop key, Pause key, Answer Tactus).</summary>
    public IReadOnlyDictionary<string, ChordButton> Chords => chords;

    /// <summary>Applies a change, writes the whole settings, and stops everything when the master switch went off.</summary>
    public void Change(Func<PcSettings, PcSettings> change)
    {
        var before = value;
        value = change(parts.Read());
        parts.Write(value);
        if (before.Enabled && !value.Enabled) parts.SwitchedOff?.Invoke();
        if (!before.UserDeniedApps.SequenceEqual(value.UserDeniedApps)) RebuildDenied();
    }

    /// <summary>
    /// Checks and saves a captured chord for a row: the problem when it is refused (one key, a Windows-key chord, a clash
    /// with another Tactus chord, or a chord that types a character); None when it was saved.
    /// </summary>
    public PcChordProblem SaveChord(string row, PcHotkey chord)
    {
        var others = new List<PcHotkey>();
        foreach (var (title, text) in new[] { (StopKeyRow, value.StopChord), (PauseKeyRow, value.PauseChord), (AnswerKeyRow, value.AnswerChord) })
        {
            if (title != row && PcHotkey.TryParse(text, out var other)) others.Add(other);
        }
        var problem = ChordValidator.Check(chord, others, parts.TypesCharacter);
        if (problem != PcChordProblem.None) return problem;
        var text2 = chord.Format();
        Change(row switch
        {
            StopKeyRow => s => s with { StopChord = text2 },
            PauseKeyRow => s => s with { PauseChord = text2 },
            _ => s => s with { AnswerChord = text2 },
        });
        if (chords.TryGetValue(row, out var button)) button.Show(text2);
        return PcChordProblem.None;
    }

    /// <summary>The words for a refused chord.</summary>
    public static string ProblemText(PcChordProblem problem) => problem switch
    {
        PcChordProblem.SingleKey => "Add Ctrl, Alt or Shift.",
        PcChordProblem.WinKey => "Windows-key chords belong to Windows. Choose another.",
        PcChordProblem.Clash => "Another Tactus key uses this. Choose another.",
        PcChordProblem.TypesCharacter => "This types a character on one of your keyboards. Choose another.",
        _ => "",
    };

    /// <summary>Reads the workspace's status off the UI thread and shows it; Watch is offered only while one runs.</summary>
    public async Task RefreshWorkspace()
    {
        workspaceRead?.Cancel();
        var reading = workspaceRead = new CancellationTokenSource();
        WorkspaceStatus status;
        try
        {
            status = await parts.ReadWorkspace(reading.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (reading.IsCancellationRequested) return;
        Workspace = status;
        WorkspaceStatusText.Text = status.Text;
        WatchButton.Dimmed = parts.WatchWorkspace is null || status.State != WorkspaceState.Running;
        AutomationProperties.SetHelpText(WatchButton, status.State == WorkspaceState.Running ? WatchDetail : WatchNotRunning);
    }

    /// <summary>"Set up": folds the steps open (and reads the status every 3 s while they are open), or closes them.</summary>
    public void ToggleSetUp()
    {
        SetUpFold.Open = !SetUpFold.Open;
        AutomationProperties.SetHelpText(SetUpButton, SetUpFold.Open ? SetUpCloseHelp : SetUpOpenHelp);
        if (SetUpFold.Open)
        {
            workspaceRefresh.Start();
            CopyEnableButton.Focus();
            return;
        }
        workspaceRefresh.Stop();
        CopyNote.IsVisible = false;
        SetUpButton.Focus();
    }

    private void Watch()
    {
        if (parts.WatchWorkspace?.Invoke() != true) _ = RefreshWorkspace();
    }

    private async Task CopyCommand(string command)
    {
        try
        {
            var copy = parts.Copy;
            if (copy is null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) copy = text => clipboard.SetTextAsync(text);
            if (copy is null) throw new InvalidOperationException("no clipboard");
            await copy(command);
            CopyNote.Text = Copied;
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.ExternalException or TimeoutException)
        {
            _ = error;
            CopyNote.Text = CopyFailed;
        }
        CopyNote.IsVisible = true;
    }

    private CzCapsuleButton CopyButton(string name, string command)
    {
        var button = Capsule(CopyLabel, () => _ = CopyCommand(command));
        AutomationProperties.SetName(button, name);
        return button;
    }

    // The steps of DESIGN.md 2.13: what changes, open the terminal, the command, come back, the undo, Remote Desktop, the limits.
    private StackPanel SetUpSteps()
    {
        var steps = new StackPanel { Spacing = 8, Margin = new Thickness(16, 2, 16, 14) };
        steps.Children.Add(Step(WorkspaceSetup.WhatChanges));
        steps.Children.Add(Step(WorkspaceSetup.OpenTerminal));
        steps.Children.Add(Step(WorkspaceSetup.PasteCommand));
        steps.Children.Add(CommandBox(EnableCommandText, CopyEnableButton));
        steps.Children.Add(Step(WorkspaceSetup.ComeBack));
        steps.Children.Add(Step(WorkspaceSetup.UndoLine));
        steps.Children.Add(CommandBox(DisableCommandText, CopyDisableButton));
        steps.Children.Add(Step(WorkspaceSetup.RemoteDesktopNote));
        steps.Children.Add(Capsule(OpenRemoteDesktop, parts.OpenRemoteDesktopSettings));
        var limits = new TextBlock { Text = LimitsTitle, Margin = new Thickness(0, 4, 0, 0) };
        SwiftFont.Footnote.Apply(limits);
        limits.FontWeight = FontWeight.SemiBold;
        steps.Children.Add(limits);
        foreach (var limit in WorkspaceSetup.Limits) steps.Children.Add(Step("\u2022 " + limit));
        steps.Children.Add(CopyNote);
        return steps;
    }

    private static TextBlock Step(string text)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.Footnote.Apply(line);
        line.Foreground = Chrome.SecondaryText.Brush(line);
        return line;
    }

    // The command in a monospaced, selectable box with its Copy button; long paths wrap.
    private static Border CommandBox(string command, CzCapsuleButton copy)
    {
        var text = new SelectableTextBlock { Text = command, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        text.FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace");
        AutomationProperties.SetName(text, command);
        copy.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(text);
        grid.Children.Add(copy);
        Grid.SetColumn(copy, 1);
        var box = new Border { Padding = new Thickness(12, 8), Child = grid };
        box.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        box.Background = Chrome.Overlay(.06).Brush(box);
        return box;
    }

    private void Reload()
    {
        value = parts.Read();
        _ = RefreshWorkspace();
        foreach (var (row, button) in chords)
        {
            button.Show(row switch { StopKeyRow => value.StopChord, PauseKeyRow => value.PauseChord, _ => value.AnswerChord });
        }
        RebuildApps();
        RebuildDenied();
    }

    private IReadOnlyList<(string Id, string Text)> ShowOnOptions()
    {
        var options = new List<(string Id, string Text)> { ("latest", ShowOnLatest), ("main", ShowOnMain) };
        options.AddRange(parts.Monitors());
        if (options.All(o => o.Id != value.PillMonitor)) options.Add((value.PillMonitor, value.PillMonitorName ?? value.PillMonitor));
        return options;
    }

    private void RebuildApps()
    {
        appsRows.Children.Clear();
        Apps = parts.Grants?.All ?? [];
        if (Apps.Count == 0)
        {
            appsRows.Children.Add(Line(AppsEmpty));
            return;
        }
        for (var i = 0; i < Apps.Count; i++)
        {
            if (i > 0) appsRows.Children.Add(new ChromeRowDivider());
            var grant = Apps[i];
            var remove = Capsule("Remove", () => parts.Grants?.Remove(grant.Key));
            AutomationProperties.SetName(remove, "Remove " + grant.Name);
            var always = grant.Scope == PcGrantScope.Always;
            var detail = (grant.Use ? "Sees and uses" : "Only sees") + " · " + (always ? "Always" : "This chat");
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            // Use is a switch on an "Always" grant (PcGrants.SetUse); a chat's grant ends with the chat and is only removed here
            if (always)
            {
                var use = new SettingsSwitch(new Binding<bool>(() => grant.Use, on => parts.Grants?.SetUse(grant.Key, on)));
                AutomationProperties.SetName(use, "Use " + grant.Name);
                controls.Children.Add(use);
            }
            controls.Children.Add(remove);
            // the app's icon (32 DIP) before its row (DESIGN.md 4.7 "Apps: list with icon"; ED-NEW-pcctl-46)
            var icon = PcAppIcons.Tile(grant.Name, grant.Key, 32);
            icon.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(icon, Dock.Left);
            appsRows.Children.Add(new DockPanel { Children = { icon, new ChromeRow(grant.Name, () => controls, detail) } });
        }
    }

    private void RebuildDenied()
    {
        deniedRows.Children.Clear();
        foreach (var kind in BuiltInNever) deniedRows.Children.Add(new ChromeRow(kind, () => new TextBlock { Text = "Built in", VerticalAlignment = VerticalAlignment.Center }, null));
        foreach (var app in value.UserDeniedApps)
        {
            var path = app;
            var unblock = Capsule("Remove", () => Change(s => s with { UserDeniedApps = [.. s.UserDeniedApps.Where(a => !string.Equals(a, path, StringComparison.OrdinalIgnoreCase))] }));
            AutomationProperties.SetName(unblock, "Allow " + PcChatMirror.NameOf("exe:" + path) + " again");
            deniedRows.Children.Add(new ChromeRowDivider());
            deniedRows.Children.Add(new ChromeRow(PcChatMirror.NameOf("exe:" + path), () => unblock, path));
        }
    }

    private async Task Block()
    {
        if (parts.PickApp is not { } pick || await pick() is not { Length: > 0 } path) return;
        if (value.UserDeniedApps.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        Change(s => s with { UserDeniedApps = [.. s.UserDeniedApps, path] });
    }

    private Control Chord(string row, string chord, string fallback, bool required)
    {
        var button = new ChordButton(this, row, chord, required || chord.Length > 0 ? ChangeLabel : SetLabel, fallback);
        chords[row] = button;
        return button;
    }

    private static SettingsSwitch Switch(string name, Func<bool> read, Action<bool> write)
    {
        var toggle = new SettingsSwitch(new Binding<bool>(read, write));
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    private static CzCapsuleButton Capsule(string label, Action? run)
    {
        var button = new CzCapsuleButton(label);
        AutomationProperties.SetName(button, label);
        if (run is null) button.Dimmed = true;
        else button.Invoked += run;
        return button;
    }

    private static TextBlock Line(string text)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8) };
        SwiftFont.Footnote.Apply(line);
        line.Foreground = Chrome.SecondaryText.Brush(line);
        return line;
    }

    private static StackPanel Rows(params Control[] rows)
    {
        var panel = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) panel.Children.Add(new ChromeRowDivider());
            panel.Children.Add(rows[i]);
        }
        return panel;
    }

    /// <summary>
    /// A chord row's control: the chord as text and "Change" (or "Set"). Change listens for the next chord pressed on this
    /// control (Esc cancels); a refused chord says why and keeps the old one.
    /// </summary>
    public sealed class ChordButton : StackPanel
    {
        private readonly PcControlSettingsPage page;
        private readonly string row;
        private readonly string fallback;
        private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock problem = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };

        public ChordButton(PcControlSettingsPage page, string row, string chord, string label, string fallback)
        {
            this.page = page;
            this.row = row;
            this.fallback = fallback;
            Orientation = Orientation.Horizontal;
            Spacing = 8;
            Focusable = true;
            Button = Capsule(label, Listen);
            AutomationProperties.SetName(Button, label + " " + row);
            Children.Add(problem);
            Children.Add(text);
            Children.Add(Button);
            Show(chord);
            KeyDown += Pressed;
        }

        public CzCapsuleButton Button { get; }

        public bool Listening { get; private set; }

        public string Text => text.Text ?? "";

        public string Problem => problem.Text ?? "";

        public void Show(string chord)
        {
            text.Text = chord is { Length: > 0 } ? chord : fallback is { Length: > 0 } ? fallback : NotSet;
        }

        public void Listen()
        {
            Listening = true;
            problem.Text = "";
            text.Text = PressKeys;
            Focus();
        }

        /// <summary>A key pressed while listening (the page's capture; tests call it directly).</summary>
        public void Take(Key key, KeyModifiers modifiers)
        {
            if (!Listening) return;
            if (key == Key.Escape && modifiers == KeyModifiers.None)
            {
                Listening = false;
                page.Reload();
                return;
            }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var mods = PcHotkeyModifiers.None;
            if (modifiers.HasFlag(KeyModifiers.Control)) mods |= PcHotkeyModifiers.Control;
            if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= PcHotkeyModifiers.Alt;
            if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= PcHotkeyModifiers.Shift;
            if (modifiers.HasFlag(KeyModifiers.Meta)) mods |= PcHotkeyModifiers.Win;
            if (VirtualKey(key) is not { } virtualKey) return;
            Listening = false;
            var result = page.SaveChord(row, new PcHotkey(mods, virtualKey));
            if (result != PcChordProblem.None)
            {
                page.Reload();
                problem.Text = ProblemText(result);
            }
        }

        /// <summary>The Windows virtual key of an Avalonia key: letters, digits, F1 to F24, the number pad, the named keys and the OEM keys; null for others.</summary>
        public static ushort? VirtualKey(Key key) => key switch
        {
            >= Key.A and <= Key.Z => (ushort)(0x41 + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (ushort)(0x30 + (key - Key.D0)),
            >= Key.F1 and <= Key.F24 => (ushort)(0x70 + (key - Key.F1)),
            >= Key.NumPad0 and <= Key.NumPad9 => (ushort)(0x60 + (key - Key.NumPad0)),
            Key.Space => 0x20,
            Key.Escape => 0x1B,
            Key.Return => 0x0D,
            Key.Tab => 0x09,
            Key.Back => 0x08,
            Key.Delete => 0x2E,
            Key.Insert => 0x2D,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Pause => 0x13,
            Key.PrintScreen => 0x2C,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            _ => null,
        };

        private void Pressed(object? sender, KeyEventArgs e)
        {
            if (!Listening) return;
            Take(e.Key, e.KeyModifiers);
            e.Handled = true;
        }
    }
}
