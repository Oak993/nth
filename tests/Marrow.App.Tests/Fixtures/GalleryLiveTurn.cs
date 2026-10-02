using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Window.Native;

// A turn that is under way, on a thread's real runtime: a scripted offline session (the one the runtime tests use) is installed with the
// launch signature the runtime computes, a prompt is sent through the runtime's own StartTurn, and the provider's events are emitted
// as a CLI would stream them (reasoning, a running command with output, the start of a reply). The turn is left running, so the
// thread view shows what it shows while an agent works: the working line with its thinking, the live tool row, the stop button.
internal static class GalleryLiveTurn
{
    /// <summary>Starts the turn on the UI thread (the runtime's StartTurn runs on until it has sent the prompt, a few awaits in).</summary>
    internal static (ThreadRuntimeScriptedSession Session, Task Started) Start(AppModel model, ChatThread thread, string prompt)
    {
        var runtime = model.Runtime(thread.Id);
        var session = new ThreadRuntimeScriptedSession();
        runtime.Observe(session);
        var current = model.Thread(thread.Id)!;
        var keeps = (bool)typeof(ThreadRuntime).GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [current.Provider])!;
        var directory = model.Project(current.ProjectID)!.Path;
        var signature = typeof(ThreadRuntime).GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [current, directory, model.Settings.AntiSlopEnabled, keeps ? model.HydraLaunch(current) : null]);
        ThreadRuntimeReflection.Set(runtime, "session", session);
        ThreadRuntimeReflection.Set(runtime, "sessionSignature", signature);
        var started = ThreadRuntimeReflection.Call<Task>(runtime, "StartTurn", prompt, new List<Attachment>(), null, false)!;
        return (session, started);
    }

    /// <summary>Streams the first events of the turn as a CLI would; the turn is left running.</summary>
    internal static void Stream(ThreadRuntimeScriptedSession session)
    {
        session.Emit(new ProviderEvent.TurnStarted(null));
        session.Emit(new ProviderEvent.ReasoningDelta("r1", "The workspace file and the lockfile are in place. CI still installs with npm, so the job needs the pnpm setup step and a cache for the store. "));
        session.Emit(new ProviderEvent.ReasoningDelta("r1", "I should check how the workflow caches today before changing it."));
        session.Emit(new ProviderEvent.ToolStarted("t0", new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = "Read .github/workflows/ci.yml" }));
        session.Emit(new ProviderEvent.ToolUpdated("t0", new ToolUpdate { Status = ToolCall.Status.Completed }));
        session.Emit(new ProviderEvent.ToolStarted("t1", new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run pnpm install --frozen-lockfile", Detail = "pnpm install --frozen-lockfile" }));
        session.Emit(new ProviderEvent.ToolOutput("t1", "Lockfile is up to date, resolution step is skipped\nProgress: resolved 1, reused 0, downloaded 0, added 0\npackages/web | + react 18.3.1, + vite 5.4.2\n"));
        session.Emit(new ProviderEvent.MessageDelta("m1", "I'm updating the CI workflow now: the pnpm setup step first, then the cache for the store"));
    }
}
