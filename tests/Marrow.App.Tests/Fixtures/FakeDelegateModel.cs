using Marrow.App.Assistant;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-22: the delegate's model, faked. It answers a prompt with the text the test gives (JSON by default for the question it was asked), records
/// every prompt it was given, and can be made to think until the test lets it, to fail, or to be unbound. Nothing here calls a provider.
/// </summary>
internal sealed class FakeDelegateModel : IDelegateModel
{
    private readonly object gate = new();

    /// <summary>The provider the call says it runs on (the privacy rules read it).</summary>
    public ProviderKind Provider { get; set; } = ProviderKind.Claude;

    /// <summary>False: no model can be asked (Prepare answers null).</summary>
    public bool Bound { get; set; } = true;

    /// <summary>The text for a prompt; null is a model that said nothing.</summary>
    public Func<string, string?> Reply { get; set; } = _ => null;

    /// <summary>While set and not done, the model is still thinking; it ends with the call's cancellation.</summary>
    public Task? Thinking { get; set; }

    public Exception? Fails { get; set; }

    public DelegateWho? LastWho { get; private set; }

    public List<string> Prompts { get; } = [];

    public int Asked
    {
        get { lock (gate) return Prompts.Count; }
    }

    public int Cancelled { get; private set; }

    public DelegateCall? Prepare(DelegateWho? who)
    {
        LastWho = who;
        if (!Bound) return null;
        return new DelegateCall(Provider, async (prompt, cancel) =>
        {
            lock (gate) Prompts.Add(prompt);
            try
            {
                if (Thinking is { } thinking) await thinking.WaitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                lock (gate) Cancelled++;
                throw;
            }
            if (Fails is { } error) throw error;
            return Reply(prompt);
        });
    }

    /// <summary>The model's JSON for one single-question ask.</summary>
    public static string Json(string answer, double confidence = 0.92, string basis = "technical", string flag = "none", string evidence = "", string reason = "The repo already does it.", string id = "q1") =>
        "{\"answers\": {\"" + id + "\": [\"" + answer + "\"]}, \"confidence\": " + confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ", \"basis\": \"" + basis + "\", \"flag\": \"" + flag + "\", \"evidence\": \"" + evidence + "\", \"reason\": \"" + reason + "\"}";
}
