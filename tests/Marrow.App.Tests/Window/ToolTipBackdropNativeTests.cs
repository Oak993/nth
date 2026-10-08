using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window.Native;

[Collection("WindowManagerNative")]
public sealed class ToolTipBackdropNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Decision", "ED-NEW-T45")]
    public void The_real_Dark_and_Light_help_tags_attach_their_owned_compositor_blur_and_release_it_on_close()
    {
        native.Run(_ =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var target = native.OnUi(() =>
                {
                    var target = new Border { Width = 120, Height = 40 };
                    ToolTip.SetTip(target, "Test-owned help tag");
                    var window = new Avalonia.Controls.Window { Content = target, Width = 400, Height = 240, RequestedThemeVariant = variant };
                    native.Register(window);
                    window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((w, _) => new ContentPresenter { Content = w.Content });
                    window.Show(); window.UpdateLayout(); ToolTip.SetIsOpen(target, true);
                    return target;
                });
                var material = native.OnUi(() =>
                {
                    var property = (AttachedProperty<ToolTip?>)typeof(ToolTip).GetField("ToolTipProperty", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                    return Assert.Single(target.GetValue(property)!.GetVisualDescendants().OfType<ToolTipBackdrop>());
                });
                WindowManagerNativeMethods.Until(() => native.OnUi(() => material.Attached || material.Failure is not null), "The native help tag did not complete attachment", 15000);
                native.OnUi(() =>
                {
                    Assert.True(material.Attached, "Native help-tag attachment: " + material.Failure);
                    Assert.Null(material.Failure);
                    Assert.Equal(45, material.Sigma);
                    Assert.Equal(variant == ThemeVariant.Light ? 0.8 : 1, material.Saturation);
                    ToolTip.SetIsOpen(target, false);
                });
                WindowManagerNativeMethods.Until(() => native.OnUi(() => !material.Attached), "A closed help tag retained its native backdrop");
            }
        }, factType: typeof(ToolTipBackdropNativeTests));
    }
}
