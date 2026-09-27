using Avalonia.Platform.Storage;

namespace Marrow.App.Tests.Fixtures;

// Browser boundary double: records requests from the real MCPKeyPanel and never opens a browser.
public sealed class MCPProofsMcpALauncher : ILauncher
{
    public List<Uri> Requests { get; } = [];
    public Task<bool> LaunchUriAsync(Uri uri) { Requests.Add(uri); return Task.FromResult(true); }
    public Task<bool> LaunchFileAsync(IStorageItem storageItem) => throw new InvalidOperationException("This proof opens no files.");
}
