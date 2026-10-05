// PORT-ONLY: SK-13 / DL-15. A scripted licence-text source: it serves Marrow-made bytes and counts the reads; there is no network.
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class StubLicenceText(string body) : ILicenceText
{
    public byte[] Bytes { get; set; } = Encoding.UTF8.GetBytes(body);
    public int Reads { get; private set; }

    public Task<byte[]> ReadAsync(CatalogueItem item, CancellationToken cancellation)
    {
        Reads++;
        return Task.FromResult(Bytes);
    }
}
