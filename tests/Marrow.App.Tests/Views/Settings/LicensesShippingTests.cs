using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
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
    internal const string LicenseSha256 = "0180cc054aff4d1012b8805e2d3adaafa0e633a460918369190446ceb455debf";
    internal const string UpstreamNoticesSha256 = "46aed5220dcffabaaf022ba97e93297b7bf2dfcffdd979f6c74f31c4cfc80c88";
    internal const int UpstreamNoticesLength = 11093;
    internal const string TrademarkSha256 = "0474b31ac5075ee9b2993fbd72df75d723fbc763c896b912e9536de3ff8a0975";

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

    // The build output the app reads (and the app project's own output): the first of the places PR-SETTINGS-249's windows_path ships the three documents;
    // the package built from the publish folder is LicensesPackTests.
    [Fact]
    [Trait("Req", "PR-SETTINGS-249")]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1007-1057
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1012-1056
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

    // The publish folder is what the installer packs (scripts/package-windows.ps1): the project copies the three documents to its root; the package built from it
    // holds them (LicensesPackTests, and the packaging script's own check).
    [Fact]
    [Trait("Req", "PR-SETTINGS-249")]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1049 (read from the bundle: here the folder beside the executable)
    public void The_project_publishes_the_three_documents_to_the_root_of_the_publish_folder_beside_the_executable()
    {
        // The tests above prove the BUILD output. The folder the installer packs is the publish folder, so the project must also say that each
        // of the three is copied there, under its own name at the root (a Content item's Link is its path relative to the output root).
        // `dotnet msbuild -t:ComputeResolvedFilesToPublishList -getItem:ResolvedFileToPublish` on the project lists LICENSE, TRADEMARK.md and
        // THIRD_PARTY_NOTICES.md with those relative paths and PreserveNewest.
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Marrow.App", "Marrow.App.csproj"));
        var contents = project.Descendants().Where(element => element.Name.LocalName == "Content").ToArray();
        foreach (var name in new[] { "LICENSE", "THIRD_PARTY_NOTICES.md", "TRADEMARK.md" })
        {
            var item = Assert.Single(contents, content => (string?)content.Attribute("Link") == name);
            Assert.Equal("PreserveNewest", (string?)item.Attribute("CopyToOutputDirectory"));
            Assert.Equal("PreserveNewest", (string?)item.Attribute("CopyToPublishDirectory"));
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
        Assert.Equal(["nth", "Third-party notices", "Trademarks"], Nodes<StackPanel>(content).Single(panel => panel.Spacing == 18).Children.Select(block => Text((Control)block).First()));
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
    [Trait("Req", "PR-SETTINGS-249")] // App/Views/Settings/SettingsView.swift:1020-1044: Show opens the popover, a click outside dismisses it (cancellation clause)
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1020-1044 (.popover(isPresented:): Scroll, and Click outside: dismisses)
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1020-1044 (Click Show to open; Scroll the popover)
    public void The_licenses_popover_scrolls_and_a_click_outside_it_dismisses_it_and_Show_opens_it_again() => With(() => new LicensesRow(), (view, _, h, window) =>
    {
        // A real popup on Avalonia's overlay host, with real input (SettingsRestPopupPresenter): the light dismissal is the popup's own.
        var show = Nodes<Button>(view).Single(button => Text(button).Contains("Show"));
        Click(window, show);
        Tick(h, 1000); // the popover grows in
        Drain(window);
        var popup = Assert.Single(Nodes<Popup>(view));
        Assert.True(popup.IsOpen);
        var scroll = Assert.Single(Nodes<ScrollViewer>(Assert.IsAssignableFrom<Control>(popup.Child)), viewer => viewer.Width == 480);
        Assert.Equal(440, scroll.Height);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, $"the documents are taller than the popover (extent {scroll.Extent}, viewport {scroll.Viewport})");

        // Scroll: a wheel over the popover moves its documents.
        var inside = scroll.TranslatePoint(new Avalonia.Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        window.MouseMove(inside);
        window.MouseWheel(inside, new Avalonia.Vector(0, -3));
        Drain(window);
        Assert.True(scroll.Offset.Y > 0, $"the wheel scrolled the documents (offset {scroll.Offset})");

        // Click outside: a press away from the popover closes it and lets go of its content.
        var outside = new Avalonia.Point(4, 4);
        Assert.False(new Avalonia.Rect(scroll.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, scroll.Bounds.Size).Contains(outside));
        window.MouseDown(outside, Avalonia.Input.MouseButton.Left);
        window.MouseUp(outside, Avalonia.Input.MouseButton.Left);
        Drain(window);
        Assert.False(popup.IsOpen);
        Assert.Null(popup.Child);

        // Show opens it again.
        Click(window, show);
        Tick(h, 1000);
        Drain(window);
        Assert.True(popup.IsOpen);
        Assert.NotNull(popup.Child);
    }, nativePopup: true);

    [Fact]
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1027-1032 (VStack(spacing: 8): the title at .system(.body, weight: .semibold) over the document in Chrome.secondaryText at 12 pt); that the text selects and copies is LicensesSelectionTests
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1027-1032
    public void Each_block_is_a_13_point_semibold_title_8_points_above_its_document_in_the_secondary_colour() => With(() => new LicensesRow(), (view, _, _, window) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        Click(window, Nodes<Button>(view).Single(button => Text(button).Contains("Show")));
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        Assert.Equal(Avalonia.Controls.PlacementMode.Bottom, popup.Placement); // :1023 .popover(isPresented:, arrowEdge: .bottom)
        var content = Open(popup);
        var blocks = Nodes<StackPanel>(content).Single(panel => panel.Spacing == 18).Children.Cast<StackPanel>().ToArray();
        Assert.Equal(3, blocks.Length);
        foreach (var block in blocks)
        {
            Assert.Equal(8, block.Spacing); // :1027 VStack(alignment: .leading, spacing: 8)
            Assert.Equal(2, block.Children.Count);
            var title = Assert.IsType<TextBlock>(block.Children[0]);
            Font(title, 13, Avalonia.Media.FontWeight.SemiBold); // :1029 .font(.system(.body, weight: .semibold)): body is 13 pt
            var body = Assert.IsType<MarkdownView>(block.Children[1]);
            Assert.Equal(12, MarkdownView.GetMarkdownPointSize(body)); // :1031 .environment(\.markdownPointSize, 12)
            Ink(body, Avalonia.Controls.Documents.TextElement.GetForeground(body), Marrow.UI.Chrome.Chrome.SecondaryText); // :1032 .foregroundStyle(Chrome.secondaryText)
        }
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
