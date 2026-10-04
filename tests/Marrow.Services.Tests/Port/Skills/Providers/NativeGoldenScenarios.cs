using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY test fixture (SK-11f). The scenarios whose bytes were captured from the provider code as it stood before SK-11f
// (base commit 13a62183 of cloud/f-sk-11f; the goldens are Fixtures/skills-native/golden-*) and are compared byte for byte afterwards.
internal static class NativeGoldenScenarios
{
    public static readonly (RuntimeMode Runtime, InteractionMode Interaction, string File)[] Modes =
    [
        (RuntimeMode.Supervised, InteractionMode.Build, "golden-prompt-supervised-build.txt"),
        (RuntimeMode.AutoAcceptEdits, InteractionMode.Build, "golden-prompt-autoaccept-build.txt"),
        (RuntimeMode.FullAccess, InteractionMode.Build, "golden-prompt-fullaccess-build.txt"),
        (RuntimeMode.FullAccess, InteractionMode.Plan, "golden-prompt-fullaccess-plan.txt"),
    ];

    /// <summary>One plain turn: the single request the provider sends.</summary>
    public static async Task<byte[]> PlainTurn(Action<OpenAICompatibleSession>? configure = null)
    {
        await using var rig = NativeSessionRig.Create(["plain-turn.sse"], configure: configure);
        await rig.Begin();
        await rig.Turn("hello there");
        return Assert.Single(rig.RequestBodies);
    }

    /// <summary>A turn with one tool round the provider does not know (so no tool body runs): both requests it sends.</summary>
    public static async Task<(byte[] First, byte[] Second)> ToolRound(Action<OpenAICompatibleSession>? configure = null)
    {
        await using var rig = NativeSessionRig.Create(["unknown-tool-call.sse", "final-answer.sse"], configure: configure);
        await rig.Begin();
        await rig.Turn("use the tool");
        Assert.Equal(2, rig.RequestBodies.Count);
        return (rig.RequestBodies[0], rig.RequestBodies[1]);
    }

    /// <summary>The system prompt for one mode pair, taken from a fresh session.</summary>
    public static async Task<string> Prompt(RuntimeMode runtime, InteractionMode interaction, Action<OpenAICompatibleSession>? configure = null)
    {
        await using var rig = NativeSessionRig.Create([], runtime, interaction, configure);
        return rig.Session.SystemPromptForTests();
    }
}
