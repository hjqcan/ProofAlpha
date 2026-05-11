[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/releases/phase-7",
    [switch]$RequirePhaseComplete
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

if ([System.IO.Path]::IsPathRooted($ArtifactRoot)) {
    $artifactDir = $ArtifactRoot
}
else {
    $artifactDir = Join-Path $repoRoot $ArtifactRoot
}

$artifactDir = [System.IO.Path]::GetFullPath($artifactDir)
$jsonPath = Join-Path $artifactDir "release-candidate-record.json"
$markdownPath = Join-Path $artifactDir "release-candidate-record.md"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-EvidenceRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Category,
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Description = ""
    )

    $full = Resolve-RepoPath $Path
    $exists = Test-Path -LiteralPath $full
    $isDirectory = $exists -and (Get-Item -LiteralPath $full).PSIsContainer
    $hash = ""
    $length = 0L
    if ($exists -and -not $isDirectory) {
        $item = Get-Item -LiteralPath $full
        $length = $item.Length
        $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    }

    return [pscustomobject]@{
        category = $Category
        path = $full
        exists = $exists
        isDirectory = $isDirectory
        length = $length
        sha256 = $hash
        description = $Description
    }
}

function Add-Evidence {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Target,
        [Parameter(Mandatory = $true)][string]$Category,
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Description = ""
    )

    $Target.Add((Get-EvidenceRecord -Category $Category -Path $Path -Description $Description)) | Out-Null
}

function Add-DirectoryFiles {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Target,
        [Parameter(Mandatory = $true)][string]$Category,
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Filter = "*"
    )

    $full = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        Add-Evidence -Target $Target -Category $Category -Path $Path -Description "directory missing"
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $full -Filter $Filter -File) {
        Add-Evidence -Target $Target -Category $Category -Path $file.FullName
    }
}

function Read-JsonOrNull {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $full | ConvertFrom-Json
}

$acceptanceJsonPath = "artifacts/acceptance/phase-7/acceptance-report.json"
$acceptanceMarkdownPath = "artifacts/acceptance/phase-7/acceptance-report.md"
$qualityGateJsonPath = "artifacts/quality-gates/phase-7/autotrade-product-gate.json"
$qualityGateMarkdownPath = "artifacts/quality-gates/phase-7/autotrade-product-gate.md"
$localSmokeJsonPath = "artifacts/deploy/phase-7/local-smoke.json"
$localSmokeMarkdownPath = "artifacts/deploy/phase-7/local-smoke.md"
$releaseNotesPath = "docs/releases/autotrade-product-hardening-release.md"
$operatorRunbookPath = "docs/operations/autotrade-operator-runbook.md"
$incidentRunbookPath = "docs/operations/autotrade-incident-runbook.md"
$localDeployRunbookPath = "docs/operations/local-product-deploy.md"
$phase7TaskPath = "task-board/08-phase-7-integrated-quality-gate-and-release.txt"

$acceptance = Read-JsonOrNull $acceptanceJsonPath
$qualityGate = Read-JsonOrNull $qualityGateJsonPath
$localSmoke = Read-JsonOrNull $localSmokeJsonPath

$evidence = [System.Collections.Generic.List[object]]::new()
Add-Evidence -Target $evidence -Category "acceptance" -Path $acceptanceJsonPath -Description "machine-readable P7 acceptance report"
Add-Evidence -Target $evidence -Category "acceptance" -Path $acceptanceMarkdownPath -Description "human-readable P7 acceptance report"
Add-Evidence -Target $evidence -Category "quality-gate" -Path $qualityGateJsonPath -Description "single cross-surface quality gate"
Add-Evidence -Target $evidence -Category "quality-gate" -Path $qualityGateMarkdownPath
Add-Evidence -Target $evidence -Category "local-deploy" -Path $localSmokeJsonPath
Add-Evidence -Target $evidence -Category "local-deploy" -Path $localSmokeMarkdownPath
Add-Evidence -Target $evidence -Category "release-docs" -Path $releaseNotesPath
Add-Evidence -Target $evidence -Category "operator-runbook" -Path $operatorRunbookPath
Add-Evidence -Target $evidence -Category "operator-runbook" -Path $incidentRunbookPath
Add-Evidence -Target $evidence -Category "operator-runbook" -Path $localDeployRunbookPath
Add-Evidence -Target $evidence -Category "paper-run-report" -Path "artifacts/reports/phase-4/paper-run-report.json"
Add-Evidence -Target $evidence -Category "paper-run-report" -Path "artifacts/reports/phase-4/paper-run-report.csv"
Add-Evidence -Target $evidence -Category "paper-run-report" -Path "artifacts/reports/phase-4/promotion-checklist.json"
Add-Evidence -Target $evidence -Category "paper-run-report" -Path "artifacts/reports/phase-4/promotion-checklist.csv"
Add-Evidence -Target $evidence -Category "readiness" -Path "artifacts/readiness/phase-2/paper-ready.json"
Add-Evidence -Target $evidence -Category "readiness" -Path "artifacts/readiness/phase-2/readiness.json"
Add-Evidence -Target $evidence -Category "audit-risk-replay" -Path "artifacts/audit/phase-5/phase5-audit-regression-gate.json"
Add-Evidence -Target $evidence -Category "audit-risk-replay" -Path "artifacts/audit/phase-5/replay-package.json"
Add-Evidence -Target $evidence -Category "audit-risk-replay" -Path "artifacts/audit/phase-5/phase5-api-audit-incident-contract-tests.out.log"
Add-Evidence -Target $evidence -Category "audit-risk-replay" -Path "artifacts/audit/phase-5/phase5-strategy-audit-replay-tests.out.log"
Add-Evidence -Target $evidence -Category "audit-risk-replay" -Path "artifacts/audit/phase-5/phase5-trading-risk-drilldown-tests.out.log"
Add-DirectoryFiles -Target $evidence -Category "browser-screenshot" -Path "artifacts/audit/phase-5" -Filter "*.png"
Add-DirectoryFiles -Target $evidence -Category "browser-screenshot" -Path "artifacts/webapp/phase-6" -Filter "*.png"

if ($null -ne $acceptance) {
    foreach ($name in @("dotnet restore", "dotnet build", "dotnet test", "webapp build", "cli readiness", "cli health readiness", "export run report", "export replay package")) {
        $result = @($acceptance.results | Where-Object { $_.name -eq $name } | Select-Object -First 1)
        if ($result.Count -gt 0) {
            Add-Evidence -Target $evidence -Category "acceptance-command-log" -Path $result[0].stdoutPath -Description $name
            Add-Evidence -Target $evidence -Category "acceptance-command-log" -Path $result[0].stderrPath -Description $name
        }
    }
}

$taskText = [System.IO.File]::ReadAllText((Resolve-RepoPath $phase7TaskPath))
$openTaskMarkers = @()
if ($taskText.Contains("[TODO]")) {
    $openTaskMarkers += "[TODO]"
}
if ($taskText.Contains("[WIP]")) {
    $openTaskMarkers += "[WIP]"
}

$missingEvidence = @($evidence | Where-Object { -not $_.exists })
$emptyFileEvidence = @($evidence | Where-Object { $_.exists -and -not $_.isDirectory -and $_.length -eq 0 })
$acceptanceBlockingFailures = if ($null -eq $acceptance) { 1 } else { [int]$acceptance.summary.requiredBlockingFailures }
$qualityGateStatus = if ($null -eq $qualityGate) { "missing" } else { [string]$qualityGate.gateStatus }
$localSmokeStatus = if ($null -eq $localSmoke) { "missing" } else { [string]$localSmoke.overallStatus }
$phaseCompleteOk = -not $RequirePhaseComplete -or $openTaskMarkers.Count -eq 0

$passed =
    $missingEvidence.Count -eq 0 -and
    $acceptanceBlockingFailures -eq 0 -and
    $qualityGateStatus -eq "Passed" -and
    $localSmokeStatus -eq "Passed" -and
    $phaseCompleteOk

$record = [ordered]@{
    schemaVersion = "autotrade.phase7.release-candidate-record.v1"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    releaseName = "Autotrade product hardening release candidate"
    releaseDate = "2026-05-03"
    status = if ($passed) { "Passed" } else { "Failed" }
    repositoryRoot = [System.IO.Path]::GetFullPath($repoRoot)
    artifactRoot = [System.IO.Path]::GetFullPath($artifactDir)
    jsonPath = [System.IO.Path]::GetFullPath($jsonPath)
    markdownPath = [System.IO.Path]::GetFullPath($markdownPath)
    acceptanceStatus = if ($null -eq $acceptance) { "missing" } else { $acceptance.overallStatus }
    acceptanceRequiredBlockingFailures = $acceptanceBlockingFailures
    qualityGateStatus = $qualityGateStatus
    localSmokeStatus = $localSmokeStatus
    requirePhaseComplete = $RequirePhaseComplete.IsPresent
    openTaskMarkers = $openTaskMarkers
    residualLimitations = @(
        "Runtime readiness/status/export gates remain environment-dependent until local database migrations, API reachability, credentials, and compliance checks are intentionally prepared.",
        "Live trading is not enabled or approved by this release record.",
        "No profitability, fill, cancellation, or exchange-availability claims are made."
    )
    evidence = @($evidence)
    failures = [ordered]@{
        missingEvidence = @($missingEvidence)
        emptyFileEvidence = @($emptyFileEvidence)
    }
}

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add("# Release Candidate Record") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("- Generated at UTC: $($record.generatedAtUtc)") | Out-Null
$markdown.Add("- Status: $($record.status)") | Out-Null
$markdown.Add("- Acceptance status: $($record.acceptanceStatus)") | Out-Null
$markdown.Add("- Quality gate status: $($record.qualityGateStatus)") | Out-Null
$markdown.Add("- Local smoke status: $($record.localSmokeStatus)") | Out-Null
$markdown.Add("- Evidence count: $($evidence.Count)") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("## Residual Limitations") | Out-Null
$markdown.Add("") | Out-Null
foreach ($limitation in $record.residualLimitations) {
    $markdown.Add("- $limitation") | Out-Null
}
$markdown.Add("") | Out-Null
$markdown.Add("## Evidence") | Out-Null
$markdown.Add("") | Out-Null
foreach ($item in $evidence) {
    $hashText = if ([string]::IsNullOrWhiteSpace($item.sha256)) { "" } else { " sha256=$($item.sha256)" }
    $markdown.Add("- $($item.category): $($item.path) exists=$($item.exists)$hashText") | Out-Null
}

Write-Utf8File -Path $jsonPath -Content (($record | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
Write-Utf8File -Path $markdownPath -Content (($markdown -join [Environment]::NewLine) + [Environment]::NewLine)

Write-Host "Release closure JSON: $jsonPath"
Write-Host "Release closure summary: $markdownPath"

if (-not $passed) {
    exit 1
}

exit 0
