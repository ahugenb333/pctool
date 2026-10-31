<#
.SYNOPSIS
  Captures README screenshots of every PcTool page into docs/screenshots.
.DESCRIPTION
  Launches the app once per page with --page, presses page buttons through UI Automation, and captures
  the window with PrintWindow, so it works while other windows cover it.
  Build first: dotnet build src/PcTool.App -c Release
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/capture-screenshots.ps1 -OutDir docs/screenshots/readme -RunBenchmark -ClearBenchmarkHistory
#>
param(
    [string]$Exe = "src/PcTool.App/bin/Release/net8.0-windows/PcTool.exe",
    [string]$OutDir = "docs/screenshots",
    [int]$Width = 1400,
    [int]$Height = 900,
    # Runs a full benchmark first so the Benchmarks page has results (about a minute).
    [switch]$RunBenchmark,
    # Deletes local benchmark history so README shots do not show stale runs from before calibration fixes.
    [switch]$ClearBenchmarkHistory
)

if ($ClearBenchmarkHistory) {
    $hist = Join-Path $env:LOCALAPPDATA "PcTool\benchmarks.json"
    if (Test-Path $hist) {
        Remove-Item $hist -Force
        Write-Host "cleared $hist"
    }
}

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class WindowCapture
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);

    public static void Save(IntPtr hwnd, string path)
    {
        RECT outer, visible;
        GetWindowRect(hwnd, out outer);
        // DWMWA_EXTENDED_FRAME_BOUNDS excludes the invisible resize border.
        if (DwmGetWindowAttribute(hwnd, 9, out visible, Marshal.SizeOf(typeof(RECT))) != 0) visible = outer;
        int w = outer.Right - outer.Left, h = outer.Bottom - outer.Top;
        using (var full = new Bitmap(w, h))
        {
            using (var g = Graphics.FromImage(full))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
            }
            var crop = new Rectangle(visible.Left - outer.Left, visible.Top - outer.Top,
                visible.Right - visible.Left, visible.Bottom - visible.Top);
            using (var cropped = full.Clone(crop, full.PixelFormat)) cropped.Save(path, ImageFormat.Png);
        }
    }
}
"@

$AE = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$out = (New-Item -ItemType Directory -Force -Path $OutDir).FullName

function Find-Control($root, $controlType, [string]$name = $null) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    if ($name) {
        $cond = New-Object System.Windows.Automation.AndCondition($cond,
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    }
    for ($i = 0; $i -lt 40; $i++) {
        $el = $root.FindFirst($Tree::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    }
    throw "Control '$name' ($($controlType.ProgrammaticName)) not found"
}

function Press($root, [string]$name) {
    $button = Find-Control $root ([System.Windows.Automation.ControlType]::Button) $name
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Type-Text($root, [string]$text) {
    $edit = Find-Control $root ([System.Windows.Automation.ControlType]::Edit)
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Capture([string]$page, [string]$file, [scriptblock]$prepare = $null, [int]$settle = 2) {
    $proc = Start-Process -FilePath $Exe -ArgumentList "--page", $page -PassThru
    try {
        for ($i = 0; $i -lt 60 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $proc.Refresh() }
        $hwnd = $proc.MainWindowHandle
        [WindowCapture]::MoveWindow($hwnd, 40, 40, $Width, $Height, $true) | Out-Null
        $window = $AE::FromHandle($hwnd)
        Start-Sleep -Seconds 6 # hardware profiling
        if ($prepare) { & $prepare $window }
        Start-Sleep -Seconds $settle
        [WindowCapture]::Save($hwnd, (Join-Path $out $file))
        Write-Host "saved $file"
    }
    finally {
        if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2 }
        if (-not $proc.HasExited) { $proc.Kill() }
    }
}

Capture "Overview" "overview.png" -settle 3
if ($RunBenchmark) {
    Capture "Benchmarks" "benchmarks.png" { param($w) Press $w "Run benchmarks"; Start-Sleep -Seconds 75 }
} else {
    Capture "Benchmarks" "benchmarks.png"
}
Capture "Games" "games.png"
Capture "Diagnostics" "diagnostics.png" { param($w) Press $w "Run diagnostics" } -settle 8
Capture "Compatibility" "compatibility.png" { param($w) Type-Text $w "9070" }
Capture "Upgrades" "upgrades.png"
Capture "Settings" "settings.png"
