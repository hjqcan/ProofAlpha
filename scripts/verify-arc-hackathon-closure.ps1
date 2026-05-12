[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/arc-hackathon/demo-run",
    [string]$DeploymentRoot = "interfaces/ArcContracts/deployments",
    [switch]$RequireArcTestnet
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

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required artifact is missing: $(Get-RepoRelativePath $Path)"
    }

    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Add-Check {
    param(
        [Parameter(Mandatory = $true)]$Target,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Requirement,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [string]$Details = ""
    )

    $Target.Add([pscustomobject]@{
        id = $Id
        requirement = $Requirement
        status = $Status
        evidence = $Evidence
        details = $Details
    }) | Out-Null
}

function Has-Value {
    param(
        [Parameter(Mandatory = $true)]$Values,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    foreach ($value in @($Values)) {
        if ([string]::Equals([string]$value, $Expected, [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

$artifactDir = Resolve-RepoPath $ArtifactRoot
$deploymentDir = Resolve-RepoPath $DeploymentRoot
$auditJsonPath = Join-Path $artifactDir "completion-audit.json"
$auditMarkdownPath = Join-Path $artifactDir "completion-audit.md"
$arcTestnetChainId = 5042002
$arcTestnetUsdc = "0x3600000000000000000000000000000000000000"

$checks = [System.Collections.Generic.List[object]]::new()

$summaryText = Get-Content -Raw -LiteralPath (Join-Path $artifactDir "demo-summary.md")
$signalProof = Read-JsonFile (Join-Path $artifactDir "signal-proof.json")
$signalPublication = Read-JsonFile (Join-Path $artifactDir "signal-publication.json")
$subscription = Read-JsonFile (Join-Path $artifactDir "subscription.json")
$accessDenied = Read-JsonFile (Join-Path $artifactDir "access-denied.json")
$accessAllowed = Read-JsonFile (Join-Path $artifactDir "access-allowed.json")
$autoTradePermission = Read-JsonFile (Join-Path $artifactDir "autotrade-permission.json")
$builderEvidence = Read-JsonFile (Join-Path $artifactDir "builder-attribution.json")
$performanceOutcome = Read-JsonFile (Join-Path $artifactDir "performance-outcome.json")
$agentReputation = Read-JsonFile (Join-Path $artifactDir "agent-reputation.json")
$revenueSettlement = Read-JsonFile (Join-Path $artifactDir "revenue-settlement.json")
$revenueJournal = Read-JsonFile (Join-Path $artifactDir "revenue-settlement-cli-journal.json")
$localClosedLoop = Read-JsonFile (Join-Path $artifactDir "local-evm-closed-loop.json")
$secretScanText = Get-Content -Raw -LiteralPath (Join-Path $artifactDir "secret-scan.md")
$controlRoomControllerPath = Resolve-RepoPath "interfaces/Autotrade.Api/Controllers/ControlRoomController.cs"
$controlRoomCommandServicePath = Resolve-RepoPath "interfaces/Autotrade.Api/ControlRoom/ControlRoomCommandService.cs"
$controlRoomApiTestsPath = Resolve-RepoPath "interfaces/Autotrade.Api.Tests/ControlRoomControllerContractTests.cs"
$controlRoomServiceTestsPath = Resolve-RepoPath "interfaces/Autotrade.Api.Tests/ControlRoomCommandServiceTests.cs"
$controlRoomControllerText = if (Test-Path -LiteralPath $controlRoomControllerPath -PathType Leaf) { Get-Content -Raw -LiteralPath $controlRoomControllerPath } else { "" }
$controlRoomCommandServiceText = if (Test-Path -LiteralPath $controlRoomCommandServicePath -PathType Leaf) { Get-Content -Raw -LiteralPath $controlRoomCommandServicePath } else { "" }
$controlRoomApiTestsText = if (Test-Path -LiteralPath $controlRoomApiTestsPath -PathType Leaf) { Get-Content -Raw -LiteralPath $controlRoomApiTestsPath } else { "" }
$controlRoomServiceTestsText = if (Test-Path -LiteralPath $controlRoomServiceTestsPath -PathType Leaf) { Get-Content -Raw -LiteralPath $controlRoomServiceTestsPath } else { "" }

$strategyId = [string]$signalProof.strategyId
$signalId = [string]$signalProof.opportunityHash
$subscriptionTx = [string]$subscription.transactionHash

Add-Check -Target $checks `
    -Id "objective" `
    -Requirement "Deliver a paid Arc-backed agent gateway: pre-outcome proof, USDC subscription, gated opportunity or Paper auto-trade permission, performance, and revenue evidence." `
    -Status "Info" `
    -Evidence "Objective decomposed into the checks below." `
    -Details "The local evidence gate can pass before real Arc testnet deployment; real Arc deployment is tracked separately."

$hasPassedSummary = $summaryText -match "(?m)^- Status: Passed\r?$"
$hasFailedCommand = $summaryText -match "(?m)^- .+: (failed|timed_out) "
$restoreDetail = if ($summaryText -match "(?m)^- dotnet restore: skipped ") {
    "restore=skipped by demo runner; verify dotnet restore separately for release evidence"
}
elseif ($summaryText -match "(?m)^- dotnet restore: passed ") {
    "restore=passed"
}
else {
    "restore=not recorded"
}
$commandStatus = if ($hasPassedSummary -and -not $hasFailedCommand) { "Passed" } else { "Failed" }
Add-Check -Target $checks `
    -Id "command-gate" `
    -Requirement "Build, contract tests, .NET tests, WebApp build, screenshots, and demo commands must pass." `
    -Status $commandStatus `
    -Evidence "artifacts/arc-hackathon/demo-run/demo-summary.md" `
    -Details "$restoreDetail; failedOrTimedOutCommands=$hasFailedCommand."

Add-Check -Target $checks `
    -Id "signal-proof" `
    -Requirement "Signal proof is created before outcome and names the strategy, market, reasoning hash, risk envelope hash, expected edge, and max notional." `
    -Status ($(if ($signalProof.documentVersion -eq "arc-strategy-signal-proof.v1" -and $signalProof.reasoningHash -and $signalProof.riskEnvelopeHash -and $signalProof.expectedEdgeBps -gt 0 -and $signalProof.maxNotionalUsdc -gt 0) { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/signal-proof.json" `
    -Details "signalId=$signalId; strategyId=$strategyId."

Add-Check -Target $checks `
    -Id "signal-publication" `
    -Requirement "Signal proof is anchored to a blockchain-style SignalRegistry transaction before outcome recording." `
    -Status ($(if ($signalPublication.confirmed -and $signalPublication.transactionHash -and $signalPublication.signalId -eq $signalId) { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/signal-publication.json" `
    -Details "network=$($signalPublication.networkName); chainId=$($signalPublication.chainId); tx=$($signalPublication.transactionHash)."

Add-Check -Target $checks `
    -Id "usdc-subscription" `
    -Requirement "A user pays a USDC-style subscription for agent access." `
    -Status ($(if ($subscription.confirmed -and $subscription.request.tier -eq "PaperAutotrade" -and [string]$subscription.request.priceUsdc -eq "25.00" -and [string]$subscription.event.amount -eq "25000000" -and [string]$subscription.paymentTransfer.treasuryDeltaAtomic -eq "25000000" -and [string]$subscription.paymentTransfer.subscriberDeltaAtomic -eq "-25000000") { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/subscription.json" `
    -Details "tier=$($subscription.request.tier); amount=$($subscription.request.priceUsdc); treasuryDelta=$($subscription.paymentTransfer.treasuryDeltaAtomic); subscriberDelta=$($subscription.paymentTransfer.subscriberDeltaAtomic); tx=$subscriptionTx."

Add-Check -Target $checks `
    -Id "access-denied" `
    -Requirement "An unsubscribed wallet is blocked by backend entitlement state." `
    -Status ($(if (-not [bool]$accessDenied.data.hasAccess) { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/access-denied.json" `
    -Details "hasAccess=$($accessDenied.data.hasAccess)."

Add-Check -Target $checks `
    -Id "access-allowed" `
    -Requirement "A subscribed wallet unlocks signal details and Paper auto-trade permission." `
    -Status ($(if ([bool]$accessAllowed.data.hasAccess -and (Has-Value $accessAllowed.data.permissions "RequestPaperAutoTrade") -and $accessAllowed.data.sourceTransactionHash -eq $subscriptionTx) { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/access-allowed.json" `
    -Details "tier=$($accessAllowed.data.tier); permissions=$(@($accessAllowed.data.permissions) -join ',')."

Add-Check -Target $checks `
    -Id "paper-autotrade" `
    -Requirement "The agent gateway grants a Paper auto-trading permission without bypassing Live arming." `
    -Status ($(if ([bool]$autoTradePermission.allowed -and -not [bool]$autoTradePermission.liveTradingAllowed -and [bool]$autoTradePermission.liveTradingBlockedByDesign -and $autoTradePermission.evidenceTransactionHash -eq $subscriptionTx) { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/autotrade-permission.json" `
    -Details "route=$($autoTradePermission.route); commandMode=$($autoTradePermission.commandMode)."

$paperAutoTradeRouteImplemented = $controlRoomControllerText.Contains('[HttpPost("strategies/{strategyId}/arc-paper-autotrade")]')
$paperAutoTradeTestsPresent =
    $controlRoomApiTestsText.Contains("RequestArcPaperAutoTradeMapsAccessDeniedToForbidden") -and
    $controlRoomServiceTestsText.Contains("RequestArcPaperAutoTradeStartsStrategyThroughExistingStateCommand") -and
    $controlRoomServiceTestsText.Contains("RequestArcPaperAutoTradeBlocksLiveServicesCommandMode")
$paperAutoTradeApiValid =
    $paperAutoTradeRouteImplemented -and
    $controlRoomCommandServiceText.Contains("ArcEntitlementPermission.RequestPaperAutoTrade") -and
    $controlRoomCommandServiceText.Contains("LiveTradingBlocked") -and
    $controlRoomCommandServiceText.Contains("SetStrategyStateAsync") -and
    $controlRoomCommandServiceText.Contains('"Running"') -and
    $controlRoomCommandServiceText.Contains('"ARC_PAPER_AUTOTRADE"') -and
    $paperAutoTradeTestsPresent
Add-Check -Target $checks `
    -Id "paper-autotrade-api" `
    -Requirement "The Paper auto-trade permission maps to a real backend command route guarded by Arc entitlement and Live-mode blocking." `
    -Status ($(if ($paperAutoTradeApiValid) { "Passed" } else { "Failed" })) `
    -Evidence "ControlRoomController.cs, ControlRoomCommandService.cs, ControlRoom*Tests.cs" `
    -Details "routeImplemented=$paperAutoTradeRouteImplemented; testsPresent=$paperAutoTradeTestsPresent."

$strategyAligned = $builderEvidence.strategyId -eq $strategyId -and
    $performanceOutcome.strategyId -eq $strategyId -and
    $revenueSettlement.strategyId -eq $strategyId
$signalAligned = $builderEvidence.arcSignalId -eq $signalId -and
    $performanceOutcome.signalId -eq $signalId -and
    $revenueSettlement.signalId -eq $signalId
Add-Check -Target $checks `
    -Id "cross-artifact-linkage" `
    -Requirement "Signal, builder evidence, performance outcome, and revenue settlement must link the same strategy and signal." `
    -Status ($(if ($strategyAligned -and $signalAligned) { "Passed" } else { "Failed" })) `
    -Evidence "signal-proof.json, builder-attribution.json, performance-outcome.json, revenue-settlement.json" `
    -Details "strategyAligned=$strategyAligned; signalAligned=$signalAligned."

Add-Check -Target $checks `
    -Id "builder-attribution" `
    -Requirement "Polymarket order evidence carries builder attribution without raw signature leakage." `
    -Status ($(if ($builderEvidence.builderCodeHash -and $builderEvidence.orderEnvelope.signatureHash -and $builderEvidence.externalVerification.status -eq "not_used") { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/builder-attribution.json" `
    -Details "signatureHash present; raw signature is not included in the public artifact."

Add-Check -Target $checks `
    -Id "performance-ledger" `
    -Requirement "Strategy usefulness proof includes outcomes, including negative or rejected cases, instead of only winners." `
    -Status ($(if ($performanceOutcome.confirmed -and $performanceOutcome.status -eq "ExecutedLoss" -and $agentReputation.lossCount -ge 1 -and $agentReputation.rejectedSignals -ge 1 -and $agentReputation.expiredSignals -ge 1) { "Passed" } else { "Failed" })) `
    -Evidence "performance-outcome.json, agent-reputation.json" `
    -Details "outcome=$($performanceOutcome.status); losses=$($agentReputation.lossCount); rejected=$($agentReputation.rejectedSignals); expired=$($agentReputation.expiredSignals)."

$journalEntries = @($revenueJournal)
$revenueDistributionRecipients = @($revenueSettlement.distribution.recipients)
$revenueDistributionValid = [bool]$revenueSettlement.distribution.distributed -and
    [string]$revenueSettlement.distribution.vaultDeltaAtomic -eq "-25000000" -and
    $revenueDistributionRecipients.Count -eq 3 -and
    [string]$revenueDistributionRecipients[0].deltaAtomic -eq "17500000" -and
    [string]$revenueDistributionRecipients[1].deltaAtomic -eq "5000000" -and
    [string]$revenueDistributionRecipients[2].deltaAtomic -eq "2500000"
Add-Check -Target $checks `
    -Id "revenue-settlement" `
    -Requirement "Subscription revenue is recorded and distributed into agent owner, strategy author, and platform shares." `
    -Status ($(if ($revenueSettlement.confirmed -and [string]$revenueSettlement.grossUsdc -eq "25" -and $revenueSettlement.sourceTransactionHash -eq $subscriptionTx -and @($revenueSettlement.shares).Count -eq 3 -and $journalEntries.Count -eq 1 -and [decimal]$journalEntries[0].grossUsdc -eq 25 -and $revenueDistributionValid) { "Passed" } else { "Failed" })) `
    -Evidence "revenue-settlement.json, revenue-settlement-cli-journal.json" `
    -Details "grossUsdc=$($revenueSettlement.grossUsdc); journalEntries=$($journalEntries.Count); distributed=$($revenueSettlement.distribution.distributed)."

$localClosedLoopDeltas = @($localClosedLoop.revenueSettlement.recipientDeltas)
$localClosedLoopValid = [string]$localClosedLoop.documentVersion -eq "proofalpha-local-evm-closed-loop.v1" -and
    [int]$localClosedLoop.chainId -eq 31337 -and
    [string]$localClosedLoop.plan.tier -eq "PaperAutotrade" -and
    [string]$localClosedLoop.plan.priceUsdcAtomic -eq "25000000" -and
    [string]$localClosedLoop.subscription.amountAtomic -eq "25000000" -and
    [bool]$localClosedLoop.subscription.hasAccess -and
    [string]$localClosedLoop.balances.subscriberSubscriptionDeltaAtomic -eq "-25000000" -and
    [string]$localClosedLoop.balances.settlementVaultSubscriptionDeltaAtomic -eq "25000000" -and
    [string]$localClosedLoop.balances.settlementVaultDistributionDeltaAtomic -eq "-25000000" -and
    [string]$localClosedLoop.signalPublication.signalId -eq $signalId -and
    [string]$localClosedLoop.signalPublication.strategyId -eq $strategyId -and
    [string]$localClosedLoop.performanceOutcome.signalId -eq $signalId -and
    [string]$localClosedLoop.performanceOutcome.strategyId -eq $strategyId -and
    [string]$localClosedLoop.performanceOutcome.status -eq "ExecutedLoss" -and
    [string]$localClosedLoop.revenueSettlement.signalId -eq $signalId -and
    [string]$localClosedLoop.revenueSettlement.strategyId -eq $strategyId -and
    [bool]$localClosedLoop.revenueSettlement.distributed -and
    $localClosedLoopDeltas.Count -eq 3 -and
    [string]$localClosedLoopDeltas[0].deltaAtomic -eq "17500000" -and
    [string]$localClosedLoopDeltas[1].deltaAtomic -eq "5000000" -and
    [string]$localClosedLoopDeltas[2].deltaAtomic -eq "2500000"
Add-Check -Target $checks `
    -Id "local-evm-closed-loop" `
    -Requirement "A single local EVM run must prove the full paid-agent path: deploy, subscribe, publish signal, record outcome, and distribute subscription revenue." `
    -Status ($(if ($localClosedLoopValid) { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/local-evm-closed-loop.json" `
    -Details "chainId=$($localClosedLoop.chainId); subscriptionDelta=$($localClosedLoop.balances.subscriberSubscriptionDeltaAtomic); vaultSubscriptionDelta=$($localClosedLoop.balances.settlementVaultSubscriptionDeltaAtomic); distributed=$($localClosedLoop.revenueSettlement.distributed)."

Add-Check -Target $checks `
    -Id "secret-scan" `
    -Requirement "Demo artifacts and screenshots must not expose secrets." `
    -Status ($(if ($secretScanText -match "(?m)^- Status: Passed\r?$" -and $secretScanText -match "(?m)^- Blocking findings: 0\r?$") { "Passed" } else { "Failed" })) `
    -Evidence "artifacts/arc-hackathon/demo-run/secret-scan.md" `
    -Details "secret scan markdown reports Passed with 0 blocking findings."

$arcPreflightPath = Join-Path $artifactDir "arc-testnet-preflight.json"
$arcPreflightValid = $false
$arcPreflightDetails = "No arc-testnet-preflight.json artifact found."
if (Test-Path -LiteralPath $arcPreflightPath -PathType Leaf) {
    try {
        $arcPreflight = Read-JsonFile $arcPreflightPath
        $arcPreflightValid = [string]$arcPreflight.documentVersion -eq "proofalpha-arc-testnet-preflight.v1" -and
            [string]$arcPreflight.status -eq "Passed" -and
            [int]$arcPreflight.chainId -eq $arcTestnetChainId -and
            [string]::Equals(([string]$arcPreflight.usdc.address).ToLowerInvariant(), $arcTestnetUsdc.ToLowerInvariant(), [System.StringComparison]::Ordinal) -and
            [int]$arcPreflight.usdc.decimals -eq 6 -and
            [bool]$arcPreflight.usdc.codePresent
        $arcPreflightDetails = "preflightArtifact=$(Get-RepoRelativePath $arcPreflightPath); preflightValid=$arcPreflightValid."
    }
    catch {
        $arcPreflightDetails = "Arc Testnet preflight artifact exists but could not be parsed: $($_.Exception.Message)"
    }
}

$arcPreflightStatus = if ($arcPreflightValid) { "Passed" } elseif ($RequireArcTestnet) { "Missing" } else { "Warning" }
Add-Check -Target $checks `
    -Id "arc-testnet-preflight" `
    -Requirement "Arc Testnet RPC and official USDC ERC20 interface are reachable before real deployment claims." `
    -Status $arcPreflightStatus `
    -Evidence "artifacts/arc-hackathon/demo-run/arc-testnet-preflight.json" `
    -Details $arcPreflightDetails

$arcWalletPreflightPath = Join-Path $artifactDir "arc-testnet-wallet-preflight.json"
$arcWalletPreflightValid = $false
$arcWalletPreflightDetails = "No arc-testnet-wallet-preflight.json artifact found."
if (Test-Path -LiteralPath $arcWalletPreflightPath -PathType Leaf) {
    try {
        $arcWalletPreflight = Read-JsonFile $arcWalletPreflightPath
        $arcWalletPreflightValid = [string]$arcWalletPreflight.documentVersion -eq "proofalpha-arc-testnet-wallet-preflight.v1" -and
            [string]$arcWalletPreflight.status -eq "Passed" -and
            [int]$arcWalletPreflight.chainId -eq $arcTestnetChainId -and
            [string]::Equals(([string]$arcWalletPreflight.usdc.address).ToLowerInvariant(), $arcTestnetUsdc.ToLowerInvariant(), [System.StringComparison]::Ordinal) -and
            [int]$arcWalletPreflight.usdc.decimals -eq 6 -and
            [string]$arcWalletPreflight.usdc.requiredAtomic -eq "25000000" -and
            -not [string]::IsNullOrWhiteSpace([string]$arcWalletPreflight.walletAddress) -and
            -not [string]::IsNullOrWhiteSpace([string]$arcWalletPreflight.treasury) -and
            -not [string]::Equals(([string]$arcWalletPreflight.walletAddress).ToLowerInvariant(), ([string]$arcWalletPreflight.treasury).ToLowerInvariant(), [System.StringComparison]::Ordinal)
        $arcWalletPreflightDetails = "walletPreflightArtifact=$(Get-RepoRelativePath $arcWalletPreflightPath); walletPreflightValid=$arcWalletPreflightValid."
    }
    catch {
        $arcWalletPreflightDetails = "Arc Testnet wallet preflight artifact exists but could not be parsed: $($_.Exception.Message)"
    }
}

$arcWalletPreflightStatus = if ($arcWalletPreflightValid) { "Passed" } elseif ($RequireArcTestnet) { "Missing" } else { "Warning" }
Add-Check -Target $checks `
    -Id "arc-testnet-wallet" `
    -Requirement "The Arc Testnet subscriber wallet must be funded with native gas and at least 25 official USDC before deployment." `
    -Status $arcWalletPreflightStatus `
    -Evidence "artifacts/arc-hackathon/demo-run/arc-testnet-wallet-preflight.json" `
    -Details $arcWalletPreflightDetails

$arcDeployments = @()
if (Test-Path -LiteralPath $deploymentDir -PathType Container) {
    $arcDeployments = @(Get-ChildItem -LiteralPath $deploymentDir -Filter "*.json" -File | ForEach-Object {
        try {
            $deployment = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
            if ($deployment.networkName -eq "arcTestnet" -or [int]$deployment.chainId -eq $arcTestnetChainId) {
                $contractNames = @($deployment.contracts | ForEach-Object { [string]$_.contractName })
                $hasRequiredContracts = (Has-Value $contractNames "SignalRegistry") -and
                    (Has-Value $contractNames "StrategyAccess") -and
                    (Has-Value $contractNames "PerformanceLedger") -and
                    (Has-Value $contractNames "RevenueSettlement")
                $usesOfficialUsdc = [string]::Equals(
                    ([string]$deployment.paymentToken).ToLowerInvariant(),
                    $arcTestnetUsdc.ToLowerInvariant(),
                    [System.StringComparison]::Ordinal)
                $revenueSettlementContract = $deployment.contracts | Where-Object { $_.contractName -eq "RevenueSettlement" } | Select-Object -First 1
                $revenueSettlementAddress = if ($null -ne $revenueSettlementContract) { [string]$revenueSettlementContract.address } else { "" }
                $usesSettlementVault = -not [string]::IsNullOrWhiteSpace($revenueSettlementAddress) -and (
                    [string]::Equals(([string]$deployment.treasury).ToLowerInvariant(), $revenueSettlementAddress.ToLowerInvariant(), [System.StringComparison]::Ordinal) -or
                    [string]::Equals(([string]$deployment.strategyAccessTreasury).ToLowerInvariant(), $revenueSettlementAddress.ToLowerInvariant(), [System.StringComparison]::Ordinal)
                )
                $doesNotUseTestUsdc = -not (Has-Value $contractNames "TestUsdc")
                if ($hasRequiredContracts -and $usesOfficialUsdc -and $usesSettlementVault -and $doesNotUseTestUsdc) {
                    return [pscustomobject]@{
                        path = $_.FullName
                        deployment = $deployment
                    }
                }
            }
        }
        catch {
            return $null
        }
    } | Where-Object { $null -ne $_ })
}

$arcClosurePath = Join-Path $artifactDir "arc-testnet-closure.json"
$arcClosureValid = $false
$arcClosureDetails = "No arc-testnet-closure.json artifact found."
if (Test-Path -LiteralPath $arcClosurePath -PathType Leaf) {
    try {
        $arcClosure = Read-JsonFile $arcClosurePath
        $recipientDeltas = @($arcClosure.revenueSettlement.recipientDeltas)
        $recipientAddresses = @($recipientDeltas | ForEach-Object { ([string]$_.recipient).ToLowerInvariant() })
        $uniqueRecipientAddresses = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($recipientAddress in $recipientAddresses) {
            [void]$uniqueRecipientAddresses.Add($recipientAddress)
        }
        $recipientsIndependentlyAuditable = $recipientAddresses.Count -eq 3 -and
            $uniqueRecipientAddresses.Count -eq 3 -and
            -not ($recipientAddresses -contains ([string]$arcClosure.subscriber).ToLowerInvariant())
        $subscriberSubscriptionDelta = [decimal]$arcClosure.balances.subscriberSubscriptionDeltaAtomic
        $subscriberPaidAtLeastPlanPrice = $subscriberSubscriptionDelta -le -25000000
        $distributionValid = [bool]$arcClosure.revenueSettlement.distributed -and
            $recipientsIndependentlyAuditable -and
            $recipientDeltas.Count -eq 3 -and
            [string]$recipientDeltas[0].deltaAtomic -eq "17500000" -and
            [string]$recipientDeltas[1].deltaAtomic -eq "5000000" -and
            [string]$recipientDeltas[2].deltaAtomic -eq "2500000"
        $arcClosureValid = [string]$arcClosure.documentVersion -eq "proofalpha-arc-testnet-closure.v1" -and
            [int]$arcClosure.chainId -eq $arcTestnetChainId -and
            [string]::Equals(([string]$arcClosure.paymentToken).ToLowerInvariant(), $arcTestnetUsdc.ToLowerInvariant(), [System.StringComparison]::Ordinal) -and
            [string]$arcClosure.plan.tier -eq "PaperAutotrade" -and
            [string]$arcClosure.plan.priceUsdcAtomic -eq "25000000" -and
            [string]$arcClosure.subscription.amountAtomic -eq "25000000" -and
            [bool]$arcClosure.subscription.hasAccess -and
            $subscriberPaidAtLeastPlanPrice -and
            [string]$arcClosure.balances.settlementVaultSubscriptionDeltaAtomic -eq "25000000" -and
            [string]$arcClosure.balances.settlementVaultDistributionDeltaAtomic -eq "-25000000" -and
            $distributionValid -and
            [string]$arcClosure.signalPublication.signalId -eq $signalId -and
            [string]$arcClosure.signalPublication.strategyId -eq $strategyId -and
            [string]$arcClosure.performanceOutcome.signalId -eq $signalId -and
            [string]$arcClosure.performanceOutcome.strategyId -eq $strategyId -and
            [string]$arcClosure.performanceOutcome.status -eq "ExecutedLoss" -and
            [string]$arcClosure.revenueSettlement.signalId -eq $signalId -and
            [string]$arcClosure.revenueSettlement.strategyId -eq $strategyId -and
            [string]$arcClosure.revenueSettlement.grossAmountMicroUsdc -eq "25000000" -and
            -not [string]::IsNullOrWhiteSpace([string]$arcClosure.subscription.transactionHash) -and
            -not [string]::IsNullOrWhiteSpace([string]$arcClosure.signalPublication.transactionHash) -and
            -not [string]::IsNullOrWhiteSpace([string]$arcClosure.performanceOutcome.transactionHash) -and
            -not [string]::IsNullOrWhiteSpace([string]$arcClosure.revenueSettlement.transactionHash)
        $arcClosureDetails = "closureArtifact=$(Get-RepoRelativePath $arcClosurePath); closureValid=$arcClosureValid; subscriberSubscriptionDelta=$($arcClosure.balances.subscriberSubscriptionDeltaAtomic); vaultSubscriptionDelta=$($arcClosure.balances.settlementVaultSubscriptionDeltaAtomic)."
    }
    catch {
        $arcClosureDetails = "Arc Testnet closure artifact exists but could not be parsed: $($_.Exception.Message)"
    }
}

$arcStatus = if ($arcPreflightValid -and $arcDeployments.Count -gt 0 -and $arcClosureValid) { "Passed" } elseif ($RequireArcTestnet) { "Missing" } else { "Warning" }
Add-Check -Target $checks `
    -Id "arc-testnet" `
    -Requirement "Real Arc Testnet deployment and USDC subscription evidence must exist before claiming production Arc testnet completion." `
    -Status $arcStatus `
    -Evidence "interfaces/ArcContracts/deployments, artifacts/arc-hackathon/demo-run/arc-testnet-closure.json" `
    -Details ($(if ($arcPreflightValid -and $arcDeployments.Count -gt 0 -and $arcClosureValid) { "arcTestnet deployment artifacts=$($arcDeployments.Count); $arcPreflightDetails $arcClosureDetails" } else { "validArcDeployments=$($arcDeployments.Count); $arcPreflightDetails $arcClosureDetails Required env: ARC_TESTNET_RPC_URL, ARC_TESTNET_CHAIN_ID, ARC_SETTLEMENT_USDC_ADDRESS, ARC_SETTLEMENT_PRIVATE_KEY, ARC_SETTLEMENT_TREASURY." }))

$blockingStatuses = @("Failed", "Missing")
$blockingChecks = @($checks | Where-Object { $blockingStatuses -contains $_.status })
$arcMissing = @($checks | Where-Object { $_.id -eq "arc-testnet" -and $_.status -ne "Passed" }).Count -gt 0
$status = if ($blockingChecks.Count -gt 0) {
    "Failed"
}
elseif ($arcMissing) {
    "PassedLocalEvidence_ArcTestnetMissing"
}
else {
    "Passed"
}

$audit = [pscustomobject]@{
    schemaVersion = "proofalpha.arcHackathon.completionAudit.v1"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    status = $status
    requireArcTestnet = [bool]$RequireArcTestnet
    artifactRoot = $artifactDir
    deploymentRoot = $deploymentDir
    strategyId = $strategyId
    signalId = $signalId
    subscriptionTransactionHash = $subscriptionTx
    checks = $checks
    blockingChecks = $blockingChecks
}

Write-Utf8File -Path $auditJsonPath -Content (($audit | ConvertTo-Json -Depth 32) + [Environment]::NewLine)

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# Arc Hackathon Completion Audit") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("- Status: $status") | Out-Null
$markdown.Add("- Strategy: ``$strategyId``") | Out-Null
$markdown.Add("- Signal: ``$signalId``") | Out-Null
$markdown.Add("- Require Arc Testnet: $([bool]$RequireArcTestnet)") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("| Check | Status | Evidence | Details |") | Out-Null
$markdown.Add("| --- | --- | --- | --- |") | Out-Null
foreach ($check in $checks) {
    $markdown.Add("| $($check.id) | $($check.status) | ``$($check.evidence)`` | $($check.details -replace '\|', '/') |") | Out-Null
}

Write-Utf8File -Path $auditMarkdownPath -Content (($markdown -join [Environment]::NewLine) + [Environment]::NewLine)

Write-Host "Completion audit status: $status"
Write-Host "Audit JSON: $auditJsonPath"

if ($blockingChecks.Count -gt 0) {
    exit 1
}
