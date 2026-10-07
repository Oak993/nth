using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.VisualTree;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Offline mounted editor and its real raster. Only time/frame scheduling and the native motion preference are fake.
internal sealed class DictationFragmentRig : IDisposable
{
    private readonly TestRuntime runtime = new();
    private readonly ManualFrameSource frames = new();
    private readonly IDisposable motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
    private readonly IDisposable frameScope;
    private readonly DictationReferencePaint reference = new();
    private DraftSelection original;
    public ComposerNSTextView Editor { get; }
    public DictationLayer Layer { get; }
    public AvaloniaWindow Window { get; }
    public ScrollViewer? Scroll { get; }
    public TextPresenter Presenter => Editor.GetVisualDescendants().OfType<TextPresenter>().Single();

    public DictationFragmentRig(double width = 80, string text = "", int selectionStart = 0, int selectionEnd = 0, bool still = false, bool scrolling = false)
    {
        frameScope = FrameClock.Source.Override(frames);
        Editor = new ComposerNSTextView { Width = width, Text = text, SelectionStart = selectionStart, SelectionEnd = selectionEnd, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        // The Linux font is explicit; native Windows font/scale parity remains a T9 run.
        if (!OperatingSystem.IsWindows()) Editor.FontFamily = new FontFamily("DejaVu Sans");
        if (scrolling) Scroll = new ScrollViewer { Height = 60, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Layer = new DictationLayer(Editor, Scroll) { Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        if (still) EnvironmentValues.SetAccessibilityReduceMotion(Layer, true);
        var content = new Grid { Children = { Editor, Layer, reference } };
        if (Scroll is not null) Scroll.Content = content;
        Window = new AvaloniaWindow { Width = width, Height = 520, Background = Brushes.White, Content = (Control?)Scroll ?? content };
        Window.Show(); Window.UpdateLayout(); Presenter.HideCaret();
    }

    public void Begin()
    {
        original = new DraftSelection(Editor.Text ?? "", Math.Min(Editor.SelectionStart, Editor.SelectionEnd), Math.Abs(Editor.SelectionEnd - Editor.SelectionStart));
        Layer.Begin(original);
    }
    public void Live(params WordOp[] ops) => Layer.Apply(new(TranscriptUpdateKind.Live, ops.Select(op => new ScheduledOp(TimeSpan.Zero, op)).ToArray(), []));
    public void Step(double seconds = 0)
    {
        runtime.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.FromSeconds(seconds)); Window.UpdateLayout();
    }

    public TextLayout Oracle(string text, FlowDirection direction = FlowDirection.LeftToRight, IBrush? foreground = null,
        IReadOnlyList<ValueSpan<TextRunProperties>>? spans = null) => new(text, new Typeface(Editor.FontFamily, Editor.FontStyle, Editor.FontWeight, Editor.FontStretch), Editor.FontSize,
        foreground ?? Brushes.Black, Editor.TextAlignment, TextWrapping.Wrap, null, null, direction, Layer.Bounds.Width, double.PositiveInfinity, double.NaN, 0, 0, null, spans);

    public void Resize(double width) { Editor.Width = Layer.Width = Window.Width = width; Window.UpdateLayout(); }

    public void Land()
    {
        var place = new DictationInsertion(original).Place(Layer.Words.Logical.Select(word => new TranscriptWord(word.Text, word.Tentative)).ToArray());
        var inserted = place.Text.Substring(original.Start, place.Text.Length - original.Text.Length + original.Length);
        var controller = new ComposerController { TextViewInstance = Editor };
        Editor.RunUnlocked(() => controller.ReplaceCharacters(original.Start, original.Length, inserted));
        Layer.Apply(new(TranscriptUpdateKind.Landing, TranscriptOps.PlanLanding(Layer.Words.Logical.Select(word => new TranscriptWord(word.Text, word.Tentative)).ToArray()), []));
    }

    public void AssertPicture(Action<DrawingContext> paint)
    {
        var actual = DictationProbe.Frame(Window);
        reference.Paint = paint; reference.IsVisible = true; Layer.Opacity = 0; reference.InvalidateVisual();
        var oracle = DictationProbe.Frame(Window);
        reference.IsVisible = false; Layer.Opacity = 1; reference.Paint = null;
        // Compare the entire surface (including glyph overhang), excluding only the independently tested live caret.
        if (Layer.CaretRect(SwiftClock.Now) is { } caret)
        {
            var origin = Layer.TranslatePoint(default, Window)!.Value;
            var skip = new PixelRect((int)Math.Floor(origin.X + caret.X - 1), (int)Math.Floor(origin.Y + caret.Y - 1), (int)Math.Ceiling(caret.Width + 2), (int)Math.Ceiling(caret.Height + 2));
            for (var y = Math.Max(0, skip.Y); y < Math.Min(actual.Size.Height, skip.Bottom); y++)
            for (var x = Math.Max(0, skip.X); x < Math.Min(actual.Size.Width, skip.Right); x++)
                Array.Copy(oracle.Bytes, y * oracle.Stride + x * 4, actual.Bytes, y * actual.Stride + x * 4, 4);
        }
        var area = new PixelRect(default(PixelPoint), actual.Size);
        Xunit.Assert.True(DictationProbe.Same(actual, oracle, area), $"full text picture difference {DictationProbe.Difference(actual, oracle, area):F6}");
    }

    public void AssertWordPixels(string text, int start, int length, double opacity, double rise, FlowDirection direction = FlowDirection.LeftToRight)
    {
        var ink = SwiftColor.LabelColor.Resolve(Editor.ActualThemeVariant);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(ink.A * opacity), ink.R, ink.G, ink.B));
        using var expected = Oracle(text, direction, Brushes.Transparent,
            [new(start, length, new GenericTextRunProperties(new Typeface(Editor.FontFamily, Editor.FontStyle, Editor.FontWeight, Editor.FontStretch), Editor.FontSize, null, brush))]);
        AssertPicture(context => expected.Draw(context, new Point(Editor.Padding.Left, Editor.Padding.Top + rise)));
        var actual = DictationProbe.Frame(Window);
        reference.Layout = expected; reference.Origin = new Point(Editor.Padding.Left, Editor.Padding.Top + rise);
        reference.IsVisible = true; Layer.Opacity = 0; reference.InvalidateVisual();
        var oracle = DictationProbe.Frame(Window);
        reference.IsVisible = false; Layer.Opacity = 1;
        // The vertically enlarged fragment regions can include the next line's caret. Mask that independently tested overlay here too.
        if (Layer.CaretRect(SwiftClock.Now) is { } caret)
        {
            var origin = Layer.TranslatePoint(default, Window)!.Value;
            var skip = new PixelRect((int)Math.Floor(origin.X + caret.X - 1), (int)Math.Floor(origin.Y + caret.Y - 1), (int)Math.Ceiling(caret.Width + 2), (int)Math.Ceiling(caret.Height + 2));
            for (var y = Math.Max(0, skip.Y); y < Math.Min(actual.Size.Height, skip.Bottom); y++)
            for (var x = Math.Max(0, skip.X); x < Math.Min(actual.Size.Width, skip.Right); x++)
                Array.Copy(oracle.Bytes, y * oracle.Stride + x * 4, actual.Bytes, y * actual.Stride + x * 4, 4);
        }
        foreach (var rect in expected.HitTestTextRange(start, length))
        {
            var area = new PixelRect((int)Math.Floor(rect.X + Editor.Padding.Left), (int)Math.Floor(rect.Y + Editor.Padding.Top - 6),
                (int)Math.Ceiling(rect.Right + Editor.Padding.Left) - (int)Math.Floor(rect.X + Editor.Padding.Left), (int)Math.Ceiling(rect.Height + 12));
            Xunit.Assert.True(DictationProbe.Same(actual, oracle, area), $"fragment {rect}: difference {DictationProbe.Difference(actual, oracle, area):F6}");
        }
        reference.Layout = null;
    }

    public void Dispose()
    {
        Layer.HandOverNow(); Window.Close(); frameScope.Dispose(); motion.Dispose(); runtime.Dispose();
    }
}

internal sealed class DictationReferencePaint : Control
{
    public TextLayout? Layout { get; set; }
    public Action<DrawingContext>? Paint { get; set; }
    public Point Origin { get; set; }
    public DictationReferencePaint() { IsVisible = false; IsHitTestVisible = false; }
    public override void Render(DrawingContext context) { Layout?.Draw(context, Origin); Paint?.Invoke(context); }
}
