using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The launch signature ensureSession builds (App/Runtime/ThreadRuntime.swift:1440-1459, fields declared :510-526): the
// Claude fast flag and the three Antigravity launch arguments. A session is reused only while its signature is unchanged.
public sealed class ThreadRuntimeSessionSignatureTests
{
    public static TheoryData<ProviderKind> AllProviders()
    {
        var data = new TheoryData<ProviderKind>();
        foreach (var provider in Enum.GetValues<ProviderKind>()) data.Add(provider);
        return data;
    }

    private static ChatThread Thread(ProviderKind provider) =>
        AppModelSamples.Thread(AppModelSamples.Project("P", "C:/work")) with
        {
            Provider = provider,
            Model = "model-a",
            FastMode = false,
            InteractionMode = InteractionMode.Build,
        };

    private static (ProviderSession? Session, Exception? Error) Ensure(ThreadRuntimeTurnRig rig)
    {
        Task task = null!;
        rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
        rig.H.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);
        return task.IsFaulted ? (null, task.Exception!.InnerException) : ((ProviderSession)task.GetType().GetProperty("Result")!.GetValue(task)!, null);
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    [Trait("Req", "PR-RUNTIME-241")] // :1452 launchFast: thread.provider == .claude ? thread.fastMode : nil
    public void The_fast_flag_is_part_of_the_signature_for_claude_only_and_off_is_false_not_nil(ProviderKind provider)
    {
        using var h = new AppModelHarness();
        foreach (var fast in new[] { false, true })
        {
            var value = ThreadRuntimeSignatureProbe.Field(ThreadRuntimeSignatureProbe.Make(Thread(provider) with { FastMode = fast }), "LaunchFast");
            if (provider == ProviderKind.Claude) Assert.Equal(fast, Assert.IsType<bool>(value)); // Bool?, so false is a value
            else Assert.Null(value);
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-241")] // a changed fast mode restarts Claude and no other provider (via signature equality)
    public void Changing_fast_mode_changes_the_signature_of_claude_only()
    {
        using var h = new AppModelHarness();
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var before = ThreadRuntimeSignatureProbe.Make(Thread(provider));
            var after = ThreadRuntimeSignatureProbe.Make(Thread(provider) with { FastMode = true });
            Assert.Equal(provider != ProviderKind.Claude, before.Equals(after));
        }
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    [Trait("Req", "PR-RUNTIME-242")] // :1453 launchModel: thread.provider == .antigravity ? thread.model : nil
    public void The_model_is_part_of_the_signature_for_antigravity_only(ProviderKind provider)
    {
        using var h = new AppModelHarness();
        var value = ThreadRuntimeSignatureProbe.Field(ThreadRuntimeSignatureProbe.Make(Thread(provider)), "LaunchModel");
        if (provider == ProviderKind.Antigravity) Assert.Equal("model-a", value);
        else Assert.Null(value);

        var before = ThreadRuntimeSignatureProbe.Make(Thread(provider));
        var after = ThreadRuntimeSignatureProbe.Make(Thread(provider) with { Model = "model-b" });
        Assert.Equal(provider != ProviderKind.Antigravity, before.Equals(after));
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    [Trait("Req", "PR-RUNTIME-243")] // :1454 launchInteraction: thread.provider == .antigravity ? thread.interactionMode : nil
    public void The_interaction_mode_is_part_of_the_signature_for_antigravity_only(ProviderKind provider)
    {
        using var h = new AppModelHarness();
        var value = ThreadRuntimeSignatureProbe.Field(ThreadRuntimeSignatureProbe.Make(Thread(provider)), "LaunchInteraction");
        if (provider == ProviderKind.Antigravity) Assert.Equal(InteractionMode.Build, value);
        else Assert.Null(value);

        var before = ThreadRuntimeSignatureProbe.Make(Thread(provider));
        var after = ThreadRuntimeSignatureProbe.Make(Thread(provider) with { InteractionMode = InteractionMode.Plan });
        Assert.Equal(provider != ProviderKind.Antigravity, before.Equals(after));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-242")] // a model change restarts Antigravity (--model at launch) and leaves a Claude session alone
    [Trait("Req", "PR-RUNTIME-243")] // a mode change restarts Antigravity (--mode at launch) and leaves a Claude session alone
    public void A_live_session_is_relaunched_by_an_antigravity_model_or_mode_change_and_reused_by_claude()
    {
        using var antigravity = new ThreadRuntimeTurnRig(ProviderKind.Antigravity);
        antigravity.H.Main.OnMain(() => antigravity.App.UpdateThread(antigravity.InitialThread.Id, t => t with { Model = "other-model" }));
        Ensure(antigravity);
        Assert.Equal(1, antigravity.Session.StopCalls); // released to be relaunched (no executable here, so it throws)

        using var modeChange = new ThreadRuntimeTurnRig(ProviderKind.Antigravity);
        modeChange.H.Main.OnMain(() => modeChange.App.UpdateThread(modeChange.InitialThread.Id, t => t with { InteractionMode = InteractionMode.Plan }));
        Ensure(modeChange);
        Assert.Equal(1, modeChange.Session.StopCalls);

        using var claude = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        claude.H.Main.OnMain(() => claude.App.UpdateThread(claude.InitialThread.Id, t => t with { Model = "other-model", InteractionMode = InteractionMode.Plan }));
        var (session, error) = Ensure(claude);
        Assert.Null(error);
        Assert.Same(claude.Session, session);
        Assert.Equal(0, claude.Session.StopCalls);
    }
}
