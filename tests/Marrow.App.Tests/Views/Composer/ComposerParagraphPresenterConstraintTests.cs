using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-20 equivalence guard: the editor's paragraph presenter lays its text out within exactly the bounds Avalonia's own
// TextPresenter would use (its private measure and arrange constraint, including the arrange ceiling and the zero-to-infinity rule),
// through every measure and arrange step. Before L20 the presenter read them from a native layout it built and discarded; after L20 it
// reads the native constraint directly. A plain TextPresenter driven through the same steps is the oracle.
[Collection("Headless")]
public sealed class ComposerParagraphPresenterConstraintTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-20")]
    public void The_paragraph_presenter_lays_out_within_the_bounds_of_Avalonias_own_presenter_at_every_measure_and_arrange_step()
    {
        headless.OnUiThread(() =>
        {
            var text = ComposerKeystrokeBudgetTests.Draft();
            var plain = new TextPresenter { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            var composer = new ComposerParagraphPresenter { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            var step = 0;
            void Same(string what)
            {
                step++;
                Assert.Equal(plain.TextLayout.MaxWidth, composer.TextLayout.MaxWidth);
                Assert.Equal(plain.TextLayout.MaxHeight, composer.TextLayout.MaxHeight);
                Assert.True(composer.TextLayout.TextLines.Count > 0, what);
            }
            void Measure(Size size) { foreach (var p in new[] { plain, composer }) { p.InvalidateMeasure(); p.Measure(size); } Same("measure " + size); }
            void Arrange(Rect rect) { foreach (var p in new[] { plain, composer }) { p.InvalidateArrange(); p.Arrange(rect); } Same("arrange " + rect); }

            Same("never measured");
            Measure(new Size(300, double.PositiveInfinity));
            Assert.Equal(300, composer.TextLayout.MaxWidth);
            Arrange(new Rect(0, 0, 300, 400));
            Arrange(new Rect(0, 0, 250.4, 400)); // a fractional arrange width: the native presenter keeps its ceiling, and no height
            Assert.Equal(251, composer.TextLayout.MaxWidth);
            Assert.Equal(double.PositiveInfinity, composer.TextLayout.MaxHeight);
            Measure(new Size(320, 60));
            Assert.Equal(60, composer.TextLayout.MaxHeight);
            Measure(new Size(0, 0)); // zero means unbounded
            Assert.Equal(double.PositiveInfinity, composer.TextLayout.MaxWidth);
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Measure(new Size(180, double.PositiveInfinity));
            foreach (var p in new[] { plain, composer }) p.Text = text + " more";
            Same("text changed");
            foreach (var p in new[] { plain, composer }) { p.SelectionStart = 10; p.SelectionEnd = 40; p.CaretIndex = 40; }
            Same("selection changed");
            Arrange(new Rect(0, 0, 180, 900));
            Arrange(new Rect(0, 0, 179.2, 900));
            Assert.Equal(180, composer.TextLayout.MaxWidth);
            Assert.Equal(12, step);
        });
    }
}
