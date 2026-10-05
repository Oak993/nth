using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11d / WU-9 credentials: skills-PLAN.md 2.2 source 1 and review 7(2).
public sealed class OpenCodeAuthReaderTests
{
    private static readonly string[] Secrets = ["sk-SECRET-api", "SECRET-wk-key", "SECRET-wk-token", "SECRET-nested"];
    private const string Mixed = "{\"openai\":{\"type\":\"api\",\"key\":\"sk-SECRET-api\"},\"corp\":{\"type\":\"wellknown\",\"key\":\"SECRET-wk-key\",\"token\":\"SECRET-wk-token\",\"extra\":{\"deep\":[\"SECRET-nested\"]}}}";

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Only_an_api_entry_is_not_wellknown_and_a_missing_file_is_not_wellknown()
    {
        using var f = new OpenCodeFixture();
        Assert.False(OpenCodeAuthReader.WellKnownPresent(f.AuthFile));
        f.Write(f.AuthFile, "{\"openai\":{\"type\":\"api\",\"key\":\"sk-SECRET-api\"},\"o\":{\"type\":\"oauth\",\"refresh\":\"x\"}}");
        Assert.False(OpenCodeAuthReader.WellKnownPresent(f.AuthFile));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_wellknown_entry_gives_only_yes_and_no_secret_appears_anywhere_visible()
    {
        using var f = new OpenCodeFixture();
        f.Write(f.AuthFile, Mixed);
        var launch = f.Launch;
        var result = OpenCodeSkillPaths.Resolve(f.Env, launch, null);
        Assert.True(OpenCodeAuthReader.WellKnownPresent(f.AuthFile));
        var shown = result.LeftOutReason + result + string.Join("|", result.Paths ?? []);
        foreach (var secret in Secrets) Assert.DoesNotContain(secret, shown, StringComparison.Ordinal);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_corrupt_auth_file_fails_closed_with_an_exception_that_carries_no_content()
    {
        using var f = new OpenCodeFixture();
        f.Write(f.AuthFile, "{\"corp\":{\"type\":\"wellknown\",\"key\":\"SECRET-wk-key\"");
        var thrown = Assert.Throws<OpenCodeConfigUnreadableException>(() => OpenCodeAuthReader.WellKnownPresent(f.AuthFile));
        Assert.DoesNotContain("SECRET", thrown.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(f.AuthFile, thrown.Message, StringComparison.Ordinal);
        Assert.True(OpenCodeSkillPaths.Resolve(f.Env, f.Launch, null).LeftOut);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void The_reader_has_no_logging_or_retained_state()
    {
        var type = typeof(OpenCodeAuthReader);
        Assert.True(type.IsAbstract && type.IsSealed); // static
        var fields = type.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Where(x => !x.IsLiteral).ToArray();
        Assert.Empty(fields);
    }
}
