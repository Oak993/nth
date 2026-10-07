// PORT-ONLY: multi-account step 3a. Whether movement is stilled for a control: the window's Reduce Motion (EnvironmentValues) or the system's. The system's is a Windows call; where there is none (a Linux test host) it says no.
using Avalonia.Controls;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal static class CapsuleMotion
{
    public static bool Reduce(Control owner)
    {
        if (EnvironmentValues.GetAccessibilityReduceMotion(owner)) return true;
        try { return ChromeTokens.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return true; } // nothing to ask: hold still
    }
}
