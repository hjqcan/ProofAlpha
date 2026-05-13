[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AgentLoopSummaryPath,

    [string]$OutputRoot = "artifacts/self-improve/browser-backtest",

    [int]$MinIterations = 3
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

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

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON file was not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

$summaryPath = Resolve-RepoPath $AgentLoopSummaryPath
$summary = Read-JsonFile -Path $summaryPath
$iterations = @($summary.iterations)
$failures = [System.Collections.Generic.List[string]]::new()

if ([string]$summary.status -ne "Passed") {
    $failures.Add("Agent loop status was $($summary.status), expected Passed.") | Out-Null
}

if ([int]$summary.iterationsCompleted -lt $MinIterations) {
    $failures.Add("Agent loop completed $($summary.iterationsCompleted) iterations, expected at least $MinIterations.") | Out-Null
}

if ([int]$summary.failedIterations -ne 0) {
    $failures.Add("Agent loop reported $($summary.failedIterations) failed iterations.") | Out-Null
}

$gateEvidence = [System.Collections.Generic.List[object]]::new()
foreach ($iteration in $iterations) {
    $iterationNumber = [int]$iteration.iteration
    $baselinePnl = [decimal]$iteration.baselinePnl
    $improvedPnl = [decimal]$iteration.improvedPnl
    if ($improvedPnl -le $baselinePnl) {
        $failures.Add("Iteration $iterationNumber did not improve paper PnL: baseline=$baselinePnl improved=$improvedPnl.") | Out-Null
    }

    if ([string]$iteration.liveTrading -ne "disabled") {
        $failures.Add("Iteration $iterationNumber did not keep live trading disabled.") | Out-Null
    }

    if ([string]$iteration.gateStatus -eq "Failed" -or [string]$iteration.gateStatus -eq "Missing") {
        $failures.Add("Iteration $iterationNumber had gate status $($iteration.gateStatus).") | Out-Null
    }

    $gatePath = Resolve-RepoPath ([string]$iteration.gateJsonPath)
    $gate = Read-JsonFile -Path $gatePath
    $failedChecks = @($gate.checks | Where-Object { [string]$_.status -eq "Failed" })
    if ($failedChecks.Count -gt 0) {
        $failures.Add("Iteration $iterationNumber gate has failed checks: $($failedChecks.id -join ', ').") | Out-Null
    }

    $gateEvidence.Add([ordered]@{
        iteration = $iterationNumber
        gateStatus = [string]$iteration.gateStatus
        improvementMode = [string]$iteration.improvementMode
        baselinePnl = $baselinePnl
        improvedPnl = $improvedPnl
        gateJsonPath = Get-RepoRelativePath $gatePath
        baselineRunLog = [string]$gate.checks[1].evidence
        improvedRunLog = [string]$gate.checks[4].evidence
        profitClaim = [string]$gate.profitClaim
    }) | Out-Null
}

$auditStatus = if ($failures.Count -eq 0) { "Passed" } else { "Failed" }
$stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$auditId = "self-improve-browser-backtest-audit-$stamp"
$outputRootPath = Resolve-RepoPath (Join-Path $OutputRoot $auditId)
$htmlPath = Join-Path $outputRootPath "browser-backtest-audit.html"
$jsonPath = Join-Path $outputRootPath "browser-backtest-audit.json"
$screenshotPath = Join-Path $outputRootPath "browser-backtest-audit.png"
New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

$viewModel = [ordered]@{
    auditId = $auditId
    status = $auditStatus
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    agentLoopSummaryPath = Get-RepoRelativePath $summaryPath
    iterationsRequested = [int]$summary.iterationsRequested
    iterationsCompleted = [int]$summary.iterationsCompleted
    failedIterations = [int]$summary.failedIterations
    minIterations = $MinIterations
    skipLiveLlm = [bool]$summary.skipLiveLlm
    liveTrading = [string]$summary.liveTrading
    profitClaim = [string]$summary.profitClaim
    gates = $gateEvidence
    failures = $failures
    browserVerification = [ordered]@{
        requiredSelector = "[data-proofalpha-browser-backtest='passed']"
        screenshotPath = Get-RepoRelativePath $screenshotPath
        mechanism = "Chrome/Playwright waits for the browser-computed passing DOM status before capturing the page."
    }
}

$viewModelJson = $viewModel | ConvertTo-Json -Depth 80 -Compress
$encodedViewModel = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($viewModelJson))
$htmlTemplate = @'
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ProofAlpha SelfImprove Browser Backtest Audit</title>
  <style>
    :root { color-scheme: light; font-family: Arial, sans-serif; background: #f7f7f4; color: #1b1d1f; }
    body { margin: 0; padding: 32px; }
    main { max-width: 1120px; margin: 0 auto; }
    h1 { font-size: 28px; margin: 0 0 8px; }
    h2 { font-size: 18px; margin: 24px 0 10px; }
    .muted { color: #5f686f; }
    .status { display: inline-block; padding: 6px 10px; border: 1px solid #1f7a4d; color: #145a39; background: #e8f6ef; font-weight: 700; }
    .status.failed { border-color: #a33a2f; color: #84251d; background: #fceceb; }
    .grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-top: 18px; }
    .metric { border: 1px solid #d8d8d2; background: #ffffff; padding: 14px; }
    .label { font-size: 12px; color: #687079; text-transform: uppercase; }
    .value { font-size: 22px; margin-top: 4px; font-weight: 700; }
    table { width: 100%; border-collapse: collapse; background: #ffffff; }
    th, td { border: 1px solid #d8d8d2; padding: 9px 10px; text-align: left; font-size: 13px; }
    th { background: #eeeeea; }
    code { font-family: Consolas, monospace; font-size: 12px; }
    .failures { border: 1px solid #a33a2f; background: #fceceb; padding: 12px; }
  </style>
</head>
<body>
  <main>
    <h1>SelfImprove Browser Backtest Audit</h1>
    <div class="muted" id="audit-id"></div>
    <p><span id="browser-status" class="status" data-status="pending">Pending</span></p>
    <section class="grid">
      <div class="metric"><div class="label">Iterations</div><div class="value" id="iterations"></div></div>
      <div class="metric"><div class="label">Failed</div><div class="value" id="failed"></div></div>
      <div class="metric"><div class="label">Live Trading</div><div class="value" id="live"></div></div>
      <div class="metric"><div class="label">LLM Mode</div><div class="value" id="llm"></div></div>
    </section>
    <h2>Iteration Evidence</h2>
    <table>
      <thead>
        <tr>
          <th>Iteration</th>
          <th>Gate</th>
          <th>Baseline PnL</th>
          <th>Improved PnL</th>
          <th>Run Logs</th>
        </tr>
      </thead>
      <tbody id="gates"></tbody>
    </table>
    <h2>Profit Claim</h2>
    <p id="profit-claim"></p>
    <div id="failures"></div>
  </main>
  <script>
    const encoded = "__ENCODED_VIEW_MODEL__";
    const bytes = Uint8Array.from(atob(encoded), c => c.charCodeAt(0));
    const audit = JSON.parse(new TextDecoder().decode(bytes));
    const browserFailures = [];
    if (audit.status !== "Passed") browserFailures.push("server-side audit status was not Passed");
    if (audit.iterationsCompleted < audit.minIterations) browserFailures.push("not enough iterations completed");
    if (audit.failedIterations !== 0) browserFailures.push("failed iterations were reported");
    if (audit.liveTrading !== "disabled") browserFailures.push("live trading was not disabled");
    for (const gate of audit.gates) {
      if (Number(gate.improvedPnl) <= Number(gate.baselinePnl)) {
        browserFailures.push(`iteration ${gate.iteration} did not improve paper PnL`);
      }
      if (gate.profitClaim !== "paper replay only; not a live profitability guarantee") {
        browserFailures.push(`iteration ${gate.iteration} made an unexpected profit claim`);
      }
    }
    const passed = browserFailures.length === 0;
    document.body.dataset.proofalphaBrowserBacktest = passed ? "passed" : "failed";
    const status = document.getElementById("browser-status");
    status.textContent = passed ? "PASSED" : "FAILED";
    status.dataset.status = passed ? "passed" : "failed";
    status.className = passed ? "status" : "status failed";
    document.getElementById("audit-id").textContent = `${audit.auditId} generated ${audit.generatedAtUtc}`;
    document.getElementById("iterations").textContent = `${audit.iterationsCompleted}/${audit.iterationsRequested}`;
    document.getElementById("failed").textContent = audit.failedIterations;
    document.getElementById("live").textContent = audit.liveTrading;
    document.getElementById("llm").textContent = audit.skipLiveLlm ? "deterministic" : "live";
    document.getElementById("profit-claim").textContent = audit.profitClaim;
    document.getElementById("gates").innerHTML = audit.gates.map(gate => `
      <tr>
        <td>${gate.iteration}</td>
        <td>${gate.gateStatus}<br><code>${gate.gateJsonPath}</code></td>
        <td>${gate.baselinePnl}</td>
        <td>${gate.improvedPnl}</td>
        <td><code>${gate.baselineRunLog}</code><br><code>${gate.improvedRunLog}</code></td>
      </tr>`).join("");
    const failureBox = document.getElementById("failures");
    const allFailures = [...audit.failures, ...browserFailures];
    if (allFailures.length > 0) {
      failureBox.className = "failures";
      failureBox.innerHTML = `<strong>Failures</strong><ul>${allFailures.map(f => `<li>${f}</li>`).join("")}</ul>`;
    }
    window.__proofalphaBacktestAudit = { status: passed ? "Passed" : "Failed", failures: allFailures, audit };
  </script>
</body>
</html>
'@
$html = $htmlTemplate.Replace("__ENCODED_VIEW_MODEL__", $encodedViewModel)

Write-Utf8File -Path $htmlPath -Content $html
Write-JsonFile -Path $jsonPath -Value ($viewModel + [ordered]@{
    htmlPath = Get-RepoRelativePath $htmlPath
    jsonPath = Get-RepoRelativePath $jsonPath
})

Write-Host "Browser backtest audit: $auditStatus"
Write-Host "HTML: $(Get-RepoRelativePath $htmlPath)"
Write-Host "JSON: $(Get-RepoRelativePath $jsonPath)"
Write-Host "Expected screenshot: $(Get-RepoRelativePath $screenshotPath)"

if ($auditStatus -ne "Passed") {
    exit 1
}
