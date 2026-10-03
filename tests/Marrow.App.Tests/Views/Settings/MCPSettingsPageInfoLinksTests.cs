using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPProofsMcpARound2Input;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// MCPSettingsPage.swift:1002-1020, the info popover's two links, pressed with real pointer input on the real catalog entries.
// The browser boundary is the MCPSetupLauncherScope recorder: nothing is ever opened.
[Collection("Headless")]
public sealed class MCPSettingsPageInfoLinksTests(HeadlessSession headless)
{
    private void Ui(Action<AppModel, TestRuntime, MCPSetupLauncherScope> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        using var browser = new MCPSetupLauncherScope();
        runtime.OnMain(() => { try { test(new AppModel(), runtime, browser); Dispatcher.UIThread.RunJobs(); } finally { MCPKeyPanel.Shared.Close(); } });
    });

    // The popover's view in the window whose launcher records (MCPSettingsPage.OpenURL asks the view's own top-level).
    private static MCPInfoPopover Show(MCPSetupLauncherScope browser, AppModel model, MCPCatalogEntry entry)
    {
        var info = new MCPInfoPopover(entry); AppEnvironment.SetAppModel(info, model);
        browser.MainWindow.Content = info; Dispatcher.UIThread.RunJobs(); browser.MainWindow.UpdateLayout(); return info;
    }

    private static string[] ChangeButtons(Control info) => Nodes<Button>(info).SelectMany(b => Texts(b)).Where(t => t.StartsWith("Change the", StringComparison.Ordinal)).ToArray();
    private static bool HasDocs(Control info) => Nodes<Button>(info).Any(b => Texts(b).Contains("Read the docs"));

    [Fact]
    [Trait("Req", "PR-MCP-213")] // App/Views/Settings/MCPSettingsPage.swift:1002-1020
    [Trait("Req", "UI-MCP-068")] // App/Views/Settings/MCPSettingsPage.swift:1002-1009
    [Trait("Req", "UI-MCP-069")] // App/Views/Settings/MCPSettingsPage.swift:1010-1020
    public void Info_popover_links_open_the_key_panel_for_the_first_optional_field_and_the_docs_url_in_the_browser()
    {
        Ui((model, _, browser) =>
        {
            // :1002 the change button is there for exactly the entries with a field that is not required: the catalogue has three.
            var withOptional = MCPCatalog.Entries.Where(e => e.Fields.Any(f => !f.IsRequired)).Select(e => e.Id).ToArray();
            Assert.Equal(new[] { "gitlab", "context7", "huggingface" }.Order(), withOptional.Order());
            foreach (var entry in MCPCatalog.Entries)
            {
                var info = Show(browser, model, entry); var expected = entry.Fields.FirstOrDefault(f => !f.IsRequired);
                // :1003 "Change the " + the first optional field's label lowercased + U+2026; nothing for any other entry.
                Assert.Equal(expected is null ? Array.Empty<string>() : new[] { "Change the " + expected.Label.ToLowerInvariant() + "\U00002026" }, ChangeButtons(info)); // U+2026 HORIZONTAL ELLIPSIS
                // :1010 a docs URL that parses gives the link: every catalogue entry has one.
                Assert.True(HasDocs(info), entry.Id);
            }
            Assert.Equal("Change the gitlab address\U00002026", Assert.Single(ChangeButtons(Show(browser, model, MCPCatalog.Entry("gitlab")!))));
            Assert.Equal("Change the api key\U00002026", Assert.Single(ChangeButtons(Show(browser, model, MCPCatalog.Entry("context7")!))));
            Assert.Equal("Change the access token\U00002026", Assert.Single(ChangeButtons(Show(browser, model, MCPCatalog.Entry("huggingface")!))));

            // :1004 pressing it opens the key panel for the entry, and the entry's key page when it has one (MCPKeyPanel.swift:12-19).
            foreach (var (id, keys) in new[] { ("gitlab", (string?)null), ("context7", "https://context7.com/dashboard"), ("huggingface", "https://huggingface.co/settings/tokens") })
            {
                var entry = MCPCatalog.Entry(id)!; var info = Show(browser, model, entry); var before = browser.Launcher.Requests.Count;
                Assert.False(KeyPanelVisible()); Click(Button(info, ChangeButtons(info).Single()));
                Assert.True(KeyPanelVisible(), id); Assert.Contains(entry.Fields[0].Label, Nodes<TextBlock>(Assert.IsAssignableFrom<Control>(PanelWindow(MCPKeyPanel.Shared)!.Content)).Select(t => t.Text));
                Assert.Equal(keys is null ? Array.Empty<Uri>() : new[] { new Uri(keys) }, browser.Launcher.Requests.Skip(before).ToArray()); MCPKeyPanel.Shared.Close();
            }

            // :1010-1018 the docs link opens the entry's docs URL in the default browser: no key panel, one request.
            foreach (var (id, docs) in new[] { ("gitlab", "https://docs.gitlab.com/user/model_context_protocol/mcp_server/"), ("context7", "https://github.com/upstash/context7"), ("huggingface", "https://github.com/huggingface/hf-mcp-server") })
            {
                var info = Show(browser, model, MCPCatalog.Entry(id)!); var before = browser.Launcher.Requests.Count;
                Click(Button(info, "Read the docs")); Assert.Equal(new[] { new Uri(docs) }, browser.Launcher.Requests.Skip(before).ToArray()); Assert.False(KeyPanelVisible());
            }

            // :1006-1008 and :1015-1018 the look: a plain button with no frame, callout 12 medium in the accent; the arrow caption2 10 bold in the accent, 5 apart.
            var look = Show(browser, model, MCPCatalog.Entry("context7")!);
            var change = Button(look, "Change the api key\U00002026"); Assert.Null(change.Background); Assert.True(double.IsNaN(change.Width)); Assert.True(double.IsNaN(change.Height));
            var changeText = Nodes<TextBlock>(change).Single(); Assert.Equal(12, changeText.FontSize); Assert.Equal(FontWeight.Medium, changeText.FontWeight); Colour(changeText.Foreground, Chrome.Accent, changeText);
            var docsLink = Button(look, "Read the docs"); Assert.Equal(5, Assert.IsType<StackPanel>(docsLink.Content).Spacing);
            var docsText = Nodes<TextBlock>(docsLink).Single(); Assert.Equal(12, docsText.FontSize); Assert.Equal(FontWeight.Medium, docsText.FontWeight); Colour(docsText.Foreground, Chrome.Accent, docsText);
            var arrow = Nodes<SwiftImage>(docsLink).Single(); Assert.Equal("arrow.up.right", arrow.SymbolName); Assert.Equal(10, SwiftImage.GetFont(arrow)!.Resolve().FontSize); Assert.Equal(FontWeight.Bold, SwiftImage.GetFont(arrow)!.Resolve().FontWeight); Colour(arrow.Foreground, Chrome.Accent, arrow);
            var parts = Assert.IsType<StackPanel>(docsLink.Content).Children; Assert.IsType<TextBlock>(parts[0]); Assert.IsType<SwiftImage>(parts[1]); // the arrow trails the text

            // :1010 and :1002 a custom server's converted entry has no docs URL and no optional field: neither link, whatever its fields.
            foreach (var custom in new[] { Custom(), Custom(2), Custom(1, MCPCustomServer.Kind.Http) })
            {
                var entry = custom.CatalogEntry(); Assert.Equal("", entry.DocsURL); var info = Show(browser, model, entry);
                Assert.Empty(ChangeButtons(info)); Assert.False(HasDocs(info));
            }
            // :1010 a docs URL that does not parse omits the link.
            Assert.False(HasDocs(Show(browser, model, MCPCatalog.Entry("gitlab")! with { DocsURL = "" })));
        });
    }

    private static void Pointer(Control target, RoutedEvent routed) => target.RaiseEvent(new PointerEventArgs(routed, target, new Pointer(1, PointerType.Mouse, true), target, default, 0, default, default));

    // MCPSettingsPage.swift:914-963 (the hover-open popover) with the info popover's own content: the pointer on the POPOVER keeps it open.
    [Fact]
    [Trait("Req", "UI-MCP-064")] // App/Views/Settings/MCPSettingsPage.swift:914-963,967-1024
    public void Popover_hover_keeps_the_info_popover_open_past_the_320_ms_close_and_the_click_toggles_it()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var scope = Presentation.Presenter.Override(new RecordingPresenter());
            var model = runtime.OnMain(() => new AppModel()); var info = new MCPInfoButton(MCPCatalog.Entry("gitlab")!);
            using var window = runtime.OnMain(() => Attach(info, model)); var hover = Nodes<MCPHoverInfoButton>(info).Single(); var button = Assert.Single(Nodes<Button>(info)); var popup = Assert.Single(Nodes<Popup>(info));
            // The pointer on the (i) opens it after 220 ms, not before.
            runtime.OnMain(() => Pointer(button, InputElement.PointerEnteredEvent)); runtime.AdvanceMilliseconds(219); Assert.False(Read<bool>(hover, "IsPresented")); runtime.AdvanceMilliseconds(1); Assert.True(Read<bool>(hover, "IsPresented"));
            var content = Assert.IsAssignableFrom<Control>(PopoverHost.Content(popup)); Assert.IsType<MCPInfoPopover>(content);
            // Leaving the (i) starts the 320 ms close; the pointer reaching the popover within it cancels it, and it stays open as long as the pointer is on it.
            runtime.OnMain(() => Pointer(button, InputElement.PointerExitedEvent)); runtime.AdvanceMilliseconds(200); Assert.True(Read<bool>(hover, "IsPresented"));
            runtime.OnMain(() => Pointer(content, InputElement.PointerEnteredEvent)); runtime.AdvanceMilliseconds(2000); Assert.True(Read<bool>(hover, "IsPresented"), "the popover closed under the pointer");
            // Leaving the popover closes it after 320 ms.
            runtime.OnMain(() => Pointer(content, InputElement.PointerExitedEvent)); runtime.AdvanceMilliseconds(319); Assert.True(Read<bool>(hover, "IsPresented")); runtime.AdvanceMilliseconds(1); Assert.False(Read<bool>(hover, "IsPresented"));
            // Going back to the (i) from the popover within the 320 ms also keeps it open.
            runtime.OnMain(() => Pointer(button, InputElement.PointerEnteredEvent)); runtime.AdvanceMilliseconds(220); Assert.True(Read<bool>(hover, "IsPresented"));
            runtime.OnMain(() => Pointer(content, InputElement.PointerEnteredEvent)); runtime.OnMain(() => Pointer(button, InputElement.PointerExitedEvent)); runtime.AdvanceMilliseconds(1000); Assert.True(Read<bool>(hover, "IsPresented"));
            runtime.OnMain(() => Pointer(content, InputElement.PointerExitedEvent)); runtime.OnMain(() => Pointer(button, InputElement.PointerEnteredEvent)); runtime.AdvanceMilliseconds(1000); Assert.True(Read<bool>(hover, "IsPresented"));
            // A click toggles it at once, whatever the hover.
            runtime.OnMain(() => Pointer(button, InputElement.PointerExitedEvent)); runtime.OnMain(() => Click(button)); Assert.False(Read<bool>(hover, "IsPresented")); runtime.OnMain(() => Click(button)); Assert.True(Read<bool>(hover, "IsPresented"));
            Dispatcher.UIThread.RunJobs();
        });
    }
}
