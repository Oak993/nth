using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1-6.3, 13.1 ArtifactStoreTests: versions, edits must match once,
// restore, fork, export paths; plus projects, ids, limits and the files on disk). Every store works in its own
// throwaway threads folder.
[Trait("Decision", "ED-NEW-web-w12-1")]
public sealed class ArtifactStoreTests
{
    private static readonly Guid ThreadA = Guid.Parse("0b7e3f1a-1c2d-4e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid ThreadB = Guid.Parse("0b7e3f1a-1c2d-4e5f-8a9b-0c1d2e3f4a5c");
    private static readonly Guid Turn = Guid.Parse("7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d");
    private static readonly ArtifactAuthor Model = new("claude", "sonnet-5", Turn);

    private static ArtifactDraft Page(string title = "Pricing calculator", string content = "<h3>Orbit pricing</h3>\n<p>Pick seats.</p>\n", string? id = null) =>
        new() { Kind = ArtifactKind.Html, Title = title, Content = content, Id = id };

    private static ArtifactDraft Site(params (string Path, string Content)[] files) =>
        new() { Kind = ArtifactKind.Project, Title = "Orbit site", Files = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal) };

    private static string Message(Action action) => Assert.Throws<ArtifactException>(action).Message;

    [Fact]
    public void A_project_accepts_512_files_and_keeps_the_version_byte_limit()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        var files = Enumerable.Range(1, 511).Select(i => ($"f{i}.js", "x")).Prepend(("index.html", "x")).ToArray();
        var made = store.Create(ThreadA, Site(files), Model, null);
        Assert.Equal(512, store.Read(ThreadA, made.Artifact.Id).Files!.Count);
        Assert.Contains("5 MB", Message(() => store.Create(ThreadA, Site(("index.html", new string('x', (int)ArtifactStore.MaxVersionBytes + 1))), Model, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_writes_v1_and_meta_json_in_the_thread_data_folder_and_reads_them_back()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        var changes = new List<ArtifactChange>();
        store.Changed += changes.Add;

        var made = store.Create(ThreadA, Page(), Model, "Make a pricing calculator for Orbit with yearly billing.");

        Assert.Equal("pricing-calculator", made.Artifact.Id);
        var folder = Path.Combine(scratch.Threads, SwiftUUID.UuidString(ThreadA), "artifacts", "pricing-calculator");
        Assert.Equal(folder, store.Folder(ThreadA, "pricing-calculator"));
        Assert.Equal(new[] { "meta.json", "v1.html" }, Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("<h3>Orbit pricing</h3>\n<p>Pick seats.</p>\n", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "v1.html"))));
        var read = store.Read(ThreadA, "pricing-calculator");
        Assert.Equal(made.Text, read.Text);
        Assert.Equal((1, Model, "Make a pricing calculator for Orbit with yearly billing."), (read.Version.Number, read.Version.Author, read.Version.Prompt));
        Assert.Equal(("Pricing calculator", ArtifactKind.Html, 1), (read.Artifact.Title, read.Artifact.Kind, read.Artifact.Current));
        var change = Assert.Single(changes);
        Assert.Equal(new ArtifactChange(ThreadA, "pricing-calculator", "Pricing calculator", ArtifactKind.Html, 1, ArtifactChangeKind.Created), change);
        Assert.True(change.Created);
    }

    [Fact]
    public void Every_update_is_a_new_version_and_every_old_version_stays_readable()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, Page(), Model, "first");
        var changes = new List<ArtifactChange>();
        store.Changed += changes.Add;

        var v2 = store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Content = "<h3>Orbit</h3>\n" }, Model, "second");
        var v3 = store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Edits = [new("Orbit", "Orbit pricing"), new("pricing</h3>", "pricing</h3>\n<p>Yearly saves 20%.</p>")] }, ArtifactAuthor.You, null);

        Assert.Equal((2, 3), (v2.Version.Number, v3.Version.Number));
        Assert.Equal("<h3>Orbit pricing</h3>\n<p>Yearly saves 20%.</p>\n", v3.Text); // edits apply in order, each to what the last left
        Assert.Equal("<h3>Orbit pricing</h3>\n<p>Pick seats.</p>\n", store.Read(ThreadA, "pricing-calculator", 1).Text);
        Assert.Equal("<h3>Orbit</h3>\n", store.Read(ThreadA, "pricing-calculator", 2).Text);
        var artifact = store.Find(ThreadA, "pricing-calculator")!;
        Assert.Equal(new[] { 1, 2, 3 }, artifact.Versions.Select(v => v.Number));
        Assert.Equal(3, artifact.Current);
        Assert.True(artifact.Versions[2].Author.IsYou);
        Assert.Null(artifact.Versions[2].Prompt);
        Assert.True(artifact.Versions[0].Timestamp < artifact.Versions[1].Timestamp && artifact.Versions[1].Timestamp < artifact.Versions[2].Timestamp);
        Assert.Equal(new[] { 2, 3 }, changes.Select(c => c.Version));
        Assert.All(changes, c => Assert.Equal(ArtifactChangeKind.Updated, c.Change));
        Assert.Contains("has no v9; its versions are v1 to v3", Message(() => store.Read(ThreadA, "pricing-calculator", 9)), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_edit_must_match_exactly_once_and_a_refused_update_changes_nothing()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, Page(content: "aaa and seats, seats\n"), Model, null);
        string Refused(params ArtifactTextEdit[] edits) => Message(() => store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Edits = edits }, Model, null));

        Assert.Contains("Edit 1 of 1: the find text is not in v1", Refused(new ArtifactTextEdit("missing", "x")), StringComparison.Ordinal);
        Assert.Contains("matches 2 places in v1", Refused(new ArtifactTextEdit("seats", "x")), StringComparison.Ordinal);
        Assert.Contains("matches 2 places", Refused(new ArtifactTextEdit("aa", "b")), StringComparison.Ordinal); // overlapping matches count
        Assert.Contains("the find text is empty", Refused(new ArtifactTextEdit("", "x")), StringComparison.Ordinal);
        Assert.Contains("Edit 2 of 2", Refused(new ArtifactTextEdit("and", "or"), new ArtifactTextEdit("nowhere", "x")), StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", Refused(new ArtifactTextEdit("seats", "x")), StringComparison.Ordinal);
        Assert.Contains("names a path, but this artifact is one file", Refused(new ArtifactTextEdit("aaa", "x", "index.html")), StringComparison.Ordinal);
        Assert.Contains("edits is empty", Refused(), StringComparison.Ordinal);

        Assert.Equal(1, store.Find(ThreadA, "pricing-calculator")!.Current);
        Assert.Equal(new[] { "meta.json", "v1.html" }, Directory.GetFiles(store.Folder(ThreadA, "pricing-calculator")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var fixedUp = store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Edits = [new("seats, seats", "seats")] }, Model, null);
        Assert.Equal("aaa and seats\n", fixedUp.Text);
    }

    [Fact]
    public void A_find_written_with_bare_line_feeds_matches_a_version_saved_with_CRLF()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, Page(content: "<ul>\r\n<li>One</li>\r\n</ul>\r\n"), Model, null);
        var edited = store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Edits = [new("<li>One</li>\n</ul>", "<li>One</li>\n<li>Two</li>\n</ul>")] }, Model, null);
        Assert.Equal("<ul>\r\n<li>One</li>\r\n<li>Two</li>\r\n</ul>\r\n", edited.Text);
    }

    [Fact]
    public void Restore_makes_an_old_version_the_new_latest_and_keeps_every_version()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, Page(content: "one\n"), Model, "make it");
        store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Content = "two\n" }, Model, "change it");

        var restored = store.Restore(ThreadA, "pricing-calculator", 1, ArtifactAuthor.You);

        Assert.Equal((3, "one\n", 1), (restored.Version.Number, restored.Text, restored.Version.RestoredFrom));
        Assert.True(restored.Version.Author.IsYou);
        Assert.Null(restored.Version.Prompt);
        Assert.Equal(3, restored.Artifact.Current);
        Assert.Equal("two\n", store.Read(ThreadA, "pricing-calculator", 2).Text);
        Assert.Contains("has no v7", Message(() => store.Restore(ThreadA, "pricing-calculator", 7, ArtifactAuthor.You)), StringComparison.Ordinal);
    }

    [Fact]
    public void Fork_copies_a_version_into_a_new_artifact_in_this_thread_or_another()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, Page(content: "one\n"), Model, null);
        store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Content = "two\n" }, Model, null);

        var remix = store.Fork(ThreadA, "pricing-calculator", 1, ThreadA, ArtifactAuthor.You, "Pricing calculator, remix");
        var copied = store.Fork(ThreadA, "pricing-calculator", 2, ThreadB, ArtifactAuthor.You);

        Assert.Equal(("pricing-calculator-2", "Pricing calculator, remix", "one\n"), (remix.Artifact.Id, remix.Artifact.Title, remix.Text));
        Assert.Equal(new ArtifactOrigin(ThreadA, "pricing-calculator", 1), remix.Version.ForkedFrom);
        Assert.Equal((ThreadB, "pricing-calculator", "Pricing calculator", "two\n"), (copied.Artifact.Thread, copied.Artifact.Id, copied.Artifact.Title, copied.Text));
        Assert.Equal(new ArtifactOrigin(ThreadA, "pricing-calculator", 2), copied.Version.ForkedFrom);
        Assert.Equal(1, copied.Version.Number);
        Assert.Equal(2, store.Find(ThreadA, "pricing-calculator")!.Current); // the source is untouched
    }

    [Fact]
    public void A_project_keeps_each_version_as_a_folder_and_files_null_deletes_one()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        var v1 = store.Create(ThreadA, Site(("index.html", "<link href=\"style.css\">\n"), ("style.css", "body{}\n"), ("src\\app.js", "export const a = 1;\n")), Model, null);

        Assert.Equal("orbit-site", v1.Artifact.Id);
        Assert.Equal("index.html", v1.Version.Entry);
        Assert.Equal(new[] { "index.html", "src/app.js", "style.css" }, v1.Version.Files);
        Assert.Null(v1.Version.File);
        var folder = store.Folder(ThreadA, "orbit-site");
        Assert.Equal("export const a = 1;\n", File.ReadAllText(Path.Combine(folder, "v1", "src", "app.js")));

        var v2 = store.Update(ThreadA, "orbit-site", new ArtifactRevision
        {
            Files = new Dictionary<string, string?> { ["style.css"] = "body{margin:0}\n", ["src/app.js"] = null, ["README.md"] = "# Orbit\n" },
        }, Model, null);
        Assert.Equal(new[] { "README.md", "index.html", "style.css" }, v2.Version.Files);
        Assert.Equal("body{margin:0}\n", v2.Files!["style.css"]);
        Assert.Equal(3, store.Read(ThreadA, "orbit-site", 1).Files!.Count); // v1's folder is untouched
        Assert.True(Directory.Exists(Path.Combine(folder, "v2")));
        Assert.DoesNotContain(Directory.GetDirectories(folder), d => Path.GetFileName(d).Contains("partial", StringComparison.Ordinal));

        var v3 = store.Update(ThreadA, "orbit-site", new ArtifactRevision { Edits = [new("margin:0", "margin:8px", "style.css"), new("# Orbit", "# Orbit site")] }, Model, null);
        Assert.Equal(("body{margin:8px}\n", "# Orbit site\n"), (v3.Files!["style.css"], v3.Files["README.md"]));

        Assert.Contains("is in 3 files", Message(() => store.Update(ThreadA, "orbit-site", new ArtifactRevision { Edits = [new("i", "x")] }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("\"nope.css\" is not a file of v3", Message(() => store.Update(ThreadA, "orbit-site", new ArtifactRevision { Edits = [new("a", "b", "nope.css")] }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("cannot be deleted", Message(() => store.Update(ThreadA, "orbit-site", new ArtifactRevision { Files = new Dictionary<string, string?> { ["gone.js"] = null } }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("needs an entry file", Message(() => store.Update(ThreadA, "orbit-site", new ArtifactRevision { Files = new Dictionary<string, string?> { ["index.html"] = null } }, Model, null)), StringComparison.Ordinal);
        var moved = store.Update(ThreadA, "orbit-site", new ArtifactRevision { Files = new Dictionary<string, string?> { ["index.html"] = null, ["app.html"] = "<p>app</p>" } }, Model, null);
        Assert.Equal("app.html", moved.Version.Entry); // the one page left becomes the entry
        Assert.Contains("\"main.html\" is not one of", Message(() => store.Update(ThreadA, "orbit-site", new ArtifactRevision { Files = new Dictionary<string, string?> { ["b.html"] = "b" }, Entry = "main.html" }, Model, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_paths_are_relative_safe_and_unique_and_the_limits_hold()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        foreach (var bad in new[] { "../x.js", "/abs.js", "C:/x.js", "a//b.js", "a/./b.js", "con.txt", "src/LPT1", "what?.js", "trailing.", "" })
            Assert.Contains("cannot be a project file path", Message(() => ArtifactStore.ProjectPath(bad)), StringComparison.Ordinal);
        Assert.Equal("src/app.js", ArtifactStore.ProjectPath(".\\src\\app.js"));
        Assert.Contains("longer than 160", Message(() => ArtifactStore.ProjectPath(new string('a', 161))), StringComparison.Ordinal);
        Assert.Contains("8 folders deep", Message(() => ArtifactStore.ProjectPath(string.Join('/', Enumerable.Repeat("d", 10)))), StringComparison.Ordinal);

        Assert.Contains("Two files have the path", Message(() => store.Create(ThreadA, Site(("index.html", "x"), ("A.js", "a"), ("a.js", "b")), Model, null)), StringComparison.Ordinal);
        Assert.Contains("needs an entry file", Message(() => store.Create(ThreadA, Site(("a.html", "a"), ("b.html", "b")), Model, null)), StringComparison.Ordinal);
        Assert.Equal("only.html", store.Create(ThreadA, Site(("only.html", "a"), ("style.css", "b")), Model, null).Version.Entry);
        var many = Enumerable.Range(0, ArtifactStore.MaxProjectFiles).Select(i => ($"f{i}.js", "x")).Append(("index.html", "x")).ToArray();
        Assert.Contains($"at most {ArtifactStore.MaxProjectFiles} files", Message(() => store.Create(ThreadA, Site(many), Model, null)), StringComparison.Ordinal);
        var big = new string('x', 3 * 1024 * 1024);
        Assert.Contains("larger than 5 MB together", Message(() => store.Create(ThreadA, Site(("index.html", big), ("b.js", big)), Model, null)), StringComparison.Ordinal);
        Assert.Contains("larger than 5 MB", Message(() => store.Create(ThreadA, Page(content: big + big), Model, null)), StringComparison.Ordinal);
        Assert.Contains("needs at least one file", Message(() => store.Create(ThreadA, Site(), Model, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_kind_takes_its_own_content_and_an_update_takes_exactly_one_mode()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        Assert.Contains("Only a project takes files", Message(() => store.Create(ThreadA, Page() with { Files = new Dictionary<string, string> { ["a"] = "b" } }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("A project is made of files", Message(() => store.Create(ThreadA, new ArtifactDraft { Kind = ArtifactKind.Project, Title = "P", Content = "x" }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("The content is empty", Message(() => store.Create(ThreadA, Page(content: "  \n"), Model, null)), StringComparison.Ordinal);
        store.Create(ThreadA, Page(), Model, null);
        store.Create(ThreadA, Site(("index.html", "x")), Model, null);
        Assert.Contains("Pass the new content, edits", Message(() => store.Update(ThreadA, "pricing-calculator", new ArtifactRevision(), Model, null)), StringComparison.Ordinal);
        Assert.Contains("only one of content, edits or files", Message(() => store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Content = "a", Edits = [new("a", "b")] }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("is one file, not a project", Message(() => store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Files = new Dictionary<string, string?> { ["a"] = "b" } }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("is a project", Message(() => store.Update(ThreadA, "orbit-site", new ArtifactRevision { Content = "x" }, Model, null)), StringComparison.Ordinal);
        Assert.Contains("This chat has no artifact \"nothing\"", Message(() => store.Update(ThreadA, "Nothing", new ArtifactRevision { Content = "x" }, Model, null)), StringComparison.Ordinal);

        var code = store.Create(ThreadA, new ArtifactDraft { Kind = ArtifactKind.Code, Title = "Fetch", Language = "Python", Content = "print(1)\n" }, Model, null);
        Assert.Equal(("python", "v1.py"), (code.Artifact.Language, code.Version.File));
        var component = store.Create(ThreadA, new ArtifactDraft { Kind = ArtifactKind.React, Title = "Card", Language = "TypeScript", Content = "export default () => null;\n" }, Model, null);
        Assert.Equal(("tsx", "v1.tsx"), (component.Artifact.Language, component.Version.File));
        Assert.Null(store.Create(ThreadA, Page(title: "Lang") with { Language = "python" }, Model, null).Artifact.Language);
        var renamed = store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Content = "<p>x</p>", Title = "  Orbit   pricing  " }, Model, null);
        Assert.Equal("Orbit pricing", renamed.Artifact.Title);
    }

    [Fact]
    public void Ids_are_made_from_titles_kept_unique_and_an_asked_for_id_never_overwrites()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        Assert.Equal("pricing-calculator", store.Create(ThreadA, Page(title: "Pricing Calculator!"), Model, null).Artifact.Id);
        Assert.Equal("pricing-calculator-2", store.Create(ThreadA, Page(title: "Pricing calculator"), Model, null).Artifact.Id);
        Assert.Equal("pricing-calculator", store.Create(ThreadB, Page(), Model, null).Artifact.Id); // ids are per thread
        Assert.Equal("my-thing", store.Create(ThreadA, Page(id: "My_Thing"), Model, null).Artifact.Id);
        Assert.Contains("already has an artifact \"my-thing\"", Message(() => store.Create(ThreadA, Page(id: "my thing"), Model, null)), StringComparison.Ordinal);
        Assert.Contains("cannot be an artifact id", Message(() => store.Create(ThreadA, Page(id: "!!!"), Model, null)), StringComparison.Ordinal);
        Assert.Equal("artifact", store.Create(ThreadA, Page(title: "\u00BF?"), Model, null).Artifact.Id);
        Assert.Equal("Untitled", store.Create(ThreadA, Page(title: "   "), Model, null).Artifact.Title);

        Assert.Equal("pricing-calculator", store.Read(ThreadA, "Pricing-Calculator").Artifact.Id);
        Assert.Equal("a-b-c", ArtifactStore.NormalizeId("--A..b__C--"));
        Assert.Null(ArtifactStore.NormalizeId(" - "));
        Assert.Equal(ArtifactStore.MaxIdLength, ArtifactStore.NormalizeId(new string('x', 80))!.Length);
        Assert.True(store.NewId(ThreadA, new string('y', 80)).Length <= ArtifactStore.MaxIdLength);
        Assert.Equal(ArtifactStore.MaxTitleLength, ArtifactStore.CleanTitle(new string('t', 300)).Length);
        var prompt = store.Create(ThreadA, Page(title: "Long ask"), Model, new string('p', 2000)).Version.Prompt!;
        Assert.Equal(ArtifactStore.MaxPromptLength, prompt.Length);
        Assert.EndsWith("\u2026", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void List_and_the_gallery_are_newest_first_and_skip_what_cannot_be_read()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, Page(title: "Pricing calculator"), Model, null);
        store.Create(ThreadA, new ArtifactDraft { Kind = ArtifactKind.Mermaid, Title = "Release flow", Content = "graph TD; A-->B" }, Model, null);
        store.Create(ThreadB, new ArtifactDraft { Kind = ArtifactKind.Markdown, Title = "Release notes", Content = "# Notes" }, Model, null);
        store.Update(ThreadA, "pricing-calculator", new ArtifactRevision { Content = "<p>2</p>" }, Model, null);

        Assert.Equal(new[] { "pricing-calculator", "release-flow" }, store.List(ThreadA).Select(a => a.Id));
        Assert.Equal(new[] { ThreadA, ThreadB }, store.Threads());
        Assert.Equal(new[] { "pricing-calculator", "release-notes", "release-flow" }, store.Gallery([ThreadA, ThreadB]).Select(a => a.Id));
        Assert.Equal(new[] { "release-notes", "release-flow" }, store.Gallery([ThreadA, ThreadB], "release").Select(a => a.Id));
        Assert.Equal(new[] { "release-flow" }, store.Gallery([ThreadA, ThreadB], "DIAGRAM").Select(a => a.Id));
        Assert.Equal(new[] { "release-notes" }, store.Gallery([ThreadA, ThreadB], "notes release").Select(a => a.Id));
        Assert.Equal(new[] { "release-notes" }, store.Gallery([ThreadA, ThreadB], kind: ArtifactKind.Markdown).Select(a => a.Id));

        File.WriteAllText(Path.Combine(store.Folder(ThreadA, "release-flow"), "meta.json"), "{ not json");
        Assert.Equal(new[] { "pricing-calculator" }, store.List(ThreadA).Select(a => a.Id));
        Assert.False(store.Exists(ThreadA, "release-flow"));
        // A folder a damaged or newer meta.json holds is never taken over: a new artifact of that name gets the next id.
        Assert.Equal("release-flow-2", store.Create(ThreadA, Page(title: "Release flow"), Model, null).Artifact.Id);
        Assert.Contains("already has an artifact", Message(() => store.Create(ThreadA, Page(id: "release-flow"), Model, null)), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(store.Folder(ThreadA, "release-flow-2"), "meta.json"),
            File.ReadAllText(Path.Combine(store.Folder(ThreadA, "release-flow-2"), "meta.json")).Replace("\"format\": 1", "\"format\": 2", StringComparison.Ordinal));
        Assert.Null(store.Find(ThreadA, "release-flow-2"));
    }

    [Fact]
    public void A_folder_a_crashed_create_left_without_meta_json_is_reused()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        var leftover = store.Folder(ThreadA, "pricing-calculator");
        Directory.CreateDirectory(Path.Combine(leftover, "v1"));
        File.WriteAllText(Path.Combine(leftover, "v1.html"), "stale");
        var made = store.Create(ThreadA, Page(), Model, null);
        Assert.Equal("pricing-calculator", made.Artifact.Id);
        Assert.Equal(new[] { "meta.json", "v1.html" }, Directory.GetFileSystemEntries(leftover).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(made.Text, store.Read(ThreadA, "pricing-calculator").Text);
    }

    [Fact]
    public void Storage_used_remove_and_clear_report_and_raise_their_changes()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        Assert.Equal(0, store.StorageUsed(ThreadA));
        store.Create(ThreadA, Page(content: "12345"), Model, null);
        store.Create(ThreadA, Page(title: "Second", content: "123"), Model, null);
        var meta = Directory.GetFiles(store.ArtifactsFolder(ThreadA), "meta.json", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Assert.Equal(8 + meta, store.StorageUsed(ThreadA));
        var changes = new List<ArtifactChange>();
        store.Changed += changes.Add;

        store.Remove(ThreadA, "second");
        Assert.Equal(new[] { "pricing-calculator" }, store.List(ThreadA).Select(a => a.Id));
        store.Clear(ThreadA);
        Assert.Empty(store.List(ThreadA));
        Assert.False(Directory.Exists(store.ArtifactsFolder(ThreadA)));
        Assert.Equal(new[] { ("second", ArtifactChangeKind.Removed), ("pricing-calculator", ArtifactChangeKind.Removed) }, changes.Select(c => (c.Id, c.Change)));
        Assert.Contains("no artifact \"second\"", Message(() => store.Remove(ThreadA, "second")), StringComparison.Ordinal);
    }

    [Fact]
    public void Meta_json_round_trips_every_field_through_a_fresh_store()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        store.Create(ThreadA, new ArtifactDraft { Kind = ArtifactKind.Code, Title = "Fetch \"data\"", Language = "go", Content = "package main\n" }, Model, "Write it in Go.");
        store.Update(ThreadA, "fetch-data", new ArtifactRevision { Content = "package main // 2\n" }, ArtifactAuthor.You, null);
        store.Restore(ThreadA, "fetch-data", 1, ArtifactAuthor.You);
        store.Fork(ThreadA, "fetch-data", 2, ThreadB, new ArtifactAuthor("codex", null, null));
        store.Create(ThreadA, Site(("index.html", "x"), ("a/b.css", "y")), new ArtifactAuthor("deepseek", "deepseek-chat", null), null);

        var fresh = new ArtifactStore(() => scratch.Threads);
        foreach (var (thread, id) in new[] { (ThreadA, "fetch-data"), (ThreadB, "fetch-data"), (ThreadA, "orbit-site") })
        {
            var before = store.Find(thread, id)!;
            var after = fresh.Find(thread, id)!;
            Assert.Equal((before.Thread, before.Id, before.Title, before.Kind, before.Language, before.Created), (after.Thread, after.Id, after.Title, after.Kind, after.Language, after.Created));
            Assert.Equal(before.Versions.Count, after.Versions.Count);
            for (var i = 0; i < before.Versions.Count; i++)
            {
                var (b, a) = (before.Versions[i], after.Versions[i]);
                Assert.Equal((b.Number, b.Timestamp, b.Author, b.Prompt, b.File, b.Entry, b.Bytes, b.RestoredFrom, b.ForkedFrom), (a.Number, a.Timestamp, a.Author, a.Prompt, a.File, a.Entry, a.Bytes, a.RestoredFrom, a.ForkedFrom));
                Assert.Equal(b.Files, a.Files);
            }
        }
        Assert.Equal(3, fresh.Find(ThreadA, "fetch-data")!.Versions[2].Number);
        Assert.Equal(1, fresh.Find(ThreadA, "fetch-data")!.Versions[2].RestoredFrom);
        Assert.Equal("Write it in Go.", fresh.Read(ThreadA, "fetch-data", 1).Version.Prompt);
        Assert.Equal("y", fresh.Read(ThreadA, "orbit-site").Files!["a/b.css"]);
    }

    [Fact]
    public void Export_paths_names_and_menus_follow_the_title_kind_and_version()
    {
        using var scratch = new ArtifactScratchFolder();
        var store = scratch.Store();
        var page = store.Create(ThreadA, Page(), Model, null).Artifact;
        store.Update(ThreadA, page.Id, new ArtifactRevision { Content = "<p>2</p>" }, Model, null);
        page = store.Find(ThreadA, page.Id)!;
        var site = store.Create(ThreadA, Site(("index.html", "x")), Model, null).Artifact;
        var code = store.Create(ThreadA, new ArtifactDraft { Kind = ArtifactKind.Code, Title = "Fetch data", Language = "python", Content = "x" }, Model, null).Artifact;

        Assert.Equal("artifacts/pricing-calculator.html", ArtifactExport.SaveToProjectPath(page)); // canvas: Saved to my-app/artifacts/pricing-calculator.html
        Assert.Equal("artifacts/orbit-site", ArtifactExport.SaveToProjectPath(site));
        Assert.Equal("artifacts/fetch-data.py", ArtifactExport.SaveToProjectPath(code));
        Assert.Equal("pricing-calculator.pdf", ArtifactExport.FileName(page, ArtifactExportFormat.Pdf));
        Assert.Equal("pricing-calculator-v1.html", ArtifactExport.FileName(page, ArtifactExportFormat.Html, 1));
        Assert.Equal("pricing-calculator.html", ArtifactExport.FileName(page, ArtifactExportFormat.Html, 2));
        Assert.Equal("orbit-site.zip", ArtifactExport.FileName(site, ArtifactExportFormat.Zip));
        Assert.Equal("fetch-data.py", ArtifactExport.FileName(code, ArtifactExportFormat.Source));

        Assert.Equal("\u00FCn\u00EFcode-title-2026", ArtifactExport.Slug("  \u00DCn\u00EFcode Title \u2014 2026! "));
        Assert.Equal("con-artifact", ArtifactExport.Slug("CON"));
        Assert.Equal("artifact", ArtifactExport.Slug("?!"));
        Assert.Equal(60, ArtifactExport.Slug(new string('s', 90)).Length);

        Assert.Equal(new[] { ArtifactExportFormat.Zip, ArtifactExportFormat.Html, ArtifactExportFormat.Pdf, ArtifactExportFormat.Png }, ArtifactExport.Formats(ArtifactKind.Project));
        Assert.Equal(new[] { ArtifactExportFormat.Markdown, ArtifactExportFormat.Docx, ArtifactExportFormat.Pdf, ArtifactExportFormat.Html }, ArtifactExport.Formats(ArtifactKind.Markdown));
        Assert.Equal(new[] { ArtifactExportFormat.Source }, ArtifactExport.Formats(ArtifactKind.Code));
        Assert.All(Enum.GetValues<ArtifactKind>(), kind => Assert.NotEmpty(ArtifactExport.Formats(kind)));
    }

    [Fact]
    public void Kinds_have_wire_names_aliases_extensions_and_the_canvas_chip_words()
    {
        Assert.Equal(ArtifactKinds.WireNames, Enum.GetValues<ArtifactKind>().Select(k => k.WireName()));
        Assert.All(Enum.GetValues<ArtifactKind>(), kind => Assert.Equal(kind, ArtifactKinds.Parse(kind.WireName().ToUpperInvariant())));
        Assert.Equal(new ArtifactKind?[] { ArtifactKind.Markdown, ArtifactKind.React, ArtifactKind.NthUi, ArtifactKind.Mermaid, null },
            new[] { "md", "tsx", "nth_ui", "mmd", "pdf" }.Select(ArtifactKinds.Parse));
        Assert.Equal("HTML app", ArtifactKinds.DisplayName(ArtifactKind.Html)); // canvas section 4's kind chip
        Assert.Equal("Python code", ArtifactKinds.DisplayName(ArtifactKind.Code, "python"));
        Assert.Equal("Code", ArtifactKinds.DisplayName(ArtifactKind.Code, "???"));
        Assert.Equal(("jsx", "tsx", "rs", "txt", "lua"), (ArtifactKinds.Extension(ArtifactKind.React, null), ArtifactKinds.Extension(ArtifactKind.React, "tsx"),
            ArtifactKinds.CodeExtension("Rust"), ArtifactKinds.CodeExtension("../evil"), ArtifactKinds.CodeExtension("lua")));
    }
}
