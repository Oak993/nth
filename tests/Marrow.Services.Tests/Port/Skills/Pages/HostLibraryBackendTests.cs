using System.Text.Json;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17): the pages' backend over the composed SkillsHost of one fresh data root: the terms step, enabled.json (the switches), the unbound install seam and the drive. Fresh roots under
// MARROW_PORT_DATA_DIR; no network, no CLI, no real user folder.
[Trait("Feature", "SK-15")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class HostLibraryBackendTests
{
    private static HostLibraryBackend Backend(SkillsHost host, TermsRig rig, IFirstInstall? install = null) => new(host, rig.Catalogue, install);

    private static SwitchLine Skill() => new(SwitchKind.Skill, "A2", "A2/skills/docx", "/docx", "", false, true);

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public async Task The_terms_offer_and_acceptance_go_through_the_hosts_terms_step_and_the_licence_text_is_the_pinned_file()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig("Anthropic");
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text, new FixedClock(TermsRig.At)));
            var backend = Backend(host, rig);
            Assert.Equal("Under Anthropic's terms", backend.Terms("A2")!.Label);
            Assert.Equal(TermsRig.LicenceBody, await backend.LicenceTextAsync("A2", CancellationToken.None));
            backend.AcceptTerms("A2");
            Assert.Null(backend.Terms("A2"));
            Assert.Single(backend.State.AcceptedTerms);
            Assert.Same(rig.Catalogue, backend.Catalogue);
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public async Task A_first_install_with_the_seam_unbound_fails_closed_and_writes_nothing_under_the_data_root()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig();
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text));
            var before = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
            var backend = Backend(host, rig);
            var error = await Assert.ThrowsAsync<FirstInstallUnavailableException>(() => backend.Installs.InstallAsync(rig.Item, new Progress<double?>(), CancellationToken.None));
            Assert.Equal("Downloading isn\u2019t available in this build yet.", error.Message);
            Assert.Equal(before, Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList());
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public void A_switch_is_written_to_enabled_json_with_the_catalogues_date_as_its_version_and_read_back()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig();
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text));
            var backend = Backend(host, rig);
            Assert.False(backend.Switches.Skill("A2", "skills/docx"));
            backend.SetSwitch(Skill(), true);
            backend.SetSwitch(new SwitchLine(SwitchKind.PluginFolder, "A2", "A2/sales", "sales", "3 skills", false, true), true);
            Assert.True(backend.Switches.Skill("A2", "skills/docx"));
            Assert.True(backend.Switches.PluginFolder("A2", "sales"));
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "skills", "enabled.json")));
            Assert.Equal("2026-10-05", doc.RootElement.GetProperty("Version").GetString());
            Assert.True(doc.RootElement.GetProperty("Skills").GetProperty("A2/skills/docx").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("Plugins").GetProperty("A2/sales").GetBoolean());
            backend.SetSwitch(Skill(), false);
            Assert.False(new HostLibraryBackend(host, rig.Catalogue).Switches.Skill("A2", "skills/docx"));
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public void A_version_already_in_enabled_json_is_kept_and_a_file_that_does_not_parse_is_never_replaced()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig();
            Directory.CreateDirectory(Path.Combine(root, "skills"));
            var file = Path.Combine(root, "skills", "enabled.json");
            File.WriteAllText(file, "{\"Version\":\"release-9\",\"Skills\":{\"A2/skills/xlsx\":true},\"Plugins\":{}}");
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text));
            var backend = Backend(host, rig);
            Assert.True(backend.Switches.Skill("A2", "skills/xlsx"));
            backend.SetSwitch(Skill(), true);
            using (var doc = JsonDocument.Parse(File.ReadAllText(file)))
            {
                Assert.Equal("release-9", doc.RootElement.GetProperty("Version").GetString());
                Assert.True(doc.RootElement.GetProperty("Skills").GetProperty("A2/skills/xlsx").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("Skills").GetProperty("A2/skills/docx").GetBoolean());
            }
            host.Dispose();
            File.WriteAllText(file, "this is not json");
            using var again = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text));
            var broken = Backend(again, rig);
            Assert.False(broken.Switches.Skill("A2", "skills/xlsx"));
            Assert.Throws<InvalidOperationException>(() => broken.SetSwitch(Skill(), true));
            Assert.Equal("this is not json", File.ReadAllText(file));
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public async Task Reset_goes_through_the_host_and_keeps_the_switches_and_the_accepted_terms()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig();
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text));
            var backend = Backend(host, rig);
            backend.SetSwitch(Skill(), true);
            backend.AcceptTerms("A2");
            await backend.ResetAsync(CancellationToken.None);
            Assert.True(File.Exists(Path.Combine(root, "skills", "enabled.json")));
            Assert.True(backend.Switches.Skill("A2", "skills/docx"));
            Assert.Single(backend.State.AcceptedTerms);
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public void The_drive_of_a_root_is_its_label_and_free_bytes_and_a_share_or_a_missing_drive_is_unknown()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var drive = HostLibraryBackend.DriveOf(root)!;
            Assert.Equal(Path.GetPathRoot(root)!.TrimEnd('\\'), drive.Label);
            Assert.True(drive.FreeBytes > 0);
            Assert.Null(HostLibraryBackend.DriveOf(@"\\server\share\Marrow Code"));
            Assert.Null(HostLibraryBackend.DriveOf(""));
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    public void The_seams_that_are_not_bound_report_nothing()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var rig = new TermsRig();
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), rig.Catalogue, rig.Text));
            var backend = Backend(host, rig);
            Assert.Empty(backend.Providers());
            Assert.Null(backend.Free());
            Assert.Empty(backend.State.Items);
        }
        finally { SkillsHostRig.Delete(root); }
    }
}
