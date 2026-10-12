using Marrow.Services.Web;

namespace Marrow.App.Tests.Fixtures;

/// <summary>PORT-ONLY (web surfaces W25): reads the outcome of an ask_user wait that a headless view test has already seen end (those tests cannot await).</summary>
internal static class AskOutcomes
{
    internal static AskOutcome Of(Task<AskOutcome> ended)
    {
        if (!ended.IsCompletedSuccessfully) throw new InvalidOperationException("The wait has not ended.");
        return ended.Result;
    }
}
