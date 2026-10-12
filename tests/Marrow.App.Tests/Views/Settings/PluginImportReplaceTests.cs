using System.Text;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.Skills.PluginImport;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Import;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.PluginImport;
using OwnKind = Marrow.Services.Port.Skills.Custom.CustomAddKind;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-mcpimport-6 (review of claude/mcp-import): Replace in "Import plugins" goes through the very backend "+ Add" uses and swaps only after the new copy is in, so a failed add never loses the plugin nth had.
[Collection("Headless")]
public sealed class PluginImportReplaceTests(HeadlessSession headless)
{
    private static byte[] Manifest(string description) => Encoding.UTF8.GetBytes("{ \"name\": \"fixture-plugin\", \"description\": \"" + description + "\" }");

    private static T Result<T>(LibraryPageHarness h, Task<T> task)
    {
        h.Wait(task);
        return task.Result;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-6")]
    public void A_replace_that_fails_keeps_the_plugin_nth_had_and_one_that_works_swaps_it_keeping_its_enabled_state() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
    {
        var files = new CustomOwnFake();
        files.Source.Add(".claude-plugin/plugin.json", Manifest("The old copy."));
        files.Source.Add("hooks/old.txt", [1]);
        var host = files.Open();
        var services = CustomAddSeams.For(host);
        using var bound = CustomAddSeams.Backends.Override(services);
        var scan = new ForeignPluginScan([new(ForeignPluginApp.ClaudeCode, "fixture-plugin", "", "market", "/fake/source")], []);
        var controller = new PluginImportController(_ => scan, new CustomPluginImportTarget(services.For(CustomAddKind.Plugins)!, services.Own));

        // the first time it is an ordinary import, as "+ Add > From a folder" would make it
        h.Wait(controller.OpenAsync());
        h.Wait(controller.ImportAllAsync());
        var old = Assert.Single(host.List(OwnKind.Plugins));
        Assert.Equal("The old copy.", old.Description);
        host.SetEnabled(old.Id, false);

        // the folder changes and the disk fails: Replace is chosen, the add fails, the old plugin is still there, still off
        files.Source[".claude-plugin/plugin.json"] = Manifest("The new copy.");
        files.FailPublish = true;
        controller.Close();
        h.Wait(controller.OpenAsync());
        controller.SetChoice(DuplicateChoice.Replace);
        controller.Toggle(controller.Groups.Single().Rows.Single().Key);
        var failed = Result(h, controller.ImportSelectedAsync());
        Assert.Equal((0, 0), (failed!.Imported, failed.Replaced));
        Assert.Single(failed.Failed);
        var kept = Assert.Single(host.List(OwnKind.Plugins));
        Assert.Equal((old.Id, "The old copy.", false), (kept.Id, kept.Description, kept.Enabled));
        Assert.Equal([old.Id], files.Installed.Keys);
        Assert.Contains("payload/hooks/old.txt", files.Installed[old.Id].Keys);

        // the disk works again: the swap happens, the new copy is as disabled as the old one was
        files.FailPublish = false;
        controller.Close();
        h.Wait(controller.OpenAsync());
        controller.SetChoice(DuplicateChoice.Replace);
        controller.Toggle(controller.Groups.Single().Rows.Single().Key);
        var done = Result(h, controller.ImportSelectedAsync());
        Assert.Equal(1, done!.Replaced);
        var swapped = Assert.Single(host.List(OwnKind.Plugins));
        Assert.NotEqual(old.Id, swapped.Id);
        Assert.Equal(("The new copy.", false), (swapped.Description, swapped.Enabled));
        Assert.Equal([swapped.Id], files.Installed.Keys);
        Assert.Equal(swapped.Id, Assert.Single(services.Own!.Items(CustomAddKind.Plugins)).Id); // and the page's own list shows it
    });
}
