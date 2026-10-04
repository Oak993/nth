using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// PR-COMPOSER-102 / PR-COMPOSER-103 / UI-COMPOSER-017 against the reference frame of the one-line composer working (2-chat-use.mov C 92.000, CATALOG scene 29;
// the frame is 1 px = 1 pt: the 46 point pill measures 46 rows). Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift:67-76, :124-149, :177-189;
// ComposerView.swift:661-675.
//
// MEASURED ON THE FRAME (python, E:\Droppy\work\scratch\frames\dotalpha.py and dotfit.py): the pill spans pixel rows 9 to 54 (46 high) and starts at column 45
// (the rim is column 44); the dots are 2 x 2 pixels, in three rows whose pixel rows are 25-26, 33-34 and 41-42 below the pill's top (centres 26, 34 and 42,
// that is 4, 12 and 20 up from the pill's bottom edge), the first column's left edge 3 points in from the pill's left edge, the columns 8 apart. The opacity
// of each dot along the bottom row follows 0.08 -> 0.75 -> 0.08 with 0.045 s a column (the whole wave is on the frame: 100 columns); the second row is the
// first one 2/3 as strong and 0.09 s later; the third one 1/3 as strong. Fitting the Swift keyframe curve to the bottom row gives the frame's phase (1.68 s into
// the cycle at column 0 of the bottom row) and a gain of 0.787 (a two pixel dot loses contrast to the video's 4:2:0 chroma); the values below are the frame's
// measured opacities divided by that gain, bottom row from column 1 and the second row from column 0 (columns 37 on are under the placeholder text).
// The port draws the same field at the same phase and its dots are compared with those values and with the pixel rows and columns above.
[Collection("Headless")]
public sealed class ComposerWorkingDotsFrameTests(HeadlessSession headless)
{
    // Measured opacity / 0.787 for the bottom row, columns 1 to 36.
    private static readonly double[] BottomRow =
    [
        0.455, 0.485, 0.528, 0.553, 0.579, 0.606, 0.623, 0.657, 0.679, 0.709, 0.709, 0.716, 0.752, 0.752, 0.742, 0.757, 0.760, 0.761,
        0.743, 0.754, 0.743, 0.741, 0.703, 0.689, 0.661, 0.639, 0.587, 0.531, 0.514, 0.446, 0.409, 0.355, 0.302, 0.255, 0.210, 0.187,
    ];

    // Measured opacity / 0.787 for the second row, columns 0 to 36 (already including its 2/3 fade).
    private static readonly double[] SecondRow =
    [
        0.335, 0.332, 0.359, 0.369, 0.384, 0.400, 0.429, 0.438, 0.451, 0.464, 0.465, 0.486, 0.461, 0.463, 0.485, 0.483, 0.479, 0.478, 0.484, 0.478,
        0.477, 0.463, 0.446, 0.427, 0.407, 0.377, 0.359, 0.309, 0.287, 0.249, 0.215, 0.188, 0.153, 0.117, 0.101, 0.074, 0.073,
    ];

    private static object Frame(DotFieldControl control) => control.GetType().GetProperty("LastRenderedFrame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(control)!;
    private static double Opacity(DotFieldControl control, int row, int column)
    {
        var frame = Frame(control);
        return Assert.IsType<double>(frame.GetType().GetMethod("OpacityAt")!.Invoke(frame, [row, column]));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // ComposerWorkingDots.swift:67-71,124-149; ComposerView.swift:661-675 against C 92.000
    [Trait("Req", "PR-COMPOSER-103")] // ComposerWorkingDots.swift:72-76,177-189 against C 92.000
    [Trait("Req", "UI-COMPOSER-017")] // ComposerWorkingDots.swift:12-190; ComposerView.swift:661-675 against C 92.000
    [Trait("Rule", "R-TEST-4")] // "no UI-thread ticking" (the render thread draws the field; the headless renderer draws on the UI thread) is proven on a real window by ComposerWorkingDotsNativeTests
    public void The_one_line_pills_dot_field_is_laid_and_pulses_as_the_frame_shows_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        // The slot is the pill's bottom 40 percent (ComposerView.swift:664-668): a 46 point pill, 820 wide, gives an 18.4 point field.
        var field = new DotFieldControl(new SwiftColor(1), true, false) { Height = 46 * 0.4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var window = new Avalonia.Controls.Window { Width = 820, Height = 46, Background = Brushes.Black, Content = new Grid { Children = { field } } };
        window.Show();
        try
        {
            // The shared clock: elapsed = now - 1 (:188), so the frame's 1.68 s into the cycle at the first column is the clock at 2.68 s.
            runtime.Advance(TimeSpan.FromSeconds(2.68));
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var capture = window.CaptureRenderedFrame()!;
            Assert.Equal(0, frames.Pending);

            // :124-132 three rows of ceil(820 / 8) + 1 = 104 columns, the dots 2 points across.
            var canvas = Assert.IsType<Canvas>(field.Layer);
            Assert.Equal(3 * 104, canvas.Children.Count);
            // The opacities: the bottom row and the second one against the frame's wave (columns 1-36 and 0-36), within 0.06 (the frame's own noise is 0.02).
            for (var k = 0; k < BottomRow.Length; k++) Assert.InRange(Opacity(field, 0, k + 1), BottomRow[k] - 0.06, BottomRow[k] + 0.06);
            for (var k = 0; k < SecondRow.Length; k++) Assert.InRange(Opacity(field, 1, k), SecondRow[k] - 0.06, SecondRow[k] + 0.06);
            // The peak of the wave on the frame is the keyframe's 0.75, the foot 0.08 (the bottom row's strongest measured value 0.761).
            Assert.InRange(Enumerable.Range(0, 104).Max(k => Opacity(field, 0, k)), 0.74, 0.751);
            // The pixels: each dot is a 2 x 2 blob at the frame's place, the bottom row's centre 4 up from the pill's bottom edge, the next ones 12 and 20 up,
            // the first column's centre 4 from the left edge and the columns 8 apart; between two dots, and a row above the third, the pill is as black as it was.
            using var stream = new MemoryStream();
            capture.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            using var pixels = SKBitmap.Decode(stream.ToArray());
            int Level(int x, int y) => pixels.GetPixel(x, y).Red;
            foreach (var (row, centreY) in new[] { (0, 42), (1, 34), (2, 26) })
                foreach (var k in new[] { 3, 8, 14, 20 })
                {
                    var centreX = 4 + 8 * k;
                    var strength = Opacity(field, row, k);
                    // the four pixels of the dot at (centre - 1 .. centre) hold the colour at the dot's opacity (an ellipse of diameter 2 covers them about 3/4)
                    var level = (Level(centreX - 1, centreY - 1) + Level(centreX, centreY - 1) + Level(centreX - 1, centreY) + Level(centreX, centreY)) / 4.0 / 255;
                    Assert.True(level >= strength * 0.55 && level <= strength * 1.05, $"row {row} column {k}: the dot's level is {level:F3} for an opacity of {strength:F3}");
                    Assert.Equal(0, Level(centreX + 3, centreY - 1)); // between two columns
                    Assert.Equal(0, Level(centreX - 1, centreY - 5)); // between two rows
                }
            Assert.Equal(0, Level(40, 16)); // a fourth row would sit at 18: there is none
            Assert.Equal(0, Level(40, 17));
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
