// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Skills.Install;

internal static class PysetInstaller
{
    public static async Task BuildAsync(InstallPlan plan, string target, IInstallProcess process, CancellationToken cancellationToken)
    {
        var options = plan.Pyset ?? throw new InvalidDataException("Python package parameters are required.");
        var local = Path.Combine(plan.DataRoot, "addons", "downloads", "tmp-" + Guid.NewGuid().ToString("N"));
        InstallPaths.NoReparseAncestors(local);
        Directory.CreateDirectory(local);
        try
        {
            var requirements = new List<string>();
            foreach (var wheel in options.Wheels)
            {
                if (!wheel.Path.EndsWith(".whl", StringComparison.OrdinalIgnoreCase) || wheel.Requirement.Contains('\n', StringComparison.Ordinal) || wheel.Requirement.Contains('\r', StringComparison.Ordinal) || wheel.Requirement.Contains(' ', StringComparison.Ordinal) || !Regex.IsMatch(wheel.Requirement, @"\A[A-Za-z0-9_.-]+==[A-Za-z0-9_.+!-]+\z", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Only a pinned local wheel requirement is accepted.");
                InstallHash.Verify(wheel.Path, wheel.Size, wheel.Sha256);
                InstallTree.Copy(wheel.Path, InstallPaths.Under(local, Path.GetFileName(wheel.Path)));
                requirements.Add(wheel.Requirement + " --hash=sha256:" + wheel.Sha256.ToLowerInvariant());
            }
            var lockFile = Path.Combine(local, "requirements.lock");
            File.WriteAllText(lockFile, string.Join("\n", requirements) + "\n", new UTF8Encoding(false));
            await Run(["-m", "pip", "install", "--no-index", "--find-links", local, "--require-hashes", "--no-deps", "--target", target, "-r", lockFile]);
            await Run(["-m", "compileall", "--invalidation-mode", "unchecked-hash", target]);
        }
        finally
        {
            // The staged copies are never needed after pip, and SK-5's GC does not sweep addons\downloads (ED-323 (2)).
            // A held file leaves only this tmp- folder, as an abrupt stop would.
            try { Directory.Delete(local, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        async Task Run(string[] argv)
        {
            var exit = await process.RunAsync(new InstallCommand(options.PythonExecutable, argv, local), cancellationToken);
            if (exit != 0) throw new InstallProcessException(exit);
        }
    }
}
