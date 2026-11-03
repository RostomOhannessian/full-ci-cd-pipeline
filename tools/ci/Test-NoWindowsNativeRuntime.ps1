#Requires -Version 7.0
<#
.SYNOPSIS
    Fails when a folder of published output holds the native Windows runtime of Microsoft.Data.SqlClient.SNI.runtime.
.DESCRIPTION
    ADR-0004 (amendment of 2026-11-03) allows that package, because no shipped artifact contains it: the images are Linux-only, and a publish for
    Linux does not hold its Windows assets. Its license terms allow distribution only with protective terms and an indemnity, so this check is what
    keeps that promise. Run it on the output of a publish for Linux, or on the file system of an image, and it exits with 1 when any file of the
    native runtime is there. A publish with no runtime identifier copies runtimes/win-x64, win-x86, and win-arm64, so publish with one, for example
    dotnet publish -r linux-x64.

    The license exception work, a prerequisite of WP1.8, delivers the check and the tests that show it can fail. The image build of WP2.1 runs it on
    the real publish output (planned).

    Exit codes: 0 when no folder holds the assets, 1 when one does, and 2 when a folder does not exist.
.EXAMPLE
    pwsh -NoProfile -File tools/ci/Test-NoWindowsNativeRuntime.ps1 -Path artifacts/publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsNativeRuntime.psm1') -Force

foreach ($folder in $Path) {
    if (-not (Test-Path -Path $folder -PathType Container)) {
        Write-Error -Message "The folder '$folder' does not exist." -ErrorAction Continue
        exit 2
    }
}

$failed = $false
foreach ($folder in $Path) {
    $assets = @(Find-WindowsNativeRuntimeAsset -Path $folder)

    if ($assets.Count -gt 0) {
        $failed = $true
        Write-Output "The folder '$folder' holds the native Windows runtime of Microsoft.Data.SqlClient.SNI.runtime, which no shipped artifact may contain (ADR-0004):"
        $assets | ForEach-Object { Write-Output "  $_" }
    }
    else {
        Write-Output "The folder '$folder' holds no native Windows runtime of Microsoft.Data.SqlClient.SNI.runtime."
    }
}

if ($failed) { exit 1 }
exit 0
