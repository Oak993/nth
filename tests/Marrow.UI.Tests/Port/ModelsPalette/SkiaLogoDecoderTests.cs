using Marrow.UI.Port.ModelsPalette;
using SkiaSharp;
namespace Marrow.UI.Tests.Port.ModelsPalette;
// PORT-ONLY: synthetic logos, no requests or copyrighted reference image; ED-617.
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class SkiaLogoDecoderTests
{
    [Fact]
    public void A_png_preserves_unpremultiplied_rgba_and_garbage_is_unavailable()
    {
        using var bitmap=new SKBitmap(new SKImageInfo(64,48,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        bitmap.Erase(new SKColor(10,170,110,160));
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        var pixels=new SkiaLogoDecoder().Decode(png.ToArray(),"image/png");
        Assert.NotNull(pixels);Assert.Equal(64,pixels.Width);Assert.Equal(48,pixels.Height);
        Assert.Equal(new byte[]{10,170,110,160},pixels.Rgba[..4]);
        Assert.Null(new SkiaLogoDecoder().Decode([1,2,3,4],"image/png"));
        Assert.Null(new SkiaLogoDecoder().Decode(new byte[512*1024+1],"image/png"));
    }
    [Fact]
    public void Ico_chooses_the_largest_embedded_png_frame()
    {
        byte[] Frame(int size,SKColor colour){using var b=new SKBitmap(size,size);b.Erase(colour);using var i=SKImage.FromBitmap(b);using var d=i.Encode(SKEncodedImageFormat.Png,100);return d.ToArray();}
        var small=Frame(16,SKColors.Red);var large=Frame(64,SKColors.Green);
        using var memory=new MemoryStream();using var w=new BinaryWriter(memory);
        w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)2);
        void Entry(byte size,byte[] frame,int offset){w.Write(size);w.Write(size);w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frame.Length);w.Write(offset);}
        Entry(16,small,38);Entry(64,large,38+small.Length);w.Write(small);w.Write(large);
        var p=new SkiaLogoDecoder().Decode(memory.ToArray(),"image/x-icon");
        Assert.NotNull(p);Assert.Equal(64,p.Width);Assert.Equal(64,p.Height);Assert.Equal(new byte[]{0,128,0,255},p.Rgba[..4]);
    }
}
