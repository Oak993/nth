using System.Linq;
using System.Text;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-20 (lean pass L20): the bytes one keystroke allocates in a 2,000-character draft, through the pill ComposerViewTests.Pill
// builds, on screen 600 wide (its runtime resolves no thread, so the whole pill lays out, as ComposerViewTests part 1 does). A keystroke
// is the real one: a TextInput event on the focused text box (insert at the caret, caret and selection move, undo snapshot), then the
// pill's main-loop and layout pump (ComposerViewTests.Pump). Second budget: one relayout of the editor's paragraph presenter on the same
// draft (measure only: the presenter's layout is rebuilt at every measure). Headless and deterministic (R-TEST-5): bytes are the UI
// thread's own (GC.GetAllocatedBytesForCurrentThread), no clock, no sleeps; 20 warm keystrokes and relayouts first, then the mean of 40.
// Debug build (the test project builds Debug). Numbers before and after the L20 fix: see ED-NEW-lean-20 in
// porting/engineering_decisions.md.
[Collection("Headless")]
public sealed class ComposerKeystrokeBudgetTests(HeadlessSession headless)
{
    private const int Warm = 20;
    private const int Measured = 40;
    // Limits: about 1.3x the measured mean after the L20 fix (408,543 and 59,837 bytes; on the unchanged base 664,033 and 119,589;
    // ED-NEW-lean-20).
    private const long KeystrokeLimit = 531_000;
    private const long RelayoutLimit = 78_000;

    // Four paragraphs of English words, exactly 2,000 characters.
    internal static string Draft()
    {
        const string words = "The quick brown fox jumps over the lazy dog. ";
        var text = new StringBuilder();
        while (text.Length < 2000) text.Append(words);
        text.Length = 2000;
        text[499] = '\n'; text[999] = '\n'; text[1499] = '\n';
        return text.ToString();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-20")]
    public void One_keystroke_in_a_2000_character_draft_stays_inside_its_byte_budget()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var view = Pill(h, runtime);
            var window = h.Main.OnMain(() => Show(view, h.App, width: 600));
            try
            {
                Pump(h, window);
                var box = One<ComposerNSTextView>(view);
                h.Main.OnMain(() => Type(view, Draft()));
                h.Main.OnMain(() => box.Focus());
                Pump(h, window);
                Assert.Equal(2000, box.Text!.Length);
                var typed = 0;
                void Keystroke()
                {
                    var key = "lean pass "[typed++ % 10].ToString();
                    h.Main.OnMain(() => box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = key }));
                    Pump(h, window);
                }
                for (var i = 0; i < Warm; i++) Keystroke();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < Measured; i++) Keystroke();
                var perKeystroke = (GC.GetAllocatedBytesForCurrentThread() - before) / Measured;
                Assert.Equal(2000 + Warm + Measured, box.Text!.Length); // every keystroke reached the draft
                Assert.Equal(box.Text.Length, box.CaretIndex);
                Assert.True(perKeystroke <= KeystrokeLimit, $"one keystroke allocated {perKeystroke} bytes; the budget is {KeystrokeLimit}");
            }
            finally { Close(h, window); }
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-20")]
    public void One_relayout_of_the_editor_presenter_on_a_2000_character_draft_stays_inside_its_byte_budget()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var view = Pill(h, runtime);
            var window = h.Main.OnMain(() => Show(view, h.App, width: 600));
            try
            {
                h.Main.OnMain(() => Type(view, Draft()));
                Pump(h, window);
                var presenter = One<ComposerNSTextView>(view).GetVisualDescendants().OfType<ComposerParagraphPresenter>().Single();
                Assert.True(presenter.TextLayout.TextLines.Count > 4); // the draft wraps at the pill's width
                void Relayout() { presenter.InvalidateMeasure(); window.UpdateLayout(); }
                for (var i = 0; i < Warm; i++) Relayout();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < Measured; i++) Relayout();
                var perRelayout = (GC.GetAllocatedBytesForCurrentThread() - before) / Measured;
                Assert.True(perRelayout <= RelayoutLimit, $"one relayout allocated {perRelayout} bytes; the budget is {RelayoutLimit}");
            }
            finally { Close(h, window); }
        });
    }
}
