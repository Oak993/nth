// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Mission chips hang under each node, one per active mission, marked with a Hydra glyph when a Hydra team is on it"; the canvas's .onode .ms .hd and
// its hydra icon, work/design/assistant/index.html lines 439-440 and 1576; ED-NEW-dextra-as17-4). No Swift twin.
// A mission chip: a small capsule, 18 px high, with a glyph that says the task's state without colour (a dot at work, two bars paused, a ring when it needs the owner, a hollow dot
// otherwise), a Hydra mark when a Hydra team is on it, and the start of its title. A press opens the task's lead chat; the chip is read by the full title and the state.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

internal static class TeamChips
{
    public const double Height = 18;

    /// <summary>The Hydra mark of the approved page's icon set: a head with two below it, joined (a 24-unit box).</summary>
    private const string HydraPath =
        "M14.3 6 A2.3 2.3 0 1 1 9.7 6 A2.3 2.3 0 1 1 14.3 6 Z M8.3 17.5 A2.3 2.3 0 1 1 3.7 17.5 A2.3 2.3 0 1 1 8.3 17.5 Z M20.3 17.5 A2.3 2.3 0 1 1 15.7 17.5 A2.3 2.3 0 1 1 20.3 17.5 Z "
        + "M12 8.3 L12 11.5 M12 11.5 L7.6 15.6 M12 11.5 L16.4 15.6";

    public static Button Make(TeamMission mission, Action<Guid>? open)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(StateGlyph(mission.Kind));
        if (mission.Hydra) row.Children.Add(HydraMark());
        var label = TaskLook.Text(mission.Label, 10.5, SwiftFont.Weight.Medium, TaskLook.Ink);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var face = new Border { Height = Height, Padding = new Thickness(7, 0), Child = row };
        face.Capsule(RoundedCornerStyle.Continuous);
        var rest = Chrome.Overlay(.07).Brush(face);
        var hot = Chrome.Overlay(.13).Brush(face);
        face.Background = rest;
        var enabled = open is not null && mission.LeadThread is not null;
        var button = PlainControls.Button(face, () =>
        {
            if (mission.LeadThread is { } lead) open?.Invoke(lead);
        });
        button.KeyboardFocus();
        button.IsEnabled = enabled;
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = rest;
        AutomationProperties.SetName(button, mission.Reading);
        AutomationProperties.SetHelpText(button, enabled ? "Opens its chat" : "");
        ToolTip.SetTip(button, mission.Title);
        return button;
    }

    /// <summary>The chip for the tasks that have none of their own ("+2"): a count, read as "2 more tasks"; it opens nothing.</summary>
    public static TeamMoreChip More(int hidden) => new() { Count = hidden };

    // the glyph is 8 px: filled = at work, two bars = paused, a ring = needs you, a hollow dot = the rest
    private static Control StateGlyph(TeamChipKind kind)
    {
        var host = new Panel { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(host, AccessibilityView.Raw);
        switch (kind)
        {
            case TeamChipKind.Working:
                host.Children.Add(Dot(filled: true, 8));
                break;
            case TeamChipKind.Paused:
                host.Children.Add(Bar(HorizontalAlignment.Left));
                host.Children.Add(Bar(HorizontalAlignment.Right));
                break;
            case TeamChipKind.Attention:
                host.Children.Add(Dot(filled: false, 8, thick: true));
                host.Children.Add(Dot(filled: true, 3));
                break;
            default:
                host.Children.Add(Dot(filled: false, 8));
                break;
        }
        return host;
    }

    private static Ellipse Dot(bool filled, double size, bool thick = false)
    {
        var dot = new Ellipse { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (filled) dot.Fill = TaskLook.Ink2.Brush(dot);
        else
        {
            dot.Stroke = TaskLook.Ink2.Brush(dot);
            dot.StrokeThickness = thick ? 1.6 : 1.2;
        }
        return dot;
    }

    private static Rectangle Bar(HorizontalAlignment side)
    {
        var bar = new Rectangle { Width = 2.5, Height = 8, HorizontalAlignment = side, RadiusX = .8, RadiusY = .8 };
        bar.Fill = TaskLook.Ink2.Brush(bar);
        return bar;
    }

    private static Control HydraMark()
    {
        var mark = new Path
        {
            Data = Geometry.Parse(HydraPath),
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.Stroke = TaskLook.Ink2.Brush(mark);
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        return mark;
    }
}

/// <summary>The "+N" chip: made once for a line of chips and given its count, so a change of count changes a text and never the tree.</summary>
internal sealed class TeamMoreChip : Border
{
    private readonly TextBlock label = TaskLook.Text("", 10.5, SwiftFont.Weight.Medium, TaskLook.Ink, wrap: false);

    public TeamMoreChip()
    {
        Height = TeamChips.Height;
        Padding = new Thickness(7, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        Child = label;
        this.Capsule(RoundedCornerStyle.Continuous);
        Background = Chrome.Overlay(.07).Brush(this);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Text);
    }

    /// <summary>How many tasks have no chip of their own.</summary>
    public int Count
    {
        set
        {
            label.Text = Marrow.Core.Port.Assistant.OrgChartWords.MoreChip(value);
            AutomationProperties.SetName(this, Marrow.Core.Port.Assistant.ControlWords.Plural(value, "more task"));
        }
    }
}
