Set-StrictMode -Version Latest

# The command registry and the argument parser for dev.ps1. Every subcommand that the plan names is here, either available or marked as planned with
# the work package that delivers it, so `dev help` shows the whole surface and a planned command never fails with a bare "unknown command".

Import-Module (Join-Path $PSScriptRoot 'Dev.Common.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Tools.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Env.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Doctor.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Compose.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Project.psm1')

function ConvertFrom-DevArgument {
    <#
    .SYNOPSIS
        Splits the words after a subcommand into options and positional values, and rejects an option that the subcommand does not know.
    .DESCRIPTION
        An option is written --name for a switch, and --name value or --name=value for a value. A word that does not start with two hyphens is
        positional. A problem is returned in Error, and the caller prints it with the usage line.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [string[]] $Words = @(),
        [string[]] $Switch = @(),
        [string[]] $Value = @()
    )

    $options = @{}
    $positional = [System.Collections.Generic.List[string]]::new()
    $problem = $null

    for ($i = 0; $i -lt $Words.Count; $i++) {
        $word = $Words[$i]
        if (-not $word.StartsWith('--', [StringComparison]::Ordinal)) { $positional.Add($word); continue }

        $name, $inline = $word.Substring(2) -split '=', 2
        if ($name -in $Switch) {
            if ($null -ne $inline) { $problem = "The option --$name does not take a value."; break }
            $options[$name] = $true
        }
        elseif ($name -in $Value) {
            if ($null -ne $inline) { $options[$name] = $inline }
            elseif ($i + 1 -lt $Words.Count) { $i++; $options[$name] = $Words[$i] }
            else { $problem = "The option --$name needs a value."; break }
        }
        else {
            $problem = "Unknown option --$name."
            break
        }
    }

    return [pscustomobject]@{ Options = $options; Positional = $positional.ToArray(); Error = $problem }
}

function Get-DevCommandTable {
    <#
    .SYNOPSIS
        Returns the commands in the order that `dev help` prints them.
    #>
    [CmdletBinding()]
    param()

    $planned = {
        param($group, $summary, $arrives)
        [pscustomobject]@{ Group = $group; Summary = $summary; Usage = $null; Available = $false; Arrives = $arrives }
    }

    return [ordered]@{
        doctor    = [pscustomobject]@{ Group = 'Setup'; Summary = 'Check this machine against the profile budget, tools, ports, and credentials.'; Usage = 'dev doctor [--profile api|lite|full|ci]'; Available = $true; Arrives = $null }
        bootstrap = [pscustomobject]@{ Group = 'Setup'; Summary = 'Install the pinned script tools and create .env with random local secrets.'; Usage = 'dev bootstrap [--skip-tools] [--skip-env] [--force]'; Available = $true; Arrives = $null }
        build     = [pscustomobject]@{ Group = 'Build and test'; Summary = 'Build the solution in Release.'; Usage = 'dev build'; Available = $true; Arrives = $null }
        test      = [pscustomobject]@{ Group = 'Build and test'; Summary = 'Run a test suite: unit, architecture, scripts, docs, or all.'; Usage = 'dev test [all|unit|architecture|scripts|docs]'; Available = $true; Arrives = $null }
        docs      = [pscustomobject]@{ Group = 'Build and test'; Summary = 'Build the documentation site with warnings as errors, or serve it.'; Usage = 'dev docs [serve]'; Available = $true; Arrives = $null }
        compose   = [pscustomobject]@{ Group = 'Compose'; Summary = 'Run the API-only stack: SQL Server, Valkey, Keycloak, and the telemetry stack.'; Usage = 'dev compose up|down|reset|ps|logs|check'; Available = $true; Arrives = $null }
        lab       = [pscustomobject]@{ Group = 'Data and demos'; Summary = 'Set up, run, or clean up a lab. Lab 1 is the quick start.'; Usage = 'dev lab <id> setup|run|cleanup'; Available = $true; Arrives = $null }
        platform  = & $planned 'Platform' 'Create, destroy, and inspect the local kind platform: create, destroy, status, unseal, credentials.' 'Phase 3 (WP3.1 to WP3.8)'
        tf        = & $planned 'Terraform' 'Plan and apply a Terraform stack.' 'Phase 3 (WP3.1)'
        seed      = & $planned 'Data and demos' 'Create catalog data through the API.' 'the work package that delivers the API (WP1.10 and later)'
        demo      = & $planned 'Data and demos' 'Run a demonstration scenario.' 'the work package that delivers the scenario'
        clean     = & $planned 'Housekeeping' 'Remove build output and local state.' 'the work package that needs it'
    }
}

function Write-DevHelp {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'The dev command is an interactive tool, and its help text is its output.')]
    [CmdletBinding()]
    param([string] $Name)

    $table = Get-DevCommandTable
    if ($Name -and $table.Contains($Name)) {
        $entry = $table[$Name]
        Write-Host "dev $Name"
        Write-Host "  $($entry.Summary)"
        if ($entry.Available) { Write-Host "  Usage: $($entry.Usage)" } else { Write-Host "  Planned: arrives with $($entry.Arrives)." }
        return
    }

    Write-Host 'dev: the single entry point for local development of the product catalog (plan section 12).'
    Write-Host ''
    Write-Host 'Usage: dev <command> [options]. Run "dev help <command>" for one command.'
    foreach ($group in ($table.Values | Select-Object -ExpandProperty Group -Unique)) {
        Write-Host ''
        Write-Host $group
        foreach ($key in $table.Keys) {
            $entry = $table[$key]
            if ($entry.Group -ne $group) { continue }
            $note = if ($entry.Available) { '' } else { " (planned: $($entry.Arrives))" }
            Write-Host ('  {0,-10} {1}{2}' -f $key, $entry.Summary, $note)
        }
    }
    Write-Host ''
    Write-Host 'Exit codes: 0 success, 1 a check or step failed, 2 wrong usage or a command that is not available yet.'
}

function Write-DevUsageError {
    param([string] $Message, [string] $Usage)

    Write-DevMessage -Level fail -Message $Message
    if ($Usage) { Write-DevMessage -Level info -Message "usage: $Usage" }
    return 2
}

function Invoke-DevBootstrap {
    [CmdletBinding()]
    [OutputType([int])]
    param([switch] $SkipTools, [switch] $SkipEnvironment, [switch] $Force, [string] $RepositoryRoot = (Get-DevRepositoryRoot))

    if (-not $SkipTools) {
        Write-DevMessage -Level info -Message 'Installing the pinned script tools from tools/versions.yaml (SHA-256 verified).'
        try {
            foreach ($result in (Install-DevToolSet -ManifestPath (Join-Path $RepositoryRoot 'tools' 'versions.yaml') -Root (Get-DevToolsRoot -RepositoryRoot $RepositoryRoot) -Force:$Force)) {
                Write-DevMessage -Level ok -Message "$($result.Id) $($result.Version): $($result.Action)"
            }
        }
        catch {
            Write-DevMessage -Level fail -Message $_.Exception.Message
            return 1
        }
    }

    if (-not $SkipEnvironment) {
        try {
            $result = Initialize-DevEnvironment -TemplatePath (Join-Path $RepositoryRoot '.env.example') -Path (Join-Path $RepositoryRoot '.env') -Force:$Force
        }
        catch {
            Write-DevMessage -Level fail -Message $_.Exception.Message
            return 1
        }

        if ($result.Created) { Write-DevMessage -Level ok -Message ".env created with $($result.Generated.Count) generated secret(s). It stays on this machine and Git ignores it." }
        elseif ($result.Changed) { Write-DevMessage -Level ok -Message ".env updated: $(($result.Generated + $result.Defaulted) -join ', ')." }
        else { Write-DevMessage -Level ok -Message '.env is already complete, and nothing was changed.' }

        if ($Force -and $result.Generated.Count -gt 0) {
            Write-DevMessage -Level warn -Message 'Secrets were regenerated. A database keeps the password it was first created with, so run: ./dev compose reset'
        }
    }

    Write-DevMessage -Level info -Message 'Next: ./dev doctor'
    return 0
}

function Invoke-DevLab {
    [CmdletBinding()]
    [OutputType([int])]
    param([string] $Id, [string] $Action, [string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $usage = 'dev lab <id> setup|run|cleanup'
    if ($Id -notmatch '^\d+$' -or $Action -notin 'setup', 'run', 'cleanup') { return Write-DevUsageError -Message 'A lab needs its number and one action.' -Usage $usage }

    $script = Join-Path $RepositoryRoot 'docs' 'labs' 'scripts' ('lab-{0:D2}.ps1' -f [int]$Id)
    if (-not (Test-Path -Path $script -PathType Leaf)) {
        Write-DevMessage -Level fail -Message "Lab $Id has no script yet. Lab 1 (the quick start) is the only one that exists, and the others arrive with WP1.13."
        return 2
    }

    $pwsh = Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')
    return Invoke-DevNativeLive -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', $script, '-Action', $Action) -WorkingDirectory $RepositoryRoot
}

function Invoke-DevCommand {
    <#
    .SYNOPSIS
        Runs one dev subcommand with the words that follow it, and returns the exit code.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [string] $Name = 'help',
        [AllowEmptyCollection()] [string[]] $Rest = @(),
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    $table = Get-DevCommandTable
    $Name = $Name.ToLowerInvariant()

    if ($Name -in 'help', '--help', '-h', '/?') {
        Write-DevHelp -Name ($Rest | Select-Object -First 1)
        return 0
    }

    if (-not $table.Contains($Name)) {
        Write-DevMessage -Level fail -Message "Unknown command '$Name'."
        Write-DevHelp
        return 2
    }

    $entry = $table[$Name]
    if (-not $entry.Available) {
        Write-DevMessage -Level warn -Message "'dev $Name' is planned and does not exist yet. It arrives with $($entry.Arrives)."
        return 2
    }

    switch ($Name) {
        'doctor' {
            $parsed = ConvertFrom-DevArgument -Words $Rest -Value @('profile')
            if ($parsed.Error -or $parsed.Positional.Count -gt 0) { return Write-DevUsageError -Message ($parsed.Error ?? 'doctor takes no positional values.') -Usage $entry.Usage }
            $profileName = if ($parsed.Options.ContainsKey('profile')) { $parsed.Options['profile'] } else { 'api' }
            try { $checks = Invoke-DevDoctor -ProfileName $profileName -RepositoryRoot $RepositoryRoot }
            catch { return Write-DevUsageError -Message $_.Exception.Message -Usage $entry.Usage }
            return Write-DevCheck -Check $checks -Title 'doctor'
        }
        'bootstrap' {
            $parsed = ConvertFrom-DevArgument -Words $Rest -Switch @('skip-tools', 'skip-env', 'force')
            if ($parsed.Error -or $parsed.Positional.Count -gt 0) { return Write-DevUsageError -Message ($parsed.Error ?? 'bootstrap takes no positional values.') -Usage $entry.Usage }
            return Invoke-DevBootstrap -SkipTools:($parsed.Options.ContainsKey('skip-tools')) -SkipEnvironment:($parsed.Options.ContainsKey('skip-env')) -Force:($parsed.Options.ContainsKey('force')) -RepositoryRoot $RepositoryRoot
        }
        'build' {
            if ($Rest.Count -gt 0) { return Write-DevUsageError -Message 'build takes no arguments.' -Usage $entry.Usage }
            return Invoke-DevBuild -RepositoryRoot $RepositoryRoot
        }
        'test' {
            $parsed = ConvertFrom-DevArgument -Words $Rest
            $suite = if ($parsed.Positional.Count -gt 0) { $parsed.Positional[0] } else { 'all' }
            if ($parsed.Error -or $parsed.Positional.Count -gt 1 -or $suite -notin 'all', 'unit', 'architecture', 'scripts', 'docs') {
                return Write-DevUsageError -Message ($parsed.Error ?? "Unknown suite '$suite'.") -Usage $entry.Usage
            }
            return Invoke-DevTest -Suite $suite -RepositoryRoot $RepositoryRoot
        }
        'docs' {
            $parsed = ConvertFrom-DevArgument -Words $Rest
            if ($parsed.Error -or $parsed.Positional.Count -gt 1 -or ($parsed.Positional.Count -eq 1 -and $parsed.Positional[0] -ne 'serve')) {
                return Write-DevUsageError -Message ($parsed.Error ?? 'docs takes only the optional word serve.') -Usage $entry.Usage
            }
            return Invoke-DevDocumentation -Serve:($parsed.Positional.Count -eq 1) -RepositoryRoot $RepositoryRoot
        }
        'compose' {
            $action = $Rest | Select-Object -First 1
            $others = @($Rest | Select-Object -Skip 1)
            if ($action -eq 'check') {
                if ($others.Count -gt 0) { return Write-DevUsageError -Message 'compose check takes no arguments.' -Usage $entry.Usage }
                return Write-DevCheck -Check (Invoke-DevComposeCheck -RepositoryRoot $RepositoryRoot) -Title 'compose check'
            }
            if ($action -notin 'up', 'down', 'reset', 'ps', 'logs') { return Write-DevUsageError -Message "compose needs one action, and '$action' is not one." -Usage $entry.Usage }
            return Invoke-DevCompose -Action $action -Rest $others -RepositoryRoot $RepositoryRoot
        }
        'lab' {
            return Invoke-DevLab -Id ($Rest | Select-Object -First 1) -Action ($Rest | Select-Object -Skip 1 | Select-Object -First 1) -RepositoryRoot $RepositoryRoot
        }
    }
}

Export-ModuleMember -Function ConvertFrom-DevArgument, Get-DevCommandTable, Write-DevHelp, Invoke-DevBootstrap, Invoke-DevLab, Invoke-DevCommand
