using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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

        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        var plate = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = (isDark
                ? new SwiftColor(0, 0, 0, 0.3)
                : new SwiftColor(1, 1, 1, 0.34)).Brush(this),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = new SwiftColor(0, 0, 0, isDark ? 1 : 0.65).Resolve(ActualThemeVariant),
                Blur = 28,
                OffsetY = 10
            })
        };

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
            Background = Chrome.GlassTint.Opacity(isDark ? 0.22 : 0.16).Brush(this),
            Child = surfaceContent
        };
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous))); // :90

        var panel = new Grid
        {
            Width = size.Width,
            Height = size.Height
        };
        var panelContents = new Grid();
        panelContents.Children.Add(plate);
        panelContents.Children.Add(surface);
        panel.Children.Add(panelContents);
        canvas.Children.Add(panel);

        void Place(Point point)
        {
            Canvas.SetLeft(panel, point.X);
            Canvas.SetTop(panel, point.Y);
        }

        var strip = new Grid { Height = StripHeight };
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
            Place(origin);
        }

        void EndMove()
        {
            if (dragOrigin is { } finalOrigin)
                model.Settings.SidebarPanelOrigin = finalOrigin;
            dragOrigin = null;
            grab = null;
        }

        void Resize(Size translation)
        {
            var start = resizeStart ?? size;
            resizeStart = start;
            var next = new Size(
                Math.Min(Math.Max(MinSize.Width, start.Width + translation.Width), area.Width - (dragOrigin ?? rest).X - FloatingSidebarPanelMargin),
                Math.Min(Math.Max(MinSize.Height, start.Height + translation.Height), area.Height - (dragOrigin ?? rest).Y - FloatingSidebarPanelMargin));
            model.Settings.SidebarPanelSize = next;

            // The source reads the persisted value back through the size floor on each body update.
            size = ReadSize(next, area);
            surface.Width = size.Width;
            surface.Height = size.Height;
            panel.Width = size.Width;
            panel.Height = size.Height;
        }

        var resizeCursor = new Cursor(StandardCursorType.BottomRightCorner);
        var resizeHandle = new PanelDragHandle(
            translation => Resize(translation),
            () => resizeStart = null,
            resizeCursor,
            resizeCursor)
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom
        };
        ToolTip.SetTip(resizeHandle, "Drag to resize");

        surfaceContent.Children.Add(sidebarHolder);
        surfaceContent.Children.Add(strip);
        panelContents.Children.Add(resizeHandle);

        // :13,23-35 Keep the view and drag/search state while its containing area changes.
        updateArea = newArea =>
        {
            area = newArea;
            size = ReadSize(model.Settings.SidebarPanelSize ?? DefaultSize, area);
            rest = Clamp(model.Settings.SidebarPanelOrigin ?? new Point(FloatingSidebarPanelMargin, Chrome.ContentTopInset), size, area);
            origin = dragOrigin ?? rest;
            canvas.Width = Width = area.Width;
            canvas.Height = Height = area.Height;
            surface.Width = panel.Width = size.Width;
            surface.Height = panel.Height = size.Height;
            Place(origin);
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
