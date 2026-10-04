using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
internal sealed class FakeInstallProcess : IInstallProcess
{
    public List<InstallCommand> Commands { get; } = [];
    public int ExitCode { get; set; }
    public Action<InstallCommand>? OnRun { get; set; }
    public Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(command);
        OnRun?.Invoke(command);
        return Task.FromResult(ExitCode);
    }
}
