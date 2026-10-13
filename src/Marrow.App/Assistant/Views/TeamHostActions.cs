// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.2 "Creating one", 9.4 "buttons for Talk, Pause and Settings"; the notes of AS-16: "The Team tab (AS-17) calls CreateByOwner, UpdateAstur, RetireAstur and the ProfileSheet in its
// Add and Edit modes"; ED-NEW-dextra-as17-6). No Swift twin.
// What the Team tab's buttons do in the app: Talk opens an assistant's own place (Dextra's home, or an Astur's thread), Settings opens Settings > Assistant for Dextra and the profile sheet in Edit
// mode for an Astur, Add an Astur opens the sheet in Add mode, Retire asks the controller, and a task chip opens its lead chat. The controller's three calls sit behind one small desk so a test can
// stand in for it; the sheet is shown by the home's own sheet layer (focus goes back to the opener when it closes). The sheet is the owner's card: nothing is made until he presses Create there.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>The controller's three calls the Team tab makes; each answers with what is still wrong (empty: it went through).</summary>
public interface IAsturDesk
{
    /// <summary>The owner's own card: the Astur is made from it (no card of Dextra's is answered).</summary>
    Task<IReadOnlyList<string>> Create(AsturDraft draft);

    Task<IReadOnlyList<string>> Update(string id, AsturDraft draft);

    /// <summary>Null when it was retired, else the sentence for why not.</summary>
    Task<string?> Retire(string id);
}

/// <summary>The controller as the desk.</summary>
public sealed class ControllerAsturDesk(AssistantController controller) : IAsturDesk
{
    public async Task<IReadOnlyList<string>> Create(AsturDraft draft) => Problems(await controller.CreateByOwner(draft).ConfigureAwait(true));

    public async Task<IReadOnlyList<string>> Update(string id, AsturDraft draft) => Problems(await controller.UpdateAstur(id, draft).ConfigureAwait(true));

    public async Task<string?> Retire(string id) => (await controller.RetireAstur(id).ConfigureAwait(true)).Problem;

    private static IReadOnlyList<string> Problems(TeamResult result) => result.Ok || result.Problem is null ? [] : [result.Problem];
}

/// <summary>Where the buttons reach: the home's sheet layer, Settings, the sidebar's selection.</summary>
public sealed record TeamHostSeams
{
    /// <summary>Shows a view over the home with a title and Close; focus goes back to the opener when it closes.</summary>
    public required Action<string, Control> ShowSheet { get; init; }

    public required Action CloseSheet { get; init; }

    public required Action OpenAssistantSettings { get; init; }

    public required Action ShowHome { get; init; }

    /// <summary>Selects a chat in the main area; false when nth has no such chat.</summary>
    public required Func<Guid, bool> OpenThread { get; init; }

    public required Action<string> Said { get; init; }

    /// <summary>The standing reserve rules in words for an assistant, shown under the card's budget.</summary>
    public Func<string, string> ReserveLine { get; init; } = _ => "";
    public Func<IReadOnlyList<(string Id, string Name)>>? PillChoices { get; init; }
    public Action<string>? Memory { get; init; }
    public Action<string>? Duties { get; init; }
    public Func<string, Task<string?>>? DeleteFiles { get; init; }
}

public static class TeamHostActions
{
    /// <summary>The Team tab's actions. Without a desk (no controller, or no team in this build) Add, Settings of an Astur and Retire are left out and their buttons are disabled.</summary>
    public static TeamActions Build(Func<IReadOnlyList<AssistantProfile>> profiles, IAsturDesk? desk, TeamHostSeams seams)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(seams);
        return new TeamActions
        {
            Talk = id => Talk(profiles, seams, id),
            Edit = id => Edit(profiles, desk, seams, id),
            Add = desk is null ? null : () => Add(profiles, desk, seams),
            Retire = desk is null ? null : desk.Retire,
            Memory = seams.Memory,
            Duties = seams.Duties,
            DeleteFiles = seams.DeleteFiles,
            Retired = seams.DeleteFiles is null ? null : () => seams.ShowSheet("Retired Asturs", new AsturFilesSheet(profiles, seams.DeleteFiles, seams.Said)),
            OpenChat = lead =>
            {
                if (!seams.OpenThread(lead)) seams.Said("That chat is not here any more.");
            },
        };
    }

    private static void Talk(Func<IReadOnlyList<AssistantProfile>> profiles, TeamHostSeams seams, string id)
    {
        if (profiles().FirstOrDefault(p => p.Id == id) is not { } profile) return;
        if (profile.IsRoot)
        {
            seams.ShowHome();
            return;
        }
        if (profile.HomeThread is { } thread && seams.OpenThread(thread)) return;
        seams.Said($"{profile.Name} has no thread of its own yet.");
    }

    private static void Edit(Func<IReadOnlyList<AssistantProfile>> profiles, IAsturDesk? desk, TeamHostSeams seams, string id)
    {
        if (profiles().FirstOrDefault(p => p.Id == id) is not { } profile) return;
        if (profile.IsRoot)
        {
            seams.OpenAssistantSettings();
            return;
        }
        if (desk is null) return;
        var model = new ProfileSheetModel(ProfileSheetMode.Edit, profiles, draft => desk.Update(id, draft), seams.CloseSheet, AsturTeamRules.ToDraft(profile), id, seams.ReserveLine(id)) { PillChoices = seams.PillChoices };
        Show(model, seams);
    }

    private static void Add(Func<IReadOnlyList<AssistantProfile>> profiles, IAsturDesk desk, TeamHostSeams seams)
    {
        var model = new ProfileSheetModel(ProfileSheetMode.Add, profiles, draft => desk.Create(draft), seams.CloseSheet, reserveLine: seams.ReserveLine("")) { PillChoices = seams.PillChoices };
        Show(model, seams);
    }

    // the sheet is in a scroller that never scrolls sideways (the sheet stacks its fields above their labels and wraps at any width)
    private static void Show(ProfileSheetModel model, TeamHostSeams seams)
    {
        var sheet = new ProfileSheet(model);
        sheet.Finished += seams.CloseSheet;
        var scroll = new ScrollViewer { Content = sheet, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch };
        seams.ShowSheet("Team", scroll);
    }
}
