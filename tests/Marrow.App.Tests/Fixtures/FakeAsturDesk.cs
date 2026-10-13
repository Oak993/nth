using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-17: the controller's three calls the Team tab makes, recorded and answered with what the test sets.</summary>
public sealed class FakeAsturDesk : IAsturDesk
{
    public List<string> Calls { get; } = [];

    public IReadOnlyList<string> CreateAnswer { get; set; } = [];

    public IReadOnlyList<string> UpdateAnswer { get; set; } = [];

    public string? RetireAnswer { get; set; }

    public AsturDraft? LastDraft { get; private set; }

    public Task<IReadOnlyList<string>> Create(AsturDraft draft)
    {
        Calls.Add("Create " + draft.Normalized().Name);
        LastDraft = draft;
        return Task.FromResult(CreateAnswer);
    }

    public Task<IReadOnlyList<string>> Update(string id, AsturDraft draft)
    {
        Calls.Add("Update " + id + " " + draft.Normalized().Name);
        LastDraft = draft;
        return Task.FromResult(UpdateAnswer);
    }

    public Task<string?> Retire(string id)
    {
        Calls.Add("Retire " + id);
        return Task.FromResult(RetireAnswer);
    }
}

/// <summary>Dextra AS-17: the places the Team tab's buttons reach, recorded; the sheet shown is kept for the test to press.</summary>
public sealed class FakeTeamHost
{
    public List<string> Calls { get; } = [];

    public string? SheetTitle { get; private set; }

    public Control? Sheet { get; private set; }

    public HashSet<Guid> Threads { get; } = [];

    public TeamHostSeams Seams => new()
    {
        ShowSheet = (title, content) =>
        {
            Calls.Add("Sheet " + title);
            SheetTitle = title;
            Sheet = content;
        },
        CloseSheet = () =>
        {
            Calls.Add("Close");
            Sheet = null;
        },
        OpenAssistantSettings = () => Calls.Add("Settings"),
        ShowHome = () => Calls.Add("Home"),
        OpenThread = thread =>
        {
            Calls.Add("Thread");
            return Threads.Contains(thread);
        },
        Said = line => Calls.Add("Said " + line),
        ReserveLine = id => id.Length == 0 ? "Keeps 30% on Claude 20x (your standing rule)." : "",
    };
}
