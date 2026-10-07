using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// PR-HYDRA-060: ONE predicate decides where the lead's Hydra policy lives, ThreadRuntime.keepsHydraPolicyInSystemPrompt =
// AppModel.hydraIsNative(provider) || provider.isAPIKeyBased (ThreadRuntime.swift:1388-1390; hydraIsNative = Claude, Codex, Copilot
// AppModel+Hydra.swift:141-143; isAPIKeyBased = Deepseek, Meta, Zai Provider.swift:101, and the port's OpenRouter, OD-0049/ED-234).
// It gates the per-message prefix (ThreadRuntime.swift:1297-1316), the anti-slop fallback (:1325-1334) and the session signature
// (:1446). Every test runs a real StartTurn over an offline scripted session and reads what the provider was sent. The four provider
// suppressions the record names are proven where the argv and config are built: ClaudeSessionTests (--disallowedTools Agent Task),
// CodexSessionTests (features.multi_agent = false), CopilotSessionTests (no customAgents) and the API sessions' system message.
public sealed class ThreadRuntimeHydraPolicyPlacementTests
{
    private const string Text = "Audit the module";

    // ED-410: this scripted rig predates endpoint binding. Local is covered by the fake-loopback runtime test
    // and the all-provider policy predicate test; retain every original source/OpenRouter scenario here.
    public static TheoryData<ProviderKind> EveryProvider() => new(Enum.GetValues<ProviderKind>().Where(provider => provider != ProviderKind.Local).ToArray());

    // The union the Swift predicate names, with the port's OpenRouter (an API-key provider like Deepseek, Meta and Zai).
    private static bool KeepsPolicy(ProviderKind provider) =>
        provider is ProviderKind.Claude or ProviderKind.Codex or ProviderKind.Copilot
            or ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local;

    private static ThreadRuntimeTurnRig Rig(ProviderKind provider, HydraPair? pair = null) =>
        new(provider,
            thread => pair is null ? thread : thread with { HydraPairID = pair.Id },
            hydraOn: true,
            settings: app =>
            {
                app.Settings.HydraEnabled = true;
                app.Settings.HydraIsolateHeads = false;
                app.Settings.HydraAutoMerge = false;
                app.Settings.AntiSlopEnabled = true;
                if (pair is null) return;
                // The pair's heads run on another provider: that provider has to count as installed and be switched on.
                HydraCookbookResolveFixture.SetInstalled(app.Providers, pair.WorkerProvider!.Value, true);
                app.Settings.AddHydraPair(pair);
            });

    private static HydraHeadInfo AddFinishedHead(ThreadRuntimeTurnRig rig)
    {
        var info = AppModelSamples.Head(HydraHeadInfo.Status.Completed);
        rig.H.Main.OnMain(() => rig.App.InsertThread(AppModelSamples.Thread(rig.Project) with
        {
            ParentThreadID = rig.InitialThread.Id, IsInPanel = true, Hydra = info,
        }));
        return info;
    }

    private static HydraLaunch Launch(ThreadRuntimeTurnRig rig)
    {
        var thread = rig.Thread;
        return rig.H.Main.OnMain(() => rig.App.HydraLaunch(thread))!;
    }

    [Theory]
    [MemberData(nameof(EveryProvider))]
    [Trait("Req", "PR-HYDRA-060")] // ThreadRuntime.swift:1297-1316, :1325-1334, :1388-1390 (user turns: the note, or the whole policy on every message)
    public void A_user_turn_carries_the_whole_policy_only_when_the_provider_has_nowhere_else_to_keep_it(ProviderKind provider)
    {
        using var rig = Rig(provider);
        var head = AddFinishedHead(rig);
        var launch = Launch(rig);
        var team = HydraPrompts.TeamStatus([head]);
        Assert.Equal(AppModel.HydraIsNative(provider), launch.RunsNatively); // no pair: the heads are the lead's own provider

        rig.Send(Text);
        rig.Complete();
        rig.Send(Text);

        var inputs = rig.Session.Inputs.Select(input => input.Text).ToArray();
        Assert.Equal(2, inputs.Length);
        var policy = HydraPrompts.FallbackPolicy(launch);
        if (!KeepsPolicy(provider))
        {
            // No system prompt of its own to hold it: the whole policy rides on EVERY message, behind anti-slop on the first only.
            Assert.Equal(AntiSlopPolicy.EnabledInstructions + "\n\n" + HydraPrompts.DelegationRequest(launch) + HydraPrompts.FallbackPreamble(launch, team) + Text, inputs[0]);
            Assert.Equal(HydraPrompts.DelegationRequest(launch) + HydraPrompts.FallbackPreamble(launch, team) + Text, inputs[1]);
            Assert.All(inputs, input => Assert.Contains(policy, input, StringComparison.Ordinal));
            return;
        }

        // The policy lives in the session's system prompt: never in a message, and no anti-slop fallback either.
        var expected = launch.RunsNatively
            ? HydraPrompts.DelegationRequest(launch) + Text // native heads: no team, no policy, no merge note
            : HydraPrompts.DelegationRequest(launch) + HydraPrompts.FallbackTurnNote(team) + Text; // API-key provider: the short team note
        Assert.Equal(new[] { expected, expected }, inputs);
        Assert.All(inputs, input =>
        {
            Assert.DoesNotContain(policy, input, StringComparison.Ordinal);
            Assert.DoesNotContain(AntiSlopPolicy.EnabledInstructions, input, StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(EveryProvider))]
    [Trait("Req", "PR-HYDRA-060")] // ThreadRuntime.swift:1303-1307 (report turns: fallbackReportNote or fallbackReportPreamble) and :1308-1310
    public void A_report_turn_carries_the_whole_report_preamble_only_when_the_provider_has_nowhere_else_to_keep_the_policy(ProviderKind provider)
    {
        using var rig = Rig(provider);
        var head = AddFinishedHead(rig);
        var launch = Launch(rig);
        var team = HydraPrompts.TeamStatus([head]);

        rig.Send("The reports.", hydraHeads: [1]);

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        if (!KeepsPolicy(provider))
        {
            Assert.Equal(AntiSlopPolicy.EnabledInstructions + "\n\n" + HydraPrompts.FallbackReportPreamble(launch, team, canDelegate: true) + "The reports.", prompt);
            Assert.Contains(HydraPrompts.FallbackPolicy(launch), prompt, StringComparison.Ordinal);
            return;
        }
        // A report turn never asks for delegation; a native lead (heads of its own, no merge status) adds nothing at all.
        Assert.Equal(launch.RunsNatively ? "The reports." : HydraPrompts.FallbackReportNote(team, canDelegate: true) + "The reports.", prompt);
        Assert.DoesNotContain(HydraPrompts.FallbackPolicy(launch), prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Copilot)]
    [Trait("Req", "PR-HYDRA-060")] // ThreadRuntime.swift:1298 `!launch.runsNatively`: native providers whose heads run ELSEWHERE take the team note, not the policy
    public void A_lead_whose_heads_run_elsewhere_gets_the_team_note_and_never_the_policy(ProviderKind provider)
    {
        var pair = new HydraPair(provider) { WorkerProvider = ProviderKind.Cursor };
        using var rig = Rig(provider, pair);
        var head = AddFinishedHead(rig);
        var launch = Launch(rig);
        Assert.False(launch.RunsNatively);
        Assert.Equal(ProviderKind.Cursor, launch.HeadsProvider);
        var team = HydraPrompts.TeamStatus([head]);

        rig.Send(Text);
        rig.Complete();
        rig.Send("The reports.", hydraHeads: [1]);

        var inputs = rig.Session.Inputs.Select(input => input.Text).ToArray();
        Assert.Equal(HydraPrompts.DelegationRequest(launch) + HydraPrompts.FallbackTurnNote(team) + Text, inputs[0]);
        Assert.Equal(HydraPrompts.FallbackReportNote(team, canDelegate: true) + "The reports.", inputs[1]);
        Assert.All(inputs, input => Assert.DoesNotContain(HydraPrompts.FallbackPolicy(launch), input, StringComparison.Ordinal));
    }
}
