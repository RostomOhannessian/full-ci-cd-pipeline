Set-StrictMode -Version Latest

# `dev doctor`: checks that this machine can run the project, and says what to do about each problem. A failure means the stack will not
# work, a warning means it may not, and nothing here changes the machine (plan section 12, ADR-0019).

Import-Module (Join-Path $PSScriptRoot 'Dev.Common.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Env.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dev.Tools.psm1')

# Docker's budget for each resource profile. The numbers are indicative, as plan section 12 says, until WP3.0 measures them. A machine below
# WarnMemoryGiB gets a warning, and one below FailMemoryGiB cannot run the profile at all (SQL Server refuses to start under 2 GB).
$script:Profiles = [ordered]@{
    api  = [pscustomobject]@{ Name = 'api'; Runtime = 'Compose'; WarnMemoryGiB = 4; FailMemoryGiB = 3; MinCpu = 2; Available = $true }
    lite = [pscustomobject]@{ Name = 'lite'; Runtime = 'kind (single node)'; WarnMemoryGiB = 10; FailMemoryGiB = 8; MinCpu = 4; Available = $false }
    full = [pscustomobject]@{ Name = 'full'; Runtime = 'kind (three nodes)'; WarnMemoryGiB = 16; FailMemoryGiB = 14; MinCpu = 8; Available = $false }
    ci   = [pscustomobject]@{ Name = 'ci'; Runtime = 'kind on a hosted runner'; WarnMemoryGiB = 16; FailMemoryGiB = 12; MinCpu = 4; Available = $false }
}

function Get-DevProfile {
    <#
    .SYNOPSIS
        Returns the resource profile with the given name, or throws when there is none.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Name)

    if (-not $script:Profiles.Contains($Name)) { throw "Unknown profile '$Name'. The profiles are: $($script:Profiles.Keys -join ', ')." }
    return $script:Profiles[$Name]
}

function Get-DevComposePort {
    <#
    .SYNOPSIS
        Returns the host ports that the api profile binds, from .env when it exists and from .env.example otherwise.
    #>
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $template = Read-DevEnvFile -Path (Join-Path $RepositoryRoot '.env.example')
    $actual = Read-DevEnvFile -Path (Join-Path $RepositoryRoot '.env')
    $ports = [ordered]@{
        'SQL Server'        = 'SQL_PORT'
        'Valkey'            = 'VALKEY_PORT'
        'Keycloak'          = 'KEYCLOAK_PORT'
        'Grafana'           = 'GRAFANA_PORT'
        'OTLP gRPC'         = 'OTLP_GRPC_PORT'
        'OTLP HTTP'         = 'OTLP_HTTP_PORT'
    }

    foreach ($label in $ports.Keys) {
        $name = $ports[$label]
        $value = if ($actual.Contains($name) -and $actual[$name]) { $actual[$name] } elseif ($template.Contains($name)) { $template[$name] } else { $null }
        if ($value -match '^\d+$') { [pscustomobject]@{ Name = $label; Variable = $name; Port = [int]$value } }
    }
}

function Test-DevSdkVersion {
    <#
    .SYNOPSIS
        Says whether an installed SDK satisfies the global.json pin under the rollForward policy that the repository uses.
    .DESCRIPTION
        The repository pins an SDK and allows "latestPatch", which accepts the same major, minor, and feature band with an equal or higher patch.
        For 10.0.401 that means 10.0.401 to 10.0.499.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [string] $Installed,
        [Parameter(Mandatory)] [string] $Pinned,
        [string] $RollForward = 'latestPatch'
    )

    $parse = {
        param($text)
        $match = [regex]::Match($text, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)')
        if (-not $match.Success) { return $null }
        [pscustomobject]@{ Major = [int]$match.Groups['major'].Value; Minor = [int]$match.Groups['minor'].Value; Patch = [int]$match.Groups['patch'].Value }
    }

    $have = & $parse $Installed
    $want = & $parse $Pinned
    if ($null -eq $have -or $null -eq $want) { return $false }
    if ($have.Major -ne $want.Major -or $have.Minor -ne $want.Minor) { return $false }

    if ($RollForward -eq 'disable') { return $have.Patch -eq $want.Patch }
    return [math]::Floor($have.Patch / 100) -eq [math]::Floor($want.Patch / 100) -and $have.Patch -ge $want.Patch
}

function Test-DevHostSupport {
    [CmdletBinding()]
    param()

    $machine = Get-DevHost
    $label = "$($machine.Family) $($machine.Architecture)"

    if ($machine.Architecture -in 'arm64', 'arm') {
        return Build-DevCheck -Name 'Host' -Status warn -Message "$label. SQL Server container images are supported only on x86-64, so the API-only path is best effort here." `
            -Fix 'Use a GitHub Codespace or an x86-64 host (docs/reference/host-support.md).'
    }

    return Build-DevCheck -Name 'Host' -Status ok -Message "$label is a supported host for the API-only path."
}

function Test-DevGitIdentity {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $git = Invoke-DevNative -FilePath 'git' -ArgumentList @('--version')
    if (-not $git.Found -or $git.ExitCode -ne 0) {
        return @(Build-DevCheck -Name 'Git' -Status fail -Message 'git is not installed.' -Fix 'Install Git (https://git-scm.com/downloads).')
    }

    $gitText = ($git.Output -join ' ').Trim()
    $results = @(Build-DevCheck -Name 'Git' -Status ok -Message ($gitText ? $gitText : 'git is installed.'))
    $email = Invoke-DevNative -FilePath 'git' -ArgumentList @('config', '--local', 'user.email') -WorkingDirectory $RepositoryRoot
    $value = if ($email.ExitCode -eq 0) { ($email.Output -join '').Trim() } else { '' }

    if ($value -match '^\d+\+[A-Za-z0-9-]+@users\.noreply\.github\.com$') {
        $results += Build-DevCheck -Name 'Git identity' -Status ok -Message 'The repository commit email is a GitHub noreply address.'
    }
    else {
        $results += Build-DevCheck -Name 'Git identity' -Status warn -Message 'The repository commit email is not set to a GitHub noreply address (ADR-0006).' `
            -Fix 'Run: git config --local user.email <id>+<login>@users.noreply.github.com'
    }

    return $results
}

function Test-DevDotnet {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $globalJson = Join-Path $RepositoryRoot 'global.json'
    $pinned = $null
    $rollForward = 'latestPatch'
    if (Test-Path -Path $globalJson -PathType Leaf) {
        $sdk = (Get-Content -Path $globalJson -Raw | ConvertFrom-Json).sdk
        $pinned = $sdk.version
        if ($sdk.PSObject.Properties['rollForward']) { $rollForward = $sdk.rollForward }
    }

    $dotnet = Invoke-DevNative -FilePath 'dotnet' -ArgumentList @('--version') -WorkingDirectory $RepositoryRoot
    if (-not $dotnet.Found) {
        return Build-DevCheck -Name '.NET SDK' -Status fail -Message 'dotnet is not installed.' -Fix "Install the .NET SDK $pinned (https://dotnet.microsoft.com/download/dotnet/10.0)."
    }

    $installed = ($dotnet.Output | Select-Object -Last 1)
    if ($dotnet.ExitCode -ne 0) {
        return Build-DevCheck -Name '.NET SDK' -Status fail -Message "dotnet cannot find the SDK that global.json pins ($pinned)." -Fix "Install the .NET SDK $pinned (https://dotnet.microsoft.com/download/dotnet/10.0)."
    }

    if ($pinned -and -not (Test-DevSdkVersion -Installed $installed -Pinned $pinned -RollForward $rollForward)) {
        return Build-DevCheck -Name '.NET SDK' -Status fail -Message "SDK $installed does not satisfy the pin $pinned." -Fix "Install the .NET SDK $pinned."
    }

    return Build-DevCheck -Name '.NET SDK' -Status ok -Message "SDK $installed satisfies the pin $pinned."
}

function Test-DevPowerShell {
    [CmdletBinding()]
    param()

    $version = $PSVersionTable.PSVersion
    if ($version -lt [version]'7.4') {
        return Build-DevCheck -Name 'PowerShell' -Status fail -Message "PowerShell $version is older than 7.4." -Fix 'Run dev through the local tool: dotnet tool restore, then ./dev doctor.'
    }

    return Build-DevCheck -Name 'PowerShell' -Status ok -Message "PowerShell $version."
}

function Test-DevScriptTool {
    [CmdletBinding()]
    param(
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    try {
        $tools = @(Read-DevToolManifest -Path (Join-Path $RepositoryRoot 'tools' 'versions.yaml'))
    }
    catch {
        return Build-DevCheck -Name 'Script tools' -Status fail -Message "tools/versions.yaml cannot be read: $($_.Exception.Message)"
    }

    $status = @(Get-DevToolStatus -Tool $tools -Root (Get-DevToolsRoot -RepositoryRoot $RepositoryRoot))
    $missing = @($status | Where-Object { -not $_.Installed })
    if ($missing.Count -gt 0) {
        return Build-DevCheck -Name 'Script tools' -Status warn -Message "Not installed as pinned: $(($missing | ForEach-Object { "$($_.Name) $($_.Version)" }) -join ', ')." -Fix 'Run: ./dev bootstrap'
    }

    return Build-DevCheck -Name 'Script tools' -Status ok -Message "$(($status | ForEach-Object { "$($_.Name) $($_.Version)" }) -join ', ') installed as pinned."
}

function Test-DevDocker {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [pscustomobject] $ResourceProfile)

    $results = [System.Collections.Generic.List[object]]::new()
    $version = Invoke-DevNative -FilePath 'docker' -ArgumentList @('version', '--format', '{{.Server.Version}}')
    if (-not $version.Found) {
        $results.Add((Build-DevCheck -Name 'Docker' -Status fail -Message 'docker is not installed.' -Fix 'Install Docker Desktop (Windows, macOS) or Docker Engine (Linux), or use the Dev Container.'))
        return $results
    }
    if ($version.ExitCode -ne 0) {
        $results.Add((Build-DevCheck -Name 'Docker' -Status fail -Message 'The Docker daemon does not answer.' -Fix 'Start Docker Desktop, or start the docker service.'))
        return $results
    }

    $info = Invoke-DevNative -FilePath 'docker' -ArgumentList @('info', '--format', '{{.OSType}} {{.NCPU}} {{.MemTotal}}')
    $parts = ($info.Output -join ' ').Trim() -split '\s+'
    if ($info.ExitCode -ne 0 -or $parts.Count -lt 3 -or $parts[1] -notmatch '^\d+$' -or $parts[2] -notmatch '^\d+$') {
        $results.Add((Build-DevCheck -Name 'Docker' -Status warn -Message "Docker $(($version.Output -join '').Trim()) answers, but its resources could not be read."))
        return $results
    }

    $osType = $parts[0]
    $cpus = [int]$parts[1]
    $memoryGiB = [math]::Round([double]$parts[2] / 1GB, 1)

    if ($osType -ne 'linux') {
        $results.Add((Build-DevCheck -Name 'Docker' -Status fail -Message "Docker runs $osType containers, and the stack needs Linux containers." -Fix 'Switch Docker Desktop to Linux containers.'))
        return $results
    }
    $results.Add((Build-DevCheck -Name 'Docker' -Status ok -Message "Docker $(($version.Output -join '').Trim()), Linux containers."))

    $compose = Invoke-DevNative -FilePath 'docker' -ArgumentList @('compose', 'version', '--short')
    $composeVersion = ($compose.Output -join '').Trim().TrimStart('v')
    if ($compose.ExitCode -ne 0 -or $composeVersion -notmatch '^\d+\.\d+') {
        $results.Add((Build-DevCheck -Name 'Docker Compose' -Status fail -Message 'The Docker Compose plugin is not available.' -Fix 'Install Docker Compose v2.23 or newer.'))
    }
    elseif ([version]($composeVersion -replace '^(\d+\.\d+(\.\d+)?).*', '$1') -lt [version]'2.23') {
        $results.Add((Build-DevCheck -Name 'Docker Compose' -Status fail -Message "Compose $composeVersion is older than 2.23, which compose.yaml needs for inline configs." -Fix 'Update Docker Desktop or the Compose plugin.'))
    }
    else {
        $results.Add((Build-DevCheck -Name 'Docker Compose' -Status ok -Message "Compose $composeVersion."))
    }

    $budget = "the $($ResourceProfile.Name) profile ($($ResourceProfile.Runtime)) asks for about $($ResourceProfile.WarnMemoryGiB) GB and $($ResourceProfile.MinCpu) CPUs"
    if ($memoryGiB -lt $ResourceProfile.FailMemoryGiB) {
        $results.Add((Build-DevCheck -Name 'Docker memory' -Status fail -Message "$memoryGiB GB is not enough, because $budget." -Fix 'Raise the memory in Docker Desktop settings (Resources).'))
    }
    elseif ($memoryGiB -lt $ResourceProfile.WarnMemoryGiB) {
        $results.Add((Build-DevCheck -Name 'Docker memory' -Status warn -Message "$memoryGiB GB is below the budget, because $budget." -Fix 'Raise the memory in Docker Desktop settings (Resources).'))
    }
    else {
        $results.Add((Build-DevCheck -Name 'Docker memory' -Status ok -Message "$memoryGiB GB meets the budget: $budget."))
    }

    if ($cpus -lt $ResourceProfile.MinCpu) {
        $results.Add((Build-DevCheck -Name 'Docker CPUs' -Status warn -Message "$cpus CPUs is below the budget of $($ResourceProfile.MinCpu)." -Fix 'Raise the CPU limit in Docker Desktop settings (Resources).'))
    }
    else {
        $results.Add((Build-DevCheck -Name 'Docker CPUs' -Status ok -Message "$cpus CPUs meets the budget of $($ResourceProfile.MinCpu)."))
    }

    return $results
}

function Test-DevPort {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $ports = @(Get-DevComposePort -RepositoryRoot $RepositoryRoot)
    $running = Invoke-DevNative -FilePath 'docker' -ArgumentList @('compose', '--project-directory', $RepositoryRoot, 'ps', '--quiet') -WorkingDirectory $RepositoryRoot
    if ($running.ExitCode -eq 0 -and @($running.Output | Where-Object { $_ }).Count -gt 0) {
        return Build-DevCheck -Name 'Ports' -Status ok -Message 'The stack is running, so it holds its own ports.'
    }

    $taken = @($ports | Where-Object { -not (Test-DevPortFree -Port $_.Port) })
    if ($taken.Count -gt 0) {
        $names = ($taken | ForEach-Object { "$($_.Name) ($($_.Port))" }) -join ', '
        return Build-DevCheck -Name 'Ports' -Status fail -Message "Already in use on 127.0.0.1: $names." `
            -Fix "Stop the program that uses them, or change $((($taken | ForEach-Object Variable) -join ', ')) in .env."
    }

    return Build-DevCheck -Name 'Ports' -Status ok -Message "$(($ports | ForEach-Object Port) -join ', ') are free on 127.0.0.1."
}

function Test-DevEnvironmentFile {
    [CmdletBinding()]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $state = Test-DevEnvironment -TemplatePath (Join-Path $RepositoryRoot '.env.example') -Path (Join-Path $RepositoryRoot '.env')
    if (-not $state.Exists) {
        return Build-DevCheck -Name 'Environment file' -Status warn -Message '.env does not exist, so the stack has no credentials yet.' -Fix 'Run: ./dev bootstrap'
    }
    if ($state.Missing.Count -gt 0) {
        return Build-DevCheck -Name 'Environment file' -Status warn -Message ".env lacks: $($state.Missing -join ', ')." -Fix 'Run: ./dev bootstrap'
    }

    return Build-DevCheck -Name 'Environment file' -Status ok -Message '.env has every variable that .env.example lists.'
}

function Invoke-DevDoctor {
    <#
    .SYNOPSIS
        Runs every check for a resource profile and returns the results.
    #>
    [CmdletBinding()]
    param(
        [string] $ProfileName = 'api',
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    $resourceProfile = Get-DevProfile -Name $ProfileName
    $checks = [System.Collections.Generic.List[object]]::new()

    $checks.Add((Test-DevHostSupport))
    $checks.AddRange([object[]](Test-DevGitIdentity -RepositoryRoot $RepositoryRoot))
    $checks.Add((Test-DevDotnet -RepositoryRoot $RepositoryRoot))
    $checks.Add((Test-DevPowerShell))
    $checks.Add((Test-DevScriptTool -RepositoryRoot $RepositoryRoot))
    $checks.AddRange([object[]](Test-DevDocker -ResourceProfile $resourceProfile))

    if ($resourceProfile.Available) {
        $checks.Add((Test-DevPort -RepositoryRoot $RepositoryRoot))
        $checks.Add((Test-DevEnvironmentFile -RepositoryRoot $RepositoryRoot))
    }
    else {
        $checks.Add((Build-DevCheck -Name 'Profile' -Status warn -Message "The $($resourceProfile.Name) profile runs on $($resourceProfile.Runtime), which arrives in Phase 3. Only its Docker budget was checked."))
    }

    return $checks.ToArray()
}

Export-ModuleMember -Function Get-DevProfile, Get-DevComposePort, Test-DevSdkVersion, Test-DevHostSupport, Test-DevGitIdentity, Test-DevDotnet, Test-DevPowerShell, Test-DevScriptTool, Test-DevDocker, Test-DevPort, Test-DevEnvironmentFile, Invoke-DevDoctor
