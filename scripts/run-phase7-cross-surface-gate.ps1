[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/quality-gates/phase-7",
    [string]$AcceptanceReport = "artifacts/acceptance/phase-7/acceptance-report.json",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [int]$DefaultTimeoutSeconds = 600,
    [switch]$SkipApiContractTests
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

if ([System.IO.Path]::IsPathRooted($AcceptanceReport)) {
    $acceptanceReportPath = $AcceptanceReport
}
else {
    $acceptanceReportPath = Join-Path $repoRoot $AcceptanceReport
}

$artifactDir = [System.IO.Path]::GetFullPath($artifactDir)
$acceptanceReportPath = [System.IO.Path]::GetFullPath($acceptanceReportPath)
$runId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$runDir = Join-Path (Join-Path $artifactDir "runs") $runId
$commandsDir = Join-Path $runDir "commands"
$jsonGatePath = Join-Path $artifactDir "autotrade-product-gate.json"
$markdownGatePath = Join-Path $artifactDir "autotrade-product-gate.md"

New-Item -ItemType Directory -Force -Path $commandsDir | Out-Null

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Resolve-ToolPath {
    param([Parameter(Mandatory = $true)][string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return $command.Source
        }
    }

    return $Candidates[0]
}

function ConvertTo-Slug {
    param([Parameter(Mandatory = $true)][string]$Value)

    $slug = ($Value.ToLowerInvariant() -replace "[^a-z0-9]+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "step"
    }

    return $slug
}

function Quote-Argument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '"', '\"') + '"'
}

function Join-CommandLine {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    return ((@($FilePath) + $Arguments) | ForEach-Object { Quote-Argument $_ }) -join " "
}

function Invoke-GateStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [int]$TimeoutSeconds = $DefaultTimeoutSeconds,
        [bool]$Skip = $false,
        [string]$SkipReason = ""
    )

    $slug = ConvertTo-Slug $Name
    $stdoutPath = Join-Path $commandsDir "$slug.stdout.txt"
    $stderrPath = Join-Path $commandsDir "$slug.stderr.txt"
    $startedAt = Get-Date
    $status = "skipped"
    $exitCode = $null
    $stdout = ""
    $stderr = ""
    $exception = ""
    $commandLine = Join-CommandLine -FilePath $FilePath -Arguments $Arguments

    if ($Skip) {
        $stderr = $SkipReason
    }
    else {
        try {
            $process = [System.Diagnostics.Process]::new()
            $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $process.StartInfo.FileName = $FilePath
            $process.StartInfo.Arguments = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join " "
            $process.StartInfo.WorkingDirectory = $WorkingDirectory
            $process.StartInfo.UseShellExecute = $false
            $process.StartInfo.RedirectStandardOutput = $true
            $process.StartInfo.RedirectStandardError = $true
            $process.StartInfo.CreateNoWindow = $true

            [void]$process.Start()
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $completed = $process.WaitForExit([Math]::Max(1, $TimeoutSeconds) * 1000)

            if (-not $completed) {
                $status = "timed_out"
                try {
                    $process.Kill($true)
                }
                catch {
                    try {
                        $process.Kill()
                    }
                    catch {
                        $exception = $_.Exception.Message
                    }
                }
                $process.WaitForExit()
            }

            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            $exitCode = $process.ExitCode

            if ($status -ne "timed_out") {
                $status = if ($exitCode -eq 0) { "passed" } else { "failed" }
            }
        }
        catch {
            $status = "failed"
            $exception = $_.Exception.Message
            $stderr = $exception
        }
    }

    Write-Utf8File -Path $stdoutPath -Content $stdout
    Write-Utf8File -Path $stderrPath -Content $stderr
    $finishedAt = Get-Date
    $artifactPath = if ([string]::IsNullOrWhiteSpace($stderr)) { $stdoutPath } else { $stderrPath }

    return [pscustomobject]@{
        name = $Name
        status = $status
        command = $commandLine
        workingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
        exitCode = $exitCode
        startedAtUtc = $startedAt.ToUniversalTime().ToString("O")
        finishedAtUtc = $finishedAt.ToUniversalTime().ToString("O")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        stdoutPath = [System.IO.Path]::GetFullPath($stdoutPath)
        stderrPath = [System.IO.Path]::GetFullPath($stderrPath)
        artifactPath = [System.IO.Path]::GetFullPath($artifactPath)
        exception = $exception
        skipReason = $SkipReason
    }
}

function New-Assertion {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string[]]$Artifacts = @(),
        [object]$Details = $null
    )

    return [pscustomobject]@{
        name = $Name
        status = if ($Passed) { "passed" } else { "failed" }
        reason = $Reason
        artifacts = @($Artifacts | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
        details = $Details
    }
}

function Read-Text {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.File]::ReadAllText($Path)
}

function Test-FileContainsAll {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $text = Read-Text $Path
    foreach ($pattern in $Patterns) {
        if (-not $text.Contains($pattern)) {
            return $false
        }
    }

    return $true
}

function Test-NonEmptyFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    return ((Get-Item -LiteralPath $Path).Length -gt 0)
}

$dotnetPath = Resolve-ToolPath -Candidates @("dotnet.exe", "dotnet")
$apiTestsProject = Join-Path $repoRoot "interfaces\Autotrade.Api.Tests\Autotrade.Api.Tests.csproj"
$apiReadinessController = Join-Path $repoRoot "interfaces\Autotrade.Api\Controllers\ReadinessController.cs"
$apiControlRoomController = Join-Path $repoRoot "interfaces\Autotrade.Api\Controllers\ControlRoomController.cs"
$apiRunReportsController = Join-Path $repoRoot "interfaces\Autotrade.Api\Controllers\RunReportsController.cs"
$apiReplayController = Join-Path $repoRoot "interfaces\Autotrade.Api\Controllers\ReplayExportsController.cs"
$webApiClient = Join-Path $repoRoot "webApp\src\api\controlRoom.ts"
$cliProgram = Join-Path $repoRoot "Autotrade.Cli\Program.cs"
$cliReadinessCommand = Join-Path $repoRoot "Autotrade.Cli\Commands\ReadinessCommand.cs"
$phase5GatePath = Join-Path $repoRoot "artifacts\audit\phase-5\phase5-audit-regression-gate.json"
$phase5ReplayPath = Join-Path $repoRoot "artifacts\audit\phase-5\replay-package.json"
$phase5BrowserReportPath = Join-Path $repoRoot "artifacts\audit\phase-5\phase5-browser-report.json"
$phase6WebEvidenceDir = Join-Path $repoRoot "artifacts\webapp\phase-6"
$localSmokeJsonPath = Join-Path $repoRoot "artifacts\deploy\phase-7\local-smoke.json"
$localSmokeMarkdownPath = Join-Path $repoRoot "artifacts\deploy\phase-7\local-smoke.md"
$operatorRunbookPath = Join-Path $repoRoot "docs\operations\autotrade-operator-runbook.md"
$incidentRunbookPath = Join-Path $repoRoot "docs\operations\autotrade-incident-runbook.md"
$localDeployRunbookPath = Join-Path $repoRoot "docs\operations\local-product-deploy.md"
$releaseNotesPath = Join-Path $repoRoot "docs\releases\autotrade-product-hardening-release.md"

$steps = [System.Collections.Generic.List[object]]::new()
$assertions = [System.Collections.Generic.List[object]]::new()

$apiFilter = "FullyQualifiedName~ReadinessControllerContractTests|FullyQualifiedName~ControlRoomControllerContractTests|FullyQualifiedName~ControlRoomCommandServiceTests|FullyQualifiedName~RunReportsControllerContractTests|FullyQualifiedName~ReplayExportsControllerContractTests|FullyQualifiedName~AuditTimelineControllerContractTests|FullyQualifiedName~StrategyDecisionsControllerContractTests"
$steps.Add((Invoke-GateStep `
    -Name "phase7-api-cross-surface-contract-tests" `
    -FilePath $dotnetPath `
    -Arguments @("test", $apiTestsProject, "--configuration", $Configuration, "--no-restore", "--filter", $apiFilter) `
    -WorkingDirectory $repoRoot `
    -Skip $SkipApiContractTests.IsPresent `
    -SkipReason "Skipped by -SkipApiContractTests.")) | Out-Null

$apiStep = $steps[0]
$assertions.Add((New-Assertion `
    -Name "api contract tests cover readiness commands reports audit exports" `
    -Passed ($apiStep.status -eq "passed") `
    -Reason "Targeted API contract tests must pass for readiness, control-room commands, run reports, replay exports, audit timeline, and strategy decisions." `
    -Artifacts @($apiStep.stdoutPath, $apiStep.stderrPath))) | Out-Null

$acceptanceExists = Test-NonEmptyFile $acceptanceReportPath
$assertions.Add((New-Assertion `
    -Name "p7 acceptance report exists" `
    -Passed $acceptanceExists `
    -Reason "P7-T001 acceptance report is the CLI/runtime evidence source for this cross-surface gate." `
    -Artifacts @($acceptanceReportPath))) | Out-Null

$acceptance = $null
if ($acceptanceExists) {
    $acceptance = Get-Content -Raw -LiteralPath $acceptanceReportPath | ConvertFrom-Json
    $requiredCliGates = @("cli readiness", "cli status", "export run report", "export replay package")
    $actualCliGates = @($acceptance.results | ForEach-Object { $_.name })
    $missingCliGates = @($requiredCliGates | Where-Object { $actualCliGates -notcontains $_ })
    $assertions.Add((New-Assertion `
        -Name "cli evidence commands are present" `
        -Passed ($missingCliGates.Count -eq 0) `
        -Reason "Acceptance runner must execute readiness, status, run-report export, and replay-package export commands." `
        -Artifacts @($acceptanceReportPath) `
        -Details ([ordered]@{ missing = $missingCliGates; actual = $actualCliGates }))) | Out-Null
}

$readinessSurfacePassed =
    (Test-FileContainsAll $apiReadinessController @('[Route("api/readiness")]', 'ActionResult<ReadinessReport>')) -and
    (Test-FileContainsAll $webApiClient @("apiRequest<ReadinessReport>('/api/readiness'")) -and
    (Test-FileContainsAll $cliReadinessCommand @("IReadinessReportService", "JsonStringEnumConverter", "PropertyNamingPolicy = JsonNamingPolicy.CamelCase"))
$assertions.Add((New-Assertion `
    -Name "readiness route uses shared contract across cli api ui" `
    -Passed $readinessSurfacePassed `
    -Reason "CLI readiness, API /api/readiness, and Web getReadinessReport must all consume the shared ReadinessReport contract with camelCase string enum JSON." `
    -Artifacts @($cliReadinessCommand, $apiReadinessController, $webApiClient))) | Out-Null

$strategySurfacePassed =
    (Test-FileContainsAll $apiControlRoomController @('[HttpPost("strategies/{strategyId}/state")]', 'SetStrategyState')) -and
    (Test-FileContainsAll $webApiClient @('setStrategyState', '/api/control-room/strategies/${encodeURIComponent(strategyId)}/state')) -and
    (Test-FileContainsAll $cliProgram @('strategyStartCommand', 'strategyPauseCommand', 'strategyStopCommand', 'strategyResumeCommand'))
$assertions.Add((New-Assertion `
    -Name "strategy state command surface is aligned" `
    -Passed $strategySurfacePassed `
    -Reason "API and Web must use the same strategy state endpoint, while CLI exposes the matching operator state commands." `
    -Artifacts @($cliProgram, $apiControlRoomController, $webApiClient))) | Out-Null

$killSwitchSurfacePassed =
    (Test-FileContainsAll $apiControlRoomController @('[HttpPost("risk/kill-switch")]', 'SetKillSwitch')) -and
    (Test-FileContainsAll $webApiClient @('setKillSwitch', "'/api/control-room/risk/kill-switch'")) -and
    (Test-FileContainsAll $cliProgram @('killSwitchActivateCommand', 'killSwitchResetCommand'))
$assertions.Add((New-Assertion `
    -Name "kill switch command surface is aligned" `
    -Passed $killSwitchSurfacePassed `
    -Reason "API, Web, and CLI must expose the same hard-stop and reset operator workflow." `
    -Artifacts @($cliProgram, $apiControlRoomController, $webApiClient))) | Out-Null

$reportExportSurfacePassed =
    (Test-FileContainsAll $apiRunReportsController @('[Route("api/run-reports")]', 'PaperRunReport', 'PaperPromotionChecklist')) -and
    (Test-FileContainsAll $apiReplayController @('[Route("api/replay-exports")]', 'ReplayExportQuery')) -and
    (Test-FileContainsAll $cliProgram @('exportRunReportCommand', 'exportReplayPackageCommand')) -and
    (Test-FileContainsAll $webApiClient @('getRunReport', 'getPromotionChecklist', 'getReplayExportPackage', '/api/replay-exports'))
$assertions.Add((New-Assertion `
    -Name "report and replay export surfaces are aligned" `
    -Passed $reportExportSurfacePassed `
    -Reason "Run report, promotion checklist, and replay export links must be present across API, CLI, and Web client surfaces." `
    -Artifacts @($cliProgram, $apiRunReportsController, $apiReplayController, $webApiClient))) | Out-Null

$replayExists = Test-NonEmptyFile $phase5ReplayPath
$replayIdsPassed = $false
$replayDetails = [ordered]@{}
if ($replayExists) {
    $replay = Get-Content -Raw -LiteralPath $phase5ReplayPath | ConvertFrom-Json
    $timelineItems = @($replay.timeline.items)
    $decisions = @($replay.evidence.decisions)
    $orders = @($replay.evidence.orders)
    $trades = @($replay.evidence.trades)
    $positions = @($replay.evidence.positions)

    $replayDetails = [ordered]@{
        strategyId = $replay.query.strategyId
        marketId = $replay.query.marketId
        orderId = $replay.query.orderId
        clientOrderId = $replay.query.clientOrderId
        runSessionId = $replay.query.runSessionId
        riskEventId = $replay.query.riskEventId
        correlationId = $replay.query.correlationId
        timelineItemCount = $timelineItems.Count
        decisionCount = $decisions.Count
        orderCount = $orders.Count
        tradeCount = $trades.Count
        positionCount = $positions.Count
    }

    $replayIdsPassed =
        $replay.query.runSessionId -eq $replay.runSession.sessionId -and
        @($replay.runSession.strategies) -contains $replay.query.strategyId -and
        $replay.timeline.query.runSessionId -eq $replay.query.runSessionId -and
        $replay.timeline.query.correlationId -eq $replay.query.correlationId -and
        ($timelineItems.Count -gt 0) -and
        @($timelineItems | Where-Object { $_.runSessionId -ne $replay.query.runSessionId -or $_.correlationId -ne $replay.query.correlationId }).Count -eq 0 -and
        ($decisions.Count -gt 0) -and
        @($decisions | Where-Object { $_.runSessionId -ne $replay.query.runSessionId -or $_.correlationId -ne $replay.query.correlationId }).Count -eq 0 -and
        ($orders.Count -gt 0) -and
        @($orders | Where-Object {
            $_.id -ne $replay.query.orderId -or
            $_.clientOrderId -ne $replay.query.clientOrderId -or
            $_.strategyId -ne $replay.query.strategyId -or
            $_.marketId -ne $replay.query.marketId -or
            $_.correlationId -ne $replay.query.correlationId
        }).Count -eq 0
}
$assertions.Add((New-Assertion `
    -Name "audit replay evidence ids are internally consistent" `
    -Passed $replayIdsPassed `
    -Reason "Replay export evidence must carry the same strategy, run session, order, and correlation IDs across query, timeline, decisions, and orders." `
    -Artifacts @($phase5ReplayPath) `
    -Details $replayDetails)) | Out-Null

$phase5GateExists = Test-NonEmptyFile $phase5GatePath
$phase5BrowserExists = Test-NonEmptyFile $phase5BrowserReportPath
$phase6Screenshots = @(
    "market-discovery-ranking-desktop.png",
    "trade-desktop.png",
    "ops-desktop.png",
    "activity-desktop.png",
    "run-report-desktop.png",
    "market-discovery-ranking-mobile.png",
    "trade-mobile.png"
) | ForEach-Object { Join-Path $phase6WebEvidenceDir $_ }
$missingPhase6Screenshots = @($phase6Screenshots | Where-Object { -not (Test-NonEmptyFile $_) })
$assertions.Add((New-Assertion `
    -Name "web and audit visual evidence artifacts exist" `
    -Passed ($phase5GateExists -and $phase5BrowserExists -and $missingPhase6Screenshots.Count -eq 0) `
    -Reason "The quality gate must link current audit artifacts and Phase 6 Web evidence paths used by operator workflows." `
    -Artifacts (@($phase5GatePath, $phase5BrowserReportPath) + $phase6Screenshots) `
    -Details ([ordered]@{ missingPhase6Screenshots = $missingPhase6Screenshots }))) | Out-Null

$releaseDocsAndDeployPaths = @(
    $localSmokeJsonPath,
    $localSmokeMarkdownPath,
    $operatorRunbookPath,
    $incidentRunbookPath,
    $localDeployRunbookPath,
    $releaseNotesPath
)
$missingReleaseDocsAndDeployPaths = @($releaseDocsAndDeployPaths | Where-Object { -not (Test-NonEmptyFile $_) })
$localSmokeStatus = "missing"
if (Test-NonEmptyFile $localSmokeJsonPath) {
    $localSmokeStatus = (Get-Content -Raw -LiteralPath $localSmokeJsonPath | ConvertFrom-Json).overallStatus
}
$assertions.Add((New-Assertion `
    -Name "local deploy and operator release docs are linked" `
    -Passed ($missingReleaseDocsAndDeployPaths.Count -eq 0 -and $localSmokeStatus -eq "Passed") `
    -Reason "The product gate must link local deploy smoke, operator runbooks, and release notes before release closure." `
    -Artifacts $releaseDocsAndDeployPaths `
    -Details ([ordered]@{
        missing = $missingReleaseDocsAndDeployPaths
        localSmokeStatus = $localSmokeStatus
    }))) | Out-Null

$failedAssertions = @($assertions | Where-Object { $_.status -ne "passed" })
$failedSteps = @($steps | Where-Object { $_.status -eq "failed" -or $_.status -eq "timed_out" })
$gateStatus = if ($failedAssertions.Count -gt 0 -or $failedSteps.Count -gt 0) { "Failed" } else { "Passed" }

$acceptanceStatus = if ($null -eq $acceptance) { "missing" } else { $acceptance.overallStatus }

$gate = [ordered]@{
    schemaVersion = "autotrade.phase7.cross-surface-gate.v1"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    runId = $runId
    gateStatus = $gateStatus
    acceptanceStatus = $acceptanceStatus
    repositoryRoot = [System.IO.Path]::GetFullPath($repoRoot)
    artifactRoot = [System.IO.Path]::GetFullPath($artifactDir)
    runDirectory = [System.IO.Path]::GetFullPath($runDir)
    jsonGatePath = [System.IO.Path]::GetFullPath($jsonGatePath)
    markdownGatePath = [System.IO.Path]::GetFullPath($markdownGatePath)
    summary = [ordered]@{
        steps = $steps.Count
        failedSteps = $failedSteps.Count
        assertions = $assertions.Count
        failedAssertions = $failedAssertions.Count
    }
    acceptanceReport = [System.IO.Path]::GetFullPath($acceptanceReportPath)
    steps = @($steps)
    assertions = @($assertions)
    crossSurfaceClaims = [ordered]@{
        readiness = "CLI readiness, API /api/readiness, and Web getReadinessReport share ReadinessReport."
        strategyState = "CLI strategy commands, API strategy state endpoint, and Web setStrategyState represent Running/Paused/Stopped operator actions."
        killSwitch = "CLI killswitch activate/reset, API risk/kill-switch, and Web setKillSwitch represent hard-stop/reset actions."
        reportsAndAudit = "Run report, promotion checklist, replay export, audit timeline, and replay package evidence IDs are linked."
        localDeployAndReleaseDocs = "Local deploy smoke, operator runbook, incident runbook, local deploy instructions, and release notes are linked."
    }
}

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add("# Phase 7 Cross-Surface Quality Gate") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("- Generated at UTC: $($gate.generatedAtUtc)") | Out-Null
$markdown.Add("- Gate status: $($gate.gateStatus)") | Out-Null
$markdown.Add("- Acceptance status: $($gate.acceptanceStatus)") | Out-Null
$markdown.Add("- JSON gate: $([System.IO.Path]::GetFullPath($jsonGatePath))") | Out-Null
$markdown.Add("- Run directory: $([System.IO.Path]::GetFullPath($runDir))") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("## Assertions") | Out-Null
$markdown.Add("") | Out-Null
foreach ($assertion in $assertions) {
    $markdown.Add("- $($assertion.status): $($assertion.name) - $($assertion.reason)") | Out-Null
}
$markdown.Add("") | Out-Null
$markdown.Add("## Command Steps") | Out-Null
$markdown.Add("") | Out-Null
foreach ($step in $steps) {
    $exit = if ($null -eq $step.exitCode) { "n/a" } else { [string]$step.exitCode }
    $markdown.Add("- $($step.status): $($step.name), exit $exit, artifact $($step.artifactPath)") | Out-Null
}

Write-Utf8File -Path $jsonGatePath -Content (($gate | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
Write-Utf8File -Path $markdownGatePath -Content (($markdown -join [Environment]::NewLine) + [Environment]::NewLine)

Write-Host "Cross-surface gate JSON: $jsonGatePath"
Write-Host "Cross-surface gate summary: $markdownGatePath"

if ($gateStatus -ne "Passed") {
    exit 1
}

exit 0
