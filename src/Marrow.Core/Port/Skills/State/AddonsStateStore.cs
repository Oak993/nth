// PORT-ONLY: first-release feature unit SK-7. state.json on disk, in the folder the caller names.
using System.Text;

namespace Marrow.Core.Port.Skills.State;

/// <summary>
/// ONDEMAND-PLAN.md 3.1: <c>addons\state.json</c>. Takes the folder or file it is given and has no default, so nothing here can reach a data folder on its own
/// (the caller resolves <c>%LOCALAPPDATA%\Marrow Code\addons</c> through DevDataRoot, as the plan says). A missing file is a first run; a file that does not parse is an
/// error and is left alone, never replaced by an empty state (ED-NEW-f-sk-7). Writes go through <see cref="FileOps.WriteAtomic"/>.
/// </summary>
public static class AddonsStateStore
{
    public const string FileName = "state.json";

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string PathIn(string addonsFolder)
    {
        RequireAbsolute(addonsFolder, nameof(addonsFolder));
        return Path.Combine(addonsFolder, FileName);
    }

    private static void RequireAbsolute(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute path is required.", name);
    }

    /// <summary>The state in the file, settled as <see cref="AddonsState.AfterRestart"/> describes; <see cref="AddonsState.Empty"/> when the file does not exist.</summary>
    public static AddonsState Load(string path)
    {
        RequireAbsolute(path, nameof(path));
        if (!File.Exists(path)) return AddonsState.Empty;
        string text;
        try
        {
            text = Strict.GetString(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException error)
        {
            throw new StateFormatException("The state file is not UTF-8 text.", error);
        }
        return AddonsStateJson.Parse(text).AfterRestart();
    }

    public static void Save(string path, AddonsState state)
    {
        RequireAbsolute(path, nameof(path));
        ArgumentNullException.ThrowIfNull(state);
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent)) throw new DirectoryNotFoundException("The folder of the state file does not exist and is not created here: " + parent);
        FileOps.WriteAtomic(path, Strict.GetBytes(AddonsStateJson.Serialize(state)));
    }
}
