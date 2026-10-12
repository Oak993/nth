using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Services.Web;
using Marrow.UI.Markdown;
using Marrow.UI.NthUi;

namespace Marrow.App.Tests.Views.Artifacts;

// PORT-ONLY: web surfaces W14, the Tabula pane (work/design/web-surfaces/DESIGN.md 6.1 "Preview, Code, Diff", 6.3 the pane's
// header, version strip, view bar and well, 6.4 the streaming render, 3.2 header overflow; the approved canvas section 4).
// Measured on a real headless window over the store, the streams and the artifacts runtime with the fake web engine: what
// the pane shows for each state, the header's words and actions in the canvas order, the strip's versions and captions,
// each kind in its view, Code's file list, the in-place edit, and Diff.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class TabulaPaneTests(HeadlessSession headless)
{
    private const string Page = "<!doctype html><html><body><h3>Orbit</h3><p>$10 per seat</p></body></html>";

    private static IReadOnlyList<string> ActionNames(TabulaRig rig) => rig.View.Content.Actions.Select(a => a.Name).ToList();

    [Fact]
    public void The_pane_registers_its_kind_and_a_thread_without_artifacts_has_no_content()
    {
        TabulaPane.Register();
        Assert.True(PaneContents.IsAvailable(PaneKind.Artifacts));
        Assert.False(ArtifactHost.Shared.Current.IsOn);
    }

    [Fact]
    public void With_nothing_to_show_the_pane_says_how_artifacts_arrive_and_offers_the_gallery_once_there_is_one() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        Assert.True(rig.View.IsEmpty);
        Assert.Equal("Tabula", rig.View.Content.Title);
        Assert.Empty(rig.View.Content.Actions);
        Assert.False(rig.View.Strip.IsVisible);
        Assert.NotNull(rig.View.Content.TitleMenu);

        rig.Create(ArtifactKind.Html, Page);
        rig.Layout();
        Assert.True(rig.View.IsEmpty);
        rig.View.Content.TitleMenu!();
        rig.Layout();
        Assert.Equal(["Pricing calculator", "All artifacts"], rig.MenuTitles);
        rig.Choose("All artifacts");
        Assert.True(rig.State.IsGalleryOpen);
        Assert.Equal("All artifacts", rig.View.Content.Title);
        Assert.Single(rig.View.Gallery.Shown);
    });

    [Fact]
    public void An_artifact_heads_the_pane_with_its_title_kind_and_the_canvas_actions_in_order() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, Page);
        Assert.False(rig.View.IsEmpty);
        Assert.Equal("Pricing calculator", rig.View.Content.Title);
        Assert.Equal("HTML app", rig.View.Content.Subtitle);
        Assert.Equal(["Version 1, choose a version", "Ask to change", "Copy", "Save to project", "Open in Visus", "Export"], ActionNames(rig));
        var chip = rig.View.Content.Actions[0];
        Assert.Equal(("v1", true), (chip.Text, chip.Menu));
        Assert.Equal(("Ask to change", Marrow.UI.Panes.PaneGlyphKind.Spark), (rig.View.Content.Actions[1].Text, rig.View.Content.Actions[1].Glyph!.Value));
        Assert.True(rig.View.Content.Actions[5].Menu);
        Assert.Equal(TabulaTokens.SandboxText, rig.View.SandboxText.Text);
        Assert.Equal(TabulaViewMode.Preview, rig.View.Modes.Chosen);
        Assert.False(rig.View.Modes.Buttons[2].IsEnabled); // Diff needs two versions
        Assert.Null(rig.View.CompareText);
        Assert.False(rig.View.Layouts.IsVisible);
    });

    [Fact]
    public void Open_in_Visus_expands_the_pane_until_Visus_binds_its_tab() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Html, Page);
        rig.Invoke("Open in Visus");
        Assert.Equal(1, rig.Expanded);
        Assert.Equal("Opened full size", rig.View.StatusText);
    });

    [Fact]
    public void A_page_kind_opens_its_sandboxed_page_while_Preview_shows_and_hides_it_under_Code_without_closing_it() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var snapshot = rig.Show(ArtifactKind.Html, Page);
        var preview = rig.View.Preview;
        Assert.Equal(TabulaPreviewMode.Web, preview.Mode);
        var view = Assert.IsType<WebSurfaceView>(preview.View);
        Assert.Equal(ArtifactRuntime.Address(TabulaRig.Chat, snapshot.Artifact.Id), view.Url);
        Assert.Equal(new ArtifactAddress(TabulaRig.Chat, snapshot.Artifact.Id), preview.Mount!.Artifact);
        Assert.True(view.IsShown);
        Assert.True(preview.Host!.IsVisible);

        TabulaRig.Click(rig.View.Modes.Buttons[1]);
        rig.Layout();
        Assert.Equal(TabulaViewMode.Code, rig.State.View);
        Assert.False(view.IsShown);
        Assert.Same(view, preview.View);
        Assert.True(rig.View.Code.IsVisible);
        Assert.Equal(Page, rig.View.Code.Text);

        TabulaRig.Click(rig.View.Modes.Buttons[0]);
        rig.Layout();
        Assert.True(view.IsShown);
        Assert.Same(view, preview.View);
    });

    [Fact]
    public void Markdown_and_live_views_draw_natively_and_code_shows_highlighted_in_the_preview() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        rig.Show(ArtifactKind.Markdown, "# Pricing\nThree plans.", "Pricing notes");
        Assert.Equal(TabulaPreviewMode.Markdown, rig.View.Preview.Mode);
        Assert.IsType<MarkdownView>(rig.View.Preview.Native);
        Assert.Null(rig.View.Preview.View);
        Assert.Equal("Document", rig.View.Content.Subtitle);

        rig.Show(ArtifactKind.NthUi, "<card title=\"Plans\"><text>Three plans</text></card>", "Plans");
        Assert.Equal(TabulaPreviewMode.NthUi, rig.View.Preview.Mode);
        Assert.IsType<NthUiView>(rig.View.Preview.Native);

        var code = rig.Create(ArtifactKind.Code, "def price(seats):\n    return seats * 10\n", "Price", language: "python");
        rig.State.Select(new TabulaRef(TabulaRig.Chat, code.Artifact.Id));
        rig.Layout();
        Assert.Equal(TabulaPreviewMode.Code, rig.View.Preview.Mode);
        Assert.False(rig.View.Preview.IsFallback);
        Assert.Equal(code.Text, rig.View.Preview.Code.Text);
        Assert.Contains(rig.View.Preview.Code.CodeBlock.Inlines!, inline => inline is Avalonia.Controls.Documents.Run { Text: "def" });
    });

    [Fact]
    public void Without_the_web_runtime_a_page_kind_shows_its_code_instead() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(web: false);
        rig.Show(ArtifactKind.Html, Page);
        Assert.Equal(TabulaPreviewMode.Code, rig.View.Preview.Mode);
        Assert.True(rig.View.Preview.IsFallback);
        Assert.Equal(Page, rig.View.Preview.Code.Text);
        Assert.Null(rig.View.Preview.View);
    });

    [Fact]
    public void The_strip_lists_every_version_with_the_prompt_that_made_it_and_choosing_one_shows_it() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.Update(first.Artifact.Id, Page.Replace("$10", "$12", StringComparison.Ordinal), "Raise the price\nand more");
        rig.Store.Update(TabulaRig.Chat, first.Artifact.Id, new ArtifactRevision { Content = Page }, ArtifactAuthor.You, null);
        rig.Layout();
        Assert.Equal([("v1", "Make a pricing calculator"), ("v2", "Raise the price"), ("v3 · latest", "Edited by you")], rig.View.Strip.Labels);
        Assert.Equal(3, rig.View.ShownVersion);

        rig.View.Strip.Choose(1);
        rig.Layout();
        Assert.Equal(1, rig.State.Version);
        Assert.Equal(1, rig.View.ShownVersion);
        Assert.Equal("v1", rig.View.Content.Actions[0].Text);
        Assert.Equal(1, rig.View.Preview.Mount!.Version);

        rig.View.Strip.Choose(3);
        rig.Layout();
        Assert.Null(rig.State.Version); // the latest is followed again
        Assert.Null(rig.View.Preview.Mount!.Version);
        Assert.Equal("Restored v1", TabulaVersionStrip.Caption(new ArtifactVersion { Number = 4, Timestamp = default, Author = ArtifactAuthor.You, RestoredFrom = 1 }));
    });

    [Fact]
    public void Arrow_keys_move_the_mark_at_once_and_show_the_version_once_they_rest() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.Update(first.Artifact.Id, Page + " ");
        rig.Update(first.Artifact.Id, Page + "  ");
        rig.Layout();
        var strip = rig.View.Strip;
        strip.Focus();
        void Key(Avalonia.Input.Key key) => strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Key(Avalonia.Input.Key.Left);
        Key(Avalonia.Input.Key.Left);
        Assert.Equal(1, strip.Marked);
        Assert.Null(rig.State.Version);
        rig.Run(TabulaVersionStrip.KeyRest.TotalMilliseconds + 50);
        Assert.Equal(1, rig.State.Version);
        Key(Avalonia.Input.Key.End);
        rig.Run(TabulaVersionStrip.KeyRest.TotalMilliseconds + 50);
        Assert.Null(rig.State.Version);
        Assert.Equal(3, strip.Marked);
    });

    [Fact]
    public void Diff_compares_the_shown_version_with_the_one_before_inline_or_side_by_side_and_Shift_chooses_another() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.Update(first.Artifact.Id, Page.Replace("$10", "$12", StringComparison.Ordinal));
        rig.Update(first.Artifact.Id, Page.Replace("Orbit", "Orbit Pro", StringComparison.Ordinal));
        rig.Layout();
        Assert.True(rig.View.Modes.Buttons[2].IsEnabled);
        TabulaRig.Click(rig.View.Modes.Buttons[2]);
        rig.Layout();
        Assert.True(rig.View.Diff.IsVisible);
        Assert.False(rig.View.Preview.View!.IsShown);
        Assert.Equal("Comparing v2 → v3", rig.View.CompareText);
        var file = Assert.Single(rig.View.Diff.DiffFiles);
        Assert.Equal("pricing-calculator.html", file.Path);
        Assert.Equal(2, rig.View.Strip.Compared);

        rig.View.Strip.Choose(1, shift: true);
        rig.Layout();
        Assert.Equal(1, rig.State.Compare);
        Assert.Equal("Comparing v1 → v3", rig.View.CompareText);

        TabulaRig.Click(rig.View.Layouts.Buttons[1]);
        rig.Layout();
        Assert.Equal(TabulaDiffLayout.SideBySide, rig.View.Diff.Layout);
        Assert.Equal(TabulaDiffLayout.SideBySide, rig.State.DiffLayout);

        // Outside Diff, Shift+click is a plain click.
        TabulaRig.Click(rig.View.Modes.Buttons[0]);
        rig.Layout();
        rig.View.Strip.Choose(2, shift: true);
        rig.Layout();
        Assert.Equal(2, rig.State.Version);
    });

    [Fact]
    public void A_project_lists_its_files_beside_the_code_when_wide_as_chips_when_narrow_and_diffs_file_by_file() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig(width: 700);
        var project = rig.CreateProject("Orbit site", ("index.html", "<h1>Orbit</h1>\n"), ("style.css", "h1{}\n"), ("app.js", "1\n"));
        rig.State.Select(new TabulaRef(TabulaRig.Chat, project.Artifact.Id));
        rig.State.SetView(TabulaViewMode.Code);
        rig.Layout();
        var code = rig.View.Code;
        Assert.Equal(["index.html", "app.js", "style.css"], code.Files);
        Assert.Equal("index.html", code.File);
        Assert.True(code.ShowsFileList);
        Assert.Equal(3, code.FileButtons.Count);
        TabulaRig.Click(code.FileButtons[2]);
        rig.Layout();
        Assert.Equal("style.css", rig.State.CodeFile);
        Assert.Equal("h1{}\n", code.Text);

        rig.Window.Width = 420;
        rig.Layout();
        Assert.False(code.ShowsFileList);
        Assert.Equal(3, code.FileButtons.Count);

        rig.Store.Update(TabulaRig.Chat, project.Artifact.Id, new ArtifactRevision { Files = new Dictionary<string, string?> { ["style.css"] = "h1{color:red}\n", ["app.js"] = null } }, TabulaRig.Model, "Red title");
        rig.State.SetView(TabulaViewMode.Diff);
        rig.Layout();
        Assert.Equal([("app.js", Marrow.Services.Git.DiffFile.Change.Deleted), ("style.css", Marrow.Services.Git.DiffFile.Change.Modified)],
            rig.View.Diff.DiffFiles.Select(f => (f.Path, f.FileChange)));
    });

    [Fact]
    public void Edit_changes_the_code_in_place_and_Save_writes_a_version_by_you() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.State.SetView(TabulaViewMode.Code);
        rig.Layout();
        Assert.True(rig.View.EditButton.IsVisible);
        Assert.False(rig.View.SaveButton.IsVisible);
        TabulaRig.Click(rig.View.EditButton);
        rig.Layout();
        Assert.True(rig.View.Code.IsEditing);
        Assert.True(rig.View.SaveButton.IsVisible);
        Assert.Equal(Page, rig.View.Code.Editor.Text);
        rig.View.Code.Editor.Text = Page.Replace("$10", "$9", StringComparison.Ordinal);
        TabulaRig.Click(rig.View.SaveButton);
        rig.Layout();
        var artifact = rig.Store.Find(TabulaRig.Chat, first.Artifact.Id)!;
        Assert.Equal(2, artifact.Current);
        Assert.True(artifact.Version(2)!.Author.IsYou);
        Assert.Contains("$9", rig.Store.Read(TabulaRig.Chat, first.Artifact.Id).Text, StringComparison.Ordinal);
        Assert.False(rig.View.Code.IsEditing);
        Assert.Equal("Saved as v2", rig.View.StatusText);
        Assert.Equal(("v2 · latest", "Edited by you"), rig.View.Strip.Labels[^1]);
    });

    [Fact]
    public void The_version_menu_shows_each_version_restores_an_old_one_as_the_latest_and_remixes() => headless.OnUiThread(() =>
    {
        using var rig = new TabulaRig();
        var first = rig.Show(ArtifactKind.Html, Page);
        rig.Update(first.Artifact.Id, Page + "<!-- 2 -->");
        rig.Layout();
        rig.Invoke("Version 2, choose a version");
        rig.Layout();
        Assert.Equal(["v2 · latest", "v1", "Compare with another version", "Remix v2"], rig.MenuTitles);
        rig.Choose("v1");
        Assert.Equal(1, rig.State.Version);
        rig.Invoke("Version 1, choose a version");
        rig.Layout();
        Assert.Contains("Restore v1 as the latest", rig.MenuTitles);
        rig.Choose("Restore v1 as the latest");
        var artifact = rig.Store.Find(TabulaRig.Chat, first.Artifact.Id)!;
        Assert.Equal((3, 1), (artifact.Current, artifact.Version(3)!.RestoredFrom!.Value));
        Assert.Equal("Restored v1 as v3", rig.View.StatusText);
        Assert.Null(rig.State.Version);

        rig.Invoke("Version 3, choose a version");
        rig.Layout();
        rig.Choose("Remix v3");
        Assert.Equal(2, rig.Store.List(TabulaRig.Chat).Count);
        Assert.Equal("Pricing calculator remix", rig.View.Content.Title);
    });

    [Fact]
    public void Bind_selects_and_opens_a_new_artifact_opens_on_a_create_stream_and_forgets_a_removed_one()
    {
        var data = Marrow.Testing.TestDataDirectory.Read() ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        var threads = Path.Combine(data, "TabulaBind", Guid.NewGuid().ToString("N"), "threads");
        Directory.CreateDirectory(threads);
        var store = new ArtifactStore(() => threads);
        var host = new ArtifactHost(store, null);
        var state = new TabulaState(TabulaRig.Chat);
        var opened = 0;
        using var binding = TabulaPane.Bind(host, id => id == TabulaRig.Chat ? new TabulaTarget(state, () => opened++) : null, work => work());

        host.Streams.Begin(TabulaRig.Chat, "tool:call1", null, null, null);
        Assert.Equal(1, opened);
        state.OpenGallery(true);
        host.Streams.Begin(TabulaRig.Chat, "fence:m1:0", "pricing", "Pricing", ArtifactKind.Html);
        Assert.False(state.IsGalleryOpen);
        Assert.Equal(2, opened);
        host.Streams.Begin(TabulaRig.Chat, "fence:m1:1", "pricing", null, null, isUpdate: true);
        host.Streams.Begin(TabulaRig.Chat, TabulaView.AskStreamPrefix + "pricing", "pricing", null, null, isUpdate: true);
        Assert.Equal(2, opened);

        store.Create(TabulaRig.Chat, new ArtifactDraft { Kind = ArtifactKind.Html, Title = "Pricing", Id = "pricing", Content = "<p>1</p>" }, TabulaRig.Model, null);
        Assert.Equal(new TabulaRef(TabulaRig.Chat, "pricing"), state.Selected);
        Assert.Equal(3, opened);
        Assert.Empty(host.Streams.Of(TabulaRig.Chat)); // the call and the blocks that wrote it are done

        // An update never opens the pane; a chip's request does.
        store.Update(TabulaRig.Chat, "pricing", new ArtifactRevision { Content = "<p>2</p>" }, TabulaRig.Model, null);
        Assert.Equal(3, opened);
        state.Select(null);
        host.RequestOpen(TabulaRig.Chat, "pricing");
        Assert.Equal((4, "pricing"), (opened, state.Selected!.Id));

        store.Remove(TabulaRig.Chat, "pricing");
        Assert.Null(state.Selected);

        // Another thread's events reach only its own pane.
        store.Create(TabulaRig.Neighbour, new ArtifactDraft { Kind = ArtifactKind.Html, Title = "Other", Content = "<p>x</p>" }, TabulaRig.Model, null);
        Assert.Null(state.Selected);
        Assert.Equal(4, opened);
    }

    [Fact]
    public void The_turns_the_pane_posts_name_the_artifact_its_version_the_change_and_the_part_chosen()
    {
        var artifact = new Artifact
        {
            Thread = TabulaRig.Chat,
            Id = "pricing",
            Title = "Pricing calculator",
            Kind = ArtifactKind.Html,
            Created = DateTimeOffset.UnixEpoch,
            Versions =
            [
                new ArtifactVersion { Number = 1, Timestamp = DateTimeOffset.UnixEpoch, Author = TabulaRig.Model },
                new ArtifactVersion { Number = 2, Timestamp = DateTimeOffset.UnixEpoch, Author = TabulaRig.Model },
            ],
        };
        var ask = TabulaPane.AskMessage(artifact, 2, "  Make the toggle green ", "<button>\nYearly</button>");
        Assert.Equal(
            "Change the Tabula artifact \"Pricing calculator\" (id pricing, v2, HTML app): Make the toggle green\n\nChange this part:\n> <button>\n> Yearly</button>\n\n"
            + "Read it with artifact_read if you need to, then write the change with artifact_update on the same id.", ask);
        Assert.Contains("Start from v1 (artifact_read with version 1)", TabulaPane.AskMessage(artifact, 1, "x", null), StringComparison.Ordinal);
        var fix = TabulaPane.FixMessage(artifact, 2, new ArtifactRuntimeError("x is not defined", "at f (index.html:3)"));
        Assert.Equal("The Tabula artifact \"Pricing calculator\" (id pricing, v2) hit an error: x is not defined\n\n```\nat f (index.html:3)\n```\n\nFix it with artifact_update on the same id.", fix);
    }
}
