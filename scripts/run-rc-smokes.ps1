[CmdletBinding()]
param(
    # The packaged executable to test (default: the release candidate built by build-rc.ps1).
    [string]$App,
    [string[]]$Smokes = @('scripting', 'language', 'runtime028', 'programmable-ammo', 'projectile-builder', 'export', 'old-project'),
    [string]$RuntimeTree = (Join-Path ([IO.Path]::GetTempPath()) 'hd2runtime-0.28.0-public'),
    [int]$BasePort = 9260
)

# Runs the desktop smokes against the PACKAGED app (this is also the packaged-launch check). Each smoke gets a fresh data folder and its
# own WebView2 debugging port; results go to build\release-candidate-<version>\smokes\ (logs, screenshots, summary.json).
#   scripting    custom Lua / event scripting and the developer SDK panel (--sdk-path: the pinned SDK extracted from the fixture)
#   language     English <-> 简体中文 with an open project and an unsaved edit
#   runtime028   underbarrel, programmable ammo, mounted projectile host, event/action and localization (+ Runtime validation of its export)
#   programmable-ammo  base / alternate mode cards: add, donor, labels, reopen, remove, support + native weapons, narrow layout, zh-Hans (+ export validation)
#   projectile-builder  hosts (player, support, mounted), read-only reasons, donor rows, slots, mode presentation, LAS-98 beam
#   export       export in both languages (byte-identical) + HD2Runtime 0.28.0 validation of the export
#   old-project  a ModBuilder 1.3.1 user's data folder: projects stay on 0.27.0, rebind and export

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = (& dotnet msbuild (Join-Path $repoRoot 'HD2RuntimeGUI\HD2RuntimeGUI.csproj') -getProperty:Version -nologo | Select-Object -Last 1).Trim()
$rc = Join-Path $repoRoot "build\release-candidate-$version"
if (-not $App) { $App = Join-Path $rc "HD2Runtime-ModBuilder-$version\HD2RuntimeModBuilder.exe" }
if (-not (Test-Path $App)) { throw "Packaged app not found: $App (run scripts\build-rc.ps1 first)." }
$out = Join-Path $rc 'smokes'
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

# The pinned SDK as a local folder (for the scripting smoke's developer-SDK checks); byte-identical to the bundled SDK.
$sdkDir = Join-Path $out 'sdk-0.28.0'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $repoRoot 'HD2RuntimeGUI.Tests\Fixtures\sdk-0.28.0.zip'), $sdkDir)

# A ModBuilder 1.3.1 user's data folder: SDK 0.27.0 cached and current, and the projects 1.3.1 saved.
function Initialize-OldProjectData([string]$data) {
    $cache = Join-Path $data 'Sdk\0.27.0'; New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $repoRoot 'HD2RuntimeGUI.Tests\Fixtures\sdk-0.27.0.zip'))
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '^[^/]+\.json$') { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $cache $entry.FullName), $true) }
            if ($entry.FullName -eq 'stubs/mods/skyeshade/hd2runtime.lua') { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $cache 'hd2runtime-stubs.lua'), $true) }
        }
    } finally { $zip.Dispose() }
    Set-Content -LiteralPath (Join-Path $data 'Sdk\current.json') -Value '{"version":"0.27.0"}' -Encoding ascii
    $library = @()
    foreach ($file in Get-ChildItem (Join-Path $repoRoot 'HD2RuntimeGUI.Tests\Fixtures\projects-1.3.1') -Filter '*.hd2mod.json') {
        $project = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $dir = Join-Path $data "Projects\$($project.id)"; New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dir 'project.hd2mod.json')
        $library += [ordered]@{ id = $project.id; displayName = $project.displayName; resourceId = $project.resourceId; sdkVersion = $project.sdkVersion; modifiedAt = $project.modifiedAt }
    }
    [ordered]@{ formatVersion = 1; projects = $library } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $data 'library.json') -Encoding utf8
}

$summary = [ordered]@{}
$port = $BasePort
foreach ($smoke in $Smokes) {
    $port++
    $data = Join-Path $out "$smoke\data"; New-Item -ItemType Directory -Path $data -Force | Out-Null
    if ($smoke -eq 'old-project') { Initialize-OldProjectData $data }
    $env:HD2RUNTIMEGUI_DATA_ROOT = $data
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$port"
    $arguments = if ($smoke -eq 'scripting') { @('--sdk-path', $sdkDir) } else { @() }
    $process = if ($arguments.Count) { Start-Process -FilePath $App -ArgumentList $arguments -PassThru } else { Start-Process -FilePath $App -PassThru }
    Remove-Item Env:HD2RUNTIMEGUI_DATA_ROOT, Env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    $log = Join-Path $out "$smoke.log"
    try {
        $env:HD2GUI_CDP_PORT = "$port"; $env:HD2GUI_SCREENSHOTS = Join-Path $out 'screenshots'
        Push-Location $repoRoot
        & node (Join-Path $repoRoot "tools\$smoke-smoke.mjs") *> $log
        $exit = $LASTEXITCODE
        Pop-Location
    } finally {
        Remove-Item Env:HD2GUI_CDP_PORT, Env:HD2GUI_SCREENSHOTS -ErrorAction SilentlyContinue
        & taskkill /PID $process.Id /T /F *> $null
    }
    $result = if ($exit -eq 0) { 'PASS' } else { 'FAIL' }
    # The export smoke's ZIP is validated by HD2Runtime 0.28.0 itself.
    if ($smoke -in @('export', 'runtime028', 'programmable-ammo') -and $exit -eq 0) {
        $zip = ((Get-Content -LiteralPath $log -Raw) | Select-String -Pattern '"export":\s*"([^"]+)"').Matches[0].Groups[1].Value -replace '\\\\', '\'
        & py -3 -B (Join-Path $repoRoot 'tools\validate-exports.py') --isolation --runtime $RuntimeTree --output (Join-Path $out "$smoke-export-validation.json") $zip *>> $log
        if ($LASTEXITCODE -ne 0) { $result = 'FAIL (export validation)' }
    }
    $summary[$smoke] = $result
    Write-Host "$smoke`: $result ($log)"
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'summary.json') -Encoding utf8
if ($summary.Values | Where-Object { $_ -ne 'PASS' }) { exit 1 }
