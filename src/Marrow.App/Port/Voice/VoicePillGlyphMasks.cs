// PORT-ONLY, VT-12 (OD-0121, OD-0123, OD-0125): the pill label's glyph coverage as the approved design draws it.
using System.Runtime.InteropServices;
#if !GLYPHTOOL
using Avalonia.Media;
#endif

namespace Marrow.App.Port.Voice;

/// <summary>One glyph's coverage: an 8-bit mask placed at (Left, Top) device pixels from the glyph's integer origin.</summary>
internal sealed record VoicePillGlyphMask(int Left, int Top, int Width, int Height, byte[] Alpha);

/// <summary>
/// The label's glyph masks rasterized the way the approved design page is drawn (voice-composer.html in Chrome on Windows, the locked V-T133 references): Chrome's Skia asks DirectWrite
/// for the glyph's three-column coverage (NATURAL_SYMMETRIC with the font's grid fitting, its gasp table) and keeps one greyscale mask, the mean of the three columns, then applies its
/// text contrast 1.0 under the sRGB transfer (Skia's mask pre-blend for black text). The port's own Skia path asks DirectWrite for a 16-level greyscale mask with contrast 0.5, a visibly
/// lighter and coarser edge. The result is a greyscale coverage mask, as OD-0099 Q-TEXT A asks: no colour is ever split across subpixels. Returns null where DirectWrite is unavailable.
/// </summary>
internal static class VoicePillGlyphMasks
{
    private const int TextureClearType3x1 = 1;
    private const int RenderingModeNaturalSymmetric = 5;
    private const int RenderingModeNatural = 4;
    private const int MeasuringModeNatural = 0;
    private const int GridFitEnabled = 2;
    private const int AntialiasClearType = 0;
    private const int FontFaceTrueType = 1;
    private const int FontFaceCff = 0;
    private const int FontFaceCollection = 2;

    // Skia's SkTMaskGamma_build_correcting_lut for black (luminance 0) at contrast 1.0 under the sRGB transfer (SK_GAMMA_EXPONENT 0): the table Chrome's mask pre-blend applies.
    private static readonly byte[] DesignContrast = BuildTable(1.0);

    private static readonly object Gate = new();
    private static IntPtr factory;
    private static IntPtr loader;
    private static bool failed;
    private static readonly Dictionary<object, (IntPtr Face, int RenderingMode)> Faces = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<(IntPtr, float, ushort, int), VoicePillGlyphMask> Masks = new();

    /// <summary>The mask of <paramref name="glyph"/> at <paramref name="emSize"/> device pixels, its origin at a quarter pixel <paramref name="quarter"/> (0 to 3) right of a whole pixel.</summary>
#if !GLYPHTOOL
    public static VoicePillGlyphMask? Mask(GlyphTypeface typeface, double emSize, ushort glyph, int quarter)
        => Mask(typeface.PlatformTypeface, () => typeface.PlatformTypeface.TryGetStream(out var stream) ? stream : null, (int)typeface.FontSimulations, emSize, glyph, quarter);
#endif

    /// <summary>The same for a face known by <paramref name="key"/>, its font file opened by <paramref name="open"/>, with DirectWrite's simulation bits.</summary>
    internal static VoicePillGlyphMask? Mask(object key, Func<Stream?> open, int simulations, double emSize, ushort glyph, int quarter)
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (Gate)
        {
            if (!TryFace(key, open, simulations, out var face, out var mode)) return null;
            var cacheKey = (face, (float)emSize, glyph, quarter);
            if (Masks.TryGetValue(cacheKey, out var cached)) return cached;
            var mask = Rasterize(face, mode, (float)emSize, glyph, quarter / 4f);
            if (mask is null) return null;
            if (Masks.Count > 4096) Masks.Clear();
            Masks[cacheKey] = mask;
            return mask;
        }
    }

    internal static byte[] BuildTable(double contrast)
    {
        static double FromLuma(double luma) => luma <= 0.0031308 ? luma * 12.92 : 1.055 * Math.Pow(luma, 1 / 2.4) - 0.055;
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            // float arithmetic as Skia's
            var raw = i / 255.0f;
            var srca = raw + (1.0f - raw) * (float)contrast * raw;
            var linOut = 1.0f - srca; // linSrc 0 (black), linDst 1 (the guessed white destination)
            var result = (float)((FromLuma(linOut) - 1.0) / (0.0 - 1.0));
            table[i] = (byte)Math.Clamp((int)MathF.Floor(255.0f * result + 0.5f), 0, 255);
        }
        return table;
    }

    private static bool TryFace(object platform, Func<Stream?> open, int simulations, out IntPtr face, out int mode)
    {
        face = IntPtr.Zero;
        mode = RenderingModeNaturalSymmetric;
        if (failed) return false;
        if (Faces.TryGetValue(platform, out var known))
        {
            (face, mode) = known;
            return face != IntPtr.Zero;
        }
        try
        {
            if (factory == IntPtr.Zero && !CreateFactory()) { failed = true; return false; }
            if (open() is not { } stream) { Faces[platform] = default; return false; }
            byte[] data;
            using (stream)
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                data = copy.ToArray();
            }
            var tag = data.Length >= 4 ? (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3] : 0;
            var type = tag == 0x4F54544F ? FontFaceCff : tag == 0x74746366 ? FontFaceCollection : FontFaceTrueType; // 'OTTO', 'ttcf'
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr file;
            try
            {
                // IDWriteInMemoryFontFileLoader::CreateInMemoryFontFileReference (slot 4): no owner, so DirectWrite keeps its own copy
                if (Call<CreateInMemoryReferenceFn>(loader, 4)(loader, factory, handle.AddrOfPinnedObject(), (uint)data.Length, IntPtr.Zero, out file) < 0) { Faces[platform] = default; return false; }
            }
            finally { handle.Free(); }
            simulations &= 3; // Avalonia's Bold 1, Oblique 2: DWRITE_FONT_SIMULATIONS has the same bits
            var files = new[] { file };
            // IDWriteFactory::CreateFontFace (slot 9)
            var hr = Call<CreateFontFaceFn>(factory, 9)(factory, type, 1, files, 0, simulations, out face);
            Marshal.Release(file);
            if (hr < 0) { Faces[platform] = default; face = IntPtr.Zero; return false; }
            mode = SymmetricSmoothing(data) ? RenderingModeNaturalSymmetric : RenderingModeNatural;
            Faces[platform] = (face, mode);
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or COMException or MarshalDirectiveException)
        {
            failed = true;
            return false;
        }
    }

    // Skia's DirectWrite scaler with hinting: a gasp table of version 1 picks symmetric smoothing per its flag at this size; the label's sizes are in Inter's single range.
    private static bool SymmetricSmoothing(byte[] data)
    {
        try
        {
            var tables = (data[4] << 8) | data[5];
            for (var k = 0; k < tables; k++)
            {
                var at = 12 + 16 * k;
                if (data[at] != 'g' || data[at + 1] != 'a' || data[at + 2] != 's' || data[at + 3] != 'p') continue;
                var offset = (data[at + 8] << 24) | (data[at + 9] << 16) | (data[at + 10] << 8) | data[at + 11];
                var version = (data[offset] << 8) | data[offset + 1];
                if (version < 1) return true;
                var flags = (data[offset + 6] << 8) | data[offset + 7]; // the first range covers the label's sizes (below 0xFFFF ppem)
                return (flags & 0x8) != 0;
            }
        }
        catch (IndexOutOfRangeException) { }
        return true;
    }

    private static bool CreateFactory()
    {
        var iidFactory5 = new Guid("958DB99A-BE2A-4F09-AF7D-65189803D1D3");
        if (DWriteCreateFactory(1 /* ISOLATED */, ref iidFactory5, out factory) < 0 || factory == IntPtr.Zero) return false;
        // IDWriteFactory5::CreateInMemoryFontFileLoader (slot 44), IDWriteFactory::RegisterFontFileLoader (slot 13)
        if (Call<CreateLoaderFn>(factory, 44)(factory, out loader) < 0) return false;
        return Call<RegisterLoaderFn>(factory, 13)(factory, loader) >= 0;
    }

    private static VoicePillGlyphMask? Rasterize(IntPtr face, int mode, float emSize, ushort glyph, float subX)
    {
        var indices = new[] { glyph };
        var advances = new[] { 0f };
        var offsets = new[] { 0f, 0f };
        var hi = GCHandle.Alloc(indices, GCHandleType.Pinned);
        var ha = GCHandle.Alloc(advances, GCHandleType.Pinned);
        var ho = GCHandle.Alloc(offsets, GCHandleType.Pinned);
        IntPtr analysis = IntPtr.Zero;
        try
        {
            var run = new GlyphRun
            {
                FontFace = face, FontEmSize = emSize, GlyphCount = 1, GlyphIndices = hi.AddrOfPinnedObject(), GlyphAdvances = ha.AddrOfPinnedObject(),
                GlyphOffsets = ho.AddrOfPinnedObject(), IsSideways = 0, BidiLevel = 0,
            };
            var transform = new DWriteMatrix { M11 = 1, M22 = 1, Dx = subX };
            // IDWriteFactory2::CreateGlyphRunAnalysis (slot 30), as Skia's scaler: grid fit enabled, the three-column (ClearType) analysis, origin 0
            if (Call<CreateAnalysisFn>(factory, 30)(factory, ref run, ref transform, mode, MeasuringModeNatural, GridFitEnabled, AntialiasClearType, 0, 0, out analysis) < 0) return null;
            // IDWriteGlyphRunAnalysis::GetAlphaTextureBounds (slot 3)
            if (Call<BoundsFn>(analysis, 3)(analysis, TextureClearType3x1, out var bounds) < 0) return null;
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return new VoicePillGlyphMask(0, 0, 0, 0, []);
            var texture = new byte[width * height * 3];
            // IDWriteGlyphRunAnalysis::CreateAlphaTexture (slot 4)
            if (Call<TextureFn>(analysis, 4)(analysis, TextureClearType3x1, ref bounds, texture, (uint)texture.Length) < 0) return null;
            var alpha = new byte[width * height];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = DesignContrast[(texture[3 * i] + texture[3 * i + 1] + texture[3 * i + 2]) / 3];
            return new VoicePillGlyphMask(bounds.Left, bounds.Top, width, height, alpha);
        }
        finally
        {
            if (analysis != IntPtr.Zero) Marshal.Release(analysis);
            hi.Free();
            ha.Free();
            ho.Free();
        }
    }

    private static T Call<T>(IntPtr self, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));

    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(int factoryType, ref Guid iid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateLoaderFn(IntPtr self, out IntPtr loader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RegisterLoaderFn(IntPtr self, IntPtr loader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateInMemoryReferenceFn(IntPtr self, IntPtr factory, IntPtr data, uint size, IntPtr owner, out IntPtr file);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateFontFaceFn(IntPtr self, int type, uint count, IntPtr[] files, uint index, int simulations, out IntPtr face);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateAnalysisFn(IntPtr self, ref GlyphRun run, ref DWriteMatrix transform, int renderingMode, int measuringMode, int gridFit, int antialias,
        float originX, float originY, out IntPtr analysis);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BoundsFn(IntPtr self, int textureType, out Bounds bounds);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TextureFn(IntPtr self, int textureType, ref Bounds bounds, [Out] byte[] alpha, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphRun
    {
        public IntPtr FontFace;
        public float FontEmSize;
        public uint GlyphCount;
        public IntPtr GlyphIndices;
        public IntPtr GlyphAdvances;
        public IntPtr GlyphOffsets;
        public int IsSideways;
        public uint BidiLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWriteMatrix
    {
        public float M11, M12, M21, M22, Dx, Dy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds
    {
        public int Left, Top, Right, Bottom;
    }
}
