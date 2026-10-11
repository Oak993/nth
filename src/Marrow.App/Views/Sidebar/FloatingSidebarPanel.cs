using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/FloatingSidebarPanel.swift (B3 52bc792), :1-127.

/// <summary>:9-127 <c>struct FloatingSidebarPanel: View</c>: the sidebar's thread list as a floating glass panel.</summary>
public sealed class FloatingSidebarPanel : Decorator
{
    // :17 static let defaultSize = CGSize(width: 300, height: 560)
    public static Size DefaultSize { get; } = new(300, 560);

    // :18 static let minSize = CGSize(width: 240, height: 320)
    public static Size MinSize { get; } = new(240, 320);

    // :19 static let margin: CGFloat = 12
    public const double FloatingSidebarPanelMargin = 12; // Swift: margin (clashes with Layoutable.Margin)

    // :20 static let stripHeight: CGFloat = 44
    public const double StripHeight = 44;

    // :23-25 @State transient drag and resize values
    private Point? dragOrigin;
    private Point? grab;
    private Size? resizeStart;
    private Action<Size> updateArea = null!;

    // :13-15, :27-105
    public FloatingSidebarPanel(Size area, bool canClose, Action close)
    {
        var model = WindowManager.Shared.Model;
        var stored = model.Settings.SidebarPanelSize ?? DefaultSize;
        var size = ReadSize(stored, area);
        var storedOrigin = model.Settings.SidebarPanelOrigin ?? new Point(FloatingSidebarPanelMargin, Chrome.ContentTopInset);
        var rest = Clamp(storedOrigin, size, area);
        var origin = rest;

        var canvas = new Canvas
        {
            Width = area.Width,
            Height = area.Height,
            ClipToBounds = false
        };

        var plate = new Border { CornerRadius = new CornerRadius(22) };
        // :100 the plate's shadow is its own coverage, cast at its fill's alpha and drawn under it, through it (ED-358).
        var plateShadow = new SwiftShadow.Caster(plate, Colors.Black, radius: 28, y: 10);

        var sidebar = new SidebarView(inPopover: true, dismiss: null);
        var sidebarHolder = new Border
        {
            Child = sidebar,
            Padding = new Thickness(0, StripHeight, 0, 0)
        };

        var surfaceContent = new Grid();
        var surface = new Border
        {
            Width = size.Width,
            Height = size.Height,
            CornerRadius = new CornerRadius(22),
            ClipToBounds = true,
            Child = surfaceContent
        };
        // :87, :97 `let isDark = colorScheme == .dark` is read inside `body`, so the tint, the plate and its shadow follow the
        // scheme as it changes (the control is built before it has a theme, so this cannot be fixed at construction).
        void UpdateTheme()
        {
            var isDark = ActualThemeVariant == ThemeVariant.Dark;
            surface.Background = Chrome.GlassTint.Opacity(isDark ? 0.22 : 0.16).Brush(this); // :92
            plate.Background = (isDark
                ? new SwiftColor(0, 0, 0, 0.3)
                : new SwiftColor(1, 1, 1, 0.34)).Brush(this); // :99
            plateShadow.Set(new SwiftColor(0, 0, 0, isDark ? 1 : 0.65).Resolve(ActualThemeVariant),
                radius: 28, y: 10); // :100 (sigma 28, ED-327), cast by the 0.3 / 0.34 fill and drawn under it (ED-358)
        }
        UpdateTheme();
        ActualThemeVariantChanged += (_, _) => UpdateTheme();
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous))); // :90

        var panel = new Grid
        {
            Width = size.Width,
            Height = size.Height
        };
        var panelContents = new Grid();
        panelContents.Children.Add(plateShadow);
        panelContents.Children.Add(plate);
        panelContents.Children.Add(surface);
        panel.Children.Add(panelContents);
        canvas.Children.Add(panel);

        var placed = origin;
        var slide = new ChatFrameAnimation(this);
        slide.AttachLifetime();

        void Place(Point point)
        {
            placed = point;
            Canvas.SetLeft(panel, point.X);
            Canvas.SetTop(panel, point.Y);
        }

        // :102, :104 `.offset(x:y:)` with `.animation(dragOrigin == nil ? Chrome.panelSlide : nil, value: origin)`: a change of
        // origin slides while the strip is not held and snaps while it is.
        void Settle(Point target)
        {
            slide.Stop();
            if (dragOrigin is not null || target == placed || TopLevel.GetTopLevel(this) is null)
            {
                Place(target);
                return;
            }
            var from = placed;
            slide.Start(Chrome.PanelSlide, progress =>
                Place(new Point(from.X + (target.X - from.X) * progress, from.Y + (target.Y - from.Y) * progress)));
        }

        // :28-34 `body` reads the settings again whenever it runs: the stored size, the resting origin from it, the origin drawn.
        void Refresh()
        {
            size = ReadSize(model.Settings.SidebarPanelSize ?? DefaultSize, area);
            rest = Clamp(model.Settings.SidebarPanelOrigin ?? new Point(FloatingSidebarPanelMargin, Chrome.ContentTopInset), size, area);
            origin = dragOrigin ?? rest;
            canvas.Width = Width = area.Width;
            canvas.Height = Height = area.Height;
            surface.Width = panel.Width = size.Width;
            surface.Height = panel.Height = size.Height;
            Settle(origin);
        }

        // :39 .overlay(alignment: .top): the strip hugs the top of the panel (a fixed-height Grid would otherwise centre).
        var strip = new Grid { Height = StripHeight, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var titleText = new TextBlock
        {
            Text = "Threads",
            Foreground = Chrome.PrimaryText.Opacity(0.92).Brush(this),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(titleText);

        var titleContent = new Border
        {
            Child = titleText,
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
            Height = Chrome.CapsuleContentHeight + Chrome.CapsuleVerticalPadding * 2
        };
        var titleCapsule = Chrome.ChromeGlassCapsule(titleContent);
        var titleDrag = new PanelDragHandle(
            translation => Move(translation),
            EndMove)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        var titleOverlay = new Grid();
        titleOverlay.Children.Add(titleCapsule);
        titleOverlay.Children.Add(titleDrag);

        var titleHolder = new Border
        {
            Child = titleOverlay,
            Margin = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        var dragHandle = new PanelDragHandle(
            translation => Move(translation),
            EndMove)
        {
            Height = StripHeight,
            Margin = new Thickness(0, 0, canClose ? 8 : 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        ToolTip.SetTip(dragHandle, "Drag to move");
        AutomationProperties.SetName(dragHandle, "Drag to move");

        Grid.SetColumn(titleHolder, 0);
        Grid.SetColumn(dragHandle, 1);
        strip.Children.Add(titleHolder);
        strip.Children.Add(dragHandle);

        if (canClose)
        {
            var closeButton = new ChromeCircleButton("xmark", "Back into the column", close)
            {
                Margin = new Thickness(0, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };
            Grid.SetColumn(closeButton, 2);
            strip.Children.Add(closeButton);
        }

        void Move(Size translation)
        {
            var start = grab ?? rest;
            grab = start;
            var moved = Clamp(new Point(start.X + translation.Width, start.Y + translation.Height), size, area);
            dragOrigin = moved;
            origin = moved;
            slide.Stop();
            Place(origin);
        }

        void EndMove()
        {
            if (dragOrigin is { } finalOrigin)
                model.Settings.SidebarPanelOrigin = finalOrigin;
            dragOrigin = null;
            grab = null;
            Refresh(); // :114-118 the next body reads the written origin as the resting one
        }

        // The largest size the panel can take where it rests: its origin stays, its far edges stop at the margin.
        Size Largest() => new(
            Math.Max(MinSize.Width, area.Width - (dragOrigin ?? rest).X - FloatingSidebarPanelMargin),
            Math.Max(MinSize.Height, area.Height - (dragOrigin ?? rest).Y - FloatingSidebarPanelMargin));

        Size Fit(Size wanted) => new(
            Math.Min(Math.Max(MinSize.Width, wanted.Width), Largest().Width),
            Math.Min(Math.Max(MinSize.Height, wanted.Height), Largest().Height));

        void Resize(Size translation)
        {
            SettleGrow();
            var start = resizeStart ?? size;
            resizeStart = start;
            var next = new Size(
                Math.Min(Math.Max(MinSize.Width, start.Width + translation.Width), area.Width - (dragOrigin ?? rest).X - FloatingSidebarPanelMargin),
                Math.Min(Math.Max(MinSize.Height, start.Height + translation.Height), area.Height - (dragOrigin ?? rest).Y - FloatingSidebarPanelMargin));
            model.Settings.SidebarPanelSize = next;

            // The source reads the persisted value back through the size floor on each body update.
            Refresh();
        }

        // PORT-ONLY (web W1, ED-NEW-web-w1-6): a key, Home, End or the double-click reset moves the size under PanelSlide
        // from the size drawn now; a press lands a change still in flight first (pointer-down settling).
        var grow = new ChatFrameAnimation(this);
        grow.AttachLifetime();
        Size? growing = null;

        void GrowTo(Size wanted)
        {
            var to = Fit(wanted);
            var from = size;
            SettleGrow();
            size = from;
            if (to == from)
            {
                Refresh();
                return;
            }
            if (!FrameClock.IsBound || TopLevel.GetTopLevel(this) is null)
            {
                model.Settings.SidebarPanelSize = to;
                Refresh();
                return;
            }
            growing = to;
            grow.Start(Chrome.PanelSlide, progress =>
            {
                size = new Size(from.Width + (to.Width - from.Width) * progress, from.Height + (to.Height - from.Height) * progress);
                surface.Width = panel.Width = size.Width;
                surface.Height = panel.Height = size.Height;
                if (progress < 1) return;
                growing = null;
                model.Settings.SidebarPanelSize = to;
                Refresh();
            });
        }

        void SettleGrow()
        {
            if (growing is not { } to) return;
            grow.Stop();
            growing = null;
            model.Settings.SidebarPanelSize = to;
            Refresh();
        }

        SplitterReport Report(bool height)
        {
            var largest = Largest();
            var limits = height
                ? new SplitterLimits(MinSize.Height, largest.Height, Math.Min(DefaultSize.Height, largest.Height))
                : new SplitterLimits(MinSize.Width, largest.Width, Math.Min(DefaultSize.Width, largest.Width));
            var shown = growing ?? size;
            return SplitterMath.Report(height ? shown.Height : shown.Width, false, limits);
        }

        // DESIGN 4.3 (web W1, ED-NEW-web-w1-6): the shared Splitter on the free edges (right and bottom, 8 DIP) and the
        // 16 DIP bottom-right corner, which was the only grip (PanelDragHandle, tooltip "Drag to resize"). The numbers are
        // the source's: at least 240 x 320, at most the area past the resting origin, 300 x 560 by default, saved globally.
        Splitter Grip(SplitterOrientation orientation, string label)
        {
            var width = orientation != SplitterOrientation.Horizontal;
            var height = orientation != SplitterOrientation.Vertical;
            return new Splitter(orientation, new SplitterTarget
            {
                OnReport = () => Report(orientation == SplitterOrientation.Horizontal),
                OnSettle = SettleGrow,
                OnMove = travel => Resize(new Size(width ? travel.X : 0, height ? travel.Y : 0)),
                OnEnd = _ => resizeStart = null,
                OnReset = () => GrowTo(DefaultSize),
                OnStep = delta => GrowTo(new Size((growing ?? size).Width + (width ? delta.X : 0), (growing ?? size).Height + (height ? delta.Y : 0))),
                OnStepTo = maximum => GrowTo(maximum
                    ? new Size(width ? Largest().Width : size.Width, height ? Largest().Height : size.Height)
                    : new Size(width ? MinSize.Width : size.Width, height ? MinSize.Height : size.Height)),
                OnSetValue = orientation == SplitterOrientation.Both ? null : value => GrowTo(width
                    ? new Size(value, (growing ?? size).Height)
                    : new Size((growing ?? size).Width, value)),
            })
            {
                Label = label,
                MirrorsInRightToLeft = false, // placed and measured in window coordinates, as before
                FineInset = 0,
            };
        }

        var rightGrip = Grip(SplitterOrientation.Vertical, "Resize sidebar width");
        rightGrip.Width = Splitter.EdgeThickness;
        rightGrip.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        rightGrip.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        var bottomGrip = Grip(SplitterOrientation.Horizontal, "Resize sidebar height");
        bottomGrip.Height = Splitter.EdgeThickness;
        bottomGrip.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        bottomGrip.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        var resizeHandle = Grip(SplitterOrientation.Both, "Resize sidebar");
        resizeHandle.SetCorner(SplitterCorner.BottomRight);
        resizeHandle.Width = Splitter.CornerSize;
        resizeHandle.Height = Splitter.CornerSize;
        resizeHandle.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        resizeHandle.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;

        surfaceContent.Children.Add(sidebarHolder);
        surfaceContent.Children.Add(strip);
        panelContents.Children.Add(rightGrip);
        panelContents.Children.Add(bottomGrip);
        panelContents.Children.Add(resizeHandle);

        // :13,23-35 Keep the view and drag/search state while its containing area changes.
        updateArea = newArea =>
        {
            area = newArea;
            Refresh();
        };

        Place(origin);
        Child = canvas;
        Width = area.Width;
        Height = area.Height;
    }

    // :13,23-35 The parent changes the input while this instance retains its @State.
    internal void UpdateArea(Size area) => updateArea(area);

    // :28-32
    private static Size ReadSize(Size stored, Size area) =>
        new(
            Math.Min(Math.Max(MinSize.Width, stored.Width), Math.Max(MinSize.Width, area.Width - FloatingSidebarPanelMargin * 2)),
            Math.Min(Math.Max(MinSize.Height, stored.Height), Math.Max(MinSize.Height, area.Height - FloatingSidebarPanelMargin * 2)));

    // :121-126 private static func clamp(_:size:in:) -> CGPoint
    private static Point Clamp(Point origin, Size size, Size @in) =>
        new(
            Math.Min(Math.Max(FloatingSidebarPanelMargin, origin.X), Math.Max(FloatingSidebarPanelMargin, @in.Width - size.Width - FloatingSidebarPanelMargin)),
            Math.Min(Math.Max(FloatingSidebarPanelMargin, origin.Y), Math.Max(FloatingSidebarPanelMargin, @in.Height - size.Height - FloatingSidebarPanelMargin)));
}
