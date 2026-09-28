using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestArchiveTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-053")] // SettingsView.swift:877-909; ChromeTextButton :519-558; Popovers.swift:258-337.
    public void Delete_all_real_pointer_handles_empty_singular_plural_confirm_and_immediate_delete()
    {
        foreach (var count in new[] { 0, 1, 2 }) foreach (var confirm in new[] { false, true })
        With(() => new ArchiveDeleteAllButton(), (view, model, h, w) =>
        {
            model.Settings.ConfirmBeforeDeleting = confirm; Drain(w);
            var button = Assert.Single(Nodes<Button>(view)); Assert.Equal("Delete all", Text(button).Single()); Assert.Equal("Delete every archived thread", ToolTip.GetTip(button)); Assert.Equal(count > 0, button.IsEffectivelyEnabled);
            var image = Assert.Single(Nodes<SwiftImage>(view)); Assert.Equal("trash", image.SymbolName);
            if (count == 0) { Click(w, button); Assert.Empty(model.ArchivedThreads); Assert.False(Assert.Single(Nodes<Popup>(view)).IsOpen); return; }
            Click(w, button); var popup = Assert.Single(Nodes<Popup>(view)); Assert.Equal(PlacementMode.Bottom, popup.Placement);
            if (!confirm) { Assert.Empty(model.ArchivedThreads); Assert.False(popup.IsOpen); return; }
            Assert.Equal(count, model.ArchivedThreads.Count); Assert.True(popup.IsOpen);
            var menu = popup.Child!; Assert.Equal(count == 1 ? "Delete the archived thread?" : "Delete all archived threads?", Text(Assert.Single(Nodes<PopoverSectionHeader>(menu))).Single());
            Assert.Equal(count == 1 ? "Its history will be removed. Files in your project stay as they are." : "The histories of all 2 threads will be removed. Files in your projects stay as they are.", Text(Assert.Single(Nodes<PopoverNote>(menu))).Single());
            var item = Assert.Single(Nodes<PopoverItem>(menu)); Assert.Equal(count == 1 ? "Delete thread" : "Delete 2 threads", Text(item).Single());
            var row = Assert.IsType<StackPanel>(Assert.IsType<Border>(Assert.Single(Nodes<Button>(item)).Content).Child); Ink(row, Avalonia.Controls.Documents.TextElement.GetForeground(row), Chrome.Danger);
            Click(w, Assert.Single(Nodes<Button>(item))); Assert.Empty(model.ArchivedThreads); Assert.False(popup.IsOpen);
        }, before: h => { var project = new Project("Project", h.NewFolder("project")); h.WriteLibrary([project], Enumerable.Range(0, count).Select(i => new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) with { IsArchived = true, Title = "Archived " + i })); }, nativePopup: true);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-054")] // SettingsView.swift:896-934; ED-216; Chrome.swift:1055-1079.
    public void Archived_rows_real_actions_preserve_optional_detail_and_confirm_roles_cancel_restore_and_delete()
    {
        foreach (var hasProject in new[] { false, true })
        With(() => new ArchiveSettingsPage(), (view, model, h, w) =>
        {
            var row = Row(view, "Archived"); CheckRow(row, "Archived", hasProject ? "Project" : null);
            var buttons = Nodes<Button>(row); Assert.Equal(new[] { "Restore", "Delete" }, buttons.Select(b => Text(b).Single())); Assert.Equal(8, Assert.IsType<StackPanel>(Assert.IsType<Grid>(Assert.IsType<Border>(row.Child).Child).Children[1]).Spacing);
            var presenter = new RecordingConfirmationPresenter(); using var seam = AlertPresentation.Presenter.Override(presenter);
            Click(w, buttons[1]); Assert.Single(model.ArchivedThreads); var dialog = Assert.Single(presenter.Shown);
            Assert.Equal("Delete \"Archived\"?", dialog.Title); Assert.Equal("Its history will be removed. Files in your project stay as they are.", dialog.Message); Assert.Equal(AlertKind.ConfirmationDialog, dialog.Kind);
            Assert.Equal(new[] { "Delete thread", "Cancel" }, dialog.Buttons.Select(b => b.Title)); Assert.Equal(new[] { AlertButtonRole.Destructive, AlertButtonRole.Cancel }, dialog.Buttons.Select(b => b.Role)); Assert.Null(dialog.ReturnDefault);
            var task = Field<TaskHandle>(view, "confirmation").Completion; presenter.Cancel(); HeadlessSession.RunDispatcherUntil(task, AppModelHarness.Bound); Drain(w); Assert.Single(model.ArchivedThreads);
            Click(w, buttons[0]); Assert.Empty(model.ArchivedThreads); Assert.Single(presenter.Shown);
        }, before: h => { var project = new Project("Project", h.NewFolder("project")); h.WriteLibrary(hasProject ? [project] : [], [new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) with { IsArchived = true, Title = "Archived" }]); });
        foreach (var confirm in new[] { false, true })
        With(() => new ArchiveSettingsPage(), (view, model, h, w) =>
        {
            model.Settings.ConfirmBeforeDeleting = confirm; Drain(w); var presenter = new RecordingConfirmationPresenter(); using var seam = AlertPresentation.Presenter.Override(presenter);
            Click(w, Nodes<Button>(Row(view, "Archived"))[1]);
            if (confirm) { Assert.Single(model.ArchivedThreads); var task = Field<TaskHandle>(view, "confirmation").Completion; presenter.Press("Delete thread"); HeadlessSession.RunDispatcherUntil(task, AppModelHarness.Bound); Drain(w); }
            else Assert.Empty(presenter.Shown);
            Assert.Empty(model.ArchivedThreads);
        }, before: h => { var project = new Project("Project", h.NewFolder("project")); h.WriteLibrary([project], [new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) with { IsArchived = true, Title = "Archived" }]); });
    }
}
