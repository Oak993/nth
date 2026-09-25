using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Project script files used by AppModel part 3 tests, under AppModelHarness's throwaway data root.</summary>
public static class AppModelPart3Fixtures
{
    public static SwiftURL ProjectURL(string path) => new Project("Project", path).Url;

    public static void WriteScripts(string projectPath, string json) =>
        File.WriteAllBytes(
            SwiftPath.AppendingPathComponent(projectPath, "droppy-code.json"),
            Encoding.UTF8.GetBytes(json));
}
