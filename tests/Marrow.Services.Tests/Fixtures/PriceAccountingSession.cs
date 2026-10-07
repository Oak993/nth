using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Executes production admission/accounting with fake rounds, or the dispatch boundary under an in-process fake HTTP handler.
public sealed class PriceAccountingSession : OpenAICompatibleSession
{
    public List<Dictionary<string, JSONValue>> Requests { get; } = [];
    public bool ExerciseDispatchBoundary { get; set; }
    public StreamRound Next { get; set; } = new() { Content = "answer", TokenUsage = new() { Total = 100, Input = 60, Output = 40 } };
    public PriceAccountingSession(IApiKeyRotation rotation, ProviderKind provider = ProviderKind.Deepseek, string model = "fixture") : base(new Config
    {
        Kind = provider, Title = "Fake", DefaultModel = model,
        ChatURL = new("https://invalid.test/never-send"), ContextWindow = 1000, AgentIdentity = _ => "fake", IsVisionModel = _ => false,
    }, new SessionConfiguration
    {
        Provider = provider, Model = model, WorkingDirectory = SwiftURL.Create("marrow-native-path:/offline")!,
        Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
    }) => KeyRotation = rotation;
    protected override Task<StreamRound> PerformStreamAttempt(Dictionary<string, JSONValue> payload)
    {
        if (ExerciseDispatchBoundary) return base.PerformStreamAttempt(payload);
        Requests.Add(payload); return Task.FromResult(Next);
    }
    public Task<StreamRound> Round(string model = "fixture") => StreamOneRound(model, null, false);
}
