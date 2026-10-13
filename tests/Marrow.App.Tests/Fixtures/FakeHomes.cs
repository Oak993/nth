using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-16): the home view's seam over fake chats: it makes an assistant's home chat and records what it was asked.</summary>
internal sealed class FakeHomes(AsturRig rig) : IAssistantHomes
{
    public List<string> Opened { get; } = [];

    /// <summary>The project an assistant's own work is in; null: it has none.</summary>
    public MissionProject? Project { get; set; }

    /// <summary>When set, making a home chat fails.</summary>
    public bool Fails { get; set; }

    public MissionProject? ProjectOf(AssistantProfile profile) => Project;

    public Guid? OpenHome(AssistantProfile profile)
    {
        if (Fails) throw new IOException("The chat store is full.");
        Opened.Add(profile.Id);
        return rig.AddHomeChat(profile).Id;
    }
}
