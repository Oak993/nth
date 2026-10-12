using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

/// <summary>W13: the pane's side of the bridge, answered by the test (consents, the file sheet, drafts, the theme).</summary>
internal sealed class FakeArtifactBridgeHost : IArtifactBridgeHost
{
    public bool AllowModel { get; set; } = true;

    public bool AllowTools { get; set; } = true;

    public List<(ArtifactAddress Artifact, string Model)> ModelConsents { get; } = [];

    public List<(ArtifactAddress Artifact, string Server, string Tool)> ToolConsents { get; } = [];

    public List<(string Accept, bool Multiple, bool FromDrop)> Picks { get; } = [];

    public IReadOnlyList<ArtifactPickedFile> Picked { get; set; } = [];

    public List<(ArtifactAddress Artifact, string Text)> Drafts { get; } = [];

    public ArtifactTheme Theme { get; set; } = ArtifactTheme.Default;

    public event Action? ThemeChanged;

    public Task<bool> ConfirmModelAsync(ArtifactAddress artifact, string modelName, CancellationToken cancellationToken)
    {
        ModelConsents.Add((artifact, modelName));
        return Task.FromResult(AllowModel);
    }

    public Task<bool> ConfirmToolAsync(ArtifactAddress artifact, string server, string tool, CancellationToken cancellationToken)
    {
        ToolConsents.Add((artifact, server, tool));
        return Task.FromResult(AllowTools);
    }

    public Task<IReadOnlyList<ArtifactPickedFile>> PickFilesAsync(ArtifactAddress artifact, string accept, bool multiple, bool fromDrop, CancellationToken cancellationToken)
    {
        Picks.Add((accept, multiple, fromDrop));
        return Task.FromResult(Picked);
    }

    public void Draft(ArtifactAddress artifact, string text) => Drafts.Add((artifact, text));

    public void RaiseThemeChanged() => ThemeChanged?.Invoke();
}
