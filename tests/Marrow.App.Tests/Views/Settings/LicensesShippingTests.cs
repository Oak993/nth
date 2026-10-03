using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), LicensesRow :1007-1057; project.yml:30-34
// (LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md are resources of the app bundle); OD-0002 (the three files continue
// to ship, AGPLv3 section 7).
//
// A real install showed "The LICENSE file is missing from this copy of the app." three times because nothing put the
// files next to the executable. These tests read what the app reads: AppContext.BaseDirectory, and the app project's own
// output folder (the folder the installer's publish step starts from).
[Collection("Headless")]
public sealed class LicensesShippingTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    // The pinned upstream files are git blobs of commit 52bc7926097cd622d08d6294e0d8b08a48c7516e (LF line endings):
    // LICENSE aa22744177a2a2baa627f035a5f1605ab3bdf9c4, THIRD_PARTY_NOTICES.md 02fb89f5b7217a35bc5aa3e248a5a43327dd97ca,
    // TRADEMARK.md 24f643399c3b0ef497ce8ebc5bd4f27afb18e96b. The digests are of those blobs' bytes.
    private const string LicenseSha256 = "0180cc054aff4d1012b8805e2d3adaafa0e633a460918369190446ceb455debf";
    private const string UpstreamNoticesSha256 = "46aed5220dcffabaaf022ba97e93297b7bf2dfcffdd979f6c74f31c4cfc80c88";
    private const int UpstreamNoticesLength = 11093;
    private const string TrademarkSha256 = "0474b31ac5075ee9b2993fbd72df75d723fbc763c896b912e9536de3ff8a0975";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    // tests\Marrow.App.Tests\bin\<configuration>\<framework>\ -> src\Marrow.App\bin\<configuration>\<framework>\
    private static string AppOwnOutputFolder()
    {
        var root = RepositoryRoot();
        var relative = Path.GetRelativePath(Path.Combine(root, "tests", "Marrow.App.Tests"), AppContext.BaseDirectory);
        var own = Path.GetFullPath(Path.Combine(root, "src", "Marrow.App", relative));
        Assert.True(Directory.Exists(own), $"The app project's own output folder was not found at {own}.");
        return own;
    }

    private static string LicensesReader(string resource) =>
        (string)typeof(LicensesRow).GetMethod("Text", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [resource])!;

    private static string ReadFrom(string resource, string directory) =>
        (string)typeof(LicensesRow).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic, [typeof(string), typeof(string)])!.Invoke(null, [resource, directory])!;

    private static string Missing(string resource) => $"The {resource} file is missing from this copy of the app.";

    [Fact]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1007-1057
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1012-1056
    [Trait("Req", "PR-SETTINGS-249")] // App/Views/Settings/SettingsView.swift:1009-1057; project.yml:30-34
    public void The_three_legal_documents_ship_in_the_folder_the_app_reads_and_in_the_app_projects_own_output()
    {
        var addendum = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "THIRD_PARTY_NOTICES.md"));
        foreach (var folder in new[] { AppContext.BaseDirectory, AppOwnOutputFolder() })
        {
            // LICENSE has no extension; the other two are .md (SettingsView.swift:1049).
            Assert.Equal(LicenseSha256, Sha256(File.ReadAllBytes(Path.Combine(folder, "LICENSE"))));
            Assert.Equal(TrademarkSha256, Sha256(File.ReadAllBytes(Path.Combine(folder, "TRADEMARK.md"))));

            // The notices are upstream's file, byte for byte, then the port's own additions (OD-0002, OD-0089): one document.
            var notices = File.ReadAllBytes(Path.Combine(folder, "THIRD_PARTY_NOTICES.md"));
            Assert.Equal(UpstreamNoticesLength + 1 + addendum.Length, notices.Length);
            Assert.Equal(UpstreamNoticesSha256, Sha256(notices[..UpstreamNoticesLength]));
            Assert.Equal((byte)'\n', notices[UpstreamNoticesLength]);
            Assert.Equal(addendum, notices[(UpstreamNoticesLength + 1)..]);
        }
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1046-1056
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1046-1056
    public void The_licenses_popover_shows_the_shipped_documents_not_the_missing_file_sentence() => With(() => new LicensesRow(), (view, _, _, window) =>
    {
        foreach (var (resource, file) in new[] { ("LICENSE", "LICENSE"), ("THIRD_PARTY_NOTICES", "THIRD_PARTY_NOTICES.md"), ("TRADEMARK", "TRADEMARK.md") })
        {
            var shipped = StrictUtf8.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, file)));
            Assert.NotEqual("", shipped);
            Assert.Equal(shipped, LicensesReader(resource));
            Assert.NotEqual(Missing(resource), LicensesReader(resource));
        }
        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", LicensesReader("LICENSE"), StringComparison.Ordinal);
        Assert.Contains("# Droppy Code Trademark Policy", LicensesReader("TRADEMARK"), StringComparison.Ordinal);
        Assert.Contains("border-beam", LicensesReader("THIRD_PARTY_NOTICES"), StringComparison.Ordinal); // the port's own additions (OD-0089)

        // Open the popover: three blocks, each with its heading and a rendered document below it.
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        var show = Nodes<Button>(view).Single(button => Text(button).Contains("Show"));
        Click(window, show);
        var content = Open(presenter.Calls.Last(call => call.Presented).Popup);
        Assert.Equal(["Marrow", "Third-party notices", "Trademarks"], Nodes<StackPanel>(content).Single(panel => panel.Spacing == 18).Children.Select(block => Text((Control)block).First()));
        var bodies = Nodes<MarkdownView>(content);
        string[] resources = ["LICENSE", "THIRD_PARTY_NOTICES", "TRADEMARK"];
        Assert.Equal(resources.Length, bodies.Length);
        for (var index = 0; index < bodies.Length; index++)
        {
            var resource = resources[index];
            Assert.Equal(LicensesReader(resource), bodies[index].Text); // the document, not the sentence
            bodies[index].Measure(new Avalonia.Size(444, double.PositiveInfinity));
            Assert.True(bodies[index].DesiredSize.Height > 100, $"{resource} renders as a real document, not one line.");
        }
        Assert.DoesNotContain(Text(content), line => line.Contains("is missing from this copy of the app", StringComparison.Ordinal));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1046-1056 (a file that cannot be read shows the sentence naming the resource)
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1051 (the sentence names the RESOURCE, not the title)
    public void A_folder_without_the_files_gives_the_sentence_naming_each_resource_and_the_files_are_read_with_their_names()
    {
        var empty = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "licenses-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        foreach (var resource in new[] { "LICENSE", "THIRD_PARTY_NOTICES", "TRADEMARK" }) Assert.Equal(Missing(resource), ReadFrom(resource, empty));

        // LICENSE has no extension, the other two are .md: the right name is read for each.
        var named = Path.Combine(empty, "named");
        Directory.CreateDirectory(named);
        File.WriteAllText(Path.Combine(named, "LICENSE"), "license text");
        File.WriteAllText(Path.Combine(named, "THIRD_PARTY_NOTICES.md"), "notices text");
        File.WriteAllText(Path.Combine(named, "TRADEMARK.md"), "trademark text");
        Assert.Equal("license text", ReadFrom("LICENSE", named));
        Assert.Equal("notices text", ReadFrom("THIRD_PARTY_NOTICES", named));
        Assert.Equal("trademark text", ReadFrom("TRADEMARK", named));
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1051 (String(contentsOf:encoding:.utf8) throws on bytes that are not UTF-8; try? turns that into the sentence)
    public void A_file_that_is_not_valid_utf8_shows_the_sentence_as_swift_does()
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "licenses-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "LICENSE"), [0x41, 0xFF, 0xFE, 0x42]);
        Assert.Equal(Missing("LICENSE"), ReadFrom("LICENSE", folder));
        File.WriteAllBytes(Path.Combine(folder, "TRADEMARK.md"), Encoding.UTF8.GetBytes("café — fine"));
        Assert.Equal("café — fine", ReadFrom("TRADEMARK", folder));
    }
}
