// PORT-ONLY: first-release feature unit UE-5 (BUILD-PLAN.md 2.4; OD-0100: Marrow itself, through Velopack, is one item of "Update everything"). Marrow as an IUpdateItem over the UE-1 pieces.
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The running Marrow as an update item. The installed version is the running one (once a newer package is downloaded, the version that will run after the restart, so the row reads "1.1.0, Restart to finish" as the design page draws it); the latest is what the feed offers (the <see cref="UpdateChecker"/>'s check, which reads the
/// engine); updating downloads the offered package through the engine with its percent as the row's progress, and asks for a restart (Marrow's is the one update that does,
/// OD-0100 "only Marrow needs a restart"); <see cref="Restart"/> ("Restart Marrow") hands the downloaded package to the applier (<see cref="AppUpdater.ApplyDownloaded"/>).
/// ENGINEERING DECISIONS (ED-344): a run that is not a Velopack install has nothing to update in place, so Marrow is not installed as far as this list is concerned (no row);
/// a feed that cannot be reached names no latest version ("Checks when updated"); an update with no newer version known is refused, never guessed.
/// </summary>
public sealed class MarrowUpdateItem(UpdateChecker checker, ISelfUpdateEngine engine, Action<string, string> apply) : IUpdateItem
{
    private string? package;
    private string? version;

    public string Id => "marrow";

    public string Title => "Marrow";

    public UpdateItemKind Kind => UpdateItemKind.Marrow;

    public Task<string?> InstalledVersion(CancellationToken cancellation) =>
        Task.FromResult(engine.IsInstalled ? version ?? checker.CurrentVersion : null);

    public async Task<string?> LatestVersion(CancellationToken cancellation)
    {
        if (!engine.IsInstalled) return null;
        await checker.Check();
        if (checker.LastError is not null) return null;
        return checker.UpdateAvailable && checker.Update is { } update ? update.Version : checker.CurrentVersion;
    }

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        if (!checker.UpdateAvailable || checker.Update is not { } update) throw new InvalidOperationException("No newer Marrow is known, so there is nothing to download.");
        try
        {
            package = await engine.DownloadAsync(update.Version, percent => progress.Report(SelfUpdateProgress.Fraction(percent)), cancellation);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new InvalidOperationException($"The update could not be downloaded. {error.Message}", error); // AppUpdater.swift:523-527's sentence, as DownloadPackage words it
        }
        version = update.Version;
        return new UpdateOutcome(true, update.Version, true, true, null);
    }

    /// <summary>"Restart Marrow": applies the package the last update downloaded. Nothing downloaded, nothing to do.</summary>
    public void Restart()
    {
        if (package is { } downloaded && version is { } offered) apply(downloaded, offered);
    }
}
