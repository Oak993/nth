// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): the model store (voice-SPEC V-9.3, V-9.4, Q-V-13 B).
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Models;

public sealed class ModelStore
{
    public ModelStore(string dataRoot, Func<string?> selectedModel, TimeProvider? clock = null) => throw new NotImplementedException();

    /// <summary>The store on the app's data root (StoragePaths.Root).</summary>
    public static ModelStore CreateDefault(Func<string?> selectedModel) => throw new NotImplementedException();

    public string Folder => throw new NotImplementedException();

    public string PathFor(ModelFile file) => throw new NotImplementedException();

    public string InstalledJsonPath => throw new NotImplementedException();

    public ModelInstallState State(ModelFile file) => throw new NotImplementedException();

    public long InstalledBytes => throw new NotImplementedException();

    public IReadOnlyList<InstalledModelRecord> Records => throw new NotImplementedException();

    public Task<ModelVerification> VerifyForLoad(ModelFile file, CancellationToken cancellationToken) => throw new NotImplementedException();

    public ModelRemoveResult Remove(ModelFile file) => throw new NotImplementedException();

    public void RecordInstalled(ModelFile file) => throw new NotImplementedException();

    internal void SetDownloading(ModelFile file, Func<double>? fraction) => throw new NotImplementedException();
}
