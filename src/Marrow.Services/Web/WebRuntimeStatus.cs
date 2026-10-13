using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>DESIGN 2.1, 2.7: whether the WebView2 runtime can run Visus.</summary>
public enum WebRuntimeState
{
    Ready,
    Missing,
    TooOld,
}

/// <summary>The runtime check's result: the state and the version found (null when missing).</summary>
public sealed record WebRuntimeStatus(WebRuntimeState State, string? Version)
{
    /// <summary>
    /// <c>GetAvailableCoreWebView2BrowserVersionString</c> against the minimum: missing when there is no version,
    /// too old when it sorts below the minimum.
    /// </summary>
    public static WebRuntimeStatus Evaluate(IWebEngine engine)
    {
        var version = engine.RuntimeVersion();
        if (string.IsNullOrWhiteSpace(version)) return new(WebRuntimeState.Missing, null);
        return engine.CompareVersions(version, engine.MinimumRuntimeVersion) < 0
            ? new(WebRuntimeState.TooOld, version)
            : new(WebRuntimeState.Ready, version);
    }

    /// <summary>
    /// Orders two runtime version strings ("141.0.3537.71", optionally followed by a channel name) part by part as
    /// numbers; a missing part counts as 0 and text that is not a number sorts first.
    /// </summary>
    public static int CompareVersions(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;

        static long[] Parts(string version) =>
            version.Trim().Split(' ', 2)[0].Split('.')
                .Select(p => long.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1)
                .ToArray();
    }

    /// <summary>The card a pane shows when the runtime cannot run it, or null when ready (DESIGN 2.7).</summary>
    public WebFailureCard? Card(bool showsCard) => State switch
    {
        WebRuntimeState.Ready => null,
        WebRuntimeState.Missing => WebFailureCard.For(WebFailureKind.RuntimeMissing, showsCard),
        WebRuntimeState.TooOld => WebFailureCard.For(WebFailureKind.RuntimeTooOld, showsCard),
    };
}
