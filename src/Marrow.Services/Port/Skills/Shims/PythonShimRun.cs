namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: ONDEMAND 3.4/3.6 require per-call ._pth without touching the locked runtime.
// ED-NEW-f-sk-6: private launch directory with copied executable/DLLs and an absolute ._pth.
public sealed class PythonShimRun : IDisposable
{
    private readonly string directory;
    public string Target { get; }
    private PythonShimRun(string directory, string target) => (this.directory, Target) = (directory, target);

    public static PythonShimRun Prepare(string target, string pathFile, IReadOnlyList<string> packages, string work)
    {
        var original = Path.GetDirectoryName(Path.GetFullPath(target))!;
        var directory = Path.Combine(work, "python-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var run = new PythonShimRun(directory, Path.Combine(directory, Path.GetFileName(target)));
        try
        {
            CopyForRun(target, run.Target);
            foreach (var file in Directory.GetFiles(original))
            {
                var extension = Path.GetExtension(file);
                // DLLs are loaded beside the copied interpreter. JSON is needed only by the offline .NET fake target.
                if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(file).Equals("pip.ini", StringComparison.OrdinalIgnoreCase))
                    CopyForRun(file, Path.Combine(directory, Path.GetFileName(file)));
            }
            var entries = File.ReadAllLines(pathFile).Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#') && l != "import site")
                .Select(l => Path.GetFullPath(l, original)).Concat(packages.Select(Path.GetFullPath)).Append("import site");
            File.WriteAllLines(Path.Combine(directory, Path.GetFileName(pathFile)), entries);
            return run;
        }
        catch { run.Dispose(); throw; }
    }
    private static void CopyForRun(string source, string destination)
    {
        File.Copy(source, destination);
        File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
    }
    public void Dispose() => Directory.Delete(directory, true);
}
