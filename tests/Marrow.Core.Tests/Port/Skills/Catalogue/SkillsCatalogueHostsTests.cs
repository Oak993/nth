using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-1: the host allow-list. ONDEMAND-PLAN.md 3.2: "HTTPS only. The first URL must be on the catalogue's host list (www.python.org, nodejs.org,
// download.documentfoundation.org, github.com, raw.githubusercontent.com, files.pythonhosted.org, registry.npmjs.org, cdn.playwright.dev)".
// The list is code (the downloader of SK-3 reads it), the catalogue only names URLs; nothing here opens a connection.
[Trait("Spec", "DL-1")]
public sealed class SkillsCatalogueHostsTests
{
    private static IEnumerable<(string Id, string Field, string Url)> AllUrls(SkillsCatalogue catalogue) =>
        catalogue.Items.SelectMany(i => new[] { (i.Id, "sourceUrl", i.SourceUrl), (i.Id, "licenceUrl", i.LicenceUrl) }
            .Concat(i.Artifacts.Select(a => (i.Id, "artifact " + a.Name, a.Url))))
            .Where(t => t.Item3 is not null).Select(t => (t.Item1, t.Item2, t.Item3!));

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_allow_list_is_the_eight_hosts_the_plan_names_and_no_other()
    {
        Assert.Equal(
            ["cdn.playwright.dev", "download.documentfoundation.org", "files.pythonhosted.org", "github.com", "nodejs.org", "raw.githubusercontent.com", "registry.npmjs.org", "www.python.org"],
            CatalogueHosts.Allowed.OrderBy(h => h, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_url_in_the_embedded_catalogue_is_https_on_an_allowed_host()
    {
        var urls = AllUrls(CatalogueFixtures.Real).ToList();
        Assert.NotEmpty(urls);
        foreach (var (id, field, url) in urls) Assert.True(field == "licenceUrl" ? CatalogueHosts.IsAllowedLicence(url) : CatalogueHosts.IsAllowed(url), $"{id} {field}: {url}");
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Rule", "R-TEST-4")]
    public void A_licence_only_host_is_accepted_for_a_licence_url_alone_and_the_gate_refuses_it_anywhere_else()
    {
        // ED-NEW-dl-13: the plan's eight hosts stay the list for every URL; gitlab.freedesktop.org (the Poppler project's own home) may name a licence file and nothing else.
        Assert.Equal(["gitlab.freedesktop.org"], CatalogueHosts.LicenceOnly);
        const string Copying = "https://gitlab.freedesktop.org/poppler/poppler/-/raw/poppler-26.09.0/COPYING";
        Assert.False(CatalogueHosts.IsAllowed(Copying));
        Assert.True(CatalogueHosts.IsAllowedLicence(Copying));
        Assert.True(CatalogueHosts.IsAllowedLicence("https://raw.githubusercontent.com/openai/codex/2739e828581c3deee59a270cd740675dc278ed14/LICENSE"));
        foreach (var bad in new[] { "http://gitlab.freedesktop.org/x", "https://evil.gitlab.freedesktop.org/x", "https://gitlab.freedesktop.org.evil.example/x", "https://gitlab.freedesktop.org:8443/x",
            "https://u@gitlab.freedesktop.org/x", "https://gitlab.com/x", "https://evil.example/x" })
            Assert.False(CatalogueHosts.IsAllowedLicence(bad), bad);
        var real = CatalogueFixtures.Real;
        Assert.False(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "T5", i => i with { LicenceUrl = Copying })), CatalogueRules.Host, "T5"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "A1", i => i with { SourceUrl = "https://gitlab.freedesktop.org/poppler/poppler" })), CatalogueRules.Host, "A1"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "T4", i => i with { Artifacts = i.Artifacts.Select(a => a with { Url = Copying }).ToList() })), CatalogueRules.Host, "T4"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "T4", i => i with { LicenceUrl = "https://gitlab.com/poppler/poppler/-/raw/poppler-26.09.0/COPYING" })), CatalogueRules.Host, "T4"));
    }

    [Theory]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("https://github.com/anthropics/skills")]
    [InlineData("https://GITHUB.COM/anthropics/skills")]
    [InlineData("https://raw.githubusercontent.com/openai/codex/2739e828581c3deee59a270cd740675dc278ed14/LICENSE")]
    [InlineData("https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip")]
    [InlineData("https://cdn.playwright.dev/builds/cft/153.0.8010.12/win64/chrome-win64.zip")]
    [InlineData("https://github.com:443/anthropics/skills")]
    public void These_urls_are_accepted(string url) => Assert.True(CatalogueHosts.IsAllowed(url));

    [Theory]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("http://github.com/anthropics/skills")]
    [InlineData("ftp://github.com/x")]
    [InlineData("file:///C:/x")]
    [InlineData("https://evil.example/github.com")]
    [InlineData("https://github.com.evil.example/x")]
    [InlineData("https://evilgithub.com/x")]
    [InlineData("https://sub.github.com/x")]
    [InlineData("https://nodejs.org@evil.example/x")]
    [InlineData("https://user:pw@github.com/x")]
    [InlineData("https://github.com:8443/x")]
    [InlineData("https://github.com./x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://pypi.org/project/pip/")]
    [InlineData("//github.com/x")]
    [InlineData("github.com/x")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://")]
    public void These_urls_are_refused(string url) => Assert.False(CatalogueHosts.IsAllowed(url));

    [Fact]
    [Trait("Feature", "SK-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_gate_refuses_a_url_off_the_list_in_any_field_and_names_the_item()
    {
        var real = CatalogueFixtures.Real;
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "A1", i => i with { SourceUrl = "https://gitlab.com/anthropics/skills" })), CatalogueRules.Host, "A1"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "A1", i => i with { LicenceUrl = "http://github.com/x/LICENSE" })), CatalogueRules.Host, "A1"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "T1", i => i with { Artifacts = i.Artifacts.Select(a => a with { Url = "https://mirror.example/python.zip" }).ToList() })), CatalogueRules.Host, "T1"));
        Assert.True(CatalogueFixtures.Fires(CatalogueGate.Check(CatalogueFixtures.Change(real, "T1", i => i with { Artifacts = i.Artifacts.Select(a => a with { Url = "https://www.python.org@evil.example/python.zip" }).ToList() })), CatalogueRules.Host, "T1"));
    }
}
