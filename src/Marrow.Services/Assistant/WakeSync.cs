// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4, 16.1 "Task Scheduler"; unit AS-10; ED-NEW-dextra-as10-15). No Swift twin.
// Keeps the Windows Task Scheduler entries under \nth\ in step with the duties that asked to wake the PC: registers a duty's entry
// when it asks, renews it when its schedule changes, removes it when the duty is off, deleted, or no longer asks. It touches only
// tasks whose name nth made (duty-<id> under \nth\) and never a task it did not. The host is behind an interface: the product's
// is schtasks.exe with an argument list (no shell), a test's is a list. A failure is a line for the duty card, never an exception.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What the Task Scheduler is asked. Names are the task's name under \nth\ (duty-id).</summary>
public interface ITaskSchedulerHost
{
    /// <summary>The names under \nth\ (empty when the folder is not there).</summary>
    Task<IReadOnlyList<string>> List(CancellationToken cancellationToken);

    /// <summary>Creates or replaces the task. Throws <see cref="WakeEntryException"/> with the reason when Windows refuses.</summary>
    Task Register(string name, string xml, CancellationToken cancellationToken);

    Task Delete(string name, CancellationToken cancellationToken);
}

public sealed class WakeEntryException(string message) : Exception(message);

public sealed class WakeSync(ITaskSchedulerHost host, string exePath, TimeProvider? time = null, TimeZoneInfo? pcZone = null)
{
    private readonly Dictionary<string, string> registered = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim one = new(1, 1);
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly TimeZoneInfo zone = pcZone ?? TimeZoneInfo.Local;

    /// <summary>What happened to each duty's entry the last time (duty id to a line for its card; absent when all is well).</summary>
    public IReadOnlyDictionary<string, string> Problems { get; private set; } = new Dictionary<string, string>();

    /// <summary>
    /// Brings the entries in line with these duties. One sync at a time; an entry whose XML has not changed since this run of nth
    /// registered it is not asked again. Returns the problems by duty id.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> Sync(IEnumerable<Duty> duties, CancellationToken cancellationToken)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = clock.GetUtcNow();
            var wanted = duties.Select(d => WakeTaskXml.For(d, exePath, now, zone)).OfType<WakeEntry>().ToDictionary(e => e.Name, StringComparer.Ordinal);
            var problems = new Dictionary<string, string>();
            IReadOnlyList<string> existing;
            try { existing = await host.List(cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is WakeEntryException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                foreach (var entry in wanted.Values) problems[entry.DutyId] = "Windows Task Scheduler could not be read: " + error.Message;
                Problems = problems;
                return problems;
            }
            foreach (var (name, entry) in wanted)
            {
                if (registered.TryGetValue(name, out var before) && before == entry.Xml && existing.Contains(name, StringComparer.Ordinal)) continue;
                try
                {
                    await host.Register(name, entry.Xml, cancellationToken).ConfigureAwait(false);
                    registered[name] = entry.Xml;
                }
                catch (Exception error) when (error is WakeEntryException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    problems[entry.DutyId] = "Windows would not make the wake entry: " + error.Message;
                    registered.Remove(name);
                }
            }
            // Only what nth made, and only what no duty wants any more.
            foreach (var name in existing.Where(n => WakeTaskXml.IsOurs(n) && !wanted.ContainsKey(n)))
            {
                try
                {
                    await host.Delete(name, cancellationToken).ConfigureAwait(false);
                    registered.Remove(name);
                }
                catch (Exception error) when (error is WakeEntryException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    problems[name[WakeTaskXml.NamePrefix.Length..]] = "Windows would not remove the wake entry: " + error.Message;
                }
            }
            Problems = problems;
            return problems;
        }
        finally
        {
            one.Release();
        }
    }

    /// <summary>Removes every entry nth made (the owner turned waking off for good, or nth is being uninstalled).</summary>
    public async Task RemoveAll(CancellationToken cancellationToken) => await Sync([], cancellationToken).ConfigureAwait(false);
}

/// <summary>What a command-line program answered.</summary>
public sealed record ProcessOutcome(int ExitCode, string Output, string Error);

/// <summary>Runs a program with an argument list (never through a shell). The product's runs it; a test's answers.</summary>
public interface IProcessRunner
{
    Task<ProcessOutcome> Run(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    public static SystemProcessRunner Instance { get; } = new();

    public async Task<ProcessOutcome> Run(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return new ProcessOutcome(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception killed) when (killed is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new ProcessOutcome(-1, "", $"{Path.GetFileName(file)} did not finish in time.");
        }
    }
}

/// <summary>The Task Scheduler as schtasks.exe answers for it. Names are checked before they reach a command line.</summary>
public sealed class SchtasksHost(IProcessRunner? runner = null, string? system32 = null) : ITaskSchedulerHost
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner run = runner ?? SystemProcessRunner.Instance;
    private readonly string schtasks = Path.Combine(system32 ?? Environment.SystemDirectory, "schtasks.exe");

    private static string Path_(string name) => @"\" + WakeTaskXml.Folder + @"\" + name;

    public async Task<IReadOnlyList<string>> List(CancellationToken cancellationToken)
    {
        var result = await run.Run(schtasks, ["/Query", "/FO", "CSV", "/NH", "/TN", @"\" + WakeTaskXml.Folder + @"\"], Limit, cancellationToken).ConfigureAwait(false);
        // "ERROR: The system cannot find the path specified." is exit 1 with the folder missing: no entries yet.
        if (result.ExitCode != 0) return [];
        var prefix = @"\" + WakeTaskXml.Folder + @"\";
        return [.. result.Output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('"'))
            .Select(l => l.Trim('"').Split("\",\"")[0])
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(n => n[prefix.Length..]).Where(WakeTaskXml.IsOurs)];
    }

    public async Task Register(string name, string xml, CancellationToken cancellationToken)
    {
        if (!WakeTaskXml.IsOurs(name)) throw new WakeEntryException("That task name is not one nth makes.");
        // The task file must be UTF-16 with a byte order mark for schtasks.
        var file = Path.Combine(Path.GetTempPath(), "nth-wake-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            await File.WriteAllTextAsync(file, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true), cancellationToken).ConfigureAwait(false);
            var result = await run.Run(schtasks, ["/Create", "/TN", Path_(name), "/XML", file, "/F"], Limit, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new WakeEntryException(Reason(result));
        }
        finally
        {
            try { File.Delete(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task Delete(string name, CancellationToken cancellationToken)
    {
        if (!WakeTaskXml.IsOurs(name)) throw new WakeEntryException("That task name is not one nth makes.");
        var result = await run.Run(schtasks, ["/Delete", "/TN", Path_(name), "/F"], Limit, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 && !result.Error.Contains("cannot find", StringComparison.OrdinalIgnoreCase)) throw new WakeEntryException(Reason(result));
    }

    private static string Reason(ProcessOutcome result)
    {
        var text = (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim();
        return text.Length == 0 ? "schtasks answered " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + "." : text.Split('\n')[0].Trim();
    }
}
