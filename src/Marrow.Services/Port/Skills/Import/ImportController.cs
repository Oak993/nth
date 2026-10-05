// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The logic of the "Import skills" sheet: no Avalonia; the view draws what it holds and sends it the user's presses (the pattern of LibraryController).
namespace Marrow.Services.Port.Skills.Import;

public enum ImportPhase
{
    Closed,
    Scanning,
    Choosing,
    AskingDuplicate,
    Importing,
    Done,
}

public sealed record ImportRow(string Key, string Name, string Description, string Source, bool Ticked, bool AlreadyImported);

public sealed record ImportGroup(ForeignApp App, string Title, IReadOnlyList<ImportRow> Rows, bool AllTicked);

public sealed class ImportController
{
    public ImportController(Func<IReadOnlyList<string>, ForeignScan> scan, ImportedSkillsLibrary library, Func<Task<string?>>? pickFolder = null)
        => throw new NotImplementedException();

    public event Action? Changed;

    public int Revision => throw new NotImplementedException();

    public ImportPhase Phase => throw new NotImplementedException();

    public IReadOnlyList<ImportGroup> Groups => throw new NotImplementedException();

    public ImportMode Mode => throw new NotImplementedException();

    public int TickedCount => throw new NotImplementedException();

    public int ProblemCount => throw new NotImplementedException();

    public DuplicateQuestion? Question => throw new NotImplementedException();

    public ImportResult? Result => throw new NotImplementedException();

    public string Summary => throw new NotImplementedException();

    public IReadOnlyList<ImportedSkill> Imported => throw new NotImplementedException();

    public Task OpenAsync() => throw new NotImplementedException();

    public void Close() => throw new NotImplementedException();

    public void Toggle(string key) => throw new NotImplementedException();

    public void ToggleGroup(ForeignApp app) => throw new NotImplementedException();

    public void SetMode(ImportMode mode) => throw new NotImplementedException();

    public Task AddFolderAsync() => throw new NotImplementedException();

    public Task ImportSelectedAsync() => throw new NotImplementedException();

    public Task ImportAllAsync() => throw new NotImplementedException();

    public void AnswerDuplicate(DuplicateChoice choice, bool applyToRest) => throw new NotImplementedException();

    public bool Remove(string id) => throw new NotImplementedException();

    public void SetEnabled(string id, bool on) => throw new NotImplementedException();

    internal void Raise() => Changed?.Invoke();
}
