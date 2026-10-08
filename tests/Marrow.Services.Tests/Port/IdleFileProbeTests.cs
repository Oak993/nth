using System.Runtime.ExceptionServices;
using Marrow.Services.MCP;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port;

[Trait("Decision", "ED-177")]
public sealed class IdleFileProbeTests
{
    [Fact]
    public void Missing_optional_mcp_configs_return_nil_without_throwing_on_every_probe()
    {
        using var scratch = new StorageScratchFolder();
        var exceptions = 0;
        void Observe(object? _, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is IOException && e.Exception.Message.Contains(scratch.Root, StringComparison.Ordinal))
                Interlocked.Increment(ref exceptions);
        }
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        try
        {
            for (var i = 0; i < 10; i++)
            {
                Assert.Null(MCPProviderConfig.ClaudeConfigPath());
                Assert.Null(MCPProviderConfig.GeminiSettingsPath());
                Assert.Null(MCPProviderConfig.CopilotConfigJSON());
                Assert.Null(MCPProviderConfig.CodexOverrides([]));
            }
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Observe; }
        Assert.Equal(0, exceptions);
    }

    [Fact]
    public void A_missing_thread_document_returns_nil_without_throwing_on_every_probe()
    {
        using var scratch = new StorageScratchFolder();
        var exceptions = 0;
        void Observe(object? _, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is IOException && e.Exception.Message.Contains(scratch.Root, StringComparison.Ordinal))
                Interlocked.Increment(ref exceptions);
        }
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        try
        {
            var id = Guid.NewGuid();
            for (var i = 0; i < 10; i++) Assert.Null(Storage.ReadDocument(id));
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Observe; }
        Assert.Equal(0, exceptions);
    }
}
