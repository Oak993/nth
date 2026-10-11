// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Text;

namespace Marrow.Services.Port.Skills.Install;

internal static class PythonEmbedInstaller
{
    public static void Build(InstallPlan plan, string target, string archive)
    {
        var options = plan.Python ?? throw new InvalidDataException("Python embed parameters are required.");
        if (options.PipWheel is null) throw new InvalidDataException("The pinned pip wheel is required.");
        ZipInstaller.Extract(archive, target);
        InstallPaths.Relative(options.StandardLibraryZip);
        InstallPaths.Relative(options.AgentVersion);
        var agent = InstallPaths.Under(plan.DataRoot, "addons/work/python-agent/" + options.AgentVersion);
        // Exact LF text, UTF-8 without BOM. Absolute work path is the fourth line (ONDEMAND 3.4).
        File.WriteAllText(InstallPaths.Under(target, options.PthFile), options.StandardLibraryZip + "\n.\nLib\\site-packages\n" + Path.GetFullPath(agent) + "\nimport site\n", new UTF8Encoding(false));
        // ONDEMAND 3.6 "Agent package installs": a site pip.ini beside Python sends the agent's own `pip install x` to the writable agent folder on the ._pth, never into a
        // locked set. The shim copies it next to the per-run interpreter (PythonShimRun), which is sys.prefix there (ED-NEW-dl-7). Exact LF text, UTF-8 without BOM.
        var pipConfig = InstallPaths.Under(target, "pip.ini");
        if (File.Exists(pipConfig)) throw new InvalidDataException("The Python archive already holds a pip.ini.");
        File.WriteAllText(pipConfig, "[install]\ntarget = " + Path.GetFullPath(agent) + "\n", new UTF8Encoding(false));
        if (options.PipWheel is { } pip)
        {
            InstallHash.Verify(pip.Path, pip.Size, pip.Sha256);
            ZipInstaller.Extract(pip.Path, InstallPaths.Under(target, "Lib/site-packages"));
        }
    }
}
