[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/self-improve/closed-loop",
    [string]$LoopArtifactRoot = "artifacts/self-improve/agent-loop",
    [int]$Iterations = 1,
    [int]$DelaySeconds = 300,
    [switch]$SkipLiveLlm,
    [int]$LiveTimeoutSeconds = 120,
    [switch]$ContinueOnFailure
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Iterations -lt 0) {
    throw "Iterations must be 0 for continuous mode or a positive integer."
}

if ($DelaySeconds -lt 0) {
    throw "DelaySeconds cannot be negative."
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$runnerPath = Join-Path $scriptDir "run-self-improve-closed-loop.ps1"

if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
    throw "Closed-loop runner was not found: $runnerPath"
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $trimChars = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $base = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd($trimChars) + [System.IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($base.Length)
    }

    return $full
}

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

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowNull()]$Value
    )

    Write-Utf8File -Path $Path -Content ($Value | ConvertTo-Json -Depth 80)
}

function Add-JsonLine {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    [System.IO.File]::AppendAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 80 -Compress) + [Environment]::NewLine,
        $utf8NoBom)
}

function Get-LatestGate {
    param(
        [Parameter(Mandatory = $true)][string]$ClosedLoopRoot,
        [Parameter(Mandatory = $true)][DateTimeOffset]$StartedAtUtc
    )

    if (-not (Test-Path -LiteralPath $ClosedLoopRoot -PathType Container)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $ClosedLoopRoot -Recurse -Filter "self-improve-closed-loop-gate.json" |
        Where-Object { $_.LastWriteTimeUtc -ge $StartedAtUtc.UtcDateTime.AddSeconds(-2) } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Read-GateStatus {
    param([Parameter(Mandatory = $true)][string]$GatePath)

    try {
        $json = Get-Content -LiteralPath $GatePath -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            gateStatus = [string]$json.gateStatus
            liveTrading = [string]$json.liveTrading
            profitClaim = [string]$json.profitClaim
            baselinePnl = $json.baselineSummary.realizedPnl
            improvedPnl = $json.improvedSummary.realizedPnl
            improvementMode = [string]$json.improvement.mode
            model = [string]$json.improvement.model
        }
    }
    catch {
        return [pscustomobject]@{
            gateStatus = "Unreadable"
            liveTrading = ""
            profitClaim = $_.Exception.Message
            baselinePnl = $null
            improvedPnl = $null
            improvementMode = ""
            model = ""
        }
    }
}

$loopStamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$loopId = "self-improve-agent-loop-$loopStamp"
$loopRoot = Resolve-RepoPath (Join-Path $LoopArtifactRoot $loopId)
$closedLoopRoot = Resolve-RepoPath $ArtifactRoot
$eventsPath = Join-Path $loopRoot "controller-events.ndjson"
$summaryPath = Join-Path $loopRoot "agent-loop-summary.json"
New-Item -ItemType Directory -Force -Path $loopRoot | Out-Null

$iterationSummaries = [System.Collections.Generic.List[object]]::new()
$startedAtUtc = [DateTimeOffset]::UtcNow

Add-JsonLine -Path $eventsPath -Value ([ordered]@{
    type = "loop-started"
    loopId = $loopId
    startedAtUtc = $startedAtUtc.ToString("O")
    iterations = $Iterations
    delaySeconds = $DelaySeconds
    skipLiveLlm = [bool]$SkipLiveLlm
    liveTrading = "disabled"
    profitClaim = "paper replay only; not a live profitability guarantee"
})

$iteration = 0
$failedCount = 0

while ($Iterations -eq 0 -or $iteration -lt $Iterations) {
    $iteration++
    $iterationStartedAtUtc = [DateTimeOffset]::UtcNow
    $iterationLogPath = Join-Path $loopRoot ("iteration-{0:0000}.log" -f $iteration)
    $runnerArgs = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $runnerPath,
        "-ArtifactRoot",
        $ArtifactRoot,
        "-LiveTimeoutSeconds",
        $LiveTimeoutSeconds
    )
    if ($SkipLiveLlm) {
        $runnerArgs += "-SkipLiveLlm"
    }

    Add-JsonLine -Path $eventsPath -Value ([ordered]@{
        type = "iteration-started"
        loopId = $loopId
        iteration = $iteration
        startedAtUtc = $iterationStartedAtUtc.ToString("O")
        logPath = Get-RepoRelativePath $iterationLogPath
    })

    & powershell.exe @runnerArgs *> $iterationLogPath
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }

    $gateFile = Get-LatestGate -ClosedLoopRoot $closedLoopRoot -StartedAtUtc $iterationStartedAtUtc
    $gateInfo = $null
    $gatePath = ""
    if ($null -ne $gateFile) {
        $gatePath = Get-RepoRelativePath $gateFile.FullName
        $gateInfo = Read-GateStatus -GatePath $gateFile.FullName
    }
    else {
        $gateInfo = [pscustomobject]@{
            gateStatus = "Missing"
            liveTrading = ""
            profitClaim = "No gate JSON was produced for this iteration."
            baselinePnl = $null
            improvedPnl = $null
            improvementMode = ""
            model = ""
        }
    }

    if ($exitCode -ne 0 -or $gateInfo.gateStatus -eq "Failed" -or $gateInfo.gateStatus -eq "Missing") {
        $failedCount++
    }

    $finishedAtUtc = [DateTimeOffset]::UtcNow
    $durationSeconds = [Math]::Round(($finishedAtUtc - $iterationStartedAtUtc).TotalSeconds, 3)
    $iterationSummary = [ordered]@{
        iteration = $iteration
        startedAtUtc = $iterationStartedAtUtc.ToString("O")
        finishedAtUtc = $finishedAtUtc.ToString("O")
        durationSeconds = $durationSeconds
        exitCode = $exitCode
        gateStatus = $gateInfo.gateStatus
        gateJsonPath = $gatePath
        logPath = Get-RepoRelativePath $iterationLogPath
        improvementMode = $gateInfo.improvementMode
        model = $gateInfo.model
        baselinePnl = $gateInfo.baselinePnl
        improvedPnl = $gateInfo.improvedPnl
        liveTrading = $gateInfo.liveTrading
        profitClaim = $gateInfo.profitClaim
    }
    $iterationSummaries.Add($iterationSummary) | Out-Null

    Add-JsonLine -Path $eventsPath -Value ([ordered]@{
        type = "iteration-finished"
        loopId = $loopId
        iteration = $iteration
        finishedAtUtc = $finishedAtUtc.ToString("O")
        durationSeconds = $durationSeconds
        exitCode = $exitCode
        gateStatus = $gateInfo.gateStatus
        gateJsonPath = $gatePath
        logPath = Get-RepoRelativePath $iterationLogPath
    })

    $summaryStatus = if ($failedCount -eq 0) { "Passed" } else { "Failed" }
    Write-JsonFile -Path $summaryPath -Value ([ordered]@{
        status = if ($Iterations -eq 0) { "Running" } else { $summaryStatus }
        loopId = $loopId
        startedAtUtc = $startedAtUtc.ToString("O")
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        iterationsRequested = $Iterations
        iterationsCompleted = $iterationSummaries.Count
        failedIterations = $failedCount
        skipLiveLlm = [bool]$SkipLiveLlm
        liveTrading = "disabled"
        profitClaim = "paper replay only; not a live profitability guarantee"
        controllerEventsPath = Get-RepoRelativePath $eventsPath
        iterations = $iterationSummaries
    })

    if ($exitCode -ne 0 -and -not $ContinueOnFailure) {
        break
    }

    if ($Iterations -ne 0 -and $iteration -ge $Iterations) {
        break
    }

    if ($DelaySeconds -gt 0) {
        Start-Sleep -Seconds $DelaySeconds
    }
}

$finalStatus = if ($failedCount -eq 0) { "Passed" } else { "Failed" }
Write-JsonFile -Path $summaryPath -Value ([ordered]@{
    status = $finalStatus
    loopId = $loopId
    startedAtUtc = $startedAtUtc.ToString("O")
    finishedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    iterationsRequested = $Iterations
    iterationsCompleted = $iterationSummaries.Count
    failedIterations = $failedCount
    skipLiveLlm = [bool]$SkipLiveLlm
    liveTrading = "disabled"
    profitClaim = "paper replay only; not a live profitability guarantee"
    controllerEventsPath = Get-RepoRelativePath $eventsPath
    iterations = $iterationSummaries
})

Add-JsonLine -Path $eventsPath -Value ([ordered]@{
    type = "loop-finished"
    loopId = $loopId
    finishedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    status = $finalStatus
    iterationsCompleted = $iterationSummaries.Count
    failedIterations = $failedCount
    summaryPath = Get-RepoRelativePath $summaryPath
})

Write-Host "Agent loop status: $finalStatus"
Write-Host "Agent loop summary: $(Get-RepoRelativePath $summaryPath)"
Write-Host "Controller events: $(Get-RepoRelativePath $eventsPath)"

if ($finalStatus -ne "Passed") {
    exit 1
}
