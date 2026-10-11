Set-StrictMode -Version Latest

# `dev build`, `dev test`, and `dev docs`: thin, honest wrappers over the commands that CI runs (AGENTS.md, "Commands that run today"). They add no
# behavior of their own, so a result here means the same as a result in CI.

Import-Module (Join-Path $PSScriptRoot 'Dev.Common.psm1')

$script:ComposeLint = 'tools/lint/compose.yaml'
$script:DocsGate = @('markdownlint', 'links', 'spelling', 'diagrams', 'secrets-worktree')
# Test projects that need a container runtime or a running API. They arrive in later work packages and get their own suites.
$script:NotUnitPattern = '(IntegrationTests|ContractTests|PactTests|PactVerification|Image\.Tests)$'

function Get-DevTestProject {
    <#
    .SYNOPSIS
        Returns the .NET test projects of a suite, found by name so a new project joins its suite without a change here.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateSet('unit', 'architecture')] [string] $Suite,
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    $projects = @(Get-ChildItem -Path (Join-Path $RepositoryRoot 'tests') -Filter '*.csproj' -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })

    $selected = switch ($Suite) {
        'architecture' { $projects | Where-Object { $_.BaseName -eq 'Catalog.Architecture.Tests' } }
        'unit' { $projects | Where-Object { $_.BaseName -ne 'Catalog.Architecture.Tests' -and $_.BaseName -notmatch $script:NotUnitPattern } }
    }

    return @($selected | Sort-Object FullName | ForEach-Object { [System.IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName).Replace('\', '/') })
}

function Invoke-DevBuild {
    [CmdletBinding()]
    [OutputType([int])]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    return Invoke-DevNativeLive -FilePath 'dotnet' -ArgumentList @('build', 'ProductCatalog.slnx', '--configuration', 'Release') -WorkingDirectory $RepositoryRoot
}

function Invoke-DevDotnetTest {
    [CmdletBinding()]
    [OutputType([int])]
    param([Parameter(Mandatory)] [string[]] $Project, [string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $worst = 0
    foreach ($path in $Project) {
        Write-DevMessage -Level info -Message "dotnet test $path"
        $code = Invoke-DevNativeLive -FilePath 'dotnet' -ArgumentList @('test', '--project', $path, '--configuration', 'Release') -WorkingDirectory $RepositoryRoot
        if ($code -ne 0) { $worst = $code }
    }

    return $worst
}

function Invoke-DevScriptTest {
    <#
    .SYNOPSIS
        Runs PSScriptAnalyzer and Pester through scripts/Invoke-ScriptTests.ps1, in a new PowerShell process so the tests never share modules with the command.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $pwsh = Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')
    return Invoke-DevNativeLive -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $RepositoryRoot 'scripts' 'Invoke-ScriptTests.ps1')) -WorkingDirectory $RepositoryRoot
}

function Invoke-DevDocumentationGate {
    <#
    .SYNOPSIS
        Runs the documentation gate that CI runs: the lint containers and the documentation auditor.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param([string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $worst = 0
    foreach ($service in $script:DocsGate) {
        Write-DevMessage -Level info -Message "docker compose run $service"
        $code = Invoke-DevNativeLive -FilePath 'docker' -ArgumentList @('compose', '-f', $script:ComposeLint, 'run', '--rm', $service) -WorkingDirectory $RepositoryRoot
        if ($code -ne 0) { $worst = $code }
    }

    Write-DevMessage -Level info -Message 'documentation auditor'
    $code = Invoke-DevNativeLive -FilePath 'dotnet' -ArgumentList @('run', '--project', 'tools/Documentation.Auditor', '--', 'audit') -WorkingDirectory $RepositoryRoot
    if ($code -ne 0) { $worst = $code }

    return $worst
}

function Invoke-DevTest {
    <#
    .SYNOPSIS
        Runs one test suite, or all of them, and returns 0 only when every suite passed.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [ValidateSet('all', 'unit', 'architecture', 'scripts', 'docs')] [string] $Suite = 'all',
        [string] $RepositoryRoot = (Get-DevRepositoryRoot)
    )

    $suites = if ($Suite -eq 'all') { @('unit', 'architecture', 'scripts', 'docs') } else { @($Suite) }
    $worst = 0
    foreach ($name in $suites) {
        Write-DevMessage -Level info -Message "suite: $name"
        $code = switch ($name) {
            'unit' { Invoke-DevDotnetTest -Project @(Get-DevTestProject -Suite unit -RepositoryRoot $RepositoryRoot) -RepositoryRoot $RepositoryRoot }
            'architecture' { Invoke-DevDotnetTest -Project @(Get-DevTestProject -Suite architecture -RepositoryRoot $RepositoryRoot) -RepositoryRoot $RepositoryRoot }
            'scripts' { Invoke-DevScriptTest -RepositoryRoot $RepositoryRoot }
            'docs' { Invoke-DevDocumentationGate -RepositoryRoot $RepositoryRoot }
        }
        if ($code -ne 0) {
            Write-DevMessage -Level fail -Message "suite $name failed (exit code $code)"
            $worst = $code
        }
        else {
            Write-DevMessage -Level ok -Message "suite $name passed"
        }
    }

    return $worst
}

function Invoke-DevDocumentation {
    <#
    .SYNOPSIS
        Builds the documentation site with warnings as errors, or serves a built site.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param([switch] $Serve, [string] $RepositoryRoot = (Get-DevRepositoryRoot))

    $pwsh = Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')
    $restore = Invoke-DevNativeLive -FilePath 'dotnet' -ArgumentList @('tool', 'restore') -WorkingDirectory $RepositoryRoot
    if ($restore -ne 0) { return $restore }

    foreach ($command in 'metadata', 'build') {
        $code = Invoke-DevNativeLive -FilePath $pwsh -ArgumentList @('-NoProfile', '-File', (Join-Path $RepositoryRoot 'tools' 'ci' 'Invoke-DocFx.ps1'), '-Command', $command) -WorkingDirectory $RepositoryRoot
        if ($code -ne 0) { return $code }
    }

    if ($Serve) {
        return Invoke-DevNativeLive -FilePath 'dotnet' -ArgumentList @('docfx', 'serve', '_site') -WorkingDirectory $RepositoryRoot
    }

    return 0
}

Export-ModuleMember -Function Get-DevTestProject, Invoke-DevBuild, Invoke-DevDotnetTest, Invoke-DevScriptTest, Invoke-DevDocumentationGate, Invoke-DevTest, Invoke-DevDocumentation
