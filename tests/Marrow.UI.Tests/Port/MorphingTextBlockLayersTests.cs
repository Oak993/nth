using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (final review of unit/effort-title-fidelity, round 1, 2026-10-03, finding 6; mutant M12 survived): a whole
// kept letter is drawn without an opacity layer, so the text renderer draws it as the label draws it at rest (an opacity
// layer takes the text off ClearType). The headless renderer cannot tell a letter drawn under PushOpacity(1) from one drawn
// plain, so the layers are read from what the label pushes: its drawing recorded into a DrawingGroup, where each opacity
// push is a group whose Opacity is set. The ClearType look of the letters on a real window is still owed at the quiet-PC gate.
[Collection("Headless")]
public sealed class MorphingTextBlockLayersTests(HeadlessSession headless)
{
    // Each glyph run of the recording with the number of opacity layers it sits under.
    private static void Walk(Drawing drawing, int layers, List<int> runs)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var inner = layers + (group.IsSet(DrawingGroup.OpacityProperty) ? 1 : 0);
                foreach (var child in group.Children) Walk(child, inner, runs);
                break;
            case GlyphRunDrawing:
                runs.Add(layers);
                break;
        }
    }

    private static List<int> Layers(DrawnMorphingTextBlock label)
    {
        var recording = new DrawingGroup();
        using (var context = recording.Open()) label.DrawInto(context);
        var runs = new List<int>();
        Walk(recording, 0, runs);
        return runs;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_whole_kept_letter_is_drawn_with_no_opacity_layer_at_a_changes_start_and_end() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new DrawnMorphingTextBlock { Text = "High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White, MaxLines = 1 };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = label };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var rest = Layers(label); // at rest: the line's own glyph runs, plain
            Assert.True(rest.Count > 0 && rest.All(layers => layers == 0), $"at rest ({string.Join(",", rest)})");
            label.SetText("Extra High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            // The change's first frame: the kept "High" is whole; the letters of "Extra" have not appeared.
            Assert.Equal([0, 0, 0, 0], Layers(label));
            // Under way the new letters are drawn through a layer, so the recording does see layers: it is not blind to them.
            for (var ms = 0; ms < 100; ms += 20)
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                frames.Advance(TimeSpan.FromMilliseconds(20));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            var middle = Layers(label);
            Assert.True(middle.Count(layers => layers > 0) >= 5, $"the arriving letters are not drawn through a layer ({string.Join(",", middle)})");
            // Near its end the kept letters have reached their places and are whole: at least their four letters have no layer
            // (a letter that has arrived is whole too), and some of the last to arrive are still through one.
            for (var ms = 100; ms < 300; ms += 10)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
                frames.Advance(TimeSpan.FromMilliseconds(10));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            Assert.True(label.IsMorphing, "the change has ended");
            var late = Layers(label);
            Assert.True(late.Count(layers => layers == 0) >= 4, $"the kept letters are drawn through a layer ({string.Join(",", late)})");
            Assert.True(late.Count(layers => layers > 0) >= 1, $"no letter is still arriving through a layer ({string.Join(",", late)})");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
