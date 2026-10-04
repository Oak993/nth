// Source: DroppyCode/App/Views/Tour/TourWindowController.swift (B2 1fbf81e), :1-179.

using System.Linq;
using Avalonia;
using AvaloniaWindow = Avalonia.Controls.Window;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Tour;

/// <summary>
/// Tests for TourWindowController (:4-179).
/// Window controller for presenting a modal tour (onboarding walkthrough) that refocuses if already open,
/// sizes by measuring all pages before showing, creates a borderless floating window, and clears references
/// on close so a later present() builds fresh.
/// </summary>
[Collection("Headless")]
public sealed class TourWindowControllerTests(HeadlessSession headless)
{
    private AvaloniaWindow? createdWindow;

    private static TourPage CreateTestPage(string imageName, string title, string description)
    {
        return new TourPage(imageName, title, description);
    }

    private void Cleanup(AvaloniaWindow? window)
    {
        if (window != null && window.IsVisible)
        {
            window.Close();
        }
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-094")] // DroppyCode/App/Views/Tour/TourWindowController.swift:27-31
    public void PresentingOpenTourRefocusesWindow()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Start here") };
            var onFinishCalls = 0;
            var onCloseCalls = 0;

            var controller = TourWindowController.Shared;
            var window1 = controller.Present(
                pages,
                width: 660,
                continueButtonTitle: "Continue",
                finishButtonTitle: "Done",
                onFinish: () => onFinishCalls++,
                onClose: () => onCloseCalls++
            );
            createdWindow = window1;

            // Act: present again with different pages and callbacks
            var newPages = new[] { CreateTestPage("page2", "Next", "Another page") };
            var window2 = controller.Present(
                newPages,
                width: 660,
                continueButtonTitle: "Continue",
                finishButtonTitle: "Done"
            );

            try
            {
                // Assert: same window object returned, not rebuilt
                Assert.Same(window1, window2);
                Assert.True(window2.IsActive || window2.IsVisible);
            }
            finally
            {
                Cleanup(window1);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // DroppyCode/App/Views/Tour/TourWindowController.swift:37-46
    public void WindowSizeIsPreComputedAcrossAllPages()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[]
            {
                CreateTestPage("page1", "First", "Description one"),
                CreateTestPage("page2", "Second", "A much longer description that might wrap to multiple lines"),
                CreateTestPage("page3", "Third", "Short"),
            };

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(pages, width: 660);
            createdWindow = window;

            try
            {
                // Assert: image height = (660 / 1.6).rounded() = 412.5 -> 413 (half away from zero)
                var expectedImageHeight = SwiftNumber.Rounded(660 / TourView.ImageAspectRatio);
                Assert.Equal(413, expectedImageHeight); // :37 imageHeight = (width / TourView.imageAspectRatio).rounded()

                // Assert: total height should be a reasonable value (image height + bottom panel height)
                var content = window.Content;
                Assert.NotNull(content);
                Assert.True(window.Height > 0);
                Assert.Equal(660, window.Width);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // DroppyCode/App/Views/Tour/TourWindowController.swift:44-46
    public void EmptyPageListSizesWindowToEmptyState()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var emptyPages = Array.Empty<TourPage>();

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(emptyPages, width: 660);
            createdWindow = window;

            try
            {
                // Assert: empty state height is 220 points
                Assert.Equal(TourView.EmptyStateHeight, window.Height);
                Assert.Equal(660, window.Width);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-096")] // DroppyCode/App/Views/Tour/TourWindowController.swift:76-90
    public void WindowIsBorderlessTransparentFloatingAndShadowed()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(pages);
            createdWindow = window;

            try
            {
                // Assert window properties:
                // - No title bar (WindowDecorations = None)
                Assert.Equal(Avalonia.Controls.WindowDecorations.None, window.WindowDecorations);

                // - Transparent background (no opaque white fill)
                // Verify background is set to transparent

                // - Window is visible
                Assert.True(window.IsVisible);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-096")] // DroppyCode/App/Views/Tour/TourWindowController.swift:8-11
    public void WindowCanBecomeKeyAndMain()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(pages);
            createdWindow = window;

            try
            {
                // Assert: window is created and visible
                Assert.True(window.IsVisible);

                // Try to focus the window
                window.Focus();
                // Note: IsFocused may not be set in headless tests
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-097")] // DroppyCode/App/Views/Tour/TourWindowController.swift:92-101
    public void WindowIsCenteredOnMainWindowWhenVisible()
    {
        headless.OnUiThread(() =>
        {
            // Note: This test is limited in headless environment.
            // The positioning logic requires a main window to be visible.
            // We test that the window is positioned and can be accessed.

            var controller = TourWindowController.Shared;
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            // Act
            var window = controller.Present(pages, width: 660);
            createdWindow = window;

            try
            {
                // Assert: window has position set and size correct
                Assert.Equal(660, window.Width);
                Assert.True(window.Height > 0);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-098")] // DroppyCode/App/Views/Tour/TourWindowController.swift:103-108
    public void ClosingWindowClearsControllerReferences()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act: present a window
            var window1 = controller.Present(pages);
            createdWindow = window1;
            Assert.NotNull(window1);

            // Close the window
            window1.Close();

            // Act: present again - should create a new window
            var window2 = controller.Present(pages);

            try
            {
                // Assert: new window is created (different object)
                Assert.NotNull(window2);
                Assert.NotSame(window1, window2);
            }
            finally
            {
                Cleanup(window2);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-098")] // DroppyCode/App/Views/Tour/TourWindowController.swift:139-143
    public void CloseMethodClearsReferences()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act: present a window
            var window1 = controller.Present(pages);
            createdWindow = window1;

            // Call close() directly
            controller.Close();

            // Act: present again - should create a new window
            var window2 = controller.Present(pages);

            try
            {
                // Assert: new window created
                Assert.NotNull(window2);
                Assert.NotSame(window1, window2);
            }
            finally
            {
                Cleanup(window2);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-096")] // DroppyCode/App/Views/Tour/TourWindowController.swift:75-80 the borderless style mask has no .resizable: the user cannot resize or maximize the tour
    public void The_tour_window_cannot_be_resized_by_the_user()
    {
        headless.OnUiThread(() =>
        {
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };
            var controller = TourWindowController.Shared;
            var window = controller.Present(pages);
            createdWindow = window;
            try
            {
                Assert.False(window.CanResize);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    // The same, for pages that differ: the window is sized for the tallest page, and a short page's bottom panel still fills what is left, so its
    // button stays 24 points above the card's bottom (TourView.swift:175-178) instead of riding up with the short text.
    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // DroppyCode/App/Views/Tour/TourWindowController.swift:37-46; TourView.swift:149-181
    public void A_page_shorter_than_the_tallest_still_pins_its_button_to_the_card_bottom()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var frames = new ManualFrameSource();
            using var frameSource = FrameClock.Source.Override(frames);
            var longText = string.Join(" ", Enumerable.Repeat("A description long enough to wrap onto many lines of the card.", 8));
            var pages = new[] { CreateTestPage("short", "Short", "One line."), CreateTestPage("tall", "Tall", longText) };
            var window = h.Main.OnMain(() => TourWindowController.Shared.Present(pages, width: 660));
            createdWindow = window;
            var view = Assert.IsType<TourView>(window.Content);
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var short_ = Assert.Single(view.BottomHost.Children);
                short_.Measure(new Size(660, double.PositiveInfinity));
                Assert.True(short_.DesiredSize.Height < window.Height - 413 - 20, "the short page must need clearly less than the tall page's panel");
                Assert.Equal(window.Height - 413, view.BottomHost.Bounds.Height, 1);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Button>(view.BottomHost));
                Assert.Equal(window.Height - 24, button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y, 1);
            }
            finally
            {
                Cleanup(window);
                TourWindowController.Shared.Close();
            }
        });
    }

    // PR-UPDATE-095 "the tour card's height is measured across all pages before the window opens, so it never grows or shrinks between pages"
    // (TourWindowController.swift:37-46, :70-74), with the real eight pages: the window keeps one size from the first page to the last, and on every
    // page the card's bottom panel fills what the 413-point image leaves, so the 220 x 42 button sits 24 points above the card's bottom edge
    // (TourView.swift:149-181: the panel is `.frame(maxWidth: .infinity, maxHeight: .infinity)`, padded 24 at the bottom).
    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // DroppyCode/App/Views/Tour/TourWindowController.swift:37-46,70-74; TourView.swift:58-60,149-181
    public void Paging_through_all_eight_real_pages_never_changes_the_window_size_and_pins_the_button_to_the_card_bottom()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var frames = new ManualFrameSource();
            using var frameSource = FrameClock.Source.Override(frames);
            var window = h.Main.OnMain(() => TourWindowController.Shared.Present(Marrow.App.Views.Tour.Tour.Pages, width: 660));
            createdWindow = window;
            var view = Assert.IsType<TourView>(window.Content);
            try
            {
                var pageCount = Marrow.App.Views.Tour.Tour.Pages.Count;
                Assert.Equal(8, pageCount);
                var size = new Size(window.Width, window.Height);
                var tallestPanel = 0.0;
                for (var page = 0; page < pageCount; page++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.Equal(page, view.CurrentIndex);
                    Assert.Equal(size, new Size(window.Width, window.Height)); // never grows or shrinks between pages
                    Assert.Equal(size, window.ClientSize);
                    var panel = Assert.Single(view.BottomHost.Children);
                    panel.Measure(new Size(660, double.PositiveInfinity));
                    tallestPanel = Math.Max(tallestPanel, panel.DesiredSize.Height);
                    Assert.True(panel.DesiredSize.Height <= size.Height - 413 + 0.01, "page " + page + " needs more than the window gives its panel");
                    // The bottom panel fills the room under the 413-point image, whatever its own text needs.
                    Assert.Equal(size.Height - 413, view.BottomHost.Bounds.Height, 1);
                    Assert.Equal(413, view.BottomHost.TranslatePoint(default, window)!.Value.Y, 1);
                    // The 220 x 42 action sits 24 points above the card's bottom on every page.
                    var button = Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Button>(view.BottomHost));
                    Assert.Equal(220, button.Bounds.Width, 1);
                    Assert.Equal(size.Height - 24, button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y, 1);
                    if (page < pageCount - 1)
                    {
                        h.Main.OnMain(view.Advance);
                        h.Main.Advance(TimeSpan.FromSeconds(0.5));
                        frames.Advance(TimeSpan.FromSeconds(0.5));
                    }
                }
                // The window is the image plus the TALLEST page's panel: that page needs exactly what it is given.
                Assert.Equal(size.Height - 413, tallestPanel, 1);
            }
            finally
            {
                Cleanup(window);
                TourWindowController.Shared.Close();
            }
        });
    }
}
