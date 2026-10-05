// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3; DESIGN cz.js:776 chipTap): the preview composer's model chip, in the real chip's style (ModelEffortButton: a plain chip button, the
// mark, the name at Callout, the chevron). The mark is the maker's dot; clicking steps to the next maker (the preview has no model popover); the name crossfades as the real
// chip's does when it changes.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewComposerChip : Decorator
{
    private readonly Action step;
    private readonly Grid nameCell = new();
    private readonly PreviewDot dot;

    internal Button Button { get; }
    internal TextBlock NameLabel { get; private set; }

    internal PreviewComposerChip(string makerId, Action step)
    {
        this.step = step;
        dot = new PreviewDot(Colour(makerId));
        var mark = new Grid { Width = 14, Height = 14, Children = { dot } };
        dot.HorizontalAlignment = HorizontalAlignment.Center;
        dot.VerticalAlignment = VerticalAlignment.Center;
        NameLabel = Label(PreviewMakers.For(makerId).Name);
        nameCell.Children.Add(NameLabel);
        var chevron = SwiftImage.SystemName("chevron.down").Font(Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText);
        chevron.Margin = new Thickness(5, 0, 0, 0);
        var row = new DockPanel { LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Center };
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(chevron, Dock.Right);
        nameCell.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(mark);
        row.Children.Add(chevron);
        row.Children.Add(nameCell);
        Button = PlainControls.Button(new Grid { Children = { row } }, () => step());
        Button.Focusable = false;
        Button.Chip();
        Child = Button;
        Describe(makerId);
        ActualThemeVariantChanged += (_, _) => dot.Set(Colour(current));
        AttachedToVisualTree += (_, _) => dot.Set(Colour(current));
    }

    private string current = "";

    private Avalonia.Media.Color Colour(string makerId)
    {
        current = makerId;
        return Avalonia.Media.Color.Parse(PreviewMakers.Dot(makerId, CzDark()));
    }

    private bool CzDark() => ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;

    private static TextBlock Label(string value) => EffortView.Text(value, SwiftFont.Callout, Chrome.PrimaryText);

    private void Describe(string makerId)
    {
        AutomationProperties.SetName(Button, PreviewMakers.ChipName(makerId));
    }

    /// <summary>A click (or an assistive technology's invoke): the next maker.</summary>
    internal void Activate() => step();

    /// <summary>The maker changed: the dot eases to its colour and the name crossfades.</summary>
    internal void Show(string makerId)
    {
        var old = NameLabel;
        var fresh = Label(PreviewMakers.For(makerId).Name);
        if (fresh.Text == old.Text) return;
        NameLabel = fresh;
        dot.Ease(Colour(makerId));
        Describe(makerId);
        nameCell.Children.Add(fresh);
        if (!FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(this) || old.Bounds.Width <= 0)
        {
            nameCell.Children.Remove(old);
            return;
        }
        // the old text keeps its laid-out width in a layer over the cell, taking no room, and fades out as the new one fades in
        var width = old.Bounds.Width;
        var height = old.Bounds.Height;
        nameCell.Children.Remove(old);
        old.Width = width;
        var layer = new Canvas { Height = height, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Children = { old } };
        nameCell.Children.Add(layer);
        var animation = Animation.EaseOut(duration: 0.2);
        EffortView.Animate(old, animation, progress =>
        {
            old.Opacity = 1 - Math.Clamp(progress, 0, 1);
            if (progress >= 1) nameCell.Children.Remove(layer);
        });
        EffortView.Animate(fresh, animation, progress => fresh.Opacity = Math.Clamp(progress, 0, 1));
    }
}
