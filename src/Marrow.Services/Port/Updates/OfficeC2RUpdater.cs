// PORT-ONLY: UE-3, OD-0100; test-first API. No test can reach Office.
using Marrow.Core.Port.Updates;

namespace Marrow.Services.Port.Updates;

public sealed class OfficeC2RUpdater : IUpdateItem
{
    public OfficeC2RUpdater(Func<CancellationToken, Task<OfficeInstallation?>>? read = null, Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, Func<TimeSpan, CancellationToken, Task>? delay = null) { }
    public string Id => "dev:office";
    public string Title => "Office";
    public UpdateItemKind Kind => UpdateItemKind.DevTool;
    public static DevToolCommand UpgradeCommand(string path) => throw new NotImplementedException();
    public Task<string?> InstalledVersion(CancellationToken cancellation) => throw new NotImplementedException();
    public Task<string?> LatestVersion(CancellationToken cancellation) => throw new NotImplementedException();
    public Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation) => throw new NotImplementedException();
}
