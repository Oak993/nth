using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Services.Web;
using Marrow.Core.Web;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window.Native;

// CU-W8c: write but NEVER run as an implementing agent. Lead takes native.lock and schedules each Fact alone.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w8c-1")]
public sealed class VisusFindZoomPrintNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    public void Find_opens_a_focused_top_level_bar_below_the_toolbar_and_above_the_page() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            native.OnUi(() =>
            {
                Assert.True(Enum.TryParse<VisusCommand>("Find", out var find));
                Assert.True(rig.Session.Perform(find));
            });
            var property = typeof(VisusPaneBody).GetProperty("FindBar");
            Assert.NotNull(property);
            var bar = native.OnUi(() => Assert.IsAssignableFrom<Control>(property.GetValue(rig.Body)));
            var boxProperty = bar.GetType().GetProperty("Box");
            Assert.NotNull(boxProperty);
            var box = native.OnUi(() => Assert.IsType<TextBox>(boxProperty.GetValue(bar)));
            WindowManagerNativeMethods.Until(() => native.OnUi(() => box.IsFocused), "The find query never received focus.");
            var popup = native.OnUi(() => TopLevel.GetTopLevel(box));
            Assert.NotNull(popup);
            Assert.NotSame(rig.Window, popup);
            var popupHandle = native.OnUi(() => popup.TryGetPlatformHandle()!.Handle);
            WindowManagerNativeMethods.RequireOwner(popupHandle);
            Assert.True(WindowManagerNativeMethods.IsWindowVisible(popupHandle));
            var thread = WindowManagerNativeMethods.GetWindowThreadProcessId(popupHandle, out var popupProcess);
            Assert.Equal((uint)Environment.ProcessId, popupProcess);
            WindowManagerNativeMethods.Until(() => WindowManagerNativeMethods.GetAncestor(WindowManagerNativeMethods.Gui(thread).Focus, 2) == popupHandle,
                "The OS keyboard focus did not belong to the find popup.");
            var centre = native.OnUi(() => box.PointToScreen(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2)));
            Assert.Equal(popupHandle, WindowManagerNativeMethods.RootAt(new NativeWindowPoint(centre.X, centre.Y)));
            var toolbarBottom = native.OnUi(() => rig.Body.Toolbar.PointToScreen(new Point(0, rig.Body.Toolbar.Bounds.Height)));
            // The popup's HWND includes invisible shadow room. Measure the visible card, not the query's padded inset.
            var card = native.OnUi(() => Assert.Single(bar.GetVisualAncestors().OfType<PopoverCard>()));
            WindowManagerNativeMethods.Until(() => native.OnUi(() =>
            {
                var pose = card.RenderTransform?.Value ?? Matrix.Identity;
                return card.Opacity >= .999 && Math.Abs(pose.M11 - 1) < .001 && Math.Abs(pose.M22 - 1) < .001
                    && Math.Abs(pose.M31) < .001 && Math.Abs(pose.M32) < .001;
            }), "The find card did not settle before its visible placement was measured.");
            var cardTop = native.OnUi(() => card.PointToScreen(default));
            var scale = native.OnUi(() => rig.Window.RenderScaling);
            Assert.InRange((cardTop.Y - toolbarBottom.Y) / scale, 9, 11);
            var toolbarRight = native.OnUi(() => rig.Body.Toolbar.PointToScreen(new Point(rig.Body.Toolbar.Bounds.Width, 0)));
            var cardRight = native.OnUi(() => card.PointToScreen(new Point(card.Bounds.Width, 0)));
            Assert.InRange((toolbarRight.X - cardRight.X) / scale, 23, 25);
            native.OnUi(() => rig.Session.NewTab(VisusTabKind.Browse));
            WindowManagerNativeMethods.Until(() => !WindowManagerNativeMethods.IsWindowVisible(popupHandle), "Switching tabs left the find popup visible.");
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(VisusFindZoomPrintNativeTests));

    [Fact]
    public void Real_find_counts_case_matches_and_moves_to_the_next_match() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            var find = native.OnUi(() => rig.Tab.View!.Engine!.FindAsync("price", false, CancellationToken.None));
            Assert.Equal(new WebFindResult(3, 0), find.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            native.OnUi(() => rig.Tab.View!.Engine!.StopFind());
            var exact = native.OnUi(() => rig.Tab.View!.Engine!.FindAsync("price", true, CancellationToken.None));
            Assert.Equal(new WebFindResult(2, 0), exact.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            var changed = new TaskCompletionSource<WebFindResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Event added by W8c. Reflection keeps the test-first draft compilable against W8a.
            var resultEvent = typeof(IWebEngineView).GetEvent("FindResultChanged");
            Assert.NotNull(resultEvent);
            EventHandler<WebFindResult> onResult = (_, result) => { if (result.ActiveMatchIndex == 1) changed.TrySetResult(result); };
            native.OnUi(() => resultEvent.AddEventHandler(rig.Tab.View!.Engine!, onResult));
            native.OnUi(() => rig.Tab.View!.Engine!.FindNext());
            Assert.Equal(new WebFindResult(2, 1), changed.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            native.OnUi(() => resultEvent.RemoveEventHandler(rig.Tab.View!.Engine!, onResult));
            native.OnUi(() => rig.Tab.View!.Engine!.StopFind());
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(VisusFindZoomPrintNativeTests));

    [Fact]
    public void Print_command_opens_the_owned_system_dialog_and_cancel_returns_to_the_page() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        nint dialog = 0;
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            var handle = native.OnUi(() => rig.Window.TryGetPlatformHandle()!.Handle);
            var thread = WindowManagerNativeMethods.GetWindowThreadProcessId(handle, out var printProcess);
            Assert.Equal((uint)Environment.ProcessId, printProcess);
            var command = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Do not block the driver behind PrintDlgEx's modal loop: only the fixture thread is enumerated.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { command.TrySetResult(rig.Session.Perform(VisusCommand.Print)); }
                catch (Exception e) { command.TrySetException(e); }
            });
            WindowManagerNativeMethods.Until(() => (dialog = VisusNativeRig.PrintDialogOn(thread)) != 0, "The system print dialog did not appear.", 10000);
            WindowManagerNativeMethods.Post(dialog, 0x0111, 2); // WM_COMMAND / IDCANCEL; no printing.
            WindowManagerNativeMethods.Until(() => !WindowManagerNativeMethods.IsWindowVisible(dialog), "Cancel did not dismiss the print dialog.");
            Assert.True(command.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            Assert.True(WindowManagerNativeMethods.IsWindowVisible(handle));
        }
        finally
        {
            if (dialog != 0 && WindowManagerNativeMethods.IsWindow(dialog)) WindowManagerNativeMethods.Post(dialog, 0x0111, 2);
            native.OnUi(rig.Dispose);
        }
    }, factType: typeof(VisusFindZoomPrintNativeTests));

    [Fact]
    public void Real_print_to_pdf_writes_a_pdf_from_the_fixture_page() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            var path = Path.Combine(rig.Folder, "fixture.pdf");
            var print = native.OnUi(() => rig.Tab.View!.Engine!.PrintToPdfAsync(path, CancellationToken.None));
            Assert.True(print.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult());
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 1000);
            Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(VisusFindZoomPrintNativeTests));

    [Fact]
    public void Real_zoom_follows_navigation_between_sites() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            native.OnUi(() => rig.Tab.SetZoom(1.5));
            Assert.Equal(1.5, native.OnUi(() => rig.Tab.View!.Engine!.Zoom));
            native.OnUi(() => rig.Tab.Open("https://nth-other.invalid/page"));
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.LastSuccessfulUrl == "https://nth-other.invalid/page"), "Second fixture origin did not load successfully.", 20000);
            Assert.Equal(1, native.OnUi(() => rig.Tab.View!.Engine!.Zoom));
            native.OnUi(() => rig.Tab.Open("https://nth-w8c.invalid/returned"));
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.LastSuccessfulUrl == "https://nth-w8c.invalid/returned"), "Original fixture origin did not reload successfully.", 20000);
            Assert.Equal(1.5, native.OnUi(() => rig.Tab.View!.Engine!.Zoom));
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(VisusFindZoomPrintNativeTests));

    [Fact]
    public void Real_cdp_fallback_preserves_block_breaks_inline_text_and_literal_queries() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            var find = native.OnUi(() => new WebFindFallback(rig.Tab.View!.Engine!));
            WebFindResult Search(string query) => native.OnUi(() => find.StartAsync(query, true, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.Equal(new WebFindResult(0, -1), Search("pricecross"));
            Assert.Equal(new WebFindResult(1, 0), Search("cross node match"));
            var replace = native.OnUi(() => rig.Tab.View!.Engine!.ExecuteScriptAsync("document.body.innerHTML='<p>a.b aXb</p><p>left<br>right</p><p>İ i</p><p>a   b</p><p>a<b> </b>  b</p><pre>a   b</pre><p style=\"white-space:pre-line\">a   b</p><pre>tail<b> </b></pre><p>end</p><p hidden>a.b</p><script>a.b</script>'", CancellationToken.None));
            replace.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.Equal(new WebFindResult(1, 0), Search("a.b"));
            Assert.Equal(new WebFindResult(0, -1), Search("leftright"));
            Assert.Equal(new WebFindResult(1, 0), Search("İ"));
            Assert.Equal(new WebFindResult(3, 0), Search("a b"));
            Assert.Equal(new WebFindResult(1, 0), Search("a   b"));
            Assert.Equal(new WebFindResult(1, 0), Search("tail "));
            native.OnUi(() => find.StopAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(VisusFindZoomPrintNativeTests));

    [Fact]
    public void Real_cdp_fallback_finds_literal_text_across_nodes_and_clears_highlights() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new VisusNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The fixture page did not load.", 20000);
            var type = typeof(IWebEngineView).Assembly.GetType("Marrow.Services.Web.WebFindFallback");
            Assert.NotNull(type);
            dynamic find = native.OnUi(() => Activator.CreateInstance(type, rig.Tab.View!.Engine!)!);
            Task<WebFindResult> search = native.OnUi(() => (Task<WebFindResult>)find.StartAsync("price", false, CancellationToken.None));
            Assert.Equal(new WebFindResult(3, 0), search.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            Task<WebFindResult> previous = native.OnUi(() => (Task<WebFindResult>)find.MoveAsync(true, CancellationToken.None));
            Assert.Equal(new WebFindResult(3, 2), previous.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            Task stop = native.OnUi(() => (Task)find.StopAsync(CancellationToken.None));
            stop.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            search = native.OnUi(() => (Task<WebFindResult>)find.StartAsync("cross node match", true, CancellationToken.None));
            Assert.Equal(new WebFindResult(1, 0), search.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
            stop = native.OnUi(() => (Task)find.StopAsync(CancellationToken.None));
            stop.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            var marks = native.OnUi(() => rig.Tab.View!.Engine!.ExecuteScriptAsync("CSS.highlights.size", CancellationToken.None));
            Assert.Equal("0", marks.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(VisusFindZoomPrintNativeTests));
}
