// PORT-ONLY: ED-NEW-dl-13. A scripted licence reader for PluginLicenceTermsTests: it serves each licence file of a set by its path and counts the reads; any other path is refused.
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class PluginLicenceFiles(Dictionary<string, string> texts) : ILicenceText
{
    public List<string> Read { get; } = [];

    public Task<byte[]> ReadAsync(CatalogueItem item, CancellationToken cancellation) => throw new InvalidOperationException("No item licence file.");

    public Task<byte[]> ReadFileAsync(CatalogueItem item, string path, CancellationToken cancellation)
    {
        Read.Add(path);
        return Task.FromResult(Encoding.UTF8.GetBytes(texts[path]));
    }
}
