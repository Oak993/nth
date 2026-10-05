// PORT-ONLY: SK-11d / WU-9. The message is fixed text: it never carries file content, a path value or a secret.
namespace Marrow.Services.Port.Skills.Providers;

public sealed class OpenCodeConfigUnreadableException(string reason) : Exception(reason);
