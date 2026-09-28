[CmdletBinding()]
param(
    # Folder with the new release: HD2Runtime-ModBuilder-v<version>-win-x64.zip and modbuilder-update.json.
    [string]$Release = (Join-Path $PSScriptRoot '..\artifacts\release'),
    # Data folder copied into the sandbox (projects, SDK cache, icons); the original is only read.
    [string]$UserData = (Join-Path $env:LOCALAPPDATA 'HD2RuntimeGUI'),
    [string]$Work = (Join-Path $PSScriptRoot '..\artifacts\update-from-1.0.0'),
    [int]$DebugPort = 9280
)

# Real 1.0.0 -> new-version update in a sandbox, with no GitHub and no real installation involved:
#   1. installs the published v1.0.0 ZIP (HD2RuntimeGUI.exe) and starts it on a copy of the user data;
#   2. runs 1.0.0's own in-app update code (HD2RuntimeGUI.Core from the v1.0.0 tag, tools/update-from-1.0.0/Driver.cs)
#      against the new release: check, manifest, download, SHA-256, staging, start the staged updater, close the app;
#   3. the new release's updater migrates the installation and restarts it as HD2RuntimeModBuilder.exe;
#   4. checks the result: new binaries only, user files kept, user data unchanged, the new app running.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Release = (Resolve-Path $Release).Path
$Work = [IO.Path]::GetFullPath($Work)
$version = (Get-Content -Raw (Join-Path $Release 'modbuilder-update-v2.json') | ConvertFrom-Json).version
if (-not (Test-Path (Join-Path $Release "HD2Runtime-ModBuilder-v$version-win-x64.zip"))) { throw "No $version release ZIP in $Release." }
function Hashes([string]$dir) { Get-ChildItem -LiteralPath $dir -Recurse -File | ForEach-Object { "$($_.FullName.Substring($dir.Length)) $((Get-FileHash -LiteralPath $_.FullName).Hash)" } }

# 1.0.0 update code and the published 1.0.0 package.
$worktree = Join-Path $repo 'artifacts\wt-1.0.0'
if (-not (Test-Path $worktree)) { & git -C $repo worktree add $worktree v1.0.0 | Out-Null }
$published = Join-Path $repo 'artifacts\v1.0.0-published'
if (-not (Test-Path (Join-Path $published 'HD2Runtime-ModBuilder-v1.0.0-win-x64.zip'))) { & gh release download v1.0.0 --repo SkyeShade/HD2Runtime-ModBuilder --dir $published --clobber }
$expected = (Get-Content (Join-Path $published 'HD2Runtime-ModBuilder-v1.0.0-win-x64.zip.sha256')).Split(' ')[0]
if ((Get-FileHash (Join-Path $published 'HD2Runtime-ModBuilder-v1.0.0-win-x64.zip')).Hash.ToLowerInvariant() -ne $expected) { throw 'The 1.0.0 ZIP does not match its published SHA-256.' }

# Sandbox: installation, data, temp.
Get-Process HD2RuntimeGUI, HD2RuntimeModBuilder -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Work*" } | Stop-Process -Force
if (Test-Path $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
$install = Join-Path $Work 'HD2Runtime ModBuilder'; $data = Join-Path $Work 'data'; $temp = Join-Path $Work 'temp'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $published 'HD2Runtime-ModBuilder-v1.0.0-win-x64.zip'), $install)
Set-Content -LiteralPath (Join-Path $install 'my-notes.txt') -Value 'user file next to the app'
Set-Content -LiteralPath (Join-Path $install 'HD2RuntimeGUI.my-backup.txt') -Value 'user file with the old name'
if (Test-Path $UserData) { Copy-Item -LiteralPath $UserData -Destination $data -Recurse } else { New-Item -ItemType Directory $data | Out-Null }
Remove-Item -LiteralPath (Join-Path $data 'app-update.json'), (Join-Path $data 'app-update-result.json') -ErrorAction SilentlyContinue
$dataBefore = Hashes $data

$env:HD2RUNTIMEGUI_DATA_ROOT = $data
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$DebugPort"
Write-Host "Starting published 1.0.0 from $install ..."
$app = Start-Process (Join-Path $install 'HD2RuntimeGUI.exe') -PassThru
Start-Sleep 10
if ($app.HasExited) { throw '1.0.0 did not start.' }
if (-not (Test-Path (Join-Path $install 'HD2RuntimeGUI.exe.WebView2'))) { throw '1.0.0 did not create its WebView2 folder.' }

Write-Host "Running the 1.0.0 in-app update code against $version ..."
# Output goes to a file: the updater and the restarted app inherit the driver's handles, and a captured pipe would stay open.
$log = Join-Path $Work 'driver.log'
$driver = Start-Process dotnet -ArgumentList @('run', 'Driver.cs', '--', "`"$Release`"", $version, "`"$install`"", "`"$data`"", "`"$temp`"", $app.Id) `
    -WorkingDirectory (Join-Path $PSScriptRoot 'update-from-1.0.0') -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
$null = $driver.Handle # Windows PowerShell only reports ExitCode if the handle was opened while the process ran
$driver.WaitForExit()
$out = @(Get-Content $log)
$out | ForEach-Object { Write-Host "  $_" }
if ($driver.ExitCode -ne 0) { throw "The 1.0.0 update code did not start the update (exit $($driver.ExitCode)): $(Get-Content "$log.err" -Raw)" }
$updaterPid = [int]([regex]::Match(($out -join "`n"), 'updater pid (\d+)').Groups[1].Value)
try { Wait-Process -Id $updaterPid -Timeout 180 -ErrorAction Stop } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
Start-Sleep 8

# Results.
$files = @(Get-ChildItem -LiteralPath $install -File | ForEach-Object Name)
$running = @(Get-Process HD2RuntimeGUI, HD2RuntimeModBuilder -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$install*" })
$inventory = Get-Content -Raw (Join-Path $install 'modbuilder-files.json') | ConvertFrom-Json
$dataAfter = @(Hashes $data | Where-Object { $_ -notmatch '^\\app-update(-result)?\.json ' })
$checks = [ordered]@{
    'HD2RuntimeModBuilder.exe installed' = $files -contains 'HD2RuntimeModBuilder.exe'
    'installed version' = (Get-Item (Join-Path $install 'HD2RuntimeModBuilder.exe')).VersionInfo.ProductVersion
    'old HD2RuntimeGUI.* app files removed' = @($files | Where-Object { $_ -like 'HD2RuntimeGUI.*' -and $_ -ne 'HD2RuntimeGUI.my-backup.txt' }).Count -eq 0
    'user files kept' = ($files -contains 'my-notes.txt') -and ($files -contains 'HD2RuntimeGUI.my-backup.txt')
    '1.0.0 WebView2 cache removed' = -not (Test-Path (Join-Path $install 'HD2RuntimeGUI.exe.WebView2'))
    'no update backup left' = -not (Test-Path (Join-Path $install '.modbuilder-update-backup'))
    'inventory version' = $inventory.version
    'inventory matches files' = @($inventory.files | Where-Object { -not (Test-Path -LiteralPath (Join-Path $install $_.path)) }).Count -eq 0 -and @($inventory.files | Where-Object { $_.path -eq 'HD2RuntimeGUI.exe' }).Count -eq 0
    'running' = ($running | ForEach-Object { $_.Path }) -join ', '
    'user data unchanged' = $null -eq (Compare-Object @($dataBefore) $dataAfter)
}
$checks.GetEnumerator() | ForEach-Object { Write-Host ("{0,-40} {1}" -f $_.Key, $_.Value) }
$ok = $checks['HD2RuntimeModBuilder.exe installed'] -and $checks['old HD2RuntimeGUI.* app files removed'] -and $checks['user files kept'] -and $checks['1.0.0 WebView2 cache removed'] -and
    $checks['no update backup left'] -and $checks['inventory version'] -eq $version -and $checks['inventory matches files'] -and $checks['running'] -eq (Join-Path $install 'HD2RuntimeModBuilder.exe') -and $checks['user data unchanged']
if (-not $ok) { throw "1.0.0 -> $version update check failed." }
Write-Host "1.0.0 -> $version update passed. The updated app is still running (DevTools port $DebugPort)."
