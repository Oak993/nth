using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
[Trait("Spec", "DL-4")]
public sealed class InstallerCrashTests
{
    public static IEnumerable<object[]> Crashes() => Enum.GetValues<InstallerKind>().SelectMany(k => Enum.GetValues<InstallStep>().Select(s => new object[] { k, s }));
    [Theory]
    [MemberData(nameof(Crashes))]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Every_kind_crash_at_every_step_leaves_only_tmp_folders(InstallerKind kind, InstallStep step)
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(kind);
        plan = rig.AddPip(plan) with {
            Pyset = new PysetOptions(Path.Combine(rig.Root, "python.exe"), []), NodePackages = [], Extractor = plan.LocalFiles["payload.zip"], Spares = [] };
        rig.Process.OnRun = cmd => { var target = kind == InstallerKind.MsiAdmin ? cmd.Arguments[3]["TARGETDIR=".Length..] : kind == InstallerKind.Sfx7z ? cmd.Arguments[0][2..] : cmd.Arguments.Contains("--target", StringComparer.Ordinal) ? cmd.Arguments[cmd.Arguments.ToList().IndexOf("--target") + 1] : null; if (target is not null) File.WriteAllText(Path.Combine(target, "fixture.exe"), "fixture"); };
        await Assert.ThrowsAsync<InjectedCrashException>(() => rig.Installer((s, _) => { if (s == step) throw new InjectedCrashException(); }).InstallAsync(plan));
        rig.AssertOnlyTemporaryFolders();
        Assert.Equal(Marrow.Core.Port.Skills.State.ItemState.Checking, plan.Record.State);
    }
}
