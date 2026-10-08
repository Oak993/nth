using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Marrow.UI.Port;

// PORT-ONLY ED-NEW-T45: Avalonia 12.1.2 combines hooks as a multicast delegate, whose LAST return wins even when an
// earlier callback set handled. A later observer returning zero erased caption hit-test results. Each port-owned
// top-level has one dispatcher, stopping at the first consumer and retaining its return value.
public static class NativeWindowHooks
{
    private static readonly ConditionalWeakTable<TopLevel, List<Win32Properties.CustomWndProcHookCallback>> hooks = new();

    // Input proxies must filter synthetic leave messages before observers alter the real owner's hover state.
    public static void AddFirst(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Entries(window).Insert(0, hook);

    public static void Add(TopLevel window, Win32Properties.CustomWndProcHookCallback hook) => Entries(window).Add(hook);

    private static List<Win32Properties.CustomWndProcHookCallback> Entries(TopLevel window) =>
        hooks.GetValue(window, owner =>
        {
            Win32Properties.AddWndProcHookCallback(owner, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
                Dispatch(owner, hwnd, message, wp, lp, ref handled));
            return [];
        });

    public static void Remove(TopLevel window, Win32Properties.CustomWndProcHookCallback hook)
    {
        if (hooks.TryGetValue(window, out var entries)) entries.Remove(hook);
    }

    internal static nint Dispatch(TopLevel window, nint hwnd, uint message, nint wp, nint lp, ref bool handled)
    {
        if (!hooks.TryGetValue(window, out var entries)) return 0;
        foreach (var hook in entries)
        {
            var result = hook(hwnd, message, wp, lp, ref handled);
            if (handled) return result;
        }
        return 0;
    }
}
