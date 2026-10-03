using System.Collections;
using System.Reflection;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test rig (R-TEST-10; ED-256 (10)). A test that shows link text (a markdown link, a bare domain, a
/// video link) makes InlineText ask the host's favicon (FaviconCache.Image, MarkdownView.swift:914-953), which without a seed goes
/// out to https://host/favicon.ico, or, when the asking task was cancelled first (a popover measured off screen is attached and
/// detached in one turn), ends in the cache's failed set, which a later test for the same host then finds. This rig keeps all of that
/// out of the test, and fails the test that let it happen:
///  - each named host has an in-flight fetch that never ends (HydraMergeFaviconSeed), so the asking awaits it and nothing is sent;
///  - the flow's HTTP client is a fail-closed handler (Marrow.Testing.InProcessHttpHandler with no answer: a request to a host that
///    is not loopback is refused in process and recorded), so a request cannot leave the machine;
///  - the cache's own tables are watched: a host the cache learned about while the rig lived and the test did not name was asked for.
/// Disposing the rig puts everything back and reports each refused request and each unnamed host. Make it first in the test, so every
/// main-actor item the test starts takes its flow (MainActor captures the caller's context).
/// </summary>
public sealed class NoLinkNetwork : IDisposable
{
    private readonly InProcessHttpHandler handler = new();
    private readonly IDisposable transport;
    private readonly HashSet<string> named;
    private readonly HashSet<string> known;
    private readonly List<HydraMergeFaviconSeed> seeds = [];
    private bool disposed;

    public NoLinkNetwork(params string[] hosts)
    {
        named = [];
        known = [.. CacheKeys()];
        transport = URLSession.Transport.Override(handler);
        foreach (var host in hosts) Pending(host);
    }

    /// <summary>
    /// Names a host: its favicon fetch is in flight from now on, and finishes only if the test completes the task this returns (with an
    /// icon or none, as a test of the icon's arrival does); the cache's trace of it is removed when the rig is disposed.
    /// </summary>
    public TaskCompletionSource<Avalonia.Media.IImage?> Pending(string host)
    {
        var pending = new TaskCompletionSource<Avalonia.Media.IImage?>();
        named.Add(host.ToLowerInvariant());
        seeds.Add(new HydraMergeFaviconSeed(host, null, pending.Task));
        return pending;
    }

    /// <summary>The requests the fail-closed handler refused so far, as "METHOD address".</summary>
    public string[] Refused => [.. handler.Received.Select(request => request.Method + " " + request.Url)];

    /// <summary>The hosts the favicon cache learned about (an icon, a failure or a fetch under way) that the rig was not given.</summary>
    public string[] Asked => [.. CacheKeys().Where(key => !known.Contains(key) && !named.Contains(key)).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Puts everything back and fails the test if a request reached the handler or the cache asked for an unnamed host.</summary>
    public void Dispose()
    {
        if (disposed) return;
        var problems = DisposeAndReport();
        Assert.True(problems.Length == 0, "the test showed link text it did not account for: " + string.Join("; ", problems));
    }

    /// <summary>Puts everything back and returns what <see cref="Dispose"/> would fail on, with no failure (for the rig's own tests).</summary>
    public string[] DisposeAndReport()
    {
        string[] Problems() => [.. Refused.Select(request => "refused " + request), .. Asked.Select(host => "the favicon of " + host + " was asked for")];
        var problems = Problems();
        if (disposed) return problems;
        disposed = true;
        transport.Dispose();
        foreach (var seed in seeds) seed.Dispose();
        handler.Dispose();
        return problems;
    }

    /// <summary>Whether the favicon cache holds the host in any table (an icon, a failure or a fetch under way).</summary>
    public static bool CacheKnows(string host) => CacheKeys().Contains(host.ToLowerInvariant());

    private static IEnumerable<string> CacheKeys()
    {
        static object Table(string name) => typeof(FaviconCache).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var keys = new List<string>();
        keys.AddRange(((HashSet<string>)Table("failed")).ToArray());
        keys.AddRange(((IDictionary)Table("memory")).Keys.Cast<string>().ToArray());
        keys.AddRange(((IDictionary)Table("inFlight")).Keys.Cast<string>().ToArray());
        return keys;
    }
}
