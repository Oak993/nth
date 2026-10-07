namespace Marrow.Core.Accounts;

// A step may name a service's original row and select another connected key of that same service.
public interface IPillStepAccountStore
{
    AccountInfo? ResolvePillStep(PillStep step);
}
