using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/SubagentPanel.swift (B3 52bc792), :268-356 body.
[Collection("Headless")]
public sealed class SubagentPanelSurfaceTests(HeadlessSession headless)
{
    private static SubagentPanel MakePanel(ChatThread thread, Project project, Size size)
    {
        var constructor = typeof(SubagentPanel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 8);
        return (SubagentPanel)constructor.Invoke([thread, size, project.Path, project.Name,
            (Action<Size>)(_ => { }), (Action)(() => { }), (Action)(() => { }), (Func<TimelineScrollState, Control>)(_ => new Border())]);
    }

    private void Run(Action<SubagentPanel, Size> check, bool head = true)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness(withRuntime: false);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var project = AppModelSamples.Project("Panel", h.NewFolder("panel"));
            var thread = AppModelSamples.Thread(project) with { Hydra = head ? AppModelSamples.Head(HydraHeadInfo.Status.Running) : null };
            h.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            Marrow.Services.Store.DocumentPrefetch.Shared.Warm([thread.Id], urgent: true)?.Completion.GetAwaiter().GetResult();
            var manager = new WindowManager();
            using var shared = WindowManager.OverrideShared(manager);
            var model = manager.Model;
            model.Settings.HydraShowsHeadDetails = false;
            var runtime = model.Runtime(thread.Id);
            var size = new Size(400, 280);
            var panel = MakePanel(thread, project, size);
            var host = new Border { Child = panel, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            AppEnvironment.SetAppModel(host, model);
            var window = new Avalonia.Controls.Window { Content = host, Width = 900, Height = 700 };
            AppEnvironment.SetWindowLiveResize(host, new WindowLiveResize(window));
            window.Show();
            for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            try { check(panel, size); }
            finally
            {
                window.Close();
                runtime.CancelPersistence();
                var preparation = new FileIndex().Prepare(project.Path);
                HeadlessSession.RunDispatcherUntil(preparation, AppModelHarness.Bound);
                preparation.GetAwaiter().GetResult();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRA-230")] // App/Views/Chat/SubagentPanel.swift:268-296 the panel's chat box is the bottom inset, under the top strip
    public void The_panels_own_chat_box_sits_at_its_bottom_edge_below_the_top_strip(bool head) => Run((panel, size) =>
    {
        var composer = panel.GetVisualDescendants().OfType<ComposerArea>().Single();
        var top = composer.TranslatePoint(default, panel)!.Value.Y;
        Assert.Equal(size.Height, top + composer.Bounds.Height, 1);
        Assert.True(top > Marrow.UI.Chrome.Chrome.ChromeTopPadding + Marrow.UI.Chrome.Chrome.CapsuleHeight, $"the chat box starts at {top}, inside the top strip");
    }, head);
}
