using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ThreadChangesTab.swift (B2 1fbf81e), :1-75

/// <summary>
/// The thread's changes as a small tab rising from the top of the chat box. The composer draws
/// over its lower edge, so it reads as part of the box. Clicking it opens the changes popover. (:4-5)
/// </summary>
public sealed class ThreadChangesTab : Button
{
    // :7 The overlap constant: the tab extends this far down into the composer
    public const double Overlap = 14;

    private readonly FileChangeSummary stats;
    private readonly AttachmentAnchorCapture? anchorCapture;

    public ThreadChangesTab(FileChangeSummary stats, Action<Control> anchor, Action action)
    {
        this.stats = stats;

        // :36 .buttonStyle(.plain)
        Padding = new Thickness(0);
        Background = null;
        BorderThickness = new Thickness(0);

        // :37 .focusable(false)
        Focusable = false;

        // :16 Set the click action
        Click += (_, _) => action();

        // Create the content: HStack with spacing 7 (:17-29)
        var contentPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7, // :17 spacing: 7
            Margin = new Thickness(12, 7, 12, 7 + Overlap), // :31-33 padding horizontal 12, top 7, bottom 21
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // :18 Add the plusminus icon
        var icon = SwiftImage.SystemName("plusminus"); // :18 Image(systemName: "plusminus")
        icon.Font(Chrome.InlineIconFont); // :19 .font(Chrome.inlineIconFont)
        icon.ForegroundStyle(Chrome.SecondaryText); // :20 .foregroundStyle(Chrome.secondaryText)
        contentPanel.Children.Add(icon);

        // :21 Add the file count text
        var fileCountText = stats.Files.Count == 1 ? "1 file" : $"{stats.Files.Count} files";
        var fileCountControl = CreateTextBlock(fileCountText, Chrome.PrimaryText.Opacity(0.9)); // :22 Chrome.primaryText.opacity(0.9)
        contentPanel.Children.Add(fileCountControl);

        // :25 Add the additions text: "+N"
        var additionsText = $"+{stats.Additions}";
        var additionsControl = CreateTextBlock(additionsText, Chrome.Success);
        contentPanel.Children.Add(additionsControl);

        // :27 Add the deletions text: "−N" (U+2212 mathematical minus)
        var deletionsText = $"−{stats.Deletions}"; // U+2212 is the mathematical minus sign
        var deletionsControl = CreateTextBlock(deletionsText, Chrome.Danger);
        contentPanel.Children.Add(deletionsControl);

        // :42 .fixedSize() - sized to content
        // Create the border with the rounded rectangle shape (:15)
        // UnevenRoundedRectangle(topLeadingRadius: 12, topTrailingRadius: 12, style: .continuous)
        var borderControl = new Border
        {
            Child = contentPanel,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Background = new SolidColorBrush(Colors.Transparent)
        };

        // :39-40 AttachmentAnchorCapture in the background for anchor capture
        anchorCapture = new AttachmentAnchorCapture(onResolve: anchor);

        // :38 Apply glass effect (.glassEffect(.regular.interactive(), in: shape))
        // MARROW_DEFERRED ED-180: The app-drawn material and interactive() press response
        // The glass styling is applied through the border structure

        // Build the final control
        var grid = new Grid();
        grid.Children.Add(borderControl);
        grid.Children.Add(anchorCapture);

        Content = grid;

        // :43 .help() - set tooltip with shortcut hint
        SetTooltip();

        // :44 .accessibilityLabel() - set accessibility label
        SetAccessibilityLabel();
    }

    private TextBlock CreateTextBlock(string text, SwiftColor color)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = color.Brush(this)
        };

        // :30 .font(.system(.callout, weight: .medium).monospacedDigit())
        var font = SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium)
            .MonospacedDigit();
        font.Apply(textBlock);

        return textBlock;
    }

    private void SetTooltip()
    {
        // :43 .help("Show this thread's changes\(ShortcutStore.hint(for: .toggleChanges))")
        var tooltipText = "Show this thread's changes";

        // Get the shortcut hint (this would come from ShortcutStore in the real app)
        var shortcutHint = GetShortcutHint();
        if (!string.IsNullOrEmpty(shortcutHint))
        {
            tooltipText += $" ({shortcutHint})";
        }

        ToolTip.SetTip(this, tooltipText);
    }

    private void SetAccessibilityLabel()
    {
        // :44 .accessibilityLabel(Text(verbatim: "\(stats.files.count) files changed, \(stats.additions) added, \(stats.deletions) removed"))
        var fileWord = stats.Files.Count == 1 ? "file" : "files";
        var label = $"{stats.Files.Count} {fileWord} changed, {stats.Additions} added, {stats.Deletions} removed";
        AutomationProperties.SetName(this, label);
    }

    private static string GetShortcutHint()
    {
        // This would normally come from ShortcutStore.hint(for: .toggleChanges)
        // For now, return a default hint that can be overridden by the caller
        // The actual shortcut would be "Ctrl+D" on Windows (OD-0008 B7)
        // TODO: Integrate with ShortcutStore once available
        return "Ctrl+D"; // Default chord
    }
}
