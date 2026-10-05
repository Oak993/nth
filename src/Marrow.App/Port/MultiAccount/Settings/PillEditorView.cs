// PORT-ONLY: T11 step 2. The pill editor, one step in from the list of Settings > Pills, as the page draws it: "Pill editor" and its one line, the pill drawn large with its name to edit (up to 24 characters), "Handshake point" ("Same for every
// step" with one slider, or "Set each step on its own"), the steps (each with its account, its model line and, if it has one, its Handshake slider), "Add a step", the loop line, and "When an earlier account is back". A change is written to the
// PillStore as it is made. The way back is the button "Pills".
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Composer;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class PillEditorView : Decorator
{
    private readonly PillEditorModel editor;
    private readonly MultiAccountContext context;
    private readonly ModelPulse pulse;
    private readonly TextBox nameBox = new() { MaxLength = 24, MinWidth = 150, FontSize = 14, Padding = new Thickness(4, 1) };
    private readonly PillShape shape;
    private readonly CzCrossfadeText count = new("") { FontSize = 12, Secondary = true };
    private readonly CzSegmentRow modeRow;
    private readonly HandshakeSliderControl everyStep;
    private readonly CzFoldRow everyFold;
    private readonly StackPanel steps = new() { Spacing = 0 };
    private readonly Dictionary<string, PillStepRow> stepRows = [];
    private readonly SettingRowView earlier;
    private readonly Control addRow;
    private bool settingName;

    internal PillEditorView(MultiAccountContext context, PillEditorModel editor, Action back)
    {
        this.context = context;
        this.editor = editor;
        pulse = new ModelPulse(h => editor.Changed += h);

        var backButton = LibraryButtons.Make(MultiAccountWords.PillsTitle, back, ButtonLook.Plain, "pill-back");
        backButton.HorizontalAlignment = HorizontalAlignment.Left;
        var title = LibraryUi.Text(MultiAccountWords.EditorTitle, 15, weight: SwiftFont.Weight.Semibold);
        var sub = LibraryUi.Text(MultiAccountWords.EditorSub, 12, secondary: true, wrap: true);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var words = new StackPanel { Spacing = 2, Children = { title, sub } };
        count.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(count, 1);
        head.Children.Add(words);
        head.Children.Add(count);

        AutomationProperties_Name(nameBox, MultiAccountWords.PillNameInput, "pill-name");
        shape = new PillShape(nameBox);
        nameBox.Text = editor.Name;
        nameBox.TextChanged += (_, _) => { if (!settingName) CommitName(false); };
        nameBox.LostFocus += (_, _) => CommitName(true);
        nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(true); e.Handled = true; } };

        modeRow = new CzSegmentRow(MultiAccountWords.HandshakePoint, editor.HandshakeDescription,
            [("all", MultiAccountWords.SameForEvery), ("each", MultiAccountWords.EachOnItsOwn)], editor.Mode == PillHandshakeMode.SameForEvery ? "all" : "each",
            id => editor.SetMode(id == "all" ? PillHandshakeMode.SameForEvery : PillHandshakeMode.PerStep), stacked: true);
        everyStep = new HandshakeSliderControl(() => Same(), p => editor.SetSame(ToCore(p)), "Handshake point for every step", EffortBrand.Create(null), pulse);
        var everyRow = new CzRow(MultiAccountWords.EveryStep, "", everyStep);
        everyFold = new CzFoldRow(new Border { Padding = new Thickness(18, 0, 0, 0), Child = everyRow }, editor.Mode == PillHandshakeMode.SameForEvery);
        var handshakeCard = new ChromeCard(() => new StackPanel { Children = { modeRow.Row, everyFold } });

        addRow = AddStepRow();
        var stepCard = new ChromeCard(() => new StackPanel { Children = { steps, addRow } });
        var loop = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0),
            Children = { LibraryUi.Glyph("info.circle", 12, ChromeTokens.SecondaryText), LibraryUi.Text(MultiAccountWords.LoopLine, 12, secondary: true, wrap: true) },
        };
        foreach (var part in loop.Children) part.VerticalAlignment = VerticalAlignment.Top;
        earlier = new SettingRowView(editor.EarlierRow, () => editor.EarlierRow, pulse, choose: editor.ChooseEarlier);
        var earlierCard = new ChromeCard(() => earlier);

        Child = new StackPanel
        {
            Spacing = ChromeTokens.SectionSpacing,
            Children = { backButton, head, new Border { Child = shape, Padding = new Thickness(0, 0, 0, 2) }, handshakeCard, stepCard, loop, earlierCard },
        };
        this.Track(() => pulse.Revision, _ => Apply());
        Apply();
    }

    private static void AutomationProperties_Name(Control control, string name, string id)
    {
        Avalonia.Automation.AutomationProperties.SetName(control, name);
        Avalonia.Automation.AutomationProperties.SetAutomationId(control, id);
    }

    internal PillEditorModel Model => editor;

    internal PillShape Shape => shape;

    internal TextBox NameBox => nameBox;

    internal string CountShown => count.Text;

    internal CzSegmentRow Mode => modeRow;

    internal HandshakeSliderControl EveryStep => everyStep;

    internal bool EveryOpen => everyFold.Open;

    internal IReadOnlyDictionary<string, PillStepRow> Steps => stepRows;

    internal IReadOnlyList<PillStepRow> StepList => [.. steps.Children.OfType<PillStepRow>()];

    internal SettingRowView Earlier => earlier;

    private Marrow.Core.Port.Accounts.HandshakePoint Same() => editor.Same.Percent is { } p ? new Marrow.Core.Port.Accounts.HandshakePoint(p) : Marrow.Core.Port.Accounts.HandshakePoint.Smart;

    private static Marrow.Core.Accounts.HandshakePoint ToCore(Marrow.Core.Port.Accounts.HandshakePoint p) => p.Percentage is { } percent ? Marrow.Core.Accounts.HandshakePoint.At(percent) : Marrow.Core.Accounts.HandshakePoint.Smart;

    private void CommitName(bool final)
    {
        if (!editor.Exists) return;
        var result = editor.Rename(nameBox.Text ?? "");
        if (final && result != PillRenameResult.Ok)
        {
            settingName = true;
            nameBox.Text = editor.Name;
            settingName = false;
        }
    }

    private Control AddStepRow()
    {
        var plus = LibraryUi.Glyph("plus", 12, ChromeTokens.Accent, SwiftFont.Weight.Medium);
        var text = LibraryUi.Text(MultiAccountWords.AddStep, 13, weight: SwiftFont.Weight.Medium);
        text.Foreground = ChromeTokens.Accent.Brush(text);
        var host = new Panel();
        var button = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 10), Children = { plus, text } }, () => open?.Invoke());
        button.Focusable = true;
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.Named(MultiAccountWords.AddStep, "pill-add-step");
        var (popup, openMenu) = MenuPopover.Anchored(button, () => [.. editor.Available.Select(a => new MenuEntry(a.AccountId, a.Label))], key => editor.AddStep(key));
        open = openMenu;
        host.Children.Add(button);
        host.Children.Add(popup);
        return host;
    }

    private Action? open;

    /// <summary>What the "Add a step" menu offers now.</summary>
    internal IReadOnlyList<MenuEntry> AddMenu => [.. editor.Available.Select(a => new MenuEntry(a.AccountId, a.Label))];

    internal void AddStep(string accountId) => editor.AddStep(accountId);

    private void Apply()
    {
        if (!editor.Exists) return;
        if (!nameBox.IsFocused && nameBox.Text != editor.Name)
        {
            settingName = true;
            nameBox.Text = editor.Name;
            settingName = false;
        }
        shape.Apply(editor.Marks);
        count.SetText(editor.CountText);
        var each = editor.Mode == PillHandshakeMode.PerStep;
        modeRow.Apply(each ? "each" : "all", editor.HandshakeDescription);
        everyFold.Open = !each;
        var view = editor.Steps;
        foreach (var gone in stepRows.Keys.Where(k => view.All(s => s.AccountId != k)).ToArray())
        {
            steps.Children.Remove(stepRows[gone]);
            stepRows.Remove(gone);
        }
        for (var i = 0; i < view.Count; i++)
        {
            var step = view[i];
            if (!stepRows.TryGetValue(step.AccountId, out var row))
            {
                stepRows[step.AccountId] = row = new PillStepRow(editor, step, pulse, context.Models);
                steps.Children.Insert(Math.Min(i, steps.Children.Count), row);
            }
            else row.Apply(step);
            if (steps.Children.IndexOf(row) != i)
            {
                steps.Children.Remove(row);
                steps.Children.Insert(Math.Min(i, steps.Children.Count), row);
            }
        }
        earlier.Apply(editor.EarlierRow);
    }
}
