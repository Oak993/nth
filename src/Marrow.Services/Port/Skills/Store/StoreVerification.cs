// PORT-ONLY: SK-5. Internal verification result; user-facing damage is ItemState.Damaged (SK-7).
namespace Marrow.Services.Port.Skills.Store;

public sealed record StoreVerification(bool IsValid);
