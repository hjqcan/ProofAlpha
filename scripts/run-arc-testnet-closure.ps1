[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/arc-hackathon/demo-run",
    [string]$RpcUrl = "https://rpc.testnet.arc.network",
    [string]$ChainId = "5042002",
    [string]$UsdcAddress = "0x3600000000000000000000000000000000000000",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
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

function Require-EnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is required. $Reason"
    }

    return $value
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 900
    )

    Write-Host "Running: $Name"
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $FilePath
    $process.StartInfo.WorkingDirectory = $repoRoot
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
        throw "$Name timed out after $TimeoutSeconds seconds."
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Host $stdout
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Error $stderr
        }
        throw "$Name failed with exit code $($process.ExitCode)."
    }

    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        Write-Host $stdout
    }
}

$privateKey = Require-EnvironmentVariable `
    -Name "ARC_SETTLEMENT_PRIVATE_KEY" `
    -Reason "Set a dedicated Arc Testnet funded wallet private key. Do not use a mainnet or real-funds key."
$treasury = Require-EnvironmentVariable `
    -Name "ARC_SETTLEMENT_TREASURY" `
    -Reason "Set a recipient address different from the subscriber/deployer so subscription payment evidence is meaningful."

if ($privateKey -notmatch '^0x[0-9a-fA-F]{64}$') {
    throw "ARC_SETTLEMENT_PRIVATE_KEY must be a 0x-prefixed 32-byte private key."
}

if ($treasury -notmatch '^0x[0-9a-fA-F]{40}$') {
    throw "ARC_SETTLEMENT_TREASURY must be a valid EVM address."
}

$artifactDir = Resolve-RepoPath $ArtifactRoot
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$npm = Resolve-ToolPath @("npm.cmd", "npm")
$powershell = Resolve-ToolPath @("powershell.exe", "powershell")

$envOverrides = @{
    ARC_TESTNET_RPC_URL = $RpcUrl
    ARC_TESTNET_CHAIN_ID = $ChainId
    ARC_SETTLEMENT_USDC_ADDRESS = $UsdcAddress
    ARC_SETTLEMENT_PRIVATE_KEY = $privateKey
    ARC_SETTLEMENT_TREASURY = $treasury
    ARC_HACKATHON_ARTIFACT_ROOT = $artifactDir
    ARC_TESTNET_WALLET_PREFLIGHT_OUTPUT = (Join-Path $artifactDir "arc-testnet-wallet-preflight.json")
    ARC_TESTNET_CLOSURE_OUTPUT = (Join-Path $artifactDir "arc-testnet-closure.json")
}

if (-not $SkipBuild) {
    Invoke-Step `
        -Name "Arc contracts build" `
        -FilePath $npm `
        -Arguments @("--prefix", "interfaces\ArcContracts", "run", "build") `
        -Environment $envOverrides
}

Invoke-Step `
    -Name "Arc Testnet preflight" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "testnet:preflight") `
    -Environment $envOverrides `
    -TimeoutSeconds 300

Invoke-Step `
    -Name "Arc Testnet funded wallet preflight" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "testnet:wallet-preflight") `
    -Environment $envOverrides `
    -TimeoutSeconds 300

Invoke-Step `
    -Name "Deploy Arc contracts to Arc Testnet" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "deploy:arc-testnet") `
    -Environment $envOverrides `
    -TimeoutSeconds 1200

Invoke-Step `
    -Name "Run Arc Testnet paid-agent closure" `
    -FilePath $npm `
    -Arguments @("--prefix", "interfaces\ArcContracts", "run", "testnet:closed-loop") `
    -Environment $envOverrides `
    -TimeoutSeconds 1200

Invoke-Step `
    -Name "Verify completion with Arc Testnet required" `
    -FilePath $powershell `
    -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts\verify-arc-hackathon-closure.ps1", "-RequireArcTestnet") `
    -Environment $envOverrides `
    -TimeoutSeconds 300

Write-Host "Arc Testnet closure completed. Artifact: $(Join-Path $artifactDir "arc-testnet-closure.json")"
