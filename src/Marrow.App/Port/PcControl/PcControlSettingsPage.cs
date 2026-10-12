// PORT-ONLY: Tactus, Settings › Tactus (OD-0140, OD-0145, ED-NEW-pcctl-29; DESIGN.md 4.7 and its 2026-10-11 updates, 10.1,
// 10.10; units P16, P29, unit 13). No Swift twin. The page: the master switch with the lead sentence; Apps (each allowed app
// with Use, its scope and its remove button, or the empty line); Never allowed (the built-in kinds, read-only, and the
// user's own blocked apps with "Block another app"); Safety (confirm guarded actions, keep screenshots, real mouse and
// keyboard, which stays off until the user turns it on, its privacy line, the sound, the Stop key, the Pause key and Answer
// Tactus with their Change buttons, and every stop path in one line); Activity log; Pill (Position, Show on, answer in the
// pill, continue the chat); Separate workspace (preview); Advanced (the Tactus pointer, screenshot detail, OCR, typed
// text, forget learned app behaviour). Built from the Settings pages' own parts (ChromeSection, ChromeCard, ChromeRow,
// ChromeSegmentedPicker, SettingsSwitch, CzMenuButton, CzCapsuleButton), as the Ignis page is. Every change writes the
// whole settings through the page's parts (PcSettingsStore.Write in the app) and the master switch going off stops
// everything (stop path 9). "Fast helper for simple steps" is not shown: pc_delegate is not part of this version.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
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

    /// <summary>The separate workspace's status line ("Not set up", "Ready", "Running, 2 apps").</summary>
    public Func<string> WorkspaceStatus { get; init; } = () => PcControlSettingsPage.WorkspaceNotSetUp;

    /// <summary>"Set up": the steps of DESIGN.md 2.13 (the command to copy, what changes, the undo); null: not offered yet.</summary>
    public Action? SetUpWorkspace { get; init; }

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

        // Separate workspace (preview)
        WorkspaceStatusText = new TextBlock { Text = parts.WorkspaceStatus(), VerticalAlignment = VerticalAlignment.Center };
        WorkspacePicker = new ChromeSegmentedPicker<string>(
            new ChromeSegmentedOption<string>[] { new("auto", "Auto"), new("always", "Always"), new("never", "Never") },
            new Binding<string>(() => value.WorkspaceUse, use => Change(s => s with { WorkspaceUse = use })));
        AutomationProperties.SetName(WorkspacePicker, WorkspaceUseRow);
        var workspace = Rows(
            Line(WorkspaceLine),
            new ChromeRow(WorkspaceStatusRow, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { WorkspaceStatusText, Capsule(SetUp, parts.SetUpWorkspace) } }, null),
            new ChromeRow(WorkspaceUseRow, () => WorkspacePicker, null));

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
        };
    }

    public SettingsSwitch MasterSwitch { get; }

    public SettingsSwitch? RealInputSwitch { get; private set; }

    public ChromeSegmentedPicker<string> PositionPicker { get; }

    public CzMenuButton ShowOnMenu { get; }

    public ChromeSegmentedPicker<string> WorkspacePicker { get; }

    public ChromeSegmentedPicker<PcDetail> DetailPicker { get; }

    public TextBlock WorkspaceStatusText { get; }

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

    private void Reload()
    {
        value = parts.Read();
        WorkspaceStatusText.Text = parts.WorkspaceStatus();
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
            appsRows.Children.Add(new ChromeRow(grant.Name, () => controls, detail));
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
