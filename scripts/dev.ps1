#Requires -Version 7.4
<#
.SYNOPSIS
    The single entry point for local development: setup, build and test, docs, and the API-only stack.
.DESCRIPTION
    Run it through the shims, which start PowerShell as a local .NET tool so the only prerequisites are Git, Docker, and the .NET SDK:
    ./dev help          (Linux, macOS, the Dev Container, Git Bash)
    dev help            (Windows, through dev.cmd)
    Without the shims: dotnet tool restore, then dotnet pwsh -File scripts/dev.ps1 help
.PARAMETER Command
    The subcommand. "dev help" lists them, including the ones that a later work package delivers.
.PARAMETER Rest
    The words after the subcommand. Options are written --name or --name value.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)] [string] $Command = 'help',
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)] [string[]] $Rest = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'modules' 'Dev.Commands.psm1')
Import-Module (Join-Path $PSScriptRoot 'modules' 'Dev.Tools.psm1')

# The pinned tools win over any copy on the machine, so a test or a lint behaves the same everywhere.
Enable-DevToolPath

exit (Invoke-DevCommand -Name $Command -Rest $Rest)
