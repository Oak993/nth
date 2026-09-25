using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimePart10Tests
{
    private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Theory]
    [Trait("Req", "PR-RUNTIME-239")]
    [Trait("Req", "PR-RUNTIME-240")]
    // App/Runtime/ThreadRuntime.swift:1432-1451
    [InlineData(ProviderKind.Cursor, RuntimeMode.FullAccess, RuntimeMode.FullAccess, null)]
    [InlineData(ProviderKind.Grok, RuntimeMode.AutoAcceptEdits, RuntimeMode.AutoAcceptEdits, null)]
    [InlineData(ProviderKind.Devin, RuntimeMode.Supervised, RuntimeMode.Supervised, null)]
    [InlineData(ProviderKind.Antigravity, RuntimeMode.Auto, RuntimeMode.Auto, "high")]
    [InlineData(ProviderKind.Copilot, RuntimeMode.Auto, RuntimeMode.Auto, null)]
    [InlineData(ProviderKind.Copilot, RuntimeMode.FullAccess, null, null)]
    [InlineData(ProviderKind.Claude, RuntimeMode.FullAccess, null, "high")]
    [InlineData(ProviderKind.Codex, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.Opencode, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.OpencodeGo, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.Deepseek, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.Meta, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.Zai, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.Commandcode, RuntimeMode.Auto, null, null)]
    [InlineData(ProviderKind.Pi, RuntimeMode.Auto, null, null)]
    public void Session_signature_carries_only_the_provider_launch_flags(
        ProviderKind provider, RuntimeMode runtimeMode, RuntimeMode? expectedLaunchMode, string? expectedEffort)
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Runtime signature", harness.NewFolder("signature"));
        var thread = AppModelSamples.Thread(project) with
        {
            Provider = provider,
            RuntimeMode = runtimeMode,
            Effort = "high",
        };
        var makeSignature = typeof(ThreadRuntime).GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(makeSignature);
        // Inspect the value seam directly: this contract does not require resolving or starting a provider CLI.
        var signature = makeSignature.Invoke(null, [thread, project.Path, false, null]);
        Assert.NotNull(signature);
        Assert.Equal(expectedLaunchMode, ReadMember(signature, "LaunchRuntimeMode"));
        Assert.Equal(expectedEffort, ReadMember(signature, "LaunchEffort"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-238")] // App/Runtime/ThreadRuntime.swift:1042-1044
    public void Idle_lead_keeps_queued_followups_for_its_own_next_turn()
    {
        using var runtimeScope = new TestRuntime();
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        var queued = new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = "keep this queued" }]);
        WriteMember(runtime, "phase", RuntimePhase.Idle);
        WriteMember(runtime, "followUps", queued);

        runtimeScope.OnMain(() => InvokePrivate(runtime, "DispatchQueuedFollowUps"));

        var after = Assert.IsType<ValueList<FollowUpPrompt>>(ReadMember(runtime, "followUps"));
        Assert.Equal(queued.Select(prompt => prompt.Text), after.Select(prompt => prompt.Text));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-234")]
    [Trait("Req", "PR-RUNTIME-235")]
    // App/Runtime/ThreadRuntime.swift:2670-2682
    public void A_droppy_head_report_uses_its_start_time_and_replaces_its_own_pending_report()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Head report", harness.NewFolder("head-report"));
        var lead = AppModelSamples.Thread(project);
        var headID = Guid.NewGuid();
        var startedAt = SwiftDate.Now;
        var info = new HydraHeadInfo(6, "inspect the change", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Queued)
        {
            StartedAt = startedAt,
            ToolCalls = 8,
        };
        var head = AppModelSamples.Thread(project) with
        {
            Id = headID,
            ParentThreadID = lead.Id,
            Hydra = info,
        };
        harness.WriteLibrary([project], [lead, head]);
        AppModelHarness.WriteDocument(lead.Id);
        AppModelHarness.WriteDocument(headID);
        var app = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => new ThreadRuntime(lead.Id, app));

        harness.Main.Advance(TimeSpan.FromSeconds(7));
        harness.Main.OnMain(() =>
        {
            WriteMember(runtime, "phase", RuntimePhase.Running);
            runtime.HydraHeadFinished(headID, info, HydraHeadInfo.Status.Failed, summary: null, copyPath: "C:\\work\\head");
            runtime.HydraHeadFinished(headID, info, HydraHeadInfo.Status.Stopped, summary: "replacement", copyPath: null);
        });

        var reports = Assert.IsAssignableFrom<IEnumerable<HydraReport>>(ReadMember(runtime, "hydraPendingReports"));
        var report = Assert.Single(reports);
        Assert.Equal(6, report.HeadIndex);
        Assert.Equal("inspect the change", report.Task);
        Assert.Equal(HydraHeadInfo.Origin.Queued, report.Origin);
        Assert.Equal(HydraHeadInfo.Status.Stopped, report.Status);
        Assert.Equal("replacement", report.Text);
        Assert.Null(report.CopyPath);
        Assert.Equal(7d, report.Elapsed);
        Assert.Equal(8, report.ToolCalls);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-237")] // App/Runtime/ThreadRuntime.swift:1028-1040
    public void Dispatch_head_refuses_to_send_when_the_app_is_unavailable()
    {
        using var runtimeScope = new TestRuntime();
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        var prompt = new FollowUpPrompt { Text = "delegate this" };

        var sent = runtimeScope.OnMain(() => (bool)InvokePrivate(runtime, "DispatchHead", prompt, HydraHeadInfo.Origin.Sent)!);

        Assert.False(sent);
    }

    private static object? ReadMember(object instance, string name) =>
        instance.GetType().GetField(name, InstanceMembers)?.GetValue(instance)
        ?? instance.GetType().GetProperty(name, InstanceMembers)?.GetValue(instance);

    private static void WriteMember(object instance, string name, object? value)
    {
        var field = instance.GetType().GetField(name, InstanceMembers);
        Assert.NotNull(field);
        field.SetValue(instance, value);
    }

    private static object? InvokePrivate(object instance, string name, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(instance, arguments);
    }
}
