// PORT-ONLY: AC-3 C11, bounded image decode on the palette actor; ED-617.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Marrow.Core.Port.ModelsPalette;
using SkiaSharp;
namespace Marrow.UI.Port.ModelsPalette;
public sealed class SkiaLogoDecoder : ILogoDecoder
{
    public LogoPixels? Decode(byte[] bytes,string contentType)
    {
        if(bytes.Length>512*1024)return null;
        try
        {
            if(contentType is "image/x-icon" or "image/vnd.microsoft.icon")
            {
                var frame=Ico(bytes);if(frame is null)return null;bytes=frame;
            }
            using var data=SKData.CreateCopy(bytes);using var codec=SKCodec.Create(data);
            if(codec is null||codec.Info.Width<=0||codec.Info.Height<=0||codec.Info.Width>1024||codec.Info.Height>1024)return null;
            var info=new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
            using var bitmap=new SKBitmap(info);
            if(codec.GetPixels(info,bitmap.GetPixels())!=SKCodecResult.Success)return null;
            var rgba=new byte[info.Width*info.Height*4];Marshal.Copy(bitmap.GetPixels(),rgba,0,rgba.Length);
            return new(info.Width,info.Height,rgba);
        }
        catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
    }
    private static byte[]? Ico(byte[] bytes)
    {
        if(bytes.Length<6||BinaryPrimitives.ReadUInt16LittleEndian(bytes)!=0||BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2))!=1)return null;
        var count=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));if(count==0||6+count*16>bytes.Length)return null;
        var frames=new List<(int Area,int Depth,int Offset,int Length)>();
        for(var n=0;n<count;n++)
        {
            var e=bytes.AsSpan(6+n*16,16);var w=e[0]==0?256:e[0];var h=e[1]==0?256:e[1];
            var size=BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);var offset=BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
            if(offset<6+count*16||size>int.MaxValue||offset>int.MaxValue||(ulong)offset+size>(ulong)bytes.Length)return null;
            frames.Add((w*h,BinaryPrimitives.ReadUInt16LittleEndian(e[6..]),(int)offset,(int)size));
        }
        var best=frames.OrderByDescending(x=>x.Area).ThenByDescending(x=>x.Depth).ThenBy(x=>x.Offset).First();
        var frame=bytes.AsSpan(best.Offset,best.Length);
        if(frame.StartsWith(new byte[]{137,80,78,71,13,10,26,10}))return frame.ToArray();
        // ICO DIB heights include the AND mask. Skia decodes the colour bitmap after a BMP header is added.
        if(frame.Length<40||BinaryPrimitives.ReadInt32LittleEndian(frame)<40)return null;
        var header=BinaryPrimitives.ReadInt32LittleEndian(frame);if(header>frame.Length)return null;
        var dib=frame.ToArray();var height=BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));if(height<=0||height%2!=0)return null;
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8),height/2);
        var depth=BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));var palette=depth<=8?4*(1<<depth):0;
        var bmp=new byte[14+dib.Length];bmp[0]=(byte)'B';bmp[1]=(byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2),bmp.Length);BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10),14+header+palette);dib.CopyTo(bmp,14);return bmp;
    }
}
