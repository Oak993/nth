using Marrow.Core.Accounts;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>One answer a test's card gave: the thread, the choices and the Remember line.</summary>
public sealed record CardAnswer(Guid Thread, IReadOnlyList<Choice> Choices, RememberScope Scope);
