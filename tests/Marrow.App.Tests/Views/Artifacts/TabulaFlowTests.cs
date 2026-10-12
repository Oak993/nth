using System.Text;
using Avalonia.Input;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Artifacts;

// PORT-ONLY: web surfaces W14, what the Tabula pane does (DESIGN.md 6.3 "Ask to change", "Copy", "Save to project",
// "Export (html, zip, pdf, png)", 6.4 the streaming skeleton and render, 6.1 the gallery "artifacts across threads", 6.5 the
// consent cards, 10 Reduce Motion; the approved canvas section 4's toasts and composer). Each flow runs through the pane on a
// real headless window: the turn it posts, the version it waits for, what it copies and writes, the stream it draws, the
// gallery's scopes, the bridge's cards, and the motion with and without Reduce Motion.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class TabulaFlowTests(HeadlessSession headless)
{
    private const string Page = "<!doctype html><html><body><h3>Orbit</h3><p>$10 per seat</p></body></html>";

    private static void Press(Avalonia.Interactivity.Interactive target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    private static FakeWebView Attach(TabulaRig rig)
    {
        var view = rig.View.Preview.View!;
        if (view.Engine is not FakeWebView { IsAttached: true }) view.AttachNative(1, 2);
        var fake = Assert.IsType<FakeWebView>(view.Engine);
        Assert.True(fake.IsAttached);
        return fake;
    }

    [Fact]
    public void Ask_to_change_posts_the_change_shows_the_version_being_written_and_says_when_it_lands() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        var fake = Attach(rig);
        int Loads() => fake.Log.Count(line => line.StartsWith("Navigate ", StringComparison.Ordinal));
        var loads = Loads();
        rig.Invoke("Ask to change");
        Assert.True(rig.State.IsAskOpen);
        Assert.True(rig.View.Ask.IsOpen);
        Assert.Equal(TabulaAsk.Placeholder, rig.View.Ask.Field.PlaceholderText);
        rig.View.Ask.Field.Text = "Make the toggle green";
        rig.View.Ask.Field.CaretIndex = rig.View.Ask.Field.Text.Length;
        Press(rig.View.Ask.Field, Key.Enter, KeyModifiers.Shift);
        Assert.Equal("Make the toggle green\n", rig.View.Ask.Field.Text);
        Assert.Empty(rig.Sent);
        rig.View.Ask.Field.Text = "Make the toggle green";
        Press(rig.View.Ask.Field, Key.Enter);
        rig.Layout();

        var artifact = rig.Store.Find(TabulaRig.Chat, first.Artifact.Id)!;
        Assert.Equal(TabulaPane.AskMessage(artifact, 1, "Make the toggle green", null), Assert.Single(rig.Sent));
        Assert.False(rig.State.IsAskOpen);
        Assert.Equal(("pricing-calculator", 1, "Make the toggle green"), (rig.State.PendingAsk!.Id, rig.State.PendingAsk.FromVersion, rig.State.PendingAsk.Prompt));
        Assert.Equal(("v2", "Make the toggle green"), rig.View.Strip.Labels[^1]);
        Assert.Equal(("v1", "Make a pricing calculator"), rig.View.Strip.Labels[0]); // "latest" waits for the new one
        Assert.True(rig.View.Strip.IsPulsing, "the pending version pulses");
        Assert.Contains(rig.Streams.Of(TabulaRig.Chat), s => s.Key == TabulaView.AskStreamPrefix + first.Artifact.Id);

        rig.Update(first.Artifact.Id, Page.Replace("Orbit", "Orbit green", StringComparison.Ordinal), "Make the toggle green");
        rig.Layout();
        Assert.Null(rig.State.PendingAsk);
        Assert.Empty(rig.Streams.Of(TabulaRig.Chat));
        Assert.Equal("v2 written by sonnet · “Make the toggle green”", rig.View.StatusText);
        Assert.Equal(("v2 · latest", "Make the toggle green"), rig.View.Strip.Labels[^1]);
        Assert.False(rig.View.Strip.IsPulsing);
        Assert.True(Loads() > loads, "the page follows the latest"); // the page followed the latest
    });

    [Fact]
    public void A_turn_that_ends_without_a_new_version_takes_the_pending_version_away() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.Invoke("Ask to change");
        rig.View.Ask.Field.Text = "Make it blue";
        TabulaRig.Click(rig.View.Ask.SendButton);
        rig.Layout();
        Assert.NotNull(rig.State.PendingAsk);
        rig.Streams.EndAll(TabulaRig.Chat);
        rig.Layout();
        Assert.Null(rig.State.PendingAsk);
        Assert.Equal(["v1 · latest"], rig.View.Strip.Labels.Select(l => l.Label));
        Assert.Equal(1, rig.Store.Find(TabulaRig.Chat, first.Artifact.Id)!.Current);
    });

    [Fact]
    public void The_text_chosen_in_Code_becomes_the_part_to_change_and_Escape_closes_the_composer() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.State.SetView(TabulaViewMode.Code);
        rig.Layout();
        var block = rig.View.Code.CodeBlock;
        var at = Page.IndexOf("<p>", StringComparison.Ordinal);
        block.SelectionStart = at;
        block.SelectionEnd = at + "<p>$10 per seat</p>".Length;
        rig.Invoke("Ask to change");
        Assert.Equal("<p>$10 per seat</p>", rig.State.AskSelection);
        Assert.Equal("“<p>$10 per seat</p>”", rig.View.Ask.Part);
        rig.View.Ask.Field.Text = "Twelve dollars";
        Press(rig.View.Ask.Field, Key.Enter);
        rig.Layout();
        Assert.Contains("Change this part:\n> <p>$10 per seat</p>", Assert.Single(rig.Sent), StringComparison.Ordinal);
        Assert.Equal(TabulaViewMode.Preview, rig.State.View);
        Assert.Equal(first.Artifact.Id, rig.State.PendingAsk!.Id);

        rig.Invoke("Ask to change");
        Assert.True(rig.View.Ask.IsOpen);
        Assert.Null(rig.View.Ask.Part);
        Press(rig.View.Ask.Field, Key.Escape);
        rig.Layout();
        Assert.False(rig.State.IsAskOpen);
        Assert.False(rig.View.Ask.IsOpen);
    });

    [Fact]
    public void Copy_puts_the_content_on_the_clipboard_and_says_what_and_how_big() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, Page);
        rig.Invoke("Copy");
        Assert.Equal(Page, Assert.Single(rig.Clipboard.Texts));
        Assert.Equal("Copied v1 · HTML · " + TabulaTokens.Size(Encoding.UTF8.GetByteCount(Page)), rig.View.StatusText);

        var project = rig.CreateProject("Orbit site", ("index.html", "<h1>Orbit</h1>"), ("style.css", "h1{}"));
        rig.State.Select(new TabulaRef(TabulaRig.Chat, project.Artifact.Id));
        rig.Layout();
        rig.Invoke("Copy");
        Assert.Equal("<h1>Orbit</h1>", rig.Clipboard.Texts[^1]);
        rig.State.SetView(TabulaViewMode.Code);
        rig.State.SetCodeFile("style.css");
        rig.Layout();
        rig.Invoke("Copy");
        Assert.Equal("h1{}", rig.Clipboard.Texts[^1]);
        Assert.StartsWith("Copied v1 · style.css · ", rig.View.StatusText, StringComparison.Ordinal);
    });

    [Fact]
    public void Save_to_project_starts_in_the_projects_artifacts_folder_writes_the_file_and_a_project_as_its_folder() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var folder = Path.Combine(rig.Root, "my-app");
        Directory.CreateDirectory(Path.Combine(folder, "artifacts"));
        rig.ProjectFolder = folder;
        rig.Files.SaveAnswer = request => Path.Combine(request.Folder!, request.FileName);
        rig.Show(ArtifactKind.Html, Page);
        rig.Invoke("Save to project");
        rig.Layout();
        var request = Assert.Single(rig.Files.Saves);
        Assert.Equal(new TabulaSaveRequest("Save to project", Path.Combine(folder, "artifacts"), "pricing-calculator.html", "HTML file", "html"), request);
        Assert.Equal(Page, File.ReadAllText(Path.Combine(folder, "artifacts", "pricing-calculator.html")));
        Assert.Equal("Saved to my-app/artifacts/pricing-calculator.html · it shows in Changes", rig.View.StatusText);

        var project = rig.CreateProject("Orbit site", ("index.html", "<h1>Orbit</h1>"), ("css/style.css", "h1{}"));
        rig.State.Select(new TabulaRef(TabulaRig.Chat, project.Artifact.Id));
        rig.Layout();
        rig.Invoke("Save to project");
        rig.Layout();
        Assert.True(rig.Files.Saves[^1].IsFolder);
        Assert.Equal("orbit-site", rig.Files.Saves[^1].FileName);
        Assert.Equal("h1{}", File.ReadAllText(Path.Combine(folder, "artifacts", "orbit-site", "css", "style.css")));
        Assert.Equal("Saved to my-app/artifacts/orbit-site · it shows in Changes", rig.View.StatusText);

        // A dialog closed without a choice writes nothing and says nothing.
        rig.Files.SaveAnswer = _ => null;
        var said = rig.View.StatusText;
        rig.Invoke("Save to project");
        rig.Layout();
        Assert.Equal(said, rig.View.StatusText);
        Assert.Equal("Saved to C:\\elsewhere\\a.html", TabulaView.SavedWords(folder, "C:\\elsewhere\\a.html"));
    });

    [Fact]
    public void Export_lists_the_kinds_formats_writes_a_standalone_page_a_zip_and_prints_the_shown_page_to_PDF() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var out1 = Path.Combine(rig.Root, "out");
        Directory.CreateDirectory(out1);
        rig.Files.SaveAnswer = request => Path.Combine(out1, request.FileName);
        rig.Show(ArtifactKind.Html, Page);
        var fake = Attach(rig);
        rig.Invoke("Export");
        rig.Layout();
        Assert.Equal(["Copy as image", "Standalone HTML", "PDF", "PNG image"], rig.MenuTitles);
        rig.Choose("Standalone HTML");
        rig.Layout();
        Assert.Equal(("Export Pricing calculator", "pricing-calculator.html", "html"), (rig.Files.Saves[^1].Title, rig.Files.Saves[^1].FileName, rig.Files.Saves[^1].Extension));
        Assert.Contains("<h3>Orbit</h3>", File.ReadAllText(Path.Combine(out1, "pricing-calculator.html")), StringComparison.Ordinal);
        Assert.Equal("Exported pricing-calculator.html", rig.View.StatusText);

        rig.Invoke("Export");
        rig.Layout();
        rig.Choose("PDF");
        rig.Layout();
        Assert.Contains("PrintToPdf " + Path.Combine(out1, "pricing-calculator.pdf"), fake.Log);
        Assert.Equal("Exported pricing-calculator.pdf", rig.View.StatusText);

        var project = rig.CreateProject("Orbit site", ("index.html", "<h1>Orbit</h1>"), ("style.css", "h1{}"));
        rig.State.Select(new TabulaRef(TabulaRig.Chat, project.Artifact.Id));
        rig.Layout();
        rig.Invoke("Export");
        rig.Layout();
        Assert.Equal("Folder as .zip", rig.MenuTitles[1]);
        rig.Choose("Folder as .zip");
        rig.Layout();
        using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(out1, "orbit-site.zip"));
        Assert.Contains(zip.Entries, entry => entry.FullName.EndsWith("style.css", StringComparison.Ordinal));
    });

    [Fact]
    public void A_native_view_copies_and_exports_as_its_own_picture() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var out1 = Path.Combine(rig.Root, "out");
        Directory.CreateDirectory(out1);
        rig.Files.SaveAnswer = request => Path.Combine(out1, request.FileName);
        rig.Show(ArtifactKind.NthUi, "Three plans", "Plans");
        rig.Invoke("Export");
        rig.Layout();
        Assert.Equal(["Copy as image", "PNG image", "Source file"], rig.MenuTitles);
        rig.Choose("Copy as image");
        rig.Layout();
        var png = Assert.Single(rig.Clipboard.Images);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4));
        Assert.Equal("Copied v1 as an image", rig.View.StatusText);
    });

    [Fact]
    public void A_create_stream_opens_on_the_skeleton_and_a_document_draws_as_its_text_arrives() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Streams.Begin(TabulaRig.Chat, "tool:call1", null, null, null);
        rig.Layout();
        Assert.True(rig.View.IsSkeletonShown);
        Assert.False(rig.View.IsEmpty);
        Assert.Equal(("New artifact", "writing"), (rig.View.Content.Title, rig.View.Content.Subtitle));
        Assert.False(rig.View.Strip.IsVisible);
        Assert.False(rig.View.Modes.Buttons[2].IsEnabled);
        Assert.True(rig.View.Skeleton.IsSheenMoving);

        rig.Streams.EndAll(TabulaRig.Chat);
        rig.Streams.Begin(TabulaRig.Chat, "fence:m1:0", "notes", "Pricing notes", ArtifactKind.Markdown);
        rig.Layout();
        Assert.Equal(("Pricing notes", "Document · writing"), (rig.View.Content.Title, rig.View.Content.Subtitle));
        Assert.True(rig.View.IsSkeletonShown);
        rig.Streams.Update(TabulaRig.Chat, "fence:m1:0", () => "# Pricing\nThree", force: true);
        rig.Layout();
        Assert.Equal(TabulaPreviewMode.Markdown, rig.View.Preview.Mode);
        rig.Run(700);
        Assert.False(rig.View.IsSkeletonShown);

        rig.State.SetView(TabulaViewMode.Code);
        rig.Layout();
        rig.Streams.Update(TabulaRig.Chat, "fence:m1:0", () => "# Pricing\nThree plans", force: true);
        rig.Layout();
        Assert.True(rig.View.Code.IsVisible);
        Assert.Equal("# Pricing\nThree plans", rig.View.Code.Text);

        // The block closed and saved: the pane shows the artifact itself.
        var saved = rig.Create(ArtifactKind.Markdown, "# Pricing\nThree plans", "Pricing notes", "notes");
        rig.State.Select(new TabulaRef(TabulaRig.Chat, saved.Artifact.Id));
        rig.Streams.End(TabulaRig.Chat, "fence:m1:0");
        rig.Layout();
        Assert.Null(rig.View.Creating);
        Assert.True(rig.View.Strip.IsVisible);
        Assert.Equal(("Pricing notes", "Document"), (rig.View.Content.Title, rig.View.Content.Subtitle));
    });

    [Fact]
    public void A_page_being_written_keeps_its_skeleton_until_it_exists_and_a_new_version_morphs_the_shown_page() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Streams.Begin(TabulaRig.Chat, "fence:m1:0", "pricing-calculator", "Pricing calculator", ArtifactKind.Html);
        rig.Streams.Update(TabulaRig.Chat, "fence:m1:0", () => "<!doctype html><html><body><h3>Or", force: true);
        rig.Layout();
        Assert.True(rig.View.IsSkeletonShown);
        Assert.Equal(TabulaPreviewMode.None, rig.View.Preview.Mode);
        rig.Streams.End(TabulaRig.Chat, "fence:m1:0");

        var first = rig.Show(ArtifactKind.Html, Page);
        var fake = Attach(rig);
        rig.Streams.Begin(TabulaRig.Chat, "fence:m2:0", first.Artifact.Id, null, null, isUpdate: true);
        rig.Streams.Update(TabulaRig.Chat, "fence:m2:0", () => "<!doctype html><html><body><h3>Orbit Pro</h3>", force: true);
        rig.Layout();
        Assert.Contains(fake.Log, line => line.StartsWith("Post ", StringComparison.Ordinal) && line.Contains("\"event\":\"morph\"", StringComparison.Ordinal) && line.Contains("Orbit Pro", StringComparison.Ordinal));
        Assert.False(rig.View.EditButton.IsVisible && rig.State.View == TabulaViewMode.Code);
    });

    [Fact]
    public void A_page_error_shows_a_calm_notice_and_Ask_to_fix_posts_the_error_and_its_stack() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        var fake = Attach(rig);
        var mount = rig.View.Preview.Mount!;
        fake.RaiseMessage(mount.Origin, "{\"nth\":1,\"s\":\"doc1\",\"type\":\"error\",\"message\":\"total is not defined\",\"stack\":\"at price (index.html:12)\"}");
        rig.Layout();
        Assert.Equal("This artifact hit an error", rig.View.Notice.Title);
        Assert.Equal("total is not defined", rig.View.Notice.Detail);
        var fix = Assert.Single(rig.View.Notice.Actions);
        TabulaRig.Click(fix);
        rig.Layout();
        var artifact = rig.Store.Find(TabulaRig.Chat, first.Artifact.Id)!;
        Assert.Equal(TabulaPane.FixMessage(artifact, 1, new ArtifactRuntimeError("total is not defined", "at price (index.html:12)")), Assert.Single(rig.Sent));
        Assert.Null(rig.View.Notice.Title);
        Assert.Equal("Asked the chat to fix it", rig.View.StatusText);

        // A message from another origin is not the page's.
        fake.RaiseMessage("https://evil.test", "{\"nth\":1,\"type\":\"error\",\"message\":\"spoof\"}");
        rig.Layout();
        Assert.Null(rig.View.Notice.Title);
    });

    [Fact]
    public void The_gallery_lists_this_projects_artifacts_or_every_threads_searches_and_opens_another_threads_read_only() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, Page);
        rig.Create(ArtifactKind.Svg, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "Landing hero", chat: TabulaRig.Neighbour);
        rig.Create(ArtifactKind.Markdown, "# Notes", "Release notes", chat: TabulaRig.Elsewhere);
        rig.State.OpenGallery(true);
        rig.Layout();
        var gallery = rig.View.Gallery;
        Assert.True(gallery.IsVisible);
        Assert.False(rig.View.Preview.View!.IsShown);
        Assert.Equal(["Landing hero", "Pricing calculator"], gallery.Shown.Select(a => a.Title));
        TabulaRig.Click(gallery.Scope.Buttons[1]);
        rig.Layout();
        Assert.Equal(TabulaGalleryScope.AllThreads, rig.State.GalleryScope);
        Assert.Equal(["Release notes", "Landing hero", "Pricing calculator"], gallery.Shown.Select(a => a.Title));
        gallery.Search.Text = "landing";
        rig.Layout();
        Assert.Equal(["Landing hero"], gallery.Shown.Select(a => a.Title));
        gallery.Search.Text = "nothing like it";
        rig.Layout();
        Assert.Equal("No artifact matches “nothing like it”.", gallery.EmptyText);
        gallery.Search.Text = "landing";
        rig.Layout();

        TabulaRig.Click(Assert.Single(gallery.Cards));
        rig.Layout();
        Assert.False(rig.State.IsGalleryOpen);
        Assert.True(rig.State.IsReadOnly);
        Assert.Equal("SVG image · read-only", rig.View.Content.Subtitle);
        Assert.Equal("Copy into this thread", rig.View.Content.Actions[1].Name);
        Assert.Equal("From “Landing page”", rig.View.Notice.Title);
        Assert.False(rig.View.Ask.IsOpen);

        rig.Invoke("Copy into this thread");
        Assert.False(rig.State.IsReadOnly);
        Assert.Equal(TabulaRig.Chat, rig.State.Selected!.Thread);
        Assert.Contains(rig.Store.List(TabulaRig.Chat), a => a.Title == "Landing hero");
        Assert.Equal("Copied into this thread as “Landing hero”", rig.View.StatusText);
        Assert.Equal("Ask to change", rig.View.Content.Actions[1].Name);
    });

    [Fact]
    public void The_pane_keeps_its_choice_in_the_threads_folder_and_a_broken_file_is_an_empty_pane() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.Update(first.Artifact.Id, Page + " ");
        rig.State.ShowVersion(1);
        rig.State.SetView(TabulaViewMode.Code);
        rig.State.SetDiffLayout(TabulaDiffLayout.SideBySide);
        var again = TabulaState.Load(TabulaRig.Chat, rig.StateFile);
        Assert.Equal((new TabulaRef(TabulaRig.Chat, first.Artifact.Id), (int?)1, TabulaViewMode.Code, TabulaDiffLayout.SideBySide),
            (again.Selected, again.Version, again.View, again.DiffLayout));
        Assert.False(again.IsAskOpen);
        File.WriteAllText(rig.StateFile, "{ not json");
        var broken = TabulaState.Load(TabulaRig.Chat, rig.StateFile);
        Assert.Null(broken.Selected);
    });

    [Fact]
    public void The_bridge_asks_on_the_pane_showing_the_artifact_and_a_no_is_said_once_in_the_status() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        var address = new ArtifactAddress(TabulaRig.Chat, first.Artifact.Id);
        var asked = TabulaBridgeHost.Instance.ConfirmModelAsync(address, "Claude Sonnet", CancellationToken.None);
        rig.Layout();
        Assert.Equal(TabulaBridgeHost.ModelTitle("Claude Sonnet"), rig.View.Cards.Showing);
        Assert.NotNull(rig.View.Cards.Popup);
        rig.View.Cards.Answer(1);
        rig.Layout();
        Assert.True(asked.IsCompletedSuccessfully && asked.Result);

        var tool = TabulaBridgeHost.Instance.ConfirmToolAsync(address, "github", "search_issues", CancellationToken.None);
        rig.Layout();
        Assert.Equal(TabulaBridgeHost.ToolTitle("github", "search_issues"), rig.View.Cards.Showing);
        rig.View.Cards.Answer(0);
        rig.Layout();
        Assert.True(tool.IsCompletedSuccessfully);
        Assert.False(tool.Result);
        Assert.Equal(TabulaBridgeHost.Declined, rig.View.StatusText);

        // No pane shows this artifact: the answer is no, at once.
        var nobody = TabulaBridgeHost.Instance.ConfirmModelAsync(new ArtifactAddress(TabulaRig.Neighbour, "other"), "Claude Sonnet", CancellationToken.None);
        rig.Layout();
        Assert.True(nobody.IsCompletedSuccessfully);
        Assert.False(nobody.Result);

        TabulaBridgeHost.Instance.Draft(address, "Plan: Team, 12 seats");
        rig.Layout();
        Assert.Equal(["Plan: Team, 12 seats"], rig.Drafts);
        Assert.Equal("Added to your message. Send it when you are ready.", rig.View.StatusText);
    });

    [Fact]
    public void The_page_gets_nths_glass_tokens_for_each_appearance() => headless.OnUiThread(() =>
    {
        var light = TabulaBridgeHost.ThemeFor(ThemeVariant.Light);
        var dark = TabulaBridgeHost.ThemeFor(ThemeVariant.Dark);
        Assert.False(light.Dark);
        Assert.True(dark.Dark);
        foreach (var name in new[] { "--nth-bg", "--nth-text", "--nth-text-2", "--nth-text-3", "--nth-line", "--nth-fill", "--nth-accent", "--nth-primary", "--nth-error" })
        {
            Assert.StartsWith("rgba(", light.Variables[name], StringComparison.Ordinal);
            Assert.StartsWith("rgba(", dark.Variables[name], StringComparison.Ordinal);
        }
        Assert.NotEqual(light.Variables["--nth-text"], dark.Variables["--nth-text"]);
        Assert.Equal("14px", light.Variables["--nth-radius"]);
    });

    [Fact]
    public void The_composer_rises_on_a_spring_and_the_pending_version_pulses_and_Reduce_Motion_stills_both() => headless.OnUiThread(() =>
    {
        using (var rig = new TabulaRig())
        {
            rig.Show(ArtifactKind.Html, Page);
            rig.Invoke("Ask to change");
            Assert.True(rig.View.Ask.Motion.IsMoving);
            Assert.True(rig.View.Ask.Motion.Rise > 1);
            rig.Run(900);
            Assert.False(rig.View.Ask.Motion.IsMoving);
            Assert.True(Math.Abs(rig.View.Ask.Motion.Rise) < 0.1);
            Assert.True(rig.View.Ask.Motion.Opacity > 0.99);
        }

        using (var still = new TabulaRig(reduceMotion: true))
        {
            still.Show(ArtifactKind.Html, Page);
            still.Invoke("Ask to change");
            Assert.Equal(0, still.View.Ask.Motion.Rise);
            still.View.Ask.Field.Text = "Green";
            Press(still.View.Ask.Field, Key.Enter);
            still.Layout();
            Assert.NotNull(still.State.PendingAsk);
            Assert.False(still.View.Strip.IsPulsing);
        }
    });
}
