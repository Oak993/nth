using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Microsoft.Web.WebView2.Core;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 2.1 (ED-NEW-web-w7-1): the real engine. Avalonia's <c>NativeWebView</c> (package Avalonia.Controls.WebView,
/// MIT) hosts each view in windowed (HWND) mode; the UI host hands this engine the adapter's raw <c>ICoreWebView2</c>
/// and <c>ICoreWebView2Controller</c>, which it wraps with the managed WebView2 SDK for permissions, downloads, the
/// DevTools Protocol and accelerator keys. The engine creates the one shared environment itself (identical options for
/// every view, the same object for a pre-warm, a fresh one after "Restart Visus") and gives the host its pointer for
/// <c>ExplicitEnvironment</c>. Native behaviour is confirmed by the lead's spikes (a) and (i); unit tests cover the pure
/// parts and the reflection seams, the rest runs through <see cref="FakeWebEngine"/>.
/// </summary>
public sealed class WebView2Engine : IWebEngine, IDisposable
{
    /// <summary>
    /// The oldest runtime nth accepts (DESIGN 2.1, 2.7). Planning value: the runtime that shipped the newest API nth needs
    /// without a fallback (<c>CallDevToolsProtocolMethodForSessionAsync</c>, <c>LaunchingExternalUriScheme</c>,
    /// <c>MemoryUsageTargetLevel</c>, profiles and controller options). Spike (a) confirms it on the owner's PC.
    /// </summary>
    public const string MinimumVersion = "112.0.1722.39";

    // ICoreWebView2Environment (WebView2.h).
    private static readonly Guid EnvironmentIid = new("B96D755E-0319-4E92-A296-23436F46A1FC");

    private readonly Logger log = new(Logger.Subsystem, "web");
    private CoreWebView2Environment? environment;
    private nint environmentPointer;
    private Task<CoreWebView2Environment>? pending;
    private int openViews;
    private bool disposed;

    public string MinimumRuntimeVersion => MinimumVersion;

    public bool IsPrewarmed { get; private set; }

    /// <summary>W10: the clears waiting for a profile's next page, read from the data folder when the environment is first made.</summary>
    public WebProfileClears? ProfileClears { get; private set; }

    public event EventHandler? NewRuntimeVersionAvailable;

    public string? RuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString((string?)null);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException or COMException or FileNotFoundException)
        {
            return null;
        }
    }

    public int CompareVersions(string left, string right) => WebRuntimeStatus.CompareVersions(left, right);

    public IWebEngineView CreateView(WebCreationOptions creation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        openViews++;
        return new WebView2View(this, creation);
    }

    public async Task<nint> AcquireEnvironmentAsync(WebCreationOptions creation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (environment is not null && environmentPointer != 0) return environmentPointer;
        pending ??= CreateEnvironmentAsync(creation);
        try
        {
            await pending.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            pending = null;
            log.Error($"web: environment creation failed: {ex.Message}");
            return 0; // the platform view creates its own (Avalonia's cache, same options)
        }

        return environmentPointer;
    }

    public void ResetEnvironment() => DropEnvironment();

    public async Task PrewarmAsync(WebCreationOptions creation, CancellationToken cancellationToken)
    {
        if (await AcquireEnvironmentAsync(creation, cancellationToken) != 0) IsPrewarmed = true;
    }

    public void ReleaseNative(nint pointer)
    {
        if (pointer != 0) Marshal.Release(pointer);
    }

    public void ReleasePrewarm()
    {
        IsPrewarmed = false;
        // Unused pre-warm: let the browser process go. With views open, their own references keep it alive.
        if (openViews == 0) DropEnvironment();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DropEnvironment();
    }

    internal void ViewClosed() => openViews = Math.Max(0, openViews - 1);

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync(WebCreationOptions creation)
    {
        Directory.CreateDirectory(creation.UserDataFolder);
        ProfileClears ??= await Task.Run(() => WebProfileClears.ForFolder(creation.UserDataFolder)); // off the UI thread: it reads a file
        var options = new CoreWebView2EnvironmentOptions(additionalBrowserArguments: creation.AdditionalBrowserArguments, language: creation.Language);
        var created = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: creation.UserDataFolder, options: options);
        environment = created;
        environmentPointer = EnvironmentPointer(created);
        created.NewBrowserVersionAvailable += OnNewBrowserVersion;
        return created;
    }

    private void OnNewBrowserVersion(object? sender, object e) => NewRuntimeVersionAvailable?.Invoke(this, EventArgs.Empty);

    private void DropEnvironment()
    {
        pending = null;
        IsPrewarmed = false;
        if (environment is { } old) old.NewBrowserVersionAvailable -= OnNewBrowserVersion;
        environment = null;
        if (environmentPointer != 0)
        {
            Marshal.Release(environmentPointer);
            environmentPointer = 0;
        }
    }

    /// <summary>
    /// ED-NEW-web-w7-1: the managed SDK has no public accessor for an environment's COM object, so the pinned SDK's
    /// internal <c>_rawNative</c> field is read (a unit test pins its presence). Returns an owned
    /// <c>ICoreWebView2Environment</c> pointer, or zero when the field is gone (the host then lets Avalonia create one).
    /// </summary>
    internal static nint EnvironmentPointer(CoreWebView2Environment created)
    {
        var raw = RawNativeField?.GetValue(created);
        if (raw is null) return 0;
        var unknown = Marshal.GetIUnknownForObject(raw);
        try
        {
            var iid = EnvironmentIid;
            return Marshal.QueryInterface(unknown, in iid, out var pointer) == 0 ? pointer : 0;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>The pinned SDK's internal environment COM field (null if a later SDK removed it).</summary>
    internal static FieldInfo? RawNativeField { get; } = typeof(CoreWebView2Environment).GetField("_rawNative", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// ED-NEW-web-w7-1: <c>AcceleratorKeyPressed</c> lives on the controller, and the managed SDK only wraps a raw
    /// controller through its internal constructor <c>CoreWebView2Controller(object)</c> (a unit test pins it). Null when
    /// a later SDK removed it; chord forwarding then stays off and the engine logs it.
    /// </summary>
    internal static ConstructorInfo? ControllerConstructor { get; } =
        typeof(CoreWebView2Controller).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(object)]);

    internal static CoreWebView2Controller? WrapController(nint controller)
    {
        if (ControllerConstructor is null || controller == 0) return null;
        var raw = Marshal.GetObjectForIUnknown(controller);
        return (CoreWebView2Controller)ControllerConstructor.Invoke([raw]);
    }

    /// <summary>The pixel size in a PNG's IHDR chunk, or (0, 0) when the bytes are not a PNG.</summary>
    public static (int Width, int Height) PngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature)) return (0, 0);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[16..20]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[20..24]));
    }

    /// <summary>
    /// A CSS colour as <c>getComputedStyle</c> reports it ("rgb(r, g, b)" or "rgba(r, g, b, a)") to ARGB; null for a
    /// fully transparent colour or text it does not read.
    /// </summary>
    public static uint? ParseCssColor(string? css)
    {
        if (string.IsNullOrWhiteSpace(css)) return null;
        var text = css.Trim();
        var open = text.IndexOf('(', StringComparison.Ordinal);
        var close = text.LastIndexOf(')');
        if (open < 0 || close <= open) return null;
        var name = text[..open].Trim();
        if (!name.Equals("rgb", StringComparison.OrdinalIgnoreCase) && !name.Equals("rgba", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = text[(open + 1)..close].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        if (!TryChannel(parts[0], out var r) || !TryChannel(parts[1], out var g) || !TryChannel(parts[2], out var b)) return null;
        var alpha = 1.0;
        if (parts.Length >= 4)
        {
            var a = parts[3];
            var percent = a.EndsWith('%');
            if (!double.TryParse(percent ? a[..^1] : a, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha)) return null;
            if (percent) alpha /= 100;
        }

        if (alpha <= 0) return null;
        var a8 = (uint)Math.Clamp(Math.Round(alpha * 255), 0, 255);
        return (a8 << 24) | (r << 16) | (g << 8) | b;

        static bool TryChannel(string value, out uint channel)
        {
            channel = 0;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
            channel = (uint)Math.Clamp(Math.Round(number), 0, 255);
            return true;
        }
    }

    /// <summary>Maps the SDK's process failure kind.</summary>
    internal static WebProcessFailureKind Map(CoreWebView2ProcessFailedKind kind) => kind switch
    {
        CoreWebView2ProcessFailedKind.BrowserProcessExited => WebProcessFailureKind.BrowserProcessExited,
        CoreWebView2ProcessFailedKind.RenderProcessExited => WebProcessFailureKind.RenderProcessExited,
        CoreWebView2ProcessFailedKind.RenderProcessUnresponsive => WebProcessFailureKind.RenderProcessUnresponsive,
        CoreWebView2ProcessFailedKind.FrameRenderProcessExited => WebProcessFailureKind.FrameRenderProcessExited,
        CoreWebView2ProcessFailedKind.GpuProcessExited => WebProcessFailureKind.GpuProcessExited,
        CoreWebView2ProcessFailedKind.UtilityProcessExited => WebProcessFailureKind.UtilityProcessExited,
        CoreWebView2ProcessFailedKind.SandboxHelperProcessExited => WebProcessFailureKind.SandboxHelperProcessExited,
        CoreWebView2ProcessFailedKind.PpapiPluginProcessExited => WebProcessFailureKind.PpapiPluginProcessExited,
        CoreWebView2ProcessFailedKind.PpapiBrokerProcessExited => WebProcessFailureKind.PpapiBrokerProcessExited,
        CoreWebView2ProcessFailedKind.UnknownProcessExited => WebProcessFailureKind.UnknownProcessExited,
        _ => WebProcessFailureKind.UnknownProcessExited,
    };

    /// <summary>Maps the SDK's permission kind.</summary>
    internal static WebPermissionKind Map(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => WebPermissionKind.Camera,
        CoreWebView2PermissionKind.Microphone => WebPermissionKind.Microphone,
        CoreWebView2PermissionKind.Geolocation => WebPermissionKind.Location,
        CoreWebView2PermissionKind.Notifications => WebPermissionKind.Notifications,
        CoreWebView2PermissionKind.ClipboardRead => WebPermissionKind.ClipboardRead,
        CoreWebView2PermissionKind.MidiSystemExclusiveMessages => WebPermissionKind.Midi,
        CoreWebView2PermissionKind.OtherSensors => WebPermissionKind.Sensors,
        CoreWebView2PermissionKind.LocalFonts => WebPermissionKind.LocalFonts,
        CoreWebView2PermissionKind.WindowManagement => WebPermissionKind.WindowManagement,
        CoreWebView2PermissionKind.FileReadWrite => WebPermissionKind.FileSystemAccess,
        CoreWebView2PermissionKind.Autoplay => WebPermissionKind.Autoplay,
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => WebPermissionKind.MultipleDownloads,
        CoreWebView2PermissionKind.PersistentStorage => WebPermissionKind.PersistentStorage,
        _ => WebPermissionKind.Unknown,
    };

    /// <summary>
    /// The settings each profile runs with (DESIGN 8.1, 8.4): host objects off everywhere; web messages only for nth's
    /// own sandboxed origins; browser accelerator keys off (Visus handles its keys itself, DESIGN 3.7); autofill and
    /// password saving only in <c>browse</c>; script dialogs drawn by nth; DevTools only for preview tabs; SmartScreen on.
    /// </summary>
    internal static WebViewSettingsPlan SettingsFor(WebCreationOptions creation)
    {
        var sandboxed = creation.Kind == WebProfileKind.Artifacts;
        return new WebViewSettingsPlan(
            AreHostObjectsAllowed: false,
            IsWebMessageEnabled: sandboxed,
            AreDevToolsEnabled: creation.EnableDevTools,
            AreBrowserAcceleratorKeysEnabled: false,
            AreDefaultScriptDialogsEnabled: false,
            AreDefaultContextMenusEnabled: !sandboxed,
            // W8b: the 8.4 row "Autofill and password saving: Chromium's, in browse only", read from the one table.
            IsPasswordAutosaveEnabled: WebFeaturePolicy.Rule(creation.Kind, WebFeature.Autofill) == WebFeatureRule.System,
            IsGeneralAutofillEnabled: WebFeaturePolicy.Rule(creation.Kind, WebFeature.Autofill) == WebFeatureRule.System,
            IsZoomControlEnabled: !sandboxed,
            IsSwipeNavigationEnabled: !sandboxed,
            IsStatusBarEnabled: !sandboxed,
            IsReputationCheckingRequired: true,
            IsNonClientRegionSupportEnabled: false);
    }
}

/// <summary>The per-profile WebView2 settings (DESIGN 8.1, 8.4), as data so tests can read them without WebView2.</summary>
internal sealed record WebViewSettingsPlan(
    bool AreHostObjectsAllowed,
    bool IsWebMessageEnabled,
    bool AreDevToolsEnabled,
    bool AreBrowserAcceleratorKeysEnabled,
    bool AreDefaultScriptDialogsEnabled,
    bool AreDefaultContextMenusEnabled,
    bool IsPasswordAutosaveEnabled,
    bool IsGeneralAutofillEnabled,
    bool IsZoomControlEnabled,
    bool IsSwipeNavigationEnabled,
    bool IsStatusBarEnabled,
    bool IsReputationCheckingRequired,
    bool IsNonClientRegionSupportEnabled);
