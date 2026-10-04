namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.6: one copied NativeAOT launcher per provided name.
// npm/npx remain .cmd; their sidecars target Node with the upstream CLI .js as a prefix argument.
// The installer supplies pinned paths. Locking and atomic version switching belong to SK-5/SK-8.
public static class ShimFiles
{
    public static string Write(string bin, string name, string nativeLauncher, ShimDefinition definition)
    {
        if (Path.GetFileName(name) != name || name.IndexOfAny(['\\', '/', '"', '%', '\r', '\n']) >= 0)
            throw new ArgumentException(nameof(name));
        var extension = Path.GetExtension(name);
        var batch = extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        if (!batch && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(nameof(name));
        Directory.CreateDirectory(bin);
        var entry = Path.Combine(bin, name);
        var launcher = batch ? Path.Combine(bin, ".launchers", Path.ChangeExtension(name, ".exe")) : entry;
        Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
        ShimDefinition.Write(Path.ChangeExtension(launcher, ".shim"), definition);
        File.Copy(nativeLauncher, launcher, true);
        if (batch)
            File.WriteAllText(entry, "@\"%~dp0.launchers\\" + Path.GetFileName(launcher) + "\" %*\r\n@exit /b %errorlevel%\r\n");
        return entry;
    }
}
