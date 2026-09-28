[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    # Local test builds only: allow uncommitted changes. Public releases must be built from a clean, committed tree.
    [switch]$AllowDirty
)

# Builds an HD2Runtime ModBuilder release in artifacts/release/:
#   HD2Runtime-ModBuilder-v<version>-win-x64.zip         flat application folder (HD2RuntimeModBuilder.exe at the root)
#   HD2Runtime-ModBuilder-v<version>-win-x64.zip.sha256  "<sha256> *<file>"
#   modbuilder-update-v2.json                            update manifest read by ModBuilder 1.0.1 and later
#   modbuilder-update.json                               format-1 manifest read by ModBuilder 1.0.0
# Upload all four to the GitHub release tagged v<version> on SkyeShade/HD2Runtime-ModBuilder.
# The package also contains HD2RuntimeGUI.exe, a byte-identical copy of HD2RuntimeModBuilder.exe: ModBuilder 1.0.0 only
# accepts packages that contain its old entry point. Updaters from 1.0.1 on do not install it (see docs/app-updates.md).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem, System.Drawing
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'HD2RuntimeGUI\HD2RuntimeGUI.csproj'
$updaterProject = Join-Path $repoRoot 'HD2RuntimeModBuilder.Updater\HD2RuntimeModBuilder.Updater.csproj'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$product = 'HD2Runtime ModBuilder'
$entrypoint = 'HD2RuntimeModBuilder.exe'
$legacyEntrypoint = 'HD2RuntimeGUI.exe'
$updaterExe = 'HD2RuntimeModBuilder.Updater.exe'
$inventoryName = 'modbuilder-files.json'
$utf8 = New-Object System.Text.UTF8Encoding $false

function Assert-ReleasePath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    $root = [IO.Path]::GetFullPath($releaseRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the release staging directory: $full"
    }
    return $full
}
function Get-Sha256([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
# The reticle tile is brand yellow (#FDD00E) just inside its top edge; the default .NET icon is purple.
function Assert-ReticleIcon([System.Drawing.Bitmap]$bitmap, [string]$what) {
    $pixel = $bitmap.GetPixel([int]($bitmap.Width / 2), [int]($bitmap.Height / 16))
    if ($pixel.R -lt 230 -or $pixel.G -lt 190 -or $pixel.G -gt 225 -or $pixel.B -gt 60) { throw "$what is not the HD2Runtime ModBuilder reticle icon (sampled $pixel)." }
}

Push-Location $repoRoot
try {
    $dirty = (& git status --porcelain) -join "`n"
    if ($dirty -and -not $AllowDirty) { throw "The working tree has uncommitted changes. Commit them first (or pass -AllowDirty for a local test build):`n$dirty" }
    $commit = (& git rev-parse HEAD).Trim()
    if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Could not determine the git commit (got '$commit')." }

    $version = (& dotnet msbuild $project -getProperty:Version -nologo | Select-Object -Last 1).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Could not determine a stable application version (got '$version')." }
    $tag = "v$version"
    $name = "HD2Runtime-ModBuilder-v$version-win-x64"

    $stage = Assert-ReleasePath (Join-Path $releaseRoot $name)
    $zip = Assert-ReleasePath (Join-Path $releaseRoot "$name.zip")
    $checksum = Assert-ReleasePath (Join-Path $releaseRoot "$name.zip.sha256")
    $manifestPath = Assert-ReleasePath (Join-Path $releaseRoot 'modbuilder-update-v2.json')
    $legacyManifestPath = Assert-ReleasePath (Join-Path $releaseRoot 'modbuilder-update.json')
    foreach ($path in @($stage, $zip, $checksum, $manifestPath, $legacyManifestPath)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    # Resizetizer can keep previously generated icons (appicon.ico, tiles); regenerate them from Resources/AppIcon/appicon.svg.
    $resizetizer = Join-Path $repoRoot "HD2RuntimeGUI\obj\$Configuration\net10.0-windows10.0.19041.0\win-x64\resizetizer"
    if (Test-Path $resizetizer) { Remove-Item -LiteralPath $resizetizer -Recurse -Force }

    Write-Host "Publishing $product $version ($Configuration, win-x64, self-contained) from $commit..."
    & dotnet restore $project --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    & dotnet publish $project -c $Configuration -f net10.0-windows10.0.19041.0 -r win-x64 `
        --self-contained true --no-restore --nologo -o $stage `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    # The updater is one self-contained, trimmed executable: it runs without the MAUI app or an installed .NET runtime.
    $updaterOut = Join-Path ([IO.Path]::GetTempPath()) ("modbuilder-updater-" + [Guid]::NewGuid().ToString('N'))
    & dotnet publish $updaterProject -c $Configuration -r win-x64 --nologo -o $updaterOut
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed with exit code $LASTEXITCODE." }
    Copy-Item -LiteralPath (Join-Path $updaterOut $updaterExe) -Destination $stage
    Remove-Item -LiteralPath $updaterOut -Recurse -Force

    Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    # Compatibility copy for the ModBuilder 1.0.0 updater (it requires HD2RuntimeGUI.exe in the package); it starts the same app.
    Copy-Item -LiteralPath (Join-Path $stage $entrypoint) -Destination (Join-Path $stage $legacyEntrypoint)

    # ---- Verify the application folder -----------------------------------------------------------------------------
    $exe = Join-Path $stage $entrypoint
    if (-not (Test-Path $exe)) { throw "Published executable was not found: $exe" }
    if (-not (Test-Path (Join-Path $stage $updaterExe))) { throw "The updater was not published." }
    $exeInfo = (Get-Item -LiteralPath $exe).VersionInfo
    if ($exeInfo.ProductName -ne $product -or $exeInfo.FileDescription -ne $product) { throw "Executable metadata is not branded (product '$($exeInfo.ProductName)', description '$($exeInfo.FileDescription)')." }
    $expectedProductVersion = if ($dirty) { "$version+$commit.dirty" } else { "$version+$commit" }
    if ($exeInfo.ProductVersion -ne $expectedProductVersion -or $exeInfo.FileVersion -ne "$version.0") { throw "Executable version is '$($exeInfo.ProductVersion)' / '$($exeInfo.FileVersion)', expected '$expectedProductVersion' / '$version.0'." }
    $icon = Join-Path $stage 'appicon.ico'
    if (-not (Test-Path $icon)) { throw 'appicon.ico (window/taskbar icon) is missing from the release.' }
    $iconImage = New-Object System.Drawing.Icon $icon, 64, 64
    try { Assert-ReticleIcon $iconImage.ToBitmap() 'appicon.ico' } finally { $iconImage.Dispose() }
    $exeIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($exe)
    try { Assert-ReticleIcon $exeIcon.ToBitmap() 'The executable icon' } finally { $exeIcon.Dispose() }

    # Nothing user-specific or development-only may ship: no SDK cache, projects, imported game icons, screenshots or symbols.
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { $_.FullName.Substring($stage.Length + 1).Replace('\', '/') } | Sort-Object)
    $forbidden = @($files | Where-Object { $_ -match '(?i)(\.pdb$|\.hd2snap$|(^|/)library\.json$|project\.hd2mod\.json$|(^|/)(Projects|Sdk|Icons|Exports|screenshots|artifacts)/|app-update|\.weapon-map\.json$)' })
    if ($forbidden.Count -gt 0) { throw "Release contains files that must not ship:`n$($forbidden -join "`n")" }
    if ($files -contains $inventoryName) { throw "$inventoryName must be generated, not published." }
    # Since 1.0.1 the application ships as HD2RuntimeModBuilder.*; only the 1.0.0 compatibility launcher keeps the old name.
    $oldNames = @($files | Where-Object { $_ -match '(?i)^HD2RuntimeGUI\.' -and $_ -ne $legacyEntrypoint })
    if ($oldNames.Count -gt 0) { throw "Release still contains HD2RuntimeGUI.* files:`n$($oldNames -join "`n")" }
    foreach ($required in @('HD2RuntimeModBuilder.dll', 'HD2RuntimeModBuilder.runtimeconfig.json', 'HD2RuntimeModBuilder.deps.json', 'HD2RuntimeModBuilder.Core.dll')) {
        if ($files -notcontains $required) { throw "Release is missing $required." }
    }
    if ((Get-Sha256 (Join-Path $stage $legacyEntrypoint)) -ne (Get-Sha256 $exe)) { throw "$legacyEntrypoint is not a copy of $entrypoint." }

    # ---- Inventory, ZIP, manifest -----------------------------------------------------------------------------------
    $entries = foreach ($file in $files) {
        $full = Join-Path $stage ($file.Replace('/', '\'))
        [ordered]@{ path = $file; size = (Get-Item -LiteralPath $full).Length; sha256 = Get-Sha256 $full }
    }
    $inventory = [ordered]@{ format = 1; product = $product; version = $version; files = @($entries) }
    [IO.File]::WriteAllText((Join-Path $stage $inventoryName), ($inventory | ConvertTo-Json -Depth 5), $utf8)

    # Flat layout with forward-slash entry names: HD2RuntimeGUI.exe, the updater and the inventory at the ZIP root.
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in @($files) + $inventoryName) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $stage ($file.Replace('/', '\'))), $file, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }

    $read = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $names = @($read.Entries | ForEach-Object { $_.FullName })
        if ($names.Count -ne $files.Count + 1) { throw "ZIP has $($names.Count) entries, expected $($files.Count + 1)." }
        foreach ($required in @($entrypoint, $legacyEntrypoint, $updaterExe, $inventoryName, 'appicon.ico')) { if ($names -notcontains $required) { throw "ZIP root is missing $required." } }
        $bad = @($names | Where-Object { $_ -match '\\' -or $_.StartsWith('/') -or $_ -match '(^|/)\.\.(/|$)' -or $_.StartsWith("$name/") })
        if ($bad.Count -gt 0) { throw "ZIP has unexpected entry names:`n$($bad -join "`n")" }
    } finally { $read.Dispose() }

    $zipInfo = Get-Item -LiteralPath $zip
    $sha = Get-Sha256 $zip
    # Format 2 (1.0.1+) names the real entry point; format 1 keeps the exact fields ModBuilder 1.0.0 validates.
    foreach ($m in @(@{ path = $manifestPath; format = 2; entry = $entrypoint }, @{ path = $legacyManifestPath; format = 1; entry = $legacyEntrypoint })) {
        $manifest = [ordered]@{
            format = $m.format; product = $product; version = $version; tag = $tag; asset = $zipInfo.Name; size = $zipInfo.Length
            sha256 = $sha; entrypoint = $m.entry; updater = $updaterExe; commit = $commit
        }
        [IO.File]::WriteAllText($m.path, ($manifest | ConvertTo-Json), $utf8)
        $check = [IO.File]::ReadAllText($m.path) | ConvertFrom-Json
        if ($check.format -ne $m.format -or $check.entrypoint -ne $m.entry -or $check.sha256 -ne (Get-Sha256 $zip) -or $check.size -ne (Get-Item -LiteralPath $zip).Length -or $check.version -ne $version -or $check.commit -ne $commit) {
            throw "$([IO.Path]::GetFileName($m.path)) does not match the ZIP."
        }
    }
    [IO.File]::WriteAllText($checksum, "$sha *$($zipInfo.Name)`n", $utf8)

    Write-Host "Version: $version ($tag)"
    Write-Host "Commit: $commit$(if ($dirty) { ' (DIRTY - local test build, do not publish)' })"
    Write-Host "Executable: $exe"
    Write-Host "Files: $($files.Count + 1)"
    Write-Host "ZIP: $zip"
    Write-Host ("ZIP size: {0:N1} MB" -f ($zipInfo.Length / 1MB))
    Write-Host "SHA-256: $sha"
    Write-Host "Manifests: $manifestPath, $legacyManifestPath"
    Write-Host "Checksum: $checksum"
}
finally {
    Pop-Location
}
