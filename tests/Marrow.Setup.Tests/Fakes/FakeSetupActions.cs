using Avalonia.Controls;
using Marrow.Setup.Screens;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>The screens' actions outside the window, recorded instead of done (nothing is started, copied or picked).</summary>
public sealed class FakeSetupActions : ISetupActions
{
    public List<string> Opened { get; } = [];

    public List<string> Copied { get; } = [];

    public int LicenseViews { get; private set; }

    public bool OpenSucceeds { get; set; } = true;

    public bool CopySucceeds { get; set; } = true;

    /// <summary>The folder the next pick returns (null: cancelled).</summary>
    public string? NextFolder { get; set; }

    public bool OpenApp(string installRoot)
    {
        Opened.Add(installRoot);
        return OpenSucceeds;
    }

    public Task<bool> CopyAsync(TopLevel window, string text)
    {
        if (CopySucceeds) Copied.Add(text);
        return Task.FromResult(CopySucceeds);
    }

    public bool ViewLicense()
    {
        LicenseViews++;
        return true;
    }

    public Task<string?> PickFolderAsync(TopLevel window, string current) => Task.FromResult(NextFolder);
}
