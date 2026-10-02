namespace Marrow.App.Tests.Window.Native;

// The opt-in of the view gallery (GalleryNativeTests). The gate never runs it: a Fact returns at once unless MARROW_GALLERY=1
// is set in the environment of the process that runs the test (the parent). The native child's environment is an allow-list
// (WindowManagerNativeChildProtocol.ChildEnvironment), so the decision and the output folder travel in the child's request
// file instead: the parent writes the folder into request.json, and the child reads it back from there.
internal static class GalleryRun
{
    internal const string EnableVariable = "MARROW_GALLERY";
    internal const string DirectoryVariable = "MARROW_GALLERY_DIR";
    internal const string DefaultDirectory = @"E:\Droppy\work\gallery";

    /// <summary>True in the child (the parent decided before starting it), and in the parent only when the owner opted in.</summary>
    internal static bool Enabled => WindowManagerNativeChildProtocol.IsChild || Environment.GetEnvironmentVariable(EnableVariable) == "1";

    /// <summary>The parent's side: the folder the PNGs go to when the gallery is on, otherwise null.</summary>
    internal static string? RequestedDirectory() =>
        Environment.GetEnvironmentVariable(EnableVariable) != "1" ? null
            : Path.GetFullPath(Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } custom ? custom : DefaultDirectory);

    /// <summary>The child's side: the folder the parent's request names.</summary>
    internal static string OutputFolder()
    {
        var run = Environment.GetEnvironmentVariable(WindowManagerNativeChildProtocol.DirectoryVariable)
            ?? throw new InvalidOperationException("The gallery's output folder is only known inside the native child.");
        var request = WindowManagerNativeChildProtocol.Read<WindowManagerNativeRequest>(run, "request.json");
        var folder = request.GalleryDirectory ?? throw new InvalidOperationException("The parent did not enable the gallery (MARROW_GALLERY=1).");
        System.IO.Directory.CreateDirectory(folder);
        return folder;
    }
}
