using System.Text;
using System.Text.Json;
using Marrow.Services.Port;

namespace Marrow.App.Tests.Fixtures;

// Independent literal expectations. The asymmetric inputs are test choices, not product defaults.
// Geometry follows WindowManager.swift:67-70 at ED-NEW-settingsinapp-3's first-launch size; storage is ED-006 and ED-156's Windows format.
// OD-0151: Settings is a panel inside the main window, so the frame of the window it is in is the main one (SavedMain) in both phases.
internal static class WindowManagerRestartExpectations
{
    internal const string FrameName = "DroppyCodeMainWindow";
    internal static readonly WindowManagerRestartFrame MainAfterMove = new(2345, 6789, 1325, 1178);
    internal static readonly WindowManagerRestartFrame SavedMain = new(2345, 6789, 1453, 973);

    // SettingsStore.cs:241-301: ordinal keys, dict, real dimensions, integer pixel coordinates.
    private const string SavedFrameJson =
        "{\"dict\":{\"height\":{\"real\":973},\"width\":{\"real\":1453},\"x\":{\"integer\":2345},\"y\":{\"integer\":6789}}}";

    internal static void AssertStoredFrame(SettingsStore store, WindowManagerRestartFrame expected)
    {
        var frame = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(store.Object(FrameName));
        Assert.Equal(4, frame.Count);
        Assert.Equal((long)expected.X, Assert.IsType<long>(frame["x"]));
        Assert.Equal((long)expected.Y, Assert.IsType<long>(frame["y"]));
        Assert.Equal(expected.Width, Assert.IsType<double>(frame["width"]));
        Assert.Equal(expected.Height, Assert.IsType<double>(frame["height"]));
    }

    internal static void AssertSerializedFrame(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        var entry = Assert.Single(document.RootElement.EnumerateObject(), property => property.Name == FrameName);
        Assert.Equal(Encoding.UTF8.GetBytes(SavedFrameJson), Encoding.UTF8.GetBytes(entry.Value.GetRawText()));
    }

    internal static string[] Events(int phase) => phase switch
    {
        1 => ["phase-authorized", "store-empty", "main-shown", "main-position-saved", "main-size-saved",
            "settings-panel-shown", "settings-store-unchanged", "windows-hidden",
            "store-synchronized", "headless-disposed", "phase-returning"],
        2 => ["phase-authorized", "store-bytes-read", "store-reloaded", "main-restored", "settings-panel-shown",
            "settings-store-unchanged", "windows-hidden", "store-synchronized", "headless-disposed", "phase-returning"],
        _ => throw new InvalidOperationException("The restart proof has exactly two phases."),
    };
}

internal sealed record WindowManagerRestartFrame(int X, int Y, double Width, double Height);
