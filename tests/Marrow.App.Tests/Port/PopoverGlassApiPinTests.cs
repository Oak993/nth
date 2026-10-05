using System.Reflection;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

// ED-NEW-glass-g2: PopoverGlass reaches Avalonia.Win32's internals by name (the composited window, its blur sprite, the
// MicroCom interfaces). A mismatch fails safe, to the opaque panel, which no headless test would notice; this pin finds every
// one of those members in the Avalonia.Win32 the app ships with, so an upgrade that renames one fails here, loudly.
public sealed class PopoverGlassApiPinTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-glass-g2")]
    public void Every_Avalonia_Win32_internal_the_popover_glass_reads_is_there_in_the_pinned_version()
    {
        var win32 = Assembly.Load("Avalonia.Win32");
        Assert.Equal(new Version(12, 1, 2), new Version(win32.GetName().Version!.Major, win32.GetName().Version!.Minor, win32.GetName().Version!.Build));
        var api = typeof(PopoverGlass).GetNestedType("Api", BindingFlags.NonPublic)!;
        // Load itself, not the cached Current: the pin reads the assembly now, whatever an earlier test left in the cache.
        var load = api.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;
        var found = load.Invoke(null, null);
        var failure = api.GetProperty("LoadFailure", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        Assert.True(found is not null, "PopoverGlass cannot read Avalonia.Win32: " + failure);
        // Every interface method and property the glass calls by name.
        var calls = new (string Interface, string Member)[]
        {
            ("IVisual", "Parent"), ("IVisual", "SetIsVisible"), ("IVisual", "SetClip"), ("IVisual", "SetCompositeMode"), ("IVisual", "SetSize"),
            ("IVisual", "SetCenterPoint"), ("IVisual", "SetRotationAngleInDegrees"), ("IVisual", "SetOffset"), ("IVisual", "SetOpacity"),
            ("IVisual2", "SetRelativeSizeAdjustment"), ("IContainerVisual", "Children"), ("IVisualCollection", "InsertAbove"),
            ("IVisualCollection", "InsertAtTop"), ("IVisualCollection", "Remove"), ("ISpriteVisual", "SetBrush"), ("ICompositor", "CreateContainerVisual"),
            ("ICompositor", "CreateSpriteVisual"), ("ICompositor5", "CreateRoundedRectangleGeometry"), ("ICompositor6", "CreateGeometricClipWithGeometry"),
            ("ICompositionRoundedRectangleGeometry", "SetOffset"), ("ICompositionRoundedRectangleGeometry", "SetSize"),
            ("ICompositionRoundedRectangleGeometry", "SetCornerRadius"),
        };
        var missing = calls.Where(call =>
        {
            var type = win32.GetType("Avalonia.Win32.WinRT." + call.Interface);
            return type is null || type.GetMember(call.Member).Length != 1;
        }).Select(call => call.Interface + "." + call.Member).ToList();
        Assert.True(missing.Count == 0, "missing or ambiguous in Avalonia.Win32: " + string.Join(", ", missing));
    }
}
