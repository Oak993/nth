using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/HydraFusedPairIcon.swift (B2 1fbf81e), :1-91.

namespace Marrow.App.Views.Composer;

/// <summary>:7-91 <c>struct HydraFusedPairIcon: View</c>.</summary>
// HydraFusedPairIcon.swift:7-91
public sealed class HydraFusedPairIcon : Decorator
{
    // HydraFusedPairIcon.swift:18-19
    private const double Gap = 1;

    // HydraFusedPairIcon.swift:10, :19
    private readonly double size;
    private readonly double halfWidth;

    // HydraFusedPairIcon.swift:25-34
    private readonly Border leadHalf;
    private readonly Border headsHalf;
    private readonly Border divider;
    private readonly LinearGradientBrush gradient;

    // HydraFusedPairIcon.swift:15
    private bool isFused;

    /// <param name="lead">HydraFusedPairIcon.swift:8 <c>let lead: ProviderKind</c>.</param>
    /// <param name="heads">HydraFusedPairIcon.swift:9 <c>let heads: ProviderKind</c>.</param>
    /// <param name="size">HydraFusedPairIcon.swift:10 <c>var size: CGFloat = 15</c>.</param>
    /// <param name="animates">HydraFusedPairIcon.swift:12 <c>var animates: Bool = true</c>.</param>
    // HydraFusedPairIcon.swift:21-44
    public HydraFusedPairIcon(ProviderKind lead, ProviderKind heads, double size = 15, bool animates = true)
    {
        this.size = size;
        halfWidth = (size - 1) / 2 - Gap;

        gradient = CreateGradientBrush(lead, heads, ActualThemeVariant);

        leadHalf = Half(lead, alignment: AlignmentX.Left);
        headsHalf = Half(heads, alignment: AlignmentX.Right);

        // HydraFusedPairIcon.swift:31-34
        divider = new Border
        {
            Width = 1,
            Height = size * 0.85,
            CornerRadius = new CornerRadius(0.5),
            Background = Chrome.SecondaryText.Opacity(0.55).Brush(this),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new ScaleTransform(1, 0.15),
        };

        var body = new Grid
        {
            Width = size,
            Height = size,
            Children = { leadHalf, headsHalf, divider },
        };

        Width = size;
        Height = size;
        Child = body;

        // HydraFusedPairIcon.swift:23-34
        SetFused(!animates);

        // HydraFusedPairIcon.swift:37-42
        this.Task(() => $"{lead.Id}/{heads.Id}", async _ =>
        {
            if (!animates) return;

            SetFused(false);
            await FrameClock.TrySleep(TimeSpan.FromMilliseconds(30));
            Transaction.WithAnimation(
                EnvironmentValues.GetAccessibilityReduceMotion(this)
                    ? null
                    : Animation.Spring(duration: 0.55, bounce: 0.25),
                () => SetFused(true));
        });

        ActualThemeVariantChanged += (_, _) => UpdateGradient(lead, heads);

        // HydraFusedPairIcon.swift:43
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    // HydraFusedPairIcon.swift:66-74
    public static IReadOnlyList<(SwiftColor Color, double Location)> Gradient(ProviderKind lead, ProviderKind heads)
    {
        var leadBrand = EffortBrand.Create(lead);
        IReadOnlyList<SwiftColor> headColors = heads == lead
            ? [leadBrand.IsLight
                ? leadBrand.TitleColor.Opacity(0.55)
                : leadBrand.TitleColor.Mix(SwiftColor.White, 0.4)]
            : BrandColors(heads);

        return Spread(BrandColors(lead), 0, 0.42)
            .Concat(Spread(headColors, 0.58, 1))
            .ToArray();
    }

    // HydraFusedPairIcon.swift:76-79
    private static IReadOnlyList<SwiftColor> BrandColors(ProviderKind provider)
    {
        var brand = EffortBrand.Create(provider);
        return brand == EffortBrand.Antigravity ? EffortBrand.Gemini : [brand.TitleColor];
    }

    // HydraFusedPairIcon.swift:82-90
    private static IReadOnlyList<(SwiftColor Color, double Location)> Spread(
        IReadOnlyList<SwiftColor> colors,
        double from,
        double to)
    {
        if (colors.Count <= 1)
        {
            var color = colors.Count == 0 ? SwiftColor.Clear : colors[0];
            return [(color, from), (color, to)];
        }

        var result = new (SwiftColor Color, double Location)[colors.Count];
        for (var index = 0; index < colors.Count; index++)
        {
            result[index] = (
                colors[index],
                from + (to - from) * index / (colors.Count - 1));
        }

        return result;
    }

    // HydraFusedPairIcon.swift:47-60
    private Border Half(ProviderKind provider, AlignmentX alignment)
    {
        var icon = SwiftImage.Named(provider.IconName)
            .Resizable()
            .AspectRatio(ContentMode.Fit);
        icon.Width = size;
        icon.Height = size;

        var fill = new Rectangle
        {
            Width = size,
            Height = size,
            Fill = gradient,
            OpacityMask = new VisualBrush
            {
                Visual = icon,
                Stretch = Stretch.Fill,
            },
        };

        var left = alignment == AlignmentX.Left;
        var clip = left
            ? new Rect(0, 0, halfWidth, size)
            : new Rect(size - halfWidth, 0, halfWidth, size);

        return new Border
        {
            Width = size,
            Height = size,
            Clip = new RectangleGeometry(clip),
            Child = fill,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new TranslateTransform(left ? -size * 0.35 : size * 0.35, 0),
            Opacity = 0,
        };
    }

    // HydraFusedPairIcon.swift:66-74
    private static LinearGradientBrush CreateGradientBrush(ProviderKind lead, ProviderKind heads, ThemeVariant? theme)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };

        foreach (var stop in Gradient(lead, heads))
            brush.GradientStops.Add(new GradientStop(stop.Color.Resolve(theme ?? ThemeVariant.Default), stop.Location));

        return brush;
    }

    // HydraFusedPairIcon.swift:66-74
    private void UpdateGradient(ProviderKind lead, ProviderKind heads)
    {
        gradient.GradientStops.Clear();
        foreach (var stop in Gradient(lead, heads))
            gradient.GradientStops.Add(new GradientStop(stop.Color.Resolve(ActualThemeVariant), stop.Location));
    }

    // HydraFusedPairIcon.swift:23-34
    private void SetFused(bool fused)
    {
        if (isFused == fused) return;

        isFused = fused;
        leadHalf.Opacity = fused ? 1 : 0;
        headsHalf.Opacity = fused ? 1 : 0;
        leadHalf.RenderTransform = new TranslateTransform(fused ? 0 : -size * 0.35, 0);
        headsHalf.RenderTransform = new TranslateTransform(fused ? 0 : size * 0.35, 0);
        divider.RenderTransform = new ScaleTransform(1, fused ? 1 : 0.15);
    }
}
