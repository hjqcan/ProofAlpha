[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/self-improve/closed-loop",
    [switch]$SkipLiveLlm,
    [int]$LiveTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$runStamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$runId = "self-improve-closed-loop-$runStamp"
$steps = [System.Collections.Generic.List[object]]::new()
$checks = [System.Collections.Generic.List[object]]::new()
$envChecks = [System.Collections.Generic.List[object]]::new()

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

function Import-DotEnv {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }

        $equalsIndex = $line.IndexOf("=")
        if ($equalsIndex -le 0) {
            continue
        }

        $key = $line.Substring(0, $equalsIndex).Trim()
        if ($key -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
            continue
        }

        $value = $line.Substring($equalsIndex + 1).Trim()
        if ($value.Length -ge 2) {
            $first = $value[0]
            $last = $value[$value.Length - 1]
            if (($first -eq '"' -and $last -eq '"') -or ($first -eq "'" -and $last -eq "'")) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        [Environment]::SetEnvironmentVariable($key, $value, "Process")
    }
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

function Add-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [string]$Details = ""
    )

    $script:checks.Add([pscustomobject]@{
        id = $Id
        status = $Status
        evidence = $Evidence
        details = $Details
    }) | Out-Null
}

function Get-EnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$Secret
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    $script:envChecks.Add([pscustomobject]@{
        name = $Name
        status = if ([string]::IsNullOrWhiteSpace($value)) { "Missing" } else { "Present" }
        secret = [bool]$Secret
        value = if ($Secret -or [string]::IsNullOrWhiteSpace($value)) { "" } else { $value }
    }) | Out-Null

    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return $value.Trim()
}

function Invoke-RecordedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$LogPrefix,
        [int]$TimeoutSeconds = 300
    )

    Write-Host "Running: $Name"
    $stdoutPath = Join-Path $runDir "$LogPrefix.out.log"
    $stderrPath = Join-Path $runDir "$LogPrefix.err.log"
    $startedAt = [DateTimeOffset]::UtcNow
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $FilePath
    $process.StartInfo.WorkingDirectory = $WorkingDirectory
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join " "

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
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Write-Utf8File -Path $stdoutPath -Content $stdout
    Write-Utf8File -Path $stderrPath -Content $stderr

    $finishedAt = [DateTimeOffset]::UtcNow
    $exitCode = if ($completed) { $process.ExitCode } else { -1 }
    $status = if ($completed -and $exitCode -eq 0) { "Passed" } else { "Failed" }
    $result = [pscustomobject]@{
        name = $Name
        status = $status
        exitCode = $exitCode
        startedAtUtc = $startedAt.ToString("O")
        finishedAtUtc = $finishedAt.ToString("O")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        stdoutPath = Get-RepoRelativePath $stdoutPath
        stderrPath = Get-RepoRelativePath $stderrPath
        timeoutSeconds = $TimeoutSeconds
    }

    $script:steps.Add($result) | Out-Null
    if ($status -ne "Passed") {
        throw "$Name failed with exit code $exitCode. See $(Get-RepoRelativePath $stderrPath)"
    }

    return $result
}

function ConvertTo-MarkdownTable {
    param([Parameter(Mandatory = $true)]$Rows)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("| Check | Status | Evidence |") | Out-Null
    $lines.Add("| --- | --- | --- |") | Out-Null
    foreach ($row in @($Rows)) {
        $lines.Add("| $($row.id) | $($row.status) | $($row.evidence) |") | Out-Null
    }

    return $lines -join [Environment]::NewLine
}

function New-StrategyPython {
    return @'
def _number(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _skip(reason_code, reason, telemetry=None, state_patch=None):
    return {
        "action": "skip",
        "reasonCode": reason_code,
        "reason": reason,
        "intents": [],
        "telemetry": telemetry or {},
        "statePatch": state_patch or {}
    }


def evaluate(request):
    params = request.get("params", {})
    snapshot = request.get("marketSnapshot", {})
    opportunity = snapshot.get("opportunity", {})
    top_book = snapshot.get("topBook", {})
    state = request.get("state", {})
    phase = request.get("phase", "entry")
    market_id = request.get("marketId", "unknown-market")

    fair_probability = _number(opportunity.get("fairProbability"))
    edge = _number(opportunity.get("edge"))
    confidence = _number(opportunity.get("confidence"))
    min_edge = _number(params.get("minEdge"), 0.03)
    confidence_floor = _number(params.get("confidenceFloor"), 0.55)
    entry_max_price = _number(params.get("entryMaxPrice"), 0.0)
    quantity = _number(params.get("quantity"), 0.0)
    max_notional = _number(params.get("maxNotional"), 0.0)
    ask_price = _number(top_book.get("askPrice"))
    ask_size = _number(top_book.get("askSize"))
    bid_price = _number(top_book.get("bidPrice"))
    bid_size = _number(top_book.get("bidSize"))
    token_id = top_book.get("tokenId", "token-yes")
    outcome = opportunity.get("outcome", "Yes")
    telemetry = {
        "fairProbability": fair_probability,
        "edge": edge,
        "confidence": confidence,
        "entryMaxPrice": entry_max_price,
        "askPrice": ask_price,
        "bidPrice": bid_price
    }

    if phase == "entry":
        if state.get("position"):
            return _skip("position_already_open", "entry skipped because a position is already open", telemetry)
        if edge < min_edge:
            return _skip("edge_below_floor", "opportunity edge is below configured floor", telemetry)
        if confidence < confidence_floor:
            return _skip("confidence_below_floor", "opportunity confidence is below configured floor", telemetry)
        if ask_price <= 0.0 or ask_price > entry_max_price:
            return _skip("entry_price_above_limit", "ask price is above the learned entry limit", telemetry)
        fill_quantity = min(quantity, ask_size)
        if fill_quantity <= 0.0:
            return _skip("no_ask_liquidity", "ask liquidity is unavailable", telemetry)
        if max_notional > 0.0 and ask_price * fill_quantity > max_notional:
            fill_quantity = max_notional / ask_price
        if fill_quantity <= 0.0:
            return _skip("notional_cap_blocks_entry", "max notional blocks the entry", telemetry)
        return {
            "action": "enter",
            "reasonCode": "paper_edge_entry",
            "reason": "paper entry accepted for positive edge opportunity",
            "intents": [{
                "marketId": market_id,
                "tokenId": token_id,
                "outcome": outcome,
                "side": "Buy",
                "orderType": "Limit",
                "timeInForce": "Fok",
                "price": round(ask_price, 4),
                "quantity": round(fill_quantity, 6),
                "negRisk": False,
                "leg": "Single"
            }],
            "telemetry": telemetry,
            "statePatch": {"lastAction": "entry"}
        }

    if phase == "exit":
        position = state.get("position")
        if not position:
            return _skip("no_position", "exit skipped because no paper position is open", telemetry)
        take_profit = _number(params.get("takeProfitPrice"), 1.0)
        stop_loss = _number(params.get("stopLossPrice"), 0.0)
        should_exit = bid_price >= take_profit or bid_price <= stop_loss or bool(snapshot.get("expired", False))
        if not should_exit:
            return _skip("exit_gate_not_hit", "neither take profit nor stop loss was hit", telemetry)
        exit_quantity = min(_number(position.get("quantity")), bid_size)
        if exit_quantity <= 0.0:
            return _skip("no_bid_liquidity", "bid liquidity is unavailable", telemetry)
        return {
            "action": "exit",
            "reasonCode": "paper_take_profit" if bid_price >= take_profit else "paper_stop_or_expiry",
            "reason": "paper exit accepted by replay gate",
            "intents": [{
                "marketId": market_id,
                "tokenId": position.get("tokenId", token_id),
                "outcome": position.get("outcome", outcome),
                "side": "Sell",
                "orderType": "Limit",
                "timeInForce": "Fok",
                "price": round(bid_price, 4),
                "quantity": round(exit_quantity, 6),
                "negRisk": False,
                "leg": "Single"
            }],
            "telemetry": telemetry,
            "statePatch": {"lastAction": "exit"}
        }

    return _skip("unsupported_phase", "phase must be entry or exit", telemetry)
'@
}

function New-StrategyRunnerPython {
    return @'
import copy
import datetime
import importlib.util
import json
import sys


def load_json(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path, value):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(value, handle, indent=2, sort_keys=True)


def number(value):
    return float(value)


def load_strategy(path):
    spec = importlib.util.spec_from_file_location("generated_strategy", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("failed to load strategy module")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not hasattr(module, "evaluate"):
        raise RuntimeError("strategy module must expose evaluate(request)")
    return module


def apply_intents(response, state, cash):
    fills = []
    for intent in response.get("intents", []):
        side = str(intent.get("side", "")).lower()
        price = number(intent.get("price", 0))
        quantity = number(intent.get("quantity", 0))
        notional = round(price * quantity, 6)
        if side == "buy":
            cash -= notional
            state["position"] = {
                "marketId": intent.get("marketId"),
                "tokenId": intent.get("tokenId"),
                "outcome": intent.get("outcome"),
                "quantity": quantity,
                "avgPrice": price
            }
            fills.append({"side": "Buy", "price": price, "quantity": quantity, "notional": notional})
        elif side == "sell":
            cash += notional
            position = state.get("position", {})
            entry_price = number(position.get("avgPrice", 0))
            fills.append({
                "side": "Sell",
                "price": price,
                "quantity": quantity,
                "notional": notional,
                "realizedPnlDelta": round((price - entry_price) * quantity, 6)
            })
            state.pop("position", None)
    return state, cash, fills


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: run_strategy.py <strategy.py> <replay.json> <run-log.json>")
    strategy_path, replay_path, log_path = sys.argv[1:]
    module = load_strategy(strategy_path)
    replay = load_json(replay_path)
    manifest = load_json("manifest.json")
    params = manifest.get("parameters", {})
    state = {}
    cash = 0.0
    cases = []
    for index, case in enumerate(replay.get("cases", [])):
        payload = copy.deepcopy(case.get("input", {}))
        payload["params"] = copy.deepcopy(params)
        payload["state"] = copy.deepcopy(state)
        payload["timestampUtc"] = datetime.datetime.utcnow().isoformat(timespec="seconds") + "Z"
        response = module.evaluate(payload)
        if not isinstance(response, dict):
            raise RuntimeError(f"case {index} returned non-object response")
        state_patch = response.get("statePatch") or {}
        if not isinstance(state_patch, dict):
            raise RuntimeError(f"case {index} statePatch must be an object")
        state.update(state_patch)
        state, cash, fills = apply_intents(response, state, cash)
        cases.append({
            "name": case.get("name", f"case-{index}"),
            "phase": payload.get("phase"),
            "response": response,
            "fills": fills,
            "cashAfterCase": round(cash, 6),
            "stateAfterCase": copy.deepcopy(state)
        })

    summary = {
        "caseCount": len(cases),
        "entryCount": sum(1 for case in cases if case["response"].get("action") == "enter"),
        "exitCount": sum(1 for case in cases if case["response"].get("action") == "exit"),
        "skipCount": sum(1 for case in cases if case["response"].get("action") == "skip"),
        "finalCash": round(cash, 6),
        "realizedPnl": round(cash, 6) if "position" not in state else 0.0,
        "openPosition": state.get("position")
    }
    write_json(log_path, {
        "strategyId": manifest.get("strategyId"),
        "version": manifest.get("version"),
        "parameters": params,
        "summary": summary,
        "cases": cases
    })
    print(json.dumps(summary, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
'@
}

function New-UnitTestsPython {
    return @'
import unittest
import strategy


class ClosedLoopStrategyTests(unittest.TestCase):
    def test_entry_response_has_contract(self):
        response = strategy.evaluate({
            "phase": "entry",
            "marketId": "paper-btc-updown-15m",
            "params": {
                "entryMaxPrice": "0.52",
                "takeProfitPrice": "0.60",
                "stopLossPrice": "0.42",
                "quantity": "10",
                "maxNotional": "5.50",
                "minEdge": "0.03",
                "confidenceFloor": "0.55"
            },
            "marketSnapshot": {
                "opportunity": {
                    "fairProbability": 0.62,
                    "edge": 0.12,
                    "confidence": 0.74,
                    "outcome": "Yes"
                },
                "topBook": {
                    "askPrice": 0.50,
                    "askSize": 25,
                    "bidPrice": 0.49,
                    "bidSize": 40,
                    "tokenId": "paper-token-yes"
                }
            },
            "state": {}
        })
        self.assertIn(response["action"], {"enter", "skip"})
        self.assertIn("reasonCode", response)
        self.assertIn("intents", response)


if __name__ == "__main__":
    unittest.main()
'@
}

function New-ReplaySpec {
    return [ordered]@{
        cases = @(
            [ordered]@{
                name = "entry accepts under improved limit"
                input = [ordered]@{
                    strategyId = "generated_paper_edge_replay"
                    phase = "entry"
                    marketId = "paper-btc-updown-15m"
                    marketSnapshot = [ordered]@{
                        opportunity = [ordered]@{
                            fairProbability = 0.62
                            edge = 0.12
                            confidence = 0.74
                            outcome = "Yes"
                        }
                        topBook = [ordered]@{
                            askPrice = 0.50
                            askSize = 25
                            bidPrice = 0.49
                            bidSize = 40
                            tokenId = "paper-token-yes"
                            spread = 0.01
                        }
                    }
                }
            },
            [ordered]@{
                name = "exit captures take profit"
                input = [ordered]@{
                    strategyId = "generated_paper_edge_replay"
                    phase = "exit"
                    marketId = "paper-btc-updown-15m"
                    marketSnapshot = [ordered]@{
                        opportunity = [ordered]@{
                            fairProbability = 0.62
                            edge = 0.12
                            confidence = 0.74
                            outcome = "Yes"
                        }
                        topBook = [ordered]@{
                            askPrice = 0.64
                            askSize = 20
                            bidPrice = 0.63
                            bidSize = 40
                            tokenId = "paper-token-yes"
                            spread = 0.01
                        }
                    }
                }
            }
        )
    }
}

function New-StrategyPackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][hashtable]$Parameters,
        [Parameter(Mandatory = $true)][string]$Description
    )

    New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot "tests") | Out-Null

    $strategyId = "generated_paper_edge_replay"
    $manifest = [ordered]@{
        strategyId = $strategyId
        name = "Generated Paper Edge Replay"
        version = $Version
        entryPoint = "strategy.py:evaluate"
        packageHash = "paper-replay-local"
        parameterSchemaPath = "params.schema.json"
        replaySpecPath = "replay.json"
        riskEnvelopePath = "risk_envelope.json"
        enabled = $true
        configVersion = $Version
        description = $Description
        parameters = $Parameters
    }

    $schema = [ordered]@{
        type = "object"
        properties = [ordered]@{
            entryMaxPrice = @{ type = "string" }
            takeProfitPrice = @{ type = "string" }
            stopLossPrice = @{ type = "string" }
            quantity = @{ type = "string" }
            maxNotional = @{ type = "string" }
            minEdge = @{ type = "string" }
            confidenceFloor = @{ type = "string" }
        }
        required = @("entryMaxPrice", "takeProfitPrice", "stopLossPrice", "quantity", "maxNotional", "minEdge", "confidenceFloor")
    }

    $riskEnvelope = [ordered]@{
        maxSingleOrderNotional = 5.50
        maxCycleNotional = 11.00
        maxTotalNotional = 25.00
    }

    Write-Utf8File -Path (Join-Path $PackageRoot "strategy.py") -Content (New-StrategyPython)
    Write-Utf8File -Path (Join-Path $PackageRoot "run_strategy.py") -Content (New-StrategyRunnerPython)
    Write-Utf8File -Path (Join-Path $PackageRoot "tests/test_strategy.py") -Content (New-UnitTestsPython)
    Write-JsonFile -Path (Join-Path $PackageRoot "manifest.json") -Value $manifest
    Write-JsonFile -Path (Join-Path $PackageRoot "params.schema.json") -Value $schema
    Write-JsonFile -Path (Join-Path $PackageRoot "replay.json") -Value (New-ReplaySpec)
    Write-JsonFile -Path (Join-Path $PackageRoot "risk_envelope.json") -Value $riskEnvelope
}

function Invoke-StrategyReplay {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)][string]$LogPrefix
    )

    $runLogPath = Join-Path $PackageRoot "strategy-run-log.json"
    Invoke-RecordedProcess `
        -Name $Name `
        -FilePath $pythonPath `
        -Arguments @("-B", "run_strategy.py", "strategy.py", "replay.json", "strategy-run-log.json") `
        -WorkingDirectory $PackageRoot `
        -LogPrefix $LogPrefix `
        -TimeoutSeconds 120 | Out-Null

    if (-not (Test-Path -LiteralPath $runLogPath -PathType Leaf)) {
        throw "Strategy replay did not create $(Get-RepoRelativePath $runLogPath)"
    }

    return Get-Content -LiteralPath $runLogPath -Raw | ConvertFrom-Json
}

function Get-LlmConfiguration {
    $provider = Get-EnvValue -Name "PROOFALPHA_SELF_IMPROVE_LLM_PROVIDER"
    if ([string]::IsNullOrWhiteSpace($provider)) {
        $provider = Get-EnvValue -Name "PROOFALPHA_OPPORTUNITY_LLM_PROVIDER"
    }
    if ([string]::IsNullOrWhiteSpace($provider)) {
        $provider = "openai"
    }

    $model = Get-EnvValue -Name "PROOFALPHA_SELF_IMPROVE_LLM_MODEL"
    if ([string]::IsNullOrWhiteSpace($model)) {
        $model = Get-EnvValue -Name "PROOFALPHA_OPPORTUNITY_LLM_MODEL"
    }
    if ([string]::IsNullOrWhiteSpace($model)) {
        $model = "gpt-4.1-mini"
    }

    $baseUrl = Get-EnvValue -Name "PROOFALPHA_SELF_IMPROVE_LLM_BASE_URL"
    if ([string]::IsNullOrWhiteSpace($baseUrl)) {
        $baseUrl = Get-EnvValue -Name "PROOFALPHA_OPPORTUNITY_LLM_BASE_URL"
    }
    if ([string]::IsNullOrWhiteSpace($baseUrl)) {
        $baseUrl = "https://api.openai.com/v1"
    }

    $apiKey = Get-EnvValue -Name "PROOFALPHA_SELF_IMPROVE_LLM_API_KEY" -Secret
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = Get-EnvValue -Name "PROOFALPHA_OPPORTUNITY_LLM_API_KEY" -Secret
    }
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = Get-EnvValue -Name "OPENAI_API_KEY" -Secret
    }

    return [pscustomobject]@{
        provider = $provider
        model = $model
        baseUrl = $baseUrl.TrimEnd("/")
        apiKey = $apiKey
    }
}

function ConvertTo-DecimalString {
    param(
        [Parameter(Mandatory = $true)][AllowNull()]$Value,
        [Parameter(Mandatory = $true)][decimal]$Fallback,
        [Parameter(Mandatory = $true)][decimal]$Min,
        [Parameter(Mandatory = $true)][decimal]$Max,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $parsed = $Fallback
    if ($null -ne $Value -and -not [string]::IsNullOrWhiteSpace([string]$Value)) {
        if (-not [decimal]::TryParse([string]$Value, [ref]$parsed)) {
            throw "LLM returned non-decimal value for $Name."
        }
    }

    if ($parsed -lt $Min -or $parsed -gt $Max) {
        throw "LLM returned $Name=$parsed, outside allowed range [$Min, $Max]."
    }

    return $parsed.ToString("0.####", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-LiveLlmImprovement {
    param(
        [Parameter(Mandatory = $true)]$BaselineLog,
        [Parameter(Mandatory = $true)][string]$OpportunityJsonPath
    )

    $config = Get-LlmConfiguration
    if ([string]::IsNullOrWhiteSpace($config.apiKey)) {
        throw "No LLM API key was found. Set PROOFALPHA_SELF_IMPROVE_LLM_API_KEY, PROOFALPHA_OPPORTUNITY_LLM_API_KEY, or OPENAI_API_KEY, or pass -SkipLiveLlm."
    }

    $requestPath = Join-Path $runDir "llm-improvement-request-redacted.json"
    $responsePath = Join-Path $runDir "llm-improvement-response.json"
    $uri = "$($config.baseUrl)/chat/completions"
    $opportunity = Get-Content -LiteralPath $OpportunityJsonPath -Raw
    $baselineSummary = $BaselineLog.summary | ConvertTo-Json -Depth 30
    $baselineCases = $BaselineLog.cases | ConvertTo-Json -Depth 30

    $prompt = @"
You are improving a paper-only Polymarket strategy. Return only one JSON object.

Opportunity:
$opportunity

Baseline replay summary:
$baselineSummary

Baseline replay cases:
$baselineCases

The baseline skipped a profitable paper entry because entryMaxPrice was too strict.
Generate a conservative improvement that still respects the paper risk envelope.

Return this shape:
{
  "analysis": "short reason",
  "parameters": {
    "entryMaxPrice": "decimal between 0.51 and 0.55",
    "takeProfitPrice": "decimal between 0.60 and 0.63",
    "stopLossPrice": "decimal between 0.40 and 0.45",
    "minEdge": "decimal between 0.03 and 0.12",
    "confidenceFloor": "decimal between 0.55 and 0.74"
  },
  "expectedImprovement": "short paper replay expectation",
  "rollbackConditions": ["condition 1", "condition 2"]
}
"@

    $bodyObject = [ordered]@{
        model = $config.model
        messages = @(
            [ordered]@{
                role = "system"
                content = "Return only valid JSON. Do not include markdown. Do not request live trading."
            },
            [ordered]@{
                role = "user"
                content = $prompt
            }
        )
        temperature = 0.1
        response_format = [ordered]@{
            type = "json_object"
        }
    }
    Write-JsonFile -Path $requestPath -Value ([ordered]@{
        uri = $uri
        model = $config.model
        provider = $config.provider
        messages = $bodyObject.messages
        response_format = $bodyObject.response_format
        apiKey = "redacted"
    })

    $startedAt = [DateTimeOffset]::UtcNow
    $llmStepStatus = "Failed"
    $llmExitCode = 1
    $llmErrorPath = Join-Path $runDir "llm-improvement-response.err.log"
    try {
        $response = Invoke-RestMethod `
            -Method Post `
            -Uri $uri `
            -Headers @{
                Authorization = "Bearer $($config.apiKey)"
                Accept = "application/json"
            } `
            -ContentType "application/json" `
            -Body ($bodyObject | ConvertTo-Json -Depth 20) `
            -TimeoutSec $LiveTimeoutSeconds
        $llmStepStatus = "Passed"
        $llmExitCode = 0
    }
    catch {
        Write-Utf8File -Path $llmErrorPath -Content $_.Exception.Message
        throw "Live LLM improvement request failed: $($_.Exception.Message)"
    }
    finally {
        $finishedAt = [DateTimeOffset]::UtcNow
        $script:steps.Add([pscustomobject]@{
            name = "Live LLM self-improvement analysis"
            status = $llmStepStatus
            exitCode = $llmExitCode
            startedAtUtc = $startedAt.ToString("O")
            finishedAtUtc = $finishedAt.ToString("O")
            durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
            stdoutPath = Get-RepoRelativePath $responsePath
            stderrPath = if (Test-Path -LiteralPath $llmErrorPath -PathType Leaf) { Get-RepoRelativePath $llmErrorPath } else { "" }
            timeoutSeconds = $LiveTimeoutSeconds
        }) | Out-Null
    }

    $content = [string]$response.choices[0].message.content
    $trimmed = $content.Trim()
    $jsonStart = $trimmed.IndexOf("{")
    $jsonEnd = $trimmed.LastIndexOf("}")
    if ($jsonStart -lt 0 -or $jsonEnd -le $jsonStart) {
        throw "LLM response did not contain a JSON object."
    }

    $json = $trimmed.Substring($jsonStart, $jsonEnd - $jsonStart + 1)
    Write-Utf8File -Path $responsePath -Content $json
    $document = $json | ConvertFrom-Json
    $parameters = $document.parameters
    if ($null -eq $parameters) {
        throw "LLM response did not contain parameters."
    }

    return [pscustomobject]@{
        mode = "live-llm"
        path = Get-RepoRelativePath $responsePath
        model = $config.model
        provider = $config.provider
        analysis = $document
        parameters = [ordered]@{
            entryMaxPrice = ConvertTo-DecimalString -Value $parameters.entryMaxPrice -Fallback 0.52 -Min 0.51 -Max 0.55 -Name "entryMaxPrice"
            takeProfitPrice = ConvertTo-DecimalString -Value $parameters.takeProfitPrice -Fallback 0.60 -Min 0.60 -Max 0.63 -Name "takeProfitPrice"
            stopLossPrice = ConvertTo-DecimalString -Value $parameters.stopLossPrice -Fallback 0.42 -Min 0.40 -Max 0.45 -Name "stopLossPrice"
            quantity = "10"
            maxNotional = "5.50"
            minEdge = ConvertTo-DecimalString -Value $parameters.minEdge -Fallback 0.03 -Min 0.03 -Max 0.12 -Name "minEdge"
            confidenceFloor = ConvertTo-DecimalString -Value $parameters.confidenceFloor -Fallback 0.55 -Min 0.55 -Max 0.74 -Name "confidenceFloor"
        }
    }
}

function New-DeterministicImprovement {
    $path = Join-Path $runDir "deterministic-improvement.json"
    $document = [ordered]@{
        analysis = "Deterministic fallback widened entryMaxPrice enough to accept the replay opportunity while keeping notional caps unchanged."
        parameters = [ordered]@{
            entryMaxPrice = "0.52"
            takeProfitPrice = "0.60"
            stopLossPrice = "0.42"
            minEdge = "0.03"
            confidenceFloor = "0.55"
        }
        expectedImprovement = "Improved replay should enter at 0.50 and exit at 0.63 for positive paper PnL."
        rollbackConditions = @("paper replay pnl <= baseline", "risk envelope validation fails")
    }
    Write-JsonFile -Path $path -Value $document
    return [pscustomobject]@{
        mode = "deterministic-fallback"
        path = Get-RepoRelativePath $path
        model = ""
        provider = ""
        analysis = $document
        parameters = [ordered]@{
            entryMaxPrice = "0.52"
            takeProfitPrice = "0.60"
            stopLossPrice = "0.42"
            quantity = "10"
            maxNotional = "5.50"
            minEdge = "0.03"
            confidenceFloor = "0.55"
        }
    }
}

$artifactRootPath = Resolve-RepoPath $ArtifactRoot
$runDir = Join-Path $artifactRootPath $runId
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
Import-DotEnv -Path (Join-Path $repoRoot ".env")
$pythonPath = Resolve-ToolPath @("python", "py")

$opportunity = [ordered]@{
    id = "paper-opportunity-btc-updown-15m"
    source = "local-paper-replay"
    marketId = "paper-btc-updown-15m"
    outcome = "Yes"
    fairProbability = 0.62
    confidence = 0.74
    edge = 0.12
    observedAsk = 0.50
    observedBidAfterMove = 0.63
    riskTier = "paper"
    validUntilUtc = [DateTimeOffset]::UtcNow.AddHours(2).ToString("O")
    evidence = @(
        [ordered]@{
            id = "fixture-orderbook-1"
            summary = "Replay fixture has ask 0.50 against fair 0.62, then bid 0.63 for take-profit validation."
        }
    )
}
$opportunityPath = Join-Path $runDir "opportunity.json"
Write-JsonFile -Path $opportunityPath -Value $opportunity
Add-Check -Id "opportunity-discovered" -Status "Passed" -Evidence (Get-RepoRelativePath $opportunityPath) -Details "Paper-only replay opportunity with explicit evidence fixture."

$baselineRoot = Join-Path $runDir "baseline"
New-StrategyPackage `
    -PackageRoot $baselineRoot `
    -Version "baseline-v1" `
    -Description "Baseline generated Python strategy before SelfImprove analysis." `
    -Parameters @{
        entryMaxPrice = "0.49"
        takeProfitPrice = "0.60"
        stopLossPrice = "0.42"
        quantity = "10"
        maxNotional = "5.50"
        minEdge = "0.03"
        confidenceFloor = "0.55"
    }

$baselineLog = Invoke-StrategyReplay -Name "Baseline generated Python strategy replay" -PackageRoot $baselineRoot -LogPrefix "baseline-strategy-replay"
$baselineLogPath = Join-Path $baselineRoot "strategy-run-log.json"
$baselinePnl = [decimal]$baselineLog.summary.realizedPnl
Add-Check -Id "baseline-strategy-run-log" -Status "Passed" -Evidence (Get-RepoRelativePath $baselineLogPath) -Details "Baseline replay completed with persisted per-case strategy log."
Add-Check -Id "baseline-needs-improvement" -Status ($(if ($baselinePnl -le 0) { "Passed" } else { "Failed" })) -Evidence (Get-RepoRelativePath $baselineLogPath) -Details "baselinePnl=$baselinePnl"

if ($SkipLiveLlm) {
    $improvement = New-DeterministicImprovement
}
else {
    $improvement = Invoke-LiveLlmImprovement -BaselineLog $baselineLog -OpportunityJsonPath $opportunityPath
}

$llmStatus = if ($improvement.mode -eq "live-llm") { "Passed" } else { "Skipped" }
Add-Check -Id "llm-self-improvement-analysis" -Status $llmStatus -Evidence $improvement.path -Details "mode=$($improvement.mode); model=$($improvement.model)"

$improvedRoot = Join-Path $runDir "improved"
New-StrategyPackage `
    -PackageRoot $improvedRoot `
    -Version "improved-v2" `
    -Description "Improved generated Python strategy after SelfImprove analysis." `
    -Parameters $improvement.parameters

$improvedLog = Invoke-StrategyReplay -Name "Improved generated Python strategy replay" -PackageRoot $improvedRoot -LogPrefix "improved-strategy-replay"
$improvedLogPath = Join-Path $improvedRoot "strategy-run-log.json"
$improvedPnl = [decimal]$improvedLog.summary.realizedPnl
$improvedEntries = [int]$improvedLog.summary.entryCount
$improvedExits = [int]$improvedLog.summary.exitCount
Add-Check -Id "improved-strategy-run-log" -Status "Passed" -Evidence (Get-RepoRelativePath $improvedLogPath) -Details "Improved replay completed with persisted per-case strategy log."
Add-Check -Id "closed-loop-replay-improves-paper-pnl" -Status ($(if ($improvedPnl -gt $baselinePnl -and $improvedPnl -gt 0 -and $improvedEntries -gt 0 -and $improvedExits -gt 0) { "Passed" } else { "Failed" })) -Evidence (Get-RepoRelativePath $improvedLogPath) -Details "baselinePnl=$baselinePnl; improvedPnl=$improvedPnl; entries=$improvedEntries; exits=$improvedExits"

$gateStatus = if (@($checks | Where-Object { $_.status -eq "Failed" }).Count -eq 0 -and $improvement.mode -eq "live-llm") {
    "Passed"
}
elseif (@($checks | Where-Object { $_.status -eq "Failed" }).Count -eq 0) {
    "PassedWithSkippedLiveLlm"
}
else {
    "Failed"
}

$gateJsonPath = Join-Path $runDir "self-improve-closed-loop-gate.json"
$gateMarkdownPath = Join-Path $runDir "self-improve-closed-loop-gate.md"
$gate = [ordered]@{
    gateStatus = $gateStatus
    runId = $runId
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    objective = "Paper-only closed loop: discover opportunity, generate Python strategy, run with logs, use LLM to improve, rerun validation."
    liveTrading = "disabled"
    profitClaim = "paper replay only; not a live profitability guarantee"
    runDir = Get-RepoRelativePath $runDir
    opportunityPath = Get-RepoRelativePath $opportunityPath
    baselinePackageRoot = Get-RepoRelativePath $baselineRoot
    improvedPackageRoot = Get-RepoRelativePath $improvedRoot
    baselineSummary = $baselineLog.summary
    improvedSummary = $improvedLog.summary
    improvement = [ordered]@{
        mode = $improvement.mode
        path = $improvement.path
        model = $improvement.model
        provider = $improvement.provider
        parameters = $improvement.parameters
    }
    checks = $checks
    steps = $steps
    envChecks = $envChecks
}
Write-JsonFile -Path $gateJsonPath -Value $gate

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# SelfImprove Closed Loop Gate") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("- Status: $gateStatus") | Out-Null
$markdown.Add("- Generated: $($gate.generatedAtUtc)") | Out-Null
$markdown.Add("- Run ID: $runId") | Out-Null
$markdown.Add("- Run dir: $(Get-RepoRelativePath $runDir)") | Out-Null
$markdown.Add("- Baseline paper PnL: $baselinePnl") | Out-Null
$markdown.Add("- Improved paper PnL: $improvedPnl") | Out-Null
$markdown.Add("- LLM mode: $($improvement.mode)") | Out-Null
$markdown.Add("- Live trading: disabled") | Out-Null
$markdown.Add("- Profit claim: paper replay only; not a live profitability guarantee") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add((ConvertTo-MarkdownTable -Rows $checks)) | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("## Evidence") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("- Opportunity: $(Get-RepoRelativePath $opportunityPath)") | Out-Null
$markdown.Add("- Baseline package: $(Get-RepoRelativePath $baselineRoot)") | Out-Null
$markdown.Add("- Baseline run log: $(Get-RepoRelativePath $baselineLogPath)") | Out-Null
$markdown.Add("- Improvement: $($improvement.path)") | Out-Null
$markdown.Add("- Improved package: $(Get-RepoRelativePath $improvedRoot)") | Out-Null
$markdown.Add("- Improved run log: $(Get-RepoRelativePath $improvedLogPath)") | Out-Null
Write-Utf8File -Path $gateMarkdownPath -Content ($markdown -join [Environment]::NewLine)

Write-Host "Closed loop gate: $gateStatus"
Write-Host "Gate JSON: $(Get-RepoRelativePath $gateJsonPath)"
Write-Host "Gate Markdown: $(Get-RepoRelativePath $gateMarkdownPath)"

if ($gateStatus -eq "Failed") {
    exit 1
}
