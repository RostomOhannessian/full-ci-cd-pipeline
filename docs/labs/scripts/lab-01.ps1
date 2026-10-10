#Requires -Version 7.4
<#
.SYNOPSIS
    Lab 1, the quick start: starts the API-only stack, proves what it promises, and cleans up.
.DESCRIPTION
    "dev lab 1 setup", "dev lab 1 run", and "dev lab 1 cleanup" run this script, and the Dev Container workflow runs the same three commands, so a
    fresh environment proves the lab. The lab page (docs/labs/quick-start-api-stack.md) includes the regions below, which the comments of the form
    "# <name>" and "# </name>" mark. The API itself arrives in WP1.10, so this lab covers the services that the API depends on.
.PARAMETER Action
    setup creates .env and starts the stack, run checks it and shows what it issues, and cleanup removes it with its data.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('setup', 'run', 'cleanup')] [string] $Action
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -Path (Join-Path $PSScriptRoot '..' '..' '..')).ProviderPath
Import-Module (Join-Path $root 'scripts' 'modules' 'Dev.Common.psm1')
Import-Module (Join-Path $root 'scripts' 'modules' 'Dev.Env.psm1')
Import-Module (Join-Path $root 'scripts' 'modules' 'Dev.Compose.psm1')

# The lab prints observations as text for a reader, so it writes to the host on purpose.
function Write-Observation {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'A lab prints what the learner should observe.')]
    param([string] $Text)
    Write-Host "  observe: $Text"
}

switch ($Action) {
    'setup' {
        # <setup>
        Write-DevMessage -Level info -Message 'Step 1: create .env with random local secrets, and start the stack.'
        Initialize-DevEnvironment | Out-Null
        $code = Invoke-DevCompose -Action up
        # </setup>
        exit $code
    }
    'run' {
        # <check>
        Write-DevMessage -Level info -Message 'Step 2: check that every service answers as the plan describes.'
        $checks = Invoke-DevComposeCheck
        $failed = Write-DevCheck -Check $checks -Title 'quick start'
        # </check>
        if ($failed -ne 0) { exit 1 }

        # <token>
        Write-DevMessage -Level info -Message 'Step 3: ask Keycloak for a token as the smoke-tests client, and read its claims.'
        $environment = Get-DevStackEnvironment
        $issuer = "http://localhost:$($environment['KEYCLOAK_PORT'])/realms/catalog"
        $token = Get-DevToken -Issuer $issuer -ClientId 'smoke-tests' -Secret $environment['CATALOG_SMOKE_TESTS_SECRET']
        if (-not $token.Ok) { Write-DevMessage -Level fail -Message "No token: $($token.Error)"; exit 1 }
        Write-Observation "iss = $($token.Claims.iss)"
        Write-Observation "aud = $($token.Claims.aud)"
        Write-Observation "scope = $($token.Claims.scope)"
        # </token>

        # <roles>
        Write-DevMessage -Level info -Message 'Step 4: see which roles each demo user receives when it asks for every scope.'
        $admin = [System.Net.NetworkCredential]::new($environment['KEYCLOAK_ADMIN_USER'], $environment['KEYCLOAK_ADMIN_PASSWORD'])
        foreach ($user in (Get-DevUserRole -KeycloakUrl "http://localhost:$($environment['KEYCLOAK_PORT'])" -AdminCredential $admin -UserName @('reader', 'editor', 'admin'))) {
            Write-Observation "$($user.User) gets the roles: $($user.Roles -join ', ')"
        }
        # </roles>
        exit 0
    }
    'cleanup' {
        # <cleanup>
        Write-DevMessage -Level info -Message 'Cleanup: stop the stack and delete its data.'
        $code = Invoke-DevCompose -Action reset
        # </cleanup>
        exit $code
    }
}
