// PORT-ONLY: SK-5, ONDEMAND 3.1 data layout. An explicit absolute Marrow Code root, no user-folder fallback.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StorePaths
{
    public StorePaths(string dataRoot) => throw new NotImplementedException();
    public string Tools => throw new NotImplementedException();
    public string Store => throw new NotImplementedException();
    public string Views => throw new NotImplementedException();
    public string Addons => throw new NotImplementedException();
    public string StateFile => throw new NotImplementedException();
    public string CondemnedFile => throw new NotImplementedException();
    public string EnabledFile => throw new NotImplementedException();
}
