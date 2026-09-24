namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A fresh UpdateInstallProgress for one test. Swift gives the type only `private init() {}` and one
/// `static let shared` (App/AppUpdater.swift:30, :70); the port keeps the constructor private (R-NAME-6), so a
/// test that must not share the process-wide instance with tests running in parallel creates its own through
/// the private parameterless constructor. Nothing else about the instance differs from `shared`.
/// </summary>
public static class UpdateInstallProgressFixtures
{
    public static UpdateInstallProgress NewProgress() =>
        (UpdateInstallProgress)Activator.CreateInstance(typeof(UpdateInstallProgress), nonPublic: true)!;
}
