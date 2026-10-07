// PORT-ONLY: OD-0098 Welcome voice, wv_lib.py .pl download face: ring16, bold13, measured MB.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

internal sealed class VoicePreparationFace : Panel
{
    private readonly VoicePreparationRing ring = new();
    private readonly TextBlock title = new();
    private readonly TextBlock bytes = new();
    private string? model;
    private double measured;

    internal VoicePreparationFace()
    {
        IsHitTestVisible = false;
        UseLayoutRounding = false;
        Opacity = 0;
        SwiftFont.System(13, SwiftFont.Weight.Semibold).Apply(title);
        SwiftFont.System(13).Apply(bytes);
        Children.Add(ring);
        Children.Add(title);
        Children.Add(bytes);
        ActualThemeVariantChanged += (_, _) => Tint();
        Tint();
    }

    internal double OpenWidth => 120 + title.DesiredSize.Width + bytes.DesiredSize.Width;
    internal bool IsDownload { get; private set; }
    internal string Description => title.Text ?? "Getting voice ready";

    internal void Update(VoicePrepItem? item)
    {
        IsDownload = item?.Id == VoicePreparation.DownloadId;
        title.Text = item?.Id == VoicePreparation.SelfTestId ? "Testing speed…" : "Getting voice ready";
        if (item?.ModelId != model)
        {
            model = item?.ModelId;
            measured = 0;
        }
        if (item is { Id: VoicePreparation.DownloadId, Fraction: { } fraction }) measured = Math.Clamp(fraction, 0, 1);
        // These stages are emitted only after installation has completed. A coalesced UI delivery still knows the file is complete.
        if (item is { ModelId: not null, Id: VoicePreparation.VerifyId or VoicePreparation.SelfTestId }) measured = 1;
        var total = ModelCatalog.Find(model)?.Bytes;
        bytes.Text = total is { } value
            ? Math.Round(value * measured / 1_000_000.0).ToString(CultureInfo.InvariantCulture) + " of " + Math.Round(value / 1_000_000.0).ToString(CultureInfo.InvariantCulture) + " MB"
            : "";
        ring.Fraction = measured;
        Measure(Size.Infinity);
        InvalidateMeasure();
    }

    internal void Reset() { model = null; measured = 0; }

    private void Tint()
    {
        var dark = LookDraw.IsDark(ActualThemeVariant);
        var ink = dark ? Color.FromRgb(247, 247, 248) : Color.FromRgb(22, 22, 26);
        title.Foreground = new SolidColorBrush(ink);
        bytes.Foreground = new SolidColorBrush(LookDraw.WithAlpha(ink, dark ? .6 : .55));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);
        return new Size(OpenWidth, 40);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ring.Arrange(new Rect(40, 12, 16, 16));
        title.Arrange(new Rect(64, (40 - title.DesiredSize.Height) / 2, title.DesiredSize.Width, title.DesiredSize.Height));
        bytes.Arrange(new Rect(72 + title.DesiredSize.Width, (40 - bytes.DesiredSize.Height) / 2, bytes.DesiredSize.Width, bytes.DesiredSize.Height));
        return finalSize;
    }
}
