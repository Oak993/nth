using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The store protection and install lock of a skills host a test opens on a temp root: nothing is locked, nothing reaches a real folder (SK-18 fixtures).</summary>
internal sealed class OpenProtection : IStoreProtection, IInstallLock
{
    public void Lock(string path) { }
    public void Unlock(string path) { }
    public bool IsLocked(string path) => false;
    public bool HasNamedStreams(string path) => false;
}
