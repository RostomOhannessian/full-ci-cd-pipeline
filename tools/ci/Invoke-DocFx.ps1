#Requires -Version 7.0
<#
.SYNOPSIS
    Runs a DocFX command and fails on any warning or error, with one named exception for the metadata step.
.DESCRIPTION
    DocFX reads docfx.json at the repository root. The build step runs with --warningsAsErrors. The metadata step runs without it,
    because DocFX warns that it found no .NET API until WP1.6 adds public types, so the log is checked here instead, and only the
    warnings that DocFX.psm1 allows pass. Run it from the repository root after "dotnet tool restore" and a restore of the solution:
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

$arguments = @('docfx', $Command, $Configuration, '--log', $LogPath)
if ($Command -eq 'build') { $arguments += '--warningsAsErrors' }

& dotnet @arguments
$exitCode = $LASTEXITCODE

$problems = @(Get-DocFxProblem -LogPath $LogPath -Allowed (Get-DocFxAllowance))
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
