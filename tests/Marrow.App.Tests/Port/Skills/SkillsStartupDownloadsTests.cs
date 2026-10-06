using System.Reflection;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-16 (DL-19, ED-NEW-f-sk-13 item 4: "the download seam, the binding seam and the licence-text seam are unbound ... until SK-16 binds them"). The app's composition binds them,
// so fetching is available; the binding itself runs on a fresh data root and reaches no network (nothing is downloaded until an item is asked for).
public sealed class SkillsStartupDownloadsTests
{
    private static string FreshRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk16 startup " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void The_default_composition_binds_the_download_binding_and_licence_seams_so_fetching_is_available()
    {
        var root = FreshRoot();
        try
        {
            var host = SkillsStartup.Bind(root, publish: _ => { });
            Assert.NotNull(host);
            try
            {
                Assert.True(host.Downloads.Available);
                Assert.True(host.Pipeline.FetchingBound);
                Assert.NotNull(host.GuardedDownloads);
                Assert.Null(host.Downloads.Running("A1")); // nothing started on its own
            }
            finally { host.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Main_attaches_the_Skills_and_tools_rows_of_the_updates_page_right_after_binding_the_host_and_before_the_application_is_built()
    {
        var calls = MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!).Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var bind = calls.IndexOf("SkillsStartup.Bind");
        var attach = calls.IndexOf("SkillsUpdateRows.Attach");
        Assert.True(attach > bind && bind >= 0, "Attach comes after Bind: " + string.Join(", ", calls));
        Assert.True(attach < calls.IndexOf("MarrowApplication.BuildAvaloniaApp"), "before the application is built");
    }
}
