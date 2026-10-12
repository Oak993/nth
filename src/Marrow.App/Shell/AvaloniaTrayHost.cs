// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4; unit AS-6; ED-NEW-dextra-as6-10). No Swift twin.
// The real tray icon, over Avalonia's TrayIcon. Every call may come from any thread and is posted to the UI thread; the menu is
// rebuilt only when AppTray hands it a changed model, and the icon's frames are swapped only when the state changes. The icon
// follows the taskbar's own theme (a light taskbar gets the dark mark). Never used by automated tests (a test binds a fake
// ITrayHost); AppTrayNativeTests are the lead's to run on a real desktop with the window closed.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Win32;
using SkiaSharp;

namespace Marrow.App.Tray;

public sealed class AvaloniaTrayHost : ITrayHost
{
    private TrayIcon? icon;
    private TrayState? drawn;
    private bool lightTaskbar;
    private bool disposed;

    public void Show(TrayModel model) => Dispatcher.UIThread.Post(() => Apply(model), DispatcherPriority.Background);

    public void Hide() => Dispatcher.UIThread.Post(Remove, DispatcherPriority.Background);

    public void Dispose()
    {
        disposed = true;
        Dispatcher.UIThread.Post(Remove, DispatcherPriority.Background);
    }

    private void Apply(TrayModel model)
    {
        if (disposed) return;
        var created = icon is null;
        icon ??= new TrayIcon { ToolTipText = model.ToolTip };
        var light = LightTaskbar();
        if (drawn != model.State || lightTaskbar != light)
        {
            using var stream = new MemoryStream(TrayGlyph.Ico(model.State, light ? new SKColor(0x1C, 0x1C, 0x1E) : new SKColor(0xEC, 0xEC, 0xEE)));
            icon.Icon = new WindowIcon(stream);
            drawn = model.State;
            lightTaskbar = light;
        }
        icon.ToolTipText = model.ToolTip;
        var menu = new NativeMenu();
        var first = true;
        foreach (var group in model.Groups)
        {
            if (!first) menu.Items.Add(new NativeMenuItemSeparator());
            first = false;
            foreach (var item in group.Items)
            {
                var entry = new NativeMenuItem(item.Label) { IsEnabled = item.Enabled };
                entry.Click += (_, _) => item.Run();
                menu.Items.Add(entry);
            }
        }
        icon.Menu = menu;
        icon.IsVisible = true;
        if (created && Application.Current is { } app)
        {
            var icons = TrayIcon.GetIcons(app) ?? new TrayIcons();
            icons.Add(icon);
            TrayIcon.SetIcons(app, icons);
        }
    }

    private void Remove()
    {
        if (icon is not { } held) return;
        icon = null;
        drawn = null;
        held.IsVisible = false;
        if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons) icons.Remove(held);
        held.Dispose();
    }

    // The taskbar's own theme (read only): a light taskbar gets the dark mark.
    private static bool LightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
