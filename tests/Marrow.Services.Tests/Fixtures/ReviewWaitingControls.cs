using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Sentinel transports: touching RPC or stdio fails immediately. No process, pipe, CLI or network is created.
public static class ReviewWaitingControls
{
    public static ProviderSession Session(ProviderKind kind, bool discarding, bool openConnection = false)
    {
        var configuration = new SessionConfiguration
        {
            Provider = kind, WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/offline")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        ProviderSession session;
        if (kind == ProviderKind.Codex)
        {
            var connection = new JSONRPCConnection((StdioProcess)RuntimeHelpers.GetUninitializedObject(typeof(StdioProcess)), false);
            Set(connection, "<IsClosed>k__BackingField", !openConnection);
            session = new CodexSession(configuration);
            Set(session, "connection", connection);
            Set(session, "threadID", "waiting-thread");
        }
        else
        {
            var process = (StdioProcess)RuntimeHelpers.GetUninitializedObject(typeof(StdioProcess));
            var launcher = (ProcessLauncher)RuntimeHelpers.GetUninitializedObject(typeof(ProcessLauncher));
            Set(launcher, "running", true);
            Set(process, "process", launcher);
            session = new PiSession(configuration);
            Set(session, "process", process);
            Set(session, "started", true);
        }
        Set(session, discarding ? "standbyDiscarding" : "standbyActive", true);
        return session;
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
