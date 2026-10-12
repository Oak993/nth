using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Setup.Install;
using Marrow.Setup.Motion;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;
using StepState = Marrow.Setup.Install.StepState;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b ("Reserved, not animated": the byte count keeps the width of its
// longest form) and 4 ("210 of 420 MB" beside Copying); unit U3 (ED-NEW-inst-u3-1). The count's width never changes as the
// numbers climb, so the step's label never re-wraps; a label that wraps at a large text size grows its row instead of
// running under the count; the count fades in when its step starts working (0.15 s fades in Calm) and is part of the
// row's name for Narrator. Frames are driven by hand.
[Collection("SetupHeadless")]
public sealed class ByteCountTests(SetupHeadlessSession headless)
{
    private const string Longest = "420 of 420 MB";

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    [Trait("Decision", "ED-247")]
    public void The_count_keeps_its_longest_form_s_width_and_the_label_never_re_wraps(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (row, _, window) = Rig(textScale, 420);
            window.Show();
            row.Now(Copying("0 of 420 MB"));
            window.UpdateLayout();
            var width = row.Count.Bounds.Width;
            var label = Label(row).Bounds;

            foreach (var words in new[] { "1 of 420 MB", "88 of 420 MB", "210 of 420 MB", Longest })
            {
                row.Set(Copying(words));
                window.UpdateLayout();
                Assert.Equal(width, row.Count.Bounds.Width, 3);
                Assert.Equal(label, Label(row).Bounds);
            }
            var plain = SetupTheme.Text(new TextBlock { Text = Longest, HorizontalAlignment = HorizontalAlignment.Left }, TextRole.Numbers, SetupTheme.Ink3);
            ((StackPanel)window.Content!).Children.Add(plain);
            window.UpdateLayout();
            Assert.Equal(plain.Bounds.Width, width, 1);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void At_a_large_text_size_a_wrapping_label_grows_its_row_and_never_runs_under_the_count()
    {
        headless.OnUiThread(() =>
        {
            var (row, _, window) = Rig(2.25, 420);
            window.Show();
            var adding = new StepView(InstallStep.Copying, SetupStrings.StepAddingBoth, StepState.Working, "210 of 420 MB", Longest);
            row.Now(adding);
            window.UpdateLayout();

            var label = Label(row);
            Assert.True(row.Bounds.Height > 34, "the row grew for its wrapped label");
            Assert.True(label.Bounds.Height > label.FontSize * 1.9, "the label wrapped");
            Assert.True(label.Bounds.Right <= row.Count.Bounds.Left + 0.5, "the label runs under the count");
            Assert.True(row.Count.Bounds.Right <= row.Bounds.Width + 0.5);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_count_fades_in_when_its_step_works_and_Calm_fades_quicker()
    {
        headless.OnUiThread(() =>
        {
            var usual = FadeIn(calm: false);
            var calm = FadeIn(calm: true);
            Assert.True(calm < usual, $"Calm took {calm} s, the usual fade {usual} s");

            var loose = new ByteCount(() => false);
            loose.Set("1 of 420 MB", Longest, shown: true);
            Assert.Equal(1, loose.CountOpacity);
            loose.Set("1 of 420 MB", Longest, shown: false);
            Assert.Equal(0, loose.CountOpacity);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_waiting_step_keeps_its_count_hidden_and_the_row_s_name_carries_the_count_once_shown()
    {
        headless.OnUiThread(() =>
        {
            var (row, frames, window) = Rig(1, 420);
            window.Show();
            row.Now(Copying("0 of 420 MB") with { State = StepState.Waiting });
            Assert.Equal(0, row.Count.CountOpacity);
            Assert.Equal(SetupStrings.StepCopying, Avalonia.Automation.AutomationProperties.GetName(row));
            row.Set(Copying("12 of 420 MB"));
            frames.Run(1);
            Assert.Equal(1, row.Count.CountOpacity, 2);
            Assert.Equal(Longest, row.Count.Longest);
            Assert.Equal(SetupStrings.StepCopying + ", 12 of 420 MB", Avalonia.Automation.AutomationProperties.GetName(row));
            window.Close();
        });
    }

    private static StepView Copying(string words) => new(InstallStep.Copying, SetupStrings.StepCopying, StepState.Working, words, Longest);

    private static TextBlock Label(StepRowView row) => row.Children.OfType<TextBlock>().Single();

    private static double FadeIn(bool calm)
    {
        var frames = new ManualFrameSource();
        var count = new ByteCount(() => calm);
        count.Attach(new Animator(frames));
        count.Set("1 of 420 MB", Longest, shown: true);
        Assert.Equal(0, count.CountOpacity);
        var seconds = 0.0;
        var previous = 0.0;
        while (frames.HasPendingFrame && seconds < 5)
        {
            frames.Frame();
            seconds += 1.0 / 60;
            Assert.True(count.CountOpacity >= previous - 1e-9, "the fade went back");
            previous = count.CountOpacity;
        }
        Assert.Equal(1, count.CountOpacity, 2);
        return seconds;
    }

    private static (StepRowView Row, ManualFrameSource Frames, Window Window) Rig(double textScale, double width)
    {
        var frames = new ManualFrameSource();
        var row = new StepRowView(() => false) { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        row.Attach(new Animator(frames));
        var window = new Window { Width = 520, SizeToContent = SizeToContent.Height, Content = new StackPanel { Children = { row } } };
        SetupTheme.Apply(window.Resources, SetupPalette.Dark, textScale);
        return (row, frames, window);
    }
}
