// PORT-ONLY: UE-3, OD-0100; exact argv from trial H. An asynchronous Office request is never a verified installation by exit code alone.
using Marrow.Core.Port.Updates;

namespace Marrow.Services.Port.Updates;

public sealed class OfficeC2RUpdater : IUpdateItem
{
    private readonly Func<CancellationToken, Task<OfficeInstallation?>> read;
    private readonly Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>> run;
    public OfficeC2RUpdater(Func<CancellationToken, Task<OfficeInstallation?>>? read = null, Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.read = read ?? RefuseRegistry;
        this.run = run ?? DevToolProcess.Refuse;
        // The optional former delay seam remains source-compatible. Later checks now belong to UE-2/the shared service.
    }
    internal static Task<OfficeInstallation?> RefuseRegistry(CancellationToken cancellation) => throw new InvalidOperationException("The Office registry seam is not configured.");
    public string Id => "dev:office";
    public string Title => "Office";
    public UpdateItemKind Kind => UpdateItemKind.DevTool;
    public static DevToolCommand UpgradeCommand(string path) => new(path, ["/update", "user", "updatepromptuser=false", "forceappshutdown=false", "displaylevel=false"]);
    public async Task<string?> InstalledVersion(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return (await read(cancellation))?.Version;
    }
    public Task<string?> LatestVersion(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null); // No supported public listing tied to this installed product/channel was proven by the trial.
    }
    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var before = await read(cancellation);
        if (before is null) return new(false, null, false, false, "Office is no longer installed.");
        if (!before.ClientExists) return new(false, before.Version, false, false, "Office's Click-to-Run updater is unavailable.");
        progress.Report(null);
        var result = await run(UpgradeCommand(before.ClientPath), cancellation);
        if (result.ExitCode == 0 && cancellation.IsCancellationRequested) return Pending(before.Version);
        OfficeInstallation? after;
        try { after = await read(cancellation); }
        catch (Exception error) when (result.ExitCode == 0 && error is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Pending(before.Version); // The accepted asynchronous request survives an unavailable immediate registry read.
        }
        if (result.ExitCode == 0 && cancellation.IsCancellationRequested) return Pending(before.Version);
        cancellation.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) return new(false, after?.Version, false, false, WingetUpdater.Failure(result));
        var confirmed = after is not null && after.Version != before.Version;
        return confirmed ? new(true, after!.Version, true, false, null) : Pending(before.Version);
    }

    private static UpdateOutcome Pending(string version) => new(true, version, false, false, "Updating in the background", UpdatingInBackground: true);
}
