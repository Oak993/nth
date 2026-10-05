using System.Text;

namespace Marrow.App.Views.Terminal.Port;

/// <summary>SwiftTerm Terminal.swift:677-725 <c>Terminal.MouseMode</c>: what mouse events the running program asked to be told of (DECSET 9, 1000, 1002, 1003).</summary>
internal enum TerminalMouseMode
{
    Off,
    X10,
    Vt200,
    ButtonEventTracking,
    AnyEvent,
}

/// <summary>SwiftTerm Terminal.swift:568-587 <c>MouseProtocolEncoding</c>: how a reported event is written (DECSET 1005, 1006, 1015, 1016).</summary>
internal enum TerminalMouseEncoding
{
    X10,
    Utf8,
    Sgr,
    Urxvt,
    SgrPixel,
}

/// <summary>The program's mouse request as the surface reads it when an event arrives: the mode, the encoding, and XTSHIFTESCAPE.</summary>
internal readonly record struct TerminalMouseState(TerminalMouseMode Mode, TerminalMouseEncoding Encoding, bool ShiftCapture);

/// <summary>
/// PORT-ONLY twin of SwiftTerm 1.20.0's mouse reporting (Terminal.swift:677-725 the mode's questions, :6916-7000 the encoding of a button and of an event).
/// A line cite below is to Sources/SwiftTerm/Terminal.swift of the tag the app pins (project.yml:8-9), read from the copy under
/// E:\Droppy\wt\cx\terminal\_evidence\SwiftTerm-v1.20.0 (ED Q128 authorises the transcription).
/// </summary>
internal static class TerminalMouse
{
    /// <summary>:697-700 <c>sendButtonPress</c>.</summary>
    public static bool SendsButtonPress(this TerminalMouseMode mode) => mode is TerminalMouseMode.Vt200 or TerminalMouseMode.ButtonEventTracking or TerminalMouseMode.AnyEvent;

    /// <summary>:703-706 <c>sendButtonRelease</c>: every mode but off (including X10, which never reports a press: SwiftTerm's own rule).</summary>
    public static bool SendsButtonRelease(this TerminalMouseMode mode) => mode != TerminalMouseMode.Off;

    /// <summary>:709-712 <c>sendButtonTracking</c>.</summary>
    public static bool SendsButtonTracking(this TerminalMouseMode mode) => mode is TerminalMouseMode.ButtonEventTracking or TerminalMouseMode.AnyEvent;

    /// <summary>:715-718 <c>sendMotionEvent</c>.</summary>
    public static bool SendsMotion(this TerminalMouseMode mode) => mode == TerminalMouseMode.AnyEvent;

    /// <summary>:721-724 <c>sendsModifiers</c>.</summary>
    public static bool SendsModifiers(this TerminalMouseMode mode) => mode is TerminalMouseMode.Vt200 or TerminalMouseMode.ButtonEventTracking or TerminalMouseMode.AnyEvent;

    /// <summary>
    /// :6916-6946 <c>encodeButton</c>: button 0, 1, 2 are left, middle, right; 4 and 5 are the wheel up and down (64, 65); a release is 3. Shift 4, meta 8 and
    /// control 16 are added in the modes that send modifiers (the caller passes Alt as meta, MacTerminalView.swift:2757-2759: <c>.option</c>).
    /// </summary>
    public static int EncodeButton(TerminalMouseMode mode, int button, bool release, bool shift, bool meta, bool control)
    {
        int value;
        if (release) value = 3;
        else
        {
            value = button switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                4 => 64,
                5 => 65,
                _ => 0,
            };
        }
        if (mode.SendsModifiers())
        {
            if (shift) value |= 4;
            if (meta) value |= 8;
            if (control) value |= 16;
        }
        return value;
    }

    /// <summary>
    /// :6962-6993 <c>sendEvent</c>: the bytes written to the program for an event with <paramref name="buttonFlags"/> (<see cref="EncodeButton"/>) at the
    /// zero-based cell (<paramref name="x"/>, <paramref name="y"/>) and, for the pixel encoding, the pixel (<paramref name="pixelX"/>, <paramref name="pixelY"/>).
    /// </summary>
    public static byte[] EncodeEvent(TerminalMouseEncoding encoding, int buttonFlags, int x, int y, int pixelX, int pixelY)
    {
        var csi = new byte[] { 0x1b, 0x5b }; // EscapeSequences.swift:63 CSI, 7-bit
        switch (encoding)
        {
            case TerminalMouseEncoding.X10: // :6966-6967
                return [.. csi, (byte)'M', (byte)Math.Min(buttonFlags + 32, 255), (byte)Math.Min(32 + x + 1, 255), (byte)Math.Min(32 + y + 1, 255)];
            case TerminalMouseEncoding.Sgr: // :6968-6971
                return Sgr(csi, buttonFlags, x + 1, y + 1);
            case TerminalMouseEncoding.SgrPixel: // :6972-6976
                return Sgr(csi, buttonFlags, pixelX, pixelY);
            case TerminalMouseEncoding.Urxvt: // :6978-6979
                return [.. csi, .. Encoding.UTF8.GetBytes($"{buttonFlags + 32};{x + 1};{y + 1}M")];
            default: // :6980-6985 utf8
                var buffer = new List<byte> { (byte)'M' };
                EncodeUtf(buffer, buttonFlags + 32);
                EncodeUtf(buffer, x + 33);
                EncodeUtf(buffer, y + 33);
                return [.. csi, .. buffer];
        }
    }

    /// <summary>:6996-6999 <c>sendMotion</c>: an event whose button flags carry the motion bit (32).</summary>
    public static byte[] EncodeMotion(TerminalMouseEncoding encoding, int buttonFlags, int x, int y, int pixelX, int pixelY) =>
        EncodeEvent(encoding, buttonFlags + 32, x, y, pixelX, pixelY);

    private static byte[] Sgr(byte[] csi, int buttonFlags, int first, int second)
    {
        var isRelease = (buttonFlags & 3) == 3 && (buttonFlags & 32) == 0; // :6969, :6973
        var flags = isRelease ? buttonFlags & ~3 : buttonFlags;
        return [.. csi, .. Encoding.UTF8.GetBytes($"<{flags};{first};{second}{(isRelease ? 'm' : 'M')}")];
    }

    // :6879-6892 encodeMouseUtf.
    private static void EncodeUtf(List<byte> data, int value)
    {
        if (value == 2047)
        {
            data.Add(0);
            return;
        }
        if (value < 127)
        {
            data.Add((byte)value);
            return;
        }
        var clamped = value > 2047 ? 2047 : value;
        data.Add((byte)(0xc0 | (clamped >> 6)));
        data.Add((byte)(0x80 | (clamped & 0x3f)));
    }
}
