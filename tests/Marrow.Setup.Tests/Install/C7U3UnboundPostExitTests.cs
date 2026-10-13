using Marrow.Core.Support;
using Marrow.Setup.Install;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Text;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Install;

// ED-NEW-inst-merge-12: the integration bound the real U5 step (ProductionAfterSetupExit) and removed the NoAfterSetupExit
// stub, so the C7b fact that an unbound adapter fails closed is held on the production adapter: a step that reached no shell
// or registry, or left a required step undone, throws Problem's plain sentence; only a finished one returns. Fakes only.
public sealed class C7U3UnboundPostExitTests : IDisposable
{
    private readonly string area = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-c7-unbound-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(area, recursive: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unbound_post_exit_adapter_cannot_report_success()
    {
        IAfterSetupExit after = new ProductionAfterSetupExit((_, _) => null);

        var error = Assert.Throws<InvalidOperationException>(() => after.Run(Path.Combine(area, "nth"), Path.Combine(area, "result.json")));
        Assert.Equal(SetupStrings.ProblemOther, error.Message);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_production_step_cannot_report_success_while_the_uninstall_entry_is_not_repointed()
    {
        var shell = new FakeShellLinks();
        var registry = new FakeUserRegistry(); // Velopack's key is missing: nothing to re-point
        var after = new ProductionAfterSetupExit((root, result) => AfterSetupExit.Run(shell, registry, root, result, _ => 1, _ => true));

        Assert.Throws<InvalidOperationException>(() => after.Run(FakeShellLinks.DefaultRoot, Path.Combine(area, "result.json")));
        registry.WriteVelopackUninstallKey(@"C:\Elsewhere\nth"); // another root's entry is not this install's
        Assert.Throws<InvalidOperationException>(() => after.Run(FakeShellLinks.DefaultRoot, Path.Combine(area, "result.json")));
        Assert.Contains("Update.exe", (string)registry.GetValue(UninstallEntry.KeyPath, "UninstallString")!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_busy_Start_writer_or_a_lost_re_stamp_record_holds_the_install_back()
    {
        var root = FakeShellLinks.DefaultRoot;
        var start = new StartLinkResult(Found: true, Restamped: true);
        var repointed = new UninstallRepointResult(KeyFound: true, Ours: true, Writes: 3, UninstallerPresent: true);
        AfterSetupExitReport Report(StartLinkResult link, bool lost = false) =>
            new(true, new OldStartLinkResult(OldStartLinkOutcome.None), link, null, repointed, !lost, ResultWriteFailed: lost);

        Assert.True(Report(start).RequiredStepsDone);
        Assert.False(Report(start with { Busy = true }).RequiredStepsDone);
        Assert.False(Report(start with { HResult = unchecked((int)0x80004005) }).RequiredStepsDone);
        Assert.False(Report(start, lost: true).RequiredStepsDone);
        Assert.False((Report(start) with { UninstallKey = null }).RequiredStepsDone);
        Assert.Throws<InvalidOperationException>(() => new ProductionAfterSetupExit((_, _) => Report(start with { Busy = true })).Run(root, Path.Combine(area, "result.json")));
        new ProductionAfterSetupExit((_, _) => Report(start)).Run(root, Path.Combine(area, "result.json"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_production_step_returns_once_the_entry_is_repointed_to_nth_Uninstall()
    {
        var shell = new FakeShellLinks();
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(FakeShellLinks.DefaultRoot);
        var after = new ProductionAfterSetupExit((root, result) => AfterSetupExit.Run(shell, registry, root, result, _ => 1, _ => true));

        after.Run(FakeShellLinks.DefaultRoot, Path.Combine(area, "result.json"));

        Assert.Contains("nth Uninstall.exe", (string)registry.GetValue(UninstallEntry.KeyPath, "UninstallString")!, StringComparison.Ordinal);
    }
}
