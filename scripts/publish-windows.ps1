[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'HD2RuntimeGUI\HD2RuntimeGUI.csproj'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'

function Assert-ReleasePath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    $root = [IO.Path]::GetFullPath($releaseRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the release staging directory: $full"
    }
    return $full
}

Push-Location $repoRoot
try {
    $version = (& dotnet msbuild $project -getProperty:Version -nologo | Select-Object -Last 1).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') { throw "Could not determine a valid application version (got '$version')." }

    $stage = Assert-ReleasePath (Join-Path $releaseRoot "HD2RuntimeGUI-v$version-win-x64")
    $zip = Assert-ReleasePath (Join-Path $releaseRoot "HD2RuntimeGUI-v$version-win-x64.zip")
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    # Resizetizer can keep previously generated icons (appicon.ico, tiles); regenerate them from Resources/AppIcon/appicon.svg.
    $resizetizer = Join-Path $repoRoot "HD2RuntimeGUI\obj\$Configuration\net10.0-windows10.0.19041.0\win-x64\resizetizer"
    if (Test-Path $resizetizer) { Remove-Item -LiteralPath $resizetizer -Recurse -Force }

    Write-Host "Publishing HD2RuntimeGUI $version ($Configuration, win-x64, self-contained)..."
    & dotnet restore $project --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    & dotnet publish $project -c $Configuration -f net10.0-windows10.0.19041.0 -r win-x64 `
        --self-contained true --no-restore --nologo -o $stage `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    $exe = Join-Path $stage 'HD2RuntimeGUI.exe'
    if (-not (Test-Path $exe)) { throw "Published executable was not found: $exe" }
    if ((Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File -Recurse | Measure-Object).Count -ne 0) { throw 'Debug symbols remain in the release package.' }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    $zipInfo = Get-Item -LiteralPath $zip
    Write-Host "Version: $version"
    Write-Host "Executable: $exe"
    Write-Host "ZIP: $zip"
    Write-Host ("ZIP size: {0:N1} MB" -f ($zipInfo.Length / 1MB))
}
finally {
    Pop-Location
}
