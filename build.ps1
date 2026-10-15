# Builds dist\BrightnessGlass.exe with the C# compiler that ships with .NET Framework 4.x (no SDK needed).
#
#   .\build.ps1                  version from git: tag vX.Y.Z -> X.Y.Z, later commits -> X.Y.Z-dev.N+sha
#   .\build.ps1 -Version 1.2.3   explicit version
param(
    [string]$Version,
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# returns git's output, or nothing if git fails (no tags/commits yet); stderr must not trip 'Stop' on PS 5.1
function Invoke-Git {
    $ErrorActionPreference = 'SilentlyContinue'
    $out = & git.exe -C $root @args 2>$null
    if ($LASTEXITCODE -eq 0) { $out }
}

if (-not $Version) {
    $desc = Invoke-Git describe --tags --match 'v[0-9]*' --long
    if ($desc -match '^v(\d+\.\d+\.\d+)-(\d+)-g([0-9a-f]+)$') {
        $Version = if ($Matches[2] -eq '0') { $Matches[1] } else { "$($Matches[1])-dev.$($Matches[2])+$($Matches[3])" }
    } else {
        $count = Invoke-Git rev-list --count HEAD; if (-not $count) { $count = 0 }
        $sha = Invoke-Git rev-parse --short HEAD
        $Version = "0.0.0-dev.$count" + $(if ($sha) { "+$sha" } else { '' })
    }
}
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-dev\.(\d+))?') { throw "Invalid version '$Version' (expected X.Y.Z)" }
$build = if ($Matches[4]) { [Math]::Min([int]$Matches[4], 65534) } else { 0 }
$numeric = "$($Matches[1]).$($Matches[2]).$($Matches[3]).$build"   # AssemblyVersion must be numeric

$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $obj, $OutDir | Out-Null
$year = (Get-Date).Year
@"
using System.Reflection;
[assembly: AssemblyTitle("BrightnessGlass")]
[assembly: AssemblyProduct("BrightnessGlass")]
[assembly: AssemblyDescription("Brightness control for laptop panel and DDC/CI monitors")]
[assembly: AssemblyCopyright("Copyright (c) $year Sascha Bauer, MIT License")]
[assembly: AssemblyVersion("$numeric")]
[assembly: AssemblyFileVersion("$numeric")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content (Join-Path $obj 'AssemblyInfo.g.cs') -Encoding UTF8

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe from .NET Framework 4.x not found' }

$exe = Join-Path $OutDir 'BrightnessGlass.exe'
$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName) + (Join-Path $obj 'AssemblyInfo.g.cs')
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu `
    "/win32icon:$(Join-Path $root 'assets\app.ico')" "/out:$exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Management.dll /r:System.Device.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
"$hash  BrightnessGlass.exe" | Set-Content (Join-Path $OutDir 'BrightnessGlass.exe.sha256') -Encoding ASCII

Write-Host "Built $exe  version $Version ($numeric)"
if ($env:GITHUB_OUTPUT) { "version=$Version" | Add-Content $env:GITHUB_OUTPUT }
