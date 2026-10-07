// PORT-ONLY RR-37: publish the real Avalonia UIA fragment at the input HWND that Windows queries.
// A token marshalled for the visual HWND cannot simply be relayed as the input HWND's response.
using System.Reflection;
using Avalonia.Automation.Peers;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal sealed class SettingsShellAutomation(HostWindow window)
{
    private object? provider;
    private MethodInfo? publish;

    internal nint Return(nint input, nint wp, nint lp)
    {
        if (provider is null)
        {
            var assembly = Assembly.Load("Avalonia.Win32.Automation"); // separate pinned 12.1.2 provider assembly
            var node = assembly.GetType("Avalonia.Win32.Automation.AutomationNode", true)!;
            var get = node.GetMethod("GetOrCreate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            provider = get.Invoke(null, [ControlAutomationPeer.CreatePeerForElement(window)])!;
            publish = assembly.GetType("Avalonia.Win32.Automation.Interop.UiaCoreProviderApi", true)!
                .GetMethod("UiaReturnRawElementProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        }
        return (nint)publish!.Invoke(null, [input, wp, lp, provider])!;
    }

    internal void Release(nint input)
    {
        if (publish is not null) publish.Invoke(null, [input, (nint)0, (nint)0, null]);
        provider = null; publish = null; // release only this HWND's UIA map; the visual HWND owns the same fragment
    }
}
