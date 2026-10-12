using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-1")]
public sealed class VisusZoomPdfPersistenceTests(HeadlessSession headless)
{
    [Fact]
    public void Saved_profile_records_round_trip_without_private_or_unknown_profiles()
    {
        var store = new VisusSiteZoom();
        var a = WebProfiles.AgentProfileName(@"C:\nth-test\threads\a");
        var b = WebProfiles.AgentProfileName(@"C:\nth-test\threads\b");
        store.Set(WebProfiles.BrowseProfile, "https://example.test/path", 1.1);
        store.Set(WebProfiles.PreviewProfile, "https://example.test/", 1.25);
        store.Set(a, "https://example.test/", 1.5);
        store.Set(b, "https://example.test/", 1.75);
        store.Set(WebProfiles.PrivateProfile, "https://private.test/", 2);
        var json = store.ToJson();
        Assert.DoesNotContain("private.test", json, StringComparison.Ordinal);
        var read = VisusSiteZoom.FromJson(json);
        Assert.Equal(1.1, read.Get(WebProfiles.BrowseProfile, "https://EXAMPLE.test:443/other"));
        Assert.Equal(1.25, read.Get(WebProfiles.PreviewProfile, "https://example.test/"));
        Assert.Equal(1.5, read.Get(a, "https://example.test/"));
        Assert.Equal(1.75, read.Get(b, "https://example.test/"));
        Assert.Equal(1, read.Get(WebProfiles.PrivateProfile, "https://private.test/"));
        Assert.Equal(1, VisusSiteZoom.FromJson("{\"private\":{\"https://private.test/\":2},\"unknown\":{\"https://example.test/\":2}}").Get("unknown", "https://example.test/"));
    }

    [Fact]
    public void Zoom_survives_host_recreation_in_an_owned_data_folder() => headless.OnUiThread(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-w8c-zoom-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = new VisusRig())
            {
                VisusHost.DataFolder = folder;
                first.Page("https://example.test/one").SetZoom(1.5);
                first.Page("https://secret.test/", VisusTabKind.Private).SetZoom(2);
                VisusHost.Flush();
                Assert.DoesNotContain("secret.test", File.ReadAllText(Path.Combine(folder, "site-zoom.json")), StringComparison.Ordinal);
            }
            using var second = new VisusRig();
            VisusHost.DataFolder = folder;
            var tab = second.Page("https://example.test/two");
            Assert.Equal(1.5, tab.Zoom);
            Assert.Equal(1.5, VisusRig.Live(tab).Zoom);
            Assert.Equal(1, second.Page("https://secret.test/", VisusTabKind.Private).Zoom);
        }
        finally { VisusHost.DataFolder = null; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Fact]
    public void Zoom_is_applied_to_the_recreated_engine_after_runtime_restart() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var original = VisusRig.Live(tab);
        tab.SetZoom(1.5);
        VisusHost.Restart();
        var replacement = VisusRig.Live(tab);
        Assert.NotSame(original, replacement);
        Assert.Equal(1.5, replacement.Zoom);
        Assert.Equal(1.5, tab.Zoom);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pdf_failure_is_reported_and_a_later_export_can_retry(bool throws) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab);
        var notices = new List<string>(); visus.Session.Notice += (_, text) => notices.Add(text);
        page.PdfHandler = (_, _) => throws ? Task.FromException<bool>(new InvalidOperationException("fixture")) : Task.FromResult(false);
        var files = new FakeTabulaFiles { SaveAnswer = _ => @"C:\nth-test\chosen.pdf" };
        var body = new VisusPaneBody(visus.Session) { Files = files };
        var save = body.SavePdfAsync(); HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2)); save.GetAwaiter().GetResult();
        Assert.Equal(new[] { "Could not save PDF" }, notices);
        page.PdfHandler = (_, _) => Task.FromResult(true);
        save = body.SavePdfAsync(); HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2)); save.GetAwaiter().GetResult();
        Assert.Equal(2, files.Saves.Count);
        Assert.Single(notices);
    });

    [Fact]
    public void Only_one_pdf_picker_is_opened_while_a_request_is_pending() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var files = new DeferredVisusFiles();
        var body = new VisusPaneBody(visus.Session) { Files = files };
        var save = body.SavePdfAsync();
        Assert.False(save.IsCompleted);
        var duplicate = body.SavePdfAsync();
        Assert.True(duplicate.IsCompletedSuccessfully);
        Assert.Equal(1, files.Saves);
        files.Answer.SetResult(@"C:\nth-test\chosen.pdf");
        HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2)); save.GetAwaiter().GetResult();
        Assert.Equal(1, VisusRig.Live(tab).Log.Count(entry => entry.StartsWith("PrintToPdf ", StringComparison.Ordinal)));
    });
}
