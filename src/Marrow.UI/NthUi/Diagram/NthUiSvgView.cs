// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "svg (line art, sanitised)" and the language doc's "its SVG markup, as an indented body ...
// they stream in line by line"; 7.4 motion; 10; ED-NEW-web-w16c-1). No Swift counterpart. The `svg` component of a live answer: the
// markup a model wrote is sanitised and drawn natively, never shown to a web view. While the body streams the picture is read at
// most every 80 ms with its unfinished elements closed, so the line art draws as it arrives; the box it will fill is known from
// the opening tag, so nothing moves. A picture that cannot be read at the end shows a quiet note with the line's description.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.NthUi;

namespace Marrow.UI.NthUi.Diagram;

/// <summary>`svg`: sanitised line art drawn natively.</summary>
public sealed class NthUiSvgView : NthUiNodeView
{
    /// <summary>The longest a streaming picture waits before it is read again.</summary>
    public const double ReadSeconds = .08;

    private readonly Panel host = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly NthSvgCanvas canvas = new() { Adapt = true, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border note;
    private readonly TextBlock noteText;
    private readonly Reader reader;
    private string lastBody = "";
    private bool dirty;
    private double lastRead = double.NegativeInfinity;
    private bool drawn;
    private bool closed;
    private NthSvgFade? fade;

    public NthUiSvgView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        reader = new Reader(this);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (NthUiText.Icon("pen-tool", 16, NthUiTokens.Text3) is { } glyph) row.Children.Add(glyph);
        noteText = NthUiText.Block(Alt(), NthUiTokens.Small, NthUiTokens.Text2);
        row.Children.Add(noteText);
        note = new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(10), Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        note.Background = NthUiTokens.ControlFill.Brush(note);
        canvas.RequestedWidth = Runtime.Value(node, "width") is { IsNumber: true } w ? Math.Clamp(w.AsNumber, 1, 4096) : null;
        canvas.Opacity = 0;
        canvas.IsVisible = false;
        host.Children.Add(note);
        host.Children.Add(canvas);
        Describe();
        if (!string.IsNullOrEmpty(node.Body)) ReadNow(final: false);
    }

    public override Control Control => host;

    /// <summary>The picture drawn now, or null before the first readable piece.</summary>
    public NthSvgDocument? Document => canvas.Document;

    /// <summary>The control that draws the picture.</summary>
    public NthSvgCanvas Canvas => canvas;

    /// <summary>Whether the picture could not be read at the end (the note shows instead).</summary>
    public bool Failed { get; private set; }

    private string Alt() => Node.Arg("alt") is null ? NthUiStrings.LineArt : Runtime.Display(Node, "alt") is { Length: > 0 } alt ? alt : NthUiStrings.LineArt;

    private void Describe()
    {
        var name = Node.Arg("alt") is not null && Runtime.Display(Node, "alt") is { Length: > 0 } alt ? alt : canvas.Document?.Title ?? NthUiStrings.LineArt;
        AutomationProperties.SetName(canvas, name);
        AutomationProperties.SetName(note, name);
        if (canvas.Document?.Description is { } description) AutomationProperties.SetHelpText(canvas, description);
    }

    public override void BodyChanged()
    {
        dirty = true;
        if (!Context.Motion.CanAnimate)
        {
            ReadNow(final: false);
            return;
        }
        Context.Motion.Start(reader);
    }

    public override void Refresh()
    {
        noteText.Text = Alt();
        Describe();
    }

    public override void Close()
    {
        closed = true;
        ReadNow(final: true);
    }

    private void ReadNow(bool final)
    {
        var body = Node.Body ?? "";
        dirty = false;
        lastRead = NthUiMotion.Now;
        if (!final && body == lastBody) return;
        lastBody = body;
        NthSvgParseResult result;
        if (final)
        {
            result = NthSvgSanitizer.Parse(body);
            // A model that forgot the closing tag still gets its picture.
            if (!result.Ok) result = NthSvgSanitizer.ParsePartial(body);
        }
        else result = NthSvgSanitizer.ParsePartial(body);
        if (result.Document is not { } document)
        {
            if (final && !drawn)
            {
                Failed = true;
                noteText.Text = NthUiDiagramStrings.LineArtFailed;
            }
            return;
        }
        Failed = false;
        canvas.Document = document;
        Describe();
        if (drawn) return;
        drawn = true;
        Slot?.AnimateNextChange();
        note.IsVisible = false;
        canvas.IsVisible = true;
        if (Context.Motion.CanAnimate)
        {
            fade = new NthSvgFade(canvas, NthUiMotion.Now, Context.IsCalm(canvas) ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds);
            Context.Motion.Start(fade);
        }
        else canvas.Opacity = 1;
    }

    private sealed class Reader(NthUiSvgView owner) : INthUiAnimated
    {
        public bool Step(double now)
        {
            if (!owner.dirty) return false;
            if (now - owner.lastRead < ReadSeconds) return true;
            owner.ReadNow(owner.closed);
            return owner.dirty;
        }
    }
}
