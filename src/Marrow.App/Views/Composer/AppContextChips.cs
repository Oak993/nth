// PORT-ONLY: web surfaces W19, what the chat's MCP Apps asked the model to know (DESIGN.md 7.6 "ui/update-model-context is
// visible, not silent. The current context from each app shows as a chip above the composer ("shipyard context · 1.2k", with
// an untrusted mark); clicking it shows the exact text and offers Remove. Capped at 4,000 characters per app (longer is
// truncated, and the chip says so)"; ED-NEW-web-w19-2). No Swift counterpart. One chip per app, in the composer's chip capsule,
// left-aligned above the composer; each opens a popover with the exact text the model will read, the cut note when it was cut,
// and Remove. The row is gone while no app has said anything.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

public sealed class AppContextChips : Decorator
{
    public const string RemoveText = "Remove";

    private readonly ThreadRuntime runtime;
    private readonly WrapPanel row = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
    private readonly Dictionary<string, Control> chips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McpAppContextEntry> drawn = new(StringComparer.Ordinal);

    private sealed class ChipState : MainActorModel
    {
        private bool open;

        public bool IsOpen { get => Read(open); set => Set(ref open, value); }
    }

    public AppContextChips(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        Child = row;
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(4, 0, 4, 0);
        Show();
    }

    /// <summary>The chips' words, in order (tests).</summary>
    public IReadOnlyList<string> ChipTexts => [.. drawn.Values.Select(entry => entry.ChipText)];

    /// <summary>The chip of a server (tests).</summary>
    internal Control? ChipOf(string server) => chips.GetValueOrDefault(server);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        runtime.AppContexts.Changed += OnChanged;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        runtime.AppContexts.Changed -= OnChanged;
    }

    private void OnChanged() => MainActor.Post(Show);

    private void Show()
    {
        var entries = runtime.AppContexts.Entries;
        var keep = entries.Select(entry => entry.Server).ToHashSet(StringComparer.Ordinal);
        foreach (var server in chips.Keys.Where(server => !keep.Contains(server)).ToArray())
        {
            row.Children.Remove(chips[server]);
            chips.Remove(server);
            drawn.Remove(server);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!drawn.TryGetValue(entry.Server, out var before) || before != entry)
            {
                if (chips.Remove(entry.Server, out var old)) row.Children.Remove(old);
                chips[entry.Server] = Chip(entry);
                drawn[entry.Server] = entry;
            }

            var chip = chips[entry.Server];
            var at = row.Children.IndexOf(chip);
            if (at < 0) row.Children.Insert(Math.Min(index, row.Children.Count), chip);
            else if (at != index) row.Children.Move(at, index);
        }

        IsVisible = entries.Count > 0;
    }

    private Control Chip(McpAppContextEntry entry)
    {
        var shield = new PaneGlyph(PaneGlyphKind.Shield, 12) { VerticalAlignment = VerticalAlignment.Center };
        shield.Foreground = Chrome.SecondaryText.Brush(shield);
        var text = new TextBlock { Text = entry.ChipText, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        text.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(text);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { shield, text, UntrustedMark() } };
        if (entry.Truncated)
        {
            var cut = new TextBlock { Text = "cut", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            cut.Foreground = Chrome.SecondaryText.Brush(cut);
            content.Children.Add(cut);
        }

        var state = new ChipState();
        var button = PlainControls.Button(new ChipChrome().Body(content), () => state.IsOpen = !state.IsOpen);
        var name = entry.ChipText + ", " + McpAppModelContext.UntrustedMark + (entry.TruncatedNote is { } note ? ", " + note : "");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, entry.TruncatedNote is { } tip ? entry.ChipText + " · " + tip : entry.ChipText);
        return button.BadgePopover(new Binding<bool>(() => state.IsOpen, value => state.IsOpen = value), () => Detail(entry, state));
    }

    private Control Detail(McpAppContextEntry entry, ChipState state)
    {
        var heading = new TextBlock { Text = entry.Server + " context", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        heading.Foreground = Chrome.PrimaryText.Brush(heading);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { heading, UntrustedMark() } };
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Width = 360, Margin = new Thickness(14), Children = { top } };
        if (entry.TruncatedNote is { } note)
        {
            var cut = new TextBlock { Text = note, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            cut.Foreground = Chrome.SecondaryText.Brush(cut);
            column.Children.Add(cut);
        }

        var exact = new SelectableTextBlock { Text = entry.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        exact.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(exact);
        column.Children.Add(new ScrollViewer { Content = exact, MaxHeight = 240 });
        var remove = TimelineRows.GlassCommand(RemoveText, () =>
        {
            state.IsOpen = false;
            runtime.AppContexts.Remove(entry.Server);
        }, prominent: false, small: true);
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        column.Children.Add(remove);
        return column;
    }

    // The untrusted mark: a small outlined word, the same on the chip and in its popover.
    private static Border UntrustedMark()
    {
        var word = new TextBlock { Text = McpAppModelContext.UntrustedMark, FontSize = 10.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        word.Foreground = Chrome.SecondaryText.Brush(word);
        var mark = new Border
        {
            Padding = new Thickness(5, 0, 5, 1),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(0.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = word,
        };
        mark.BorderBrush = Chrome.SecondaryText.Opacity(0.6).Brush(mark);
        return mark;
    }
}
