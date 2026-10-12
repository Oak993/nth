using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Web;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W10, the first-time question for a link from a chat (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11 "The
// first-time prompt: 'Open links in Visus or your default browser?' with 'Remember' checked"; ED-NEW-web-w10-3). No Swift counterpart. A
// sheet over the chat in nth's glass, drawn in the window like the other small sheets: the question, where the link goes (its site, so
// the reader sees which link asked), Remember (checked), and Cancel, Default browser and Visus. The sheet takes the keyboard while it
// shows: Tab moves between the controls, Space presses the focused one, Esc cancels and the focus goes back to where it was. Closing it
// any other way (the chat going away) is a cancel too. Nothing opens until the reader answers.

namespace Marrow.App.Port.Web;

/// <summary>The sheet's content: the question and its answers over a choice.</summary>
public sealed class ChatLinkSheetView : Decorator
{
    public const string RememberLabel = "Remember";
    public const string CancelLabel = "Cancel";
    public const string DefaultBrowserLabel = "Default browser";
    public const string VisusLabel = "Visus";

    /// <summary>The sheet's width; the question wraps inside it and a long site name trims.</summary>
    public const double SheetWidth = 400;

    public ChatLinkSheetView(ChatLinkChoice choice, Action dismiss)
    {
        var title = LibraryUi.Text(ChatLinkChoice.Title, 15, weight: SwiftFont.Weight.Semibold, wrap: true);
        title.Named(ChatLinkChoice.Title, "chat-link-title");
        var site = LibraryUi.Text(WebPolicy.SourceName(choice.Url.AbsoluteString), 12, secondary: true, trim: true);
        site.Named("Link to " + site.Text, "chat-link-site");

        Remember = new CheckBox { Content = LibraryUi.Text(RememberLabel, 13), IsChecked = choice.Remember };
        ChatDialogControls.Checkbox(Remember);
        Remember.Named("Remember this choice for links from chats", "chat-link-remember");
        Remember.IsCheckedChanged += (_, _) => choice.Remember = Remember.IsChecked == true;

        CancelButton = LibraryButtons.Make(CancelLabel, () => { choice.Cancel(); dismiss(); }, ButtonLook.Plain, "chat-link-cancel");
        DefaultBrowserButton = LibraryButtons.Make(DefaultBrowserLabel, () => { choice.Choose(false); dismiss(); }, ButtonLook.Plain, "chat-link-default-browser");
        VisusButton = LibraryButtons.Make(VisusLabel, () => { choice.Choose(true); dismiss(); }, ButtonLook.Primary, "chat-link-visus");
        CancelButton.Named("Cancel, open nothing", "chat-link-cancel");
        DefaultBrowserButton.Named("Open this link in your default browser", "chat-link-default-browser");
        VisusButton.Named("Open this link in Visus", "chat-link-visus");

        // Wrapping keeps all three buttons reachable at a large text scale or in a long translation.
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 8, LineSpacing = 8, Children = { CancelButton, DefaultBrowserButton, VisusButton } };
        var stack = new StackPanel { Spacing = 14, Children = { new StackPanel { Spacing = 3, Children = { title, site } }, Remember, buttons } };

        var surface = new Border { Child = stack, Padding = new Thickness(20), Width = SheetWidth }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        Child = surface;
        AutomationProperties.SetName(this, ChatLinkChoice.Title);
        AutomationProperties.SetAutomationId(this, "chat-link-sheet");
    }

    public CheckBox Remember { get; }

    public Button CancelButton { get; }

    public Button DefaultBrowserButton { get; }

    public Button VisusButton { get; }
}

/// <summary>The sheet's layer in the window: a wash over everything and the sheet at the centre.</summary>
internal sealed class ChatLinkSheetLayer : Panel
{
    private const double Reach = 3000;
    private readonly Border wash = new() { IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x52, 0, 0, 0)) };

    internal ChatLinkSheetLayer(ChatLinkSheetView sheet)
    {
        Sheet = sheet;
        ClipToBounds = false;
        Children.Add(wash);
        Children.Add(sheet);
    }

    internal ChatLinkSheetView Sheet { get; }

    protected override Size MeasureOverride(Size availableSize)
    {
        wash.Measure(new Size(Reach * 2, Reach * 2));
        Sheet.Measure(availableSize);
        return Sheet.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        wash.Arrange(new Rect(finalSize.Width / 2 - Reach, finalSize.Height / 2 - Reach, Reach * 2, Reach * 2));
        Sheet.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}

public static class ChatLinkSheet
{
    /// <summary>
    /// Shows the question over <paramref name="anchor"/>'s window. It closes when the choice is answered by any means, and dismissing it
    /// (Esc, the window going away) cancels the choice.
    /// </summary>
    public static void Show(ChatLinkChoice choice, Control? anchor)
    {
        Action? close = null;
        choice.Finished += () => close?.Invoke();
        ChatSheetPresentation.Show(anchor, dismiss =>
        {
            close = dismiss;
            return new ChatLinkSheetLayer(new ChatLinkSheetView(choice, dismiss));
        }, onDismiss: choice.Cancel, inWindow: true, still: anchor is not null && LibraryUi.Still(anchor));
    }
}
