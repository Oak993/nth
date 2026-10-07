// PORT-ONLY: T11 step 2. The two menus of the pages, in the app's own popover menu (PopoverMenu with PopoverItem rows, as the chat's menus are): the ⋯ of an account, a service and a pill, and the value on the right of a grouped row. Both
// hand the chosen entry's key to the page; the popover closes first.
using Avalonia;
using Marrow.Core.Port;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal static class MenuPopover
{
    private sealed class Presented : MainActorModel
    {
        private bool isPresented;
        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    /// <summary>The menu's rows for <paramref name="entries"/>: each a PopoverItem, a separator a PopoverDivider; a pressed entry calls <paramref name="pick"/> after <paramref name="close"/>.</summary>
    internal static Control Build(IReadOnlyList<MenuEntry> entries, Action<string> pick, Action close)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        foreach (var entry in entries)
        {
            if (entry.Separator) { stack.Children.Add(new PopoverDivider()); continue; }
            var key = entry.Key;
            stack.Children.Add(new PopoverItem(entry.Label, () => { close(); pick(key); }, detail: entry.Hint, isChecked: entry.Checked ? true : null, isEnabled: !entry.Disabled, isDestructive: entry.Quiet && key == "signout"));
        }
        return stack;
    }

    /// <summary>A bare ⋯ button that opens <paramref name="entries"/> (read each time it opens, so a menu is never stale).</summary>
    internal static Control More(string name, string id, Func<IReadOnlyList<MenuEntry>> entries, Action<string> pick)
    {
        var state = new Presented();
        var button = LibraryButtons.Glyph("ellipsis", name, id, () => state.IsPresented = !state.IsPresented);
        var popover = PlainControls.Popover(button, new Binding<bool>(() => state.IsPresented, v => state.IsPresented = v),
            () => new PopoverMenu(() => Build(entries(), pick, () => state.IsPresented = false)));
        return new Panel { Width = 22, Height = 22, Children = { button, popover } };
    }

    /// <summary>A menu on an anchor that is opened from code (a ⋯ menu entry that opens a second menu). Put <c>Host</c> in the tree beside the anchor.</summary>
    internal static (Control Host, Action Open) Anchored(Control anchor, Func<IReadOnlyList<MenuEntry>> entries, Action<string> pick)
    {
        var state = new Presented();
        var popover = PlainControls.Popover(anchor, new Binding<bool>(() => state.IsPresented, v => state.IsPresented = v),
            () => new PopoverMenu(() => Build(entries(), pick, () => state.IsPresented = false)));
        return (new Panel { Width = 0, Height = 0, Children = { popover } }, () => state.IsPresented = true);
    }

    /// <summary>The value of a grouped row: its words and a small chevron, opening the row's options with the current one ticked and the recommended one marked.</summary>
    internal static Control Value(string name, string id, CzCrossfadeText words, Func<IReadOnlyList<MenuOption>> options, Action<string> pick)
    {
        var state = new Presented();
        var chevron = LibraryUi.Glyph("chevron.up.chevron.down", 10, ChromeTokens.SecondaryText, SwiftFont.Weight.Semibold);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { words, chevron } };
        var button = PlainControls.Button(new Border { Padding = new Thickness(8, 4), Child = row, Background = Avalonia.Media.Brushes.Transparent }, () => state.IsPresented = !state.IsPresented);
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Named(name, id);
        var popover = PlainControls.Popover(button, new Binding<bool>(() => state.IsPresented, v => state.IsPresented = v),
            () => new PopoverMenu(() => Build([.. options().Select(o => new MenuEntry(o.Value, o.Label, o.Recommended ? MultiAccountWords.Recommended : null, o.Chosen))], pick, () => state.IsPresented = false)));
        return new Panel { Children = { button, popover } };
    }
}
