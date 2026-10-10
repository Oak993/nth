// PORT-ONLY: first-release feature unit SK-14 (OD-0099 review of version 3: "in case a download fails, have a backup plan"). The words of the "Download it yourself" block, byte for byte from the locked
// skills-downloads design page (sd-page.js fbBlock, lines 401 to 408; the design's own "ED" tag beside the folder is its marker of an engineering decision, not a word of the page). "Checking…" and "Installed"
// beside the button are LibraryWords'.
namespace Marrow.Core.Port.Skills.Pages;

public static class ManualFallbackWords
{
    public const string Heading = "Download it yourself";
    public const string Get = "Get ";
    public const string PutIn = "Put it in ";
    public const string OpenPage = "Open download page ↗";
    public const string OpenFolder = "Open folder";
    public const string CheckAgain = "Check again";

    /// <summary>Step 3 for a tool (and a package set): the design's "its SHA-256 against the pinned one".</summary>
    public const string CheckingTool = "nth checks its SHA-256 against the pinned one and installs it.";

    /// <summary>Step 3 for a skill set or plugin: every file.</summary>
    public const string CheckingSet = "nth checks every file against its pinned hash and installs it.";

    /// <summary>The folder the design names (ED, "UN until built": a proposal of the design; <c>addons\downloads\</c> itself is the plan's, 3.1).</summary>
    public const string Folder = "%LOCALAPPDATA%\\" + Marrow.Core.Support.AppInfo.DataFolderName + "\\addons\\downloads\\manual\\"; // OD-0135: "nth"
}
