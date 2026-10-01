using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// A dropped delegation block's notice rides on the front of the lead's next send (App/Runtime/ThreadRuntime.swift:1383-1390,
// B3; the record cites B2 :1345-1350). The turn is the real StartTurn over an offline scripted session.
public sealed class ThreadRuntimeDropNoticePromptTests
{
    private const string Message = "No heads went out.\nYour reply's delegation block was dropped: the turn failed before the heads could go out.";
    private const string Notice = "[Hydra] " + Message + "\n\n---\n\n";

    private static void Wait(ThreadRuntimeTurnRig rig, string message = Message) => rig.H.Main.OnMain(() =>
    {
        var budget = ThreadRuntimeReflection.Get<HydraDelegationBudget>(rig.Runtime, "hydraBudget")!;
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraBudget", budget.NoteDropped(message));
    });

    private static string? Waiting(ThreadRuntimeTurnRig rig) => rig.H.Main.OnMain(() =>
        ThreadRuntimeReflection.Get<HydraDelegationBudget>(rig.Runtime, "hydraBudget")!.DropNotice);

    [Fact]
    [Trait("Req", "PR-RUNTIME-259")] // :1383-1390 notice + prompt, in front of every other prefix, spent on exactly one send; the user row shows the typed text
    public void A_waiting_notice_goes_in_front_of_the_whole_prompt_of_the_next_send_once()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Cursor, settings: app => app.Settings.AntiSlopEnabled = true);
        Wait(rig);

        rig.Send("Fix the parser");

        // After every other prefix (here the anti-slop policy) is in place: the notice leads the whole prompt.
        var first = Assert.Single(rig.Session.Inputs).Text;
        Assert.Equal(Notice + AntiSlopPolicy.Instructions(true) + "\n\n" + "Fix the parser", first);
        Assert.Null(Waiting(rig));
        Assert.Equal(["Fix the parser"], rig.H.Main.OnMain(() => rig.Runtime.SentPrompts.ToArray())); // the row shows the typed words, not the notice

        rig.Complete();
        rig.Send("And the lexer");

        Assert.DoesNotContain("[Hydra]", rig.Session.Inputs[1].Text, StringComparison.Ordinal); // spent
        Assert.Equal("And the lexer", rig.Session.Inputs[1].Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-259")] // :1383-1390 no slash-command guard and no user-turn guard: a slash command carries it too
    public void A_slash_command_carries_the_waiting_notice_as_well()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Cursor);
        Wait(rig);

        rig.Send("/review the diff");

        Assert.Equal(Notice + "/review the diff", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-259")] // :1386-1390 if session.send then throws, the notice is not put back
    public void A_notice_whose_send_failed_is_not_put_back()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Cursor, settings: app => app.Settings.AntiSlopEnabled = false);
        rig.Session.SendFailure = new ProviderError(new ProviderError.Case.Failed("The provider went away."));
        Wait(rig);

        rig.Send("Fix the parser");

        Assert.Equal(Notice + AntiSlopPolicy.Instructions(false) + "\n\n" + "Fix the parser", Assert.Single(rig.Session.Inputs).Text); // it was on the send that failed
        Assert.Null(Waiting(rig)); // and is gone
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-259")] // :1383-1384 taken only once the send is about to run: a turn that fails while starting keeps it waiting
    public void A_turn_that_fails_while_starting_leaves_the_notice_waiting()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Cursor, install: false); // no session and no executable: the start fails
        Wait(rig);

        rig.Send("Fix the parser");

        Assert.Equal(Notice, Waiting(rig) ?? "");
        Assert.Empty(rig.Session.Inputs);
    }
}
