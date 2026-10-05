// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-7 A, OD-0106; first-release voice typing, VT-10, voice-SPEC V-14.6): a note of the composer, drawn.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>Where a note shows: over the placeholder in the field, or, with text in the draft, in the composer's notice line (never hide the user's text).</summary>
internal enum VoiceNotePlace { Field, Notice }

/// <summary>
/// A note: <see cref="VoiceNoteContent.Text"/>, then its one link when it has one (underlined; it opens <see cref="VoiceNoteContent.LinkTarget"/>), led by the warning mark
/// (15 px circle and mark in the warning colour) for a warning. In the field it is 14 pt in the placeholder's quiet ink and the link in the text colour with a faint
/// underline (DESIGN `.note`, `.lnk`); in the notice line it reads as the attachment notice does (the warning colour, subheadline).
/// </summary>
internal sealed class VoiceNoteLine : Decorator
{
    private readonly TextBlock text;
    private readonly TranslateTransform rise = new(0, 0);

    public VoiceNoteLine(VoiceNoteContent content, VoiceNotePlace place)
    {
        Content = content;
        Place = place;
        var field = place == VoiceNotePlace.Field;
        // the text and its link are one sentence, so the run has no gap; the mark stands 10 px before it
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Top };
        if (content.Warning && field) row.Children.Add(Mark());
        text = new TextBlock
        {
            Text = content.Text,
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (field) SwiftFont.System(14).Apply(text);
        else SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(text);
        text.Track(() => field ? Chrome.SecondaryText : Chrome.Warning, color => text.Foreground = color.Brush(text));
        row.Children.Add(text);
        if (content.LinkText is { } linkText)
        {
            var label = new TextBlock { Text = linkText, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            if (field) SwiftFont.System(14).Apply(label);
            else SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(label);
            label.TextDecorations = TextDecorations.Underline;
            label.Track(() => field ? Chrome.PrimaryText : Chrome.Warning, color => label.Foreground = color.Brush(label));
            Link = PlainControls.Button(label, Open);
            Link.Focusable = true;
            Link.PointerStyle(PointerStyle.Link);
            AutomationProperties.SetName(Link, linkText);
            row.Children.Add(Link);
        }
        RenderTransform = rise;
        Child = row;
        IsHitTestVisible = true;
    }

    public VoiceNoteContent Content { get; }

    public VoiceNotePlace Place { get; }

    /// <summary>The note's one link, null for a note without.</summary>
    public Button? Link { get; }

    /// <summary>The words before the link.</summary>
    public string Text => text.Text ?? "";

    /// <summary>Moves the line down by <paramref name="px"/> from its place (the field's fade rises it 3 px).</summary>
    public void SetRise(double px) => rise.Y = px;

    // V-14.6: the link opens the Windows page that fixes it, through the shell seam (R-TEST-10: a test records it).
    private void Open()
    {
        if (Content.LinkTarget is { } target && SwiftURL.Create(target) is { } url) Workspace.Open.Current(url);
    }

    // 15 px circle and mark in the warning colour (DESIGN `.note svg`, viewBox 16: circle r 6.2, a stroke 1.4 wide).
    private static Control Mark()
    {
        var circle = new Path { Data = new EllipseGeometry(new Rect(1.8, 1.8, 12.4, 12.4)), StrokeThickness = 1.4 };
        var bang = new Path { Data = Geometry.Parse("M8 4.8L8 8.4M8 10.9L8 11"), StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round };
        var grid = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Children = { circle, bang } };
        var mark = new Viewbox { Width = 15, Height = 15, Child = grid, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        mark.Track(() => Chrome.Warning, color =>
        {
            circle.Stroke = color.Brush(mark);
            bang.Stroke = color.Brush(mark);
        });
        return mark;
    }
}
