using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeHydraPromptDeliveryTests
{
    [Theory]
    [InlineData(ProviderKind.Cursor, false)]
    [InlineData(ProviderKind.Cursor, true)]
    [InlineData(ProviderKind.Meta, false)]
    [InlineData(ProviderKind.Meta, true)]
    [InlineData(ProviderKind.Codex, false)]
    [InlineData(ProviderKind.Codex, true)]
    [Trait("Req", "PR-RUNTIME-069")]
    [Trait("Req", "PR-RUNTIME-070")]
    public void User_and_report_turns_deliver_their_distinct_Hydra_policy_to_the_session(
        ProviderKind provider, bool reportTurn)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project) with
        {
            Provider = provider, Model = "offline-model", HydraEnabled = true,
        };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var captured = new HydraPromptCaptureSession();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAlwaysHeads = false;
            model.Settings.HydraQueueHeads = false;
            model.Settings.HydraIsolateHeads = false;
            model.Settings.HydraAutoMerge = false;
            model.Settings.AntiSlopEnabled = false;
            var current = model.Thread(thread.Id)!;
            var launch = model.HydraLaunch(current);
            Assert.NotNull(launch);
            var type = typeof(ThreadRuntime);
            var keepsPolicy = (bool)type.GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [provider])!;
            var signature = type.GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [current, project.Path, false, keepsPolicy ? launch : null]);
            type.GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, captured);
            type.GetField("sessionSignature", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, signature);
            if (reportTurn)
            {
                // Use the real head-to-parent entry point: it schedules the lead's automatic report turn.
                runtime.HydraHeadFinished(Guid.NewGuid(),
                    new HydraHeadInfo(0, "Finished task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Queued),
                    HydraHeadInfo.Status.Completed, "The requested work is ready.");
            }
            else
            {
                runtime.Draft = new ComposerDraft { Text = "Do a broad multi-file change." };
                runtime.Send();
            }
        });
        h.Main.Loop.RunUntil(() => captured.Inputs.Count != 0, AppModelHarness.Bound);
        var prompt = Assert.Single(captured.Inputs).Text;
        if (reportTurn)
        {
            Assert.DoesNotContain("[Hydra setting: enabled]", prompt, StringComparison.Ordinal);
            if (provider != ProviderKind.Codex)
                Assert.Contains("Your heads reported back below.", prompt, StringComparison.Ordinal);
        }
        else
        {
            // ThreadRuntime.swift:1325-1331 puts the anti-slop note (also when it is off) in front of the
            // turn for a provider that does not keep the Hydra policy in its system prompt (Cursor), after
            // the delegation request at :1319-1321 was prepended.
            Assert.StartsWith(provider == ProviderKind.Cursor
                ? "[nth anti-slop guidance: off]"
                : "[Hydra setting: enabled]", prompt, StringComparison.Ordinal);
            Assert.Contains("[Hydra setting: enabled]", prompt, StringComparison.Ordinal);
            Assert.Contains(provider == ProviderKind.Codex
                ? "Use the configured Hydra agent tools and head profiles."
                : "Delegate through the final hydra block so nth runs the selected heads; do not use native agent tools.",
                prompt, StringComparison.Ordinal);
            Assert.EndsWith("Do a broad multi-file change.", prompt, StringComparison.Ordinal);
        }
    }
}
