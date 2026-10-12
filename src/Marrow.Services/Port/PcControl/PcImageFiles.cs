// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-41; DESIGN.md 2.4, 3.4). Screenshots as files, for a client whose MCP
// bridge turns image parts into "[image]" (Pi and Command Code read the JPEG with their own file tool instead). One folder
// per chat; only the newest few files stay, so a long run leaves no trail of old screens on disk.
using System.Security.Cryptography;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public static class PcImageFiles
{
    /// <summary>
    /// Saves each image as a JPEG in <paramref name="folder"/> (made when missing) and returns its observation id and path,
    /// in order; then deletes all but the newest <see cref="PcProviderWiring.KeptImageFiles"/> files there. A file that
    /// cannot be written is left out (the text then names no path for it); nothing here throws.
    /// </summary>
    public static IReadOnlyList<(string ObsId, string Path)> Save(string folder, IReadOnlyList<PcImage> images)
    {
        var saved = new List<(string, string)>();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return saved;
        }
        foreach (var image in images)
        {
            var path = Path.Combine(folder, SafeName(image.ObsId) + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant() + ".jpg");
            try
            {
                File.WriteAllBytes(path, image.Jpeg);
                saved.Add((image.ObsId, path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        Prune(folder);
        return saved;
    }

    private static void Prune(string folder)
    {
        try
        {
            var old = new DirectoryInfo(folder).GetFiles("*.jpg")
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(PcProviderWiring.KeptImageFiles);
            foreach (var file in old)
            {
                try { file.Delete(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private static string SafeName(string obsId) =>
        obsId.Length == 0 ? "screen" : string.Concat(obsId.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
}
