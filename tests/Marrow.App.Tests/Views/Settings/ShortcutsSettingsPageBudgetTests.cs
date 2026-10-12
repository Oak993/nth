using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-lean-21 (lean pass L21): one shortcut edit updates the Shortcuts page in place. ShortcutStore keeps every override in
// one observable map, so an edit re-runs every row's chord Track; the rows whose chord did not change now keep what they show
// instead of restyling. Budgets: controls new to the page after one edit, and bytes allocated on the UI thread per edit
// (store.Set and the dispatcher jobs it posts; Debug build, after a warm-up).
[Collection("Headless")]
public sealed class ShortcutsSettingsPageBudgetTests(HeadlessSession headless)
{
    private static readonly KeyChord First = new(0x2E, ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift);
    private static readonly KeyChord Second = new(0x2F, ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift);

    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 500, Height = 900, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Settle(ManualFrameSource frames, ManualTimeProvider clock)
    {
        for (var i = 0; i < 180 && frames.Pending > 0; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1000.0 / 60));
            frames.Advance(TimeSpan.FromMilliseconds(1000.0 / 60));
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(0, frames.Pending);
    }

    // What each recorder shows: its label, the label's font and ink, the key plate's fill and its accessible name.
    private static List<string> Shown(Visual root) => root.GetVisualDescendants().OfType<ShortcutRecorder>().Select(recorder =>
    {
        var label = recorder.GetVisualDescendants().OfType<TextBlock>().First();
        var plate = recorder.GetVisualDescendants().OfType<Border>().First(b => b.RenderTransform is ScaleTransform);
        static string Ink(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color.ToString() : "none";
        return $"{label.Text}|{label.FontSize}|{label.FontWeight}|{Ink(label.Foreground)}|{Ink(plate.Background)}|{AutomationProperties.GetName(plate)}|{((ScaleTransform)plate.RenderTransform!).ScaleX}";
    }).ToList();

    [Fact]
    [Trait("Decision", "ED-NEW-lean-21")]
    public void One_shortcut_edit_rebuilds_no_control_on_the_page()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var store = FreshStore();
            using var shared = ShortcutStore.Shared.Override(store);
            var window = Show(new ShortcutsSettingsPage());
            var before = new HashSet<Visual>(window.GetVisualDescendants(), ReferenceEqualityComparer.Instance);
            store.Set(First, AppShortcut.NewThread);
            Dispatcher.UIThread.RunJobs();
            var rebuilt = window.GetVisualDescendants().Count(visual => !before.Contains(visual));
            Assert.True(rebuilt <= ControlsRebuiltPerEdit, $"one edit put {rebuilt} new controls on the page (budget {ControlsRebuiltPerEdit})");
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-21")]
    public void One_shortcut_edit_allocates_within_its_budget()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var store = FreshStore();
            using var shared = ShortcutStore.Shared.Override(store);
            var window = Show(new ShortcutsSettingsPage());
            void Edit(int i)
            {
                store.Set(i % 2 == 0 ? First : Second, AppShortcut.NewThread);
                Dispatcher.UIThread.RunJobs();
            }
            for (var i = 0; i < 20; i++) Edit(i); // warm-up
            const int Edits = 40;
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Edits; i++) Edit(i);
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - start) / Edits;
            Assert.True(bytes <= BytesPerEdit, $"one shortcut edit allocated {bytes} B (budget {BytesPerEdit} B)");
            window.Close();
        });
    }

    // Equivalence guard: after edits, a clear and a section reset (each settled), every row shows exactly what a page built
    // fresh from the same store shows.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-21")]
    public void An_edited_page_shows_what_a_freshly_built_page_shows()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var frames = new ManualFrameSource();
        var clock = new ManualTimeProvider();
        using var frameScope = FrameClock.Source.Override(frames);
        using var clockScope = SwiftClock.Provider.Override(clock);
        headless.OnUiThread(() =>
        {
            var store = FreshStore();
            using var shared = ShortcutStore.Shared.Override(store);
            var window = Show(new ShortcutsSettingsPage());
            Settle(frames, clock);
            void Check(string after)
            {
                Dispatcher.UIThread.RunJobs();
                Settle(frames, clock);
                var fresh = Show(new ShortcutsSettingsPage());
                Settle(frames, clock);
                Assert.True(Shown(fresh).SequenceEqual(Shown(window)), $"after {after}: the edited page differs from a fresh one");
                fresh.Close();
            }
            store.Set(First, AppShortcut.NewThread);
            Check("a new chord");
            store.Set(Second, AppShortcut.NewThread);
            Check("a second chord on the same row");
            store.Set(null, AppShortcut.CommandPalette);
            Check("a cleared row");
            store.Reset(AppShortcutSection.Threads);
            Check("a section reset");
            window.Close();
        });
    }

    // Debug build. Measured on claude/release-integration 0a946c558 before the fix, and after; see ED-NEW-lean-21.
    private const int ControlsRebuiltPerEdit = 0; // measured 0 before the fix
    private const long BytesPerEdit = 83_500; // measured 55,565 B before the fix
}
