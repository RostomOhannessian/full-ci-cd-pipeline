#Requires -Version 7.4
<#
.SYNOPSIS
    Runs the repository's script tests: PSScriptAnalyzer over the scripts, then Pester, both from the versions that tools/versions.yaml pins.
.DESCRIPTION
    This is what "dev test scripts" runs, and what CI runs, so a result here is the CI result. The pinned tools are installed on first use,
    after their checksums match, into the ignored .dev folder. Run it from anywhere:
    pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1
.PARAMETER Path
    The folders to analyze and test, relative to the repository root.
.PARAMETER SkipInstall
    Does not install the pinned tools, and fails when they are missing. Use it offline after one install.
#>
[CmdletBinding()]
param(
    [string[]] $Path = @('scripts', 'tools/ci'),
    [switch] $SkipInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -Path (Join-Path $PSScriptRoot '..')).ProviderPath
Import-Module (Join-Path $PSScriptRoot 'modules' 'Dev.Tools.psm1') -Force

$tools = @(Read-DevToolManifest)
$toolsRoot = Get-DevToolsRoot -RepositoryRoot $root

if (-not $SkipInstall) {
    Install-DevToolSet -Root $toolsRoot | ForEach-Object { Write-Output "$($_.Id) $($_.Version): $($_.Action)" }
}

$missing = @(Get-DevToolStatus -Tool $tools -Root $toolsRoot | Where-Object { -not $_.Installed })
if ($missing.Count -gt 0) {
    throw "The pinned tools are not installed: $(($missing | ForEach-Object { $_.Name }) -join ', '). Run './dev bootstrap'."
}

Enable-DevToolPath -Root $toolsRoot
$pester = $tools | Where-Object Id -EQ 'pester'
$analyzer = $tools | Where-Object Id -EQ 'psscriptanalyzer'
Import-Module Pester -RequiredVersion $pester.Version -Force
Import-Module PSScriptAnalyzer -RequiredVersion $analyzer.Version -Force
Write-Output "Pester $($pester.Version), PSScriptAnalyzer $($analyzer.Version)"

Push-Location -Path $root
try {
    $findings = @(foreach ($folder in $Path) { Invoke-ScriptAnalyzer -Path $folder -Recurse -Severity Warning, Error })
    if ($findings.Count -gt 0) {
        $findings | Format-Table RuleName, Severity, ScriptName, Line, Message -AutoSize -Wrap | Out-String -Width 200 | Write-Output
        Write-Output "PSScriptAnalyzer found $($findings.Count) problem(s)."
    }
    else {
        Write-Output 'PSScriptAnalyzer found no problems.'
    }

    $configuration = New-PesterConfiguration
    $configuration.Run.Path = $Path
    $configuration.Run.PassThru = $true
    $configuration.Output.Verbosity = 'Normal'
    $result = Invoke-Pester -Configuration $configuration

    if ($env:GITHUB_STEP_SUMMARY) {
        $summary = @('### Script tests', '', "| Passed | Failed | Skipped | Analyzer findings |", '| --- | --- | --- | --- |', "| $($result.PassedCount) | $($result.FailedCount) | $($result.SkippedCount) | $($findings.Count) |", '')
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary
    }

    if ($findings.Count -gt 0 -or $result.FailedCount -gt 0 -or $result.Result -ne 'Passed') { exit 1 }
}
finally {
    Pop-Location
}
