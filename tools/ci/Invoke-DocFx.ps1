#Requires -Version 7.0
<#
.SYNOPSIS
    Runs a DocFX command and fails on any warning or error.
.DESCRIPTION
    DocFX reads docfx.json at the repository root. Both steps run with --warningsAsErrors, and the log is checked here as well, so a
    warning that DocFX writes but does not count still fails the step. DocFX.psm1 lists the warnings that pass, and the list is empty.
    Run it from the repository root after "dotnet tool restore" and a restore of the solution:
    pwsh -File tools/ci/Invoke-DocFx.ps1 -Command metadata
    pwsh -File tools/ci/Invoke-DocFx.ps1 -Command build
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('metadata', 'build')] [string] $Command,
    [string] $Configuration = 'docfx.json',
    [string] $LogPath = (Join-Path ([System.IO.Path]::GetTempPath()) "docfx-$Command.log.json")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'DocFx.psm1') -Force

if (Test-Path -Path $LogPath) { Remove-Item -Path $LogPath -Force }

$arguments = @('docfx', $Command, $Configuration, '--log', $LogPath, '--warningsAsErrors')

& dotnet @arguments
$exitCode = $LASTEXITCODE

$problems = @(Get-DocFxProblem -LogPath $LogPath -Allowed @(Get-DocFxAllowance))
$summary = Format-DocFxSummary -Command $Command -Problem $problems

$summary | Write-Output
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($summary + '')
}

$failing = @($problems | Where-Object { -not $_.Allowed })
if ($failing.Count -gt 0) {
    Write-Output "DocFX $Command failed: $($failing.Count) warning(s) or error(s) that the build does not accept."
    exit 1
}

if ($exitCode -ne 0) {
    Write-Output "DocFX $Command exited with code $exitCode."
    exit 1
}

Write-Output "DocFX $Command passed."
