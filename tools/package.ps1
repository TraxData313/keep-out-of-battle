# Builds the mod and assembles a CLEAN module layout under dist\KeepOutOfBattle (the folder
# the Steam Workshop uploader points at), then zips it as dist\KeepOutOfBattle_v<version>.zip
# with a top-level KeepOutOfBattle\ folder - the file for the GitHub release. The version is
# read from module\SubModule.xml. No .pdb is shipped. dist\ is gitignored.
# Usage: powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-Configuration Release] [-Force]
param(
    [string]$Configuration = "Release",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot "dist"
$moduleName = "KeepOutOfBattle"
$moduleDir = Join-Path $distRoot $moduleName
$binDir = Join-Path $moduleDir "bin\Win64_Shipping_Client"

# The version stamp comes from the manifest, so the zip name always tells the truth.
[xml]$manifest = Get-Content (Join-Path $repoRoot "module\SubModule.xml")
$version = $manifest.Module.Version.value -replace '[^\w\.\-]', ''
if (-not $version) { throw "No <Version> in module\SubModule.xml." }

# Refuse to overwrite the zip of a release that may already be live, unless asked (-Force).
$zipPath = Join-Path $distRoot "$($moduleName)_$version.zip"
if ((Test-Path $zipPath) -and -not $Force) {
    throw "dist\$($moduleName)_$version.zip already exists - bump the version in module\SubModule.xml, or pass -Force to overwrite."
}

# A clean slate is the whole point of packaging. DeployToGame=false: packaging never touches the game.
if (Test-Path $moduleDir) { Remove-Item $moduleDir -Recurse -Force }
dotnet build (Join-Path $repoRoot "src\$moduleName\$moduleName.csproj") -c $Configuration -p:DeployToGame=false
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

New-Item -ItemType Directory -Force $binDir | Out-Null
Copy-Item (Join-Path $repoRoot "module\SubModule.xml") $moduleDir -Force
Copy-Item (Join-Path $repoRoot "module\bin\Win64_Shipping_Client\$moduleName.dll") $binDir -Force

# Zip with forward-slash entry names (PowerShell 5.1's Compress-Archive writes backslashes, which some
# non-Windows unzippers turn into flat file names).
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $moduleDir -Recurse -File | ForEach-Object {
        $entry = $moduleName + "/" + $_.FullName.Substring($moduleDir.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }

Write-Host "Packaged $version to $moduleDir"
Write-Host "Zip: $zipPath"
Write-Host "Workshop upload: point the uploader at the dist\$moduleName folder."
