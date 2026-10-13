// PORT-ONLY: Dextra (DESIGN.md rev 5, 9, 15.2; unit AS-16; ED-NEW-dextra-as16-10, -12, -13). No Swift twin.
// What the controller needs to run a team of Asturs: the team in the store, the seam that makes an assistant's home chat (AS-8 binds it: the
// home view owns where those chats live), and where the pending delegations are kept. All of it is optional: without a team the controller says
// delegate_to and create_assistant are not part of this build, as before.
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>Where an assistant's own work and its home chat live. The Assistant's home (AS-8) binds this; none is bound while that is not here.</summary>
public interface IAssistantHomes
{
    /// <summary>
    /// The project that holds an assistant's own work (its home folder, as a project the missions can work in); null when it has none, and a
    /// delegation then needs the project named, as start_mission does.
    /// </summary>
    MissionProject? ProjectOf(AssistantProfile profile);

    /// <summary>Makes the assistant's home chat (a chat whose AssistantID is its id) and returns the chat's id; null when it cannot be made.</summary>
    Guid? OpenHome(AssistantProfile profile);
}

/// <summary>The team behind the controller (AS-16).</summary>
public sealed class AsturSetup
{
    public required AssistantTeam Team { get; init; }

    public IAssistantHomes? Homes { get; init; }

    /// <summary>The Asturs' web clients (AssistantWebClients): a retired Astur's is let go. Null: the controller holds none.</summary>
    public AssistantWebClients? Web { get; init; }

    /// <summary>Reads delegations.json as nth last saved it (AssistantStore.Delegations); null: none is kept across a restart.</summary>
    public Func<JsonObject?>? LoadDelegations { get; init; }

    /// <summary>Writes it (AssistantStore.SaveDelegations), off the main thread, one write at a time.</summary>
    public Action<JsonObject>? SaveDelegations { get; init; }
}

/// <summary>Where a message the owner typed or said went (9.3).</summary>
public enum TalkKind
{
    /// <summary>To nobody by name: the chat keeps it, with <see cref="TalkResult.Message"/> (a name for Dextra taken off).</summary>
    Same,
    /// <summary>Sent to an Astur's home thread.</summary>
    Sent,
    /// <summary>The name fits several: a "Which one?" question is on the board, and the message goes to the one the owner picks.</summary>
    Asked,
    /// <summary>The Astur has no home thread yet and none could be made: nothing was sent.</summary>
    NoHome,
}

public sealed record TalkResult(TalkKind Kind, string Message)
{
    public AssistantProfile? Target { get; init; }

    /// <summary>The thread to show: the Astur's home thread (Sent), or the Astur's when the message was only its name.</summary>
    public Guid? Thread { get; init; }

    /// <summary>The "Which one?" card.</summary>
    public Guid? Card { get; init; }

    /// <summary>A sentence for the composer when the message was not sent.</summary>
    public string? Note { get; init; }
}
