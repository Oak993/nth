using System.Reflection;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests;

// ED-NEW-perf-startup-structure-2, -3, -5: Program.Main cannot run in a test host, so its wiring is read from its IL (as SkillsStartupTests does): the voice programs join the
// warm-up registry next to the nth motion pack, and the three primes start where they overlap the most work. The Skills page takes the host's catalogue, not a second parse.
public sealed class ProgramStartupPrimesTests
{
    private static List<string> MainCalls() => MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!)
        .Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-2")] // ED-NEW-perf-startup-structure-2
    public void Main_registers_the_voice_orb_and_glow_programs_with_the_warm_up_before_Avalonia_is_set_up()
    {
        var calls = MainCalls();
        var nth = calls.IndexOf("NthMotionWarmup.Register");
        var orbs = calls.IndexOf("VoiceWarmup.Register");
        var glow = calls.IndexOf("VoiceGlowWarmup.Register");
        Assert.True(nth >= 0 && orbs > nth, "the voice orbs join the registry after the nth motion pack: " + string.Join(", ", calls));
        Assert.True(glow > orbs, "the voice glow joins it after the orbs");
        Assert.True(glow < calls.IndexOf("MarrowApplication.BuildAvaloniaApp"), "before the Avalonia application is built (and so before any first-run plan is taken)");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-3")] // ED-NEW-perf-startup-structure-3
    public void Main_starts_the_skills_catalogue_read_before_the_skills_host_asks_for_it()
    {
        var calls = MainCalls();
        var prime = calls.IndexOf("StartupPrimes.PrimeSkillsCatalogue");
        Assert.True(prime >= 0, "Main never primes the skills catalogue: " + string.Join(", ", calls));
        Assert.True(prime < calls.IndexOf("SkillsStartup.Bind"), "before the host is bound");
        Assert.True(prime < calls.IndexOf("StoragePathsLocations.CreateProduction"), "as early as the data bindings, to overlap them");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-5")] // ED-NEW-perf-startup-structure-5
    public void Main_primes_the_wallpapers_after_the_settings_are_bound_and_the_symbols_after_Avalonia_is_set_up()
    {
        var calls = MainCalls();
        var wallpapers = calls.IndexOf("StartupPrimes.PrimeWallpapers");
        var symbols = calls.IndexOf("StartupPrimes.PrimeSymbols");
        Assert.True(wallpapers >= 0 && symbols >= 0, "Main never primes them: " + string.Join(", ", calls));
        Assert.True(wallpapers < calls.IndexOf("SkillsStartup.Bind"), "the wallpaper read starts with the bindings");
        Assert.True(symbols > calls.IndexOf("MainActor.Bind"), "the symbol mapping needs the asset loader the Avalonia setup registers");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-3")] // ED-NEW-perf-startup-structure-3
    public void The_Skills_page_controllers_take_the_hosts_catalogue_and_never_parse_the_embedded_file_again()
    {
        var field = typeof(SkillsPageSeams).GetField("Default", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var make = Assert.IsAssignableFrom<Delegate>(field.GetValue(null));
        var calls = MethodCalls.Of(make.Method).Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        Assert.DoesNotContain("SkillsCatalogueLoader.LoadEmbedded", calls);
        Assert.Contains("SkillsHost.get_Catalogue", calls);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-3")] // ED-NEW-perf-startup-structure-3
    public void The_production_skills_host_options_take_the_one_shared_parse()
    {
        var calls = MethodCalls.Of(typeof(Marrow.Services.Port.Skills.Host.SkillsHostOptions).GetMethod(nameof(Marrow.Services.Port.Skills.Host.SkillsHostOptions.Production))!)
            .Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        Assert.Contains("SkillsCatalogueLoader.get_Embedded", calls);
        Assert.DoesNotContain("SkillsCatalogueLoader.LoadEmbedded", calls);
    }
}
