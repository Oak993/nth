// PORT-ONLY: first-release feature unit SK-16 (DL-19, ED-326 (6)(a), ED-NEW-f-sk-13 item 4: "the download seam, the binding seam and the licence-text seam are unbound ... until SK-16 binds them").
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;

namespace Marrow.Services.Port.Skills.Fetch;

public static class SkillsProductionSeams
{
    /// <summary>
    /// The options with the download, binding and licence-text seams bound to the one downloader (the catalogue's host list, ONDEMAND 3.2) over <see cref="CatalogueInstallPlans"/>, and (RR-14,
    /// ED-NEW-dl-8) the installers' process seam bound to <see cref="CatalogueInstallProcess"/>, whose allow-list is exactly the executables the catalogue's installers run. A test passes its own
    /// downloader (SK-0's loopback fake), raw base and process; the app passes none. Everything else of the options is as it was.
    /// </summary>
    public static SkillsHostOptions WithDownloads(this SkillsHostOptions options, string dataRoot, Downloader? downloader = null, Uri? rawBase = null, IInstallProcess? process = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var plans = new CatalogueInstallPlans(dataRoot, rawBase, options.Catalogue);
        downloader ??= new Downloader(ManualFiles.PartFolder(dataRoot), DownloadPolicy.Catalogue());
        var source = new PipelineDownloadSource(downloader, plans.Plan, plans.Requests);
        return options with
        {
            Downloads = source.DownloadAsync, Bindings = plans.Bind, LicenceText = new DownloaderLicenceText(downloader, dataRoot), Installable = CatalogueInstallPlans.CanPlan,
            InstallProcess = process ?? new CatalogueInstallProcess(dataRoot, options.Catalogue),
        };
    }
}
