using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-19, Native tests only (the lead runs them alone, with focus, on a quiet desktop): a window to type into and the input a person makes,
/// sent the way Windows sends it (SendInput), so a low-level hook hears it. Nothing here is used by a test that runs without a desktop.
/// </summary>
internal static class TeachNativeDesk
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint KeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    /// <summary>A real Notepad on a temporary file, in front. Dispose closes it.</summary>
    internal sealed class Notepad : IDisposable
    {
        private readonly Process process;
        private readonly string file;

        public Notepad(string text)
        {
            file = Path.Combine(Path.GetTempPath(), "as19-native-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
            File.WriteAllText(file, text);
            process = Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true })!;
            process.WaitForInputIdle(10_000);
            for (var wait = 0; wait < 100 && process.MainWindowHandle == 0; wait++)
            {
                Thread.Sleep(100);
                process.Refresh();
            }
            SetForegroundWindow(process.MainWindowHandle);
            Thread.Sleep(500);
        }

        public nint Handle => process.MainWindowHandle;

        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }
            process.Dispose();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>The pointer to a point and a left click there.</summary>
    internal static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Send(Mouse(MouseLeftDown), Mouse(MouseLeftUp));
    }

    /// <summary>Each key pressed and let go, in order.</summary>
    internal static void Press(params ushort[] keys)
    {
        foreach (var key in keys) Send(Key(key, 0), Key(key, KeyUp));
    }

    /// <summary>A key pressed with Ctrl held.</summary>
    internal static void PressWithControl(ushort key) => Send(Key(0x11, 0), Key(key, 0), Key(key, KeyUp), Key(0x11, KeyUp));

    private static Input Mouse(uint flags) => new() { Type = InputMouse, Union = new InputUnion { Mouse = new MouseInput { Flags = flags } } };

    private static Input Key(ushort vk, uint flags) => new() { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = flags } } };

    private static void Send(params Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
}
