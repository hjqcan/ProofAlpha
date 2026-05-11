[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/acceptance/phase-7",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$CliConfig = "",
    [int]$DefaultTimeoutSeconds = 900,
    [switch]$IncludeOptionalPostgres,
    [switch]$FailOnRuntimeGateFailure,
    [switch]$SkipRestore
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
$runId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$runDir = Join-Path (Join-Path $artifactDir "runs") $runId
$commandsDir = Join-Path $runDir "commands"
$exportsDir = Join-Path $runDir "exports"
$jsonReportPath = Join-Path $artifactDir "acceptance-report.json"
$markdownReportPath = Join-Path $artifactDir "acceptance-report.md"

New-Item -ItemType Directory -Force -Path $commandsDir | Out-Null
New-Item -ItemType Directory -Force -Path $exportsDir | Out-Null

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

function ConvertTo-Slug {
    param([Parameter(Mandatory = $true)][string]$Value)

    $slug = ($Value.ToLowerInvariant() -replace "[^a-z0-9]+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "gate"
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

function Join-ProcessArguments {
    param([string[]]$Arguments = @())

    return ($Arguments | ForEach-Object { Quote-Argument $_ }) -join " "
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

function New-Gate {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Tier,
        [Parameter(Mandatory = $true)][bool]$Required,
        [Parameter(Mandatory = $true)][bool]$Blocking,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [int]$TimeoutSeconds = $DefaultTimeoutSeconds,
        [bool]$Skip = $false,
        [string]$SkipReason = ""
    )

    return [pscustomobject]@{
        name = $Name
        tier = $Tier
        required = $Required
        blocking = $Blocking
        filePath = $FilePath
        arguments = $Arguments
        workingDirectory = $WorkingDirectory
        timeoutSeconds = $TimeoutSeconds
        skip = $Skip
        skipReason = $SkipReason
    }
}

function Invoke-AcceptanceGate {
    param(
        [Parameter(Mandatory = $true)][object]$Gate,
        [Parameter(Mandatory = $true)][int]$Sequence
    )

    $slug = ConvertTo-Slug $Gate.name
    $stdoutPath = Join-Path $commandsDir ("{0:00}-{1}.stdout.txt" -f $Sequence, $slug)
    $stderrPath = Join-Path $commandsDir ("{0:00}-{1}.stderr.txt" -f $Sequence, $slug)
    $commandLine = Join-CommandLine -FilePath $Gate.filePath -Arguments $Gate.arguments
    $startedAt = Get-Date
    $status = "skipped"
    $exitCode = $null
    $exception = ""
    $stdout = ""
    $stderr = ""

    if ($Gate.skip) {
        $stderr = $Gate.skipReason
        Write-Utf8File -Path $stdoutPath -Content ""
        Write-Utf8File -Path $stderrPath -Content $stderr
    }
    else {
        try {
            $process = [System.Diagnostics.Process]::new()
            $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $process.StartInfo.FileName = $Gate.filePath
            $process.StartInfo.Arguments = Join-ProcessArguments -Arguments $Gate.arguments
            $process.StartInfo.WorkingDirectory = $Gate.workingDirectory
            $process.StartInfo.UseShellExecute = $false
            $process.StartInfo.RedirectStandardOutput = $true
            $process.StartInfo.RedirectStandardError = $true
            $process.StartInfo.CreateNoWindow = $true

            [void]$process.Start()
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $completed = $process.WaitForExit([Math]::Max(1, $Gate.timeoutSeconds) * 1000)

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
                if ($exitCode -eq 0) {
                    $status = "passed"
                }
                else {
                    $status = "failed"
                }
            }
        }
        catch {
            $status = "failed"
            $exception = $_.Exception.Message
            $stderr = $exception
        }
        finally {
            Write-Utf8File -Path $stdoutPath -Content $stdout
            Write-Utf8File -Path $stderrPath -Content $stderr
        }
    }

    $finishedAt = Get-Date
    $artifactPath = if (-not [string]::IsNullOrWhiteSpace($stderr)) { $stderrPath } else { $stdoutPath }

    return [pscustomobject]@{
        sequence = $Sequence
        name = $Gate.name
        tier = $Gate.tier
        required = $Gate.required
        blocking = $Gate.blocking
        status = $status
        command = $commandLine
        workingDirectory = $Gate.workingDirectory
        exitCode = $exitCode
        startedAtUtc = $startedAt.ToUniversalTime().ToString("O")
        finishedAtUtc = $finishedAt.ToUniversalTime().ToString("O")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        stdoutPath = [System.IO.Path]::GetFullPath($stdoutPath)
        stderrPath = [System.IO.Path]::GetFullPath($stderrPath)
        artifactPath = [System.IO.Path]::GetFullPath($artifactPath)
        exception = $exception
        skipReason = $Gate.skipReason
    }
}

function Add-CliGate {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[object]]$Gates,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Tier,
        [Parameter(Mandatory = $true)][bool]$Required,
        [Parameter(Mandatory = $true)][bool]$Blocking,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $cliArgs = @($script:cliDll)
    if (-not [string]::IsNullOrWhiteSpace($script:resolvedCliConfig)) {
        $cliArgs += @("--config", $script:resolvedCliConfig)
    }

    $cliArgs += $Arguments
    $Gates.Add((New-Gate -Name $Name -Tier $Tier -Required $Required -Blocking $Blocking -FilePath $script:dotnetPath -Arguments $cliArgs -WorkingDirectory $script:repoRoot)) | Out-Null
}

function New-MarkdownReport {
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][object[]]$Failures
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Phase 7 Acceptance Report") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Generated at UTC: $($Report.generatedAtUtc)") | Out-Null
    $lines.Add("- Overall status: $($Report.overallStatus)") | Out-Null
    $lines.Add("- Repository: $($Report.repositoryRoot)") | Out-Null
    $lines.Add("- Run artifacts: $($Report.runDirectory)") | Out-Null
    $lines.Add("- JSON report: $($Report.jsonReportPath)") | Out-Null
    $lines.Add("- Required blocking failures: $($Report.summary.requiredBlockingFailures)") | Out-Null
    $lines.Add("- Required runtime evidence failures: $($Report.summary.requiredRuntimeFailures)") | Out-Null
    $lines.Add("- Optional Postgres status: $($Report.summary.optionalPostgresStatus)") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Gate Results") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| # | Gate | Tier | Blocking | Status | Exit | Seconds | Artifact |") | Out-Null
    $lines.Add("|---:|---|---|---|---|---:|---:|---|") | Out-Null

    foreach ($result in $Report.results) {
        $exit = if ($null -eq $result.exitCode) { "" } else { [string]$result.exitCode }
        $artifact = ($result.artifactPath -replace "\|", "\|")
        $gateName = ($result.name -replace "\|", "\|")
        $lines.Add("| $($result.sequence) | $gateName | $($result.tier) | $($result.blocking) | $($result.status) | $exit | $($result.durationSeconds) | $artifact |") | Out-Null
    }

    if ($Failures.Count -gt 0) {
        $lines.Add("") | Out-Null
        $lines.Add("## Failures") | Out-Null
        $lines.Add("") | Out-Null
        foreach ($failure in $Failures) {
            $exit = if ($null -eq $failure.exitCode) { "n/a" } else { [string]$failure.exitCode }
            $lines.Add("- $($failure.name): status $($failure.status), exit code $exit, command $($failure.command), artifact $($failure.artifactPath).") | Out-Null
        }
    }

    $lines.Add("") | Out-Null
    $lines.Add("## Gate Policy") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("Required blocking gates are restore, build, test, Web App build, and config validation. Readiness, status, and export commands are executed as required runtime evidence because they depend on the local database, migrations, API process, and exchange/network conditions. Use -FailOnRuntimeGateFailure when the runtime environment is intentionally provisioned and should block release closure.") | Out-Null
    $lines.Add("Optional Postgres smoke tests run only when -IncludeOptionalPostgres is passed or AUTOTRADE_TEST_POSTGRES is set.") | Out-Null

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

$dotnetPath = Resolve-ToolPath -Candidates @("dotnet.exe", "dotnet")
$npmPath = Resolve-ToolPath -Candidates @("npm.cmd", "npm")
$webAppDir = Join-Path $repoRoot "webApp"
$solutionPath = Join-Path $repoRoot "Autotrade.sln"
$cliDll = Join-Path $repoRoot ("Autotrade.Cli\bin\{0}\net10.0\Autotrade.Cli.dll" -f $Configuration)
$resolvedCliConfig = ""

if (-not [string]::IsNullOrWhiteSpace($CliConfig)) {
    $resolvedCliConfig = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $CliConfig))
    if ([System.IO.Path]::IsPathRooted($CliConfig)) {
        $resolvedCliConfig = [System.IO.Path]::GetFullPath($CliConfig)
    }
}

$gates = [System.Collections.Generic.List[object]]::new()

if ($SkipRestore) {
    $gates.Add((New-Gate -Name "dotnet restore" -Tier "required-static" -Required $true -Blocking $false -FilePath $dotnetPath -Arguments @("restore") -WorkingDirectory $repoRoot -Skip $true -SkipReason "Skipped by -SkipRestore.")) | Out-Null
}
else {
    $gates.Add((New-Gate -Name "dotnet restore" -Tier "required-static" -Required $true -Blocking $true -FilePath $dotnetPath -Arguments @("restore") -WorkingDirectory $repoRoot)) | Out-Null
}

$gates.Add((New-Gate -Name "dotnet build" -Tier "required-static" -Required $true -Blocking $true -FilePath $dotnetPath -Arguments @("build", "--configuration", $Configuration, "--no-restore") -WorkingDirectory $repoRoot)) | Out-Null
$gates.Add((New-Gate -Name "dotnet test" -Tier "required-static" -Required $true -Blocking $true -FilePath $dotnetPath -Arguments @("test", $solutionPath, "--configuration", $Configuration, "--no-build") -WorkingDirectory $repoRoot)) | Out-Null
$gates.Add((New-Gate -Name "webapp build" -Tier "required-static" -Required $true -Blocking $true -FilePath $npmPath -Arguments @("run", "build") -WorkingDirectory $webAppDir)) | Out-Null

Add-CliGate -Gates $gates -Name "cli config validate" -Tier "required-static" -Required $true -Blocking $true -Arguments @("config", "validate", "--json")
Add-CliGate -Gates $gates -Name "cli readiness" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("readiness", "--json")
Add-CliGate -Gates $gates -Name "cli health readiness" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("health", "--mode", "readiness", "--json")
Add-CliGate -Gates $gates -Name "cli status" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("status", "--json")

Add-CliGate -Gates $gates -Name "export decisions" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "decisions", "--limit", "50", "--output", (Join-Path $exportsDir "decisions.csv"))
Add-CliGate -Gates $gates -Name "export orders" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "orders", "--limit", "50", "--output", (Join-Path $exportsDir "orders.csv"))
Add-CliGate -Gates $gates -Name "export trades" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "trades", "--limit", "50", "--output", (Join-Path $exportsDir "trades.csv"))
Add-CliGate -Gates $gates -Name "export pnl" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "pnl", "--strategy-id", "phase7-acceptance", "--output", (Join-Path $exportsDir "pnl.csv"))
Add-CliGate -Gates $gates -Name "export order events" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "order-events", "--limit", "50", "--output", (Join-Path $exportsDir "order-events.csv"))
Add-CliGate -Gates $gates -Name "export replay package" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "replay-package", "--limit", "50", "--output", (Join-Path $exportsDir "replay-package.json"))
Add-CliGate -Gates $gates -Name "export run report" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "run-report", "--session-id", "00000000-0000-0000-0000-000000000001", "--limit", "50", "--output", (Join-Path $exportsDir "run-report.csv"))
Add-CliGate -Gates $gates -Name "export promotion checklist" -Tier "required-runtime" -Required $true -Blocking $FailOnRuntimeGateFailure.IsPresent -Arguments @("export", "promotion-checklist", "--session-id", "00000000-0000-0000-0000-000000000001", "--limit", "50", "--output", (Join-Path $exportsDir "promotion-checklist.csv"))

$postgresRequested = $IncludeOptionalPostgres.IsPresent -or (-not [string]::IsNullOrWhiteSpace($env:AUTOTRADE_TEST_POSTGRES))
if ($postgresRequested) {
    $gates.Add((New-Gate -Name "optional postgres smoke" -Tier "optional-postgres" -Required $false -Blocking $false -FilePath $dotnetPath -Arguments @("test", $solutionPath, "--configuration", $Configuration, "--no-build", "--filter", "FullyQualifiedName~Postgres|Category=Postgres") -WorkingDirectory $repoRoot)) | Out-Null
}
else {
    $gates.Add((New-Gate -Name "optional postgres smoke" -Tier "optional-postgres" -Required $false -Blocking $false -FilePath $dotnetPath -Arguments @("test", $solutionPath, "--configuration", $Configuration, "--no-build", "--filter", "FullyQualifiedName~Postgres|Category=Postgres") -WorkingDirectory $repoRoot -Skip $true -SkipReason "Skipped because AUTOTRADE_TEST_POSTGRES is not set and -IncludeOptionalPostgres was not passed.")) | Out-Null
}

$results = [System.Collections.Generic.List[object]]::new()
$sequence = 1
foreach ($gate in $gates) {
    Write-Host ("[{0:00}/{1:00}] {2}" -f $sequence, $gates.Count, $gate.name)
    $results.Add((Invoke-AcceptanceGate -Gate $gate -Sequence $sequence)) | Out-Null
    $sequence++
}

$blockingFailures = @($results | Where-Object { $_.blocking -and ($_.status -eq "failed" -or $_.status -eq "timed_out") })
$runtimeFailures = @($results | Where-Object { $_.required -and -not $_.blocking -and $_.tier -eq "required-runtime" -and ($_.status -eq "failed" -or $_.status -eq "timed_out") })
$optionalPostgres = @($results | Where-Object { $_.tier -eq "optional-postgres" } | Select-Object -First 1)
$allFailures = @($results | Where-Object { $_.status -eq "failed" -or $_.status -eq "timed_out" })

if ($blockingFailures.Count -gt 0) {
    $overallStatus = "Failed"
}
elseif ($runtimeFailures.Count -gt 0) {
    $overallStatus = "PassedWithRuntimeEvidenceFailures"
}
else {
    $overallStatus = "Passed"
}

if ($FailOnRuntimeGateFailure -and $runtimeFailures.Count -gt 0) {
    $overallStatus = "Failed"
}

$optionalPostgresStatus = if ($optionalPostgres.Count -eq 0) { "not-configured" } else { $optionalPostgres[0].status }

$report = [ordered]@{
    schemaVersion = "autotrade.phase7.acceptance.v1"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    runId = $runId
    repositoryRoot = [System.IO.Path]::GetFullPath($repoRoot)
    configuration = $Configuration
    artifactRoot = [System.IO.Path]::GetFullPath($artifactDir)
    runDirectory = [System.IO.Path]::GetFullPath($runDir)
    jsonReportPath = [System.IO.Path]::GetFullPath($jsonReportPath)
    markdownReportPath = [System.IO.Path]::GetFullPath($markdownReportPath)
    failOnRuntimeGateFailure = $FailOnRuntimeGateFailure.IsPresent
    overallStatus = $overallStatus
    summary = [ordered]@{
        total = $results.Count
        passed = @($results | Where-Object { $_.status -eq "passed" }).Count
        failed = @($results | Where-Object { $_.status -eq "failed" }).Count
        timedOut = @($results | Where-Object { $_.status -eq "timed_out" }).Count
        skipped = @($results | Where-Object { $_.status -eq "skipped" }).Count
        requiredBlockingFailures = $blockingFailures.Count
        requiredRuntimeFailures = $runtimeFailures.Count
        optionalPostgresStatus = $optionalPostgresStatus
    }
    policy = [ordered]@{
        requiredBlocking = @("dotnet restore", "dotnet build", "dotnet test", "webapp build", "cli config validate")
        requiredRuntimeEvidence = @("cli readiness", "cli health readiness", "cli status", "export decisions", "export orders", "export trades", "export pnl", "export order events", "export replay package", "export run report", "export promotion checklist")
        optionalPostgres = "Runs only when -IncludeOptionalPostgres is passed or AUTOTRADE_TEST_POSTGRES is set."
    }
    results = @($results)
}

$json = $report | ConvertTo-Json -Depth 8
Write-Utf8File -Path $jsonReportPath -Content ($json + [Environment]::NewLine)
Write-Utf8File -Path $markdownReportPath -Content (New-MarkdownReport -Report ([pscustomobject]$report) -Failures $allFailures)

Write-Host "Acceptance JSON: $jsonReportPath"
Write-Host "Acceptance summary: $markdownReportPath"

if ($blockingFailures.Count -gt 0) {
    exit 1
}

if ($FailOnRuntimeGateFailure -and $runtimeFailures.Count -gt 0) {
    exit 1
}

exit 0
