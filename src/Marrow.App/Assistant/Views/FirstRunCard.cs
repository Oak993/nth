// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.6 "First run": one short introduction and one card with four choices, each a
// plain switch with its safe value preselected; the owner's 2026-10-08 rules shown read-only with a link to Settings; nothing
// outward happens until the card is done; ED-NEW-dextra-as8-8). No Swift twin.
// The card sits in the home's conversation column above the chat. Its choices write DextraSettings as they change (so the
// switches mean what they show), and Done marks first run finished, which is what lets the controller start.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class FirstRunCard : Border
{
    private readonly DextraSettings settings;
    private readonly AssistantRows rows;
    private readonly List<SettingsSwitch> duties = [];
    private readonly Action? done;

    /// <param name="done">After Done stored first run as finished (the home starts Dextra).</param>
    /// <param name="openSettings">The rules' "Change in Settings" link; null hides it.</param>
    public FirstRunCard(DextraSettings settings, BrainChoices? brain = null, Action? done = null, Action? openSettings = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.done = done;
        rows = new AssistantRows(settings, brain);

        var title = new TextBlock { Text = AssistantSettingsWords.FirstRunTitle, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(17, SwiftFont.Weight.Semibold).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        AutomationProperties.SetHeadingLevel(title, 2);
        var lead = new TextBlock { Text = AssistantSettingsWords.FirstRunLead, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(13).Apply(lead);
        lead.Foreground = Chrome.SecondaryText.Brush(lead);

        var dutyRows = new StackPanel();
        foreach (var (id, text, detail) in DextraSettings.FirstDuties)
        {
            var toggle = new SettingsSwitch(new Binding<bool>(() => this.settings.Value.FirstDuty == id,
                on => this.settings.Update(p => p with { FirstDuty = on ? id : (p.FirstDuty == id ? null : p.FirstDuty) })));
            AutomationProperties.SetName(toggle, text);
            duties.Add(toggle);
            if (dutyRows.Children.Count > 0) dutyRows.Children.Add(new ChromeRowDivider());
            dutyRows.Children.Add(new ChromeRow(text, () => toggle, detail));
        }

        var (doneButton, doneShown) = TaskLook.Small(AssistantSettingsWords.Done, Finish, primary: true, name: "Done, start Dextra");
        DoneButton = doneButton;
        doneShown.HorizontalAlignment = HorizontalAlignment.Right;

        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new StackPanel { Spacing = 4, Children = { title, lead } });
        stack.Children.Add(Choice(1, AssistantSettingsWords.RowProvider, rows.BrainRows()));
        stack.Children.Add(Choice(2, AssistantSettingsWords.Reading, rows.ReadingRows()));
        stack.Children.Add(Choice(3, AssistantSettingsWords.Delegate, rows.DelegateRows()));
        stack.Children.Add(Choice(4, AssistantSettingsWords.FirstDuty, dutyRows));
        stack.Children.Add(Section(AssistantSettingsWords.Rules, new ChromeCard(() => AssistantRows.RulesList(openSettings))));
        stack.Children.Add(doneShown);

        Padding = new Thickness(18, 16);
        Background = TaskLook.Card.Brush(this);
        BorderBrush = TaskLook.Line.Brush(this);
        BorderThickness = new Thickness(1);
        this.RoundedRectangle(16, RoundedCornerStyle.Continuous);
        Child = stack;
        AutomationProperties.SetName(this, AssistantSettingsWords.FirstRunTitle);
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Group);

        settings.Changed += OnSettings;
        DetachedFromVisualTree += (_, _) => settings.Changed -= OnSettings;
        AttachedToVisualTree += (_, _) => { settings.Changed -= OnSettings; settings.Changed += OnSettings; rows.Show(); };
    }

    /// <summary>The primary button; first run is done once it is pressed.</summary>
    public Button DoneButton { get; }

    /// <summary>The three first-duty switches, in the order of <see cref="DextraSettings.FirstDuties"/>.</summary>
    public IReadOnlyList<SettingsSwitch> DutySwitches => duties;

    internal AssistantRows Rows => rows;

    private void Finish()
    {
        settings.Update(p => p with { FirstRunDone = true });
        done?.Invoke();
    }

    // a change redraws the card once on the UI thread, and only while it is shown (the attach redraws it anyway)
    private void OnSettings() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(this)) rows.Show();
    });

    private static Control Choice(int number, string heading, Control content) =>
        Section(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "  " + heading, new ChromeCard(() => content));

    private static Control Section(string heading, Control card)
    {
        var head = new TextBlock { Text = heading, Margin = new Thickness(4, 0) };
        SwiftFont.System(12, SwiftFont.Weight.Semibold).Apply(head);
        head.Foreground = Chrome.SecondaryText.Brush(head);
        AutomationProperties.SetHeadingLevel(head, 3);
        return new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { head, card } };
    }
}
