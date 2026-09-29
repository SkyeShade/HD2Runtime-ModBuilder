<#
Portable DEVELOPMENT build of HD2Runtime ModBuilder, for trying features against an unreleased HD2Runtime SDK.
Not a release: no tag, no version bump, no update manifests, no checksum publication. Output goes to artifacts\dev\ (ignored by git).

The package contains a launcher that starts ModBuilder with --sdk-path <HD2Runtime>\sdk and a separate data root (dev-data\ next to the
executable), so real projects in %LOCALAPPDATA%\HD2RuntimeGUI are never validated against unreleased metadata.

  .\scripts\publish-dev.ps1 [-SdkPath <HD2Runtime>\sdk]
#>
param(
    [string]$SdkPath = (Join-Path (Split-Path $PSScriptRoot -Parent) '..\HD2Runtime\sdk'),
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$SdkPath = [IO.Path]::GetFullPath($SdkPath)
if (-not (Test-Path (Join-Path $SdkPath 'metadata.json'))) { throw "No HD2Runtime SDK at $SdkPath (metadata.json missing)." }
$project = Join-Path $repoRoot 'HD2RuntimeGUI\HD2RuntimeGUI.csproj'
$updaterProject = Join-Path $repoRoot 'HD2RuntimeGUI.Updater\HD2RuntimeGUI.Updater.csproj'
$commit = (& git -C $repoRoot rev-parse --short=7 HEAD).Trim()
$dirty = [bool](& git -C $repoRoot status --porcelain)
$runtimeCommit = try { (& git -C (Split-Path $SdkPath -Parent) rev-parse --short=7 HEAD).Trim() } catch { 'unknown' }
$runtimeDirty = try { [bool](& git -C (Split-Path $SdkPath -Parent) status --porcelain -- sdk) } catch { $false }
$name = "HD2Runtime-ModBuilder-dev-$commit$(if ($dirty) { '-dirty' })-win-x64"
$devRoot = Join-Path $repoRoot 'artifacts\dev'
$stage = Join-Path $devRoot $name; $zip = "$stage.zip"
foreach ($path in @($stage, $zip)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host "Publishing development build $name ($Configuration, win-x64, self-contained)..."
& dotnet restore $project --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
& dotnet publish $project -c $Configuration -f net10.0-windows10.0.19041.0 -r win-x64 `
    --self-contained true --no-restore --nologo -o $stage `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
$updaterOut = Join-Path ([IO.Path]::GetTempPath()) ("modbuilder-updater-" + [Guid]::NewGuid().ToString('N'))
& dotnet publish $updaterProject -c $Configuration -r win-x64 --nologo -o $updaterOut
if ($LASTEXITCODE -ne 0) { throw "Updater publish failed with exit code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $updaterOut 'HD2RuntimeModBuilder.Updater.exe') -Destination $stage
Remove-Item -LiteralPath $updaterOut -Recurse -Force
Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File -Recurse | Remove-Item -Force
if (-not (Test-Path (Join-Path $stage 'HD2RuntimeModBuilder.exe'))) { throw 'Published executable was not found.' }

# Launcher: the local SDK and a separate data root.
$launcher = @"
@echo off
rem HD2Runtime ModBuilder DEVELOPMENT build ($commit) - not a release.
rem Uses the unreleased HD2Runtime SDK below (override with HD2RUNTIME_SDK_PATH) and its own data folder (dev-data).
set "SDK=$SdkPath"
if not "%HD2RUNTIME_SDK_PATH%"=="" set "SDK=%HD2RUNTIME_SDK_PATH%"
set "HD2RUNTIMEGUI_DATA_ROOT=%~dp0dev-data"
start "" "%~dp0HD2RuntimeModBuilder.exe" --sdk-path "%SDK%"
"@
Set-Content -LiteralPath (Join-Path $stage 'Launch ModBuilder DEV (local HD2Runtime SDK).cmd') -Value $launcher -Encoding ascii
$readme = @"
HD2Runtime ModBuilder - DEVELOPMENT BUILD (not a release)

ModBuilder commit: $commit$(if ($dirty) { ' (uncommitted changes)' })
Built: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm'))
HD2Runtime SDK: $SdkPath (HD2Runtime $runtimeCommit$(if ($runtimeDirty) { ', with uncommitted sdk changes' }); reports its own version)

Start it with "Launch ModBuilder DEV (local HD2Runtime SDK).cmd". The launcher:
  - binds the unreleased local HD2Runtime SDK (--sdk-path), which is validated in memory and never cached;
  - keeps projects, SDK cache and settings in dev-data\ next to this file, not in %LOCALAPPDATA%\HD2RuntimeGUI.
Starting HD2RuntimeModBuilder.exe directly uses your normal data folder and the published SDK.

What is new here: Enemies and Structures authoring (hd2.enemy / hd2.structure). See docs/enemy-authoring.md in the repository.
The in-app version still reads 1.3.1; this build is identified by its commit.
"@
Set-Content -LiteralPath (Join-Path $stage 'DEV-BUILD.txt') -Value $readme -Encoding utf8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Development build: $stage"
Write-Host "Executable: $(Join-Path $stage 'HD2RuntimeModBuilder.exe')"
Write-Host "Launcher: $(Join-Path $stage 'Launch ModBuilder DEV (local HD2Runtime SDK).cmd')"
Write-Host "ZIP: $zip ($([Math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
