// PORT-ONLY: job 6 sends only after explicit cross-service consent.
namespace Marrow.Core.Port.MultiAccount.Opinions;
public enum AskSendResult { NoSelection, NeedsConfirmation, Canceled, Sent, Busy }
