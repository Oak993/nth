// PORT-ONLY: Dextra AS-22 (DESIGN.md rev 5, 4.3 "Shown and reversible: every delegated answer is labelled ... logged with its reasoning, listed in the digest,
// and has an Override button"; the canvas's feed line "Answered for you in “Pitch deck v3”: keep the existing chart style · Override",
// work/design/assistant/index.html line 2231; ED-NEW-dextra-as22-11). No Swift twin.
// What the delegate answered for the owner, under the Decisions tab's cards: the answer, the question, who answered and where, and why. Each has an Override:
// it opens a field ("What should it have been?"), Enter or Send correction sends it to the chat that asked and keeps it as a past answer, and the row says
// Corrected. Escape closes the field and focus goes back to Override. The section is not there until something was answered, so a delegate that is off
// adds nothing to the tab. Rows use the home's own glass look and ink (4.5:1 on the small lines), wrap long words, and never grow the tab sideways.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>Where the Decisions tab reads what the delegate answered and sends an Override.</summary>
public interface IAnsweredSource
{
    /// <summary>The newest answers first.</summary>
    IReadOnlyList<DelegateRecord> Recent { get; }

    /// <summary>The owner's correction of an answer; the result says whether it was kept and sent, or why not.</summary>
    DelegateOverrideResult Override(Guid answer, string correction);

    event Action? Changed;
}

/// <summary>The delegate responder as the tab's source.</summary>
public sealed class ResponderAnswered(DelegateResponder responder) : IAnsweredSource
{
    public IReadOnlyList<DelegateRecord> Recent => responder.Book.Recent(AnsweredList.Shown);

    public DelegateOverrideResult Override(Guid answer, string correction) => responder.Override(answer, correction);

    public event Action? Changed
    {
        add => responder.Book.Changed += value;
        remove => responder.Book.Changed -= value;
    }
}

public sealed class AnsweredList : Decorator
{
    /// <summary>How many answers the tab lists.</summary>
    public const int Shown = 20;

    private readonly IAnsweredSource source;
    private readonly StackPanel rows = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly TextBlock heading = TaskLook.Text(DelegateWords.AnsweredHeading, 11.5, SwiftFont.Weight.Semibold, TaskLook.Ink2);
    private readonly Dictionary<Guid, AnsweredRow> shown = [];
    private bool attached;

    public AnsweredList(IAnsweredSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        AutomationProperties.SetHeadingLevel(heading, 3);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(0, 14, 0, 0), Children = { heading, rows } };
        Child = stack;
        AutomationProperties.SetName(this, DelegateWords.AnsweredHeading);
        IsVisible = false;
        Refresh();
    }

    public IReadOnlyList<AnsweredRow> Rows => [.. rows.Children.OfType<AnsweredRow>()];

    public TextBlock Heading => heading;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!attached) source.Changed += OnChanged;
        attached = true;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (attached) source.Changed -= OnChanged;
        attached = false;
    }

    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Refresh()
    {
        var records = source.Recent;
        var keep = records.Select(r => r.Id).ToHashSet();
        foreach (var gone in shown.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            rows.Children.Remove(shown[gone]);
            shown.Remove(gone);
        }
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (shown.TryGetValue(record.Id, out var row))
            {
                row.Show(record);
                continue;
            }
            row = new AnsweredRow(record, source);
            shown[record.Id] = row;
            rows.Children.Insert(Math.Min(i, rows.Children.Count), row);
        }
        IsVisible = records.Count > 0;
    }
}

/// <summary>One answer: what was answered, to what, by whom and why, with Override (4.3).</summary>
public sealed class AnsweredRow : Decorator
{
    private readonly IAnsweredSource source;
    private readonly Border card = new() { BorderThickness = new Thickness(1), Padding = new Thickness(12), Focusable = true };
    private readonly StackPanel body = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly LiveStatusText status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private Button? overrideButton;
    private Control? overrideShown;
    private RevealBox? reveal;
    private TextBox? entryBox;

    public AnsweredRow(DelegateRecord record, IAnsweredSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        Record = record ?? throw new ArgumentNullException(nameof(record));
        card.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        card.Background = Chrome.Overlay(.05).Brush(card);
        card.BorderBrush = TaskLook.Line.Brush(card);
        card.Child = body;
        Child = card;
        SwiftFont.System(11.5, SwiftFont.Weight.Medium).Apply(status);
        status.Foreground = Chrome.PrimaryText.Brush(status);
        AutomationProperties.SetControlTypeOverride(card, Avalonia.Automation.Peers.AutomationControlType.Group);
        KeyboardNavigation.SetIsTabStop(card, true);
        Build();
    }

    public DelegateRecord Record { get; private set; }

    /// <summary>The Override button; null once the answer was corrected.</summary>
    public Button? OverrideButton => overrideButton;

    /// <summary>The correction field, once opened.</summary>
    public TextBox? Field => entryBox;

    public bool FieldOpen => reveal?.IsOpen == true;

    public string StatusText => status.Text ?? "";

    /// <summary>The row's own words, in order (for the tests and the accessible name).</summary>
    public IReadOnlyList<string> Lines => [.. body.Children.OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0)];

    public void Show(DelegateRecord record)
    {
        if (record == Record) return;
        var corrected = !Record.Corrected && record.Corrected;
        Record = record;
        if (corrected) Build();
    }

    private void Build()
    {
        body.Children.Clear();
        overrideButton = null;
        overrideShown = null;
        reveal = null;
        entryBox = null;
        var r = Record;
        var answer = TaskLook.Text(r.Answer, 13, SwiftFont.Weight.Semibold, TaskLook.Ink, wrap: true);
        var question = TaskLook.Text(r.Question, 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        var who = TaskLook.Text($"{DelegateWords.AnsweredBy(r.By)} in “{r.ChatTitle}” · {TaskWords.Local(r.At).ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture)}", 11, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        body.Children.Add(answer);
        body.Children.Add(question);
        body.Children.Add(who);
        if (r.Reasoning.Length > 0) body.Children.Add(TaskLook.Text(r.Reasoning, 11, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true));
        if (r.Correction is { } correction)
        {
            body.Children.Add(TaskLook.Text($"{DelegateWords.Corrected}: {correction}", 12, SwiftFont.Weight.Medium, TaskLook.Ink, wrap: true));
            if (!r.CorrectionSent) body.Children.Add(TaskLook.Text("The chat is gone, so it was kept for next time and not sent.", 11, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true));
        }
        else
        {
            var (button, shownButton) = TaskLook.Small(DelegateWords.OverrideButton, OpenOverride, name: $"{DelegateWords.OverrideButton}: {DelegateWords.Clip(r.Answer, 60)}");
            overrideButton = button;
            overrideShown = shownButton;
            shownButton.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(shownButton);
            reveal = new RevealBox { Child = Entry() };
            body.Children.Add(reveal);
        }
        body.Children.Add(status);
        status.IsVisible = status.Text?.Length > 0;
        AutomationProperties.SetName(card, $"{DelegateWords.AnsweredBy(r.By)}: {DelegateWords.Clip(r.Answer, 80)}. {DelegateWords.Clip(r.Question, 120)}");
    }

    private Control Entry()
    {
        entryBox = new TextBox { PlaceholderText = DelegateWords.CorrectionPrompt, AcceptsReturn = false, MaxLength = DelegateWords.CorrectionChars };
        SwiftFont.System(12).Apply(entryBox);
        AutomationProperties.SetName(entryBox, DelegateWords.CorrectionPrompt);
        entryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Send(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        var (send, sendShown) = TaskLook.Small(DelegateWords.SendCorrection, Send, primary: true);
        _ = send;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(sendShown, 1);
        row.Children.Add(entryBox);
        row.Children.Add(sendShown);
        return row;
    }

    private void OpenOverride()
    {
        if (reveal is null) return;
        if (reveal.IsOpen) { Close(); return; }
        reveal.Open(true, TaskLook.Still(this));
        entryBox?.Focus(NavigationMethod.Tab);
    }

    // Escape: the field folds away and focus goes back to the button that opened it.
    private void Close()
    {
        reveal?.Open(false, TaskLook.Still(this));
        status.Text = "";
        status.IsVisible = false;
        overrideButton?.Focus(NavigationMethod.Tab);
    }

    private void Send()
    {
        var result = source.Override(Record.Id, entryBox?.Text ?? "");
        if (!result.Saved)
        {
            status.Text = result.Problem ?? "";
            status.IsVisible = status.Text.Length > 0;
            return;
        }
        status.Text = "";
        // the book's change repaints the row as Corrected; focus lands on the row itself, never on a button that is gone
        card.Focus(NavigationMethod.Tab);
    }
}
