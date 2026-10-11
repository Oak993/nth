// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1-6.2; ED-NEW-web-w12-1; owner 2026-10-11 "Visus and Tabula
// approved"). Program.Main's one Bind line: the process's artifact store lives under the threads folder (each thread's
// data folder holds its artifacts), and a tool call asks the chat's runtime, on the main actor, for the running turn and
// its prompt. From here every chat session gets the nth-ui server (CLI providers) or the native artifact tools (API
// providers), and every answer runs through the fenced fallback.
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Services.Web;

namespace Marrow.App.Port.Tabula;

public static class ArtifactRuntimeStartup
{
    public static void Bind() =>
        ArtifactHost.Shared.SetProcessDefault(new ArtifactHost(new ArtifactStore(() => Storage.ThreadsDirectory),
            thread => MainActor.Run(() => Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(thread)?.ArtifactTurn())));
}
