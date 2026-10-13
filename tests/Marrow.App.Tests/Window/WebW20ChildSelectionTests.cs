using Marrow.App.Tests.Window.Native;

namespace Marrow.App.Tests.Window;

[Trait("Decision", "ED-NEW-web-w20-3")]
public sealed class WebW20ChildSelectionTests
{
    [Fact]
    public void The_child_protocol_accepts_only_the_two_real_W20_facts()
    {
        var type = typeof(WebW20NativeTests);
        foreach (var name in new[]
        {
            nameof(WebW20NativeTests.Windows_UIA_exposes_the_Visus_trust_control_and_the_offline_Chromium_document),
            nameof(WebW20NativeTests.Injected_contrast_palettes_keep_the_address_focus_and_the_Windows_UIA_trust_name),
        }) Assert.Equal(type.FullName + "." + name, WindowManagerNativeChildProtocol.FactName(name, type));
        Assert.Throws<InvalidOperationException>(() => WindowManagerNativeChildProtocol.FactName("NotAnExistingFact", type));
        Assert.Throws<InvalidOperationException>(() => WindowManagerNativeChildProtocol.FactName(nameof(The_child_protocol_accepts_only_the_two_real_W20_facts), typeof(WebW20ChildSelectionTests)));
    }
}
