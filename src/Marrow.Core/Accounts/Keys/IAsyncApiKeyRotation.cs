namespace Marrow.Core.Accounts.Keys;

/// <summary>A live host can wait for the user's first choice without ending the rejected request.</summary>
public interface IAsyncApiKeyRotation : IApiKeyRotation
{
    Task<KeyChoice?> CurrentAsync(CancellationToken ct);
    Task<KeyChoice?> OnFailureAsync(int status, TimeSpan? retryAfter, CancellationToken ct);
}
