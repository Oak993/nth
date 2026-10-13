using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10, the first-time question for a link from a chat (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11 "Open links in Visus or your default browser?" with "Remember" checked; 10 accessibility; ED-NEW-web-w10-4). A sheet
// over the chat window: the question, the site, Remember (checked), Cancel, Default browser and Visus; every control named, reachable by Tab and Space, Esc cancels and the keyboard goes back
// where it was, the window going away cancels, and a long site name or a right-to-left window leaves every button on the sheet.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-4")]
public sealed class WebW10ChatLinkSheetTests(HeadlessSession headless)
{
    private static (WebSettings Web, ChatLinkChoice Choice) Choice(string address = "https://example.org/article", bool control = false)
    {
        var web = new WebSettings(new InMemorySettingsStore());
        return (web, new ChatLinkChoice(web, SwiftURL.Create(address)!, control));
    }

    private static Avalonia.Controls.Window Show(Control content, double width = 640)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Avalonia.Controls.Window { Width = width, Height = 420, Content = content };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void The_sheet_asks_the_designed_question_names_the_site_and_has_five_named_controls() => headless.OnUiThread(() =>
    {
        var (_, choice) = Choice();
        var view = new ChatLinkSheetView(choice, () => { });
        var window = Show(view);
        try
        {
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Contains("Open links in Visus or your default browser?", texts);
            Assert.Contains("example.org", texts);
            Assert.Contains("Remember", texts);
            Assert.True(view.Remember.IsChecked);
            Assert.Equal("Cancel", Text(view.CancelButton));
            Assert.Equal("Default browser", Text(view.DefaultBrowserButton));
            Assert.Equal("Visus", Text(view.VisusButton));
            Assert.Equal("Cancel, open nothing", AutomationProperties.GetName(view.CancelButton));
            Assert.Equal("Open this link in your default browser", AutomationProperties.GetName(view.DefaultBrowserButton));
            Assert.Equal("Open this link in Visus", AutomationProperties.GetName(view.VisusButton));
            Assert.Equal("Remember this choice for links from chats", AutomationProperties.GetName(view.Remember));
            Assert.Equal("Open links in Visus or your default browser?", AutomationProperties.GetName(view));
            Assert.All(new Control[] { view.CancelButton, view.DefaultBrowserButton, view.VisusButton, view.Remember }, control => Assert.True(control.Focusable));
        }
        finally
        {
            window.Close();
        }
    });

    private static string Text(Button button) => ((TextBlock)button.Content!).Text ?? "";

    [Fact]
    public void Tab_order_follows_the_reading_order_Remember_then_Cancel_Default_browser_Visus() => headless.OnUiThread(() =>
    {
        var (_, choice) = Choice();
        var view = new ChatLinkSheetView(choice, () => { });
        var window = Show(view);
        try
        {
            var order = view.GetVisualDescendants().OfType<InputElement>().Where(element => element.Focusable && element.IsEffectivelyVisible).ToList();
            Assert.Equal([view.Remember, view.CancelButton, view.DefaultBrowserButton, view.VisusButton], order.Where(element => element is CheckBox || element is Button).Cast<Control>());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Visus_chooses_Visus_and_closes_the_sheet() => headless.OnUiThread(() =>
    {
        var (web, choice) = Choice();
        var dismissed = 0;
        var view = new ChatLinkSheetView(choice, () => dismissed++);
        var window = Show(view);
        try
        {
            Click(view.VisusButton);
            Assert.True(choice.IsFinished);
            Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
            Assert.Equal(1, dismissed);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Default_browser_chooses_it_and_Remember_cleared_stores_nothing() => headless.OnUiThread(() =>
    {
        var (web, choice) = Choice();
        var view = new ChatLinkSheetView(choice, () => { });
        var window = Show(view);
        try
        {
            view.Remember.IsChecked = false;
            Assert.False(choice.Remember);
            Click(view.DefaultBrowserButton);
            Assert.True(choice.IsFinished);
            Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
            view.Remember.IsChecked = true;
            Assert.True(choice.Remember);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Cancel_cancels_and_closes_the_sheet() => headless.OnUiThread(() =>
    {
        var (web, choice) = Choice();
        var dismissed = 0;
        var view = new ChatLinkSheetView(choice, () => dismissed++);
        var window = Show(view);
        try
        {
            Click(view.CancelButton);
            Assert.True(choice.IsFinished);
            Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
            Assert.Equal(1, dismissed);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Space_presses_the_focused_button_and_Enter_alone_chooses_nothing() => headless.OnUiThread(() =>
    {
        var (web, choice) = Choice();
        var view = new ChatLinkSheetView(choice, () => { });
        var window = Show(view);
        try
        {
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);
            Assert.False(choice.IsFinished); // nothing has the keyboard yet: a stray Enter chooses nothing
            view.VisusButton.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Settle(window);
            Assert.True(choice.IsFinished);
            Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_long_site_name_is_trimmed_and_all_three_buttons_stay_on_the_sheet() => headless.OnUiThread(() =>
    {
        var host = string.Join('.', Enumerable.Repeat("a-very-long-subdomain-label-for-the-site", 4)) + ".example.org";
        var (_, choice) = Choice("https://" + host + "/path");
        var view = new ChatLinkSheetView(choice, () => { });
        var window = Show(view);
        try
        {
            var site = view.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == "chat-link-site");
            Assert.Equal(TextTrimming.CharacterEllipsis, site.TextTrimming);
            Assert.Equal(host, site.Text);
            Assert.Equal("Link to " + host, AutomationProperties.GetName(site));
            var sheet = view.Bounds;
            foreach (var button in new[] { view.CancelButton, view.DefaultBrowserButton, view.VisusButton })
            {
                var box = button.TranslatePoint(default, view)!.Value;
                Assert.True(box.X >= -0.5 && box.X + button.Bounds.Width <= sheet.Width + 0.5, $"{Text(button)} sits outside the sheet");
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void In_a_right_to_left_window_the_question_wraps_and_every_button_stays_on_the_sheet() => headless.OnUiThread(() =>
    {
        var (_, choice) = Choice();
        var view = new ChatLinkSheetView(choice, () => { }) { FlowDirection = FlowDirection.RightToLeft };
        var window = Show(view);
        try
        {
            var title = view.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == "chat-link-title");
            Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            foreach (var button in new[] { view.CancelButton, view.DefaultBrowserButton, view.VisusButton })
            {
                var box = button.TranslatePoint(default, view)!.Value;
                Assert.True(box.X >= -0.5 && box.X + button.Bounds.Width <= view.Bounds.Width + 0.5, $"{Text(button)} sits outside the sheet");
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_buttons_wrap_instead_of_leaving_the_sheet_when_the_words_are_wide() => headless.OnUiThread(() =>
    {
        var (_, choice) = Choice();
        var view = new ChatLinkSheetView(choice, () => { });
        var window = Show(view);
        try
        {
            // Widen the words the way a large text scale or a long translation does.
            foreach (var button in new[] { view.CancelButton, view.DefaultBrowserButton, view.VisusButton })
                ((TextBlock)button.Content!).FontSize = 30;
            Settle(window);
            var sheetWidth = view.Bounds.Width;
            foreach (var button in new[] { view.CancelButton, view.DefaultBrowserButton, view.VisusButton })
            {
                var box = button.TranslatePoint(default, view)!.Value;
                Assert.True(box.X + button.Bounds.Width <= sheetWidth + 0.5, $"{Text(button)} left the sheet");
            }
            var rows = new[] { view.CancelButton, view.DefaultBrowserButton, view.VisusButton }.Select(button => Math.Round(button.TranslatePoint(default, view)!.Value.Y)).Distinct().Count();
            Assert.True(rows >= 2, "wide buttons did not wrap onto a second line");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Over_a_chat_window_the_sheet_takes_the_keyboard_Escape_cancels_and_the_keyboard_goes_back() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var scope = new AppModelHarness();
        var web = new WebSettings(new InMemorySettingsStore());
        var choice = new ChatLinkChoice(web, SwiftURL.Create("https://example.org/article")!, control: false);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        Marrow.App.Window.ThreadWindow window = null!;
        TextBox box = null!;
        scope.Main.OnMain(() =>
        {
            var anchor = new Border();
            box = new TextBox { Width = 200, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            window = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { box, anchor } }, Width = 900, Height = 700 };
            ChatHeadlessFixtures.Track(window);
            window.Show();
            box.Focus();
            ChatLinkSheet.Show(choice, anchor);
        });
        SettleAll(scope, frames);
        scope.Main.OnMain(() =>
        {
            var layer = window.GetVisualDescendants().OfType<WindowSheetLayer>().Single();
            var focused = window.FocusManager?.GetFocusedElement() as Visual;
            Assert.True(focused is not null && (ReferenceEquals(focused, layer) || layer.IsVisualAncestorOf(focused)), "the sheet did not take the keyboard");
            Assert.NotNull(layer.GetVisualDescendants().OfType<ChatLinkSheetView>().SingleOrDefault());
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        });
        SettleAll(scope, frames);
        scope.Main.OnMain(() =>
        {
            Assert.Empty(window.GetVisualDescendants().OfType<WindowSheetLayer>());
            Assert.True(choice.IsFinished);
            Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
            Assert.True(box.IsFocused, "the keyboard did not go back to where it was");
        });
    });

    [Fact]
    public void Answering_from_outside_closes_the_open_sheet_and_the_window_going_away_cancels_the_question() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var scope = new AppModelHarness();
        var web = new WebSettings(new InMemorySettingsStore());
        var answered = new ChatLinkChoice(web, SwiftURL.Create("https://example.org/a")!, control: false);
        var abandoned = new ChatLinkChoice(web, SwiftURL.Create("https://example.org/b")!, control: false);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        Marrow.App.Window.ThreadWindow first = null!;
        Marrow.App.Window.ThreadWindow second = null!;
        scope.Main.OnMain(() =>
        {
            var anchor = new Border();
            first = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { anchor } }, Width = 900, Height = 700 };
            ChatHeadlessFixtures.Track(first);
            first.Show();
            ChatLinkSheet.Show(answered, anchor);
            var other = new Border();
            second = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { other } }, Width = 900, Height = 700 };
            ChatHeadlessFixtures.Track(second);
            second.Show();
            ChatLinkSheet.Show(abandoned, other);
        });
        SettleAll(scope, frames);
        scope.Main.OnMain(() =>
        {
            Assert.Single(first.GetVisualDescendants().OfType<WindowSheetLayer>());
            answered.Choose(visus: true); // answered some other way: the sheet goes
        });
        SettleAll(scope, frames);
        scope.Main.OnMain(() =>
        {
            Assert.Empty(first.GetVisualDescendants().OfType<WindowSheetLayer>());
            Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
            Assert.False(abandoned.IsFinished);
            second.Close();
        });
        SettleAll(scope, frames);
        scope.Main.OnMain(() => Assert.True(abandoned.IsFinished));
    });

    private static void SettleAll(AppModelHarness h, ManualFrameSource frames)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            h.Main.Advance(TimeSpan.FromMilliseconds(16));
            frames.Advance(TimeSpan.FromMilliseconds(16));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
