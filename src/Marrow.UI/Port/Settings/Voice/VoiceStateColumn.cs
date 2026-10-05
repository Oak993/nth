// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): a model row's 96 pt state column (DESIGN gen.py:451-459 .mst, .mso).
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// The four faces in one cell, stacked: `In use` (a check and the word, 12 medium), the progress (`{n}%` tabular, 11.5 secondary, and the 20 pt cross named `Cancel the download`), the
/// `Use` capsule and the `Download` capsule. The face shown is opaque at full size; the others are transparent at .94. A face going away fades over 0.15 s ease and shrinks to .94
/// over 0.25 s ez; the face coming in fades over 0.25 s ease after 0.08 s and grows over 0.35 s ez after 0.08 s (`.mso`, `.mso.on`). A face that is not shown takes no pointer and no focus.
/// </summary>
public sealed class VoiceStateColumn : Grid
{
    private const double Hidden = .94;

    private readonly Panel[] faces;
    private readonly CzNumber[] fades;
    private readonly CzNumber[] scales;
    private readonly TextBlock percent;
    private VoiceModelState state;

    /// <summary>The `Use` capsule.</summary>
    public CzCapsuleButton UseButton { get; }

    /// <summary>The `Download` capsule.</summary>
    public CzCapsuleButton DownloadButton { get; }

    /// <summary>The cross that cancels a download.</summary>
    public VoiceCancelButton CancelButton { get; }

    /// <summary>The check beside `In use`.</summary>
    public CzCheckMark InUseCheck { get; }

    public VoiceModelState State => state;

    /// <summary>The progress text now (`37%`).</summary>
    public string PercentText => percent.Text ?? "";

    public VoiceStateColumn(VoiceModelState initial = VoiceModelState.NotInstalled)
    {
        Width = 96;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        InUseCheck = new CzCheckMark { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        var inUseText = new CzCrossfadeText("In use") { FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        var inUse = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { InUseCheck, inUseText } };
        percent = new TextBlock
        {
            Text = "0%", FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 11.5, MinWidth = 30, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, FontFeatures = FontFeatureCollection.Parse("tnum"),
        };
        percent.Foreground = CzTheme.Text(this, true);
        ActualThemeVariantChanged += (_, _) => percent.Foreground = CzTheme.Text(this, true);
        CancelButton = new VoiceCancelButton();
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { percent, CancelButton } };
        UseButton = new CzCapsuleButton("Use");
        DownloadButton = new CzCapsuleButton("Download");
        Control[] contents = [inUse, progress, UseButton, DownloadButton];
        faces = new Panel[contents.Length];
        fades = new CzNumber[contents.Length];
        scales = new CzNumber[contents.Length];
        var motion = new CzMotion(this);
        for (var i = 0; i < contents.Length; i++)
        {
            var face = new Panel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative) };
            face.Children.Add(contents[i]);
            faces[i] = face;
            var shown = i == (int)initial;
            fades[i] = new CzNumber(motion, shown ? 1 : 0, Writer(face, true));
            scales[i] = new CzNumber(motion, shown ? 1 : Hidden, Writer(face, false));
            Apply(i, shown);
            Children.Add(face);
        }
        state = initial;
        InUseCheck.SetChecked(initial == VoiceModelState.InUse);
    }

    // The two scalars of a face write opacity and scale to it.
    private static Action<double> Writer(Panel face, bool opacity) =>
        opacity ? v => face.Opacity = v : v => face.RenderTransform = new ScaleTransform(v, v);

    private void Apply(int index, bool shown)
    {
        faces[index].Opacity = fades[index].Value;
        faces[index].RenderTransform = new ScaleTransform(scales[index].Value, scales[index].Value);
        faces[index].IsEnabled = shown;
        faces[index].IsHitTestVisible = shown;
    }

    /// <summary>The opacity of a face now.</summary>
    public double FaceOpacity(VoiceModelState face) => fades[(int)face].Value;

    /// <summary>The scale of a face now.</summary>
    public double FaceScale(VoiceModelState face) => scales[(int)face].Value;

    /// <summary>Shows a face, the others leaving; <paramref name="fraction"/> (0 to 1) is the progress text of the downloading face.</summary>
    public void SetState(VoiceModelState next, double fraction = 0)
    {
        percent.Text = Math.Floor(Math.Clamp(fraction, 0, 1) * 100).ToString(CultureInfo.InvariantCulture) + "%";
        AutomationProperties.SetHelpText(CancelButton, percent.Text);
        if (next == state) return;
        state = next;
        for (var i = 0; i < faces.Length; i++)
        {
            var shown = i == (int)next;
            faces[i].IsEnabled = shown;
            faces[i].IsHitTestVisible = shown;
            if (shown)
            {
                fades[i].To(1, CzMotion.Ease(.25), .08);
                scales[i].To(1, CzMotion.Ez(.35), .08);
            }
            else
            {
                fades[i].To(0, CzMotion.Ease(.15));
                scales[i].To(Hidden, CzMotion.Ez(.25));
            }
        }
        InUseCheck.SetChecked(next == VoiceModelState.InUse);
    }
}
