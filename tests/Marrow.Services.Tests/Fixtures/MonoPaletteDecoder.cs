using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Tests.Fixtures;
public sealed class MonoPaletteDecoder(PalettePipelineFixture fake) : ILogoDecoder
{
    public int Calls;
    public LogoPixels? Decode(byte[] bytes,string contentType)
    {Calls++;return bytes[^1]==2?new(64,64,Enumerable.Range(0,4096).SelectMany(_=>new byte[]{128,128,128,255}).ToArray()):fake.Decode(bytes,contentType);}
}
