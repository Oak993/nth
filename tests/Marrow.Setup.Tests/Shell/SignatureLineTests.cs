using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Platform;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, step 3, and 3b's table ("Welcome: the signature line"); unit U1
// (ED-NEW-inst-u1-1). The check finishes after the window shows, so the line keeps its place from the start and shows
// nothing, then fades in 60 ms later with no movement: "Signed by ‹Publisher›" in the facts' ink, or "Unsigned test
// build, not for sharing" in the warning colour. A later change cross-fades (out, the words swap, back in) and nothing
// around the line moves. Frames are driven by hand.
[Collection("SetupHeadless")]
public sealed class SignatureLineTests(SetupHeadlessSession headless)
{
    private static readonly SignatureResult Unsigned = new(SignatureState.Unsigned, null, unchecked((int)0x800B0100));
    private static readonly SignatureResult Signed = new(SignatureState.Signed, "Ana-María Østergård-Fernández", 0);

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Before_the_check_it_holds_its_place_and_shows_nothing()
    {
        headless.OnUiThread(() =>
        {
            var (line, _, window) = Rig();
            window.Show();

            Assert.Equal(0, line.LineOpacity);
            Assert.Null(line.Words);
            Assert.True(line.Bounds.Height > 10, "the line's place is kept before the check answers");
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void An_unsigned_build_fades_in_its_warning_after_60_ms_without_moving_anything()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames, window) = Rig();
            window.Show();
            var before = line.Bounds;

            line.Set(Unsigned);
            Assert.Equal(SetupStrings.WelcomeUnsigned, line.Words);
            frames.Run(0.05);
            Assert.Equal(0, line.LineOpacity);
            frames.Run(1, () =>
            {
                window.UpdateLayout();
                Assert.Equal(before, line.Bounds);
            });
            window.UpdateLayout();

            Assert.Equal(1, line.LineOpacity);
            Assert.Equal(before, line.Bounds);
            Assert.Equal(SetupPalette.Dark.Warn, Ink(line, SetupStrings.WelcomeUnsigned));
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_signed_build_names_its_publisher_in_the_facts_ink()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames, window) = Rig();
            window.Show();

            line.Set(Signed);
            frames.Run(1);
            window.UpdateLayout();

            Assert.Equal("Signed by Ana-María Østergård-Fernández", line.Words);
            Assert.Equal(1, line.LineOpacity);
            Assert.Equal(SetupPalette.Dark.Ink2, Ink(line, line.Words!));
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_change_fades_out_swaps_the_words_and_fades_back_in()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames, window) = Rig();
            window.Show();
            line.Set(Unsigned);
            frames.Run(1);
            window.UpdateLayout();
            var before = line.Bounds;

            line.Set(Signed);
            frames.Run(0.05);
            Assert.Equal(SetupStrings.WelcomeUnsigned, line.Words);
            Assert.InRange(line.LineOpacity, 0.01, 0.99);
            var lowest = 1.0;
            frames.Run(1, () => lowest = Math.Min(lowest, line.LineOpacity));
            window.UpdateLayout();

            Assert.True(lowest < 0.01, "the old words leave before the new ones come");
            Assert.Equal("Signed by Ana-María Østergård-Fernández", line.Words);
            Assert.Equal(1, line.LineOpacity);
            Assert.Equal(before, line.Bounds);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_fades_in_at_once_without_the_delay()
    {
        headless.OnUiThread(() =>
        {
            var (line, frames, window) = Rig(calm: true);
            window.Show();

            line.Set(Unsigned);
            frames.Frame(1.0 / 60);
            Assert.True(line.LineOpacity > 0);
            frames.Run(0.5);
            Assert.Equal(1, line.LineOpacity);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Without_a_frame_loop_the_answer_shows_at_once()
    {
        headless.OnUiThread(() =>
        {
            var line = new SignatureLine(() => false);
            line.Set(Signed);
            Assert.Equal(1, line.LineOpacity);
            Assert.Equal(Signed, line.Shown);
        });
    }

    private static (SignatureLine Line, ManualFrameSource Frames, Window Window) Rig(bool calm = false)
    {
        var frames = new ManualFrameSource();
        var line = new SignatureLine(() => calm);
        line.Attach(new Animator(frames));
        var window = new Window { Width = 520, SizeToContent = SizeToContent.Height, Content = new StackPanel { Children = { new Border { Height = 40 }, line, new Border { Height = 40 } } } };
        SetupTheme.Apply(window.Resources, SetupPalette.Dark, 1);
        return (line, frames, window);
    }

    private static Color Ink(SignatureLine line, string words) =>
        Assert.IsType<Avalonia.Media.Immutable.ImmutableSolidColorBrush>(line.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == words && t.Opacity > 0).Foreground).Color;
}
