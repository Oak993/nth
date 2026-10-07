namespace Marrow.Services.Tests.Fixtures;

public sealed class AccountTestFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? throw new InvalidOperationException("Tests require MARROW_PORT_DATA_DIR."),
        "ma1-α-" + Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
