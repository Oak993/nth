using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window.Native;

// CU-W20: WRITE ONLY. The lead schedules each Fact alone with native.lock; the page is answered entirely in memory.
// These captures cross Windows UIA, unlike the managed peer fixtures stored by W20's headless tests.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w20-3")]
public sealed class WebW20NativeTests(WindowManagerNativeSession native)
{
    private static IEnumerable<PcUiNode> Flatten(PcUiNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    private static async Task Capture(UiaReader reader, nint handle, string name, Action<PcUiNode> accept)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var tree = await reader.Tree(handle, cancellation.Token).ConfigureAwait(false);
        Assert.NotNull(tree);
        var folder = Path.Combine(AppContext.BaseDirectory, "w20-native-uia");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(
            Flatten(tree).Select(node => new { node.Role, node.Name, node.Value, node.Framework, node.States, node.Actions, node.Bounds }),
            new JsonSerializerOptions { WriteIndented = true }), cancellation.Token).ConfigureAwait(false);
        accept(tree);
    }

    [Fact]
    public void Windows_UIA_exposes_the_Visus_trust_control_and_the_offline_Chromium_document() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The offline W20 page did not load.", 20000);
            var handle = native.OnUi(() => rig.Window.TryGetPlatformHandle()!.Handle);
            WindowManagerNativeMethods.RequireOwner(handle);
            var trust = native.OnUi(() => AutomationProperties.GetName(rig.Body.Toolbar.Trust));
            using var reader = new UiaReader();
            var capture = Capture(reader, handle, "visus", tree =>
            {
                var nodes = Flatten(tree).ToArray();
                Assert.Contains(nodes, node => node.Name == trust && node.Role == "button");
                Assert.Contains(nodes, node => node.Name == "W8c fixture");
                Assert.Contains(nodes, node => node.Framework == "Chrome");
            });
            WindowManagerNativeMethods.Until(() => capture.IsCompleted, "Windows UIA did not complete the W20 snapshot.", 10000);
            Assert.True(capture.IsCompletedSuccessfully, capture.Exception?.ToString());
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebW20NativeTests));

    [Fact]
    public void Injected_contrast_palettes_keep_the_address_focus_and_the_Windows_UIA_trust_name() => native.Run(subject =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The offline W20 page did not load.", 20000);
            var handle = native.OnUi(() => rig.Window.TryGetPlatformHandle()!.Handle);
            WindowManagerNativeMethods.RequireOwner(handle);
            Assert.True(WindowManagerNativeMethods.SetForegroundWindow(handle));
            native.OnUi(() => rig.Body.Toolbar.Address.Box.Focus(NavigationMethod.Tab));
            var thread = WindowManagerNativeMethods.GetWindowThreadProcessId(handle, out _);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Body.Toolbar.Address.Box.IsFocused), "Address focus was not acquired.");
            var focus = WindowManagerNativeMethods.Gui(thread).Focus;
            Assert.NotEqual(0, focus);
            WindowManagerNativeMethods.RequireOwner(focus);
            Assert.Equal(handle, WindowManagerNativeMethods.GetAncestor(focus, 2));
            var trust = native.OnUi(() => AutomationProperties.GetName(rig.Body.Toolbar.Trust));
            using var reader = new UiaReader();
            foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
            {
                native.OnUi(() => EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette));
                Assert.Equal(focus, WindowManagerNativeMethods.Gui(thread).Focus);
                native.OnUi(() => Assert.True(rig.Body.Toolbar.Address.Box.IsFocused));
                var capture = Capture(reader, handle, palette.IsDark ? "contrast-black" : "contrast-white", tree =>
                    Assert.Contains(Flatten(tree), node => node.Name == trust && node.Role == "button"));
                WindowManagerNativeMethods.Until(() => capture.IsCompleted, "Contrast UIA capture did not complete.", 10000);
                Assert.True(capture.IsCompletedSuccessfully, capture.Exception?.ToString());
            }
            native.OnUi(() => EnvironmentValues.SetAccessibilityHighContrast(rig.Window, null));
            Assert.Equal(focus, WindowManagerNativeMethods.Gui(thread).Focus);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebW20NativeTests));
}
