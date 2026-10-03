using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The composition order of the turn prompt in ThreadRuntime.startTurn: ThreadRuntime.swift:1291-1334 (B3 1.7.0; the record
// cites the B2 lines :1261-1304 of hydra-prompt-corpus.md section 10.6, the text and the order are the same). Every test runs
// a real StartTurn over an offline scripted session (Fixtures/ThreadRuntimeScriptedSession.cs) and reads what it was sent.
public sealed class ThreadRuntimeHydraPromptOrderTests
{
    private static readonly Attachment TextFile = new() { Name = "a.txt", Path = @"C:\work\a.txt", MimeType = "text/plain" };
    private static readonly Attachment Picture = new() { Name = "pic.png", Path = @"C:\work\pic.png", MimeType = "image/png" };

    // Hydra.swift:1008-1011: the held outcome with no unmerged file reads this, whatever the time (no clock in the text).
    private const string NothingLeft = "Merge state: nothing from this job is left to merge. Do not claim a new merge or promise another one.";

    private const string AttachedTail = "\n\nAttached files:\n- C:\\work\\a.txt";

    private static ThreadRuntimeTurnRig Rig(ProviderKind provider, bool autoMerge, bool antiSlop = true) =>
        new(provider, hydraOn: true, settings: app =>
        {
            app.Settings.HydraEnabled = true;
            app.Settings.HydraIsolateHeads = false;
            app.Settings.HydraAutoMerge = autoMerge;
            app.Settings.AntiSlopEnabled = antiSlop;
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

    private static void RecordHeldMerge(ThreadRuntimeTurnRig rig) =>
        rig.H.Main.OnMain(() => rig.Runtime.RecordHydraMerge(new HydraMergeRecord
        {
            At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Held,
            Project = null, Label = null, Url = null, Files = 0, Detail = null,
        }));

    private static HydraLaunch Launch(ThreadRuntimeTurnRig rig)
    {
        var thread = rig.Thread;
        return rig.H.Main.OnMain(() => rig.App.HydraLaunch(thread))!;
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1290-1334 a CLI lead: anti-slop, delegation request, preamble with team and merge, the text, the attachments
    public void A_CLI_leads_prompt_reads_anti_slop_then_delegation_then_preamble_then_text_then_the_attachment_list()
    {
        using var rig = Rig(ProviderKind.Cursor, autoMerge: true);
        var head = AddFinishedHead(rig);
        RecordHeldMerge(rig);
        var launch = Launch(rig);
        var team = HydraPrompts.TeamStatus([head]);

        rig.Send("Audit the module", [TextFile, Picture]);

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        var note = "[Hydra] " + team + " " + NothingLeft + "\n\n---\n\n";
        Assert.Equal(
            AntiSlopPolicy.EnabledInstructions + "\n\n"
            + HydraPrompts.DelegationRequest(launch)
            + HydraPrompts.FallbackPolicy(launch) + "\n\n" + note
            + "Audit the module" + AttachedTail,
            prompt);
        Assert.StartsWith(AntiSlopPolicy.EnabledInstructions + "\n\n[Hydra setting: enabled]\n", prompt, StringComparison.Ordinal);
        Assert.Contains("[Hydra is on] You lead ", prompt, StringComparison.Ordinal);
        Assert.Contains("Your heads so far: ", team, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1297 `launch.autoMerges ? mergeStatus(...) : nil` - the merge status is only computed when the launch auto-merges
    public void A_launch_that_does_not_auto_merge_puts_no_merge_state_in_the_note_even_when_one_is_recorded()
    {
        using var rig = Rig(ProviderKind.Cursor, autoMerge: false);
        var head = AddFinishedHead(rig);
        RecordHeldMerge(rig);

        rig.Send("Audit the module");

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        Assert.DoesNotContain("Merge state:", prompt, StringComparison.Ordinal);
        Assert.EndsWith("[Hydra] " + HydraPrompts.TeamStatus([head]) + "\n\n---\n\nAudit the module", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1308-1310 `else if merge != nil` on a native launch: the turn note carries the merge, team nil
    public void A_lead_running_its_own_heads_gets_the_merge_note_without_a_team_between_the_delegation_request_and_the_text()
    {
        using var rig = Rig(ProviderKind.Claude, autoMerge: true);
        AddFinishedHead(rig);
        RecordHeldMerge(rig);
        var launch = Launch(rig);

        rig.Send("Audit the module", [TextFile]);

        // Claude keeps its Hydra policy in its system prompt, so no anti-slop prefix and no preamble.
        Assert.Equal(
            HydraPrompts.DelegationRequest(launch) + "[Hydra] " + NothingLeft + "\n\n---\n\n" + "Audit the module" + AttachedTail,
            Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1308 a native launch with no merge status (nil) prepends no turn note
    public void A_lead_running_its_own_heads_with_no_merge_status_gets_only_the_delegation_request_in_front_of_the_text()
    {
        using var rig = Rig(ProviderKind.Claude, autoMerge: true);
        AddFinishedHead(rig);
        var launch = Launch(rig);

        rig.Send("Audit the module");

        Assert.Equal(HydraPrompts.DelegationRequest(launch) + "Audit the module", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1297-1308 a native launch that does not auto-merge computes no merge status, so no note
    public void A_lead_running_its_own_heads_that_does_not_auto_merge_gets_no_note_even_with_a_merge_recorded()
    {
        using var rig = Rig(ProviderKind.Claude, autoMerge: false);
        RecordHeldMerge(rig);
        var launch = Launch(rig);

        rig.Send("Audit the module");

        Assert.Equal(HydraPrompts.DelegationRequest(launch) + "Audit the module", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1303-1305 report turn (hydraHeads != nil): the report preamble, and no delegation request; anti-slop still leads
    public void A_report_turn_has_the_report_preamble_and_no_delegation_request_with_anti_slop_still_in_front()
    {
        using var rig = Rig(ProviderKind.Cursor, autoMerge: true);
        var head = AddFinishedHead(rig);
        RecordHeldMerge(rig);
        var launch = Launch(rig);
        var team = HydraPrompts.TeamStatus([head]);

        rig.Send("The reports.", [TextFile], hydraHeads: [1]);

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        Assert.Equal(
            AntiSlopPolicy.EnabledInstructions + "\n\n"
            + HydraPrompts.FallbackReportPreamble(launch, team, canDelegate: true, NothingLeft)
            + "The reports." + AttachedTail,
            prompt);
        Assert.DoesNotContain("[Hydra setting: enabled]", prompt, StringComparison.Ordinal);
        Assert.Contains("[Hydra] Your heads reported back below. " + team + " " + NothingLeft + " ", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1305-1308 a provider that keeps the policy in its system prompt: the team note, no preamble, and NO anti-slop (:1325)
    public void An_API_provider_lead_gets_the_turn_note_with_team_and_merge_but_no_preamble_and_no_anti_slop()
    {
        using var rig = Rig(ProviderKind.Deepseek, autoMerge: true);
        var head = AddFinishedHead(rig);
        RecordHeldMerge(rig);
        var launch = Launch(rig);
        var team = HydraPrompts.TeamStatus([head]);

        rig.Send("Audit the module", [TextFile, Picture]);

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        Assert.Equal(
            HydraPrompts.DelegationRequest(launch) + HydraPrompts.FallbackTurnNote(team, NothingLeft) + "Audit the module" + AttachedTail,
            prompt);
        Assert.DoesNotContain("[Hydra is on] You lead ", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(AntiSlopPolicy.EnabledInstructions, prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1303-1304 a report turn (hydraHeads != nil) from the same lead: the report note, no delegation request, no anti-slop
    public void The_same_lead_on_a_report_turn_gets_the_report_note_and_no_delegation_request()
    {
        using var rig = Rig(ProviderKind.Deepseek, autoMerge: true);
        var head = AddFinishedHead(rig);
        RecordHeldMerge(rig);
        var team = HydraPrompts.TeamStatus([head]);

        rig.Send("The reports.", [TextFile], hydraHeads: [1]);

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        Assert.Equal(HydraPrompts.FallbackReportNote(team, canDelegate: true, NothingLeft) + "The reports." + AttachedTail, prompt);
        Assert.DoesNotContain("[Hydra setting: enabled]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(AntiSlopPolicy.EnabledInstructions, prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1290-1294 the list is "\n\nAttached files:\n" then one "- PATH" per non-image attachment, in order; images are skipped
    public void Several_attachments_are_listed_one_per_line_in_order_and_images_are_left_out()
    {
        var second = new Attachment { Name = "b.md", Path = @"C:\work\b.md", MimeType = "text/markdown" };
        using var rig = Rig(ProviderKind.Cursor, autoMerge: false);

        rig.Send("Audit the module", [TextFile, Picture, second]);

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        Assert.EndsWith("Audit the module\n\nAttached files:\n- C:\\work\\a.txt\n- C:\\work\\b.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("pic.png", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1288-1334 with Hydra off there is no launch: only anti-slop (a CLI provider) in front of the text and the attachment list
    public void With_hydra_off_only_anti_slop_and_the_attachment_list_surround_the_text()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Cursor, hydraOn: false, settings: app => app.Settings.AntiSlopEnabled = true);

        rig.Send("Audit the module", [TextFile]);

        Assert.Equal(AntiSlopPolicy.EnabledInstructions + "\n\n" + "Audit the module" + AttachedTail, Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1325-1334 anti-slop only while it has not been sent: the next message starts at the delegation request
    public void The_anti_slop_prefix_is_only_the_outermost_layer_of_the_first_message()
    {
        using var rig = Rig(ProviderKind.Cursor, autoMerge: false);
        var launch = Launch(rig);

        rig.Send("one");
        rig.Complete();
        rig.Send("two");

        var inputs = rig.Session.Inputs.Select(input => input.Text).ToArray();
        Assert.StartsWith(AntiSlopPolicy.EnabledInstructions + "\n\n" + HydraPrompts.DelegationRequest(launch), inputs[0], StringComparison.Ordinal);
        Assert.StartsWith(HydraPrompts.DelegationRequest(launch), inputs[1], StringComparison.Ordinal);
        Assert.DoesNotContain("anti-slop", inputs[1], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("---\n\n" + "two", inputs[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-071")] // :1326 SlashCommand.name(in: text) == nil: a slash command gets no anti-slop, but the Hydra layers are still prepended
    public void A_slash_command_gets_no_anti_slop_but_keeps_the_delegation_request_and_preamble()
    {
        using var rig = Rig(ProviderKind.Cursor, autoMerge: false);
        var launch = Launch(rig);

        rig.Send("/review the diff");

        var prompt = Assert.Single(rig.Session.Inputs).Text;
        Assert.StartsWith(HydraPrompts.DelegationRequest(launch) + HydraPrompts.FallbackPolicy(launch), prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("anti-slop", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("\n\n---\n\n/review the diff", prompt, StringComparison.Ordinal);
    }
}
