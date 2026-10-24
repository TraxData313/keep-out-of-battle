# Builds Keep Out of Battle and installs it into the game's Modules folder as the DEV module.
# Usage: powershell -ExecutionPolicy Bypass -File tools\deploy.ps1 [-Configuration Release] [-GameFolder <path>]
#
# The dev install wears its own identity - folder and Id "KeepOutOfBattle.Dev", shown as
# "Keep Out of Battle (dev)" in the launcher. Same convention as SmartSteward.Dev /
# ImmersiveAI.Dev: it can sit beside a Steam Workshop copy and be picked deliberately (enable only
# ONE of the two). package.ps1 keeps the real identity for Workshop uploads.
#
# (ASCII only in this file: Windows PowerShell 5.1 reads a BOM-less script as ANSI.)
param(
    [string]$Configuration = "Release",
    # Empty = read GameFolder from Directory.Build.props (and its git-ignored .user override).
    [string]$GameFolder = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleName = "KeepOutOfBattle"
$moduleProject = Join-Path $repoRoot "src\$moduleName\$moduleName.csproj"

if (-not $GameFolder) {
    $GameFolder = "$(& dotnet msbuild $moduleProject -nologo -getProperty:GameFolder)".Trim()
    if ($LASTEXITCODE -ne 0 -or -not $GameFolder) { throw "Could not read GameFolder from Directory.Build.props." }
}
if (-not (Test-Path (Join-Path $GameFolder "Modules\Native\SubModule.xml"))) {
    throw "No Bannerlord install at '$GameFolder' - set GameFolder in Directory.Build.props.user or pass -GameFolder."
}

$moduleDir = Join-Path $GameFolder "Modules\$moduleName.Dev"
$binDir = Join-Path $moduleDir "bin\Win64_Shipping_Client"

# The game holds module DLLs from startup until it quits, main menu included.
if (Test-Path $binDir) {
    foreach ($dll in Get-ChildItem $binDir -Filter *.dll) {
        try { [System.IO.File]::Open($dll.FullName, 'Open', 'ReadWrite', 'None').Dispose() }
        catch { throw "$($dll.Name) is locked - Bannerlord is running. Quit the game, then deploy again." }
    }
}

# DeployToGame=false: the csproj's own deploy target installs under the REAL identity; the dev
# install is laid out here instead.
dotnet build $moduleProject -c $Configuration -p:DeployToGame=false
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (Test-Path $binDir) { Remove-Item $binDir -Recurse -Force }
New-Item -ItemType Directory -Force $binDir | Out-Null
$outDir = Join-Path $repoRoot "module\bin\Win64_Shipping_Client"
Copy-Item (Join-Path $outDir "$moduleName.dll") $binDir -Force
$pdb = Join-Path $outDir "$moduleName.pdb"
if (Test-Path $pdb) { Copy-Item $pdb $binDir -Force }

# The GUI files (brushes for the picker's health bars), mirrored so a removed file goes too.
$guiDir = Join-Path $moduleDir "GUI"
if (Test-Path $guiDir) { Remove-Item $guiDir -Recurse -Force }
Copy-Item (Join-Path $repoRoot "module\GUI") $guiDir -Recurse -Force

# The manifest, re-badged as the dev module. Only the FIRST Id/Name (the module's own) change.
$manifest = Get-Content (Join-Path $repoRoot "module\SubModule.xml") -Raw
$devManifest = ([regex]"<Id value=""$moduleName"" />").Replace($manifest, "<Id value=""$moduleName.Dev"" />", 1)
$devManifest = ([regex]'<Name value="Keep Out of Battle" />').Replace($devManifest, '<Name value="Keep Out of Battle (dev)" />', 1)
if ($devManifest -notmatch "<Id value=""$moduleName.Dev"" />" -or $devManifest -notmatch '<Name value="Keep Out of Battle \(dev\)" />') {
    throw "SubModule.xml no longer matches the expected Id/Name lines - update deploy.ps1."
}
Set-Content (Join-Path $moduleDir "SubModule.xml") $devManifest -Encoding utf8

Write-Host "Deployed to $moduleDir as 'Keep Out of Battle (dev)' - enable it in the launcher."
