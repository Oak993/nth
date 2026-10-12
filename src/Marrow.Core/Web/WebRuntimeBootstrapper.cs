using System.Globalization;

namespace Marrow.Core.Web;

/// <summary>Why the runtime flow did not end with a usable runtime (DESIGN 2.1, W23).</summary>
public enum WebRuntimeInstallFailure
{
    /// <summary>The flow succeeded.</summary>
    None,

    /// <summary>Microsoft's server could not be reached.</summary>
    Offline,

    /// <summary>The download started and did not finish (an HTTP error, a cut connection, a file of the wrong size).</summary>
    DownloadFailed,

    /// <summary>The file is not signed by Microsoft or its signature does not verify; it was never run.</summary>
    NotSigned,

    /// <summary>Windows would not start the installer (a policy, a security product, a missing file).</summary>
    CouldNotStart,

    /// <summary>The installer ran and stopped with a failure exit code and no usable runtime afterwards.</summary>
    InstallerFailed,

    /// <summary>The installer did not finish in <see cref="WebRuntimeBootstrapper.InstallTimeout"/>; nth stopped waiting.</summary>
    TimedOut,

    /// <summary>The installer finished and the runtime is still not there.</summary>
    StillMissing,

    /// <summary>The installer finished and the runtime is still older than the minimum.</summary>
    StillTooOld,
}

/// <summary>
/// DESIGN 2.1 (W23): everything about Microsoft's Evergreen bootstrapper that needs no I/O. Where it comes from, how it
/// is run, who must have signed it, how long nth waits, and the one calm sentence for each way the flow can end badly.
/// The flow itself (<c>WebRuntimeInstaller</c> in Marrow.Services) runs only after the user pressed Install or Update on
/// the pane's card, and installs per user (it is not started elevated): Microsoft's documentation says a non-elevated
/// run installs the runtime for the current user and that a per-machine Edge updater replaces that copy.
/// </summary>
public static class WebRuntimeBootstrapper
{
    /// <summary>
    /// The link Microsoft's WebView2 page gives under "Get the Link" for the Evergreen bootstrapper (the same link the
    /// WebView2Samples deployment samples carry). It redirects to Microsoft's download service.
    /// </summary>
    public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>What the file is called when the redirect names none.</summary>
    public const string FileName = "MicrosoftEdgeWebview2Setup.exe";

    /// <summary>The documented silent install switches: no window, install (or update) the Evergreen runtime.</summary>
    public const string Arguments = "/silent /install";

    /// <summary>Microsoft's page for people who want to install the runtime themselves (the failure card's second button).</summary>
    public const string RuntimePageUrl = "https://developer.microsoft.com/microsoft-edge/webview2/consumer/";

    /// <summary>The common name the bootstrapper's Authenticode signer must carry.</summary>
    public const string RequiredSigner = "Microsoft Corporation";

    /// <summary>The bootstrapper is about 2 MB; anything over this is not it and is dropped while downloading.</summary>
    public const long MaxBytes = 8L * 1024 * 1024;

    /// <summary>A file under this is not a signed installer either.</summary>
    public const long MinBytes = 64L * 1024;

    /// <summary>How long the download may take before nth gives up.</summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long nth waits for the installer to finish.</summary>
    public static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The download is accepted only over HTTPS, at the end of the redirects too (the signature check below is the real
    /// gate; this keeps a plain-text hop from ever carrying the file).
    /// </summary>
    public static bool IsAcceptableSource(Uri? finalUri) =>
        finalUri is { IsAbsoluteUri: true } && string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a file of this length can be the bootstrapper.</summary>
    public static bool IsAcceptableSize(long bytes) => bytes is >= MinBytes and <= MaxBytes;

    /// <summary>
    /// True when a certificate subject ("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, ...") names
    /// <see cref="RequiredSigner"/> as its common name. A name that merely contains the words (a look-alike signer) does not pass.
    /// </summary>
    public static bool IsAcceptableSigner(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        foreach (var part in SplitSubject(subject))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) continue;
            if (!part[..equals].Trim().Equals("CN", StringComparison.OrdinalIgnoreCase)) continue;
            if (Unquote(part[(equals + 1)..].Trim()).Equals(RequiredSigner, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>The sentence the install-failed card shows for <paramref name="failure"/> (DESIGN 2.7 style; W22 moves it into the resource files).</summary>
    /// <param name="failure">How the flow ended.</param>
    /// <param name="foundVersion">The runtime version Windows reports, for <see cref="WebRuntimeInstallFailure.StillTooOld"/>.</param>
    public static string ReasonFor(WebRuntimeInstallFailure failure, string? foundVersion = null) => failure switch
    {
        WebRuntimeInstallFailure.None => "",
        WebRuntimeInstallFailure.Offline => "nth could not reach Microsoft. Check your connection and try again.",
        WebRuntimeInstallFailure.DownloadFailed => "The download from Microsoft did not finish. Try again in a moment.",
        WebRuntimeInstallFailure.NotSigned => "The file did not pass the signature check, so nth did not run it.",
        WebRuntimeInstallFailure.CouldNotStart => "Windows would not start Microsoft's installer. A security setting may be blocking it.",
        WebRuntimeInstallFailure.InstallerFailed => "Microsoft's installer stopped before it finished. Try again, or install it from Microsoft's page.",
        WebRuntimeInstallFailure.TimedOut => "The install is taking too long, so nth stopped waiting. Try again, or install it from Microsoft's page.",
        WebRuntimeInstallFailure.StillMissing => "The installer finished, but Windows still reports no WebView2 runtime. Try again, or install it from Microsoft's page.",
        WebRuntimeInstallFailure.StillTooOld => string.Create(CultureInfo.InvariantCulture,
            $"The runtime is still version {(string.IsNullOrWhiteSpace(foundVersion) ? "unknown" : foundVersion)}. If your organization controls updates, ask them to update the WebView2 runtime."),
    };

    private static IEnumerable<string> SplitSubject(string subject)
    {
        // A distinguished name separates its parts with commas; a comma inside a quoted value does not split.
        var start = 0;
        var quoted = false;
        for (var i = 0; i < subject.Length; i++)
        {
            if (subject[i] == '"') quoted = !quoted;
            else if (subject[i] == ',' && !quoted)
            {
                yield return subject[start..i];
                start = i + 1;
            }
        }

        yield return subject[start..];
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal) : value;
}
