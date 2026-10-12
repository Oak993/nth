using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace Marrow.App.Tests.Port;

// ED-NEW-lean-22 (lean unit L22): the desktop executable ships a manifest that adds only the segment heap and the
// Windows 10/11 supportedOS entry to what the SDK's default manifest already carried (identity and asInvoker).
// No DPI or other settings: Avalonia sets the DPI awareness itself. No headless budget fits this unit; the working
// set and boot verdict comes from the native N4 run. These guards pin the manifest itself, in source and in the
// built binaries (Debug build; the manifest is identical in Release).
public sealed class AppManifestTests
{
    private const string Asm1 = "urn:schemas-microsoft-com:asm.v1";
    private const string Asm2 = "urn:schemas-microsoft-com:asm.v2";
    private const string Asm3 = "urn:schemas-microsoft-com:asm.v3";
    private const string Compatibility = "urn:schemas-microsoft-com:compatibility.v1";
    private const string Settings2020 = "http://schemas.microsoft.com/SMI/2020/WindowsSettings";
    private const string Windows10And11 = "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";

    [Fact]
    [Trait("Decision", "ED-NEW-lean-22")]
    public void The_desktop_project_embeds_its_own_manifest()
    {
        var properties = XDocument.Load(ProjectPath()).Descendants().ToArray();
        Assert.Equal("app.manifest", properties.Single(x => x.Name.LocalName == "ApplicationManifest").Value);
        Assert.DoesNotContain(properties, x => x.Name.LocalName == "NoWin32Manifest");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-22")]
    public void The_manifest_adds_only_the_segment_heap_and_supported_os()
    {
        AssertManifest(XDocument.Load(ManifestPath()));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-22")]
    public void The_built_assembly_and_app_host_carry_the_manifest()
    {
        var assembly = typeof(Program).Assembly.Location;
        var appHost = Path.ChangeExtension(assembly, ".exe");
        Assert.True(File.Exists(appHost), $"The app host is missing beside {assembly}.");
        AssertManifest(EmbeddedManifest(assembly));
        AssertManifest(EmbeddedManifest(appHost));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-22")]
    public void Composite_ready_to_run_stays_off_unless_requested()
    {
        var properties = XDocument.Load(ProjectPath()).Descendants().ToArray();
        Assert.Equal("true", properties.Single(x => x.Name.LocalName == "PublishReadyToRun").Value);
        var composite = properties.Single(x => x.Name.LocalName == "PublishReadyToRunComposite");
        Assert.Equal("'$(MarrowReadyToRunComposite)' == 'true'", (string?)composite.Attribute("Condition"));
    }

    private static void AssertManifest(XDocument manifest)
    {
        var root = manifest.Root!;
        Assert.Equal(XName.Get("assembly", Asm1), root.Name);

        // The complete element set, in document order: nothing else (no dpiAware, dpiAwareness, longPathAware, ...).
        var names = root.DescendantsAndSelf().Select(x => x.Name.LocalName).ToArray();
        Assert.Equal(
            new[]
            {
                "assembly", "assemblyIdentity", "trustInfo", "security", "requestedPrivileges", "requestedExecutionLevel",
                "compatibility", "application", "supportedOS", "application", "windowsSettings", "heapType",
            },
            names);

        var level = root.Descendants(XName.Get("requestedExecutionLevel", Asm3)).Single();
        Assert.Equal("asInvoker", (string?)level.Attribute("level"));
        Assert.Equal("false", (string?)level.Attribute("uiAccess"));
        Assert.Single(root.Elements(XName.Get("trustInfo", Asm2)));

        var supported = root.Descendants(XName.Get("supportedOS", Compatibility)).Single();
        Assert.Equal(Windows10And11, (string?)supported.Attribute("Id"));

        var heap = root.Descendants(XName.Get("heapType", Settings2020)).Single();
        Assert.Equal("SegmentHeap", heap.Value);
        Assert.Equal(XName.Get("windowsSettings", Asm3), heap.Parent!.Name);
    }

    private static XDocument EmbeddedManifest(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var start = IndexOf(bytes, "<assembly ");
        Assert.True(start >= 0, $"No embedded manifest in {path}.");
        var endTag = "</assembly>";
        var end = IndexOf(bytes, endTag, start);
        Assert.True(end > start, $"The embedded manifest in {path} is not closed.");
        Assert.True(IndexOf(bytes, "<assembly ", end) < 0, $"More than one manifest is embedded in {path}.");
        return XDocument.Parse(Encoding.UTF8.GetString(bytes, start, end + endTag.Length - start));
    }

    private static int IndexOf(byte[] bytes, string text, int from = 0)
    {
        var needle = Encoding.ASCII.GetBytes(text);
        var index = bytes.AsSpan(from).IndexOf(needle);
        return index < 0 ? -1 : from + index;
    }

    private static string ProjectPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../../src/Marrow.App/Marrow.App.csproj"));

    private static string ManifestPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../../src/Marrow.App/app.manifest"));
}
