using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (re-check of unit/blitz-frames at db0909d01). PR-CHAT-171 / UI-CHAT-073: the row's glass is drawn by the GlassEffectContainer's one material visual, which
// GlassMaterial inserts into the composition children of the container's visual parent, just before the container's own visual. A material that holds the regions but
// is not in the composition tree draws nothing. PRE-EXISTING: the census below finds the same at 653d3ee68 for each piece's own material (six of six out of the tree).
[Collection("Headless")]
public sealed class ChatViewGlassPlacementReviewTests(HeadlessSession headless)
{
    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;

    private static CompositionVisual? MaterialOf(Control container)
    {
        var table = Material.GetField("attachments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { container, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!, "the container has a material attachment");
        return (CompositionVisual?)args[1]!.GetType().GetField("material", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(args[1]);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1120-1123
    [Trait("Req", "UI-CHAT-073")]
    public void REVIEW_The_chrome_rows_one_glass_pass_is_in_the_composition_tree_beside_the_container()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = true;
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var window = new AvaloniaWindow { Content = row, Width = 1400, Height = 300 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                for (var pass = 0; pass < 6; pass++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
                var container = Assert.Single(row.GetVisualDescendants().OfType<GlassEffectContainer>());
                var material = MaterialOf(container);
                Assert.NotNull(material);
                var parent = (CompositionContainerVisual)ElementComposition.GetElementVisual((Visual)container.GetVisualParent()!)!;
                var own = ElementComposition.GetElementVisual(container)!;
                Assert.Contains(material!, parent.Children); // drawn: in the tree
                Assert.Equal(parent.Children.IndexOf(own) - 1, parent.Children.IndexOf(material!)); // just before the container: under the row
            });
        });
    }
}
