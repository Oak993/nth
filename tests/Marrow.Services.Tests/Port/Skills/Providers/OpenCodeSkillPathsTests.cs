using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11d / WU-9, skills-PLAN.md 2.2 "OpenCode config merge": one fixture per config source.
public sealed class OpenCodeSkillPathsTests
{
    private static OpenCodeSkillPathsResult Resolve(OpenCodeFixture f, string? inline = null) => OpenCodeSkillPaths.Resolve(f.Env, f.Launch, inline);

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void No_source_sets_paths_gives_an_empty_user_list()
    {
        using var f = new OpenCodeFixture();
        Assert.Equal([], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Global_files_apply_in_order_and_the_last_that_sets_paths_wins_whole()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "config.json"), f.Paths("a"));
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("b", "c"));
        Assert.Equal(["b", "c"], Resolve(f).Paths);
        f.Write(Path.Combine(f.Config, "opencode.jsonc"), "// c\n{ /* x */ \"skills\": { \"paths\": [\"d\",], }, }");
        Assert.Equal(["d"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_later_file_that_does_not_set_paths_does_not_clear_them()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("keep"));
        f.Write(Path.Combine(f.Config, "opencode.jsonc"), "{\"model\":\"x\"}");
        Assert.Equal(["keep"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void OPENCODE_CONFIG_beats_global_and_a_relative_name_resolves_against_the_session_folder()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("global"));
        f.Write(Path.Combine(f.Session, "mine.json"), f.Paths("named"));
        f.Env["OPENCODE_CONFIG"] = "mine.json";
        Assert.Equal(["named"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Project_files_two_levels_below_the_worktree_the_inner_one_wins()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("global"));
        f.Write(Path.Combine(f.Project, "opencode.json"), f.Paths("outer"));
        Assert.Equal(["outer"], Resolve(f).Paths);
        f.Write(Path.Combine(f.Session, "opencode.json"), f.Paths("inner"));
        Assert.Equal(["inner"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void The_walk_stops_at_the_worktree_so_a_file_above_it_is_ignored()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Home, "opencode.json"), f.Paths("above"));
        Assert.Equal([], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void OPENCODE_DISABLE_PROJECT_CONFIG_skips_project_files()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("global"));
        f.Write(Path.Combine(f.Session, "opencode.json"), f.Paths("inner"));
        f.Env["OPENCODE_DISABLE_PROJECT_CONFIG"] = "1";
        Assert.Equal(["global"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Config_directories_come_after_project_files_dot_opencode_then_home_then_OPENCODE_CONFIG_DIR()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Session, "opencode.json"), f.Paths("project"));
        f.Write(Path.Combine(f.Session, ".opencode", "opencode.json"), f.Paths("dot"));
        Assert.Equal(["dot"], Resolve(f).Paths);
        f.Write(Path.Combine(f.Home, ".opencode", "opencode.jsonc"), f.Paths("home-dot"));
        Assert.Equal(["home-dot"], Resolve(f).Paths);
        var dir = Path.Combine(f.Root, "cfg dir");
        f.Write(Path.Combine(dir, "opencode.json"), f.Paths("json"));
        f.Write(Path.Combine(dir, "opencode.jsonc"), f.Paths("jsonc"));
        f.Env["OPENCODE_CONFIG_DIR"] = dir;
        Assert.Equal(["jsonc"], Resolve(f).Paths); // one directory: opencode.json then opencode.jsonc
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void An_inherited_OPENCODE_CONFIG_CONTENT_is_the_last_source()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Session, ".opencode", "opencode.json"), f.Paths("dot"));
        Assert.Equal(["inline"], Resolve(f, f.Paths("inline")).Paths);
        Assert.Equal(["dot"], Resolve(f, "{\"model\":\"m\"}").Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Entries_are_copied_character_for_character_without_resolving()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "opencode.json"), "{\"skills\":{\"paths\":[\"agent-skills\",\"~/team-skills\",\"~\\\\x\",\"C:\\\\a b\\\\c\"]}}");
        Assert.Equal(["agent-skills", "~/team-skills", "~\\x", "C:\\a b\\c"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void The_legacy_TOML_config_leaves_the_bundle_out()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "config"), "model = \"x\"");
        Assert.True(Resolve(f).LeftOut);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_wellknown_auth_entry_leaves_the_bundle_out()
    {
        using var f = new OpenCodeFixture();
        f.Write(f.AuthFile, "{\"a\":{\"type\":\"api\",\"key\":\"sk-SECRET-1\"},\"b\":{\"type\":\"wellknown\",\"key\":\"SECRET-2\",\"token\":\"SECRET-3\"}}");
        Assert.True(Resolve(f).LeftOut);
    }

    [Theory, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    [InlineData("{ not json")]
    [InlineData("[1]")]
    [InlineData("{\"skills\":{\"paths\":\"x\"}}")]
    [InlineData("{\"skills\":{\"paths\":[1]}}")]
    [InlineData("{\"skills\":{\"paths\":[\"{env:HOME}/s\"]}}")]
    [InlineData("{\"skills\":{\"paths\":[\"{file:p.txt}\"]}}")]
    public void An_unreadable_or_substituted_source_leaves_the_bundle_out(string content)
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Session, "opencode.json"), content);
        Assert.True(Resolve(f).LeftOut);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Two_sources_of_unknown_order_that_disagree_leave_the_bundle_out()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Session, "opencode.json"), f.Paths("a"));
        f.Write(Path.Combine(f.Session, "opencode.jsonc"), f.Paths("b"));
        Assert.True(Resolve(f).LeftOut);
        f.Write(Path.Combine(f.Session, "opencode.jsonc"), f.Paths("a"));
        Assert.Equal(["a"], Resolve(f).Paths);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void XDG_folders_move_the_config_and_auth_locations()
    {
        using var f = new OpenCodeFixture();
        var xdg = Path.Combine(f.Root, "xdg");
        f.Write(Path.Combine(xdg, "opencode", "opencode.json"), f.Paths("xdg"));
        f.Env["XDG_CONFIG_HOME"] = xdg;
        Assert.Equal(["xdg"], Resolve(f).Paths);
        f.Write(Path.Combine(f.Root, "data", "opencode", "auth.json"), "{\"x\":{\"type\":\"wellknown\"}}");
        f.Env["XDG_DATA_HOME"] = Path.Combine(f.Root, "data");
        Assert.True(Resolve(f).LeftOut);
    }
}
