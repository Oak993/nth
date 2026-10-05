// PORT-ONLY: UE-3, OD-0100; test-first API. Implemented after the recorded acceptance tests fail.
using Marrow.Core.Port.Updates;

namespace Marrow.Services.Port.Updates;

public sealed class WingetUpdater : IUpdateItem
{
    public WingetUpdater(string id, string title, Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, IReadOnlyList<string>? query = null) { }
    public string Id => throw new NotImplementedException();
    public string Title => throw new NotImplementedException();
    public UpdateItemKind Kind => UpdateItemKind.DevTool;
    public static IReadOnlyList<WingetPackage> ParseList(DevToolCommandResult result) => throw new NotImplementedException();
    public static DevToolCommand UpgradeCommand(string id) => throw new NotImplementedException();
    public Task<string?> InstalledVersion(CancellationToken cancellation) => throw new NotImplementedException();
    public Task<string?> LatestVersion(CancellationToken cancellation) => throw new NotImplementedException();
    public Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation) => throw new NotImplementedException();
}
