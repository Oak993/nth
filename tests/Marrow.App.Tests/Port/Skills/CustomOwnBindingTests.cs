using System.Text;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using OwnKind = Marrow.Services.Port.Skills.Custom.CustomAddKind;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class CustomOwnBindingTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("Folder")]
    [InlineData("Zip")]
    [InlineData("GitHub")]
    [Trait("Decision", "ED-326")]
    public void Check_Add_reopen_and_remove_show_real_owned_provenance(string sourceName) => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var source = Enum.Parse<CustomAddSource>(sourceName);
        var files = new CustomOwnFake();
        var original = Encoding.UTF8.GetBytes("---\nname: own-skill\ndescription: Own description\n---\nBody");
        files.Source.Add("SKILL.md", original.ToArray());
        var host = files.Open();
        var services = CustomAddSeams.For(host);
        using var bound = CustomAddSeams.Backends.Override(services);
        var page = new SkillsSettingsPage();
        h.Root.Children.Add(page);
        h.Wait(services.Own!.Refresh());
        var location = source == CustomAddSource.GitHub ? "owner/repository" : "/fake/source";
        var sheet = new CustomAddSheet(CustomAddKind.Skills, services.For(CustomAddKind.Skills), _ => Task.FromResult<string?>(location), () => { });
        h.Root.Children.Add(sheet);
        h.Wait(sheet.SelectSource(source));
        if (source == CustomAddSource.GitHub) sheet.Location.Text = location;
        h.Wait(sheet.Check());
        Assert.Empty(host.List(OwnKind.Skills));
        Assert.Equal("Add 1 skill", sheet.PrimaryText);
        h.Wait(sheet.AddSelected());
        var item = Assert.Single(host.List(OwnKind.Skills));
        Assert.Equal("in", Assert.Single(sheet.Candidates).Value.State);
        Assert.Contains("Your own", h.Words(page));
        Assert.Equal(item.Id, Assert.Single(page.OwnRows).Key);
        services.Own.Select(CustomAddKind.Skills, item.Id);
        h.Settle();
        Assert.Contains(location, page.OwnDetails[item.Id].ProvenanceText, StringComparison.Ordinal);
        Assert.Contains("Added", h.Words(page));
        Assert.Equal(source == CustomAddSource.GitHub ? new string('a', 40) : null, item.Provenance.Commit);
        Assert.Equal(original, files.Source["SKILL.md"]);
        sheet.Dismiss();
        h.Root.Children.Remove(page);
        var reopened = CustomAddSeams.For(files.Open());
        using var rebind = CustomAddSeams.Backends.Override(reopened);
        var again = new SkillsSettingsPage();
        h.Root.Children.Add(again);
        h.Wait(reopened.Own!.Refresh());
        Assert.Equal(item.Id, Assert.Single(again.OwnRows).Key);
        reopened.Own.Select(CustomAddKind.Skills, item.Id);
        h.Settle();
        h.Wait(reopened.Own.Remove(item.Id));
        Assert.Empty(again.OwnRows);
        Assert.Empty(files.Installed);
        Assert.Equal(original, files.Source["SKILL.md"]);
        Assert.Equal(source == CustomAddSource.GitHub ? 1 : 0, files.Fetches);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Failed_publication_can_retry_the_same_checked_token_and_forged_details_fail() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var files = new CustomOwnFake();
        files.Source.Add("SKILL.md", Encoding.UTF8.GetBytes("---\nname: own-skill\ndescription: Description\n---\n"));
        var host = files.Open();
        var services = CustomAddSeams.For(host);
        var backend = services.For(CustomAddKind.Skills)!;
        IReadOnlyList<CustomAddCandidate> found = [];
        h.Wait(Task.Run(async () => found = await backend.Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "/fake/source", "", ""), default)));
        var candidate = Assert.Single(found);
        h.Wait(Assert.ThrowsAsync<InvalidOperationException>(() => backend.Add(candidate with { Name = "forged" }, new CustomAddProgress(_ => { }), default)));
        files.FailCommit = true;
        h.Wait(Assert.ThrowsAsync<IOException>(() => backend.Add(candidate, new CustomAddProgress(_ => { }), default)));
        Assert.Empty(services.Own!.Items(CustomAddKind.Skills));
        Assert.Empty(files.Installed);
        files.FailCommit = false;
        h.Wait(backend.Add(candidate, new CustomAddProgress(_ => { }), default));
        Assert.Single(services.Own.Items(CustomAddKind.Skills));
        h.Wait(Assert.ThrowsAsync<InvalidOperationException>(() => backend.Add(candidate, new CustomAddProgress(_ => { }), default)));
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Changing_source_discards_checked_snapshots() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var files = new CustomOwnFake();
        files.Source.Add("SKILL.md", Encoding.UTF8.GetBytes("---\nname: own-skill\ndescription: Description\n---\n"));
        var services = CustomAddSeams.For(files.Open());
        var backend = services.For(CustomAddKind.Skills)!;
        var sheet = new CustomAddSheet(CustomAddKind.Skills, backend, _ => Task.FromResult<string?>("/fake/source"), () => { });
        h.Root.Children.Add(sheet);
        h.Wait(sheet.SelectSource(CustomAddSource.Folder));
        h.Wait(sheet.Check());
        var candidate = Assert.Single(sheet.Candidates).Value.Candidate;
        h.Wait(sheet.SelectSource(CustomAddSource.Zip));
        h.Wait(Assert.ThrowsAsync<InvalidOperationException>(() => backend.Add(candidate, new CustomAddProgress(_ => { }), default)));
        Assert.Empty(files.Installed);
    });

    [Theory]
    [InlineData("Plugins")]
    [InlineData("Tools")]
    [Trait("Decision", "ED-326")]
    public void Plugin_and_tool_copies_bind_their_real_registry_and_registration(string kindName) => LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
    {
        var kind = Enum.Parse<CustomAddKind>(kindName);
        var files = new CustomOwnFake();
        if (kind == CustomAddKind.Plugins) files.Source.Add(".claude-plugin/plugin.json", Encoding.UTF8.GetBytes("{\"name\":\"own-plugin\",\"description\":\"Own plugin\"}"));
        else files.Source.Add("tool.exe", [77, 90, 0, 1]);
        var host = files.Open();
        var services = CustomAddSeams.For(host);
        var backend = services.For(kind)!;
        IReadOnlyList<CustomAddCandidate> checkedItems = [];
        h.Wait(Task.Run(async () => checkedItems = await backend.Check(new(kind, CustomAddSource.Folder, "/fake/source", "Own Tool", "own-tool"), default)));
        h.Wait(backend.Add(Assert.Single(checkedItems), new CustomAddProgress(_ => { }), default));
        var item = Assert.Single(services.Own!.Items(kind));
        var detail = new CustomOwnDetail(services.Own, kind, item);
        h.Root.Children.Add(detail);
        h.Settle();
        Assert.Contains("Added", h.Words(detail));
        if (kind == CustomAddKind.Tools)
        {
            Assert.Contains("Provides own-tool", h.Words(detail));
            Assert.DoesNotContain(LibraryWords.InEverySession, h.Words(detail));
            Assert.Equal("own-tool", Assert.Single(host.Commands()).Name);
        }
        else Assert.Single(host.PluginDirectories());
        h.Wait(services.Own.SetEnabled(item.Id, false));
        Assert.Empty(host.Commands());
        Assert.Empty(host.PluginDirectories());
        h.Wait(services.Own.SetEnabled(item.Id, true));
        Assert.True(Assert.Single(services.Own.Items(kind)).Enabled);
        h.Wait(services.Own.Remove(item.Id));
        Assert.Empty(files.Installed);
        Assert.Single(files.Source);
    });
}
