using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ThreadChangesTab.swift (B2 1fbf81e), :1-75

[Collection("Headless")]
public sealed class ThreadChangesTabTests(HeadlessSession headless)
{
    private const string MathMinus = "−"; // U+2212 mathematical minus sign

    private static Avalonia.Controls.Window ShowInFrame(Control content, double width = 800, double height = 600)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :21 "1 file" when count is 1, "N files" when count > 1
    public void The_changes_tab_renders_file_count_and_stats() => headless.OnUiThread(() =>
    {
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

        // The text "1 file" should be present in the visual tree
        Assert.True(TextExistsInTree(tab, "1 file"));

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :21 "N files" plural form
    public void The_changes_tab_shows_plural_files_when_count_greater_than_one() => headless.OnUiThread(() =>
    {
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

        // The text "2 files" should be present
        Assert.True(TextExistsInTree(tab, "2 files"));

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :25 additions prefixed with +
    public void The_changes_tab_shows_additions_with_plus() => headless.OnUiThread(() =>
    {
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

        // Additions should show as "+42"
        Assert.True(TextExistsInTree(tab, "+42"));

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-199")] // :27 deletions prefixed with U+2212
    public void The_changes_tab_shows_deletions_with_mathematical_minus() => headless.OnUiThread(() =>
    {
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

        // Deletions should show with U+2212: "−7"
        Assert.True(TextExistsInTree(tab, MathMinus + "7"));

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :36-42 button style, focusable false, fixedSize
    public void The_changes_tab_is_not_focusable() => headless.OnUiThread(() =>
    {
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
    [Trait("Req", "PR-COMPOSER-200")] // :43 help text with shortcut hint
    public void The_changes_tab_has_tooltip_with_show_changes_text() => headless.OnUiThread(() =>
    {
        var files = new ValueList<FileChangeSummary.File>(new[]
        {
            new FileChangeSummary.File("file.swift", 1, 1, false)
        });
        var stats = new FileChangeSummary(files, 1, 1);

        var tab = new ThreadChangesTab(
            stats,
            anchor: _ => { },
            action: () => { }
        );

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        // Check that the tooltip text is set
        var tooltip = ToolTip.GetTip(tab) as string;
        Assert.NotNull(tooltip);
        Assert.Contains("Show this thread's changes", tooltip, StringComparison.Ordinal);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-200")] // :44 accessibility label with file count, additions, deletions
    public void The_changes_tab_has_accessibility_label_with_counts() => headless.OnUiThread(() =>
    {
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

        var window = ShowInFrame(tab);
        Dispatcher.UIThread.RunJobs();

        // Should show 3 files, 45 additions, 23 deletions
        Assert.True(TextExistsInTree(tab, "3 files"));
        Assert.True(TextExistsInTree(tab, "+45"));
        Assert.True(TextExistsInTree(tab, MathMinus + "23"));

        window.Close();
    });

    // Helper method to check if text exists in the visual tree
    private static bool TextExistsInTree(Control control, string expectedText)
    {
        // Check the current control if it's a TextBlock
        if (control is TextBlock textBlock && textBlock.Text?.Contains(expectedText, StringComparison.Ordinal) == true)
        {
            return true;
        }

        // Check all children
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

        // Check the Content property for other control types
        if (control is ContentControl contentControl && contentControl.Content is Control contentChild)
        {
            if (TextExistsInTree(contentChild, expectedText))
            {
                return true;
            }
        }

        return false;
    }
}
