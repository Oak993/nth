// PORT-ONLY: SK-13. Where the app's one skills host is bound. There is no Swift twin.
using Marrow.Core.Port;

namespace Marrow.Services.Port.Skills.Host;

public static class SkillsSeams
{
    private const string Message = "SkillsSeams.Host is not bound: Marrow.App binds it at start-up (SkillsStartup.Bind); a test opens its own host on its own data root (R-TEST-9).";

    /// <summary>
    /// The host of the data root the app runs on. A trap until bound, so a test can never reach the real <c>%LOCALAPPDATA%\Marrow Code</c> through it (the pattern of
    /// <c>StoragePaths.Locations</c>); the Settings pages and the providers' wiring (SK-11, SK-15, SK-16) read it.
    /// </summary>
    public static Ambient<SkillsHost> Host { get; } = new((Func<SkillsHost>)(() => throw new SwiftTrapException(Message)));
}
