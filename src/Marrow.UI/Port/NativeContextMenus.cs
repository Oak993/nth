// PORT-ONLY. SwiftUI's `.contextMenu { ... }` and AppKit's right-click menu are an NSMenu: the system menu, run in its own
// modal tracking loop, its item's action called synchronously when it is chosen. The port's sites build Avalonia
// MenuItem/Separator lists and set Control.ContextMenu (SidebarView project and thread rows, ParagraphTextPresenter's
// Copy/Select All, RowActionMenuButtons), on the assumption that Avalonia's ContextMenu is a native menu host. It is not:
// it is a templated managed popup, and this app carries no theme package, so it opened EMPTY (probe 2026-10-01: IsOpen
// true, 0x0, items never attached).
//
// ENGINEERING DECISION (proposal ED-229 (1), E:\Droppy\work\reference-video\ED_PROPOSAL_system_controls.md): the Windows
// counterpart of the system menu is the system menu. One class handler takes every ContextRequested (right-click,
// Shift+F10, the Menu key) on a control that has a ContextMenu, lets the menu rebuild its items as it would on opening
// (its Opening event, which every site uses for the source's fresh per-open read), and presents those items through
// `Presenter`: the native Win32 popup menu (TrackPopupMenuEx, TPM_RETURNCMD: modal like NSMenu) in the app, a recording
// presenter in tests (as R-TEST-7 does for popovers). The chosen item gets a normal MenuItem Click (and its Command).
// Recorded deviations: SF Symbol icons and a destructive item's red title are not drawn (a Win32 menu shows text,
// check marks, separators, submenus and the grey of a disabled item); the menu follows the Windows menu theme, not the
// app's dark/light choice; Opening's Cancel, item visibility, toggle flipping and Opened/Closed are not honoured (no site
// uses them). The site code is unchanged. Not yet a numbered ED.
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>One row of a context menu, as the presenter shows it.</summary>
/// <param name="Title">The row's text (a MenuItem header's text).</param>
/// <param name="IsEnabled">False draws the row disabled.</param>
/// <param name="IsChecked">A check mark (a MenuItem with a ToggleType that is checked).</param>
/// <param name="IsSeparator">A divider line; the other fields are unused.</param>
/// <param name="Children">A submenu's rows; empty for a plain row.</param>
/// <param name="Choose">What choosing the row does: the MenuItem's Click (and Command).</param>
public sealed record ContextMenuEntry(string Title, bool IsEnabled, bool IsChecked, bool IsSeparator, IReadOnlyList<ContextMenuEntry> Children, Action? Choose);

/// <summary>Shows a context menu's rows at a point of a control and runs the chosen row.</summary>
public interface IContextMenuPresenter
{
    /// <param name="target">The control that was right-clicked.</param>
    /// <param name="at">The point in <paramref name="target"/>'s coordinates.</param>
    /// <param name="entries">The rows, as the menu built them for this open.</param>
    void Present(Control target, Point at, IReadOnlyList<ContextMenuEntry> entries);
}

/// <summary>PORT-ONLY. Context menus as the system menu (see the file note).</summary>
public static class NativeContextMenus
{
    private static int installed;

    /// <summary>The presenter: the Win32 popup menu, or a test's recorder.</summary>
    public static Ambient<IContextMenuPresenter> Presenter { get; } = new(() => new Win32ContextMenuPresenter());

    /// <summary>Installs the class handler once per process (called from WithPortControlThemes).</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) == 1) return;
        Control.ContextRequestedEvent.AddClassHandler<Control>(OnContextRequested);
    }

    private static void OnContextRequested(Control control, ContextRequestedEventArgs e)
    {
        if (e.Handled || control.ContextMenu is not { } menu) return;
        e.Handled = true; // the managed popup would open empty
        RaiseOpening(menu);
        var entries = Entries(menu.Items);
        if (entries.Count == 0) return;
        var at = e.TryGetPosition(control, out var point) ? point : new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        Presenter.Current.Present(control, at, entries);
    }

    // The sites rebuild their items in ContextMenu.Opening (the source's fresh per-open read); Avalonia raises it only from
    // its own Open, so it is raised here the same way, with a cancellable argument the sites ignore.
    private static void RaiseOpening(ContextMenu menu)
    {
        var field = typeof(ContextMenu).GetField("Opening", BindingFlags.Instance | BindingFlags.NonPublic);
        (field?.GetValue(menu) as CancelEventHandler)?.Invoke(menu, new CancelEventArgs());
    }

    /// <summary>The rows of a MenuItem/Separator list.</summary>
    public static IReadOnlyList<ContextMenuEntry> Entries(IEnumerable items)
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var item in items)
        {
            switch (item)
            {
                case Separator:
                    entries.Add(new ContextMenuEntry("", true, false, true, [], null));
                    break;
                case MenuItem menuItem:
                    var target = menuItem;
                    entries.Add(new ContextMenuEntry(
                        Title(menuItem.Header),
                        menuItem.IsEnabled,
                        menuItem.ToggleType != MenuItemToggleType.None && menuItem.IsChecked,
                        false,
                        Entries(menuItem.Items),
                        () =>
                        {
                            target.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                            if (target.Command is { } command && command.CanExecute(target.CommandParameter)) command.Execute(target.CommandParameter);
                        }));
                    break;
            }
        }
        // A divider never leads, trails or doubles (NSMenu draws none there either).
        var tidy = new List<ContextMenuEntry>();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator && (tidy.Count == 0 || tidy[^1].IsSeparator)) continue;
            tidy.Add(entry);
        }
        while (tidy.Count > 0 && tidy[^1].IsSeparator) tidy.RemoveAt(tidy.Count - 1);
        return tidy;
    }

    // A header is a string, a TextBlock, or a row holding a glyph and a TextBlock (RowActionMenuButtons :374-377).
    private static string Title(object? header) => header switch
    {
        null => "",
        string text => text,
        TextBlock block => block.Text ?? block.Inlines?.Text ?? "",
        ILogical logical => logical.GetSelfAndLogicalDescendants().OfType<TextBlock>().Select(b => b.Text ?? b.Inlines?.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "",
        _ => header.ToString() ?? "",
    };
}

/// <summary>The native Win32 popup menu (user32 TrackPopupMenuEx), modal, returning the chosen command.</summary>
internal sealed class Win32ContextMenuPresenter : IContextMenuPresenter
{
    private const uint MF_STRING = 0x0000, MF_GRAYED = 0x0001, MF_CHECKED = 0x0008, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002, TPM_RETURNCMD = 0x0100;

    public void Present(Control target, Point at, IReadOnlyList<ContextMenuEntry> entries)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TopLevel.GetTopLevel(target)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return; // no native window (a headless session): nothing to show the menu on
        var screen = target.PointToScreen(at);
        var commands = new List<Action?>();
        var menu = IntPtr.Zero;
        var chosen = 0;
        try
        {
            menu = Build(entries, commands);
            // TrackPopupMenuEx's documented contract: the owner must be the foreground window or a click outside the
            // menu does not dismiss it, and a posted WM_NULL afterwards lets the next click reach the window.
            SetForegroundWindow(handle);
            chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screen.X, screen.Y, handle, IntPtr.Zero);
            PostMessageW(handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu); // destroys the submenus with it
        }
        // The chosen row runs after the menu is gone, and only while its control is still on screen: the UI thread kept
        // dispatching during the modal loop, so the row (a thread, a message) may have left meanwhile.
        if (chosen > 0 && chosen <= commands.Count && TopLevel.GetTopLevel(target) is not null) commands[chosen - 1]?.Invoke();
    }

    private static IntPtr Build(IReadOnlyList<ContextMenuEntry> entries, List<Action?> commands)
    {
        var menu = CreatePopupMenu();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                continue;
            }
            var flags = (entry.IsEnabled ? 0u : MF_GRAYED) | (entry.IsChecked ? MF_CHECKED : 0u);
            var title = entry.Title.Replace("&", "&&", StringComparison.Ordinal); // '&' marks a mnemonic in a Win32 menu
            if (entry.Children.Count > 0)
            {
                AppendMenuW(menu, MF_POPUP | flags, (UIntPtr)(nuint)Build(entry.Children, commands), title);
                continue;
            }
            commands.Add(entry.Choose);
            AppendMenuW(menu, MF_STRING | flags, (UIntPtr)(uint)commands.Count, title);
        }
        return menu;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    private const uint WM_NULL = 0x0000;

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
