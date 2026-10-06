using System.Reflection;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Import;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Tests.Port.Skills.Import;

namespace Marrow.App.Tests.Views.Settings;

// SK-18 (pre-merge review C1): the Skills page of the APP, through the production seam with no test model standing in: the host is a real SkillsHost on a temp root and the home is a temp folder shaped like other apps' layouts.
// Auto-detection (OD-0118 addendum) must run there: scan on page open, the quiet line, listening while the page shows, a dismissal kept on disk.
[Collection("Headless")]
public sealed class ImportDefaultSeamTests(HeadlessSession headless)
{
    private static IDisposable HomeOf(string path)
    {
        var property = typeof(LoginEnvironment).GetProperty("HomeForTesting", BindingFlags.Static | BindingFlags.NonPublic)!;
        var ambient = property.GetValue(null)!;
        return (IDisposable)ambient.GetType().GetMethod("Override")!.Invoke(ambient, [path])!;
    }

    private static SkillsHost Open(string root) => SkillsHost.Open(root, new SkillsHostOptions
    {
        Catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [] }, Protection = new OpenProtection(), InstallLock = new OpenProtection(), ClashSource = _ => [],
    });

    private void Run(Action<LibraryPageHarness, ImportOffer, string> check, bool fill = true)
    {
        using var home = new ForeignHome();
        if (fill)
        {
            home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
            home.Skill(".codex/skills", "gamma", "gamma", "Gamma skill.");
        }
        var root = Path.Combine(home.Root, "app-data");
        Directory.CreateDirectory(root);
        using var host = Open(root);
        using var boundHost = SkillsSeams.Host.Override(host);
        using var boundHome = HomeOf(home.Home);
        var offer = SkillsPageSeams.MakeDefaultImports(); // exactly what the Ambient's default calls
        SkillsPageSeams.Imports.SetProcessDefault(offer);
        try { LibraryPageHarness.Run(headless, LibrarySection.Skills, h => check(h, offer, root), defaultImports: true); }
        finally { SkillsPageSeams.Imports.SetProcessDefault(new ImportOffer(null)); }
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_production_seam_has_the_detector_and_the_page_finds_the_apps_by_itself() => Run((h, offer, _) =>
    {
        Assert.NotNull(offer.Model);
        Assert.NotNull(offer.Model!.Detector);
        h.Until(() => h.All().OfType<Marrow.App.Port.Skills.Import.ImportArea>().Single().LineOpen);
        Assert.Contains(ImportWords.Found(2, [ImportWords.ClaudeCode, ImportWords.Codex]), h.Words());
        Assert.True(offer.Model.Detector!.Watching);
        Assert.Empty(offer.Model.Controller.Imported);
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Dismissing_the_line_is_kept_on_disk_by_the_production_seam_and_leaving_the_page_stops_the_listening()
    {
        ImportOffer? seen = null;
        string? where = null;
        Run((h, offer, root) =>
        {
            seen = offer;
            h.Until(() => h.All().OfType<Marrow.App.Port.Skills.Import.ImportArea>().Single().LineOpen);
            Assert.True(h.Press("import-found-dismiss"));
            h.Until(() => !h.All().OfType<Marrow.App.Port.Skills.Import.ImportArea>().Single().LineOpen);
            where = Directory.EnumerateFiles(root, "dismissed.json", SearchOption.AllDirectories).SingleOrDefault();
            Assert.NotNull(where);
        });
        Assert.False(seen!.Model!.Detector!.Watching);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void With_nothing_found_the_page_shows_no_line_and_the_button_is_still_there() => Run((h, offer, _) =>
    {
        h.Wait(offer.Model!.Detector!.RefreshAsync());
        Assert.False(h.All().OfType<Marrow.App.Port.Skills.Import.ImportArea>().Single().LineOpen);
        Assert.NotNull(h.TryFind("import-skills"));
    }, fill: false);
}
