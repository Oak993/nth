// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The "Import skills" action of the Skills page with its one plain line, the sheet it opens (in the window, like Download all) and, under it, the imported skills. The sheet's state
// lives in the ImportController, so a page built again finds the sheet where the user left it; the page only draws it.
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.UI.Port;

namespace Marrow.App.Port.Skills.Import;

internal sealed class ImportArea : Decorator
{
    private readonly ImportModel model;
    private readonly ImportedSection imported;
    private Func<Task<string?>>? chooser;
    private Action? closeSheet;
    private bool closingFromHere;
    private bool hidden;
    private bool shown;

    public ImportArea(ImportModel model)
    {
        this.model = model;
        imported = new ImportedSection(model);
        var button = LibraryButtons.Make(ImportWords.Action, Open, ButtonLook.Plain, "import-skills");
        button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        var note = LibraryUi.Text(ImportWords.ActionNote, 12, secondary: true, wrap: true);
        Child = new StackPanel { Spacing = 8, Children = { button, note, imported } };
        this.Track(() => model.Revision, _ => Sync());
        AttachedToVisualTree += (_, _) =>
        {
            if (model.PickFolder is null) model.PickFolder = chooser = Choose;
            Sync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (chooser is not null && ReferenceEquals(model.PickFolder, chooser)) model.PickFolder = null;
            chooser = null;
            CloseSheet();
        };
    }

    internal ImportedSection Imported => imported;

    internal bool SheetShown => shown;

    private void Open()
    {
        hidden = false;
        _ = model.Controller.OpenAsync();
    }

    private async Task<string?> Choose()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = ImportWords.ChooseFolder });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    private void Sync()
    {
        var phase = model.Controller.Phase;
        if (phase is ImportPhase.Closed or ImportPhase.Done) hidden = false;
        if (phase == ImportPhase.Closed) { CloseSheet(); return; }
        if (shown || hidden || TopLevel.GetTopLevel(this) is null) return;
        ChatSheetPresentation.Show(this, dismiss =>
        {
            closeSheet = dismiss;
            return new ImportSheetLayer(new ImportSheetView(model, dismiss));
        }, OnSheetClosed, inWindow: true, still: LibraryUi.Still(this));
        shown = closeSheet is not null;
    }

    private void CloseSheet()
    {
        if (!shown) return;
        closingFromHere = true;
        try { closeSheet?.Invoke(); }
        finally { closingFromHere = false; shown = false; closeSheet = null; }
    }

    // Escape (or Cancel and Close) closed the sheet. While an import goes on, Escape only hides the sheet (and a question still open is answered Skip for the rest, so the import can end); otherwise the import is closed.
    private void OnSheetClosed()
    {
        shown = false;
        closeSheet = null;
        if (closingFromHere) return;
        var controller = model.Controller;
        switch (controller.Phase)
        {
            case ImportPhase.Importing:
                hidden = true;
                break;
            case ImportPhase.AskingDuplicate:
                hidden = true;
                controller.AnswerDuplicate(DuplicateChoice.Skip, true);
                break;
            default:
                controller.Close();
                break;
        }
    }
}
