namespace Marrow.Core.Accounts.Keys;

/// <summary>Resumes a withheld 402/429 request after consent, retaining that request's tried-key set.</summary>
public interface IApiKeyContinuation
{
    KeyChoice? ContinueRejectedRequest();
}
