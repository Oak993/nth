using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsagePanel.swift (B3 52bc792), :1-120.

/// <summary>:8-120 <c>struct UsagePanel: View</c>: the context window, the plan's limits or the credits left.</summary>
public sealed class UsagePanel : Decorator
{
    // :19-24 init(usage:provider:headsProvider:fillsWidth:); :26-79 body
    public UsagePanel(ContextUsage? usage, ProviderKind provider, ProviderKind? headsProvider = null, bool fillsWidth = false)
    {
        if (Marrow.App.Port.MultiAccount.Usage.MultiAccountUsageSeams.Try(provider) is { } accountUsage) { Child = accountUsage; return; }
        var heads = headsProvider is { } configuredHeads && configuredHeads != provider
            ? configuredHeads
            : (ProviderKind?)null;
        var showsLimits = PlanLimitsReader.ExposesLimits(provider);
        var showsCredits = CreditsReader.ExposesCredits(provider);
        var showsContext = usage?.Fraction != null && usage?.WindowTokens != null;

        var content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };

        if (usage is { } context && context.Fraction is { } fraction && context.WindowTokens is { } window)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 12 },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            var label = new TextBlock
            {
                Text = "Context window",
                Foreground = Chrome.SecondaryText.Brush(this),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            SwiftFont.Body.Apply(label);
            row.Children.Add(label);

            var value = new TextBlock
            {
                Text = $"{Tokens(context.UsedTokens)} / {Tokens(window)} ({(int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero)}%)",
                Foreground = Chrome.SecondaryText.Brush(this),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            SwiftFont.Body.MonospacedDigit().Apply(value);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            content.Children.Add(row);

            var bar = new UsageBar(fraction, Chrome.Accent, "Context window");
            content.Children.Add(new Border
            {
                Child = bar,
                Padding = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            });
        }

        if (heads is { } distinctHeads)
        {
            AddProviderSection(content, provider, "Lead", showsContext);

            AddProviderSection(content, distinctHeads, "Heads", showsContext || showsLimits || showsCredits);
        }
        else
        {
            if (showsLimits)
            {
                if (showsContext) content.Children.Add(Divider());
                content.Children.Add(new PlanLimitsView(provider));
            }

            if (showsCredits)
            {
                if (showsContext) content.Children.Add(Divider());
                content.Children.Add(new CreditsView(provider));
            }
        }

        Child = new Border
        {
            Child = content,
            Padding = new Thickness(16),
            Width = fillsWidth ? double.NaN : 360,
            MaxWidth = double.PositiveInfinity, // :70 .frame(maxWidth: fillsWidth ? .infinity : nil): SwiftUI nil is "unconstrained", Avalonia's own MaxWidth default
            HorizontalAlignment = fillsWidth
                ? Avalonia.Layout.HorizontalAlignment.Stretch
                : Avalonia.Layout.HorizontalAlignment.Left,
        };

        AttachedToVisualTree += (_, _) =>
        {
            var model = AppEnvironment.GetAppModel(this); // :9,27 inherited @Environment, not DataContext.

            var registry = model.Providers;
            registry.RefreshPlanLimits(provider);
            registry.RefreshCredits(provider);
            if (heads is { } refreshHeads)
            {
                registry.RefreshPlanLimits(refreshHeads);
                registry.RefreshCredits(refreshHeads);
            }
        };
    }

    // :83-108 providerSection(_:role:needsDivider:)
    private static void AddProviderSection(StackPanel content, ProviderKind provider, string role, bool needsDivider)
    {
        var showsLimits = PlanLimitsReader.ExposesLimits(provider);
        var showsCredits = CreditsReader.ExposesCredits(provider);
        if (!showsLimits && !showsCredits) return;

        if (needsDivider) content.Children.Add(Divider());

        var header = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        header.Children.Add(new ProviderIcon { Provider = provider, Size = 13 });

        var title = new TextBlock
        {
            Text = $"{role} \U000000B7 {provider.DisplayName}", // U+00B7 MIDDLE DOT
            Foreground = Chrome.PrimaryText.Brush(header),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        SwiftFont.Callout.FontWeight(SwiftFont.Weight.Semibold).Apply(title);
        header.Children.Add(title);
        content.Children.Add(new Border
        {
            Child = header,
            Padding = new Thickness(0, 0, 0, 8),
        });

        if (showsLimits) content.Children.Add(new PlanLimitsView(provider));
        if (showsLimits && showsCredits) content.Children.Add(Divider());
        if (showsCredits) content.Children.Add(new CreditsView(provider));
    }

    // :55, :62, :89, :102. Divider().padding(.vertical, 14): SwiftUI's own horizontal hairline across the content width
    // (UI-USAGE-035 "Full content width"), as UsageViews.swift:34's rule is drawn (UsageViews.cs PlainControls.Divider).
    // Not ChromeDivider (Chrome.swift:324-331), the 1 x 14 vertical bar between capsule items.
    private static Control Divider()
    {
        var rule = PlainControls.Divider();
        rule.Margin = new Thickness(0, 14, 0, 14);
        return rule;
    }

    // :110-119 static func tokens(_ count: Int) -> String
    public static string Tokens(long count)
    {
        static string Compact(double value, string suffix)
        {
            var text = SwiftNumber.Fixed(value, 1);
            if (text.EndsWith(".0", StringComparison.Ordinal))
                text = text[..^2];
            return text + suffix;
        }

        if (count >= 1_000_000_000) return Compact((double)count / 1_000_000_000, "B");
        if (count >= 1_000_000) return Compact((double)count / 1_000_000, "M");
        if (count >= 1_000) return Compact((double)count / 1_000, "k");
        return count.ToString(CultureInfo.InvariantCulture);
    }
}
