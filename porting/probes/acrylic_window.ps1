# Probe: render the REAL Windows 11 Acrylic system backdrop behind a window the size of
# Droppy Code's main window, over the live desktop, and capture the result.
#
# Not a simulation. It uses DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE,
# DWMSBT_TRANSIENTWINDOW), the best material Windows offers an ordinary desktop app
# without screen capture. It is the honest upper bound for the "no capture" option.
#
# Source-derived geometry (SOURCE FACTs):
#   main window   1320 x 860   WindowManager.swift:19
#   corner radius 26           Chrome.swift:8 windowCornerRadius
#
# First attempt failed silently: Form.TransparencyKey makes the window LAYERED
# (WS_EX_LAYERED), and DWM system backdrops do not apply to layered windows, so all three
# captures came back byte-identical to the bare desktop. This version drops the
# transparency key and proves the harness with a control case before trusting any output.

$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Probe {
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int L, R, T, B; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    public const int DARK = 20, CORNER = 33, BACKDROP = 38;
    public const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000;

    public static bool IsLayered(IntPtr hwnd) {
        return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0;
    }

    // Returns the HRESULT of the backdrop call so a failure cannot pass unnoticed.
    public static int Apply(IntPtr hwnd, int backdropType) {
        int d = 1;  DwmSetWindowAttribute(hwnd, DARK, ref d, 4);
        int c = 2;  DwmSetWindowAttribute(hwnd, CORNER, ref c, 4);   // DWMWCP_ROUND
        MARGINS m = new MARGINS(); m.L = -1; m.R = -1; m.T = -1; m.B = -1;
        DwmExtendFrameIntoClientArea(hwnd, ref m);
        int b = backdropType;
        return DwmSetWindowAttribute(hwnd, BACKDROP, ref b, 4);
    }
}
'@ -Language CSharp

$W = 1320
$H = 860
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$X = [int](($screen.Width  - $W) / 2)
$Y = [int](($screen.Height - $H) / 2)
Write-Host "screen $($screen.Width)x$($screen.Height); window ${W}x${H} at $X,$Y"

function Pump([int]$ms) {
    $end = (Get-Date).AddMilliseconds($ms)
    while ((Get-Date) -lt $end) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 40
    }
}

function Grab([string]$name) {
    $path = Join-Path $out $name
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size $W, $H))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $h = (Get-FileHash $path -Algorithm SHA256).Hash.Substring(0,16)
    Write-Host ("  {0,-40} sha {1}" -f $name, $h)
    return $h
}

# 1. Bare desktop, before any window covers it.
Write-Host 'capturing:'
$hDesktop = Grab 'desktop_plain.png'

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point $X, $Y
$form.ClientSize = New-Object System.Drawing.Size $W, $H
$form.ShowInTaskbar = $false
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::Magenta   # control case: unmistakable
$form.Show()
$form.Activate()
Pump 900

# 2. CONTROL: a solid magenta window. If this matches the bare desktop, the harness is
#    broken and nothing below can be trusted.
$hControl = Grab 'control_solid.png'
if ($hControl -eq $hDesktop) {
    Write-Host 'HARNESS BROKEN: the control window did not render. Aborting.' -ForegroundColor Red
    $form.Close(); $form.Dispose()
    exit 2
}
Write-Host '  control differs from desktop: the harness renders.' -ForegroundColor Green

# 3. Real Acrylic. Black client pixels become transparent under an extended frame.
$form.BackColor = [System.Drawing.Color]::Black
$hr = [Probe]::Apply($form.Handle, 3)   # DWMSBT_TRANSIENTWINDOW = Desktop Acrylic
Write-Host ("  DwmSetWindowAttribute(SYSTEMBACKDROP_TYPE=3) hr=0x{0:X8}; layered={1}" -f $hr, [Probe]::IsLayered($form.Handle))
$form.Refresh()
Pump 1500
$hAcrylic = Grab 'acrylic_real.png'
if ($hAcrylic -eq $hControl) { Write-Host '  WARNING: acrylic capture identical to the control' -ForegroundColor Yellow }
if ($hAcrylic -eq $hDesktop) { Write-Host '  WARNING: acrylic capture identical to the bare desktop' -ForegroundColor Yellow }

# 4. Mica, for comparison: the other system material.
$hr2 = [Probe]::Apply($form.Handle, 2)  # DWMSBT_MAINWINDOW = Mica
Write-Host ("  DwmSetWindowAttribute(SYSTEMBACKDROP_TYPE=2) hr=0x{0:X8}" -f $hr2)
$form.Refresh()
Pump 1200
[void](Grab 'mica_real.png')

# 5. The capture cost. WDA_EXCLUDEFROMCAPTURE should make the window vanish from a screen
#    grab while staying visible on screen. This is what Option A pays permanently.
[Probe]::Apply($form.Handle, 3) | Out-Null
$form.Refresh()
Pump 800
$ok = [Probe]::SetWindowDisplayAffinity($form.Handle, [Probe]::WDA_EXCLUDEFROMCAPTURE)
Write-Host "  SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) returned $ok"
$form.Refresh()
Pump 900
$hExcl = Grab 'acrylic_excluded_from_capture.png'
if ($hExcl -eq $hDesktop) {
    Write-Host '  CONFIRMED: with WDA_EXCLUDEFROMCAPTURE the window is absent from screen capture.' -ForegroundColor Green
} else {
    Write-Host '  NOT confirmed: the window still appears in the capture.' -ForegroundColor Yellow
}

[void][Probe]::SetWindowDisplayAffinity($form.Handle, [Probe]::WDA_NONE)
$form.Close()
$form.Dispose()
Write-Host 'done'
