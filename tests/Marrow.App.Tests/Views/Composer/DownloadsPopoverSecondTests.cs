using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/DownloadsPopover.swift (B2 1fbf81e), :99-242 (DownloadsPopover, DownloadRow).
// Port shape read (R-VIEW-9, matching Chrome/PopoversTests.cs): DownloadsPopover is shown in a plain headless
// Window (not the Popup seam, which this unit does not present), MainActor.Spawn posts to the Avalonia dispatcher
// and only runs once Dispatcher.UIThread.RunJobs() pumps it, and RecentDownloads.DirectoryProvider is pointed at a
// throwaway folder under MARROW_PORT_DATA_DIR (R-TEST-9), never the real Downloads folder.
[Collection("Headless")]
public sealed class DownloadsPopoverSecondTests(HeadlessSession headless)
{
    private static IDisposable Directed(string? path) => RecentDownloads.DirectoryProvider.Override(() => path);

    private static Avalonia.Controls.Window Show(Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Avalonia.Controls.Window { Width = 500, Height = 800, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Pumps the dispatcher until <paramref name="loaded"/> is true or 2s pass: RecentDownloads.Load's
    /// scan runs on a real background thread (Task.detached, R-TASK-1), so its completion posting back to the
    /// dispatcher is not guaranteed to be queued by the time a single RunJobs() call runs.</summary>
    private static void PumpUntil(Func<bool> loaded)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!loaded() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>True once the popover shows either the empty note or at least one row (never the loading spinner).</summary>
    private static bool IsPastLoading(Panel listSlot) => listSlot.Children.Count > 0 && listSlot.Children[0] is not Border;

    private static (StackPanel Stack, Panel ListSlot) Parts(DownloadsPopover popover)
    {
        var menu = Assert.IsType<PopoverMenu>(popover.Child);
        var scroll = Assert.IsType<ScrollViewer>(menu.Child);
        var padded = Assert.IsType<Border>(scroll.Content);
        var wrapperStack = Assert.IsType<StackPanel>(padded.Child); // PopoverMenu's own internal StackPanel (:104)
        var stack = Assert.IsType<StackPanel>(Assert.Single(wrapperStack.Children)); // our BuildContent() result
        var listSlot = Assert.IsType<Panel>(stack.Children[1]);
        return (stack, listSlot);
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-023")] // DownloadsPopover.swift:145 .frame(width: 360, ...)
    public void The_popover_is_360_points_wide() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null);
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = Show(popover);
        Assert.Equal(360, popover.Width);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-079")] // DownloadsPopover.swift:153, :159-161: a full 15 rows' worth while loading
    public void The_popover_is_sized_for_a_full_15_rows_while_loading() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null); // Load() returns immediately (no directory), so isLoaded never flips.
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = Show(popover);
        var menu = Assert.IsType<PopoverMenu>(popover.Child);
        var scroll = Assert.IsType<ScrollViewer>(menu.Child);
        Assert.Equal(Math.Min(700, 15 * 38 + 106), scroll.Height); // :159-161 height(forRows:)
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-079")] // DownloadsPopover.swift:155, :159-161: the rows' own height once loaded
    public void The_popover_height_matches_the_loaded_row_count() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        folder.File("a.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        folder.File("b.txt", new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        using var directed = Directed(folder.Path);
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = Show(popover);
        var (_, listSlot) = Parts(popover);
        PumpUntil(() => IsPastLoading(listSlot)); // the .task load's completion posts back to main

        var menu = Assert.IsType<PopoverMenu>(popover.Child);
        var scroll = Assert.IsType<ScrollViewer>(menu.Child);
        Assert.Equal(Math.Min(700, 2 * 38 + 106), scroll.Height); // :159-161
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-079")] // DownloadsPopover.swift:154: nil for an empty folder, which sizes itself
    public void The_popover_self_sizes_for_an_empty_folder() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        using var directed = Directed(folder.Path);
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = Show(popover);
        var (_, listSlot) = Parts(popover);
        PumpUntil(() => IsPastLoading(listSlot));

        var menu = Assert.IsType<PopoverMenu>(popover.Child);
        var scroll = Assert.IsType<ScrollViewer>(menu.Child);
        Assert.True(double.IsNaN(scroll.Height));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-080")] // DownloadsPopover.swift:114 PopoverSectionHeader("Recent downloads")
    public void The_header_reads_Recent_downloads() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null);
        var popover = new DownloadsPopover(_ => { }, () => { });
        Show(popover);
        var (stack, _) = Parts(popover);
        var header = Assert.IsType<PopoverSectionHeader>(stack.Children[0]);
        var text = Assert.IsType<TextBlock>(header.Child);
        Assert.Equal("Recent downloads", text.Text);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-080")] // DownloadsPopover.swift:116-121 a small, centered ProgressView padded 14 vertically
    public void The_loading_state_is_a_small_centered_spinner_padded_14() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null);
        var popover = new DownloadsPopover(_ => { }, () => { });
        Show(popover);
        var (_, listSlot) = Parts(popover);
        var loading = Assert.IsType<Border>(Assert.Single(listSlot.Children));
        Assert.Equal(new Thickness(0, 14), loading.Margin); // :121 .padding(.vertical, 14)
        var spinner = Assert.IsType<ProgressBar>(loading.Child);
        Assert.True(spinner.IsIndeterminate); // :118 ProgressView().controlSize(.small)
        Assert.Equal(16, spinner.Width);
        Assert.Equal(16, spinner.Height);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-080")] // DownloadsPopover.swift:123
    [Trait("Req", "UI-COMPOSER-025")] // DownloadsPopover.swift:123
    public void The_empty_state_note_reads_the_exact_text() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        using var directed = Directed(folder.Path);
        var popover = new DownloadsPopover(_ => { }, () => { });
        Show(popover);
        var (_, listSlot) = Parts(popover);
        PumpUntil(() => IsPastLoading(listSlot));

        var note = Assert.IsType<PopoverNote>(Assert.Single(listSlot.Children));
        var text = Assert.IsType<TextBlock>(note.Child);
        Assert.Equal("No downloads yet. Anything you download appears here.", text.Text);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-081")] // DownloadsPopover.swift:134, :139
    [Trait("Req", "UI-COMPOSER-027")]
    [Trait("Req", "UI-COMPOSER-028")]
    [Trait("Decision", "OD-0008")] // "Show in Finder" -> "Show in File Explorer"
    public void The_footer_rows_read_Show_in_File_Explorer_and_Choose_Files() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null);
        var popover = new DownloadsPopover(_ => { }, () => { });
        Show(popover);
        var (stack, _) = Parts(popover);

        Assert.IsType<PopoverDivider>(stack.Children[2]); // :133
        var explorerRow = Assert.IsType<PopoverItem>(stack.Children[3]);
        Assert.Equal("Show in File Explorer", TitleOf(explorerRow));
        var chooseRow = Assert.IsType<PopoverItem>(stack.Children[4]);
        Assert.Equal("Choose Files…", TitleOf(chooseRow)); // U+2026 HORIZONTAL ELLIPSIS, not three periods
    });

    private static string TitleOf(PopoverItem item)
    {
        var button = Assert.IsType<Button>(item.Child);
        var background = Assert.IsType<Border>(button.Content);
        var row = Assert.IsType<StackPanel>(background.Child);
        var title = Assert.IsType<MiddleTruncatingText>(row.Children[1]); // [0] is the 16pt leading glyph column
        return title.FullText;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-081")] // DownloadsPopover.swift:134-137 NSWorkspace.shared.open(directory)
    public void Show_in_File_Explorer_opens_the_downloads_folder() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        using var directed = Directed(folder.Path);
        string? opened = null;
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = path => opened = path;
        try
        {
            var popover = new DownloadsPopover(_ => { }, () => { });
            Show(popover);
            var (stack, _) = Parts(popover);
            var explorerRow = Assert.IsType<PopoverItem>(stack.Children[3]);
            var button = Assert.IsType<Button>(explorerRow.Child);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); // the deferred main-actor turn (Popovers.cs :273)
            Assert.Equal(folder.Path, opened);
        }
        finally { ShellWorkspace.OpenWithDefaultApp = previous; }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-081")] // DownloadsPopover.swift:135 "if let directory": no directory, no call
    public void Show_in_File_Explorer_does_nothing_without_a_downloads_directory() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null);
        var called = false;
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = _ => called = true;
        try
        {
            var popover = new DownloadsPopover(_ => { }, () => { });
            Show(popover);
            var (stack, _) = Parts(popover);
            var explorerRow = Assert.IsType<PopoverItem>(stack.Children[3]);
            var button = Assert.IsType<Button>(explorerRow.Child);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(called);
        }
        finally { ShellWorkspace.OpenWithDefaultApp = previous; }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-081")] // DownloadsPopover.swift:139-141 chooseOther()
    public void Choose_Files_runs_chooseOther() => headless.OnUiThread(() =>
    {
        using var directed = Directed(null);
        var called = false;
        var popover = new DownloadsPopover(_ => { }, () => called = true);
        Show(popover);
        var (stack, _) = Parts(popover);
        var chooseRow = Assert.IsType<PopoverItem>(stack.Children[4]);
        var button = Assert.IsType<Button>(chooseRow.Child);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(called);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-082")] // DownloadsPopover.swift:178-192
    [Trait("Req", "UI-COMPOSER-026")]
    public void Clicking_a_row_arms_the_drop_closes_the_popover_then_picks_the_file() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        var path = folder.File("report.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        using var directed = Directed(folder.Path);
        string? picked = null;
        var closed = false;
        var popover = new DownloadsPopover(url => picked = url, () => { });
        Popovers.SetClosePopover(popover, () => closed = true);
        var window = Show(popover);
        var (_, listSlot) = Parts(popover);
        PumpUntil(() => IsPastLoading(listSlot)); // the .task load's completion

        var rowsPanel = Assert.IsType<StackPanel>(Assert.Single(listSlot.Children));
        var rowControl = Assert.Single(rowsPanel.Children);
        var rowDecorator = Assert.IsAssignableFrom<Decorator>(rowControl); // DownloadRow, file-private
        var button = Assert.IsType<Button>(rowDecorator.Child);

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(closed); // :190 dismiss(), runs before the deferred pick
        Assert.Null(picked); // :192 Task { @MainActor in pick() }: not yet run
        var drop = AttachmentDrops.Shared.Current.Take();
        Assert.NotNull(drop); // :185-189 armed with the row's own preview frame and a 6pt radius
        Assert.Equal(6, drop!.Radius);

        Dispatcher.UIThread.RunJobs(); // the deferred main-actor turn
        Assert.Equal(path, picked);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-026")] // DownloadsPopover.swift:194-220 the row's geometry and hover
    public void A_download_row_is_38_tall_with_the_name_and_formatted_size() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        folder.File("photo.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), bytes: 2048);
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var directed = Directed(folder.Path);
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = Show(popover);
        var (_, listSlot) = Parts(popover);
        PumpUntil(() => IsPastLoading(listSlot));

        var rowsPanel = Assert.IsType<StackPanel>(Assert.Single(listSlot.Children));
        var rowDecorator = Assert.IsAssignableFrom<Decorator>(Assert.Single(rowsPanel.Children)); // DownloadRow, file-private
        var button = Assert.IsType<Button>(rowDecorator.Child);
        var background = Assert.IsType<Border>(button.Content);
        var row = Assert.IsType<StackPanel>(background.Child);
        Assert.Equal(38, row.Height); // :210
        Assert.Equal(new Thickness(8, 0), row.Margin); // :209
        Assert.Equal(10, row.Spacing); // :194

        var nameBlock = Assert.IsType<MiddleTruncatingTextBlock>(row.Children[1]);
        Assert.Equal("photo.txt", nameBlock.FullText); // :196
        var sizeBlock = Assert.IsType<TextBlock>(row.Children[3]);
        Assert.True(sizeBlock.IsVisible);
        Assert.Equal("2 KB", sizeBlock.Text); // :201-206
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-026")] // DownloadsPopover.swift:201 if let size = item.size
    public void A_download_row_omits_its_size_text_when_the_file_is_zero_bytes() => headless.OnUiThread(() =>
    {
        using var folder = new ScratchDownloads();
        folder.File("empty.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), bytes: 0);
        using var directed = Directed(folder.Path);
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = Show(popover);
        var (_, listSlot) = Parts(popover);
        PumpUntil(() => IsPastLoading(listSlot));

        var rowsPanel = Assert.IsType<StackPanel>(Assert.Single(listSlot.Children));
        var rowDecorator = Assert.IsAssignableFrom<Decorator>(Assert.Single(rowsPanel.Children)); // DownloadRow, file-private
        var button = Assert.IsType<Button>(rowDecorator.Child);
        var background = Assert.IsType<Border>(button.Content);
        var row = Assert.IsType<StackPanel>(background.Child);
        var sizeBlock = Assert.IsType<TextBlock>(row.Children[3]);
        Assert.False(sizeBlock.IsVisible);
        window.Close();
    });
}
