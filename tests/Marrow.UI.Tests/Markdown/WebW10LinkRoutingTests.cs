using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// PORT-ONLY: web surfaces W10, links from chats in the Markdown views (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11; ED-NEW-web-w10-2, -8). A chat sets one hook above its text; a plain or Ctrl click on a link reaches it
// with the click's modifiers, a right click still shows the popover, a hook that declines leaves the old behaviour, Markdown outside a chat is untouched, and the popover's Open Link follows the
// hook and takes the chat's own extra rows.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-2")]
public sealed class WebW10LinkRoutingTests(HeadlessSession headless)
{
    private const string Source = "see [link](https://chat-route.invalid/article) and plain words here";

    private static Window Show(Control content)
    {
        var window = new Window { Width = 500, Height = 220, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Point CharacterCenter(SelectableTextBlock block, string text)
    {
        var offset = 0;
        foreach (var inline in block.Inlines!)
        {
            if (inline is Run run)
            {
                var value = run.Text ?? "";
                var found = value.IndexOf(text, StringComparison.Ordinal);
                if (found >= 0) return block.TextLayout.HitTestTextRange(offset + found + 1, 1).First().Center;
                offset += value.Length;
            }
            else if (inline is InlineUIContainer)
            {
                offset++;
            }
        }
        throw new InvalidOperationException("no run holds " + text);
    }

    private static void Press(Window window, Control host, Point at, MouseButton button, RawInputModifiers modifiers)
    {
        var point = host.TranslatePoint(at, window)!.Value;
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static (Border Parent, LinkTextView View) Paragraph(Func<SwiftURL, KeyModifiers, bool>? hook)
    {
        var paragraph = new LinkParagraphView(Source);
        var parent = new Border { Child = paragraph };
        if (hook is not null) MarkdownView.SetChatLinkAction(parent, hook);
        return (parent, (LinkTextView)paragraph.Child!);
    }

    private static Button ButtonOf(PopoverItem item) => (Button)item.Child!;

    [Fact]
    public void A_right_click_with_the_chat_hook_set_still_shows_the_popover_and_the_hook_hears_nothing() => headless.OnUiThread(() =>
    {
        var heard = new List<string>();
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var (parent, view) = Paragraph((url, _) => { heard.Add(url.AbsoluteString); return true; });
        var window = Show(parent);
        try
        {
            var block = (SelectableTextBlock)view.Child!;
            Press(window, block, CharacterCenter(block, "link"), MouseButton.Right, RawInputModifiers.None);
            Assert.True(Assert.Single(recorder.Calls).Presented);
            Assert.Empty(heard);
            Assert.Empty(opened);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    });

    [Fact]
    public void A_click_off_the_link_never_reaches_the_hook() => headless.OnUiThread(() =>
    {
        var heard = new List<string>();
        var (parent, view) = Paragraph((url, _) => { heard.Add(url.AbsoluteString); return true; });
        var window = Show(parent);
        try
        {
            var block = (SelectableTextBlock)view.Child!;
            Press(window, block, CharacterCenter(block, "plain words"), MouseButton.Left, RawInputModifiers.None);
            Press(window, block, CharacterCenter(block, "plain words"), MouseButton.Left, RawInputModifiers.Control);
            Assert.Empty(heard);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_hook_that_declines_leaves_the_old_behaviour(bool control) => headless.OnUiThread(() =>
    {
        var seen = new List<KeyModifiers>();
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var (parent, view) = Paragraph((_, modifiers) => { seen.Add(modifiers); return false; });
        var window = Show(parent);
        try
        {
            var block = (SelectableTextBlock)view.Child!;
            Press(window, block, CharacterCenter(block, "link"), MouseButton.Left, control ? RawInputModifiers.Control : RawInputModifiers.None);
            Assert.Equal([control ? KeyModifiers.Control : KeyModifiers.None], seen);
            if (control)
            {
                Assert.True(Assert.Single(recorder.Calls).Presented); // the popover, as before
                Assert.Empty(opened);
            }
            else
            {
                Assert.Equal(["https://chat-route.invalid/article"], opened); // the default open, as before
                Assert.Empty(recorder.Calls);
            }
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    });

    [Fact]
    public void Markdown_outside_a_chat_has_no_hook_and_a_Ctrl_click_still_shows_the_popover() => headless.OnUiThread(() =>
    {
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var (parent, view) = Paragraph(null);
        var window = Show(parent);
        try
        {
            Assert.Null(MarkdownView.GetChatLinkAction(view));
            var block = (SelectableTextBlock)view.Child!;
            Press(window, block, CharacterCenter(block, "link"), MouseButton.Left, RawInputModifiers.Control);
            Assert.True(Assert.Single(recorder.Calls).Presented);
            Assert.Empty(opened);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    });

    [Fact]
    public void The_hook_is_inherited_by_everything_below_the_chat() => headless.OnUiThread(() =>
    {
        Func<SwiftURL, KeyModifiers, bool> hook = (_, _) => true;
        var inner = new Border();
        var outer = new Border { Child = new Border { Child = inner } };
        MarkdownView.SetChatLinkAction(outer, hook);
        var window = Show(outer);
        try
        {
            Assert.Same(hook, MarkdownView.GetChatLinkAction(inner));
            Assert.Null(MarkdownView.GetChatLinkAction(new Border()));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_popovers_Open_Link_goes_through_the_chat_hook_and_not_to_the_shell() => headless.OnUiThread(() =>
    {
        var heard = new List<(string, KeyModifiers)>();
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        var anchor = new Border { Width = 10, Height = 10 };
        MarkdownView.SetChatLinkAction(anchor, (url, modifiers) => { heard.Add((url.AbsoluteString, modifiers)); return true; });
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { }, anchor);
        content.HorizontalAlignment = HorizontalAlignment.Left;
        var window = Show(content);
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            ButtonOf((PopoverItem)stack.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([("https://example.com/x", KeyModifiers.None)], heard);
            Assert.Empty(opened);
        }
        finally
        {
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    });

    [Fact]
    public void The_popovers_Open_Link_falls_back_to_the_shell_when_the_hook_declines() => headless.OnUiThread(() =>
    {
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        var anchor = new Border { Width = 10, Height = 10 };
        MarkdownView.SetChatLinkAction(anchor, (_, _) => false);
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { }, anchor);
        content.HorizontalAlignment = HorizontalAlignment.Left;
        var window = Show(content);
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            ButtonOf((PopoverItem)stack.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["https://example.com/x"], opened);
        }
        finally
        {
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    });

    [Fact]
    public void The_chat_can_add_rows_to_the_popover_and_they_sit_between_Open_Link_and_Copy_Link() => headless.OnUiThread(() =>
    {
        var ran = 0;
        var asked = new List<string>();
        var anchor = new Border { Width = 10, Height = 10 };
        MarkdownView.SetChatLinkMenu(anchor, url =>
        {
            asked.Add(url.AbsoluteString);
            return [new LinkMenuItem("Open in Visus", "eye", () => ran++)];
        });
        var content = new LinkPopoverContent(SwiftURL.Create("C:/fake/project/index.html")!, null, _ => { }, anchor);
        content.HorizontalAlignment = HorizontalAlignment.Left;
        var window = Show(content);
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            Assert.Equal(3, stack.Children.Count);
            Assert.NotEmpty(asked); // the content builds its rows again when it appears
            Assert.All(asked, address => Assert.Equal(SwiftURL.Create("C:/fake/project/index.html")!.AbsoluteString, address));
            ButtonOf((PopoverItem)stack.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, ran);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Without_a_chat_menu_the_popover_has_only_its_own_rows() => headless.OnUiThread(() =>
    {
        var anchor = new Border { Width = 10, Height = 10 };
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { }, anchor);
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
        Assert.Equal(2, stack.Children.Count);
    });
}
