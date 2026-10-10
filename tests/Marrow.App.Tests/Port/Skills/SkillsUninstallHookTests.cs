using System.Reflection;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Skills.Host;
using Velopack;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-13 / DL-16, skills-ONDEMAND-PLAN.md 3.8 "Uninstall" and skills-PLAN.md WU-18 r3 (b). ED-321 (1) gives SK-13 Program's uninstall callback. Velopack's own dispatch
// (Update.exe running the app with --veloapp-uninstall) is NOT run here: it needs a Velopack install, and nothing in this unit runs an installer, an uninstaller or Velopack. What is proved
// instead: the registration hands Velopack a callback, the callback clears the skills data of a fake data root and never throws, and Program.RunVelopackHook registers it before Run().
public sealed class SkillsUninstallHookTests
{
    private static string FreshRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk13 hook root " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    [Trait("Decision", "ED-321")]
    public void The_registered_callback_unlocks_and_deletes_the_skills_data_of_the_data_root_and_keeps_enabled_json()
    {
        var root = FreshRoot();
        try
        {
            var addons = Path.Combine(root, "addons", "tools", "T1-1-aaaa1111");
            var store = Path.Combine(root, "skills", "store", "set@8a1541c4");
            Directory.CreateDirectory(addons);
            Directory.CreateDirectory(store);
            File.WriteAllText(Path.Combine(addons, "python.exe"), "fixture");
            File.WriteAllText(Path.Combine(store, "SKILL.md"), "fixture");
            File.SetAttributes(Path.Combine(store, "SKILL.md"), FileAttributes.ReadOnly);
            File.WriteAllText(Path.Combine(root, "skills", "enabled.json"), "switches");
            VelopackHook? registered = null;
            SkillsUninstallHook.Register(hook => registered = hook, () => root);
            Assert.NotNull(registered);
            registered(SemanticVersion.Parse("1.0.0"));
            Assert.False(Directory.Exists(Path.Combine(root, "addons")));
            Assert.False(Directory.Exists(store));
            Assert.Equal("switches", File.ReadAllText(Path.Combine(root, "skills", "enabled.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-321")]
    public void The_callback_never_throws_into_Velopack_and_resolves_the_root_only_when_it_runs()
    {
        VelopackHook? registered = null;
        var resolved = 0;
        SkillsUninstallHook.Register(hook => registered = hook, () => { resolved++; throw new InvalidOperationException("no root"); });
        Assert.Equal(0, resolved);
        registered!(SemanticVersion.Parse("1.0.0"));
        Assert.Equal(1, resolved);
        SkillsUninstallHook.Register(hook => registered = hook, () => "relative\\root");
        registered(SemanticVersion.Parse("1.0.0"));
        SkillsUninstallHook.Register(hook => registered = hook, () => "C:\\ignored", _ => throw new IOException("a held file"));
        registered(SemanticVersion.Parse("1.0.0"));
    }

    [Fact]
    [Trait("Decision", "ED-321")]
    public void RunVelopackHook_registers_the_skills_uninstall_hook_before_it_runs_the_Velopack_dispatch()
    {
        var calls = MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.RunVelopackHook), BindingFlags.Static | BindingFlags.NonPublic)!);
        var names = calls.Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var register = names.IndexOf("SkillsUninstallHook.Register");
        var run = names.IndexOf("VelopackApp.Run");
        Assert.True(register >= 0, "RunVelopackHook never registers the skills hook: " + string.Join(", ", names));
        Assert.True(run > register, "Run() must come after the registration: " + string.Join(", ", names));
    }

    [Fact]
    [Trait("Decision", "ED-321")]
    public void The_hook_reaches_the_same_data_root_the_app_uses()
    {
        Assert.Equal(Path.Combine(Marrow.Core.Support.DevDataRoot.LocalApplicationData(), "nth"), SkillsDataRoot.Resolve());
    }
}
