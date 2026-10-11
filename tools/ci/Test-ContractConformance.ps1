#Requires -Version 7.0
<#
.SYNOPSIS
    Fails when a served OpenAPI document is not structurally equal to the authored contract.
.DESCRIPTION
    The authored contract is the source of truth (ADR-0017, REQ-CON-001). The API build generates its own OpenAPI document, and this script
    compares the two with oasdiff, ignoring the elements that never change behavior: descriptions, examples, and summaries (plan section 8.6).
    Any other difference fails, such as a changed constraint, a missing response, a renamed header, or a new endpoint.

    WP1.5 delivers the comparison and the tests that show it can fail. WP1.10 generates the served document at build time and runs it here.
    oasdiff runs in the digest-pinned container of the contract-conformance service in tools/lint/compose.yaml, so there is nothing to install.
    The files are copied to a temporary folder first, so they can be anywhere on disk.

    Exit codes: 0 when the documents are structurally equal, 1 when they differ, and 2 when the comparison could not run.
.EXAMPLE
    pwsh -NoProfile -File tools/ci/Test-ContractConformance.ps1 -Served artifacts/catalog.openapi.json
#>
[CmdletBinding()]
param(
    [string] $Authored = (Join-Path $PSScriptRoot '..' '..' 'contracts' 'openapi' 'catalog' 'v1' 'catalog.openapi.yaml'),
    [Parameter(Mandatory)] [string] $Served,
    [ValidateSet('description', 'examples', 'extensions', 'summary', 'title')]
    [string[]] $IgnoredElements = @('description', 'examples', 'summary'),
    [string] $ComposeFile = (Join-Path $PSScriptRoot '..' 'lint' 'compose.yaml')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Exit-ContractConformance {
    param([string] $Message)

    Write-Error -Message $Message -ErrorAction Continue
    exit 2
}

foreach ($file in $Authored, $Served) {
    if (-not (Test-Path -Path $file -PathType Leaf)) { Exit-ContractConformance "The file '$file' does not exist." }
    if ([IO.Path]::GetExtension($file) -notin '.yaml', '.yml', '.json') { Exit-ContractConformance "The file '$file' must be a .yaml, .yml, or .json file." }
}

$docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $docker) { Exit-ContractConformance 'Docker is not installed, and oasdiff runs in a container.' }

$work = Join-Path ([IO.Path]::GetTempPath()) "conformance-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $authoredName = "authored$([IO.Path]::GetExtension($Authored))"
    $servedName = "served$([IO.Path]::GetExtension($Served))"
    Copy-Item -Path $Authored -Destination (Join-Path $work $authoredName)
    Copy-Item -Path $Served -Destination (Join-Path $work $servedName)

    $arguments = @(
        'compose', '-f', (Resolve-Path -Path $ComposeFile).ProviderPath, 'run', '--rm', '-T', '-v', "$($work):/contracts:ro", 'contract-conformance',
        'diff', "/contracts/$authoredName", "/contracts/$servedName",
        '--exclude-elements', ($IgnoredElements -join ','), '--fail-on-diff', '--format', 'text')

    $output = @(& $docker.Source @arguments 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -notmatch '^\s*(Container|Network|Volume)\s' })
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($exitCode -eq 0) {
    Write-Output "The served document is structurally equal to the authored contract (ignoring $($IgnoredElements -join ', '))."
    exit 0
}

$output | Write-Output
if ($exitCode -eq 1) {
    Write-Output 'The served document differs from the authored contract. Change the code to match the contract, or change the contract first (ADR-0017).'
    exit 1
}

Exit-ContractConformance "oasdiff could not compare the documents (exit code $exitCode)."
