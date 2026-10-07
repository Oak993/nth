using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture (SK-11f, R-TEST-10). An OpenAICompatibleSession whose run_command answers from a script instead of starting a shell, so a test can
// see the whole turn (approval, tap events, result) without a real command.
public sealed class NativeCommandSeam(OpenAICompatibleSession.Config config, SessionConfiguration configuration) : OpenAICompatibleSession(config, configuration)
{
    public Func<string, (string Output, int ExitCode)> Script { get; set; } = _ => ("", 0);
    public List<string> Commands { get; } = [];

    protected override Task<(string Output, int ExitCode)> RunCommand(string command)
    {
        Commands.Add(command);
        return Task.FromResult(Script(command));
    }
}
