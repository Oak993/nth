using Marrow.Core.Web;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.6; ED-NEW-web-w9-2. Only TCP listeners of this script's tree may be HEAD-probed.
public sealed class PreviewDiscovery
{
    public static IReadOnlyList<int> CommonPorts { get; } = [3000, 3001, 4000, 4200, 4321, 5000, 5173, 5174, 6006, 8000, 8080, 8081, 8888, 1313];
    public Func<int, IReadOnlyCollection<int>> ProcessTree { get; set; } = PreviewProcesses.Tree;
    public static IReadOnlyCollection<string> MachineAddresses()
    {
        try { return PreviewProcesses.LanAddresses(); }
        catch (System.Net.NetworkInformation.NetworkInformationException) { return []; }
    }
    public Func<IReadOnlyList<PreviewListener>> Listeners { get; init; } = PreviewProcesses.Listeners;
    public Func<string, CancellationToken, Task<bool>> Head { get; init; } = Probe;

    public async Task<IReadOnlyList<string>> FindAsync(int root, string? explicitUrl = null, CancellationToken cancellationToken = default)
    {
        if (DevServerDetector.ValidExplicit(explicitUrl) is { } chosen) return [chosen];
        var tree = ProcessTree(root).ToHashSet();
        if (tree.Count == 0) return [];
        var ports = Listeners().Where(row => tree.Contains(row.ProcessId) && row.Port is > 0 and <= 65535)
            .Select(row => row.Port).Distinct().OrderBy(port => CommonPorts.Contains(port) ? CommonPorts.ToList().IndexOf(port) : int.MaxValue).ThenBy(port => port).ToList();
        var found = new List<string>();
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Reconcile both snapshots again before making a request; a dying process must not donate its port.
            var live = ProcessTree(root).ToHashSet();
            if (!Listeners().Any(row => row.Port == port && live.Contains(row.ProcessId))) continue;
            var url = "http://localhost:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";
            if (await Head(url, cancellationToken)) found.Add(url);
        }
        return found;
    }

    private static async Task<bool> Probe(string url, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(500) };
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        try { using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); return true; }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}
