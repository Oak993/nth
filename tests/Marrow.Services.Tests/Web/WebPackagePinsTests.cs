using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.1, 14 (W7 "Directory.Packages.props"): the web engine's packages are pinned exactly, the Services project takes
// nothing else, and the build carries the managed WebView2 Core assembly and one win-x64 loader beside the app, never a
// loader for another platform and never the SDK's WPF or WinForms assemblies (ED-NEW-web-w7-1).
[Trait("Decision", "ED-NEW-web-w7-1")]
[Trait("Feature", "W-7")]
public sealed partial class WebPackagePinsTests
{
    private const ushort MachineAmd64 = 0x8664;

    [GeneratedRegex("<PackageReference Include=\"(?<id>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PackageReference();

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Directory.Packages.props was not found above " + AppContext.BaseDirectory);
    }

    private static string Text(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]), Encoding.UTF8);

    [Fact]
    public void Both_web_packages_are_pinned_to_exact_versions()
    {
        var props = Text("Directory.Packages.props");
        Assert.Contains("""<PackageVersion Include="Avalonia.Controls.WebView" Version="12.1.0" />""", props, StringComparison.Ordinal);
        Assert.Contains("""<PackageVersion Include="Microsoft.Web.WebView2" Version="1.0.4258.31" />""", props, StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="Avalonia.Controls.WebView" />""", Text("src", "Marrow.UI", "Marrow.UI.csproj"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_Services_project_takes_exactly_the_voice_packages_and_WebView2_which_has_no_dependencies()
    {
        var project = Text("src", "Marrow.Services", "Marrow.Services.csproj");
        var ids = PackageReference().Matches(project).Select(m => m.Groups["id"].Value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Microsoft.Web.WebView2", "NAudio.Wasapi", "Whisper.net", "Whisper.net.Runtime", "Whisper.net.Runtime.Vulkan"], ids);
        Assert.Contains("""<PackageReference Include="Microsoft.Web.WebView2" ExcludeAssets="build;buildTransitive;native" GeneratePathProperty="true" />""", project, StringComparison.Ordinal);

        // The package declares no dependency, so it adds nothing to the Services project's transitive closure.
        using var assets = JsonDocument.Parse(Text("src", "Marrow.Services", "obj", "project.assets.json"));
        var folder = assets.RootElement.GetProperty("packageFolders").EnumerateObject().First().Name;
        var nuspec = File.ReadAllText(Path.Combine(folder, "microsoft.web.webview2", "1.0.4258.31", "microsoft.web.webview2.nuspec"), Encoding.UTF8);
        Assert.Contains("<dependencies>", nuspec, StringComparison.Ordinal);
        Assert.DoesNotContain("<dependency ", nuspec, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_carries_the_Core_assembly_and_one_x64_loader_beside_the_app_and_nothing_else_of_the_SDK()
    {
        var output = AppContext.BaseDirectory;
        Assert.True(File.Exists(Path.Combine(output, "Microsoft.Web.WebView2.Core.dll")));
        Assert.False(File.Exists(Path.Combine(output, "Microsoft.Web.WebView2.Wpf.dll")));
        Assert.False(File.Exists(Path.Combine(output, "Microsoft.Web.WebView2.WinForms.dll")));

        // One loader at the root of each app folder (this output and the skills pipeline worker's copy beside it, which
        // also references Services), always x64, never under runtimes\.
        var loaders = Directory.EnumerateFiles(output, "WebView2Loader.dll", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(output, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["WebView2Loader.dll", "pipeline-worker/WebView2Loader.dll"], loaders);
        Assert.All(loaders, loader => Assert.Equal(MachineAmd64, PeMachine(Path.Combine(output, loader))));
    }

    [Fact]
    public void The_notices_name_both_packages_at_their_pinned_versions()
    {
        var notices = Text("THIRD_PARTY_NOTICES.md");
        Assert.Contains("Avalonia.Controls.WebView 12.1.0", notices, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Web.WebView2 1.0.4258.31", notices, StringComparison.Ordinal);
    }

    private static ushort PeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3C;
        var header = reader.ReadInt32();
        stream.Position = header;
        Assert.Equal(0x00004550u, reader.ReadUInt32()); // "PE\0\0"
        return reader.ReadUInt16();
    }
}
