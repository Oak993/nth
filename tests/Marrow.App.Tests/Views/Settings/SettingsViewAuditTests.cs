using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using HostWindow = Avalonia.Controls.Window;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using System.Linq;

namespace Marrow.App.Tests.Views.Settings;

// Audit-fix tests for DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc7926).
[Collection("Headless")]
public sealed class SettingsViewAuditTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    private static void Library(AppModelHarness h, int archived)
    {
        var project = new Project("Project", h.NewFolder("project"));
        h.WriteLibrary([project], Enumerable.Range(0, archived).Select(i => new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) with { IsArchived = true, Title = "Archived " + i }));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-026")] // SettingsView.swift:296-305 a native switch toggles on a primary press released inside it
    public void A_switch_ignores_secondary_presses_and_presses_released_outside() => With(() =>
    {
        var on = false;
        return new SettingsSwitch(new Binding<bool>(() => on, value => on = value)) { Tag = (Func<bool>)(() => on) };
    }, (view, _, _, window) =>
    {
        var read = (Func<bool>)view.Tag!;
        var toggle = Nodes<SmallSwitch>(view).Single(); var point = At(toggle, window);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Drain(window);
        Assert.False(read());
        window.MouseMove(point); window.MouseDown(point, MouseButton.Middle); window.MouseUp(point, MouseButton.Middle); Drain(window);
        Assert.False(read());
        var away = new Point(point.X + 300, point.Y + 300);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); Drain(window);
        Assert.False(read()); // nothing changes before the release
        window.MouseMove(away); window.MouseUp(away, MouseButton.Left); Drain(window);
        Assert.False(read());
        Click(window, toggle);
        Assert.True(read());
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-022")] // SettingsView.swift:216-267 the page's ScrollView identity depends only on the page: restoring a row keeps it
    public void Restoring_an_archived_thread_keeps_the_pages_scroll_view() => With(() => new SettingsView(), (view, model, _, window) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Archive; Drain(window);
        var detail = Field<Border>(view, "detail");
        var scroll = Nodes<ScrollViewer>(detail).First();
        Assert.Equal(2, model.ArchivedThreads.Count);
        model.Unarchive(model.ArchivedThreads[0].Id); Drain(window);
        Assert.Single(model.ArchivedThreads);
        Assert.Same(scroll, Nodes<ScrollViewer>(detail).First());
    }, before: h => Library(h, 2));

    [Fact]
    [Trait("Req", "PR-SETTINGS-236")] // SettingsView.swift:912-934 each row's detail is model.project(thread.projectID)?.name read in the observed body
    public void An_archived_rows_project_name_follows_project_changes() => With(() => new ArchiveSettingsPage(), (view, model, _, window) =>
    {
        var row = Row(view, "Archived 0");
        Assert.Equal("Project", Text(row)[1]);
        model.UpdateProject(model.ArchivedThreads[0].ProjectID, project => project with { Name = "Renamed" }); Drain(window);
        Assert.Equal("Renamed", Text(Row(view, "Archived 0"))[1]);
    }, before: h => Library(h, 1));

    [Fact]
    [Trait("Req", "PR-SETTINGS-238")] // SettingsView.swift:989-998 the app-icon HStack centres the two-line label block against the 56-point icon
    public void The_about_label_block_is_centred_beside_the_icon() => With(() => new AboutSettingsPage(), (view, _, _, _) =>
    {
        var icon = Nodes<Image>(view).First(i => i.Width == 56);
        var name = Nodes<TextBlock>(view).First(t => t.Text == "Nth");
        var version = Nodes<TextBlock>(view).First(t => t.Text?.StartsWith("Version ", StringComparison.Ordinal) == true);
        var iconCentre = icon.TranslatePoint(new Point(0, icon.Bounds.Height / 2), view)!.Value.Y;
        var top = name.TranslatePoint(default, view)!.Value.Y;
        var bottom = version.TranslatePoint(new Point(0, version.Bounds.Height), view)!.Value.Y;
        Assert.InRange(Math.Abs(iconCentre - (top + bottom) / 2), 0, 1);
    });
}
