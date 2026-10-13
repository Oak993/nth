using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.App.Tests.Port;

// ED-NEW-perf-startup-structure-3 and -5: the skills catalogue, the SF-symbol mapping and (with a wallpaper saved) the wallpaper catalogue are read on the pool at launch, not by the
// UI thread before the first window. Each prime hands exactly one job to the runner it is given and runs nothing itself.
[Collection("Headless")]
public sealed class StartupPrimesTests(HeadlessSession headless)
{
    private static (Func<Action, Task> Run, List<Action> Jobs) Recorder()
    {
        var jobs = new List<Action>();
        return (work => { jobs.Add(work); return Task.CompletedTask; }, jobs);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-3")] // ED-NEW-perf-startup-structure-3
    public async Task The_skills_catalogue_prime_hands_the_runner_one_job_that_makes_the_shared_parse_off_the_calling_thread()
    {
        var (run, jobs) = Recorder();
        StartupPrimes.PrimeSkillsCatalogue(run);
        var job = Assert.Single(jobs);
        var caller = Environment.CurrentManagedThreadId;
        var ranOn = -1;
        await Task.Run(() => { ranOn = Environment.CurrentManagedThreadId; job(); });
        Assert.NotEqual(caller, ranOn);
        Assert.True(SkillsCatalogueLoader.IsEmbeddedLoaded);
        Assert.Same(SkillsCatalogueLoader.Embedded, SkillsCatalogueLoader.Embedded); // one parse for the process
        Assert.Equal(SkillsCatalogueLoader.LoadEmbedded().Items.Select(i => i.Id), SkillsCatalogueLoader.Embedded.Items.Select(i => i.Id));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-5")] // ED-NEW-perf-startup-structure-5
    public async Task The_symbol_prime_hands_the_runner_one_job_that_reads_the_mapping_on_another_thread()
    {
        Assert.NotNull(headless.Session); // the session is what registers Avalonia's asset loader for the symbol mapping
        var (run, jobs) = Recorder();
        StartupPrimes.PrimeSymbols(run);
        var job = Assert.Single(jobs);
        await Task.Run(job);
        Assert.True(SymbolCatalogue.IsLoaded);
        Assert.NotEmpty(SymbolCatalogue.SymbolNames);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-5")] // ED-NEW-perf-startup-structure-5
    public async Task The_wallpaper_prime_reads_the_catalogue_only_when_a_library_wallpaper_is_saved()
    {
        var empty = new InMemorySettingsStore();
        var (none, noJobs) = Recorder();
        StartupPrimes.PrimeWallpapers(empty, none);
        Assert.Empty(noJobs); // nothing saved: nothing to read

        var saved = new InMemorySettingsStore();
        saved.Set("wallpaperLibrary", "{\"kind\":\"static\",\"id\":\"x\"}");
        var (run, jobs) = Recorder();
        StartupPrimes.PrimeWallpapers(saved, run);
        var job = Assert.Single(jobs);
        await Task.Run(job);
        Assert.True(WallpaperCatalogue.IsLoaded);
        Assert.NotEmpty(WallpaperCatalogue.All);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-3")] // ED-NEW-perf-startup-structure-3
    public async Task The_production_runner_runs_the_job_on_the_pool_not_on_the_calling_thread()
    {
        var caller = Environment.CurrentManagedThreadId;
        var ranOn = -1;
        await StartupPrimes.Pool(() => ranOn = Environment.CurrentManagedThreadId);
        Assert.NotEqual(caller, ranOn);
    }
}
