namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: ten independent, disposable data roots; no real application data or credentials.
public sealed class ModelsPaletteRoots : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("marrow-t37-catalogue-");
    private int index;
    public string New() => Path.Combine(root.FullName, (++index).ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void Dispose() => root.Delete(recursive: true);
}
