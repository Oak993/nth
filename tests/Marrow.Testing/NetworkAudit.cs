using System.Net.Http;
using Marrow.Core.Port;

namespace Marrow.Testing;

/// <summary>
/// PORT-ONLY test audit (R-TEST-10; ED-256 (10)). With the environment variable named by
/// <see cref="LogVariable"/> set to a file path, the test process's own HTTP client (URLSession.Transport's process default, the
/// handler every flow without an override of its own uses) is a fail-closed handler: a request to a host that is not loopback
/// never reaches a socket (it is refused in process, as a refused connection), and its method and address are appended to that
/// file. A run of any test filter with the variable set is therefore a proof, over every test of the filter and whatever flow
/// it runs in, that none of them asked a real host for anything: the file stays empty. With the variable unset this does
/// nothing, and the process keeps the handler the port has always used.
/// </summary>
public static class NetworkAudit
{
    public const string LogVariable = "MARROW_TEST_NETWORK_AUDIT";

    private static readonly object Gate = new();

    /// <summary>Whether this process runs with the audit handler as its default.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Each test assembly calls this from its own module initializer, so it runs as the assembly loads.</summary>
    public static void InstallIfRequested()
    {
        if (Environment.GetEnvironmentVariable(LogVariable) is not { Length: > 0 } path) return;
        URLSession.Transport.SetProcessDefault(new AuditHandler(path));
        Installed = true;
    }

    private sealed class AuditHandler(string path) : DelegatingHandler(new InProcessHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { IsLoopback: false } url)
            {
                lock (Gate) File.AppendAllText(path, request.Method.Method + " " + url + "\n");
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
