// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.5 look: pill-shaped tinted fills, the accent only for the current value and focus,
// 10 accessibility: a radio for one choice, a check box for several; ED-NEW-web-w25-1). One choice of a choice question: a label, an
// optional line under it, a mark that fills with the accent when chosen. The same plate the provider's own question sheet draws (tinted
// fill, 10 radius, accent at 0.12 when chosen), made a real radio button or check box so a screen reader hears what it is and whether it
// is chosen, and keyboard focus shows a ring.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.AskUser;

internal sealed class AskChoicePlate
{
    private readonly bool multiple;
    private readonly Border plate;
    private readonly Border markSlot = new() { Width = 18, Height = 18 };
    private readonly TextBlock label;
    private bool hovering;
    private bool focused;
    private bool suppress;

    public AskChoicePlate(AskOption option, bool multiple, string group)
    {
        this.multiple = multiple;
        Label = option.Label;
        label = TimelineRows.ChatText(option.Label, SwiftFont.TextStyle.Callout);
        var text = new StackPanel { Spacing = 2, Children = { label } };
        if (option.Detail is { Length: > 0 } detail)
            text.Children.Add(TimelineRows.ChatText(detail, SwiftFont.TextStyle.Subheadline, color: Chrome.SecondaryText));

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), ColumnSpacing = 10 };
        Grid.SetColumn(text, 1);
        content.Children.Add(markSlot);
        content.Children.Add(text);
        plate = new Border { Padding = new Thickness(12, 9), Child = content, HorizontalAlignment = HorizontalAlignment.Stretch }
            .RoundedRectangle(10, RoundedCornerStyle.Continuous);

        ToggleButton button = multiple ? new CheckBox() : new RadioButton { GroupName = group };
        button.Content = plate;
        button.Padding = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Template = new FuncControlTemplate<ToggleButton>((owner, scope) =>
            new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
        AutomationProperties.SetName(button, option.Detail is { Length: > 0 } line ? option.Label + ", " + line : option.Label);
        button.IsCheckedChanged += (_, _) => { Refresh(); if (!suppress) Toggled?.Invoke(); };
        button.PointerEntered += (_, _) => { hovering = true; Refresh(); };
        button.PointerExited += (_, _) => { hovering = false; Refresh(); };
        button.GotFocus += (_, e) => { focused = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional; Refresh(); };
        button.LostFocus += (_, _) => { focused = false; Refresh(); };
        Button = button;
        Refresh();
    }

    public string Label { get; }

    public ToggleButton Button { get; }

    /// <summary>The person chose or cleared this plate.</summary>
    public event Action? Toggled;

    public bool IsSelected
    {
        get => Button.IsChecked == true;
        set
        {
            if (IsSelected == value) return;
            suppress = true;
            try { Button.IsChecked = value; }
            finally { suppress = false; }
            Refresh();
        }
    }

    private void Refresh()
    {
        var selected = IsSelected;
        plate.Background = (selected ? Chrome.Accent.Opacity(0.12) : Chrome.Overlay(hovering ? 0.10 : 0.06)).Brush(plate);
        plate.BorderThickness = new Thickness(focused ? 2 : 0);
        plate.BorderBrush = focused ? Chrome.Accent.Brush(plate) : null;
        var mark = SwiftImage.SystemName(multiple
                ? selected ? "checkmark.square.fill" : "square"
                : selected ? "checkmark.circle.fill" : "circle")
            .Font(SwiftFont.System(14))
            .ForegroundStyle(selected ? Chrome.Accent : Chrome.SecondaryText);
        mark.Width = 14;
        mark.Margin = new Thickness(0, 1, 0, 0);
        markSlot.Child = mark;
        Marrow.UI.Common.ChatFont.Chat(SwiftFont.TextStyle.Callout, selected ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Apply(label);
    }
}
