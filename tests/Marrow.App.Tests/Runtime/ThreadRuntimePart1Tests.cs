using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B2 1fbf81e), :116-135, :744-756, :907-929,
// :954-990, :1074-1112, :1158-1167, :1256, :1837-1856, :2004-2011.
public sealed class ThreadRuntimePart1Tests
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-032")] // App/Runtime/ThreadRuntime.swift:116-118
    public void Draft_is_empty_only_when_text_is_whitespace_and_there_are_no_files_or_command()
    {
        Assert.True(new ComposerDraft { Text = " \t\r\n\u2003" }.IsEmpty);
        Assert.False(new ComposerDraft { Text = " words " }.IsEmpty);
        Assert.False(new ComposerDraft { Attachments = new ValueList<Attachment>([Image("one.png")]) }.IsEmpty);
        Assert.False(new ComposerDraft { Command = Command("plan") }.IsEmpty);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-032")] // App/Runtime/ThreadRuntime.swift:120-135
    public void Outgoing_text_keeps_quote_blocks_empty_quote_lines_and_command_spacing()
    {
        var quoted = new ComposerDraft
        {
            Text = "question",
            Quotes = new ValueList<ReplyQuote>([
                new ReplyQuote { Text = "first\n\nthird" },
                new ReplyQuote { Text = "last" },
            ]),
        };
        Assert.Equal("> first\n>\n> third\n\n> last\n\nquestion", quoted.OutgoingText);

        Assert.Equal("/run body", new ComposerDraft
        {
            Text = "body",
            Command = Command("run"),
        }.OutgoingText);

        Assert.Equal("/run\n\n> quoted\n\nbody", new ComposerDraft
        {
            Text = "body",
            Quotes = new ValueList<ReplyQuote>([new ReplyQuote { Text = "quoted" }]),
            Command = Command("run"),
        }.OutgoingText);

        Assert.Equal("/run", new ComposerDraft
        {
            Text = " \t\n",
            Command = Command("run"),
        }.OutgoingText);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-032")] // App/Runtime/ThreadRuntime.swift:744-756
    [Trait("Req", "PR-COMPOSER-033")] // App/Runtime/ThreadRuntime.swift:1158-1163
    public void Sending_plan_clears_every_draft_field_toggles_mode_and_starts_no_turn()
    {
        using var h = new AppModelHarness();
        var (app, thread, runtime) = OpenRuntime(h, ProviderKind.Codex);

        h.Main.OnMain(() =>
        {
            runtime.Draft = new ComposerDraft
            {
                Text = " \t",
                Attachments = new ValueList<Attachment>([Image("unused.png")]),
                Command = Command("plan"),
            };
            runtime.Send();

            Assert.Equal(InteractionMode.Plan, app.Thread(thread.Id)!.InteractionMode);
            AssertFreshEmptyDraft(runtime.Draft);
            Assert.Empty(runtime.Turns);
            Assert.Contains("plan", app.Providers.RecentCommands.Values.SelectMany(commands => commands));

            runtime.Draft = new ComposerDraft { Text = "/plan" };
            runtime.Send();

            Assert.Equal(InteractionMode.Build, app.Thread(thread.Id)!.InteractionMode);
            AssertFreshEmptyDraft(runtime.Draft);
            Assert.Empty(runtime.Turns);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-034")] // App/Runtime/ThreadRuntime.swift:1164-1167
    public void Compact_is_handled_locally_for_Claude_and_the_seven_supported_providers_are_exact()
    {
        var compactProviders = SwiftEnum<ProviderKind>.AllCases.Where(provider => provider.SupportsCompact).ToArray();
        Assert.Equal(
            new[]
            {
                ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Deepseek, ProviderKind.Meta,
                ProviderKind.Zai, ProviderKind.Copilot, ProviderKind.Pi,
                ProviderKind.OpenRouter, // ED-234: not in Provider.swift:149-154; a native API provider added by OD-0049
                ProviderKind.Local, // ED-410: the shared session can compact
            },
            compactProviders);

        using var h = new AppModelHarness();
        var (app, _, runtime) = OpenRuntime(h, ProviderKind.Claude);
        h.PrepareBootstrap(app);

        h.Main.OnMain(() =>
        {
            runtime.Draft = new ComposerDraft { Text = "/compact" };
            runtime.Send();

            AssertFreshEmptyDraft(runtime.Draft);
            Assert.Empty(runtime.Turns);
            Assert.Contains("compact", app.Providers.RecentCommands.Values.SelectMany(commands => commands));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-169")] // App/Runtime/ThreadRuntime.swift:954-990
    public void Sending_a_queued_plan_command_removes_it_and_handles_it_without_a_turn()
    {
        using var h = new AppModelHarness();
        var (app, thread, runtime) = OpenRuntime(h, ProviderKind.Codex);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("/plan", []);
            var queued = Assert.Single(runtime.FollowUps);

            runtime.SendFollowUpNow(queued.Id);

            Assert.Empty(runtime.FollowUps);
            Assert.Empty(runtime.Turns);
            Assert.Equal(InteractionMode.Plan, app.Thread(thread.Id)!.InteractionMode);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-170")] // App/Runtime/ThreadRuntime.swift:907-929
    public void Bundled_text_names_each_message_and_numbers_images_across_the_bundle()
    {
        var first = new FollowUpPrompt
        {
            Text = "  inspect logo  ",
            Attachments = new ValueList<Attachment>([Image("one.png"), File("notes.txt", @"C:\uploads\notes.txt")]),
        };
        var second = new FollowUpPrompt
        {
            Text = "\n summarize \t",
            Attachments = new ValueList<Attachment>([Image("two.png")]),
        };

        Assert.Equal(
            "2 messages sent together. The pictures and files a message came with are named under it, the pictures numbered in the order they are attached.\n\n" +
            "Message 1:\ninspect logo\nAttached to message 1: image 1 (one.png), file C:\\uploads\\notes.txt.\n\n" +
            "Message 2:\nsummarize\nAttached to message 2: image 2 (two.png).",
            ThreadRuntime.BundleText([first, second]));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-170")] // App/Runtime/ThreadRuntime.swift:907-910
    public void Bundled_text_without_attachments_joins_only_trimmed_nonempty_text()
    {
        var members = new[]
        {
            new FollowUpPrompt { Text = "  first  " },
            new FollowUpPrompt { Text = " \t\n" },
            new FollowUpPrompt { Text = " second\r\n" },
        };

        Assert.Equal("first\n\nsecond", ThreadRuntime.BundleText(members));
        Assert.Equal("first", ThreadRuntime.BundleText([members[0]]));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-163")] // App/Runtime/ThreadRuntime.swift:830-846 bundleFollowUp(_:onto:)
    public void Bundling_gives_both_prompts_one_bundle_and_moves_the_dragged_one_right_after_the_bundle()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("a", []);
            runtime.EnqueueFollowUp("b", []);
            runtime.EnqueueFollowUp("c", []);
            runtime.EnqueueFollowUp("d", []);
            var (a, b, c, d) = (runtime.FollowUps[0].Id, runtime.FollowUps[1].Id, runtime.FollowUps[2].Id, runtime.FollowUps[3].Id);

            runtime.BundleFollowUp(c, a); // :837-843 c takes a's new bundle and sits right after it
            Assert.Equal(new[] { a, c, b, d }, runtime.FollowUps.Select(p => p.Id));
            var bundle = runtime.FollowUps[0].BundleID;
            Assert.NotNull(bundle);
            Assert.Equal(bundle, runtime.FollowUps[1].BundleID);
            Assert.Null(runtime.FollowUps[2].BundleID);

            runtime.BundleFollowUp(d, a); // :837 joins the existing bundle, after its last member (:842)
            Assert.Equal(new[] { a, c, d, b }, runtime.FollowUps.Select(p => p.Id));
            Assert.Equal(new Guid?[] { bundle, bundle, bundle, null }, runtime.FollowUps.Select(p => p.BundleID));

            runtime.BundleFollowUp(a, a); // :834 guard id != target
            runtime.BundleFollowUp(Guid.NewGuid(), a); // :835-836 both must exist
            Assert.Equal(new[] { a, c, d, b }, runtime.FollowUps.Select(p => p.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-163")] // App/Runtime/ThreadRuntime.swift:833-846: bundling does not tidy the bundle it left
    public void Bundling_a_prompt_away_from_its_pair_leaves_the_old_partner_tagged_as_the_source_does()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("a", []);
            runtime.EnqueueFollowUp("b", []);
            runtime.EnqueueFollowUp("c", []);
            var (a, b, c) = (runtime.FollowUps[0].Id, runtime.FollowUps[1].Id, runtime.FollowUps[2].Id);
            runtime.BundleFollowUp(b, a);
            var first = runtime.FollowUps[0].BundleID;

            runtime.BundleFollowUp(b, c); // b leaves {a, b} for a new bundle with c
            Assert.Equal(new[] { a, c, b }, runtime.FollowUps.Select(p => p.Id));
            // bundleFollowUp never calls tidyBundles(): a keeps its bundle until another operation tidies it.
            Assert.Equal(first, runtime.FollowUps[0].BundleID);
            Assert.NotNull(runtime.FollowUps[1].BundleID);
            Assert.Equal(runtime.FollowUps[1].BundleID, runtime.FollowUps[2].BundleID);
            Assert.NotEqual(first, runtime.FollowUps[2].BundleID);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-158")] // App/Runtime/ThreadRuntime.swift:849-856 unbundleFollowUp (the link glyph's tap)
    public void Unbundling_takes_a_prompt_out_and_a_bundle_left_with_one_prompt_dissolves()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("a", []);
            runtime.EnqueueFollowUp("b", []);
            runtime.EnqueueFollowUp("c", []);
            var (a, b, c) = (runtime.FollowUps[0].Id, runtime.FollowUps[1].Id, runtime.FollowUps[2].Id);
            runtime.BundleFollowUp(b, a);
            runtime.BundleFollowUp(c, a);
            var bundle = runtime.FollowUps[0].BundleID;

            runtime.UnbundleFollowUp(c); // :851 out on its own; a and b still share the bundle
            Assert.Equal(new Guid?[] { bundle, bundle, null }, runtime.FollowUps.Select(p => p.BundleID));

            runtime.UnbundleFollowUp(b); // :852 tidyBundles(): a, left alone in the bundle, is freed too
            Assert.All(runtime.FollowUps, p => Assert.Null(p.BundleID));
            Assert.Equal(new[] { a, b, c }, runtime.FollowUps.Select(p => p.Id)); // the order is unchanged

            runtime.UnbundleFollowUp(a); // :850 guard: not in a bundle, nothing happens
            Assert.All(runtime.FollowUps, p => Assert.Null(p.BundleID));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-171")] // App/Runtime/ThreadRuntime.swift:1074-1079
    public void Removing_a_bundled_prompt_dissolves_the_remaining_member_and_closes_its_edit()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("first", []);
            runtime.EnqueueFollowUp("second", []);
            var first = runtime.FollowUps[0];
            var second = runtime.FollowUps[1];
            runtime.BundleFollowUp(second.Id, first.Id);
            runtime.FollowUpEdit = new PromptEdit(second.Id, second.Text, second.Attachments);

            runtime.RemoveFollowUp(second.Id);

            var remaining = Assert.Single(runtime.FollowUps);
            Assert.Equal(first.Id, remaining.Id);
            Assert.Null(remaining.BundleID);
            Assert.Null(runtime.FollowUpEdit);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-172")] // App/Runtime/ThreadRuntime.swift:1085-1098
    public void Moving_a_bundle_member_away_dissolves_the_bundle_and_same_half_is_a_noop()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("first", []);
            runtime.EnqueueFollowUp("second", []);
            runtime.EnqueueFollowUp("third", []);
            var first = runtime.FollowUps[0];
            var second = runtime.FollowUps[1];
            var third = runtime.FollowUps[2];
            runtime.BundleFollowUp(second.Id, first.Id);

            Assert.False(runtime.MoveFollowUp(second.Id, third.Id, placeAfter: false));
            Assert.True(runtime.MoveFollowUp(second.Id, third.Id, placeAfter: true));

            Assert.Equal(new[] { first.Id, third.Id, second.Id }, runtime.FollowUps.Select(prompt => prompt.Id));
            Assert.All(runtime.FollowUps, prompt => Assert.Null(prompt.BundleID));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-175")] // App/Runtime/ThreadRuntime.swift:1100-1112
    public void Saving_an_empty_follow_up_edit_removes_it_and_dissolves_its_bundle()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        h.Main.OnMain(() =>
        {
            runtime.EnqueueFollowUp("keep", []);
            runtime.EnqueueFollowUp("remove", []);
            var keep = runtime.FollowUps[0];
            var remove = runtime.FollowUps[1];
            runtime.BundleFollowUp(remove.Id, keep.Id);

            runtime.UpdateFollowUp(remove.Id, " \t\r\n", []);

            var remaining = Assert.Single(runtime.FollowUps);
            Assert.Equal(keep.Id, remaining.Id);
            Assert.Null(remaining.BundleID);
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-057")] // App/Runtime/ThreadRuntime.swift:1256-2351
    public void Checkpoint_refs_use_the_lowercase_start_and_end_phase_literals()
    {
        var thread = Guid.Parse("A1B2C3D4-E5F6-47A8-9012-3456789ABCDE");

        Assert.Equal(
            "refs/droppy-code/checkpoints/a1b2c3d4-e5f6-47a8-9012-3456789abcde/0-start",
            Git.CheckpointRef(thread, 0, "start"));
        Assert.Equal(
            "refs/droppy-code/checkpoints/a1b2c3d4-e5f6-47a8-9012-3456789abcde/12-end",
            Git.CheckpointRef(thread, 12, "end"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-115")] // App/Runtime/ThreadRuntime.swift:1837-1845
    public async Task Reverting_a_turn_that_left_the_thread_throws_the_exact_message()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);
        var pending = h.Main.OnMain(() => runtime.Revert(Guid.NewGuid(), restoreFiles: false));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => pending);

        Assert.Equal("This message is no longer in the thread.", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-GIT-116")] // App/Runtime/ThreadRuntime.swift:1851-1856
    public async Task Restoring_files_without_a_base_checkpoint_fails_before_a_revert()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);
        var turnID = h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("message");
            var id = Assert.Single(runtime.Turns).Id;
            runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            return id;
        });
        h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        var pending = h.Main.OnMain(() => runtime.Revert(turnID, restoreFiles: true));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => pending);

        Assert.Equal("This turn has no file checkpoint. Choose to keep the file changes instead.", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-GIT-143")] // App/Runtime/ThreadRuntime.swift:2004-2011
    public void Turns_with_provider_diffs_or_touched_paths_appear_but_unchanged_turns_do_not()
    {
        using var h = new AppModelHarness();
        var (_, _, runtime) = OpenRuntime(h);

        var selected = h.Main.OnMain(() =>
        {
            runtime.RehearseTurn("unchanged");
            runtime.RehearseTurn("provider patch", providerDiff: "diff --git a/a b/a");
            runtime.RehearseTurn("reported path", touchedPaths: ["changed.txt"]);
            return runtime.TurnsWithChanges().Select(turn => turn.Index).ToArray();
        });

        Assert.Equal(new long[] { 1, 2 }, selected);
    }

    private static (AppModel App, ChatThread Thread, ThreadRuntime Runtime) OpenRuntime(
        AppModelHarness harness,
        ProviderKind provider = ProviderKind.Codex)
    {
        var project = AppModelSamples.Project("P", harness.NewFolder("project"));
        var thread = AppModelSamples.Thread(project) with { Provider = provider };
        harness.WriteLibrary([project], [thread]);
        var app = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => app.Runtime(thread.Id));
        return (app, thread, runtime);
    }

    private static Attachment Image(string name) => new()
    {
        Name = name,
        Path = @"C:\uploads\" + name,
        MimeType = "image/png",
    };

    private static Attachment File(string name, string path) => new()
    {
        Name = name,
        Path = path,
        MimeType = "text/plain",
    };

    private static DraftCommand Command(string name) => new()
    {
        Name = name,
        Detail = "",
        IsBuiltIn = true,
    };

    private static void AssertFreshEmptyDraft(ComposerDraft draft)
    {
        Assert.Equal("", draft.Text);
        Assert.Empty(draft.Attachments);
        Assert.Empty(draft.Quotes);
        Assert.Null(draft.Command);
        Assert.True(draft.IsEmpty);
    }
}
