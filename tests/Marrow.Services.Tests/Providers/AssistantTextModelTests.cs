using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

[Trait("Decision", "ED-NEW-dextra-open-1")]
public sealed class AssistantTextModelTests
{
    [Theory]
    [InlineData("claude-chosen")]
    [InlineData("a model with spaces")]
    public async Task The_delegate_model_is_one_argument_and_keeps_the_no_tools_route(string model)
    {
        var run = TextGenerationHarness.PrepareStub("stdout", TextGenerationHarness.Utf8("reply"));
        Assert.Equal("reply", await TextGeneration.Complete("question", TextGenerationHarness.ClaudeEngine(run) with { Model = model }));
        var args = TextGenerationHarness.Argv(run);
        Assert.Equal(model, args[Array.IndexOf(args, "--model") + 1]);
        Assert.Equal("", args[Array.IndexOf(args, "--tools") + 1]);
    }

    [Fact]
    public void Codex_usage_counts_cached_input_once_and_repeated_terminal_summaries_once()
    {
        const string summary = "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":120,\"cached_input_tokens\":80,\"output_tokens\":30}}";
        var usage = TextGeneration.CodexSpend("malformed\n" + summary + "\n" + summary, "chosen")!;
        Assert.Equal(150, usage.TotalTokens);
        Assert.Equal(80, usage.CachedInputTokens);
        Assert.Equal("chosen", usage.Model);
    }
}
