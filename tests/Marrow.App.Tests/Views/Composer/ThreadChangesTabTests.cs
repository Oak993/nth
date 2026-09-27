using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ThreadChangesTab.swift (B2 1fbf81e), :1-75

[Collection("Headless")]
public sealed class ThreadChangesTabTests(HeadlessSession headless)
{
    private const string MathMinus = "−"; // U+2212 mathematical minus sign

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // App/Views/Composer/ThreadChangesTab.swift:44 accessibility always says files
    public void Review_one_file_accessibility_keeps_the_literal_plural() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var tab = new ThreadChangesTab(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("a", 2, 3, false)]), 2, 3), _ => { }, () => { });
        Assert.Equal("1 files changed, 2 added, 3 removed", AutomationProperties.GetName(tab));
    });

    private static Avalonia.Controls.Window ShowInFrame(Control content, double width = 800, double height = 600)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Precedent: Views/Settings/ShortcutsSettingsPageTests.cs:28 - ShortcutStore's constructor is private.
    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :21 "1 file" when count is 1, "N files" when count > 1
    public void The_changes_tab_renders_file_count_and_stats() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("test.swift", 10, 5, false)
        });
        var stats = new FileChangeSummary(files, 10, 5);

        var anchorCalled = false;
        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { anchorCalled = true; },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(anchorCalled, "Anchor callback should be called when control renders");
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :25 "1 file" singular form
    public void The_changes_tab_shows_singular_file_when_count_is_one() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 0, false)
        });
        var stats = new FileChangeSummary(files, 1, 0);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(TextExistsInTree(tab, "1 file"), "Rendered text should read \"1 file\" for a single file");

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :21 "N files" plural form
    public void The_changes_tab_shows_plural_files_when_count_greater_than_one() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file1.swift", 5, 2, false),
            new FileChangeSummary.File("file2.swift", 3, 1, false)
        });
        var stats = new FileChangeSummary(files, 8, 3);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(TextExistsInTree(tab, "2 files"), "Rendered text should read \"2 files\" for multiple files");

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :25 additions prefixed with +
    public void The_changes_tab_shows_additions_with_plus() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 42, 5, false)
        });
        var stats = new FileChangeSummary(files, 42, 5);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(TextExistsInTree(tab, "+42"), "Rendered text should read \"+42\" for the additions");

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :27 deletions prefixed with U+2212
    public void The_changes_tab_shows_deletions_with_mathematical_minus() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 10, 7, false)
        });
        var stats = new FileChangeSummary(files, 10, 7);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(TextExistsInTree(tab, MathMinus + "7"), "Rendered text should read the U+2212 minus sign then 7 for the deletions");

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :36-42 button style, focusable false, fixedSize
    public void The_changes_tab_is_not_focusable() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 5, 2, false)
        });
        var stats = new FileChangeSummary(files, 5, 2);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        // Tab should not be focusable
        Assert.False(tab.Focusable, "ThreadChangesTab should not be focusable");

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :43 "Show this thread's changes" + ShortcutStore.hint(for: .toggleChanges)
    public void The_changes_tab_has_tooltip_with_the_default_chord_in_windows_notation() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var shared = ShortcutStore.Shared.Override(FreshStore());

        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 1, false)
        });
        var stats = new FileChangeSummary(files, 1, 1);

        var tab = new ThreadChangesTab(stats, anchor: _ => { }, action: () => { });

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        // OD-0008 B7: the port's Windows notation reads "Show this thread's changes (Ctrl+D)" where
        // upstream reads "Show this thread's changes (⌘D)" (default ToggleChanges chord, :231 KeyChord(keyCode: 2, modifiers: .command)).
        var tooltip = ToolTip.GetTip(tab) as string;
        Assert.Equal("Show this thread's changes (Ctrl+D)", tooltip);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :43 no parenthesis once the toggle-changes chord is cleared
    public void The_changes_tab_tooltip_has_no_parenthesis_when_the_chord_is_cleared() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var store = FreshStore();
        store.Set(null, AppShortcut.ToggleChanges);
        using var shared = ShortcutStore.Shared.Override(store);

        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 1, false)
        });
        var stats = new FileChangeSummary(files, 1, 1);

        var tab = new ThreadChangesTab(stats, anchor: _ => { }, action: () => { });

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        var tooltip = ToolTip.GetTip(tab) as string;
        Assert.Equal("Show this thread's changes", tooltip);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :44 accessibility label with file count, additions, deletions
    public void The_changes_tab_has_accessibility_label_with_counts() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file1.swift", 15, 8, false),
            new FileChangeSummary.File("file2.swift", 12, 9, false)
        });
        var stats = new FileChangeSummary(files, 27, 17);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        // Get the accessibility label
        var label = AutomationProperties.GetName(tab);
        Assert.NotNull(label);
        Assert.Contains("2 files changed", label, StringComparison.Ordinal);
        Assert.Contains("27 added", label, StringComparison.Ordinal);
        Assert.Contains("17 removed", label, StringComparison.Ordinal);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :16 button action is called on click
    public void The_changes_tab_calls_action_when_clicked() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 1, false)
        });
        var stats = new FileChangeSummary(files, 1, 1);

        var actionCalled = false;
        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { actionCalled = true; }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        // Simulate a click
        tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(actionCalled, "Action callback should be called on button click");

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-GIT-142")] // :9 stats comes from one FileChangeSummary
    public void The_changes_tab_uses_single_file_change_summary() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("a.swift", 10, 5, false),
            new FileChangeSummary.File("b.swift", 20, 10, false),
            new FileChangeSummary.File("c.swift", 15, 8, false)
        });
        var stats = new FileChangeSummary(files, 45, 23);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        Assert.NotNull(tab);
        // Verify all stats are properly set from the single FileChangeSummary
        Assert.True(stats.Files.Count == 3);
        Assert.True(stats.Additions == 45);
        Assert.True(stats.Deletions == 23);
        Assert.True(TextExistsInTree(tab, "3 files"));
        Assert.True(TextExistsInTree(tab, "+45"));
        Assert.True(TextExistsInTree(tab, MathMinus + "23"));
        Assert.Equal("3 files changed, 45 added, 23 removed", AutomationProperties.GetName(tab));

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-046")] // geometry: top corners 12, square bottom corners, HStack spacing 7, padding 12/7/21
    public void The_changes_tab_geometry_matches_the_uneven_rounded_shape_and_padding() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 1, false)
        });
        var stats = new FileChangeSummary(files, 1, 1);

        var tab = new ThreadChangesTab(stats, anchor: _ => { }, action: () => { });

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        var border = FindDescendant<Border>(tab);
        Assert.NotNull(border);
        // :15 UnevenRoundedRectangle(topLeadingRadius: 12, topTrailingRadius: 12): top corners round, bottom square.
        Assert.Equal(new CornerRadius(12, 12, 0, 0), border!.CornerRadius);

        var stack = FindDescendant<StackPanel>(tab);
        Assert.NotNull(stack);
        Assert.Equal(Orientation.Horizontal, stack!.Orientation);
        Assert.Equal(7, stack.Spacing); // :17 HStack(spacing: 7)
        // :31-33 .padding(.horizontal, 12), .padding(.top, 7), .padding(.bottom, 7 + overlap(14)) = 21
        Assert.Equal(new Thickness(12, 7, 12, 21), stack.Margin);

        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-046")] // :38 .glassEffect(.regular.interactive(), in: shape) recorded as data (ED-180)
    [Trait("Req", "UI-GIT-023")] // the tab carries the interactive regular glass material
    public void The_changes_tab_records_interactive_regular_glass_with_no_tint() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 1, false)
        });
        var stats = new FileChangeSummary(files, 1, 1);

        var tab = new ThreadChangesTab(stats, anchor: _ => { }, action: () => { });

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        var border = FindDescendant<Border>(tab);
        Assert.NotNull(border);
        var effect = SwiftGlass.GetEffect(border!);
        Assert.NotNull(effect);
        Assert.True(effect!.IsInteractive); // .regular.interactive()
        Assert.Null(effect.TintColor); // .regular carries no tint
        Assert.Null(effect.TintOpacity);
        Assert.Equal(GlassShapeKind.RoundedRectangle, effect.Shape.Kind);

        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-GIT-023")] // :7 overlap constant is 14, so bottom padding is 7 + 14 = 21
    public void The_changes_tab_overlap_constant_is_14() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        Assert.Equal(14, ThreadChangesTab.Overlap);
    });

    // Depth-first search for the first descendant of type T (Button.Content is not yet realized as a
    // Visual until the control template applies, so this walks the logical Content tree instead).
    private static T? FindDescendant<T>(Control control) where T : Control
    {
        if (control is T match) return match;
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control childControl && FindDescendant<T>(childControl) is { } found) return found;
            }
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratedChild)
        {
            if (FindDescendant<T>(decoratedChild) is { } found) return found;
        }
        else if (control is ContentControl contentControl && contentControl.Content is Control contentChild)
        {
            if (FindDescendant<T>(contentChild) is { } found) return found;
        }
        return null;
    }

    // Helper method to check if text exists in the visual tree
    private static bool TextExistsInTree(Control control, string expectedText)
    {
        // Check the current control if it's a TextBlock
        if (control is TextBlock textBlock && textBlock.Text?.Contains(expectedText, StringComparison.Ordinal) == true)
        {
            return true;
        }

        // For Grid and other Panel types
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control childControl && TextExistsInTree(childControl, expectedText))
                {
                    return true;
                }
            }
        }
        else if (control is Decorator decorator)
        {
            // Border (and other Decorators) hold their content in Child, not Content.
            if (decorator.Child is Control decoratedChild && TextExistsInTree(decoratedChild, expectedText))
            {
                return true;
            }
        }
        else if (control is ContentControl contentControl)
        {
            // Check the Content property for ContentControl (Button inherits from this)
            if (contentControl.Content is Control contentChild)
            {
                if (TextExistsInTree(contentChild, expectedText))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
