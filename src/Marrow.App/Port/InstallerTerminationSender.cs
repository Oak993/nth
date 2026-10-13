namespace Marrow.App.Port;

// PORT-ONLY: ED-NEW-inst-c7-6. A withdrawn old merge cannot resolve a newer quit.
internal sealed class InstallerTerminationSender(AppShell shell, string requestId, object generation) : ITerminationSender
{
    public void Reply(bool toApplicationShouldTerminate) => shell.ReplyForInstaller(requestId, generation, toApplicationShouldTerminate);
}
