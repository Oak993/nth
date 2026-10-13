using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Web;
using Velopack;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): web surfaces W23 (DESIGN 2.3 and 13.4, ED-NEW-web-w23-3). Velopack's own dispatch (Update.exe running the app
// with --veloapp-uninstall) is NOT run here: it needs a Velopack install, and nothing in this unit runs an installer, an uninstaller or
// Velopack. What is proved instead: the registration hands Velopack a callback, the callback removes the web data of a fake data root
// and never throws, and Program.RunVelopackHook registers it before the skills hook and before Run().
[Trait("Decision", "ED-NEW-web-w23-3")]
[Trait("Feature", "W-23")]
public sealed class WebDataUninstallHookTests
{
    private static string FreshDataRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "w23 hook root " + Guid.NewGuid().ToString("N"), "nth");
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void The_registered_callback_removes_the_web_data_folder_and_leaves_the_rest_of_the_data_root()
    {
        var root = FreshDataRoot();
        try
        {
            var web = Path.Combine(root, "WebView2");
            Directory.CreateDirectory(Path.Combine(web, "EBWebView", "browse"));
            File.WriteAllText(Path.Combine(web, "EBWebView", "browse", "Cookies"), "cookie");
            File.WriteAllText(Path.Combine(root, "settings.json"), "keep");
            VelopackHook? registered = null;
            WebDataUninstallHook.Register(hook => registered = hook, () => web);
            Assert.NotNull(registered);

            registered(SemanticVersion.Parse("1.0.0"));

            Assert.False(Directory.Exists(web));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "settings.json")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    [Fact]
    public void The_callback_never_throws_into_Velopack_and_resolves_the_folder_only_when_it_runs()
    {
        VelopackHook? registered = null;
        var resolved = 0;
        WebDataUninstallHook.Register(hook => registered = hook, () => { resolved++; throw new InvalidOperationException("no folder"); });
        Assert.Equal(0, resolved);
        registered!(SemanticVersion.Parse("1.0.0"));
        Assert.Equal(1, resolved);
        WebDataUninstallHook.Register(hook => registered = hook, () => "relative\\nth\\WebView2");
        registered(SemanticVersion.Parse("1.0.0"));
        WebDataUninstallHook.Register(hook => registered = hook, () => "C:\\ignored", _ => throw new IOException("a held file"));
        registered(SemanticVersion.Parse("1.0.0"));
    }

    [Fact]
    public void RunVelopackHook_registers_the_web_data_hook_before_the_skills_hook_and_before_the_Velopack_dispatch()
    {
        var calls = MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.RunVelopackHook), BindingFlags.Static | BindingFlags.NonPublic)!);
        var names = calls.Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var web = names.IndexOf("WebDataUninstallHook.Register");
        var skills = names.IndexOf("SkillsUninstallHook.Register");
        var run = names.IndexOf("VelopackApp.Run");
        Assert.True(web >= 0, "RunVelopackHook never registers the web data hook: " + string.Join(", ", names));
        Assert.True(skills > web, "the web data hook runs first, with its own budget: " + string.Join(", ", names));
        Assert.True(run > skills, "Run() must come after the registrations: " + string.Join(", ", names));
    }

    [Fact]
    public void The_hook_reaches_the_folder_the_engine_keeps_its_profiles_in()
    {
        Assert.Equal(WebProfiles.DefaultUserDataFolder, WebDataRoot.Resolve());
        Assert.Equal(Path.Combine(Marrow.Core.Support.DevDataRoot.LocalApplicationData(), "nth", "WebView2"), WebDataRoot.Resolve());
    }
}
