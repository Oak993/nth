using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.UI.Theme;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234): OpenRouter on the generic Settings -> Providers surface, with no new
// screen or control. The page lists a section for every provider (SettingsView.swift:585-614), and a key provider's section
// shows the status, the masked key field with its eye, the "Stored in your Credential Manager" line naming where to get a
// key, and a "Get a ... key" link (:646-727) - the rows DeepSeek has, for OpenRouter's own name, host and page. The window
// is headless; the HTTP client is put behind a handler that refuses every connection, so nothing can leave the process.
[Collection("Headless")]
public sealed class SettingsProviderOpenRouterTests(HeadlessSession headless)
{
    private const ProviderKind OpenRouter = ProviderKind.OpenRouter;

    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x is MiddleTruncatingText middle ? middle.FullText : x.Text ?? "").ToArray();
    private static void Sync() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static ChromeRow Row(Control view, string title) => Nodes<ChromeRow>(view).Single(row => Text(row).FirstOrDefault() == title);

    private static void Seed(AppModel model, ProviderKind provider, ProviderStatus status) =>
        typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[provider] = status));

    private void WithView(Func<Control> make, Action<Control, AppModel> check)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var refused = new InProcessHttpHandler(); // no answer scripted: any request is a refused connection
            using var transport = URLSession.Transport.Override(refused);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
            var model = new AppModel();
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            var view = make();
            host.Child = view;
            var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
            try
            {
                window.Show();
                for (var i = 0; i < 5; i++) { window.UpdateLayout(); Sync(); }
                check(view, model);
                Assert.Empty(refused.Received); // the section asked nothing of the network
            }
            finally
            {
                window.Close();
                Sync();
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_page_lists_a_section_for_OpenRouter_among_the_providers() => WithView(() => new ProvidersSettingsPage(), (view, _) =>
    {
        var sections = Nodes<ProviderSettingsSection>(view);
        Assert.Equal(16, sections.Length); // OD-0114 / ED-410 appends Local through existing provider plumbing
        Assert.Contains(sections, section => Text(section).Contains("OpenRouter"));
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_section_has_the_masked_key_field_with_its_eye_the_key_link_and_no_binary_path() => WithView(() => new ProviderSettingsSection(OpenRouter, Binding<bool>.Constant(true)), (view, _) =>
    {
        var field = Assert.Single(Nodes<TextBox>(view)); // the key field alone: a key provider has no binary path
        Assert.Equal(200, field.Width);
        Assert.Equal('•', field.PasswordChar);
        Assert.DoesNotContain(Nodes<ChromeRow>(view), row => Text(row).FirstOrDefault() == "Binary path");
        var keyRow = Row(view, "API key");
        Assert.Contains("Stored in your Credential Manager. Get one at openrouter.ai/keys.", Text(keyRow));
        var eye = Nodes<Button>(view).Single(x => Equals(ToolTip.GetTip(x), "Show the API key"));
        Click(eye); Sync();
        Assert.Equal('\0', field.PasswordChar);
        Assert.Contains("OpenRouter needs an API key. Nothing to install.", Text(Row(view, "Get a key")));
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_key_link_opens_the_page_where_a_key_is_made() => WithView(() => new ProviderSettingsSection(OpenRouter, Binding<bool>.Constant(true)), (view, _) =>
    {
        SwiftURL? opened = null;
        using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened = url; return true; });

        Click(Nodes<Button>(view).First(x => Text(x).Contains("Get an OpenRouter key")));

        Assert.Equal(OpenRouter.InstallURL, opened);
        Assert.Equal("https://openrouter.ai/keys", opened!.AbsoluteString);
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_field_shows_the_stored_OpenRouter_key_and_not_another_providers() => WithView(() => new ProviderSettingsSection(OpenRouter, Binding<bool>.Constant(true)), (view, model) =>
    {
        var field = Assert.Single(Nodes<TextBox>(view));
        Assert.Equal("", field.Text);

        model.Settings.DeepseekAPIKeyInput = "sk-deepseek-made-up";
        Sync();
        Assert.Equal("", field.Text); // DeepSeek's key is not OpenRouter's

        model.Settings.OpenRouterAPIKeyInput = "sk-or-made-up";
        Sync();
        Assert.Equal("sk-or-made-up", field.Text);
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_configured_key_shows_the_native_API_line_with_the_OpenRouter_host() => WithView(() => new ProviderSettingsSection(OpenRouter, Binding<bool>.Constant(true)), (view, model) =>
    {
        Seed(model, OpenRouter, new ProviderStatus { Version = "API", ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) });
        Sync();

        var texts = Text(view);
        Assert.Contains("Signed in", texts);
        Assert.Contains("Native API · openrouter.ai/api/v1", texts);
        Assert.DoesNotContain(Nodes<ChromeRow>(view), row => Text(row).FirstOrDefault() == "Get a key"); // installed: the link row is gone
    });

    [Fact]
    [Trait("Decision", "ED-234")]
    public void A_provider_with_no_limits_and_no_credits_shows_no_usage_block() => WithView(() => new ProviderSettingsSection(OpenRouter, Binding<bool>.Constant(true)), (view, model) =>
    {
        Seed(model, OpenRouter, new ProviderStatus { Version = "API", ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) });
        Sync();

        Assert.Empty(Nodes<Marrow.App.Views.Usage.PlanLimitsView>(view));
        Assert.Empty(Nodes<Marrow.App.Views.Usage.CreditsView>(view));
    });
}
