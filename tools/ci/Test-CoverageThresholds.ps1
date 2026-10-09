#Requires -Version 7.0
<#
.SYNOPSIS
    Fails when coverage is below the thresholds in governance/policies/coverage-thresholds.json.
.DESCRIPTION
    Reads one or more Cobertura files, prints a Markdown summary, appends it to the GitHub job summary when one is available,
    and exits with code 1 when any check fails. Run it after the tests, for example:
    pwsh -File tools/ci/Test-CoverageThresholds.ps1 -CoverageFile 'TestResults/*.cobertura.xml'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $CoverageFile,
    [string] $PolicyPath = (Join-Path $PSScriptRoot '../../governance/policies/coverage-thresholds.json'),
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'CoverageThresholds.psm1') -Force

$files = @($CoverageFile | ForEach-Object { Get-ChildItem -Path $_ -File -ErrorAction SilentlyContinue } | Select-Object -ExpandProperty FullName -Unique)
if ($files.Count -eq 0) {
    Write-Error "No coverage file matched: $($CoverageFile -join ', '). The tests must run with coverage collection on."
    exit 1
}

$policy = Get-Content -Path $PolicyPath -Raw | ConvertFrom-Json
$report = Read-CoverageReport -Path $files -ExcludePath @($policy.excludePaths)
$results = Get-CoverageResult -Report $report -Policy $policy -RepositoryRoot $RepositoryRoot
$summary = Format-CoverageSummary -Result $results

$summary | Write-Output
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value (@('### Coverage thresholds', '') + $summary)
}

$failures = @($results | Where-Object { $_.Status -eq 'Fail' })
if ($failures.Count -gt 0) {
    Write-Output "Coverage gate failed: $($failures.Count) check(s) below the policy."
    exit 1
}

Write-Output 'Coverage gate passed.'
