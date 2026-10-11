// PORT-ONLY: ED-NEW-dl-13 (the lead's 2026-10-11 decision: a plugin set's licences may be recorded per plugin; nth shows each plugin's own). The terms step and the installer of such a set.
// Marrow-made fixtures only; no vendor text, no network (the licence files come through a scripted seam), no process.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Terms;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Install;

namespace Marrow.Services.Tests.Port.Skills.Terms;

[Trait("Feature", "RR-14")]
[Trait("Decision", "ED-NEW-dl-13")]
public sealed class PluginLicenceTermsTests
{
    private const string One = "Marrow-made licence of plugin one.\n";
    private const string Two = "Marrow-made licence of plugin two.\n";
    private const string Beta = "Marrow-made licence of the skill beta.\n";
    private const string Skill = "---\nname: alpha\n---\nOffline fixture\n";

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static CatalogueFile File(string path, string text) => new() { Path = path, Size = Encoding.UTF8.GetByteCount(text), Sha256 = Sha(text) };

    private static CatalogueItem Plugin(TermsClass terms = TermsClass.Proprietary) => new()
    {
        Id = "P1", Kind = ItemKind.Plugin, Title = "Fixture plugins", Summary = "Marrow-made fixture", Vendor = "OpenAI", CountState = CountState.Added, PinState = PinState.Pinned,
        Pin = "1234567890abcdef1234567890abcdef12345678", SourceUrl = "https://github.com/example/plugins", LicenceName = "Proprietary, MIT", LicenceUrl = null, LicenceFile = null,
        TermsClass = terms, ApproxSizeMb = 1, Needs = [], Provides = [], Artifacts = [], SkillCount = 2, SkillsListed = true,
        Folders =
        [
            new CatalogueFolder { Name = "plugins/one", SkillCount = 1, LicenceName = "Proprietary", LicenceFile = "plugins/one/LICENSE" },
            new CatalogueFolder { Name = "plugins/two", SkillCount = 1, LicenceName = "MIT", LicenceFile = "plugins/two/LICENSE" },
        ],
        Skills =
        [
            new CatalogueSkill { Folder = "alpha", Name = "alpha", Path = "plugins/one/skills/alpha", Needs = [] },
            new CatalogueSkill { Folder = "beta", Name = "beta", Path = "plugins/two/skills/beta", Needs = [], LicenceName = "Apache-2.0", LicenceFile = "plugins/two/skills/beta/LICENSE.txt" },
        ],
        Files =
        [
            File("plugins/one/.codex-plugin/plugin.json", "{}"), File("plugins/one/LICENSE", One), File("plugins/one/skills/alpha/SKILL.md", Skill),
            File("plugins/two/.codex-plugin/plugin.json", "{ }"), File("plugins/two/LICENSE", Two), File("plugins/two/skills/beta/LICENSE.txt", Beta),
            File("plugins/two/skills/beta/SKILL.md", Skill.Replace("alpha", "beta", StringComparison.Ordinal)),
        ],
    };

    /// <summary>Serves each licence file by path and counts the reads; any other path is refused.</summary>
    private sealed class Files(Dictionary<string, string> texts) : ILicenceText
    {
        public List<string> Read { get; } = [];

        public Task<byte[]> ReadAsync(CatalogueItem item, CancellationToken cancellation) => throw new InvalidOperationException("No item licence file.");

        public Task<byte[]> ReadFileAsync(CatalogueItem item, string path, CancellationToken cancellation)
        {
            Read.Add(path);
            return Task.FromResult(Encoding.UTF8.GetBytes(texts[path]));
        }
    }

    private static Files Texts() => new(new Dictionary<string, string> { ["plugins/one/LICENSE"] = One, ["plugins/two/LICENSE"] = Two, ["plugins/two/skills/beta/LICENSE.txt"] = Beta });

    private static TermsStep Step(CatalogueItem item, ILicenceText text, Func<AddonsState>? state = null) =>
        new(new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-11", Items = [item] }, state ?? (() => AddonsState.Empty), (_, _) => { }, text, new FixedClock(TermsRig.At));

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_terms_of_a_set_under_licences_per_plugin_are_every_licence_file_each_checked_and_named_with_what_it_covers()
    {
        var item = Plugin();
        var texts = Texts();
        var step = Step(item, texts);
        var offer = step.Offer("P1");
        Assert.NotNull(offer);
        Assert.Equal(PluginLicences.Hash(item), offer.LicenceSha256);
        Assert.True(offer.CanAccept);
        Assert.Equal("Proprietary, MIT", offer.LicenceName);
        var text = await step.LicenceTextAsync("P1");
        Assert.Equal(
            "—— plugins/one/LICENSE · Proprietary · one ——\n\n" + One + "\n\n" +
            "—— plugins/two/LICENSE · MIT · two ——\n\n" + Two + "\n\n" +
            "—— plugins/two/skills/beta/LICENSE.txt · Apache-2.0 · /beta ——\n\n" + Beta, text);
        Assert.Equal(["plugins/one/LICENSE", "plugins/two/LICENSE", "plugins/two/skills/beta/LICENSE.txt"], texts.Read);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task One_licence_file_that_does_not_match_its_pin_refuses_the_whole_text_and_a_reader_of_item_licences_only_fails_closed()
    {
        var changed = new Files(new Dictionary<string, string> { ["plugins/one/LICENSE"] = One, ["plugins/two/LICENSE"] = Two + "!", ["plugins/two/skills/beta/LICENSE.txt"] = Beta });
        await Assert.ThrowsAsync<InvalidDataException>(() => Step(Plugin(), changed).LicenceTextAsync("P1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Step(Plugin(), new StubLicenceText(One)).LicenceTextAsync("P1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Step(Plugin(), new FailClosedLicenceText()).LicenceTextAsync("P1"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Accepted_terms_cover_the_set_until_any_one_of_its_licence_files_changes()
    {
        var item = Plugin();
        var state = AddonsState.Empty.AcceptTerms(PluginLicences.Hash(item)!, "P1", TermsRig.At);
        Assert.Null(Step(item, Texts(), () => state).Offer("P1"));
        var changed = item with { Files = item.Files.Select(f => f.Path == "plugins/two/skills/beta/LICENSE.txt" ? File(f.Path, Beta + "v2") : f).ToList() };
        Assert.NotNull(Step(changed, Texts(), () => state).Offer("P1"));
        Assert.Null(Step(Plugin(TermsClass.Open), Texts()).Offer("P1"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public async Task The_installer_requires_and_keeps_every_licence_file_of_a_set_under_licences_per_plugin()
    {
        using var rig = new InstallRig();
        var item = Plugin();
        var texts = new Dictionary<string, string>
        {
            ["plugins/one/.codex-plugin/plugin.json"] = "{}", ["plugins/one/LICENSE"] = One, ["plugins/one/skills/alpha/SKILL.md"] = Skill, ["plugins/two/.codex-plugin/plugin.json"] = "{ }",
            ["plugins/two/LICENSE"] = Two, ["plugins/two/skills/beta/LICENSE.txt"] = Beta, ["plugins/two/skills/beta/SKILL.md"] = Skill.Replace("alpha", "beta", StringComparison.Ordinal),
        };
        var locals = texts.ToDictionary(t => t.Key, t => rig.Input(t.Key.Replace('/', '_'), Encoding.UTF8.GetBytes(t.Value)));
        var plan = new InstallPlan(item, new ItemRecord(item.Id, ItemState.Checking, null, item.Pin, null), rig.Root, InstallerKind.Skillset, locals) { Source = "P1", Spares = [] };
        var result = await rig.Installer().InstallAsync(plan);
        foreach (var (path, text) in texts) Assert.Equal(text, await System.IO.File.ReadAllTextAsync(Path.Combine(result.Path, path.Replace('/', Path.DirectorySeparatorChar))));

        using var second = new InstallRig();
        var missing = new Dictionary<string, string>(texts.Where(t => t.Key != "plugins/two/LICENSE").ToDictionary(t => t.Key, t => second.Input(t.Key.Replace('/', '_'), Encoding.UTF8.GetBytes(t.Value))));
        await Assert.ThrowsAsync<InvalidDataException>(() => second.Installer().InstallAsync(plan with { LocalFiles = missing }));
        var noLicence = item with { Folders = [item.Folders[0], item.Folders[1] with { LicenceName = null, LicenceFile = null }] };
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => second.Installer().InstallAsync(plan with { Item = noLicence, LocalFiles = locals }));
        Assert.Equal("A licence file path is required.", refused.Message);
        second.AssertOnlyTemporaryFolders();
    }
}
