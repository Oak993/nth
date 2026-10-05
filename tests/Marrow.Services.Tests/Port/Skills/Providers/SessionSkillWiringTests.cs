using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11e / DL-11 and DL-12. Against the existing session classes: with nothing wired, argv and environment are today's.
public sealed class SessionSkillWiringTests
{
    private static SessionConfiguration Config(ProviderKind provider, RuntimeMode mode = RuntimeMode.Supervised, Dictionary<string, string>? env = null) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("file:///" + Path.GetTempPath().Replace('\\', '/'))!,
        Environment = new ValueMap<string, string>(env ?? new Dictionary<string, string> { ["PATH"] = "p", ["K"] = "v" }),
        RuntimeMode = mode,
        InteractionMode = InteractionMode.Build,
    };

    private static ProviderSkillLaunch Launch(string provider, string data) => new(Path.Combine(data, "skills", "views", provider, "chat-1"), data);

    private static T Call<T>(object target, string name, params object?[] args) =>
        (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args)!;

    private static void Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    // ---- Pi ----

    [Theory, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    [InlineData(RuntimeMode.Supervised, null, null, null, new[] { "--mode", "rpc", "-e", "EXT", "--no-approve" })]
    [InlineData(RuntimeMode.FullAccess, "openai/gpt-5", "high", "/s/1.jsonl", new[] { "--mode", "rpc", "-e", "EXT", "--approve", "--provider", "openai", "--model", "gpt-5", "--thinking", "high", "--session", "/s/1.jsonl" })]
    public void Pi_live_argv_without_a_launch_is_the_existing_vector_and_the_same_list(RuntimeMode mode, string? model, string? effort, string? session, string[] expected)
    {
        var args = PiSession.LiveArguments("EXT", mode, model, effort, session);
        Assert.Equal(expected, args);
        var pi = new PiSession(Config(ProviderKind.Pi));
        Assert.Same(args, pi.WithSkills(args));
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Pi_live_argv_with_a_launch_adds_only_skill_at_the_end()
    {
        var data = SkillTapFixtures.TempFolder("pi");
        var pi = new PiSession(Config(ProviderKind.Pi)) { SkillLaunch = Launch("pi", data) };
        var args = PiSession.LiveArguments("EXT", RuntimeMode.Supervised, null, null, null);
        Assert.Equal([.. args, "--skill", Path.Combine(data, "skills", "views", "pi", "chat-1")], pi.WithSkills(args));
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Pi_events_reach_the_sink_through_the_session_handlers()
    {
        var events = new List<ToolEvent>();
        var pi = new PiSession(Config(ProviderKind.Pi)) { ToolEvents = events.Add };
        foreach (var line in SkillTapFixtures.Lines("pi.jsonl")) pi.Handle(line);
        Assert.Contains(new CommandHeld("soffice --headless --convert-to pdf a.docx"), events);
        Assert.Contains(new CommandFinished("soffice --headless --convert-to pdf a.docx", 9009, true, "'soffice' is not recognized as an internal or external command"), events);
        Assert.Equal(3, events.Count);
    }

    // ---- Command Code ----

    private static List<string> CommandCodeToday(string? resume = null) => (List<string>)typeof(CommandCodeSession)
        .GetMethod("BuildArguments", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [false, null, null, InteractionMode.Build, RuntimeMode.Supervised, null, resume])!;

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task CommandCode_argv_is_todays_unless_launch_and_probe_agree()
    {
        var data = SkillTapFixtures.TempFolder("cc");
        var exe = Path.Combine(data, "cmd.exe");
        File.WriteAllText(exe, "x");
        var today = CommandCodeToday("r1");
        var plain = new CommandCodeSession(Config(ProviderKind.Commandcode));
        Assert.Same(today, plain.WithSkills(today, exe));

        var probe = new CommandCodeSkillFlagProbe((_, _) => Task.FromResult<string?>("  --skill <path>"));
        var launchOnly = new CommandCodeSession(Config(ProviderKind.Commandcode)) { SkillLaunch = Launch("commandcode", data) };
        var probeOnly = new CommandCodeSession(Config(ProviderKind.Commandcode)) { SkillFlagProbe = probe };
        var both = new CommandCodeSession(Config(ProviderKind.Commandcode)) { SkillLaunch = Launch("commandcode", data), SkillFlagProbe = probe };
        Assert.Same(today, launchOnly.WithSkills(today, exe));
        Assert.Same(today, probeOnly.WithSkills(today, exe));
        Assert.Same(today, both.WithSkills(today, exe)); // the probe has not run yet: unknown means no bundle
        await probe.Probe(exe);
        Assert.Equal([.. today, "--skill", Path.Combine(data, "skills", "views", "commandcode", "chat-1")], both.WithSkills(today, exe));
        Assert.Same(today, probeOnly.WithSkills(today, exe));
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task CommandCode_with_a_help_that_lacks_the_flag_stays_byte_identical()
    {
        var data = SkillTapFixtures.TempFolder("cc-old");
        var exe = Path.Combine(data, "cmd.exe");
        File.WriteAllText(exe, "x");
        var probe = new CommandCodeSkillFlagProbe((_, _) => Task.FromResult<string?>("--resume <id>\n--no-skills"));
        await probe.Probe(exe);
        var today = CommandCodeToday();
        var session = new CommandCodeSession(Config(ProviderKind.Commandcode)) { SkillLaunch = Launch("commandcode", data), SkillFlagProbe = probe };
        Assert.Same(today, session.WithSkills(today, exe));
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void CommandCode_events_reach_the_sink_through_the_session_handler()
    {
        var events = new List<ToolEvent>();
        var session = new CommandCodeSession(Config(ProviderKind.Commandcode)) { ToolEvents = events.Add };
        foreach (var line in SkillTapFixtures.Lines("commandcode.jsonl")) Invoke(session, "HandleEvent", line["event"]);
        Assert.Equal<ToolEvent>([
            new SkillUsed("pdf"),
            new CommandFinished("pdftoppm -png a.pdf out", null, true, "pdftoppm : The term 'pdftoppm' is not recognized as the name of a cmdlet"),
            new CommandFinished("node --version", null, false, "v22.1.0"),
        ], events);
    }

    // ---- ACP (Grok) ----

    public static IEnumerable<object[]> AcpCases() =>
        from provider in new[] { ProviderKind.Cursor, ProviderKind.Grok, ProviderKind.Devin, ProviderKind.Opencode, ProviderKind.OpencodeGo }
        from mode in Enum.GetValues<RuntimeMode>()
        select new object[] { provider, mode };

    [Theory, MemberData(nameof(AcpCases)), Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Acp_argv_and_environment_are_todays_without_a_launch_and_only_Grok_changes_with_one(ProviderKind provider, RuntimeMode mode)
    {
        var data = SkillTapFixtures.TempFolder("acp");
        var config = Config(provider, mode);
        var today = (ValueList<string>)typeof(ACPSession).GetProperty("LaunchArguments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(new ACPSession(config))!;
        var plain = new ACPSession(config);
        Assert.Equal(today, plain.EffectiveLaunchArguments);
        Assert.Same(config.Environment, plain.EffectiveEnvironment);

        var wired = new ACPSession(config) { SkillLaunch = Launch("grok", data) };
        if (provider != ProviderKind.Grok)
        {
            Assert.Equal(today, wired.EffectiveLaunchArguments);
            Assert.Same(config.Environment, wired.EffectiveEnvironment);
            return;
        }
        var folder = Path.Combine(data, "skills", "views", "grok", "chat-1");
        var at = wired.EffectiveLaunchArguments.ToList().IndexOf("--plugin-dir");
        Assert.Equal(folder, wired.EffectiveLaunchArguments[at + 1]);
        Assert.Equal(today.ToList(), wired.EffectiveLaunchArguments.Where((_, i) => i < at || i >= at + 2).ToList());
        Assert.DoesNotContain(wired.EffectiveLaunchArguments, a => a.Contains("claude", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("v", wired.EffectiveEnvironment["K"]);
        Assert.Contains(Path.Combine(data, "addons", "bin"), wired.EffectiveEnvironment["PATH"], StringComparison.Ordinal);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Acp_events_reach_the_sink_through_the_session_handlers()
    {
        var events = new List<ToolEvent>();
        var session = new ACPSession(Config(ProviderKind.Grok)) { ToolEvents = events.Add };
        foreach (var line in SkillTapFixtures.Lines("grok.jsonl"))
        {
            var method = line["method"]!.JSONValueString!;
            if (method == "session/update") Invoke(session, "HandleNotification", method, line["params"]);
        }
        Assert.Equal<ToolEvent>([
            new CommandFinished("pandoc a.md -o a.docx", 127, true, "pandoc: command not found"),
            new CommandFinished("git status", 0, false, "clean"),
        ], events);
    }
}
