[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/arc-hackathon/demo-run",
    [string]$ScreenshotRoot = "artifacts/arc-hackathon/screenshots",
    [int]$DefaultTimeoutSeconds = 600,
    [switch]$SkipRestore,
    [switch]$FinalizeOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

trap {
    Write-Error $_.ScriptStackTrace
    throw
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$runStartedUtc = (Get-Date).ToUniversalTime()
$runId = $runStartedUtc.ToString("yyyyMMddTHHmmssZ")

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

$artifactDir = Resolve-RepoPath $ArtifactRoot
$screenshotDir = Resolve-RepoPath $ScreenshotRoot
$logDir = Join-Path $artifactDir "logs"
$tempDir = Join-Path (Resolve-RepoPath ".codex-run") "arc-hackathon-demo"
$summaryPath = Join-Path $artifactDir "demo-summary.md"
$secretScanJsonPath = Join-Path $artifactDir "secret-scan.json"
$secretScanMarkdownPath = Join-Path $artifactDir "secret-scan.md"
$configPath = Join-Path $artifactDir "demo-cli.config.json"
$revenueConfigPath = Join-Path $artifactDir "demo-revenue-cli.config.json"

New-Item -ItemType Directory -Force -Path $artifactDir, $screenshotDir, $logDir, $tempDir | Out-Null

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
        [Parameter(Mandatory = $true)]$Value
    )

    Write-Utf8File -Path $Path -Content (($Value | ConvertTo-Json -Depth 32) + [Environment]::NewLine)
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Resolve-ToolPath {
    param([Parameter(Mandatory = $true)][string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return $command.Source
        }
    }

    throw "Required tool not found: $($Candidates -join ', ')"
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

$commandResults = [System.Collections.Generic.List[object]]::new()
$stepIndex = 0

function Invoke-DemoStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = $DefaultTimeoutSeconds
    )

    $script:stepIndex += 1
    $slug = "{0:D2}-{1}" -f $script:stepIndex, (ConvertTo-Slug $Name)
    $stdoutPath = Join-Path $logDir "$slug.stdout.txt"
    $stderrPath = Join-Path $logDir "$slug.stderr.txt"
    $startedAt = Get-Date
    $status = "failed"
    $exitCode = $null
    $exception = ""
    $commandLine = Join-CommandLine -FilePath $FilePath -Arguments $Arguments

    try {
        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $process.StartInfo.FileName = $FilePath
        $process.StartInfo.WorkingDirectory = $WorkingDirectory
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.Arguments = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join " "
        foreach ($key in $Environment.Keys) {
            $process.StartInfo.Environment[[string]$key] = [string]$Environment[$key]
        }

        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit([Math]::Max(1, $TimeoutSeconds) * 1000)

        if (-not $completed) {
            try {
                $process.Kill($true)
            }
            catch {
                $process.Kill()
            }
            $process.WaitForExit()
            $status = "timed_out"
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
        if ($status -ne "timed_out") {
            $status = if ($exitCode -eq 0) { "passed" } else { "failed" }
        }
    }
    catch {
        $stdout = ""
        $stderr = $_.Exception.Message
        $exception = $_.Exception.Message
        $status = "failed"
    }

    Write-Utf8File -Path $stdoutPath -Content $stdout
    Write-Utf8File -Path $stderrPath -Content $stderr

    $finishedAt = Get-Date
    $result = [pscustomobject]@{
        name = $Name
        status = $status
        exitCode = $exitCode
        command = $commandLine
        workingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
        startedAtUtc = $startedAt.ToUniversalTime().ToString("O")
        finishedAtUtc = $finishedAt.ToUniversalTime().ToString("O")
        durationMs = [int64]($finishedAt - $startedAt).TotalMilliseconds
        stdoutPath = $stdoutPath
        stderrPath = $stderrPath
        exception = $exception
    }
    $commandResults.Add($result) | Out-Null

    if ($status -ne "passed") {
        throw "Demo step failed: $Name. See $stderrPath"
    }

    return $result
}

function Add-DemoSkip {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Command = ""
    )

    $script:stepIndex += 1
    $slug = "{0:D2}-{1}" -f $script:stepIndex, (ConvertTo-Slug $Name)
    $stdoutPath = Join-Path $logDir "$slug.stdout.txt"
    $stderrPath = Join-Path $logDir "$slug.stderr.txt"
    $now = (Get-Date).ToUniversalTime().ToString("O")
    Write-Utf8File -Path $stdoutPath -Content ""
    Write-Utf8File -Path $stderrPath -Content $Reason

    $result = [pscustomobject]@{
        name = $Name
        status = "skipped"
        exitCode = 0
        command = $Command
        workingDirectory = [System.IO.Path]::GetFullPath($repoRoot)
        startedAtUtc = $now
        finishedAtUtc = $now
        durationMs = 0
        stdoutPath = $stdoutPath
        stderrPath = $stderrPath
        exception = $Reason
    }
    $commandResults.Add($result) | Out-Null
    return $result
}

function Copy-CommandJsonOutput {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $json = Get-Content -Raw -LiteralPath $Result.stdoutPath | ConvertFrom-Json
    Write-JsonFile -Path $Path -Value $json
}

function Assert-Artifact {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected artifact was not created: $Path"
    }
}

function Assert-DemoInvariant {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-FileEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Description = ""
    )

    $full = Resolve-RepoPath $Path
    $exists = Test-Path -LiteralPath $full -PathType Leaf
    $hash = ""
    $length = 0L
    if ($exists) {
        $item = Get-Item -LiteralPath $full
        $length = $item.Length
        $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    }

    return [pscustomobject]@{
        path = $full
        exists = $exists
        length = $length
        sha256 = $hash
        description = $Description
    }
}

function New-SecretScan {
    $scanRoots = @(
        (Resolve-RepoPath "docs"),
        (Resolve-RepoPath "scripts"),
        (Resolve-RepoPath "interfaces/Autotrade.Api/appsettings.json"),
        (Resolve-RepoPath "interfaces/Autotrade.Cli/appsettings.json"),
        (Resolve-RepoPath "interfaces/WebApp/dist"),
        (Resolve-RepoPath "interfaces/ArcContracts/deployments"),
        $artifactDir
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $fileExtensions = @(
        ".csproj", ".css", ".html", ".json", ".md", ".mjs", ".ps1", ".txt", ".xml", ".yml", ".yaml"
    )
    $patterns = @(
        [pscustomobject]@{ name = "privateKeyField"; regex = "privateKey|private_key" },
        [pscustomobject]@{ name = "apiSecretField"; regex = "apiSecret|api_secret" },
        [pscustomobject]@{ name = "apiKeyField"; regex = "apiKey|api_key" },
        [pscustomobject]@{ name = "passphraseField"; regex = "passphrase" },
        [pscustomobject]@{ name = "mnemonicField"; regex = "mnemonic" },
        [pscustomobject]@{ name = "arcPrivateKeyEnv"; regex = "ARC_SETTLEMENT_PRIVATE_KEY" },
        [pscustomobject]@{ name = "hardhatPrivateKeyA"; regex = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80" },
        [pscustomobject]@{ name = "hardhatPrivateKeyB"; regex = "59c6995e998f97a5a004497e5da20e72e7c4c8c0" },
        [pscustomobject]@{ name = "notForPublicMarker"; regex = "not-for-public" },
        [pscustomobject]@{ name = "signatureHashField"; regex = "signatureHash" }
    )

    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($root in $scanRoots) {
        if (Test-Path -LiteralPath $root -PathType Leaf) {
            $files.Add((Get-Item -LiteralPath $root)) | Out-Null
            continue
        }

        Get-ChildItem -LiteralPath $root -Recurse -File |
            Where-Object { $fileExtensions -contains $_.Extension.ToLowerInvariant() } |
            ForEach-Object { $files.Add($_) | Out-Null }
    }

    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        $relative = Get-RepoRelativePath $file.FullName
        $generatedScanInputs = @(
            $secretScanJsonPath,
            $secretScanMarkdownPath,
            (Join-Path $artifactDir "demo-run-record.json")
        )
        if ($generatedScanInputs | Where-Object { $file.FullName.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }) {
            continue
        }

        $relativeForMatch = $relative.Replace('\', '/')
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        for ($index = 0; $index -lt $lines.Length; $index++) {
            $line = $lines[$index]
            foreach ($pattern in $patterns) {
                if ($line -match $pattern.regex) {
                    $allowedReason = ""
                    if ($line -match "signatureHash") {
                        $allowedReason = "public hash field; no raw signature is present"
                    }
                    elseif ($line -match "PrivateKeyEnvironmentVariable|ARC_SETTLEMENT_PRIVATE_KEY") {
                        $allowedReason = "environment variable name only; no private key value is present"
                    }
                    elseif ($line -match "EnvVar|EnvironmentVariable|OPENAI_API_KEY|[A-Z][A-Z0-9_]*(API_KEY|API_SECRET|PASSPHRASE|PRIVATE_KEY)") {
                        $allowedReason = "environment variable name only; no secret value is present"
                    }
                    elseif ($relativeForMatch -like "scripts/*" -and $line -match "\[redacted\]|redact|regex\s*=|-match|-notmatch|privateKeyField|apiSecretField|apiKeyField|passphraseField|mnemonicField|hardhatPrivateKey|notForPublicMarker") {
                        $allowedReason = "script scanner pattern, redacted fixture, or redaction policy text; no secret value is present"
                    }
                    elseif ($relativeForMatch -like "docs/*" -and $line -match "apiKey|ApiKey|apiSecret|ApiSecret|passphrase|Passphrase|mnemonic|privateKey|PrivateKey|secret|private") {
                        $allowedReason = "documentation policy text or placeholder example"
                    }

                    $findings.Add([pscustomobject]@{
                        file = $relative
                        line = $index + 1
                        pattern = $pattern.name
                        preview = $line.Trim()
                        allowed = -not [string]::IsNullOrWhiteSpace($allowedReason)
                        allowedReason = $allowedReason
                    }) | Out-Null
                }
            }
        }
    }

    $screenshots = @()
    if (Test-Path -LiteralPath $screenshotDir -PathType Container) {
        $screenshots = @(Get-ChildItem -LiteralPath $screenshotDir -Filter "*.png" -File | ForEach-Object {
            Get-FileEvidence -Path ($_.FullName) -Description "generated WebApp screenshot; fixture data comes from scripts/arc-hackathon-webapp-check.mjs"
        })
    }

    $blocking = @($findings | Where-Object { -not $_.allowed })
    $record = [ordered]@{
        schemaVersion = "proofalpha.arcHackathon.secretScan.v1"
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
        scannedRoots = @($scanRoots)
        scannedTextFiles = $files.Count
        screenshotReview = [ordered]@{
            screenshotRoot = $screenshotDir
            count = $screenshots.Count
            method = "Source fixture review plus generated screenshot inventory; no OCR engine is required for this deterministic local run."
            files = $screenshots
        }
        findings = @($findings)
        blockingFindings = @($blocking)
        status = if ($blocking.Count -eq 0) { "Passed" } else { "Failed" }
    }

    Write-JsonFile -Path $secretScanJsonPath -Value $record

    $markdown = [System.Collections.Generic.List[string]]::new()
    $markdown.Add("# Arc Hackathon Secret Scan") | Out-Null
    $markdown.Add("") | Out-Null
    $markdown.Add("- Generated at UTC: $($record.generatedAtUtc)") | Out-Null
    $markdown.Add("- Status: $($record.status)") | Out-Null
    $markdown.Add("- Scanned text files: $($record.scannedTextFiles)") | Out-Null
    $markdown.Add("- Blocking findings: $($blocking.Count)") | Out-Null
    $markdown.Add("- Allowed contextual findings: $($findings.Count - $blocking.Count)") | Out-Null
    $markdown.Add("") | Out-Null
    $markdown.Add("Allowed findings are environment variable names, documentation examples/policy text, or public hash fields such as `signatureHash`.") | Out-Null
    $markdown.Add("") | Out-Null
    $markdown.Add("## Screenshot Review") | Out-Null
    $markdown.Add("") | Out-Null
    foreach ($screenshot in $screenshots) {
        $markdown.Add("- ``$(Get-RepoRelativePath $screenshot.path)`` sha256=$($screenshot.sha256)") | Out-Null
    }
    if ($blocking.Count -gt 0) {
        $markdown.Add("") | Out-Null
        $markdown.Add("## Blocking Findings") | Out-Null
        $markdown.Add("") | Out-Null
        foreach ($finding in $blocking) {
            $markdown.Add("- ``$($finding.file):$($finding.line)`` $($finding.pattern): $($finding.preview)") | Out-Null
        }
    }
    Write-Utf8File -Path $secretScanMarkdownPath -Content (($markdown -join [Environment]::NewLine) + [Environment]::NewLine)

    if ($blocking.Count -gt 0) {
        throw "Secret scan failed with $($blocking.Count) blocking finding(s). See $secretScanMarkdownPath"
    }

    return $record
}

$dotnet = Resolve-ToolPath @("dotnet")
$npm = Resolve-ToolPath @("npm.cmd", "npm")
$node = Resolve-ToolPath @("node.exe", "node")

$signalId = "0x7cc384e6393c4b85f9340bec439a81eca1d31778996494429c750946c7bb5cff"
$agentAddress = "0x70997970c51812dc3a010c7d01b50e0d17dc79c8"
$strategyId = "repricing_lag_arbitrage"
$marketId = "demo-polymarket-market"
$reasoningHash = "0x664020618931484b30288d9976c61e9ebf16d6ec304290c920bc891b24c6264a"
$riskEnvelopeHash = "0x9c7b7b3ccfb4e55ab07a273b974365eb8fcee2659f80f0c9b3b27a2fde65987c"
$unsubscribedWallet = "0x1234567890abcdef1234567890abcdef12345678"
$subscriptionGrossUsdc = "25"
$subscriptionGrossMicroUsdc = "25000000"

$signalProofPath = Join-Path $artifactDir "signal-proof.json"
$provenanceDir = Join-Path $artifactDir "provenance"
$opportunityPath = Join-Path $provenanceDir "opportunity-phase11-demo.json"
New-Item -ItemType Directory -Force -Path $provenanceDir | Out-Null

$signalProof = [ordered]@{
    documentVersion = "arc-strategy-signal-proof.v1"
    agentId = $agentAddress
    sourceKind = "opportunity"
    sourceId = "demo-opportunity-arc-phase-11"
    strategyId = $strategyId
    marketId = $marketId
    venue = "polymarket"
    createdAtUtc = $runStartedUtc.ToString("O")
    configVersion = "phase-11-demo"
    evidenceIds = @("phase11-demo-orderbook", "phase11-demo-risk-envelope")
    opportunityHash = $signalId
    reasoningHash = $reasoningHash
    riskEnvelopeHash = $riskEnvelopeHash
    expectedEdgeBps = 42
    maxNotionalUsdc = 100
    validUntilUtc = "2027-01-15T08:00:00Z"
}
Write-JsonFile -Path $signalProofPath -Value $signalProof

$opportunity = [ordered]@{
    documentVersion = "proofalpha-demo-opportunity.v1"
    opportunityId = $signalProof.sourceId
    strategyId = $strategyId
    marketId = $marketId
    venue = "polymarket"
    signalId = $signalId
    edgeBps = 42
    maxNotionalUsdc = 100
    riskTier = "paper"
    source = "deterministic Phase 11 demo fixture"
    disclosures = @(
        "Paper-first demo opportunity.",
        "No investment advice.",
        "Local EVM evidence is not mainnet settlement."
    )
    generatedAtUtc = $runStartedUtc.ToString("O")
}
Write-JsonFile -Path $opportunityPath -Value $opportunity

$demoConfig = [ordered]@{
    BackgroundJobs = [ordered]@{
        Enabled = $false
    }
    ArcSettlement = [ordered]@{
        Enabled = $true
        ChainId = 31337
        RpcUrl = "http://127.0.0.1:8545"
        BlockExplorerBaseUrl = "http://127.0.0.1:8545"
        SignalPublicationStorePath = (Join-Path $artifactDir "signal-publications-cli.json")
        EntitlementMirrorStorePath = (Join-Path $artifactDir "entitlements.json")
        PerformanceOutcomeStorePath = (Join-Path $artifactDir "performance-outcomes-cli.json")
        RevenueSettlementStorePath = (Join-Path $artifactDir "revenue-settlement-cli-journal.json")
        Contracts = [ordered]@{
            SignalRegistry = "0x1111111111111111111111111111111111111111"
            StrategyAccess = "0x2222222222222222222222222222222222222222"
            PerformanceLedger = "0x3333333333333333333333333333333333333333"
            RevenueSettlement = "0x4444444444444444444444444444444444444444"
        }
        SignalProof = [ordered]@{
            AgentAddress = $agentAddress
            Venue = "polymarket"
            OpportunityStrategyId = $strategyId
            DecisionValidForMinutes = 30
            DefaultRiskTier = "paper"
        }
    }
}
Write-JsonFile -Path $configPath -Value $demoConfig

$revenueDemoConfig = [ordered]@{
    BackgroundJobs = [ordered]@{
        Enabled = $false
    }
    ArcSettlement = [ordered]@{
        Enabled = $false
        RevenueSettlementStorePath = (Join-Path $artifactDir "revenue-settlement-cli-journal.json")
    }
}
Write-JsonFile -Path $revenueConfigPath -Value $revenueDemoConfig

if (-not $FinalizeOnly) {
foreach ($stateStorePath in @(
    (Join-Path $artifactDir "signal-publications-cli.json"),
    (Join-Path $artifactDir "entitlements.json"),
    (Join-Path $artifactDir "performance-outcomes-cli.json"),
    (Join-Path $artifactDir "revenue-settlement-cli-journal.json")
)) {
    if (Test-Path -LiteralPath $stateStorePath) {
        Remove-Item -LiteralPath $stateStorePath -Force
    }
}

if ($SkipRestore) {
    Add-DemoSkip `
        -Name "dotnet restore" `
        -Command (Join-CommandLine -FilePath $dotnet -Arguments @("restore", "Autotrade.sln")) `
        -Reason "Skipped by -SkipRestore. Use this only after dotnet restore Autotrade.sln has already passed in the current workspace." | Out-Null
}
else {
    Invoke-DemoStep -Name "dotnet restore" -FilePath $dotnet -Arguments @("restore", "Autotrade.sln") -TimeoutSeconds 900 | Out-Null
}
Invoke-DemoStep -Name "Arc contracts build" -FilePath $npm -Arguments @("--prefix", "interfaces\ArcContracts", "run", "build") -TimeoutSeconds 900 | Out-Null
Invoke-DemoStep -Name "Arc contracts test" -FilePath $npm -Arguments @("--prefix", "interfaces\ArcContracts", "test") -TimeoutSeconds 900 | Out-Null
Invoke-DemoStep -Name "dotnet build" -FilePath $dotnet -Arguments @("build", "Autotrade.sln", "--no-restore", "-v", "minimal") -TimeoutSeconds 900 | Out-Null
Invoke-DemoStep -Name "ArcSettlement targeted tests" -FilePath $dotnet -Arguments @("test", "context\ArcSettlement\Autotrade.ArcSettlement.Tests\Autotrade.ArcSettlement.Tests.csproj", "--no-build", "-v", "minimal") -TimeoutSeconds 900 | Out-Null
Invoke-DemoStep -Name "Builder attribution tests" -FilePath $dotnet -Arguments @("test", "Shared\Autotrade.Polymarket.Tests\Autotrade.Polymarket.Tests.csproj", "--no-build", "-v", "minimal") -TimeoutSeconds 900 | Out-Null
Invoke-DemoStep -Name "WebApp build" -FilePath $npm -Arguments @("--prefix", "interfaces\WebApp", "run", "build") -TimeoutSeconds 900 | Out-Null

$tempSignalPath = Join-Path $tempDir "signal-publication.json"
$tempSubscriptionPath = Join-Path $tempDir "subscription.json"
$tempPerformanceDir = Join-Path $tempDir "performance"
$tempRevenuePath = Join-Path $tempDir "revenue-settlement.json"
$tempClosedLoopPath = Join-Path $tempDir "local-evm-closed-loop.json"
New-Item -ItemType Directory -Force -Path $tempPerformanceDir | Out-Null

Invoke-DemoStep `
    -Name "Publish signal proof" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "demo:signal") `
    -Environment @{
        ARC_SIGNAL_DEMO_OUTPUT = $tempSignalPath
        ARC_SIGNAL_ID = $signalId
        ARC_SIGNAL_AGENT_ADDRESS = $agentAddress
        ARC_SIGNAL_STRATEGY_KEY = $strategyId
        ARC_SIGNAL_REASONING_HASH = $reasoningHash
        ARC_SIGNAL_RISK_ENVELOPE_HASH = $riskEnvelopeHash
        ARC_SIGNAL_EXPECTED_EDGE_BPS = "42"
        ARC_SIGNAL_MAX_NOTIONAL_USDC_ATOMIC = "100000000"
        ARC_SIGNAL_VALID_UNTIL_UNIX_SECONDS = "1800000000"
    } `
    -TimeoutSeconds 900 | Out-Null
Assert-Artifact $tempSignalPath
Copy-Item -LiteralPath $tempSignalPath -Destination (Join-Path $artifactDir "signal-publication.json") -Force

Invoke-DemoStep `
    -Name "Sync subscription on local EVM" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "demo:subscription") `
    -Environment @{ ARC_SUBSCRIPTION_DEMO_OUTPUT = $tempSubscriptionPath } `
    -TimeoutSeconds 900 | Out-Null
Assert-Artifact $tempSubscriptionPath
Copy-Item -LiteralPath $tempSubscriptionPath -Destination (Join-Path $artifactDir "subscription.json") -Force

$subscription = Read-JsonFile (Join-Path $artifactDir "subscription.json")
$subscriberWallet = [string]$subscription.subscriber
$subscriptionTx = [string]$subscription.transactionHash
$subscriptionBlock = [string]$subscription.blockNumber
$subscriptionExpiresAt = [string]$subscription.expiresAtUtc
$planId = [string]$subscription.request.planId

$denied = Invoke-DemoStep `
    -Name "Access denied JSON" `
    -FilePath $dotnet `
    -Arguments @("run", "--no-build", "--project", "interfaces\Autotrade.Cli", "--", "--config", $configPath, "--json", "arc", "access", "status", "--wallet", $unsubscribedWallet, "--strategy", $strategyId) `
    -TimeoutSeconds 600
Copy-CommandJsonOutput -Result $denied -Path (Join-Path $artifactDir "access-denied.json")

$sync = Invoke-DemoStep `
    -Name "Mirror subscription entitlement" `
    -FilePath $dotnet `
    -Arguments @("run", "--no-build", "--project", "interfaces\Autotrade.Cli", "--", "--config", $configPath, "--json", "arc", "access", "sync", "--wallet", $subscriberWallet, "--strategy", $strategyId, "--plan-id", $planId, "--tx-hash", $subscriptionTx, "--expires-at", $subscriptionExpiresAt, "--block", $subscriptionBlock) `
    -TimeoutSeconds 600
Copy-CommandJsonOutput -Result $sync -Path (Join-Path $artifactDir "entitlement-status.json")

$allowed = Invoke-DemoStep `
    -Name "Access allowed JSON" `
    -FilePath $dotnet `
    -Arguments @("run", "--no-build", "--project", "interfaces\Autotrade.Cli", "--", "--config", $configPath, "--json", "arc", "access", "status", "--wallet", $subscriberWallet, "--strategy", $strategyId) `
    -TimeoutSeconds 600
Copy-CommandJsonOutput -Result $allowed -Path (Join-Path $artifactDir "access-allowed.json")

$accessAllowedForPermission = Read-JsonFile (Join-Path $artifactDir "access-allowed.json")
$paperAutoTradePermission = [ordered]@{
    documentVersion = "proofalpha-arc-paper-autotrade-permission.v1"
    route = "POST /api/control-room/strategies/$strategyId/arc-paper-autotrade"
    allowed = [bool]($accessAllowedForPermission.data.permissions -contains "RequestPaperAutoTrade")
    status = if ([bool]($accessAllowedForPermission.data.permissions -contains "RequestPaperAutoTrade")) { "PermissionGranted" } else { "PermissionMissing" }
    walletAddress = [string]$accessAllowedForPermission.data.walletAddress
    strategyId = [string]$accessAllowedForPermission.data.strategyKey
    requiredPermission = "RequestPaperAutoTrade"
    commandMode = "Paper"
    liveTradingAllowed = $false
    liveTradingBlockedByDesign = $true
    evidenceTransactionHash = [string]$accessAllowedForPermission.data.sourceTransactionHash
    tier = [string]$accessAllowedForPermission.data.tier
    expiresAtUtc = [string]$accessAllowedForPermission.data.expiresAtUtc
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
}
Write-JsonFile -Path (Join-Path $artifactDir "autotrade-permission.json") -Value $paperAutoTradePermission

Invoke-DemoStep `
    -Name "Builder attributed paper order evidence" `
    -FilePath $dotnet `
    -Arguments @(
        "run", "--no-build", "--project", "interfaces\Autotrade.Cli", "--",
        "--config", $configPath, "--json", "--yes",
        "arc", "builder", "evidence",
        "--demo",
        "--client-order-id", "demo-client-order-phase11",
        "--strategy-id", $strategyId,
        "--market-id", $marketId,
        "--arc-signal-id", $signalId,
        "--token-id", "1",
        "--side", "BUY",
        "--price", "0.42",
        "--size", "10",
        "--time-in-force", "GTC",
        "--created-at", "2026-05-12T00:00:00Z",
        "--exchange-order-id", "demo-exchange-order-phase11",
        "--run-session-id", "demo-run-session-phase11",
        "--command-audit-id", "demo-audit-phase11",
        "--output", (Join-Path $artifactDir "builder-attribution.json"),
        "--envelope-output", (Join-Path $artifactDir "order-envelope-redacted.json")
    ) `
    -TimeoutSeconds 600 | Out-Null

Invoke-DemoStep `
    -Name "Record performance outcome" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "demo:performance") `
    -Environment @{
        ARC_PERFORMANCE_DEMO_ROOT = $tempPerformanceDir
        ARC_PERFORMANCE_SIGNAL_ID = $signalId
        ARC_PERFORMANCE_SIGNAL_TX_HASH = [string](Read-JsonFile (Join-Path $artifactDir "signal-publication.json")).transactionHash
        ARC_PERFORMANCE_STRATEGY_ID = $strategyId
    } `
    -TimeoutSeconds 900 | Out-Null
Assert-Artifact (Join-Path $tempPerformanceDir "performance-outcome.json")
Assert-Artifact (Join-Path $tempPerformanceDir "agent-reputation.json")
Copy-Item -LiteralPath (Join-Path $tempPerformanceDir "performance-outcome.json") -Destination (Join-Path $artifactDir "performance-outcome.json") -Force
Copy-Item -LiteralPath (Join-Path $tempPerformanceDir "agent-reputation.json") -Destination (Join-Path $artifactDir "agent-reputation.json") -Force

Invoke-DemoStep `
    -Name "Record revenue settlement local EVM" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "demo:revenue") `
    -Environment @{
        ARC_REVENUE_DEMO_RESULT = $tempRevenuePath
        ARC_REVENUE_SIGNAL_ID = $signalId
        ARC_REVENUE_SOURCE_TX_HASH = $subscriptionTx
        ARC_REVENUE_GROSS_MICRO_USDC = $subscriptionGrossMicroUsdc
    } `
    -TimeoutSeconds 900 | Out-Null
Assert-Artifact $tempRevenuePath
Copy-Item -LiteralPath $tempRevenuePath -Destination (Join-Path $artifactDir "revenue-settlement.json") -Force

Invoke-DemoStep `
    -Name "Run local EVM paid-agent closed loop" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "demo:closed-loop") `
    -Environment @{ ARC_LOCAL_CLOSED_LOOP_OUTPUT = $tempClosedLoopPath } `
    -TimeoutSeconds 900 | Out-Null
Assert-Artifact $tempClosedLoopPath
Copy-Item -LiteralPath $tempClosedLoopPath -Destination (Join-Path $artifactDir "local-evm-closed-loop.json") -Force

Invoke-DemoStep `
    -Name "Record revenue settlement CLI journal" `
    -FilePath $dotnet `
    -Arguments @(
        "run", "--no-build", "--project", "interfaces\Autotrade.Cli", "--",
        "--config", $revenueConfigPath, "--json",
        "arc", "revenue", "record",
        "--source-kind", "SubscriptionFee",
        "--signal-id", $signalId,
        "--gross-usdc", $subscriptionGrossUsdc,
        "--source-tx-hash", $subscriptionTx,
        "--reason", "phase 11 demo subscription settlement",
        "--wallet", $subscriberWallet,
        "--strategy-id", $strategyId
    ) `
    -TimeoutSeconds 600 | Out-Null

Invoke-DemoStep -Name "Full dotnet test gate" -FilePath $dotnet -Arguments @("test", "Autotrade.sln", "--no-build", "-v", "minimal") -TimeoutSeconds 900 | Out-Null
Invoke-DemoStep -Name "WebApp screenshot capture" -FilePath $node -Arguments @("scripts\arc-hackathon-webapp-check.mjs") -TimeoutSeconds 600 | Out-Null
}

$secretScan = New-SecretScan

$signalProofArtifact = Read-JsonFile (Join-Path $artifactDir "signal-proof.json")
$signalPublication = Read-JsonFile (Join-Path $artifactDir "signal-publication.json")
$subscriptionArtifact = Read-JsonFile (Join-Path $artifactDir "subscription.json")
$performanceOutcome = Read-JsonFile (Join-Path $artifactDir "performance-outcome.json")
$revenueSettlement = Read-JsonFile (Join-Path $artifactDir "revenue-settlement.json")
$localClosedLoop = Read-JsonFile (Join-Path $artifactDir "local-evm-closed-loop.json")
$builderEvidence = Read-JsonFile (Join-Path $artifactDir "builder-attribution.json")
$accessDenied = Read-JsonFile (Join-Path $artifactDir "access-denied.json")
$accessAllowed = Read-JsonFile (Join-Path $artifactDir "access-allowed.json")
$autoTradePermission = Read-JsonFile (Join-Path $artifactDir "autotrade-permission.json")
$subscriptionTx = [string]$subscriptionArtifact.transactionHash

Assert-DemoInvariant -Condition ([string]$signalProofArtifact.strategyId -eq $strategyId) -Message "Signal proof strategy id does not match the demo strategy."
Assert-DemoInvariant -Condition ([string]$signalPublication.signalId -eq $signalId) -Message "Signal publication id does not match the demo signal id."
Assert-DemoInvariant -Condition (-not [bool]$accessDenied.data.hasAccess) -Message "Access denied artifact unexpectedly grants access."
Assert-DemoInvariant -Condition ([bool]$accessAllowed.data.hasAccess) -Message "Access allowed artifact does not grant access."
Assert-DemoInvariant -Condition ([string]$accessAllowed.data.strategyKey -eq $strategyId) -Message "Access allowed artifact is not linked to the demo strategy id."
Assert-DemoInvariant -Condition ([bool]($accessAllowed.data.permissions -contains "RequestPaperAutoTrade")) -Message "Access allowed artifact does not grant paper auto-trade permission."
Assert-DemoInvariant -Condition ([string]$builderEvidence.arcSignalId -eq $signalId) -Message "Builder attribution artifact is not linked to the demo signal id."
Assert-DemoInvariant -Condition ([string]$builderEvidence.strategyId -eq $strategyId) -Message "Builder attribution artifact is not linked to the demo strategy id."
Assert-DemoInvariant -Condition ([string]$performanceOutcome.signalId -eq $signalId) -Message "Performance outcome artifact is not linked to the demo signal id."
Assert-DemoInvariant -Condition ([string]$performanceOutcome.strategyId -eq $strategyId) -Message "Performance outcome artifact is not linked to the demo strategy id."
Assert-DemoInvariant -Condition ([string]$revenueSettlement.signalId -eq $signalId) -Message "Revenue settlement artifact is not linked to the demo signal id."
Assert-DemoInvariant -Condition ([string]$revenueSettlement.strategyId -eq $strategyId) -Message "Revenue settlement artifact is not linked to the demo strategy id."
Assert-DemoInvariant -Condition ([string]$localClosedLoop.signalPublication.signalId -eq $signalId) -Message "Local closed-loop signal is not linked to the demo signal id."
Assert-DemoInvariant -Condition ([string]$localClosedLoop.signalPublication.strategyId -eq $strategyId) -Message "Local closed-loop signal is not linked to the demo strategy id."
Assert-DemoInvariant -Condition ([bool]$localClosedLoop.subscription.hasAccess) -Message "Local closed-loop subscription did not grant access."
Assert-DemoInvariant -Condition ([string]$localClosedLoop.balances.subscriberSubscriptionDeltaAtomic -eq "-25000000") -Message "Local closed-loop subscriber payment delta is not 25 USDC."
Assert-DemoInvariant -Condition ([string]$localClosedLoop.balances.settlementVaultSubscriptionDeltaAtomic -eq "25000000") -Message "Local closed-loop settlement vault did not receive 25 USDC."
Assert-DemoInvariant -Condition ([string]$localClosedLoop.balances.settlementVaultDistributionDeltaAtomic -eq "-25000000") -Message "Local closed-loop settlement vault did not distribute 25 USDC."
Assert-DemoInvariant -Condition ([bool]$autoTradePermission.allowed) -Message "Paper auto-trade permission artifact is not allowed."
Assert-DemoInvariant -Condition ([string]$autoTradePermission.evidenceTransactionHash -eq $subscriptionTx) -Message "Paper auto-trade permission is not linked to the subscription transaction."
Assert-DemoInvariant -Condition ([string]$secretScan.status -eq "Passed") -Message "Secret scan did not pass."

$evidence = @(
    (Get-FileEvidence -Path (Join-Path $artifactDir "signal-proof.json") -Description "canonical signal proof input")
    (Get-FileEvidence -Path (Join-Path $artifactDir "signal-publication.json") -Description "local EVM SignalPublished event artifact")
    (Get-FileEvidence -Path (Join-Path $artifactDir "subscription.json") -Description "local EVM StrategySubscribed event artifact")
    (Get-FileEvidence -Path (Join-Path $artifactDir "access-denied.json") -Description "unsubscribed wallet access decision")
    (Get-FileEvidence -Path (Join-Path $artifactDir "access-allowed.json") -Description "subscribed wallet access decision")
    (Get-FileEvidence -Path (Join-Path $artifactDir "autotrade-permission.json") -Description "paper auto-trade permission entitlement")
    (Get-FileEvidence -Path (Join-Path $artifactDir "builder-attribution.json") -Description "redacted Polymarket builder attribution evidence")
    (Get-FileEvidence -Path (Join-Path $artifactDir "order-envelope-redacted.json") -Description "redacted signed order envelope hash evidence")
    (Get-FileEvidence -Path (Join-Path $artifactDir "performance-outcome.json") -Description "local EVM OutcomeRecorded artifact")
    (Get-FileEvidence -Path (Join-Path $artifactDir "agent-reputation.json") -Description "performance/reputation aggregate")
    (Get-FileEvidence -Path (Join-Path $artifactDir "revenue-settlement.json") -Description "local EVM SettlementRecorded artifact")
    (Get-FileEvidence -Path (Join-Path $artifactDir "local-evm-closed-loop.json") -Description "single-process local EVM paid-agent closed loop")
    (Get-FileEvidence -Path (Join-Path $artifactDir "revenue-settlement-cli-journal.json") -Description "API/WebApp revenue read-model journal")
    (Get-FileEvidence -Path $secretScanJsonPath -Description "machine-readable secret scan")
    (Get-FileEvidence -Path $secretScanMarkdownPath -Description "human-readable secret scan")
)

$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# ProofAlpha Arc Hackathon Demo Summary") | Out-Null
$summary.Add("") | Out-Null
$summary.Add("- Run id: $runId") | Out-Null
$summary.Add("- Generated at UTC: $((Get-Date).ToUniversalTime().ToString("O"))") | Out-Null
$summary.Add("- Repository: $repoRoot") | Out-Null
$summary.Add("- Artifact root: $artifactDir") | Out-Null
$summary.Add("- Status: Passed") | Out-Null
$summary.Add("") | Out-Null
$summary.Add("## Demo Evidence") | Out-Null
$summary.Add("") | Out-Null
$summary.Add("- Signal id: ``$($signalPublication.signalId)``") | Out-Null
$summary.Add("- Signal publication tx: ``$($signalPublication.transactionHash)``") | Out-Null
$summary.Add("- Subscription tx: ``$subscriptionTx``") | Out-Null
$summary.Add("- Access denied allowed flag: ``$($accessDenied.data.hasAccess)``") | Out-Null
$summary.Add("- Access allowed flag: ``$($accessAllowed.data.hasAccess)``") | Out-Null
$summary.Add("- Paper auto-trade permission: ``$($autoTradePermission.allowed)``") | Out-Null
$summary.Add("- Builder evidence signal id: ``$($builderEvidence.arcSignalId)``") | Out-Null
$summary.Add("- Performance outcome signal id: ``$($performanceOutcome.signalId)``") | Out-Null
$summary.Add("- Performance outcome tx: ``$($performanceOutcome.transactionHash)``") | Out-Null
$summary.Add("- Revenue settlement signal id: ``$($revenueSettlement.signalId)``") | Out-Null
$summary.Add("- Revenue settlement tx: ``$($revenueSettlement.transactionHash)``") | Out-Null
$summary.Add("- Local closed-loop subscription tx: ``$($localClosedLoop.subscription.transactionHash)``") | Out-Null
$summary.Add("- Local closed-loop settlement tx: ``$($localClosedLoop.revenueSettlement.transactionHash)``") | Out-Null
$summary.Add("- Secret scan status: ``$($secretScan.status)``") | Out-Null
$summary.Add("") | Out-Null
$summary.Add("## Command Gate") | Out-Null
$summary.Add("") | Out-Null
foreach ($result in $commandResults) {
    $relativeStdout = Get-RepoRelativePath $result.stdoutPath
    $relativeStderr = Get-RepoRelativePath $result.stderrPath
    $summary.Add("- $($result.name): $($result.status) ($($result.durationMs) ms), logs ``$relativeStdout``, ``$relativeStderr``") | Out-Null
}
$summary.Add("") | Out-Null
$summary.Add("## Files") | Out-Null
$summary.Add("") | Out-Null
foreach ($item in $evidence) {
    $relative = Get-RepoRelativePath $item.path
    $summary.Add("- ``$relative`` exists=$($item.exists) sha256=$($item.sha256) - $($item.description)") | Out-Null
}
$summary.Add("") | Out-Null
$summary.Add("## Disclosures") | Out-Null
$summary.Add("") | Out-Null
$summary.Add("- Polymarket remains the execution venue; Arc records proof, access, performance, and settlement evidence.") | Out-Null
$summary.Add("- The demo uses Paper execution plus local Hardhat EVM evidence unless configured for a real testnet.") | Out-Null
$summary.Add("- No investment advice, profitability guarantee, custody claim, or live-trading bypass is made.") | Out-Null
Write-Utf8File -Path $summaryPath -Content (($summary -join [Environment]::NewLine) + [Environment]::NewLine)

Write-JsonFile -Path (Join-Path $artifactDir "demo-run-record.json") -Value ([ordered]@{
    schemaVersion = "proofalpha.arcHackathon.demoRun.v1"
    runId = $runId
    status = "Passed"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    artifactRoot = $artifactDir
    screenshotRoot = $screenshotDir
    summaryPath = $summaryPath
    commandResults = @($commandResults)
    evidence = @($evidence)
    secretScan = $secretScan
})

Write-Output "Arc hackathon demo completed. Summary: $summaryPath"
