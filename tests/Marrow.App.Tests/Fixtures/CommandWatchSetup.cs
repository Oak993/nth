namespace Marrow.App.Tests.Fixtures;

/// <summary>The rig, repository and captured tree of one ThreadRuntimeHydraCommandWatchTests case.</summary>
public sealed record CommandWatchSetup(ThreadRuntimeTurnRig Rig, AppModelGitRepo Repo, string Tree);
