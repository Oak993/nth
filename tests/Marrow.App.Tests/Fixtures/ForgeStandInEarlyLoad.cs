using System.Runtime.CompilerServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A34 review fixes (finding #2): forces Marrow.Services.Tests.Fixtures.ForgeStandIns's own
/// <c>[ModuleInitializer]</c> -- which puts a scripted, offline glab/gh/tea folder first on the process
/// PATH (ForgeFixtures.cs) -- to run as part of loading THIS module, not merely before whichever
/// AppModelHydraMergeTests test body happens to be the first to call
/// <see cref="Marrow.Services.Tests.Fixtures.ForgeStandIns.Ensure"/>.
///
/// Without this, the race is real: LoginEnvironment's process-wide fallback-environment snapshot
/// (Marrow.Core.Support.Shell.cs, a <c>Lazy&lt;ValueMap&lt;string,string&gt;&gt;</c> evaluated once per test
/// process by whichever caller reads <c>LoginEnvironment.Current</c>/<c>.Git</c> first) can be taken before
/// the stand-in folder is ever added to PATH -- and something as ordinary as
/// <c>AppModelGitRepo.WithCommit(...)</c> (AppModelFixtures.cs, the very first line of every HydraMerge test)
/// reads <c>LoginEnvironment.Current</c> to run `git init`. If that happens anywhere in the process before
/// <c>ForgeStandIns</c>'s module has been touched, <c>Shell.RunTool("tea"/"gh"/"glab")</c> then searches the
/// REAL PATH: on a machine with no forge CLI installed it throws "... was not found on your PATH.", and on a
/// machine that has one, it runs the real tool (R-TEST-10). Module initializers of the entry test assembly
/// run once, at module load, before any [Fact] executes and before any test can have already cached
/// LoginEnvironment's snapshot -- so touching <see cref="Marrow.Services.Tests.Fixtures.ForgeStandIns.Folder"/>
/// here (any member access forces that module to load and run its own initializer first) closes the race for
/// every test in this process, not just the ones that happen to call Ensure() themselves.
/// </summary>
internal static class ForgeStandInEarlyLoad
{
    [ModuleInitializer]
#pragma warning disable CA2255 // The whole point: run before any test in this module reads LoginEnvironment.
    internal static void EnsureForgeStandInsPathBeforeAnyTest()
#pragma warning restore CA2255
    {
        _ = Marrow.Services.Tests.Fixtures.ForgeStandIns.Folder;
    }
}
