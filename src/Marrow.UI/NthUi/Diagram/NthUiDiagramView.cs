// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Diagrams (Mermaid): ... rendered to SVG once per completed statement by the vendored
// Mermaid library inside a hidden, sandboxed view ..., then the SVG is sanitised ... and drawn natively. So the inline diagram is
// native vector, themes with the thread and never needs a live web view"; 7.4; 10 (a text alternative); ED-NEW-web-w16c-1). No Swift
// counterpart. The `diagram` component of a live answer. The Mermaid source streams in line by line; at most every 0.2 s, and never
// while a drawing is still being made, the newest source goes to the renderer (IDiagramEngine), whose SVG is sanitised and drawn by
// NthSvgCanvas in the thread's own palette. A source Mermaid cannot read yet (a statement still arriving) keeps the last good
// drawing; at the end of the stream a source that cannot be drawn shows its text with a quiet note. Without a renderer (a build or
// a machine with no web runtime) the source shows at once, as before. A drawing is cached by source and theme, so a reopened answer
// appears at once; a theme change draws the diagram again in the other palette while the old one stays on screen.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.NthUi;
using Marrow.Core.Web;

namespace Marrow.UI.NthUi.Diagram;

/// <summary>`diagram`: a Mermaid diagram drawn natively.</summary>
public sealed class NthUiDiagramView : NthUiNodeView
{
    private readonly StackPanel root = new() { Spacing = 6 };
    private readonly TextBlock caption;
    private readonly ToggleButton toggle;
    private readonly TextBlock toggleText;
    private readonly Panel body = new();
    private readonly ScrollViewer scroller;
    private readonly NthSvgCanvas canvas = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer sourceScroller;
    private readonly TextBlock sourceText;
    private readonly TextBlock status;
    private readonly Pacer pacer;
    private readonly IDiagramEngine engine;
    private CancellationTokenSource? running;
    private string? pendingLine;
    private double nextAllowed;
    private bool dirty;
    private bool inFlight;
    private bool closed;
    private bool drawn;
    private bool failed;
    private bool userSource;
    private string failure = "";
    private DiagramRenderException? lastError;
    private NthSvgFade? fade;
    private int renders;

    public NthUiDiagramView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        engine = NthUiDiagrams.Engine;
        pacer = new Pacer(this);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        caption = NthUiText.Block(Caption(), NthUiTokens.Caption, NthUiTokens.Text2, wrap: false);
        caption.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(caption);

        toggle = NthUiPress.Plain(new ToggleButton());
        var face = new NthUiFace(toggle, NthUiFaceStyle.Segment, 13) { Height = 24, Padding = new Thickness(9, 0) };
        toggleText = NthUiText.Block(NthUiDiagramStrings.ShowSource, NthUiTokens.Small, null, wrap: false);
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        face.Child = toggleText;
        toggle.Content = face;
        toggle.IsCheckedChanged += (_, _) => toggle.Classes.Set("on", toggle.IsChecked == true);
        toggle.Click += (_, _) =>
        {
            userSource = toggle.IsChecked == true;
            Slot?.AnimateNextChange();
            ApplyMode();
        };
        AutomationProperties.SetName(toggle, NthUiDiagramStrings.ShowSource);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);

        var copy = NthUiPress.Button(NthUiStrings.Copy, NthUiFaceStyle.Ghost, "copy", () => Context.Raise(new NthUiEffect(NthUiEffectKind.Copy, Source())), 24);
        AutomationProperties.SetName(copy, NthUiDiagramStrings.CopySource);
        Grid.SetColumn(copy, 2);
        header.Children.Add(copy);
        root.Children.Add(header);

        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
            IsVisible = false,
        };
        scroller.SizeChanged += (_, _) => Fit();
        sourceText = NthUiText.Block(Source(), NthUiTokens.Mono, NthUiTokens.Text1, wrap: false);
        sourceText.TextTrimming = Avalonia.Media.TextTrimming.None;
        sourceScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = sourceText,
        };
        status = NthUiText.Block("", NthUiTokens.Small, NthUiTokens.Text2);
        status.IsVisible = false;
        body.Children.Add(scroller);
        body.Children.Add(sourceScroller);
        body.Children.Add(status);
        root.Children.Add(body);
        root.Margin = new Thickness(12, 6, 6, 10);
        AutomationProperties.SetControlTypeOverride(root, AutomationControlType.Group);
        AutomationProperties.SetControlTypeOverride(canvas, AutomationControlType.Image);
        Describe();
        root.AttachedToVisualTree += (_, _) => Attached();
        root.DetachedFromVisualTree += (_, _) => Detached();
        root.ActualThemeVariantChanged += (_, _) => ThemeChanged();
        ApplyMode();
    }

    public override Control Control => root;

    public override NthUiSurface? Surface { get; } = new(NthUiTokens.Well, NthUiTokens.Hairline, 10, false, 10);

    /// <summary>The control that draws the diagram.</summary>
    public NthSvgCanvas Canvas => canvas;

    /// <summary>The diagram drawn now, or null before the first drawing.</summary>
    public NthSvgDocument? Document => canvas.Document;

    /// <summary>The "Show source" toggle.</summary>
    public ToggleButton SourceToggle => toggle;

    /// <summary>The source text on show in place of the drawing.</summary>
    public TextBlock SourceText => sourceText;

    /// <summary>The quiet line under the header: what is happening or why nothing is drawn.</summary>
    public TextBlock Status => status;

    /// <summary>Whether the source shows in place of the drawing.</summary>
    public bool IsSourceShown => sourceScroller.IsVisible;

    /// <summary>Whether the end of the stream could not be drawn.</summary>
    public bool Failed => failed;

    /// <summary>The last reason a drawing did not come, or empty (the log and tests read it).</summary>
    public string LastErrorText => lastError?.Message ?? "";

    /// <summary>Drawings asked of the renderer so far (tests).</summary>
    public int Renders => renders;

    /// <summary>The drawing's scale and the room it has (tests).</summary>
    public ScrollViewer Scroller => scroller;

    private string Caption() =>
        string.Join(" · ", new[] { NthUiStrings.Diagram, Runtime.Text(Node) }.Where(s => s.Length > 0));

    private string Source() => Node.Body ?? Runtime.Text(Node);

    private void Describe()
    {
        var alt = Node.Arg("alt") is null ? "" : Runtime.Display(Node, "alt");
        var name = alt.Length > 0 ? alt : Caption();
        AutomationProperties.SetName(root, name);
        AutomationProperties.SetName(canvas, name);
        if (alt.Length > 0) AutomationProperties.SetHelpText(root, Caption());
    }

    public override void Refresh()
    {
        caption.Text = Caption();
        Describe();
    }

    public override void BodyChanged()
    {
        sourceText.Text = Joined();
        dirty = true;
        Poke();
    }

    public override void ShowPending(string? text)
    {
        pendingLine = text;
        sourceText.Text = Joined();
    }

    public override void Close()
    {
        closed = true;
        dirty = true;
        nextAllowed = 0;
        Poke();
    }

    private string Joined()
    {
        var source = Source();
        return string.IsNullOrEmpty(pendingLine) ? source : source.Length == 0 ? pendingLine : source + "\n" + pendingLine;
    }

    // ---- asking for a drawing ------------------------------------------------------------------------------------------

    private void Attached()
    {
        engine.AvailabilityChanged += OnAvailability;
        if (dirty) Poke();
    }

    private void Detached()
    {
        engine.AvailabilityChanged -= OnAvailability;
    }

    private void OnAvailability()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (engine.IsAvailable)
            {
                if (!drawn && Source().Length > 0) dirty = true;
                failed = false;
                ApplyMode();
                if (dirty) Poke();
            }
            else ApplyMode();
        });
    }

    private void ThemeChanged()
    {
        if (!drawn || string.IsNullOrWhiteSpace(Source())) return;
        dirty = true;
        Poke();
    }

    private void Poke()
    {
        if (!engine.IsAvailable)
        {
            ApplyMode();
            return;
        }
        if (string.IsNullOrWhiteSpace(Source())) return;
        ApplyMode();
        if (!Context.Motion.CanAnimate)
        {
            if (!inFlight) Begin(NthUiMotion.Now);
            return;
        }
        Context.Motion.Start(pacer);
    }

    private bool Step(double now)
    {
        if (!dirty || inFlight) return false; // a drawing that ends asks again (RunAsync) when the source changed meanwhile
        if (!closed && now < nextAllowed) return true;
        Begin(now);
        return dirty;
    }

    private void Begin(double now)
    {
        dirty = false;
        nextAllowed = now + NthUiDiagrams.RedrawSeconds;
        var source = Source();
        if (string.IsNullOrWhiteSpace(source)) return;
        var theme = NthUiDiagramTheme.For(root.ActualThemeVariant);
        var key = DiagramCache.Key(source, theme);
        if (NthUiDiagrams.Cache.Get(key) is { } cached)
        {
            Show(cached);
            return;
        }
        inFlight = true;
        renders++;
        _ = RunAsync(source, theme, key, closed);
    }

    private async Task RunAsync(string source, DiagramTheme theme, string key, bool final)
    {
        running?.Dispose();
        var cts = running = new CancellationTokenSource();
        try
        {
            var result = await engine.RenderAsync(new DiagramRenderRequest(source, theme), cts.Token);
            var parsed = await Task.Run(() => NthSvgSanitizer.Parse(result.Svg, NthSvgLimits.Renderer), CancellationToken.None);
            if (parsed.Document is not { } document)
            {
                lastError = new DiagramRenderException(DiagramFailure.Syntax, parsed.Error ?? "The drawing could not be read.");
                if (final && !dirty) Fail(lastError);
            }
            else
            {
                NthUiDiagrams.Cache.Add(key, document);
                lastError = null;
                Show(document);
            }
        }
        catch (OperationCanceledException)
        {
            // The view went away or was replaced.
        }
        catch (DiagramRenderException error)
        {
            lastError = error;
            // A source still arriving may not read yet: the last drawing stays. At the end there is no later source to wait for.
            if (final && !dirty) Fail(error);
        }
        catch (Exception error)
        {
            // Nothing awaits this task, so nothing may leave it: whatever went wrong is a drawing that did not come.
            lastError = new DiagramRenderException(DiagramFailure.Stopped, error.Message);
            if (final && !dirty) Fail(lastError);
        }
        finally
        {
            inFlight = false;
            if (dirty) Poke();
        }
    }

    // ---- showing ------------------------------------------------------------------------------------------------------

    private void Show(NthSvgDocument document)
    {
        failed = false;
        if (ReferenceEquals(canvas.Document, document))
        {
            ApplyMode();
            return;
        }
        var first = !drawn;
        canvas.Document = document;
        Fit();
        drawn = true;
        if (first)
        {
            Slot?.AnimateNextChange();
            if (Context.Motion.CanAnimate)
            {
                fade = new NthSvgFade(canvas, NthUiMotion.Now, Context.IsCalm(canvas) ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds);
                Context.Motion.Start(fade);
            }
            else canvas.Opacity = 1;
        }
        else if (Context.Motion.CanAnimate)
        {
            // A redraw while streaming (or in the other theme) comes in over the old one in 0.15 s.
            fade = new NthSvgFade(canvas, NthUiMotion.Now, NthUiTokens.CalmFadeSeconds, from: .6);
            Context.Motion.Start(fade);
        }
        ApplyMode();
    }

    private void Fail(DiagramRenderException error)
    {
        failed = true;
        failure = NthUiDiagramStrings.FailedAt(NthUiDiagramStrings.DiagramFailed, error.Line);
        Slot?.AnimateNextChange();
        ApplyMode();
    }

    /// <summary>Whether the source shows in place of the drawing: the reader asked for it, the drawing failed, or there is no
    /// renderer and so never a drawing.</summary>
    private bool SourceMode => userSource || failed || (!drawn && !engine.IsAvailable);

    private void ApplyMode()
    {
        var source = SourceMode;
        sourceScroller.IsVisible = source;
        scroller.IsVisible = !source && drawn;
        if (failed)
        {
            status.Text = failure;
            status.IsVisible = true;
        }
        else if (!source && !drawn && !string.IsNullOrWhiteSpace(Source()))
        {
            status.Text = NthUiDiagramStrings.Drawing;
            status.IsVisible = true;
        }
        else status.IsVisible = false;
        toggle.IsChecked = source;
        toggleText.Text = source ? NthUiDiagramStrings.ShowDiagram : NthUiDiagramStrings.ShowSource;
        AutomationProperties.SetName(toggle, toggleText.Text);
        toggle.IsEnabled = engine.IsAvailable && !failed;
    }

    /// <summary>Gives the drawing the room it has: its own size, or the scroller's width down to the smallest scale, beyond which it
    /// scrolls sideways.</summary>
    private void Fit()
    {
        if (canvas.Document is not { } document) return;
        var natural = document.ViewBox.Width;
        var room = scroller.Bounds.Width;
        canvas.RequestedWidth = room <= 0 ? natural : Math.Clamp(room, natural * NthUiDiagrams.MinScale, natural);
    }

    private sealed class Pacer(NthUiDiagramView owner) : INthUiAnimated
    {
        public bool Step(double now) => owner.Step(now);
    }
}
