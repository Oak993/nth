// PORT-ONLY: web surfaces W16a (DESIGN.md 7.2 components text, heading, divider, badge, stat, callout, image, code and the
// actions drawn as buttons; 7.2 "Unknown or invalid lines degrade to their text"; "Images: data: URIs, artifact assets, or
// https images the user allows per domain"; 7.5 look; ED-NEW-web-w16a-1). No Swift counterpart. The leaves of a live view.
// A degraded line shows its own text in secondary colour. Charts, diagrams, svg and maps get honest stand-ins here (the
// chart's data as a table, the diagram's source, a list of the map's stops) that W16b and W16c replace through
// NthUiRenderers.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi;

/// <summary>`text`, and any line that degraded to its text.</summary>
public sealed class NthUiTextView : NthUiNodeView
{
    private readonly TextBlock block;

    public NthUiTextView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var font = node.Has("small") ? NthUiTokens.Small : node.Has("strong") ? NthUiTokens.BodyStrong : NthUiTokens.Body;
        var color = node.Degraded || node.Has("muted") ? NthUiTokens.Text2 : NthUiTokens.Text1;
        block = NthUiText.Block(Read(), font, color);
    }

    public override Control Control => block;

    private string Read() => Node.Degraded ? Node.Text ?? Node.Source : Runtime.Text(Node);

    public override void Refresh() => block.Text = Read();
}

/// <summary>A text, heading or callout line shown while it still arrives (DESIGN 7.4 "Text streams into text nodes").</summary>
internal sealed class NthUiPendingTextView
{
    private readonly string kind;
    private readonly TextBlock block;

    public NthUiPendingTextView(NthUiContext context, string kind)
    {
        this.kind = kind;
        block = NthUiText.Block("", kind == "heading" ? NthUiTokens.Heading(2) : NthUiTokens.Body, NthUiTokens.Text1);
        Control = kind == "callout" ? new Border { Padding = new Thickness(12, 10), Child = block } : block;
    }

    public Control Control { get; }

    public NthUiSurface? Surface => kind == "callout" ? NthUiCalloutView.SurfaceFor(null) : null;

    public void Show(string text, IReadOnlySet<string> flags)
    {
        block.Text = text;
        if (kind != "text") return;
        (flags.Contains("small") ? NthUiTokens.Small : flags.Contains("strong") ? NthUiTokens.BodyStrong : NthUiTokens.Body).Apply(block);
        if (flags.Contains("muted")) block.Foreground = NthUiTokens.Text2.Brush(block);
    }
}

/// <summary>`heading` (levels 1 to 3, 2 when not given) with an optional icon.</summary>
public sealed class NthUiHeadingView : NthUiNodeView
{
    private readonly TextBlock block;
    private readonly Control control;

    public NthUiHeadingView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var level = Runtime.Value(node, "level") is { IsNumber: true } l ? (int)Math.Clamp(Math.Round(l.AsNumber), 1, 3) : 2;
        block = NthUiText.Block(Runtime.Text(node), NthUiTokens.Heading(level), NthUiTokens.Text1);
        AutomationProperties.SetHeadingLevel(block, level);
        control = block;
        if (NthUiText.Icon(node.Arg("icon")?.Literal.AsText, level == 1 ? 18 : 16, NthUiTokens.Text2) is { } icon)
        {
            icon.VerticalAlignment = VerticalAlignment.Center;
            block.VerticalAlignment = VerticalAlignment.Center;
            control = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, block } };
        }
    }

    public override Control Control => control;

    public override void Refresh() => block.Text = Runtime.Text(Node);
}

/// <summary>`divider`: a hairline.</summary>
public sealed class NthUiDividerView(NthUiContext context, NthUiNode node) : NthUiNodeView(context, node)
{
    private readonly NthUiRule rule = new() { Margin = new Thickness(0, 2) };

    public override Control Control => rule;
}

/// <summary>`badge`: a small pill in a tone.</summary>
public sealed class NthUiBadgeView : NthUiNodeView
{
    private readonly Border pill;
    private readonly TextBlock text;

    public NthUiBadgeView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var tone = node.Arg("tone")?.Literal.AsText;
        text = NthUiText.Block(Runtime.Text(node), NthUiTokens.Caption, NthUiTokens.Tone(tone), wrap: false);
        text.VerticalAlignment = VerticalAlignment.Center;
        pill = new Border
        {
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = text,
        };
        pill.Background = NthUiTokens.ToneFill(tone).Brush(pill);
    }

    public override Control Control => pill;

    public override void Refresh() => text.Text = Runtime.Text(Node);
}

/// <summary>`stat`: a label, a large tabular value with its unit, and a change in a tone.</summary>
public sealed class NthUiStatView : NthUiNodeView
{
    private readonly StackPanel panel;
    private readonly TextBlock label;
    private readonly TextBlock value;
    private readonly TextBlock? unit;
    private readonly TextBlock? delta;

    public NthUiStatView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        label = NthUiText.Block(Runtime.Text(node), NthUiTokens.Small, NthUiTokens.Text2);
        Control head = label;
        if (NthUiText.Icon(node.Arg("icon")?.Literal.AsText, 14, NthUiTokens.Text2) is { } icon)
        {
            icon.VerticalAlignment = VerticalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, label } };
        }
        value = NthUiText.Block(Runtime.Display(node, "value"), NthUiTokens.BigNumber, NthUiTokens.Text1);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { value } };
        if (node.Arg("unit") is not null)
        {
            unit = NthUiText.Block(Runtime.Display(node, "unit"), NthUiTokens.Small, NthUiTokens.Text3);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(0, 0, 0, 2);
            line.Children.Add(unit);
        }
        panel = new StackPanel { Spacing = 2, Children = { head, line } };
        if (node.Arg("delta") is not null)
        {
            var shown = Runtime.Display(node, "delta");
            delta = NthUiText.Block(shown, NthUiTokens.Caption, NthUiTokens.Tone(DeltaTone(shown)));
            panel.Children.Add(delta);
        }
        AutomationProperties.SetName(panel, $"{label.Text}: {value.Text}{(unit is null ? "" : " " + unit.Text)}");
    }

    public override Control Control => panel;

    private string DeltaTone(string shown) => Node.Arg("tone")?.Literal.AsText ?? (shown.StartsWith('+') ? "success" : shown.StartsWith('-') || shown.StartsWith('−') ? "danger" : "neutral");

    public override void Refresh()
    {
        label.Text = Runtime.Text(Node);
        value.Text = Runtime.Display(Node, "value");
        if (unit is not null) unit.Text = Runtime.Display(Node, "unit");
        if (delta is not null)
        {
            delta.Text = Runtime.Display(Node, "delta");
            delta.Foreground = NthUiTokens.Tone(DeltaTone(delta.Text)).Brush(delta);
        }
        AutomationProperties.SetName(panel, $"{label.Text}: {value.Text}{(unit is null ? "" : " " + unit.Text)}");
    }
}

/// <summary>`callout`: a note in a tone with its icon and an optional title.</summary>
public sealed class NthUiCalloutView : NthUiNodeView
{
    private readonly Border border;
    private readonly TextBlock text;
    private readonly TextBlock? title;
    private readonly string? tone;

    public NthUiCalloutView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        tone = node.Arg("tone")?.Literal.AsText;
        text = NthUiText.Block(Runtime.Text(node), NthUiTokens.Body, NthUiTokens.Text1);
        var column = new StackPanel { Spacing = 2 };
        if (node.Arg("title") is not null)
        {
            title = NthUiText.Block(Runtime.Display(node, "title"), NthUiTokens.BodyStrong, NthUiTokens.Text1);
            column.Children.Add(title);
        }
        column.Children.Add(text);
        var iconName = node.Arg("icon")?.Literal.AsText ?? tone switch
        {
            "info" => "info",
            "success" => "circle-check",
            "warning" => "triangle-alert",
            "danger" => "octagon-alert",
            _ => null,
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (NthUiText.Icon(iconName, 16, NthUiTokens.Tone(tone)) is { } icon)
        {
            icon.Margin = new Thickness(0, 1, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
        }
        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        border = new Border { Padding = new Thickness(12, 10), Child = grid };
        AutomationProperties.SetLiveSetting(border, AutomationLiveSetting.Off);
    }

    public override Control Control => border;

    public override NthUiSurface? Surface => SurfaceFor(tone);

    internal static NthUiSurface SurfaceFor(string? tone) => new(NthUiTokens.ToneFill(tone), null, 12, false, 10);

    public override void Refresh()
    {
        text.Text = Runtime.Text(Node);
        if (title is not null) title.Text = Runtime.Display(Node, "title");
    }
}

/// <summary>`code` (and, until W16c, `diagram`): a well with the language or title, a copy button, and the body in a
/// monospaced face that grows line by line as it streams.</summary>
public sealed class NthUiCodeView : NthUiNodeView
{
    private readonly Border border;
    private readonly TextBlock body;
    private string? pendingLine;

    public NthUiCodeView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var header = new DockPanel { LastChildFill = true };
        var copy = NthUiPress.Button(NthUiStrings.Copy, NthUiFaceStyle.Ghost, "copy", () => Context.Raise(new NthUiEffect(NthUiEffectKind.Copy, Source())), 24);
        AutomationProperties.SetName(copy, NthUiStrings.CopyCode);
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        var caption = node.Kind == "diagram"
            ? string.Join(" · ", new[] { NthUiStrings.Diagram, Runtime.Text(node) }.Where(s => s.Length > 0))
            : node.Arg("title") is not null ? Runtime.Display(node, "title") : node.Arg("lang")?.Literal.AsText ?? NthUiStrings.Code;
        var label = NthUiText.Block(caption, NthUiTokens.Caption, NthUiTokens.Text2, wrap: false);
        label.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(label);
        body = NthUiText.Block(Source(), NthUiTokens.Mono, NthUiTokens.Text1, wrap: false);
        body.TextTrimming = TextTrimming.None;
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = body,
        };
        border = new Border { Padding = new Thickness(12, 6, 6, 10), Child = new StackPanel { Spacing = 4, Children = { header, scroller } } };
        AutomationProperties.SetName(border, caption);
    }

    public override Control Control => border;

    public override NthUiSurface? Surface { get; } = new(NthUiTokens.Well, NthUiTokens.Hairline, 10, false, 10);

    private string Source() => Node.Body ?? Runtime.Text(Node);

    public override void BodyChanged() => Show();

    public override void ShowPending(string? text)
    {
        pendingLine = text;
        Show();
    }

    public override void Refresh() => Show();

    private void Show()
    {
        var source = Source();
        body.Text = string.IsNullOrEmpty(pendingLine) ? source : source.Length == 0 ? pendingLine : source + "\n" + pendingLine;
    }
}

/// <summary>`image`: a box of fixed size from the start (its width and height, or 16:9), so nothing moves when the picture
/// arrives; data: images decode here, https and artifact images come through the host's loader.</summary>
public sealed class NthUiImageView : NthUiNodeView
{
    private const int MaxDataLength = 12_000_000;
    private readonly NthUiImageBox box;
    private readonly Image image;
    private CancellationTokenSource? loading;

    public NthUiImageView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var src = node.Arg("src")?.Literal.AsText ?? "";
        var alt = node.Arg("alt") is null ? "" : Runtime.Display(node, "alt");
        var width = Runtime.Value(node, "width") is { IsNumber: true } w ? w.AsNumber : (double?)null;
        var height = Runtime.Value(node, "height") is { IsNumber: true } h ? h.AsNumber : (double?)null;
        var cover = node.Arg("fit")?.Literal.AsText == "cover";
        image = new Image { Stretch = cover ? Stretch.UniformToFill : Stretch.Uniform, Opacity = 0 };
        var placeholder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (NthUiText.Icon("image", 16, NthUiTokens.Text3) is { } glyph) placeholder.Children.Add(glyph);
        var caption = alt.Length > 0 ? alt : Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? NthUiStrings.ImageFrom(uri.Host) : NthUiStrings.Image;
        placeholder.Children.Add(NthUiText.Block(caption, NthUiTokens.Small, NthUiTokens.Text3, wrap: false));
        box = new NthUiImageBox(width ?? (height is double tall ? tall * 16 / 9 : 480), width is not null && height is not null ? height.Value / width.Value : 9.0 / 16)
        {
            Child = new Panel { Children = { placeholder, image } },
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
        };
        box.Background = NthUiTokens.ControlFill.Brush(box);
        AutomationProperties.SetName(box, alt.Length > 0 ? alt : NthUiStrings.Image);
        AutomationProperties.SetControlTypeOverride(box, Avalonia.Automation.Peers.AutomationControlType.Image);
        Load(src);
    }

    public override Control Control => box;

    private void Load(string src)
    {
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            Show(Decode(src));
            return;
        }
        if (Context.View.ImageLoader is not { } loader) return;
        loading = new CancellationTokenSource();
        var token = loading.Token;
        _ = LoadAsync(loader, src, token);
    }

    private async Task LoadAsync(Func<string, CancellationToken, Task<IImage?>> loader, string src, CancellationToken token)
    {
        IImage? picture;
        try
        {
            picture = await loader(src, token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return;
        }
        if (token.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() => Show(picture));
    }

    private void Show(IImage? picture)
    {
        if (picture is null) return;
        image.Source = picture;
        if (Context.IsCalm(image)) image.Opacity = 1;
        else NthArrive.Settle(image);
    }

    /// <summary>A data:image URI's picture, or null when it is not a base64 png, jpeg, gif or webp the platform decodes.</summary>
    internal static Bitmap? Decode(string src)
    {
        if (src.Length > MaxDataLength) return null;
        var comma = src.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0 || !src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var bytes = Convert.FromBase64String(src[(comma + 1)..]);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}

/// <summary>A box of a fixed aspect ratio, as wide as allowed up to its own width.</summary>
public sealed class NthUiImageBox(double width, double ratio) : Border
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var w = Math.Max(1, Math.Min(width, double.IsFinite(availableSize.Width) ? availableSize.Width : width));
        var size = new Size(w, Math.Round(w * Math.Clamp(ratio, .05, 4)));
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = Math.Max(1, Math.Min(width, finalSize.Width));
        Child?.Arrange(new Rect(0, 0, w, Math.Round(w * Math.Clamp(ratio, .05, 4))));
        return finalSize;
    }
}

/// <summary>A stand-in with an icon and a description (svg until W16c draws it).</summary>
public sealed class NthUiPlaceholderView : NthUiNodeView
{
    private readonly Border border;

    public NthUiPlaceholderView(NthUiContext context, NthUiNode node, string what, string icon) : base(context, node)
    {
        var alt = node.Arg("alt") is null ? Runtime.Text(node) : Runtime.Display(node, "alt");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (NthUiText.Icon(icon, 16, NthUiTokens.Text3) is { } glyph) row.Children.Add(glyph);
        row.Children.Add(NthUiText.Block(alt.Length > 0 ? alt : what, NthUiTokens.Small, NthUiTokens.Text2));
        border = new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(10), Child = row };
        border.Background = NthUiTokens.ControlFill.Brush(border);
        AutomationProperties.SetName(border, alt.Length > 0 ? alt : what);
    }

    public override Control Control => border;
}

/// <summary>An action drawn as a button: copy, open, export, send, context, call, set, a toggle action, or a button that
/// runs the actions inside it in order.</summary>
public sealed class NthUiActionView : NthUiNodeView
{
    private readonly Button button;
    private readonly TextBlock label;
    private readonly string restingLabel;
    private int copies;

    public NthUiActionView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        restingLabel = Label();
        var style = node.Kind == "button" && node.Has("primary") ? NthUiFaceStyle.Primary : NthUiFaceStyle.Plain;
        button = NthUiPress.Plain(new Button());
        button.Content = NthUiPress.Face(button, style, restingLabel, Icon(), NthUiTokens.ButtonHeight, out label);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => Press();
        AutomationProperties.SetName(button, restingLabel);
    }

    public override Control Control => button;

    private string Label()
    {
        var text = Runtime.Text(Node);
        if (text.Length > 0) return text;
        return Node.Kind switch
        {
            "copy" => NthUiStrings.Copy,
            "open" => Node.Arg("url")?.Literal.AsText is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : NthUiStrings.Open,
            "export" => Node.Arg("format")?.Literal.AsText == "png" ? NthUiStrings.ExportPng : NthUiStrings.ExportCsv,
            "context" => NthUiStrings.Attach,
            "call" => NthUiStrings.Run(Node.Arg("tool")?.Literal.AsText ?? ""),
            "set" or "toggle" => NthUiStrings.Set(Node.Name ?? ""),
            _ => NthUiStrings.Send,
        };
    }

    private string? Icon() => Node.Kind switch
    {
        "copy" => "copy",
        "open" => "square-arrow-out-up-right",
        "export" => "download",
        "send" => "send",
        "context" => "paperclip",
        "call" => "play",
        _ => null,
    };

    private void Press()
    {
        Context.Run(Node);
        if (Node.Kind != "copy") return;
        var mine = ++copies;
        label.Text = NthUiStrings.Copied;
        MainActorDelay(1.4, () =>
        {
            if (mine == copies) label.Text = restingLabel;
        });
    }

    public override void Refresh()
    {
        var text = Label();
        if (label.Text != NthUiStrings.Copied) label.Text = text;
        AutomationProperties.SetName(button, text);
    }

    private void MainActorDelay(double seconds, Action then)
    {
        if (!Context.Motion.CanAnimate)
        {
            then();
            return;
        }
        Context.Motion.Start(new Delay(NthUiMotion.Now + seconds, then));
    }

    private sealed class Delay(double at, Action then) : INthUiAnimated
    {
        public bool Step(double now)
        {
            if (now < at) return true;
            then();
            return false;
        }
    }
}

/// <summary>Formats chart numbers the way the view shows numbers.</summary>
internal static class NthUiNumbers
{
    public static string Format(double? value, CultureInfo culture, string? unit) =>
        value is double v ? NthUiValue.FormatNumber(v, culture) + (string.IsNullOrEmpty(unit) ? "" : " " + unit) : "—";
}
