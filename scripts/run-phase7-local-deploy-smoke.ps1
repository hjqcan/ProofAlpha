[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/deploy/phase-7",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$DefaultApiPort = 5080,
    [int]$DefaultWebPort = 5173,
    [int]$TimeoutSeconds = 90,
    [switch]$ReserveCommonPorts
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
$logsDir = Join-Path $runDir "logs"
$packageDir = Join-Path $artifactDir "package"
$apiPackageDir = Join-Path $packageDir "api"
$safeApiConfigPath = Join-Path $apiPackageDir "appsettings.Phase7Smoke.json"
$jsonReportPath = Join-Path $artifactDir "local-smoke.json"
$markdownReportPath = Join-Path $artifactDir "local-smoke.md"

New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
New-Item -ItemType Directory -Force -Path $apiPackageDir | Out-Null

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

function Test-TcpPortAvailable {
    param([Parameter(Mandatory = $true)][int]$Port)

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), $Port)
    try {
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        try {
            $listener.Stop()
        }
        catch {
        }
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Resolve-DeployPort {
    param([Parameter(Mandatory = $true)][int]$PreferredPort)

    if (Test-TcpPortAvailable -Port $PreferredPort) {
        return [pscustomobject]@{
            port = $PreferredPort
            usedFallback = $false
            reason = "preferred port available"
        }
    }

    return [pscustomobject]@{
        port = Get-FreeTcpPort
        usedFallback = $true
        reason = "preferred port $PreferredPort was already occupied"
    }
}

function New-PortReservation {
    param([Parameter(Mandatory = $true)][int]$Port)

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), $Port)
    try {
        $listener.Start()
        return $listener
    }
    catch {
        return $null
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$Timeout = $TimeoutSeconds
    )

    $stdoutPath = Join-Path $logsDir "$Name.stdout.txt"
    $stderrPath = Join-Path $logsDir "$Name.stderr.txt"
    $startedAt = Get-Date
    $status = "failed"
    $exitCode = $null
    $stdout = ""
    $stderr = ""
    $exception = ""

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
        foreach ($key in $Environment.Keys) {
            $process.StartInfo.EnvironmentVariables[$key] = [string]$Environment[$key]
        }

        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit([Math]::Max(1, $Timeout) * 1000)
        if (-not $completed) {
            $status = "timed_out"
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
        $exitCode = $process.ExitCode
        if ($status -ne "timed_out") {
            $status = if ($exitCode -eq 0) { "passed" } else { "failed" }
        }
    }
    catch {
        $exception = $_.Exception.Message
        $stderr = $exception
    }

    Write-Utf8File -Path $stdoutPath -Content $stdout
    Write-Utf8File -Path $stderrPath -Content $stderr
    $finishedAt = Get-Date

    return [pscustomobject]@{
        name = $Name
        status = $status
        command = Join-CommandLine -FilePath $FilePath -Arguments $Arguments
        workingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
        exitCode = $exitCode
        startedAtUtc = $startedAt.ToUniversalTime().ToString("O")
        finishedAtUtc = $finishedAt.ToUniversalTime().ToString("O")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        stdoutPath = [System.IO.Path]::GetFullPath($stdoutPath)
        stderrPath = [System.IO.Path]::GetFullPath($stderrPath)
        exception = $exception
    }
}

function Start-ManagedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )

    $stdoutPath = Join-Path $logsDir "$Name.stdout.txt"
    $stderrPath = Join-Path $logsDir "$Name.stderr.txt"
    $stdoutWriter = [System.IO.StreamWriter]::new($stdoutPath, $false, $utf8NoBom)
    $stderrWriter = [System.IO.StreamWriter]::new($stderrPath, $false, $utf8NoBom)

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $FilePath
    $process.StartInfo.Arguments = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join " "
    $process.StartInfo.WorkingDirectory = $WorkingDirectory
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true
    foreach ($key in $Environment.Keys) {
        $process.StartInfo.EnvironmentVariables[$key] = [string]$Environment[$key]
    }

    [void]$process.Start()
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -Action {
        if ($EventArgs.Data -ne $null) {
            $Event.MessageData.WriteLine($EventArgs.Data)
            $Event.MessageData.Flush()
        }
    } -MessageData $stdoutWriter | Out-Null
    Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -Action {
        if ($EventArgs.Data -ne $null) {
            $Event.MessageData.WriteLine($EventArgs.Data)
            $Event.MessageData.Flush()
        }
    } -MessageData $stderrWriter | Out-Null

    return [pscustomobject]@{
        name = $Name
        process = $process
        stdoutWriter = $stdoutWriter
        stderrWriter = $stderrWriter
        stdoutPath = [System.IO.Path]::GetFullPath($stdoutPath)
        stderrPath = [System.IO.Path]::GetFullPath($stderrPath)
        command = Join-CommandLine -FilePath $FilePath -Arguments $Arguments
    }
}

function Stop-ManagedProcess {
    param([Parameter(Mandatory = $true)][object]$Managed)

    try {
        if (-not $Managed.process.HasExited) {
            try {
                $Managed.process.Kill($true)
            }
            catch {
                $Managed.process.Kill()
            }
            $Managed.process.WaitForExit(5000) | Out-Null
        }
    }
    finally {
        $Managed.stdoutWriter.Dispose()
        $Managed.stderrWriter.Dispose()
        $Managed.process.Dispose()
    }
}

function Wait-HttpOk {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$Timeout = $TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($Timeout)
    $lastError = ""
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            return [pscustomobject]@{
                ok = $true
                statusCode = [int]$response.StatusCode
                contentLength = $response.Content.Length
                error = ""
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 500
        }
    }

    return [pscustomobject]@{
        ok = $false
        statusCode = 0
        contentLength = 0
        error = $lastError
    }
}

function Test-PackageSecretFree {
    param([Parameter(Mandatory = $true)][string[]]$Roots)

    $blockedNames = @(".env", ".env.local", "appsettings.local.json", "secrets.json")
    $textExtensions = @(".json", ".config", ".txt", ".md", ".html", ".js", ".css")
    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
            if ($blockedNames -contains $file.Name.ToLowerInvariant()) {
                $findings.Add([pscustomobject]@{ path = $file.FullName; reason = "blocked package filename" }) | Out-Null
                continue
            }

            if ($textExtensions -notcontains $file.Extension.ToLowerInvariant()) {
                continue
            }

            $text = [System.IO.File]::ReadAllText($file.FullName)
            if ($text -match '(?i)(privateKey|mnemonic|authorization\s*:|password\s*=|apiKey\s*[:=]|secret\s*[:=])') {
                $findings.Add([pscustomobject]@{ path = $file.FullName; reason = "potential secret token in text asset" }) | Out-Null
            }
        }
    }

    return @($findings)
}

$dotnetPath = Resolve-ToolPath -Candidates @("dotnet.exe", "dotnet")
$npmPath = Resolve-ToolPath -Candidates @("npm.cmd", "npm")
$nodePath = Resolve-ToolPath -Candidates @("node.exe", "node")
$apiProject = Join-Path $repoRoot "interfaces\Autotrade.Api\Autotrade.Api.csproj"
$webAppDir = Join-Path $repoRoot "webApp"
$viteScriptPath = Join-Path $webAppDir "node_modules\vite\bin\vite.js"
if (-not (Test-Path -LiteralPath $viteScriptPath -PathType Leaf)) {
    throw "Missing Vite script. Run npm install in webApp before local deploy smoke."
}

$reservations = [System.Collections.Generic.List[object]]::new()
if ($ReserveCommonPorts) {
    foreach ($port in @($DefaultApiPort, $DefaultWebPort)) {
        $reservation = New-PortReservation -Port $port
        if ($null -ne $reservation) {
            $reservations.Add($reservation) | Out-Null
        }
    }
}

$apiProcess = $null
$webProcess = $null
$fatalError = ""
$apiPort = [pscustomobject]@{ port = $DefaultApiPort; usedFallback = $false; reason = "not resolved" }
$webPort = [pscustomobject]@{ port = $DefaultWebPort; usedFallback = $false; reason = "not resolved" }
$apiBaseUrl = ""
$webBaseUrl = ""
$steps = [System.Collections.Generic.List[object]]::new()
$checks = [System.Collections.Generic.List[object]]::new()

try {
    $apiPort = Resolve-DeployPort -PreferredPort $DefaultApiPort
    $webPort = Resolve-DeployPort -PreferredPort $DefaultWebPort
    $apiBaseUrl = "http://127.0.0.1:$($apiPort.port)"
    $webBaseUrl = "http://127.0.0.1:$($webPort.port)"

    $publishStep = Invoke-Step `
        -Name "api-publish" `
        -FilePath $dotnetPath `
        -Arguments @("publish", $apiProject, "--configuration", $Configuration, "--no-restore", "--output", $apiPackageDir) `
        -WorkingDirectory $repoRoot `
        -Timeout 300
    $steps.Add($publishStep) | Out-Null

    foreach ($file in Get-ChildItem -LiteralPath $apiPackageDir -Filter "appsettings*.json" -File -ErrorAction SilentlyContinue) {
        Remove-Item -LiteralPath $file.FullName -Force
    }

    Write-Utf8File -Path $safeApiConfigPath -Content (@{
        AutotradeApi = @{
            EnableModules = $false
            ControlRoom = @{
                CommandMode = "ReadOnly"
                EnableControlCommands = $false
                EnablePublicMarketData = $false
                RequireLocalAccess = $true
            }
        }
        Execution = @{ Mode = "Paper" }
        BackgroundJobs = @{ Enabled = $false }
        AccountSync = @{ Enabled = $false }
    } | ConvertTo-Json -Depth 8)

    $apiDll = Join-Path $apiPackageDir "Autotrade.Api.dll"
    $apiProcess = Start-ManagedProcess `
        -Name "api-server" `
        -FilePath $dotnetPath `
        -Arguments @($apiDll) `
        -WorkingDirectory $apiPackageDir `
        -Environment @{
            "ASPNETCORE_URLS" = $apiBaseUrl
            "ASPNETCORE_ENVIRONMENT" = "Phase7Smoke"
            "AutotradeApi__EnableModules" = "false"
            "AutotradeApi__ControlRoom__CommandMode" = "ReadOnly"
            "AutotradeApi__ControlRoom__EnableControlCommands" = "false"
            "AutotradeApi__ControlRoom__EnablePublicMarketData" = "false"
            "Execution__Mode" = "Paper"
            "BackgroundJobs__Enabled" = "false"
            "AccountSync__Enabled" = "false"
        }

    $apiLive = Wait-HttpOk -Url "$apiBaseUrl/health/live" -Timeout $TimeoutSeconds
    $apiReady = Wait-HttpOk -Url "$apiBaseUrl/health/ready" -Timeout $TimeoutSeconds
    $apiReadiness = Wait-HttpOk -Url "$apiBaseUrl/api/readiness" -Timeout $TimeoutSeconds
    $checks.Add([pscustomobject]@{ name = "api health live"; status = if ($apiLive.ok) { "passed" } else { "failed" }; url = "$apiBaseUrl/health/live"; detail = $apiLive }) | Out-Null
    $checks.Add([pscustomobject]@{ name = "api health ready"; status = if ($apiReady.ok) { "passed" } else { "failed" }; url = "$apiBaseUrl/health/ready"; detail = $apiReady }) | Out-Null
    $checks.Add([pscustomobject]@{ name = "api readiness endpoint"; status = if ($apiReadiness.ok) { "passed" } else { "failed" }; url = "$apiBaseUrl/api/readiness"; detail = $apiReadiness }) | Out-Null

    $buildStep = Invoke-Step `
        -Name "web-build" `
        -FilePath $npmPath `
        -Arguments @("run", "build") `
        -WorkingDirectory $webAppDir `
        -Environment @{ "VITE_API_BASE" = $apiBaseUrl } `
        -Timeout 300
    $steps.Add($buildStep) | Out-Null

    $webProcess = Start-ManagedProcess `
        -Name "web-preview" `
        -FilePath $nodePath `
        -Arguments @($viteScriptPath, "preview", "--host", "127.0.0.1", "--port", [string]$webPort.port, "--strictPort") `
        -WorkingDirectory $webAppDir `
        -Environment @{ "VITE_API_BASE" = $apiBaseUrl }

    $webRoot = Wait-HttpOk -Url "$webBaseUrl/" -Timeout $TimeoutSeconds
    $checks.Add([pscustomobject]@{ name = "web preview root"; status = if ($webRoot.ok) { "passed" } else { "failed" }; url = "$webBaseUrl/"; detail = $webRoot }) | Out-Null

    $packageFindings = @(Test-PackageSecretFree -Roots @($apiPackageDir, (Join-Path $webAppDir "dist")))
    $checks.Add([pscustomobject]@{
        name = "package secret scan"
        status = if ($packageFindings.Count -eq 0) { "passed" } else { "failed" }
        url = ""
        detail = [ordered]@{
            findingCount = $packageFindings.Count
            findings = $packageFindings
        }
    }) | Out-Null
}
catch {
    $fatalError = $_.Exception.Message
    $checks.Add([pscustomobject]@{
        name = "local deploy smoke fatal error"
        status = "failed"
        url = ""
        detail = [ordered]@{
            message = $fatalError
            position = $_.InvocationInfo.PositionMessage
        }
    }) | Out-Null
}
finally {
    if ($null -ne $webProcess) {
        Stop-ManagedProcess -Managed $webProcess
    }
    if ($null -ne $apiProcess) {
        Stop-ManagedProcess -Managed $apiProcess
    }
    foreach ($reservation in $reservations) {
        $reservation.Stop()
    }
}

$failedSteps = @($steps | Where-Object { $_.status -ne "passed" })
$failedChecks = @($checks | Where-Object { $_.status -ne "passed" })
$overallStatus = if ($failedSteps.Count -eq 0 -and $failedChecks.Count -eq 0) { "Passed" } else { "Failed" }

$report = [ordered]@{
    schemaVersion = "autotrade.phase7.local-deploy-smoke.v1"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    runId = $runId
    overallStatus = $overallStatus
    repositoryRoot = [System.IO.Path]::GetFullPath($repoRoot)
    artifactRoot = [System.IO.Path]::GetFullPath($artifactDir)
    runDirectory = [System.IO.Path]::GetFullPath($runDir)
    apiPackageDirectory = [System.IO.Path]::GetFullPath($apiPackageDir)
    webDistDirectory = [System.IO.Path]::GetFullPath((Join-Path $webAppDir "dist"))
    api = [ordered]@{
        preferredPort = $DefaultApiPort
        port = $apiPort.port
        usedFallbackPort = $apiPort.usedFallback
        portReason = $apiPort.reason
        baseUrl = $apiBaseUrl
        command = if ($null -ne $apiProcess) { $apiProcess.command } else { "" }
        stdoutPath = if ($null -ne $apiProcess) { $apiProcess.stdoutPath } else { "" }
        stderrPath = if ($null -ne $apiProcess) { $apiProcess.stderrPath } else { "" }
    }
    web = [ordered]@{
        preferredPort = $DefaultWebPort
        port = $webPort.port
        usedFallbackPort = $webPort.usedFallback
        portReason = $webPort.reason
        baseUrl = $webBaseUrl
        command = if ($null -ne $webProcess) { $webProcess.command } else { "" }
        stdoutPath = if ($null -ne $webProcess) { $webProcess.stdoutPath } else { "" }
        stderrPath = if ($null -ne $webProcess) { $webProcess.stderrPath } else { "" }
    }
    safety = [ordered]@{
        executionMode = "Paper"
        commandMode = "ReadOnly"
        controlCommandsEnabled = $false
        modulesEnabled = $false
        publicMarketDataEnabled = $false
        safeApiConfigPath = [System.IO.Path]::GetFullPath($safeApiConfigPath)
    }
    fatalError = $fatalError
    steps = @($steps)
    checks = @($checks)
}

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add("# Phase 7 Local Deploy Smoke") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("- Generated at UTC: $($report.generatedAtUtc)") | Out-Null
$markdown.Add("- Overall status: $($report.overallStatus)") | Out-Null
$markdown.Add("- API URL: $($report.api.baseUrl) ($($report.api.portReason))") | Out-Null
$markdown.Add("- Web URL: $($report.web.baseUrl) ($($report.web.portReason))") | Out-Null
$markdown.Add("- API package: $($report.apiPackageDirectory)") | Out-Null
$markdown.Add("- Web dist: $($report.webDistDirectory)") | Out-Null
$markdown.Add("") | Out-Null
$markdown.Add("## Checks") | Out-Null
$markdown.Add("") | Out-Null
foreach ($check in $checks) {
    $markdown.Add("- $($check.status): $($check.name) $($check.url)") | Out-Null
}
$markdown.Add("") | Out-Null
$markdown.Add("## Steps") | Out-Null
$markdown.Add("") | Out-Null
foreach ($step in $steps) {
    $markdown.Add("- $($step.status): $($step.name), exit $($step.exitCode), stdout $($step.stdoutPath), stderr $($step.stderrPath)") | Out-Null
}

Write-Utf8File -Path $jsonReportPath -Content (($report | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
Write-Utf8File -Path $markdownReportPath -Content (($markdown -join [Environment]::NewLine) + [Environment]::NewLine)

Write-Host "Local deploy smoke JSON: $jsonReportPath"
Write-Host "Local deploy smoke summary: $markdownReportPath"

if ($overallStatus -ne "Passed") {
    exit 1
}

exit 0
