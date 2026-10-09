[CmdletBinding()]
param(
    # An extracted, read-only HD2Runtime tree for export validation (created from ..\HD2Runtime at $RuntimeCommit if missing).
    [string]$RuntimeTree = (Join-Path ([IO.Path]::GetTempPath()) 'hd2runtime-0.30.3-7da218b'),
    # The HD2Runtime checkout (default ..\HD2Runtime beside this repository; resolved below, since Windows PowerShell 5.1 leaves $PSScriptRoot
    # empty in the default of a parameter that follows another computed default).
    [string]$RuntimeRepo,
    # The HD2Runtime 0.30.3 release candidate commit whose SDK ModBuilder bundles (SdkPin.RuntimeCommit; tag v0.30.3 adds only migration records).
    [string]$RuntimeCommit = '7da218b53d20ab74c70cd77deb521397b7854e2d',
    [string]$RuntimeVersion = '0.30.3',
    # Local test builds only: allow uncommitted changes (the build report marks them).
    [switch]$AllowDirty,
    # Reuse an existing artifacts\release package instead of publishing again.
    [switch]$SkipPublish
)

# Builds the local HD2Runtime ModBuilder release candidate in build\release-candidate-<version>\:
#   HD2Runtime-ModBuilder-<version>\          the packaged app, ready to run (HD2RuntimeModBuilder.exe)
#   Launch ModBuilder RC (test data).cmd      starts it with its own data folder (test-data\), leaving %LOCALAPPDATA% alone
#   package\                                  the release ZIP, its .sha256 and both update manifests (not published)
#   RELEASE-NOTES-<version>.md, TEST-CHECKLIST.md, COVERAGE.md, capability-audit.json
#   export-fixtures\*.zip, export-validation.json  representative exports and their HD2Runtime validation
#   smokes\                                   desktop smokes run on the packaged app (logs, screenshots, summary.json)
#   test-results\, BUILD-REPORT.md, build-report.json
# Nothing is pushed, tagged or published.

$ErrorActionPreference = 'Stop'
if (-not $RuntimeRepo) { $RuntimeRepo = Join-Path $PSScriptRoot '..\..\HD2Runtime' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    $dirty = (& git status --porcelain) -join "`n"
    if ($dirty -and -not $AllowDirty) { throw "The working tree has uncommitted changes. Commit them first (or pass -AllowDirty for a local test build):`n$dirty" }
    $commit = (& git rev-parse HEAD).Trim()
    $version = (& dotnet msbuild (Join-Path $repoRoot 'HD2RuntimeGUI\HD2RuntimeGUI.csproj') -getProperty:Version -nologo | Select-Object -Last 1).Trim()
    $rc = Join-Path $repoRoot "build\release-candidate-$version"
    $name = "HD2Runtime-ModBuilder-v$version-win-x64"
    # Empty the folder rather than delete it: a shell or Explorer window open in it keeps the folder itself in use.
    if (Test-Path $rc) { Get-ChildItem -LiteralPath $rc -Force | Remove-Item -Recurse -Force }
    else { New-Item -ItemType Directory -Path $rc | Out-Null }

    # 1. The Windows package (self-contained, the same script a release uses).
    if (-not $SkipPublish) {
        $publishArgs = @{}; if ($AllowDirty) { $publishArgs.AllowDirty = $true }
        & (Join-Path $PSScriptRoot 'publish-windows.ps1') @publishArgs
    }
    $release = Join-Path $repoRoot 'artifacts\release'
    $package = Join-Path $rc 'package'; New-Item -ItemType Directory -Path $package | Out-Null
    foreach ($file in @("$name.zip", "$name.zip.sha256", 'modbuilder-update-v2.json', 'modbuilder-update.json')) {
        Copy-Item -LiteralPath (Join-Path $release $file) -Destination $package
    }
    $app = Join-Path $rc "HD2Runtime-ModBuilder-$version"
    Expand-Archive -LiteralPath (Join-Path $package "$name.zip") -DestinationPath $app
    $exe = Join-Path $app 'HD2RuntimeModBuilder.exe'
    if (-not (Test-Path $exe)) { throw 'The packaged executable is missing.' }
    $launcher = @"
@echo off
rem HD2Runtime ModBuilder $version release candidate ($commit) - local test build, not published.
rem Uses its own data folder (test-data) so your projects and SDK cache in %LOCALAPPDATA%\HD2RuntimeGUI are not touched.
set "HD2RUNTIMEGUI_DATA_ROOT=%~dp0test-data"
start "" "%~dp0HD2Runtime-ModBuilder-$version\HD2RuntimeModBuilder.exe"
"@
    Set-Content -LiteralPath (Join-Path $rc 'Launch ModBuilder RC (test data).cmd') -Value $launcher -Encoding ascii

    # 2. Tests, the capability audit (COVERAGE.md) and the export fixtures.
    $env:HD2_AUDIT_OUT = $rc
    $env:HD2_EXPORT_OUT = Join-Path $rc 'export-fixtures'
    $results = Join-Path $rc 'test-results'
    try {
        & dotnet test (Join-Path $repoRoot 'HD2RuntimeGUI.Tests\HD2RuntimeGUI.Tests.csproj') -c Debug --nologo -v q --logger "trx;LogFileName=tests.trx" --results-directory $results
        $testExit = $LASTEXITCODE
    } finally { Remove-Item Env:HD2_AUDIT_OUT, Env:HD2_EXPORT_OUT -ErrorAction SilentlyContinue }
    $trx = [xml](Get-Content -LiteralPath (Join-Path $results 'tests.trx') -Raw)
    $counters = $trx.TestRun.ResultSummary.Counters

    # 3. Export validation against the frozen HD2Runtime tree of the pinned SDK (extracted read-only with git archive). git archive leaves the
    #    research submodule (research/, since 0.30.0) empty: the validators ModBuilder runs (scripts/validate_examples.py through
    #    tools/validate-exports.py, and tests/support.py with the stratagem test modules through tools/custom-content/validate-runtime.py)
    #    read nothing under research/ (checked at 893d31f, dd32795 and 7da218b), so the tree needs only the superproject. Only the Runtime's own snapshot
    #    validators and unit tests read research/.
    if (-not (Test-Path (Join-Path $RuntimeTree 'VERSION'))) {
        New-Item -ItemType Directory -Path $RuntimeTree -Force | Out-Null
        # Through a file: Windows PowerShell pipes native output as text, which corrupts a tar stream.
        $tar = Join-Path ([IO.Path]::GetTempPath()) ('hd2runtime-' + [Guid]::NewGuid().ToString('N') + '.tar')
        & git -C $RuntimeRepo archive --format=tar -o $tar $RuntimeCommit
        if ($LASTEXITCODE -ne 0) { throw 'Could not archive the HD2Runtime release tree.' }
        & tar -x -f $tar -C $RuntimeTree; $extracted = $LASTEXITCODE; Remove-Item -LiteralPath $tar -Force
        if ($extracted -ne 0) { Remove-Item -LiteralPath $RuntimeTree -Recurse -Force; throw 'Could not extract the HD2Runtime release tree.' }
    }
    & py -3 -B (Join-Path $repoRoot 'tools\validate-exports.py') --isolation --runtime $RuntimeTree --output (Join-Path $rc 'export-validation.json') (Join-Path $rc 'export-fixtures\*.zip')
    $exportExit = $LASTEXITCODE
    $exports = Get-Content -LiteralPath (Join-Path $rc 'export-validation.json') -Raw | ConvertFrom-Json

    # 4. Documents.
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\release-notes\v$version.md") -Destination (Join-Path $rc "RELEASE-NOTES-$version.md")
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\testing\TEST-CHECKLIST.md') -Destination (Join-Path $rc 'TEST-CHECKLIST.md')
    # 5. Desktop smokes against the packaged app (also the packaged-launch check).
    & (Join-Path $PSScriptRoot 'run-rc-smokes.ps1') -App $exe -RuntimeTree $RuntimeTree
    $smokeExit = $LASTEXITCODE
    $smokes = Join-Path $rc 'smokes'
    $smokeSummary = if (Test-Path (Join-Path $smokes 'summary.json')) { Get-Content -LiteralPath (Join-Path $smokes 'summary.json') -Raw | ConvertFrom-Json } else { $null }

    # 6. Build report.
    $zip = Get-Item -LiteralPath (Join-Path $package "$name.zip")
    $sdkZip = Join-Path $repoRoot "HD2RuntimeGUI.Tests\Fixtures\sdk-$RuntimeVersion.zip"
    $audit = Get-Content -LiteralPath (Join-Path $rc 'capability-audit.json') -Raw | ConvertFrom-Json
    $report = [ordered]@{
        product = 'HD2Runtime ModBuilder'; version = $version; commit = $commit; dirty = [bool]$dirty; built = (Get-Date).ToUniversalTime().ToString('o')
        package = [ordered]@{ zip = $zip.Name; size = $zip.Length; sha256 = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        runtime = [ordered]@{ version = $RuntimeVersion; releaseCommit = $RuntimeCommit; sdkCommit = $RuntimeCommit; apiVersion = 1; schemaVersion = 1
            sdkArchiveSha256 = (Get-FileHash -LiteralPath $sdkZip -Algorithm SHA256).Hash.ToLowerInvariant(); contentFingerprint = $audit.contentFingerprint; pinnedBuild = $audit.pinnedBuild }
        tests = [ordered]@{ total = [int]$counters.total; passed = [int]$counters.passed; failed = [int]$counters.failed; exitCode = $testExit }
        exportValidation = [ordered]@{ status = $exports.status; mode = $exports.mode; snapshot = $exports.snapshot; exports = $exports.exports; failed = @($exports.failed); exitCode = $exportExit }
        capabilityAudit = [ordered]@{ totals = $audit.totals; unexpectedMissing = $audit.unexpectedMissing }
        smokes = $smokeSummary
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $rc 'build-report.json') -Encoding utf8
    $lines = @(
        "# HD2Runtime ModBuilder $version release candidate - build report", '',
        "- Commit: ``$commit``$(if ($dirty) { ' (**uncommitted changes**: local test build only)' })",
        "- Package: ``$($zip.Name)`` ($('{0:N1}' -f ($zip.Length / 1MB)) MB), SHA-256 ``$($report.package.sha256)``",
        "- HD2Runtime: $RuntimeVersion (commit ``$RuntimeCommit``, SDK generated at the same commit), API 1, schema 1",
        "- SDK archive SHA-256: ``$($report.runtime.sdkArchiveSha256)``; bundled content fingerprint ``$($audit.contentFingerprint)`` (pinned build: $($audit.pinnedBuild))",
        "- Tests: $($counters.passed) passed, $($counters.failed) failed of $($counters.total)",
        "- Export validation (HD2Runtime $RuntimeVersion validator, $($exports.mode) $($exports.snapshot)): $($exports.status), $(@($exports.failed).Count) failed of $($exports.exports) exports (with isolation probes)",
        "- Capability audit: unexpected missing = $($audit.unexpectedMissing) (see COVERAGE.md)",
        "- Desktop smokes on the packaged app: $(if ($smokeSummary) { ($smokeSummary.PSObject.Properties | ForEach-Object { "$($_.Name) $($_.Value)" }) -join ', ' } else { 'not run' })",
        '', 'Open the app with "Launch ModBuilder RC (test data).cmd" and follow TEST-CHECKLIST.md. Nothing here is published.'
    )
    Set-Content -LiteralPath (Join-Path $rc 'BUILD-REPORT.md') -Value ($lines -join "`n") -Encoding utf8
    Write-Host "Release candidate: $rc"
    if ($testExit -ne 0 -or $exportExit -ne 0 -or $smokeExit -ne 0 -or $audit.unexpectedMissing -ne 0) { throw "Release candidate checks failed (tests $testExit, exports $exportExit, smokes $smokeExit, unexpected missing $($audit.unexpectedMissing))." }
}
finally { Pop-Location }
