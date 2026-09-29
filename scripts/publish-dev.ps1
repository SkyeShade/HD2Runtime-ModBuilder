<#
Portable DEVELOPMENT build of HD2Runtime ModBuilder, for trying features against an unreleased HD2Runtime SDK.
Not a release: no tag, no version bump, no update manifests, no checksum publication. Output goes to artifacts\dev\ (ignored by git).

The package contains a launcher that starts ModBuilder with --sdk-path <HD2Runtime>\sdk and a separate data root (dev-data\ next to the
executable), so real projects in %LOCALAPPDATA%\HD2RuntimeGUI are never validated against unreleased metadata.

  .\scripts\publish-dev.ps1 [-SdkPath <HD2Runtime>\sdk] [-RuntimeCommit <commit>]

-RuntimeCommit binds the SDK exactly as committed at that HD2Runtime commit instead of the working copy (for example while a Runtime
pass has uncommitted sdk/ changes this build does not read yet). The snapshot is extracted with git archive into artifacts\dev\ (the
Runtime checkout is not touched) and is never put in the package; the launcher points at it.
#>
param(
    [string]$SdkPath = (Join-Path (Split-Path $PSScriptRoot -Parent) '..\HD2Runtime\sdk'),
    [string]$Configuration = 'Release',
    [string]$RuntimeCommit
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$SdkPath = [IO.Path]::GetFullPath($SdkPath)
if (-not (Test-Path (Join-Path $SdkPath 'metadata.json'))) { throw "No HD2Runtime SDK at $SdkPath (metadata.json missing)." }
$project = Join-Path $repoRoot 'HD2RuntimeGUI\HD2RuntimeGUI.csproj'
$updaterProject = Join-Path $repoRoot 'HD2RuntimeModBuilder.Updater\HD2RuntimeModBuilder.Updater.csproj'
$commit = (& git -C $repoRoot rev-parse --short=7 HEAD).Trim()
$dirty = [bool](& git -C $repoRoot status --porcelain)
$runtimeRoot = Split-Path $SdkPath -Parent
$devRoot = Join-Path $repoRoot 'artifacts\dev'
if ($RuntimeCommit) {
    $runtimeCommit = (& git -C $runtimeRoot rev-parse --short=7 "$RuntimeCommit^{commit}").Trim()
    if ($LASTEXITCODE -ne 0) { throw "HD2Runtime commit $RuntimeCommit was not found in $runtimeRoot." }
    $snapshot = Join-Path $devRoot "hd2runtime-sdk-$runtimeCommit"
    if (Test-Path -LiteralPath $snapshot) { Remove-Item -LiteralPath $snapshot -Recurse -Force }
    New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
    $tar = Join-Path ([IO.Path]::GetTempPath()) ("hd2runtime-sdk-" + [Guid]::NewGuid().ToString('N') + '.tar')
    & git -C $runtimeRoot archive --format=tar -o $tar $runtimeCommit sdk
    if ($LASTEXITCODE -ne 0) { throw "git archive of $runtimeCommit sdk failed." }
    & tar -x -f $tar -C $snapshot; $extracted = $LASTEXITCODE; Remove-Item -LiteralPath $tar -Force
    if ($extracted -ne 0) { throw 'Extracting the SDK snapshot failed.' }
    $SdkPath = Join-Path $snapshot 'sdk'; $runtimeDirty = $false
    if (-not (Test-Path (Join-Path $SdkPath 'metadata.json'))) { throw "HD2Runtime $runtimeCommit has no sdk\metadata.json." }
} else {
    $runtimeCommit = try { (& git -C $runtimeRoot rev-parse --short=7 HEAD).Trim() } catch { 'unknown' }
    $runtimeDirty = try { [bool](& git -C $runtimeRoot status --porcelain -- sdk) } catch { $false }
    if ($runtimeDirty) { Write-Warning "HD2Runtime's sdk\ has uncommitted changes (a Runtime pass in progress). This build may refuse them; use -RuntimeCommit <commit> to bind a committed SDK." }
}
$name = "HD2Runtime-ModBuilder-dev-$commit$(if ($dirty) { '-dirty' })-win-x64"
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
HD2Runtime SDK: $SdkPath (HD2Runtime $runtimeCommit$(if ($RuntimeCommit) { ', committed snapshot' } elseif ($runtimeDirty) { ', with uncommitted sdk changes' }); reports its own version)

Start it with "Launch ModBuilder DEV (local HD2Runtime SDK).cmd". The launcher:
  - binds the unreleased local HD2Runtime SDK (--sdk-path), which is validated in memory and never cached;
  - keeps projects, SDK cache and settings in dev-data\ next to this file, not in %LOCALAPPDATA%\HD2RuntimeGUI.
Starting HD2RuntimeModBuilder.exe directly uses your normal data folder and the published SDKs (or the local SDK chosen in its
Settings > Developer panel).

What is new here: custom Lua / event scripting (src/addon.lua), projectile donors through the active projectile source (attack
outputs), Resupply, the SH-20 shield zone, the SG-20 Halt's branch-qualified fields and the Settings > Developer local SDK choice,
on top of Enemies and Structures authoring. See docs/runtime028-integration.md and docs/enemy-authoring.md in the repository.
The in-app version still reads 1.3.1; this build is identified by its commit.
"@
Set-Content -LiteralPath (Join-Path $stage 'DEV-BUILD.txt') -Value $readme -Encoding utf8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Development build: $stage"
Write-Host "Executable: $(Join-Path $stage 'HD2RuntimeModBuilder.exe')"
Write-Host "Launcher: $(Join-Path $stage 'Launch ModBuilder DEV (local HD2Runtime SDK).cmd')"
Write-Host "ZIP: $zip ($([Math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
