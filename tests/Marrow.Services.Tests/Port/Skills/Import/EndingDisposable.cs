namespace Marrow.Services.Tests.Port.Skills.Import;

/// <summary>A disposable that runs one action when it is disposed (SK-18 fixtures).</summary>
public sealed class EndingDisposable(Action end) : IDisposable
{
    public void Dispose() => end();
}
