using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Marrow.App.Tests.Port;

// T44: the actual desktop executable uses concurrent workstation GC, independent of the testhost.
public sealed class DesktopRuntimePolicyTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void The_desktop_project_uses_workstation_background_collection()
    {
        var properties = XDocument.Load(ProjectPath()).Descendants().ToArray();
        Assert.Equal("false", properties.Single(x => x.Name.LocalName == "ServerGarbageCollection").Value);
        Assert.Equal("true", properties.Single(x => x.Name.LocalName == "ConcurrentGarbageCollection").Value);
    }

    private static string ProjectPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../../src/Marrow.App/Marrow.App.csproj"));
}
