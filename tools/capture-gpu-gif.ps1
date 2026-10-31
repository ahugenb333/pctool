<#
.SYNOPSIS
  Records the 1440p GPU stress scene and writes a small GIF for the README.
.DESCRIPTION
  Dumps preview frames with pctool-cli gpu-frames, then encodes them with ffmpeg
  (downloaded to the temp folder if it is not already on PATH). The GIF is a short
  loop at 12 fps, not a video, so it stays small enough to keep in the repo.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/capture-gpu-gif.ps1
#>
param(
    [string]$OutFile = "docs/screenshots/readme/gpu-stress.gif",
    [int]$Count = 36,
    [int]$Fps = 12,
    [int]$Width = 720
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $root "PcTool.sln"))) { $root = (Get-Location).Path }
Set-Location $root

function Find-Ffmpeg {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $cached = Join-Path $env:TEMP "pctool-ffmpeg\ffmpeg.exe"
    if (Test-Path $cached) { return $cached }

    $zip = Join-Path $env:TEMP "pctool-ffmpeg.zip"
    $extract = Join-Path $env:TEMP "pctool-ffmpeg-extract"
    Write-Host "ffmpeg not on PATH; downloading a portable build"
    Invoke-WebRequest -Uri "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -OutFile $zip
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive $zip $extract
    $exe = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    New-Item -ItemType Directory -Force -Path (Split-Path $cached) | Out-Null
    Copy-Item $exe.FullName $cached
    return $cached
}

$frames = Join-Path $env:TEMP "pctool-gpu-frames"
if (Test-Path $frames) { Remove-Item $frames -Recurse -Force }
New-Item -ItemType Directory -Force -Path $frames | Out-Null

dotnet build tools/PcTool.Cli -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "CLI build failed" }
dotnet run --project tools/PcTool.Cli -c Release --no-build -- gpu-frames --out $frames --count $Count --fps $Fps
if ($LASTEXITCODE -ne 0) { throw "frame capture failed" }

$ffmpeg = Find-Ffmpeg
$out = Join-Path $root $OutFile
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
$palette = Join-Path $env:TEMP "pctool-gpu-palette.png"
$filter = "fps=$Fps,scale=${Width}:-1:flags=lanczos"
& $ffmpeg -y -framerate $Fps -i (Join-Path $frames "frame_%03d.bmp") -vf "$filter,palettegen=stats_mode=diff" -update 1 $palette
if ($LASTEXITCODE -ne 0) { throw "palette generation failed" }
& $ffmpeg -y -framerate $Fps -i (Join-Path $frames "frame_%03d.bmp") -i $palette -lavfi "$filter [x]; [x][1:v] paletteuse=dither=bayer:bayer_scale=3" $out
if ($LASTEXITCODE -ne 0) { throw "gif encode failed" }

$bytes = (Get-Item $out).Length
Write-Host ("saved {0} ({1:N0} KB)" -f $out, ($bytes / 1KB))
