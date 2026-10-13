using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-10): one thread of a launch's own model with a scripted offline session installed the way EnsureSession would have left
/// it (the same as ThreadRuntimeTurnRig, which makes a model of its own and cannot share the window manager's). With the launch's
/// WindowManager this lets a test close the real main window and still drive a turn through the model. Helper code only (R-TEST-3).
/// </summary>
public sealed class WindowedThreadRig
{
    public WindowedThreadRig(LaunchHarness launch, Guid thread)
    {
        Launch = launch;
        H = launch.Harness;
        App = launch.Manager.Model;
        Thread = thread;
        Runtime = H.Main.OnMain(() => App.Runtime(thread));
        H.Main.Loop.Run(() => Runtime.EnsureLoaded(), AppModelHarness.Bound);
        Session = new ThreadRuntimeScriptedSession();
        H.Main.OnMain(() =>
        {
            var current = App.Thread(thread)!;
            Runtime.Observe(Session);
            var keeps = (bool)typeof(ThreadRuntime).GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [current.Provider])!;
            var directory = current.WorktreePath ?? App.Project(current.ProjectID)!.Path;
            var signature = typeof(ThreadRuntime).GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [current, directory, App.Settings.AntiSlopEnabled, keeps ? App.HydraLaunch(current) : null]);
            ThreadRuntimeReflection.Set(Runtime, "session", Session);
            ThreadRuntimeReflection.Set(Runtime, "sessionSignature", signature);
        });
    }

    /// <summary>
    /// LaunchSurfaceRig.WithLaunch, with the clean-up's pending UI jobs run on the main actor: a window that showed a composer leaves a posted text
    /// change that sets the thread's draft, and the model only takes that from the main actor.
    /// </summary>
    public static void WithLaunch(HeadlessSession headless, Action<AppModelHarness>? prepare, Action<LaunchHarness> body) => headless.OnUiThread(() =>
    {
        var page = Marrow.App.Views.Settings.SettingsNavigation.Shared.RequestedPage;
        using var launch = new LaunchHarness(prepare);
        try
        {
            body(launch);
        }
        finally
        {
            launch.Harness.Main.OnMain(() => Marrow.App.Views.Settings.SettingsNavigation.Shared.RequestedPage = page);
            for (var pass = 0; pass < 3; pass++) launch.Harness.Main.OnMain(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
        }
    });

    public LaunchHarness Launch { get; }

    public AppModelHarness H { get; }

    public AppModel App { get; }

    public Guid Thread { get; }

    public ThreadRuntime Runtime { get; }

    public ThreadRuntimeScriptedSession Session { get; }

    /// <summary>The user sends <paramref name="text"/>, the provider answers <paramref name="reply"/>, and the turn ends.</summary>
    public void Turn(string text, string reply)
    {
        Task task = null!;
        H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(Runtime, "StartTurn", text, (IReadOnlyList<Attachment>)[], null, false)!);
        H.Main.Loop.Run(() => task, AppModelHarness.Bound);
        H.Main.OnMain(() =>
        {
            Session.Emit(new ProviderEvent.MessageCompleted("reply-" + Guid.NewGuid().ToString("N")[..6], reply));
            Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        H.Main.Loop.RunUntil(() => Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
    }

    /// <summary>What the assistant has said in the thread, in order.</summary>
    public IReadOnlyList<string> Said() => H.Main.OnMain(() => (IReadOnlyList<string>)[.. Runtime.Entries.Select(e => e.Item.TimelineItemContent).OfType<TimelineItem.Content.Assistant>().Select(a => a.Value.Text)]);
}
