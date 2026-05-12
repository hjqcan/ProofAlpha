[CmdletBinding()]
param(
    [string]$ArtifactRoot = "artifacts/opportunity-discovery/llm-gate",
    [switch]$SkipLiveLlm,
    [int]$LiveTimeoutSeconds = 90
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

function Invoke-RecordedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$LogPrefix,
        [int]$TimeoutSeconds = 900
    )

    Write-Host "Running: $Name"
    $stdoutPath = Join-Path $artifactDir "$LogPrefix.out.log"
    $stderrPath = Join-Path $artifactDir "$LogPrefix.err.log"
    $startedAt = [DateTimeOffset]::UtcNow
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
    return $result
}

function Get-RequiredEnv {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$Secret
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    $status = if ([string]::IsNullOrWhiteSpace($value)) { "Missing" } else { "Present" }
    $script:envChecks.Add([pscustomobject]@{
        name = $Name
        status = $status
        secret = [bool]$Secret
        value = if ($Secret -or [string]::IsNullOrWhiteSpace($value)) { "" } else { $value }
    }) | Out-Null

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is required."
    }

    return $value.Trim()
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

function Test-TextContainsAll {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string[]]$Needles
    )

    foreach ($needle in $Needles) {
        if ($Text.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) {
            return $false
        }
    }

    return $true
}

function Get-ObjectProperty {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Invoke-LiveLlmSmoke {
    $provider = Get-RequiredEnv -Name "PROOFALPHA_OPPORTUNITY_LLM_PROVIDER"
    $model = Get-RequiredEnv -Name "PROOFALPHA_OPPORTUNITY_LLM_MODEL"
    $apiKey = Get-RequiredEnv -Name "PROOFALPHA_OPPORTUNITY_LLM_API_KEY" -Secret
    $baseUrl = (Get-RequiredEnv -Name "PROOFALPHA_OPPORTUNITY_LLM_BASE_URL").TrimEnd("/")
    $uri = "$baseUrl/chat/completions"
    $startedAt = [DateTimeOffset]::UtcNow

    $body = @{
        model = $model
        messages = @(
            @{
                role = "system"
                content = "Return only one valid JSON object. No markdown."
            },
            @{
                role = "user"
                content = "Return exactly this JSON object: {""ok"":true,""purpose"":""opportunity_discovery_smoke"",""market"":""llm_config""}"
            }
        )
        temperature = 0.1
        response_format = @{
            type = "json_object"
        }
    } | ConvertTo-Json -Depth 10

    try {
        $response = Invoke-RestMethod `
            -Method Post `
            -Uri $uri `
            -Headers @{
                Authorization = "Bearer $apiKey"
                Accept = "application/json"
            } `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec $LiveTimeoutSeconds

        $content = [string]$response.choices[0].message.content
        $trimmed = $content.Trim()
        $jsonStart = $trimmed.IndexOf("{")
        $jsonEnd = $trimmed.LastIndexOf("}")
        if ($jsonStart -lt 0 -or $jsonEnd -le $jsonStart) {
            throw "LLM response did not contain a JSON object."
        }

        $json = $trimmed.Substring($jsonStart, $jsonEnd - $jsonStart + 1)
        $parsed = $json | ConvertFrom-Json
        $ok = [bool]$parsed.ok -and [string]$parsed.purpose -eq "opportunity_discovery_smoke"
        if (-not $ok) {
            throw "LLM JSON smoke response did not match the expected contract."
        }

        return [pscustomobject]@{
            status = "Passed"
            provider = $provider
            model = $model
            baseUrl = $baseUrl
            requestUri = $uri
            responseContent = $json
            startedAtUtc = $startedAt.ToString("O")
            finishedAtUtc = ([DateTimeOffset]::UtcNow).ToString("O")
        }
    }
    catch {
        return [pscustomobject]@{
            status = "Failed"
            provider = $provider
            model = $model
            baseUrl = $baseUrl
            requestUri = $uri
            error = ([string]$_.Exception.Message)
            startedAtUtc = $startedAt.ToString("O")
            finishedAtUtc = ([DateTimeOffset]::UtcNow).ToString("O")
        }
    }
}

$artifactDir = Resolve-RepoPath $ArtifactRoot
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$steps = [System.Collections.Generic.List[object]]::new()
$checks = [System.Collections.Generic.List[object]]::new()
$envChecks = [System.Collections.Generic.List[object]]::new()
$dotnet = Resolve-ToolPath @("dotnet.exe", "dotnet")

Import-DotEnv -Path (Resolve-RepoPath ".env")

$envExampleText = Get-Content -Raw -LiteralPath (Resolve-RepoPath ".env.example")
$cliSettingsText = Get-Content -Raw -LiteralPath (Resolve-RepoPath "interfaces/Autotrade.Cli/appsettings.json")
$apiSettingsText = Get-Content -Raw -LiteralPath (Resolve-RepoPath "interfaces/Autotrade.Api/appsettings.json")
$cliSettings = $cliSettingsText | ConvertFrom-Json
$apiSettings = $apiSettingsText | ConvertFrom-Json
$llmClientText = Get-Content -Raw -LiteralPath (Resolve-RepoPath "Shared/Autotrade.Llm/OpenAiCompatibleLlmJsonClient.cs")
$opportunityDomainText = Get-Content -Raw -LiteralPath (Resolve-RepoPath "context/OpportunityDiscovery/Autotrade.OpportunityDiscovery.Domain/Entities/MarketOpportunity.cs")
$opportunityTestsText = Get-Content -Raw -LiteralPath (Resolve-RepoPath "context/OpportunityDiscovery/Autotrade.OpportunityDiscovery.Tests/OpportunityDiscoveryServiceTests.cs")

$goodMemoryStyleDocumented = Test-TextContainsAll `
    -Text $envExampleText `
    -Needles @(
        "PROOFALPHA_OPPORTUNITY_LLM_PROVIDER",
        "PROOFALPHA_OPPORTUNITY_LLM_MODEL",
        "PROOFALPHA_OPPORTUNITY_LLM_API_KEY",
        "PROOFALPHA_OPPORTUNITY_LLM_BASE_URL"
    )
$cliOpportunityLlm = Get-ObjectProperty -Object (Get-ObjectProperty -Object $cliSettings -Name "OpportunityDiscovery") -Name "Llm"
$apiOpportunityLlm = Get-ObjectProperty -Object (Get-ObjectProperty -Object $apiSettings -Name "OpportunityDiscovery") -Name "Llm"
$cliSelfImproveLlm = Get-ObjectProperty -Object (Get-ObjectProperty -Object $cliSettings -Name "SelfImprove") -Name "Llm"
$apiSelfImproveLlm = Get-ObjectProperty -Object (Get-ObjectProperty -Object $apiSettings -Name "SelfImprove") -Name "Llm"
$cliOpportunityPrefix = [string](Get-ObjectProperty -Object $cliOpportunityLlm -Name "EnvPrefix") -eq "PROOFALPHA_OPPORTUNITY_LLM"
$apiOpportunityPrefix = [string](Get-ObjectProperty -Object $apiOpportunityLlm -Name "EnvPrefix") -eq "PROOFALPHA_OPPORTUNITY_LLM"
$selfImprovePrefixLeak =
    [string](Get-ObjectProperty -Object $cliSelfImproveLlm -Name "EnvPrefix") -eq "PROOFALPHA_OPPORTUNITY_LLM" -or
    [string](Get-ObjectProperty -Object $apiSelfImproveLlm -Name "EnvPrefix") -eq "PROOFALPHA_OPPORTUNITY_LLM"
$clientReadsPrefix = Test-TextContainsAll `
    -Text $llmClientText `
    -Needles @(
        'ResolveSetting(options.Provider, options.EnvPrefix, "PROVIDER")',
        'ResolveSetting(options.Model, options.EnvPrefix, "MODEL")',
        'ResolveSetting(options.BaseUrl, options.EnvPrefix, "BASE_URL")',
        'ResolveSetting(null, options.EnvPrefix, "API_KEY")',
        'EnsureOpenAiCompatibleProvider(provider)'
    )
$approvalOnlyFromCandidate = $opportunityDomainText.IndexOf("Status is not OpportunityStatus.Candidate", [System.StringComparison]::Ordinal) -ge 0
$invalidOutputRegressionTest = $opportunityTestsText.IndexOf("ScanAsync_InvalidLlmDocumentCreatesNeedsReviewAndCannotPublish", [System.StringComparison]::Ordinal) -ge 0

$buildStep = Invoke-RecordedStep `
    -Name "dotnet build" `
    -FilePath $dotnet `
    -Arguments @("build", "Autotrade.sln", "-v", "minimal") `
    -LogPrefix "dotnet-build" `
    -TimeoutSeconds 900
$llmTestStep = Invoke-RecordedStep `
    -Name "Autotrade.Llm tests" `
    -FilePath $dotnet `
    -Arguments @("test", "Shared\Autotrade.Llm.Tests\Autotrade.Llm.Tests.csproj", "-v", "minimal") `
    -LogPrefix "llm-tests" `
    -TimeoutSeconds 600
$opportunityTestStep = Invoke-RecordedStep `
    -Name "OpportunityDiscovery tests" `
    -FilePath $dotnet `
    -Arguments @("test", "context\OpportunityDiscovery\Autotrade.OpportunityDiscovery.Tests\Autotrade.OpportunityDiscovery.Tests.csproj", "-v", "minimal") `
    -LogPrefix "opportunity-discovery-tests" `
    -TimeoutSeconds 600
$strategyTestStep = Invoke-RecordedStep `
    -Name "LlmOpportunityStrategy tests" `
    -FilePath $dotnet `
    -Arguments @("test", "context\Strategy\Autotrade.Strategy.Tests\Autotrade.Strategy.Tests.csproj", "-v", "minimal", "--filter", "FullyQualifiedName~LlmOpportunityStrategyTests") `
    -LogPrefix "llm-opportunity-strategy-tests" `
    -TimeoutSeconds 600

if ($SkipLiveLlm) {
    $liveSmoke = [pscustomobject]@{
        status = "Skipped"
        reason = "SkipLiveLlm was set."
        model = ""
        baseUrl = ""
    }
}
else {
    $liveSmoke = Invoke-LiveLlmSmoke
}

$liveSmokePath = Join-Path $artifactDir "live-llm-smoke.json"
Write-Utf8File -Path $liveSmokePath -Content (($liveSmoke | ConvertTo-Json -Depth 10) + [Environment]::NewLine)

Add-Check `
    -Id "goodmemory-style-config" `
    -Status ($(if ($goodMemoryStyleDocumented -and $cliOpportunityPrefix -and $apiOpportunityPrefix -and -not $selfImprovePrefixLeak) { "Passed" } else { "Failed" })) `
    -Evidence ".env.example, interfaces/Autotrade.Cli/appsettings.json, interfaces/Autotrade.Api/appsettings.json" `
    -Details "documented=$goodMemoryStyleDocumented; cliOpportunityPrefix=$cliOpportunityPrefix; apiOpportunityPrefix=$apiOpportunityPrefix; selfImprovePrefixLeak=$selfImprovePrefixLeak"
Add-Check `
    -Id "llm-client-env-prefix" `
    -Status ($(if ($clientReadsPrefix -and $llmTestStep.status -eq "Passed") { "Passed" } else { "Failed" })) `
    -Evidence "$($llmTestStep.stdoutPath)" `
    -Details "clientReadsPrefix=$clientReadsPrefix"
Add-Check `
    -Id "invalid-llm-output-not-publishable" `
    -Status ($(if ($approvalOnlyFromCandidate -and $invalidOutputRegressionTest -and $opportunityTestStep.status -eq "Passed") { "Passed" } else { "Failed" })) `
    -Evidence "$($opportunityTestStep.stdoutPath)" `
    -Details "approvalOnlyFromCandidate=$approvalOnlyFromCandidate; regressionTest=$invalidOutputRegressionTest"
Add-Check `
    -Id "strategy-consumes-published-feed" `
    -Status ($(if ($strategyTestStep.status -eq "Passed") { "Passed" } else { "Failed" })) `
    -Evidence "$($strategyTestStep.stdoutPath)"
Add-Check `
    -Id "solution-build" `
    -Status $buildStep.status `
    -Evidence "$($buildStep.stdoutPath)"
Add-Check `
    -Id "live-openai-compatible-llm" `
    -Status $liveSmoke.status `
    -Evidence (Get-RepoRelativePath $liveSmokePath) `
    -Details "model=$($liveSmoke.model); baseUrl=$($liveSmoke.baseUrl)"

$blockingFailures = @($checks | Where-Object { $_.status -ne "Passed" }).Count
$gateStatus = if ($blockingFailures -eq 0) { "Passed" } else { "Failed" }
$record = [pscustomobject]@{
    gateStatus = $gateStatus
    generatedAtUtc = ([DateTimeOffset]::UtcNow).ToString("O")
    artifactRoot = Get-RepoRelativePath $artifactDir
    objective = "LLM-backed OpportunityDiscovery uses GoodMemory-style purpose-prefixed OpenAI-compatible config and is locally testable end to end."
    checks = $checks
    steps = $steps
    envChecks = $envChecks
    liveSmokePath = Get-RepoRelativePath $liveSmokePath
}

$jsonPath = Join-Path $artifactDir "opportunity-llm-discovery-gate.json"
$markdownPath = Join-Path $artifactDir "opportunity-llm-discovery-gate.md"
Write-Utf8File -Path $jsonPath -Content (($record | ConvertTo-Json -Depth 12) + [Environment]::NewLine)

$markdown = @(
    "# Opportunity LLM Discovery Gate",
    "",
    "- Status: $gateStatus",
    "- Generated: $($record.generatedAtUtc)",
    "- Live smoke: $($liveSmoke.status)",
    "- Artifact root: $($record.artifactRoot)",
    "",
    (ConvertTo-MarkdownTable -Rows $checks),
    ""
) -join [Environment]::NewLine
Write-Utf8File -Path $markdownPath -Content $markdown

$secretValue = [Environment]::GetEnvironmentVariable("PROOFALPHA_OPPORTUNITY_LLM_API_KEY")
if (-not [string]::IsNullOrWhiteSpace($secretValue)) {
    foreach ($artifact in @($jsonPath, $markdownPath, $liveSmokePath)) {
        $artifactText = Get-Content -Raw -LiteralPath $artifact
        if ($artifactText.IndexOf($secretValue, [System.StringComparison]::Ordinal) -ge 0) {
            throw "Secret material was written to $(Get-RepoRelativePath $artifact)."
        }
    }
}

if ($gateStatus -ne "Passed") {
    throw "Opportunity LLM discovery gate failed. See $(Get-RepoRelativePath $markdownPath)."
}

Write-Host "Opportunity LLM discovery gate passed. Artifact: $(Get-RepoRelativePath $jsonPath)"
