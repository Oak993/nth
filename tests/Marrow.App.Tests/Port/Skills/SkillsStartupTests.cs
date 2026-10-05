using System.Reflection;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-13 / ED-326 (6)(a): "nothing in the app wires either up yet. Do that composition here". Program.Main cannot run in a test host (STAThread, the single-instance lock,
// the real data folder), so the order is proved from Main's own IL, as VelopackStartupHookTests does; the binding itself runs here on fresh data roots under MARROW_PORT_DATA_DIR.
public sealed class SkillsStartupTests
{
    private static string FreshRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk13 startup " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Main_binds_the_skills_host_for_the_data_root_after_the_storage_bindings_and_before_the_Avalonia_application_is_built()
    {
        var calls = MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!).Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var bind = calls.IndexOf("SkillsStartup.Bind");
        Assert.True(bind >= 0, "Main never binds the skills host: " + string.Join(", ", calls));
        Assert.True(calls.IndexOf("SkillsDataRoot.Resolve") == bind - 1, "Bind takes the resolved data root: " + string.Join(", ", calls));
        Assert.True(bind > calls.IndexOf("StoragePathsLocations.CreateProduction"), "after the storage bindings");
        Assert.True(bind < calls.IndexOf("MarrowApplication.BuildAvaloniaApp"), "before the application is built");
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Bind_opens_the_one_host_of_the_root_and_publishes_it()
    {
        var root = FreshRoot();
        try
        {
            SkillsHost? published = null;
            var host = SkillsStartup.Bind(root, publish: h => published = h);
            Assert.NotNull(host);
            try
            {
                Assert.Same(host, published);
                Assert.Same(host, SkillsHost.Find(root));
                Assert.Equal(root, host.Paths.Root);
                Assert.NotNull(host.Pipeline);
                Assert.NotNull(host.Generations);
                Assert.Equal("Under Anthropic's terms", host.Terms.Offer("A2")!.Label); // the embedded catalogue
            }
            finally { host.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_host_that_cannot_open_is_logged_and_the_app_goes_on_without_one()
    {
        var root = FreshRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "addons"));
            File.WriteAllText(Path.Combine(root, "addons", "state.json"), "this is not json");
            var messages = new List<string>();
            using var sink = Marrow.Core.Port.Logger.Sink.Override((message, _) => messages.Add(message));
            SkillsHost? published = null;
            Assert.Null(SkillsStartup.Bind(root, publish: h => published = h));
            Assert.Null(published);
            Assert.Null(SkillsHost.Find(root));
            Assert.Contains(messages, m => m.Contains("could not be opened", StringComparison.Ordinal));
            Assert.Equal("this is not json", File.ReadAllText(Path.Combine(root, "addons", "state.json"))); // left alone, never replaced by an empty state
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void The_host_seam_is_a_trap_until_the_app_binds_it()
    {
        Assert.Throws<Marrow.Core.Port.SwiftTrapException>(() => SkillsSeams.Host.Current);
    }
}
