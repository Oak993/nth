// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.2, 15.2; the approved canvas "Create an Astur"; unit AS-16; ED-NEW-dextra-as16-15). No Swift twin.
// The profile sheet: an Astur's card, to make one from a sentence or a template, to review the one Dextra drafted, or to edit one. Name, look (six shapes,
// each in its own tint, one the owner picks and no two Asturs share), what it looks after, how it writes, its standing duties to tick, the connections it
// wants (the ones that need a sign-in say so), its day budget, and what it does alone and what it asks. Everything is the owner's to change; what is wrong with
// the card is listed under it as he types and Create is there only when nothing is. Escape closes it and puts the focus back where it came from. Fields stack
// above one another, so the sheet wraps at any width and the page never scrolls sideways. Nothing on it moves: no animation, so Reduce motion has nothing to change.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Assistant.Views;

public sealed class ProfileSheet : Border
{
    public const double ShapeButtonSize = 44;
    public const double ShapeGlyphSize = 30;
    public const double SheetPadding = 14;

    private readonly ProfileSheetModel model;
    private readonly Control? returnFocusTo;
    private readonly StackPanel content = new() { Orientation = Orientation.Vertical, Spacing = 12 };
    private readonly Dictionary<string, Control> focusable = [];
    private readonly LiveStatusText status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LiveStatusText problems = new() { TextWrapping = TextWrapping.Wrap };
    private Button? create;
    private bool finished;
    private string percentText = "";
    private string dollarsText = "";

    /// <param name="returnFocusTo">The control that opened the sheet: Escape, Cancel and a finished Create put the focus back on it.</param>
    public ProfileSheet(ProfileSheetModel model, Control? returnFocusTo = null)
    {
        this.model = model;
        this.returnFocusTo = returnFocusTo;
        Child = content;
        Padding = new Thickness(SheetPadding);
        this.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        Background = Chrome.Overlay(0.04).Brush(this);
        AutomationProperties.SetName(this, model.Title);
        Focusable = false;
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(status);
        status.Foreground = Chrome.SecondaryText.Brush(status);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(problems);
        problems.Foreground = Chrome.Danger.Brush(problems);
        model.Changed += Rebuild;
        model.Edited += Refresh;
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        Build();
    }

    /// <summary>Fires once when the sheet is done: Cancel, Escape, or a Create that went through. The host removes it.</summary>
    public event Action? Finished;

    // ---- what tests and the host can reach ----

    public TextBox? SentenceBox { get; private set; }

    public Button? DraftButton { get; private set; }

    public IReadOnlyList<Button> TemplateButtons { get; private set; } = [];

    public TextBox? NameBox { get; private set; }

    public IReadOnlyList<ToggleButton> ShapeButtons { get; private set; } = [];

    public TextBox? AreaBox { get; private set; }

    public TextBox? StyleBox { get; private set; }

    public IReadOnlyList<CheckBox> DutyBoxes { get; private set; } = [];

    public TextBox? PercentBox { get; private set; }

    public TextBox? DollarsBox { get; private set; }

    public Button? CreateButton => create;

    public Button? CancelButton { get; private set; }

    public string ProblemsText => problems.Text ?? "";

    public string StatusText => status.Text ?? "";

    /// <summary>The words of the connections, one line each, as they are listed.</summary>
    public IReadOnlyList<string> ConnectionLines { get; private set; } = [];

    // ---- building ----

    private void Rebuild()
    {
        var key = FocusedKey();
        content.Children.Clear();
        focusable.Clear();
        Build();
        if (key is not null)
            Dispatcher.UIThread.Post(() =>
            {
                if (focusable.TryGetValue(key, out var control)) control.Focus();
            }, DispatcherPriority.Input);
    }

    private string? FocusedKey()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is null ? null : focusable.FirstOrDefault(pair => ReferenceEquals(pair.Value, focused)).Key;
    }

    private void Build()
    {
        content.Children.Add(Heading(model.Title));
        content.Children.Add(Caption(model.Subtitle));
        if (model.Mode == ProfileSheetMode.Add) BuildStart();
        if (model.Draft is not null) BuildCard(model.Draft);
        else content.Children.Add(Caption("No card yet. Draft one from your words, or pick a template."));
        status.Text = model.Status;
        content.Children.Add(status);
        BuildButtons();
        Refresh();
    }

    private void BuildStart()
    {
        var sentence = PlainControls.TextField("Describe the assistant", new Binding<string>(() => model.Sentence, value => model.Sentence = value));
        sentence.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(sentence, "Describe the assistant");
        sentence.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            model.Draw();
            e.Handled = true;
        };
        SentenceBox = Track("sentence", sentence);
        var draft = Capsule(ProfileSheetModel.DraftTitle, model.Draw, "Draft the card from what you wrote");
        DraftButton = Track("draft", draft.Button);
        draft.Shown.HorizontalAlignment = HorizontalAlignment.Left;
        var say = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Children = { Boxed(sentence), draft.Shown } };
        content.Children.Add(Section("Say it", say));

        var templates = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var buttons = new List<Button>();
        foreach (var template in ProfileSheetModel.Templates)
        {
            var id = template.Id;
            var button = Capsule(template.Title, () => model.UseTemplate(id), $"Start from the {template.Title} template");
            Track("template:" + id, button.Button);
            buttons.Add(button.Button);
            templates.Children.Add(button.Shown);
        }
        TemplateButtons = buttons;
        content.Children.Add(Section("Or start from a template", templates));
    }

    private void BuildCard(AsturDraft draft)
    {
        content.Children.Add(PlainControls.Divider());

        var name = PlainControls.TextField("Name", new Binding<string>(() => model.Draft?.Name ?? "", model.SetName));
        name.MaxLength = AsturTeamRules.NameMax + 20;
        AutomationProperties.SetName(name, "Name");
        NameBox = Track("name", name);
        content.Children.Add(Section("Name", Boxed(name)));

        // The state of the looks is also said in words: the one picked, and who has the ones that are not free.
        var held = model.Shapes.Where(choice => !choice.Available && !choice.Selected).Select(choice => $"{choice.Title} belongs to {choice.TakenBy}").ToList();
        var said = (model.Draft?.Shape is { } picked ? AsturShapes.Title(picked) : "None picked") + (held.Count > 0 ? ". " + string.Join(". ", held) + "." : "");
        content.Children.Add(Section("Look", Looks(), said));

        var area = PlainControls.TextField("What it looks after", new Binding<string>(() => model.Draft?.Area ?? "", model.SetArea));
        area.TextWrapping = TextWrapping.Wrap;
        area.AcceptsReturn = false;
        AutomationProperties.SetName(area, "What it looks after");
        AreaBox = Track("area", area);
        content.Children.Add(Section("Area", Boxed(area)));

        var style = PlainControls.TextField("How it writes, for example short and plain", new Binding<string>(() => model.Draft?.Style ?? "", model.SetStyle));
        style.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(style, "How it writes");
        StyleBox = Track("style", style);
        content.Children.Add(Section("How it writes", Boxed(style)));

        content.Children.Add(Section("Standing duties", Duties(draft)));
        content.Children.Add(Section("Connections", Connections(draft)));
        content.Children.Add(Section("Budget", Budget(draft)));
        if (model.PillChoices?.Invoke() is { Count: > 0 } choices)
        {
            var picker = new CzMenuButton("Account order", [("", "Use general order"), .. choices.Select(p => (p.Id, p.Name)), .. draft.PillOrder.Where(id => !choices.Any(c => c.Id == id)).Select(id => (id, id + " (unavailable)"))], draft.PillOrder.FirstOrDefault() ?? "");
            AutomationProperties.SetName(picker, "First account for this Astur");
            Track("pill-order", picker);
            picker.SelectionChanged += id => { if (id.Length == 0) model.InheritPillOrder(); else model.PreferAccount(id); };
            var order = string.Join(" → ", draft.PillOrder.Select(id => choices.FirstOrDefault(p => p.Id == id).Name ?? id));
            content.Children.Add(Section("Account order", picker, order.Length == 0 ? "Uses your general pill order." : order + ". Other accounts follow in your general order."));
        }

        var alone = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        alone.Children.Add(Plain(draft.OnItsOwn));
        alone.Children.Add(Plain("Asks you: " + draft.AsksYou, strong: true));
        if (AsturWords.Never(draft) is { Length: > 0 } never) alone.Children.Add(Plain("Never: " + never + ".", strong: true));
        content.Children.Add(Section("On its own", alone));
    }

    private Control Looks()
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var buttons = new List<ToggleButton>();
        foreach (var choice in model.Shapes)
        {
            var glyph = new AsturShapeGlyph(choice.Shape, ShapeGlyphSize) { Strength = choice.Available ? 1.0 : 0.35, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var tick = new TextBlock { Text = "✓", IsVisible = choice.Selected, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 5, 0) };
            SwiftFont.System(11, SwiftFont.Weight.Bold).Apply(tick);
            tick.Foreground = Chrome.PrimaryText.Brush(tick);
            AutomationProperties.SetAccessibilityView(tick, AccessibilityView.Raw);
            var face = new Border { Width = ShapeButtonSize, Height = ShapeButtonSize, Child = new Panel { Children = { glyph, tick } } };
            face.RoundedRectangle(12, RoundedCornerStyle.Continuous);
            face.Background = Chrome.Overlay(choice.Selected ? 0.14 : 0.05).Brush(face);
            if (choice.Selected) face.BorderThickness = new Thickness(2);
            if (choice.Selected) face.BorderBrush = Chrome.PrimaryText.Brush(face);
            var button = new ToggleButton
            {
                Content = face,
                Padding = default,
                IsChecked = choice.Selected,
                IsEnabled = choice.Available || choice.Selected,
            };
            button.Template = PlainToggleTemplate;
            AutomationProperties.SetName(button, choice.Available || choice.Selected ? choice.Title : $"{choice.Title}, taken by {choice.TakenBy}");
            ToolTip.SetTip(button, choice.Available ? choice.Title : $"{choice.Title} belongs to {choice.TakenBy}");
            var shape = choice.Shape;
            button.Click += (_, _) => model.SetShape(shape);
            Track("shape:" + shape, button);
            buttons.Add(button);
            wrap.Children.Add(button);
        }
        ShapeButtons = buttons;
        return wrap;
    }

    private Control Duties(AsturDraft draft)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var boxes = new List<CheckBox>();
        if (draft.Duties.Count == 0) panel.Children.Add(Plain("None. It proposes nothing on a schedule."));
        for (var i = 0; i < draft.Duties.Count; i++)
        {
            var index = i;
            var duty = draft.Duties[i];
            var text = new TextBlock { Text = duty.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            SwiftFont.System(12.5, SwiftFont.Weight.Regular).Apply(text);
            text.Foreground = Chrome.PrimaryText.Brush(text);
            var box = new CheckBox { IsChecked = duty.Keep, Content = text, MinHeight = 28, Template = PlainCheckTemplate, Padding = default };
            AutomationProperties.SetName(box, duty.Text);
            box.IsCheckedChanged += (_, _) =>
            {
                if ((box.IsChecked == true) != model.Draft!.Duties[index].Keep) model.ToggleDuty(index);
            };
            Track("duty:" + index.ToString(CultureInfo.InvariantCulture), box);
            boxes.Add(box);
            panel.Children.Add(box);
        }
        DutyBoxes = boxes;
        return panel;
    }

    private Control Connections(AsturDraft draft)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var lines = new List<string>();
        if (draft.Wants.Count == 0) panel.Children.Add(Plain("None."));
        foreach (var want in draft.Wants)
        {
            var line = AsturWords.Want(want);
            lines.Add(line);
            panel.Children.Add(Plain(line));
        }
        ConnectionLines = lines;
        return panel;
    }

    private Control Budget(AsturDraft draft)
    {
        percentText = draft.Budget.DailyPercent.ToString("0.##", CultureInfo.InvariantCulture);
        dollarsText = draft.Budget.DailyDollars?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        var percent = PlainControls.TextField("15", new Binding<string>(() => percentText, value => { percentText = value; model.SetBudget(percentText, dollarsText); }));
        var dollars = PlainControls.TextField("No dollar limit", new Binding<string>(() => dollarsText, value => { dollarsText = value; model.SetBudget(percentText, dollarsText); }));
        AutomationProperties.SetName(percent, "Daily budget, percent of any account");
        AutomationProperties.SetName(dollars, "Daily dollars on an API key, optional");
        PercentBox = Track("percent", percent);
        DollarsBox = Track("dollars", dollars);
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        panel.Children.Add(Plain(AsturWords.Budget(draft.Budget)));
        panel.Children.Add(Labelled("Percent of any account, a day", Boxed(percent)));
        panel.Children.Add(Labelled("Dollars on an API key, a day (optional)", Boxed(dollars)));
        if (model.ReserveLine.Length > 0) panel.Children.Add(Plain(model.ReserveLine));
        return panel;
    }

    private void BuildButtons()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        if (model.Draft is not null)
        {
            var make = Capsule(model.CreateTitle, () => _ = model.Create(), model.Mode == ProfileSheetMode.Edit ? "Save the changes" : "Create the Astur");
            create = Track("create", make.Button);
            row.Children.Add(make.Shown);
        }
        else create = null;
        var cancel = Capsule(ProfileSheetModel.CancelTitle, Close, "Cancel and close the sheet");
        CancelButton = Track("cancel", cancel.Button);
        row.Children.Add(cancel.Shown);
        content.Children.Add(problems);
        content.Children.Add(row);
        if (model.Draft is not null) content.Children.Add(Caption(ProfileSheetModel.Isolation));
    }

    // ---- the words under the fields ----

    private void Refresh()
    {
        status.Text = model.Status;
        var found = model.Problems;
        problems.Text = found.Count == 0 ? "" : string.Join('\n', found.Select(p => "• " + p));
        problems.IsVisible = found.Count > 0;
        if (create is not null) create.IsEnabled = model.CanCreate;
        if (model.Done && !finished) Done();
    }

    private void Done()
    {
        finished = true;
        returnFocusTo?.Focus();
        Finished?.Invoke();
    }

    private void Close()
    {
        if (finished) return;
        finished = true;
        model.Cancel();
        returnFocusTo?.Focus();
        Finished?.Invoke();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;
        Close();
        e.Handled = true;
    }

    // ---- small pieces ----

    private T Track<T>(string key, T control) where T : Control
    {
        focusable[key] = control;
        return control;
    }

    private static Control Boxed(TextBox box)
    {
        var field = new Border { Padding = new Thickness(10, 6), Child = box, MinHeight = Chrome.CapsuleContentHeight };
        field.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        field.Background = Chrome.Overlay(0.06).Brush(field);
        return field;
    }

    private static (Button Button, Control Shown) Capsule(string title, Action action, string name) => PlanCard.Capsule(title, action, name);

    private static Control Section(string label, Control body, string? note = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        panel.Children.Add(Label(label));
        panel.Children.Add(body);
        if (note is not null) panel.Children.Add(Caption(note));
        AutomationProperties.SetName(panel, label);
        return panel;
    }

    private static Control Labelled(string label, Control body)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        panel.Children.Add(Caption(label));
        panel.Children.Add(body);
        return panel;
    }

    private static TextBlock Heading(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(15, SwiftFont.Weight.Semibold).Apply(block);
        block.Foreground = Chrome.PrimaryText.Brush(block);
        AutomationProperties.SetHeadingLevel(block, 2);
        return block;
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(12, SwiftFont.Weight.Semibold).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        AutomationProperties.SetHeadingLevel(block, 3);
        return block;
    }

    private static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }

    private static TextBlock Plain(string text, bool strong = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(12.5, strong ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(block);
        block.Foreground = Chrome.PrimaryText.Brush(block);
        return block;
    }

    // The app carries no theme package, so a CheckBox has no template: a box that is ticked with a check mark (never by colour alone), the words beside it, and
    // a focus ring for the keyboard.
    private static readonly FuncControlTemplate<CheckBox> PlainCheckTemplate = new((owner, scope) =>
    {
        var tick = new TextBlock { Text = "✓", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = owner.IsChecked == true };
        SwiftFont.System(12, SwiftFont.Weight.Bold).Apply(tick);
        tick.Foreground = Chrome.PrimaryText.Brush(tick);
        AutomationProperties.SetAccessibilityView(tick, AccessibilityView.Raw);
        var box = new Border { Width = 20, Height = 20, BorderThickness = new Thickness(1.5), Child = tick, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        box.RoundedRectangle(6, RoundedCornerStyle.Continuous);
        box.BorderBrush = Chrome.PrimaryText.Opacity(0.55).Brush(box);
        box.Background = Chrome.Overlay(0.06).Brush(box);
        var ring = new Border { Margin = new Thickness(-3), BorderThickness = new Thickness(2), IsVisible = false, IsHitTestVisible = false };
        ring.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        ring.BorderBrush = Chrome.Accent.Opacity(0.5).Brush(ring);
        AutomationProperties.SetAccessibilityView(ring, AccessibilityView.Raw);
        var label = new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty], VerticalAlignment = VerticalAlignment.Center }.RegisterInNameScope(scope);
        owner.IsCheckedChanged += (_, _) => tick.IsVisible = owner.IsChecked == true;
        owner.Classes.CollectionChanged += (_, _) => ring.IsVisible = owner.IsEffectivelyEnabled && owner.Classes.Contains(":focus-visible");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = Brushes.Transparent };
        row.Children.Add(new Panel { Children = { ring, box } });
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    });

    // A toggle that draws only its content: the picker's button face shows its own state (a ring and a tick), and the focus ring comes from the face.
    private static readonly FuncControlTemplate<ToggleButton> PlainToggleTemplate = new((owner, scope) =>
        new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
}
