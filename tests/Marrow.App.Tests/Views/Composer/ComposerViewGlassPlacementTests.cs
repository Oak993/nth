using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :23 (GlassEffectContainer(spacing: 8)), :52-55. ED-NEW-glass-firstchild.
// The composer's container is the only child of the area's padding Border, so it is its parent's first child. GlassMaterial inserted a material only
// when its index differed from the owner's index minus one, which a material not yet in the parent also satisfies for a first child: the pill's glass
// was never drawn. This mounts the real ComposerView and reads the container's material from the composition tree.
[Collection("Headless")]
public sealed class ComposerViewGlassPlacementTests(HeadlessSession headless)
{
    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;

    private static object? AttachmentOf(Control control)
    {
        var table = Material.GetField("attachments", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { control, null };
        return (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)! ? args[1] : null;
    }
    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 6; pass++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-002")] // ComposerView.swift:23-31: the 8 point container is drawn (partial proof: its material, not the pill pixels)
    public void The_composers_container_is_its_parents_first_child_and_its_material_is_in_the_composition_tree_just_before_it() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var view = h.Main.OnMain(() => { var area = new ComposerArea(h.Runtime, workingDirectory: null); AppEnvironment.SetAppModel(area, h.App); return area; });
        var host = new Border { Child = view, Width = 600, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        AppEnvironment.SetAppModel(host, h.App);
        var window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        for (var round = 0; round < 3; round++) h.Main.OnMain(() => Settle(window));
        h.Main.OnMain(() =>
        {
            var container = Assert.Single(view.GetVisualDescendants().OfType<Control>(), c => SwiftGlass.GetContainerSpacing(c) == 8);
            var parent = Assert.IsAssignableFrom<Visual>(container.GetVisualParent());
            Assert.Same(container, parent.GetVisualChildren().First()); // the premise: it is the first child
            var attachment = AttachmentOf(container);
            Assert.NotNull(attachment);
            var material = (CompositionVisual?)attachment!.GetType().GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attachment);
            Assert.NotNull(material);
            var composition = (CompositionContainerVisual)ElementComposition.GetElementVisual(parent)!;
            Assert.Contains(material!, composition.Children);
            Assert.Equal(composition.Children.IndexOf(ElementComposition.GetElementVisual(container)!) - 1, composition.Children.IndexOf(material!));
            var state = attachment.GetType().GetField("previous", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attachment);
            Assert.NotNull(state); // it has been sent regions to draw
            Assert.NotEmpty((Array)state!.GetType().GetProperty("Regions")!.GetValue(state)!);
            window.Close();
        });
    });
}
