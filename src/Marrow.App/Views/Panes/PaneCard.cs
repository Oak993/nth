using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.UI.Panes;

// PORT-ONLY: web surfaces W3a, a pane's card in the workspace (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.12 "Pane
// cards ... are tinted layers over [the window's one blur] (tint, hairline specular rim lit from the top left, soft wide
// shadow, 12 DIP radius, 8 DIP gaps) ... Header (36 DIP) ... Focused pane: rim brightens slightly"; 3.4 "an expanded pane
// is denser ... lifted"; ED-NEW-web-w3a-1). No Swift counterpart. The card is the pane's own visual: its body is laid out
// inside it alone, so a neighbour's spring re-measures nothing here but the card's own size. Opening and closing move
// only its opacity and scale (render transforms), never its layout.

namespace Marrow.App.Views.Panes;

/// <summary>A pane in the workspace: the glass card, its header and the pane's body.</summary>
public sealed class PaneCard : Border
{
    private readonly Border clip;
    private readonly Grid layout = new() { RowDefinitions = new RowDefinitions($"{PaneTokens.HeaderHeight},*") };
    private readonly ScaleTransform scale = new(1, 1);
    private readonly TranslateTransform offset = new(0, 0);
    private bool focused;
    private bool expanded;
    private bool lifted;

    public PaneCard(PaneKind kind, PaneContent content)
    {
        Kind = kind;
        Content = content;
        Header = new PaneHeader(kind);
        CornerRadius = new CornerRadius(PaneTokens.CornerRadius);
        BorderThickness = new Thickness(1);
        ClipToBounds = false;
        RenderTransform = new TransformGroup { Children = { scale, offset } };
        RenderTransformOrigin = RelativePoint.Center;
        // F6 reaches a pane as a whole (DESIGN 3.7, 10: "Every pane is a landmark ... F6 cycles panes and splitters"); Tab skips the
        // card itself and goes on into its body.
        Focusable = true;
        IsTabStop = false;
        clip = new Border { CornerRadius = new CornerRadius(PaneTokens.CornerRadius - 1), ClipToBounds = true, Child = layout };
        Child = clip;
        Grid.SetRow(Header, 0);
        layout.Children.Add(Header);
        var body = content.Body;
        body.HorizontalAlignment = HorizontalAlignment.Stretch;
        body.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        AutomationProperties.SetName(this, PaneKinds.AccessibleName(kind));
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Region);
        ApplyContent();
        content.Changed += ApplyContent;
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        GotFocus += (_, _) => UpdateFocus();
        LostFocus += (_, _) => UpdateFocus();
        ApplyLook();
    }

    public PaneKind Kind { get; }

    public PaneContent Content { get; }

    public PaneHeader Header { get; }

    /// <summary>The pane is closing: it fades out and leaves when its springs settle.</summary>
    public bool IsLeaving { get; internal set; }

    /// <summary>The card's scale (open and close: 0.96 to 1), a render transform only.</summary>
    public double Scale
    {
        get => scale.ScaleX;
        set
        {
            if (scale.ScaleX == value) return;
            scale.ScaleX = value;
            scale.ScaleY = value;
        }
    }

    /// <summary>
    /// Where a pane lifted by its header is, relative to its slot (3.6): a render transform only, so the drag costs no layout.
    /// </summary>
    public Vector Offset
    {
        get => new(offset.X, offset.Y);
        set
        {
            if (offset.X == value.X && offset.Y == value.Y) return;
            offset.X = value.X;
            offset.Y = value.Y;
        }
    }

    /// <summary>The lifted look of a pane held by its header: the deeper shadow, the rim as it is (3.6, canvas .pane.lift).</summary>
    public bool IsLiftedLook
    {
        get => lifted;
        set
        {
            if (lifted == value) return;
            lifted = value;
            ApplyLook();
        }
    }

    /// <summary>The expanded look: denser and lifted (3.4).</summary>
    public bool IsExpandedLook
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            ApplyLook();
        }
    }

    /// <summary>Whether keyboard focus is inside the card (its rim brightens).</summary>
    public bool IsFocusedLook => focused;

    internal void Detach() => Content.Changed -= ApplyContent;

    private void ApplyContent()
    {
        Header.SetTitle(Content.Title ?? PaneKinds.Title(Kind), Content.Subtitle);
        Header.SetActions(Content.Actions);
        Header.TitleMenu = Content.TitleMenu;
    }

    private void UpdateFocus()
    {
        var now = IsKeyboardFocusWithin;
        if (now == focused) return;
        focused = now;
        ApplyLook();
    }

    private void ApplyLook()
    {
        ThemeVariant theme = ActualThemeVariant;
        Background = PaneTokens.CardFill(theme, expanded);
        BorderBrush = PaneTokens.Rim(theme, focused);
        BoxShadow = PaneTokens.Shadow(theme, expanded || lifted);
    }
}
