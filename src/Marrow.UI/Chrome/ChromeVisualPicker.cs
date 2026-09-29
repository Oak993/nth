using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;

// Source: DroppyCode/UI/Chrome/ChromeVisualPicker.swift (B3 52bc792), :1-106.

namespace Marrow.UI.Chrome;

/// <summary>
/// :10-19 <c>enum ChromeVisualTileMetrics</c>: the tile's measures. Every tile is the same size, so a picker
/// of two options and a picker of three line up with each other and with the rows around them.
/// </summary>
public static class ChromeVisualTileMetrics
{
    public const double Height = 48; // :11
    public const double RegularWidth = 76; // :12
    public const double DenseWidth = 76; // :13
    public const double CornerRadius = 6; // :14
    public const double RingInset = 3; // :15
    public const double RingWidth = 2; // :16
    public const double Spacing = 12; // :17
    public const double CaptionSpacing = 6; // :18
}

/// <summary>
/// :22-75 The row control: one tile per option, the chosen one ringed (generic over value and preview type).
/// PR-THEME-182: A visual picker is one row of equal 76 point tiles, one per option, and a click selects that
/// option with the 0.1 second hover animation.
/// </summary>
/// <typeparam name="TValue">The type of the option's value (Hashable in Swift).</typeparam>
/// <typeparam name="TPreview">The type of the preview content (View in Swift).</typeparam>
public sealed class ChromeVisualPicker<TValue, TPreview> : Decorator
    where TValue : notnull
    where TPreview : Control
{
    /// <summary>
    /// Constructor.
    /// :27-30 `init(options:selection:preview:)` init takes options, @Binding selection, and @ViewBuilder preview.
    /// </summary>
    public ChromeVisualPicker(
        (TValue value, string title)[] options,
        Binding<TValue> selection,
        Func<TValue, TPreview> preview)
    {
        // :33-35 Calculate the tile width: 76 for 2 or 3 options (regularWidth and denseWidth are both 76)
        var tileWidth = options.Length >= 3
            ? ChromeVisualTileMetrics.DenseWidth
            : ChromeVisualTileMetrics.RegularWidth;

        // Create a horizontal stack panel for the tiles
        // :37-46 HStack(alignment: .top, spacing: spacing) containing ForEach over the options
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = ChromeVisualTileMetrics.Spacing,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Add one tile per option
        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var tile = new ChromeVisualTile(
                title: option.title,
                isSelected: SwiftEquality.Equal(option.value, selection.Value), // :40 option.value == selection
                width: tileWidth,
                action: () => {
                    // :40-41 withAnimation(Chrome.hover) { selection = option.value }
                    Transaction.WithAnimation(Chrome.Hover, () =>
                    {
                        selection.Value = option.value;
                    });
                },
                content: () => preview(option.value)
            );

            // Track the selection to update isSelected
            var optionValue = option.value;
            tile.Track(() => selection.Value, selected =>
            {
                tile.UpdateSelected(SwiftEquality.Equal(optionValue, selected));
            });

            stack.Children.Add(tile);
        }

        // :48 .background { NoWindowDragArea() } - prevent window drag
        Child = new NoWindowDragArea { Child = stack };
    }
}

/// <summary>
/// :53-106 One tile: a single symbol on a stage, accent on the symbol the chosen option carries.
/// PR-THEME-183: A visual tile is a 76 by 48 stage holding the caller's symbol, ringed and accented when chosen,
/// brighter when hovered, with its name underneath.
/// </summary>
public sealed class ChromeVisualTile : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    private readonly string title;
    private bool isSelected;
    private readonly double width;
    private readonly Func<Control> content;
    private readonly Action action;
    private readonly ViewState state = new();
    private Border? stageBack;
    private Border? paddedStage;
    private TextBlock? captionBlock;

    /// <summary>
    /// Constructor for ChromeVisualTile.
    /// </summary>
    public ChromeVisualTile(
        string title,
        bool isSelected,
        double width,
        Action action,
        Func<Control> content)
    {
        this.title = title;
        this.isSelected = isSelected;
        this.width = width;
        this.content = content;
        this.action = action;

        // Set accessibility properties
        // :72 .accessibilityLabel(Text(verbatim: title))
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetAutomationId(this, isSelected ? "selected" : null);

        // Create the vertical stack for stage and caption
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = ChromeVisualTileMetrics.CaptionSpacing,
        };

        // Add the stage (content holder)
        stack.Children.Add(CreateStage());

        // Add the caption (title text)
        stack.Children.Add(CreateCaption());

        // :61-71 Create the button with plain style
        var button = PlainControls.Button(stack, action);

        // :69-70 .onHover { hovering in withAnimation(Chrome.hover) { isHovering = hovering } }
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering));

        // Track hover state to update visuals
        stack.Track(() => state.IsHovering, _ => UpdateStageVisuals());

        Child = button;

        AttachedToVisualTree += (_, _) => WatchContent();
        DetachedFromVisualTree += (_, _) =>
        {
            contentWatch?.Dispose();
            contentWatch = null;
        };
    }

    /// <summary>
    /// Create the stage (the content area with background and ring).
    /// :86-105 The stage: content().font(...).foregroundStyle(...).frame(...).background(...).clipShape(...).padding(...).overlay(...)
    /// </summary>
    private Control CreateStage()
    {
        // Create a border for the background and ring
        stageBack = new Border
        {
            Width = width,
            Height = ChromeVisualTileMetrics.Height,
            CornerRadius = new CornerRadius(ChromeVisualTileMetrics.CornerRadius),
        };

        // Apply the background fill: Chrome.overlay(isHovering || isSelected ? 0.10 : 0.06)
        UpdateStageBrush();

        // The content itself, built where the tile reads observable state (:44-46 the body calls preview again after
        // anything it read changes); it takes the font and colour below when installed.
        WatchContent();

        // Wrap in a border to add the ring overlay
        paddedStage = new Border
        {
            Child = stageBack,
            Padding = new Thickness(ChromeVisualTileMetrics.RingInset - ChromeVisualTileMetrics.RingWidth),
            BorderThickness = new Thickness(ChromeVisualTileMetrics.RingWidth),
            CornerRadius = new CornerRadius(ChromeVisualTileMetrics.CornerRadius + ChromeVisualTileMetrics.RingInset), // ChromeVisualPicker.swift:98-101 ring radius 6 + 3.
        };

        UpdateRing();

        return paddedStage;
    }

    // :44-46 content() is part of the body: SwiftUI evaluates it again after any state it read changes, so the stage
    // holds a tracked evaluation that swaps in the fresh view. Torn down on detach and re-armed on attach.
    private Tracked<Control>? contentWatch;

    private void WatchContent() => contentWatch ??= new Tracked<Control>(content, InstallContent);

    private void InstallContent(Control contentControl)
    {
        if (stageBack is null) return;
        // :88 .font(.system(.title2, weight: .medium))
        ApplyFontToContent(contentControl, SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium));
        stageBack.Child = null;
        stageBack.Child = contentControl;
        // :89 .foregroundStyle(isSelected ? Chrome.accent : Chrome.secondaryText)
        UpdateContentColor(contentControl);
    }

    /// <summary>
    /// Create the caption (the title text below the stage).
    /// :76-84 Text(verbatim: title).font(.system(.subheadline)).foregroundStyle(...)
    /// </summary>
    private Control CreateCaption()
    {
        captionBlock = new TextBlock
        {
            Text = title,
            Width = width,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2, // :76-84 .lineLimit(2)
            TextTrimming = TextTrimming.CharacterEllipsis, // SwiftUI's default truncation is the tail ellipsis
        };

        // :79 .font(.system(.subheadline))
        var subheadlineFont = SwiftFont.System(SwiftFont.TextStyle.Subheadline);
        subheadlineFont.Apply(captionBlock);

        // :80 .foregroundStyle(isSelected ? Chrome.primaryText : Chrome.secondaryText)
        UpdateCaptionColor();

        return captionBlock;
    }

    /// <summary>
    /// Update the stage background brush based on the hover and selection state.
    /// :93 .fill(Chrome.overlay(isHovering || isSelected ? 0.10 : 0.06))
    /// </summary>
    private void UpdateStageBrush()
    {
        if (stageBack is null) return;
        var opacity = state.IsHovering || isSelected ? 0.10 : 0.06;
        stageBack.Background = Chrome.Overlay(opacity).Brush(this);
    }

    /// <summary>
    /// Update the ring visuals.
    /// :96-102 RoundedRectangle stroke overlay, inset 3, stroke 2, radius 9
    /// </summary>
    private void UpdateRing()
    {
        if (paddedStage is null) return;
        paddedStage.BorderBrush = isSelected ? Chrome.Accent.Brush(this) : null;
    }

    /// <summary>
    /// Update the stage visuals (background and ring) when hover or selection changes.
    /// </summary>
    private void UpdateStageVisuals()
    {
        UpdateStageBrush();
    }

    /// <summary>
    /// Apply font to the content control (recursively if needed).
    /// </summary>
    private void ApplyFontToContent(Control control, SwiftFont font)
    {
        SwiftImage.SetFont(control, font);
        control.Font(font);
        if (control is TextBlock textBlock)
        {
            font.Apply(textBlock);
        }
        else if (control is SwiftImage image)
        {
            image.Font(font); // ChromeVisualPicker.swift:88 applies .title2 medium to the caller's Image too.
        }
        else if (control is ContentControl contentControl && contentControl.Content is Control child)
        {
            ApplyFontToContent(child, font);
        }
    }

    /// <summary>
    /// Update the content (symbol) color based on selection state.
    /// </summary>
    private void UpdateContentColor(Control control)
    {
        var color = isSelected ? Chrome.Accent : Chrome.SecondaryText;
        TextElement.SetForeground(control, color.Brush(control));
        if (control is TextBlock textBlock)
        {
            textBlock.Foreground = color.Brush(this);
        }
        else if (control is SwiftImage image)
        {
            image.ForegroundStyle(color); // ChromeVisualPicker.swift:89 selected accent / unselected secondaryText.
        }
    }

    /// <summary>
    /// Update the caption (title) color based on selection state.
    /// </summary>
    private void UpdateCaptionColor()
    {
        if (captionBlock is null) return;
        var color = isSelected ? Chrome.PrimaryText : Chrome.SecondaryText;
        captionBlock.Foreground = color.Brush(this);
    }

    /// <summary>
    /// Update the selection state and visuals.
    /// </summary>
    public void UpdateSelected(bool selected)
    {
        AutomationProperties.SetAutomationId(this, selected ? "selected" : null);
        if (isSelected == selected) return;

        isSelected = selected;

        // Update accessibility trait
        // :73 .accessibilityAddTraits(isSelected ? .isSelected : [])

        // Update stage visuals
        UpdateStageBrush();
        UpdateRing();

        // Update content and caption colors
        if (stageBack?.Child is Control contentControl)
        {
            UpdateContentColor(contentControl);
        }

        UpdateCaptionColor();
    }

    /// <summary>
    /// Gets the selection state of this tile.
    /// </summary>
    public bool IsSelected => isSelected;

    /// <summary>
    /// Gets the width of this tile.
    /// </summary>
    public new double Width => width;

    protected override AutomationPeer OnCreateAutomationPeer() => new TilePeer(this);

    private sealed class TilePeer(ChromeVisualTile owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        public bool IsSelected => owner.IsSelected;
        public ISelectionProvider? SelectionContainer => null;
        public void AddToSelection() => owner.action();
        public void Select() => owner.action();
        public void RemoveFromSelection() { }
        public void Invoke() => owner.action();
    }
}
