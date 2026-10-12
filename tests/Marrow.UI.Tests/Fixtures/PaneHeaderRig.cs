using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// A workspace pane header (web W3a) on a real headless window at a width the test sets, with the pane actions
/// Tabula's header carries (DESIGN 6.3: the version chip, Ask to change, Copy, Save, Open in Visus, Export), each
/// recording its own invocation.
/// </summary>
public sealed class PaneHeaderRig : IDisposable
{
    public const string LongTitle = "Release notes for the spring update";

    public PaneHeaderRig(PaneKind kind = PaneKind.Artifacts, double width = 600, bool withActions = true)
    {
        Header = new PaneHeader(kind) { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Header.SetTitle(LongTitle, "Markdown");
        if (withActions)
        {
            Actions =
            [
                new PaneHeaderAction("Version", () => Invoked.Add("Version"), Text: "v3", Menu: true),
                new PaneHeaderAction("Ask to change", () => Invoked.Add("Ask to change"), Text: "Ask to change"),
                new PaneHeaderAction("Copy", () => Invoked.Add("Copy"), PaneGlyphKind.Artifacts, Shortcut: "Ctrl+Shift+C"),
                new PaneHeaderAction("Save", () => Invoked.Add("Save"), PaneGlyphKind.Changes),
                new PaneHeaderAction("Open in Visus", () => Invoked.Add("Open in Visus"), PaneGlyphKind.Browser),
                new PaneHeaderAction("Export", () => Invoked.Add("Export"), PaneGlyphKind.App),
            ];
            Header.SetActions(Actions);
        }
        Root = new Panel { Background = Brushes.Transparent, Children = { Header } };
        Window = HeadlessPopupHost.OverlayWindow(Root);
        Window.Width = 1200;
        Window.Height = 400;
        Window.Show();
        Layout();
    }

    public PaneHeader Header { get; }

    public IReadOnlyList<PaneHeaderAction> Actions { get; } = [];

    public List<string> Invoked { get; } = [];

    public Panel Root { get; }

    public Window Window { get; }

    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public void SetWidth(double width)
    {
        Header.Width = width;
        Layout();
    }

    /// <summary>A control's rectangle in the header's coordinates.</summary>
    public Rect BoxOf(Control control) => new(control.TranslatePoint(default, Header) ?? default, control.Bounds.Size);

    /// <summary>The trailing controls drawn now (chip, actions, More, Expand, Close).</summary>
    public IReadOnlyList<Control> TrailingShown()
    {
        var items = new List<Control>();
        if (Header.WorkingChip.IsVisible) items.Add(Header.WorkingChip);
        items.AddRange(Header.ActionViews.Where(view => view.IsVisible));
        if (Header.MoreButton.IsVisible) items.Add(Header.MoreButton);
        if (Header.ExpandButton.IsVisible) items.Add(Header.ExpandButton);
        items.Add(Header.CloseButton);
        return items;
    }

    /// <summary>The title part's rectangle: the title, or the tab strip when the cell has tabs.</summary>
    public Rect TitleBox => BoxOf(Header.TabStrip.IsVisible ? Header.TabStrip : Header.TitleBlock);

    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Runs the More menu's item named <paramref name="title"/>.</summary>
    public void RunMenuItem(string title)
    {
        var menu = Header.BuildMenu();
        var item = menu.GetLogicalDescendants().OfType<PopoverItem>().Single(row => row.GetLogicalDescendants().OfType<MiddleTruncatingText>().Any(text => text.FullText == title));
        Click(item.GetLogicalDescendants().OfType<Button>().First());
        Runtime.RunUntilIdle(); // a popover row runs its action as a main-actor task (Popovers.swift :273)
        Layout();
    }

    /// <summary>The More menu's row titles, as the menu draws them.</summary>
    public IReadOnlyList<string> MenuRows() =>
        Header.BuildMenu().GetLogicalDescendants().OfType<PopoverItem>()
            .Select(row => row.GetLogicalDescendants().OfType<MiddleTruncatingText>().First().FullText).ToList();

    public TestRuntime Runtime { get; } = new();

    public void Dispose()
    {
        Window.Close();
        Runtime.Dispose();
    }
}
